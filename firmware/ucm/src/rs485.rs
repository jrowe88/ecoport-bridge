//! RS-485 link to the heater's CTA-2045 port.
//!
//! The board's SP3485 transceiver is on UART1. ESP-IDF's RS-485 half-duplex mode drives the
//! transceiver's driver-enable line from the UART's RTS pin, raising it only while bytes are
//! being sent.

use std::thread;
use std::time::Duration;

use cta2045::StreamParser;
use esp_idf_svc::hal::delay::TickType;
use esp_idf_svc::hal::gpio::{AnyInputPin, InputPin, OutputPin};
use esp_idf_svc::hal::uart::{config, Uart, UartDriver};
use esp_idf_svc::hal::units::Hertz;

/// CTA-2045 serial settings are 19,200 baud, 8 data bits, no parity, 1 stop bit
/// (the last three are the driver's defaults).
const BAUD: Hertz = Hertz(19_200);

/// A gap this long with no bytes ends any partial packet, which is then discarded.
const INTER_BYTE_TIMEOUT: Duration = Duration::from_millis(100);

/// Stack size for the receive thread. Most of it is headroom for log formatting.
const TASK_STACK_SIZE: usize = 6144;

/// Opens the UART in RS-485 half-duplex mode, with `de` as the driver-enable pin.
pub fn open<'d>(
    uart: impl Uart + 'd,
    tx: impl OutputPin + 'd,
    rx: impl InputPin + 'd,
    de: impl OutputPin + 'd,
) -> anyhow::Result<UartDriver<'d>> {
    let config = config::Config::default()
        .baudrate(BAUD)
        .mode(config::Mode::RS485HalfDuplex);

    Ok(UartDriver::new(
        uart,
        tx,
        rx,
        None::<AnyInputPin>,
        Some(de),
        &config,
    )?)
}

/// Spawns a thread that logs every packet the heater sends.
///
/// Receive only: nothing is transmitted, so this is safe to run on a live heater during
/// bring-up.
pub fn spawn_monitor(uart: UartDriver<'static>) -> anyhow::Result<()> {
    thread::Builder::new()
        .name("rs485_rx".into())
        .stack_size(TASK_STACK_SIZE)
        .spawn(move || monitor(&uart))?;

    Ok(())
}

/// Body of the receive thread. Never returns.
fn monitor(uart: &UartDriver) {
    let mut parser = StreamParser::new();
    let mut buf = [0u8; 128];
    let timeout = TickType::from(INTER_BYTE_TIMEOUT).ticks();

    loop {
        match uart.read(&mut buf, timeout) {
            Ok(0) => {
                let stale = parser.flush_stale();
                if stale > 0 {
                    log::warn!("Discarded {stale} bytes of an incomplete packet");
                }
            }
            Ok(n) => {
                let dropped_before = parser.dropped_bytes();
                for frame in parser.feed(&buf[..n]) {
                    log::info!("RX {}", hex(&frame.raw));
                }
                let dropped = parser.dropped_bytes() - dropped_before;
                if dropped > 0 {
                    log::warn!("Skipped {dropped} junk bytes");
                }
            }
            Err(e) => {
                log::warn!("RS-485 read failed: {e}");
                thread::sleep(INTER_BYTE_TIMEOUT);
            }
        }
    }
}

/// Formats bytes as space-separated hex, matching the capture tools (`08 01 00 00 7e cd`).
fn hex(bytes: &[u8]) -> String {
    bytes
        .iter()
        .map(|b| format!("{b:02x}"))
        .collect::<Vec<_>>()
        .join(" ")
}
