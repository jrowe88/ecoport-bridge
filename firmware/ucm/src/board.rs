//! What differs between the boards the firmware runs on, selected by Cargo feature.
//!
//! Both boards use the same pins: WS2812 data on GPIO8, and the RS-485 transceiver on UART1
//! (TX GPIO18, RX GPIO19, driver enable GPIO20). On the dev kit, GPIO8 is its onboard LED.

#[cfg(all(feature = "board-devkit", feature = "board-rev-a"))]
compile_error!("Enable only one board feature: `board-devkit` or `board-rev-a`.");
#[cfg(not(any(feature = "board-devkit", feature = "board-rev-a")))]
compile_error!("Enable a board feature: `board-devkit` or `board-rev-a`.");

/// Number of WS2812 LEDs on the chain: the dev kit has its onboard LED only.
#[cfg(feature = "board-devkit")]
pub const LED_COUNT: usize = 1;

/// Number of WS2812 LEDs on the chain: LED 0 shows network and upload status, LED 1 the
/// heater.
#[cfg(all(feature = "board-rev-a", not(feature = "board-devkit")))]
pub const LED_COUNT: usize = 2;
