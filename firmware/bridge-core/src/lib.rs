//! UCM device logic with no hardware dependencies.
//!
//! Everything the bridge firmware does that doesn't touch a peripheral lives here, so it
//! builds and runs its tests on a PC: the record types, the upload body and request signing
//! from the device API contract (`contracts/device-v1/`), timestamp formatting, sequence
//! numbering, turning heater polls into records, the upload buffer and schedule, and a heater
//! source that replays recordings. The `ucm` crate wires ESP-IDF services to it.

pub mod buffer;
pub mod heater;
pub mod record;
pub mod recorder;
pub mod replay;
pub mod schedule;
pub mod seq;
pub mod sign;
pub mod time;
pub mod wire;
