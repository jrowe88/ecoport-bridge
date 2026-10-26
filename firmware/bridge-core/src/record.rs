//! The records the device makes and uploads.
//!
//! These are the in-memory forms. [`crate::wire`] turns them into the JSON the device API
//! expects; see `contracts/device-v1/README.md` for the meaning of each field.

/// One reading of the heater's commodity and operational state, as the heater reports it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct HeaterReading {
    /// CTA-2045 operational state code (0 idle, 1 running, ...).
    pub op_state: u8,
    pub electricity_rate_w: u32,
    pub electricity_cumulative_wh: u32,
    pub total_storage_capacity_wh: u32,
    pub present_energy_take_wh: u32,
}

/// How the link to the heater behaved while taking a sample.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LinkStats {
    pub ok: bool,
    pub retries: u32,
    pub naks: u32,
}

/// Periodic device health snapshot.
#[derive(Clone, Debug, PartialEq)]
pub struct Health {
    /// Wi-Fi signal strength; `None` when not connected.
    pub rssi_dbm: Option<i8>,
    pub free_heap_bytes: u32,
    /// Records waiting in the upload buffer.
    pub buffer_depth: u32,
    pub reset_reason: String,
}

/// Something that happened, as opposed to a periodic sample.
#[derive(Clone, Debug, PartialEq)]
pub enum EventKind {
    Boot { reset_reason: String },
    OpStateChanged { from: u8, to: u8 },
    TimeSynced,
    HeaterLinkLost { error: String },
    HeaterLinkRestored,
}

/// What a record carries, by kind.
#[derive(Clone, Debug, PartialEq)]
pub enum RecordBody {
    Sample {
        reading: HeaterReading,
        link: LinkStats,
    },
    Event(EventKind),
    Health(Health),
}

/// One numbered record.
///
/// Records carry the uptime they were made at rather than wall-clock time, because the clock
/// may not be synced yet; the upload converts uptime to UTC once it is.
#[derive(Clone, Debug, PartialEq)]
pub struct Record {
    /// Device-wide sequence number, strictly increasing across reboots.
    pub seq: u32,
    /// Milliseconds since boot when the record was made.
    pub uptime_ms: u64,
    pub body: RecordBody,
}

impl Record {
    /// Events are kept in preference to samples when the buffer is full, and trigger an
    /// upload straight away.
    pub fn is_event(&self) -> bool {
        matches!(self.body, RecordBody::Event(_))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn record(body: RecordBody) -> Record {
        Record {
            seq: 1,
            uptime_ms: 0,
            body,
        }
    }

    #[test]
    fn only_events_are_events() {
        assert!(record(RecordBody::Event(EventKind::TimeSynced)).is_event());
        assert!(!record(RecordBody::Health(Health {
            rssi_dbm: None,
            free_heap_bytes: 0,
            buffer_depth: 0,
            reset_reason: String::new(),
        }))
        .is_event());
        assert!(!record(RecordBody::Sample {
            reading: HeaterReading {
                op_state: 0,
                electricity_rate_w: 0,
                electricity_cumulative_wh: 0,
                total_storage_capacity_wh: 0,
                present_energy_take_wh: 0,
            },
            link: LinkStats {
                ok: true,
                retries: 0,
                naks: 0,
            },
        })
        .is_event());
    }
}
