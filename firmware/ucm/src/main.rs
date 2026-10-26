//! EcoPort Bridge UCM firmware: Rust `std` on ESP-IDF, for the ESP32-C6.
//!
//! Milestone 1 (bench reporting). The firmware currently:
//!
//! - Joins Wi-Fi and shows the connection status on LED 0
//!   (pulsing blue: connecting, breathing green: connected and uploading, fast red: Wi-Fi
//!   failed, amber: uploads failing).
//! - Reads the heater every 15 seconds from the source chosen by Cargo feature: a replay of
//!   recorded Rinnai data (`heater-replay`), or the RS-485 port (`heater-rs485`, where
//!   CTA-2045 polling isn't written yet and only the receive-only packet monitor runs).
//!   LED 1, on boards that have it, shows the heater status.
//! - Buffers samples, events and health records in RAM and uploads them, signed, to the
//!   device API once a minute (`contracts/device-v1/`).
//!
//! The device logic is in the host-testable `bridge-core` crate; this crate wires ESP-IDF
//! services to it. See `docs/architecture.md` §3 for what the finished firmware does.

mod board;
mod clock;
mod config;
mod heater;
mod nvs_seq;
#[cfg(feature = "heater-rs485")]
mod rs485;
mod sampler;
mod status_led;
mod uploader;
mod wifi;
mod ws2812;

use std::sync::Arc;
use std::thread;
use std::time::Duration;

use anyhow::Context;
use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine;
use bridge_core::seq::SeqAllocator;
use esp_idf_svc::eventloop::EspSystemEventLoop;
use esp_idf_svc::hal::peripherals::Peripherals;
use esp_idf_svc::hal::reset::ResetReason;
use esp_idf_svc::nvs::EspDefaultNvsPartition;

use config::{API_BASE_URL, DEVICE_ID, DEVICE_SECRET_B64, WIFI_PASSWORD, WIFI_SSID};
use nvs_seq::NvsSeqStore;
use status_led::{LedStatus, StatusLed};
use uploader::{Outbox, UploaderConfig};

/// How often to check that Wi-Fi is still connected and has an IP address.
const CONNECTION_CHECK_INTERVAL: Duration = Duration::from_secs(5);
/// How long to wait after a failed attempt before trying again.
const RETRY_DELAY: Duration = Duration::from_secs(10);
/// Testing aid: show the rainbow instead of the green breathe once connected.
const RAINBOW_WHEN_CONNECTED: bool = false;

fn main() -> anyhow::Result<()> {
    // Applies runtime patches from esp-idf-sys. Must be called once, before anything else.
    esp_idf_svc::sys::link_patches();

    // Routes the `log` crate's macros to the ESP-IDF serial console.
    esp_idf_svc::log::EspLogger::initialize_default();
    wifi::quiet_driver_logs();

    let reset_reason = format!("{:?}", ResetReason::get());
    log::info!(
        "EcoPort Bridge UCM {} starting, LEDs: {}, reset reason: {reset_reason}",
        env!("CARGO_PKG_VERSION"),
        board::LED_COUNT
    );

    // Checked first: a mistyped secret is a build mistake, better reported straight away.
    let secret = BASE64
        .decode(DEVICE_SECRET_B64)
        .context("DEVICE_SECRET_B64 in config.rs is not valid Base64")?;
    if secret.len() != 32 {
        log::warn!(
            "DEVICE_SECRET_B64 decodes to {} bytes; device secrets are 32",
            secret.len()
        );
    }

    let peripherals = Peripherals::take()?;
    let pins = peripherals.pins;

    // Start the LEDs first so they animate while Wi-Fi setup blocks below.
    // Both boards: WS2812 data on GPIO8 (rev A through a level shifter).
    let led = StatusLed::start(pins.gpio8)?;

    // Both boards: SP3485 RS-485 transceiver on UART1, TX GPIO18, RX GPIO19, DE GPIO20.
    #[cfg(feature = "heater-rs485")]
    let heater = heater::open(peripherals.uart1, pins.gpio18, pins.gpio19, pins.gpio20)?;
    #[cfg(not(feature = "heater-rs485"))]
    let heater = heater::open()?;
    let simulated = heater.is_simulated();

    let nvs = EspDefaultNvsPartition::take()?;
    let seq = SeqAllocator::new(Box::new(NvsSeqStore::new(nvs.clone())?));

    let mut wifi = wifi::start(
        peripherals.modem,
        EspSystemEventLoop::take()?,
        nvs,
        WIFI_SSID,
        WIFI_PASSWORD,
    )?;
    // Kept alive by the loop below, like `wifi`.
    let _sntp = clock::start()?;

    // Random per boot, so the server can tell boots apart. Taken after the radio starts,
    // when the hardware RNG is truly random.
    // SAFETY: No preconditions.
    let boot_id = format!("{:08x}", unsafe { esp_idf_svc::sys::esp_random() });
    log::info!("Device {DEVICE_ID}, boot {boot_id}, uploading to {API_BASE_URL}");

    let outbox = Arc::new(Outbox::new());
    sampler::spawn(heater, seq, Arc::clone(&outbox), led.clone(), reset_reason)?;
    uploader::spawn(
        UploaderConfig {
            base_url: API_BASE_URL.into(),
            device_id: DEVICE_ID.into(),
            secret,
            boot_id,
            firmware: env!("CARGO_PKG_VERSION").into(),
            simulated,
        },
        outbox,
        led.clone(),
    )?;

    // Supervisor loop. It never returns, which also keeps `wifi` and `_sntp` alive:
    // returning from main would drop them and shut Wi-Fi and SNTP down.
    loop {
        // "Up" means joined to the network *and* holding an IP address.
        let is_up = wifi.is_up().unwrap_or_else(|e| {
            log::warn!("Couldn't read Wi-Fi status: {e}");
            false
        });
        wifi::set_link_up(is_up);

        if !is_up {
            led.set_status(LedStatus::Connecting);
            log::info!("Connecting to Wi-Fi network '{WIFI_SSID}'...");

            match wifi::connect(&mut wifi) {
                Ok(ip) => {
                    log::info!("Wi-Fi connected, IP address {ip}");
                    wifi::set_link_up(true);
                    led.set_status(if RAINBOW_WHEN_CONNECTED {
                        LedStatus::Rainbow
                    } else {
                        LedStatus::Connected
                    });
                }
                Err(e) => {
                    log::warn!("Wi-Fi connection failed: {e:?}. Retrying in {RETRY_DELAY:?}");
                    led.set_status(LedStatus::Failed);
                    thread::sleep(RETRY_DELAY);
                    continue;
                }
            }
        }

        thread::sleep(CONNECTION_CHECK_INTERVAL);
    }
}
