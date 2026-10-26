//! Where heater readings come from.

use crate::record::HeaterReading;

/// A source of heater readings: the CTA-2045 link to a real heater, or a replayed recording.
pub trait HeaterSource: Send {
    /// Reads the heater's current state. An error means the heater didn't answer.
    fn poll(&mut self) -> anyhow::Result<HeaterReading>;

    /// True when the readings are not from a real heater (uploaded as `"simulated": true`).
    fn is_simulated(&self) -> bool;
}
