# Firmware

EcoPort Bridge UCM firmware for the **ESP32-C6**, in **Rust on ESP-IDF** (`std`, using the
community `esp-idf-svc` crates). This is not bare-metal Rust: crates and examples written for
`esp-hal` / `no_std` generally won't work here.

## Layout

| Crate | Builds for | Purpose |
|---|---|---|
| [`cta2045/`](cta2045/) | PC and ESP32 | CTA-2045 link layer: framing, checksum, stream parser. No hardware dependencies. Ported from `python/src/ecoport/cta2045/` |
| [`ucm/`](ucm/) | ESP32-C6 only | The device app: Wi-Fi, status LED, RS-485 link, and later the record buffer, uploader and command executor |

Appliance-specific code (Rinnai) will get its own host-testable crate next to `cta2045`.

The two are separate packages rather than one Cargo workspace on purpose. `ucm/` has its
own `.cargo/config.toml` (ESP32 target, nightly toolchain) and `rust-toolchain.toml`, which
only apply when you run cargo inside `ucm/`. In `cta2045/`, plain `cargo test` runs on your PC
with stable Rust.

## Board pin map (rev A)

| Function | GPIO |
|---|---|
| RS-485 TX (UART1) | 18 |
| RS-485 RX (UART1) | 19 |
| RS-485 driver enable (UART1 RTS) | 20 |
| WS2812 status LED | 8 |
| I²C SDA / SCL (Qwiic) | 21 / 22 |
| Spare | 23 |
| USB D− / D+ (native USB-Serial/JTAG) | 12 / 13 |

## Prerequisites

Same as the [esp32-c6-pulse-rust](https://github.com/jrowe88/esp32-c6-pulse-rust) project:
`rustup`, `ldproxy` (`cargo install ldproxy`), `espflash` (`cargo install espflash`), and
Python 3 for the ESP-IDF build. The first build downloads ESP-IDF into `~/.espressif` and
takes a while; later builds are quick.

## Build and test

```bash
# Protocol crate: runs on your PC, including replaying the recordings in captures/
cd firmware/cta2045
cargo test

# Device app
cd firmware/ucm
cp src/wifi_credentials.example.rs src/wifi_credentials.rs   # then enter your Wi-Fi details
cargo build --release
cargo run --release      # build, flash over USB, open the serial monitor
```

On Windows, ESP-IDF builds fail when the output path is long, so `ucm/.cargo/config.toml`
puts build output in `C:/t/ucm`. On macOS/Linux, `export CARGO_TARGET_DIR=target`.

## Status

Bring-up. The app joins Wi-Fi, drives the status LED, and logs every CTA-2045 packet the
heater sends. **It does not transmit to the heater yet.** Next steps are in
[`docs/architecture.md` §14](../docs/architecture.md#14-implementation-sequence).

Wi-Fi credentials are compiled in for now; ESP BLE provisioning replaces them later
(architecture §9.2).
