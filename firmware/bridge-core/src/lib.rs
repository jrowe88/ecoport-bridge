//! UCM device logic with no hardware dependencies.
//!
//! Everything the bridge firmware does that doesn't touch a peripheral lives here, so it
//! builds and runs its tests on a PC: the record types, the upload body and request signing
//! from the device API contract (`contracts/device-v1/`), timestamp formatting, and (in later
//! modules) the record buffer and upload schedule. The `ucm` crate wires ESP-IDF services to it.

pub mod record;
pub mod sign;
pub mod time;
pub mod wire;
