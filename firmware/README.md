# Firmware

EcoPort Bridge UCM firmware for the **ESP32-C6**, in **Rust on ESP-IDF** (`std`, using the
community `esp-idf-svc` crates). This is not bare-metal Rust: crates and examples written for
`esp-hal` / `no_std` generally won't work here.

## Layout

| Crate | Builds for | Purpose |
|---|---|---|
| [`cta2045/`](cta2045/) | PC and ESP32 | CTA-2045 link layer: framing, checksum, stream parser. No hardware dependencies. Ported from `python/src/ecoport/cta2045/` |
| [`bridge-core/`](bridge-core/) | PC and ESP32 | Device logic with no hardware dependencies: records and sequence numbers, the RAM upload buffer and schedule, the upload JSON and request signing from [`contracts/device-v1/`](../contracts/device-v1/), and a heater source that replays `captures/rinnai/*/timeline.csv` |
| [`ucm/`](ucm/) | ESP32-C6 only | The device app: wires ESP-IDF (Wi-Fi, SNTP, NVS, HTTP, LEDs, RS-485) to `bridge-core`; samples the heater and uploads signed records. Later the CTA-2045 poller and command executor |

Appliance-specific code (Rinnai) will get its own host-testable crate next to `cta2045`.

They are separate packages rather than one Cargo workspace on purpose. `ucm/` has its
own `.cargo/config.toml` (ESP32 target, nightly toolchain) and `rust-toolchain.toml`, which
only apply when you run cargo inside `ucm/`. In `cta2045/` and `bridge-core/`, plain
`cargo test` runs on your PC with stable Rust.

## Board pin map (rev A)

| Function | GPIO |
|---|---|
| RS-485 TX (UART1) | 18 |
| RS-485 RX (UART1) | 19 |
| RS-485 driver enable (UART1 RTS) | 20 |
| WS2812 status LEDs (data) | 8 |
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

# Device logic: runs on your PC, including the contract's signing vectors
cd firmware/bridge-core
cargo test

# Device app
cd firmware/ucm
cp src/config.example.rs src/config.rs   # then enter Wi-Fi, API URL, device ID and secret
cargo build --release
cargo run --release      # build, flash over USB, open the serial monitor
```

`src/config.rs` is gitignored. It replaces the older `src/wifi_credentials.rs`; copy your
Wi-Fi details across if you had one.

### Feature flags

Exactly one board and one heater source; the build fails with a clear message otherwise.

| Features | Board | Heater data |
|---|---|---|
| (default) `board-devkit,heater-replay` | ESP32-C6 dev kit, 1 LED | Replay of `captures/rinnai/` recordings, uploaded as `"simulated": true` |
| `--no-default-features --features board-rev-a,heater-replay` | Rev-A board or breadboard, 2 LEDs | Replay |
| `--no-default-features --features board-rev-a,heater-rs485` | Rev-A board on a heater | RS-485 (CTA-2045 polling not written yet: every poll fails, the receive-only monitor logs packets) |

### Status LEDs

| LED | Shows |
|---|---|
| 0 | Blue pulse: connecting to Wi-Fi. Green breathe: connected, uploads working. Fast red: Wi-Fi failed. Amber breathe: uploads failing (wrong secret, server down, ...) |
| 1 (rev A only) | Dim purple: replayed data. Dim green: heater answering. Red: heater not answering |

On Windows, ESP-IDF builds fail when the output path is long, so `ucm/.cargo/config.toml`
puts build output in `C:/t/ucm`. On macOS/Linux, `export CARGO_TARGET_DIR=target`.

## Status

Milestone 1 (bench reporting). The app joins Wi-Fi, syncs the clock over SNTP, reads the
heater every 15 s (replayed data on the bench), and uploads samples, events and 5-minute
health records to the device API every minute, signed with the device secret. Records wait
in a 256-record RAM buffer until the server acknowledges them; when it is full, the oldest
samples are dropped before any event. **It does not transmit to the heater yet.** Next steps
are in
[`docs/architecture.md` §14](../docs/architecture.md#14-implementation-sequence).

Wi-Fi credentials and the device secret are compiled in for now; ESP BLE provisioning replaces them later
(architecture §9.2).
