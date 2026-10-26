//! The heater source, selected by Cargo feature.
//!
//! - `heater-replay`: plays back recorded Rinnai readings from `captures/`, compiled in.
//! - `heater-rs485`: the real heater on the RS-485 port. Polling over CTA-2045 isn't written
//!   yet, so every poll fails (the device reports `heaterLinkLost`); the receive-only packet
//!   monitor runs as before.

#[cfg(all(feature = "heater-replay", feature = "heater-rs485"))]
compile_error!("Enable only one heater feature: `heater-replay` or `heater-rs485`.");
#[cfg(not(any(feature = "heater-replay", feature = "heater-rs485")))]
compile_error!("Enable a heater feature: `heater-replay` or `heater-rs485`.");

#[cfg(feature = "heater-replay")]
pub use replay::open;
#[cfg(all(feature = "heater-rs485", not(feature = "heater-replay")))]
pub use rs485::open;

#[cfg(feature = "heater-replay")]
mod replay {
    use bridge_core::heater::HeaterSource;
    use bridge_core::replay::ReplayHeater;

    /// A full heating cycle: running on the heat pump, then idle.
    const HEATING_CYCLE: &str =
        include_str!("../../../captures/rinnai/2026-10-04T082724-heating-cycle-1/timeline.csv");
    /// Control-panel mode changes, including vacation mode (storage capacity 0).
    const PANEL_MODES: &str =
        include_str!("../../../captures/rinnai/2026-10-04T123929-panel-modes-1/timeline.csv");

    /// Opens the replay source. It loops through both recordings forever.
    pub fn open() -> anyhow::Result<Box<dyn HeaterSource>> {
        let heater = ReplayHeater::from_csvs(&[HEATING_CYCLE, PANEL_MODES])?;
        log::info!("Replaying {} recorded heater readings", heater.len());
        Ok(Box::new(heater))
    }
}

#[cfg(all(feature = "heater-rs485", not(feature = "heater-replay")))]
mod rs485 {
    use bridge_core::heater::HeaterSource;
    use bridge_core::record::HeaterReading;
    use esp_idf_svc::hal::gpio::{InputPin, OutputPin};
    use esp_idf_svc::hal::uart::Uart;

    /// Placeholder until the CTA-2045 poller exists.
    struct Rs485Heater;

    impl HeaterSource for Rs485Heater {
        fn poll(&mut self) -> anyhow::Result<HeaterReading> {
            anyhow::bail!("CTA-2045 polling not implemented yet")
        }

        fn is_simulated(&self) -> bool {
            false
        }
    }

    /// Opens the RS-485 port, starts the receive-only monitor on it, and returns a source
    /// whose polls fail.
    pub fn open(
        uart: impl Uart + 'static,
        tx: impl OutputPin + 'static,
        rx: impl InputPin + 'static,
        de: impl OutputPin + 'static,
    ) -> anyhow::Result<Box<dyn HeaterSource>> {
        let uart = crate::rs485::open(uart, tx, rx, de)?;
        crate::rs485::spawn_monitor(uart)?;
        Ok(Box::new(Rs485Heater))
    }
}
