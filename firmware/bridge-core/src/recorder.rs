//! Turns heater polls into numbered records.

use crate::record::{EventKind, Health, HeaterReading, LinkStats, Record, RecordBody};
use crate::seq::SeqAllocator;

/// Numbers records and notices changes between polls (operational state, link up/down).
pub struct Recorder {
    seq: SeqAllocator,
    /// The operational state of the last good reading.
    last_op_state: Option<u8>,
    /// True after a failed poll, until the next good one.
    link_lost: bool,
}

impl Recorder {
    pub fn new(seq: SeqAllocator) -> Self {
        Self {
            seq,
            last_op_state: None,
            link_lost: false,
        }
    }

    /// The records for one heater poll, in order:
    ///
    /// - A good reading gives a sample, preceded by `HeaterLinkRestored` if the previous poll
    ///   failed and followed by `OpStateChanged` if the state differs from the last reading.
    /// - The first failed poll in a run gives `HeaterLinkLost`; further failures give nothing.
    pub fn on_poll(
        &mut self,
        result: anyhow::Result<HeaterReading>,
        uptime_ms: u64,
    ) -> Vec<Record> {
        let mut out = Vec::new();

        match result {
            Ok(reading) => {
                if self.link_lost {
                    self.link_lost = false;
                    out.push(self.event(EventKind::HeaterLinkRestored, uptime_ms));
                }

                // M1 sources don't report link statistics; a good reading means the link
                // worked first time.
                let link = LinkStats {
                    ok: true,
                    retries: 0,
                    naks: 0,
                };
                out.push(self.record(RecordBody::Sample { reading, link }, uptime_ms));

                if let Some(from) = self.last_op_state.filter(|&s| s != reading.op_state) {
                    let to = reading.op_state;
                    out.push(self.event(EventKind::OpStateChanged { from, to }, uptime_ms));
                }
                self.last_op_state = Some(reading.op_state);
            }
            Err(e) => {
                if !self.link_lost {
                    self.link_lost = true;
                    let error = format!("{e:#}");
                    out.push(self.event(EventKind::HeaterLinkLost { error }, uptime_ms));
                }
            }
        }

        out
    }

    pub fn event(&mut self, kind: EventKind, uptime_ms: u64) -> Record {
        self.record(RecordBody::Event(kind), uptime_ms)
    }

    pub fn health(&mut self, health: Health, uptime_ms: u64) -> Record {
        self.record(RecordBody::Health(health), uptime_ms)
    }

    fn record(&mut self, body: RecordBody, uptime_ms: u64) -> Record {
        Record {
            seq: self.seq.next(),
            uptime_ms,
            body,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::record::{EventKind, HeaterReading, Record, RecordBody};
    use crate::seq::SeqStore;

    struct NullStore;

    impl SeqStore for NullStore {
        fn load(&mut self) -> Option<u32> {
            None
        }
        fn save(&mut self, _: u32) -> anyhow::Result<()> {
            Ok(())
        }
    }

    fn recorder() -> Recorder {
        Recorder::new(SeqAllocator::new(Box::new(NullStore)))
    }

    fn reading(op_state: u8) -> HeaterReading {
        HeaterReading {
            op_state,
            electricity_rate_w: 242,
            electricity_cumulative_wh: 0,
            total_storage_capacity_wh: 12011,
            present_energy_take_wh: 4202,
        }
    }

    fn events(records: &[Record]) -> Vec<EventKind> {
        records
            .iter()
            .filter_map(|r| match &r.body {
                RecordBody::Event(e) => Some(e.clone()),
                _ => None,
            })
            .collect()
    }

    fn sample_count(records: &[Record]) -> usize {
        records
            .iter()
            .filter(|r| matches!(r.body, RecordBody::Sample { .. }))
            .count()
    }

    #[test]
    fn first_reading_is_one_sample() {
        let mut rec = recorder();
        let out = rec.on_poll(Ok(reading(1)), 1_000);

        assert_eq!(out.len(), 1);
        assert_eq!(sample_count(&out), 1);
        assert_eq!(out[0].uptime_ms, 1_000);
        match &out[0].body {
            RecordBody::Sample { reading: r, link } => {
                assert_eq!(*r, reading(1));
                assert!(link.ok);
            }
            other => panic!("expected a sample, got {other:?}"),
        }
    }

    #[test]
    fn op_state_change_adds_event() {
        let mut rec = recorder();
        rec.on_poll(Ok(reading(1)), 0);
        let out = rec.on_poll(Ok(reading(0)), 15_000);

        assert_eq!(sample_count(&out), 1);
        assert_eq!(events(&out), [EventKind::OpStateChanged { from: 1, to: 0 }]);
    }

    #[test]
    fn same_op_state_adds_no_event() {
        let mut rec = recorder();
        rec.on_poll(Ok(reading(1)), 0);
        let out = rec.on_poll(Ok(reading(1)), 15_000);

        assert_eq!(out.len(), 1);
        assert!(events(&out).is_empty());
    }

    #[test]
    fn link_lost_and_restored() {
        let mut rec = recorder();
        rec.on_poll(Ok(reading(1)), 0);

        let lost = rec.on_poll(Err(anyhow::anyhow!("no answer")), 15_000);
        assert_eq!(sample_count(&lost), 0);
        assert_eq!(
            events(&lost),
            [EventKind::HeaterLinkLost {
                error: "no answer".into()
            }]
        );

        // Further failures while the link is down add nothing.
        assert!(rec
            .on_poll(Err(anyhow::anyhow!("no answer")), 30_000)
            .is_empty());

        let restored = rec.on_poll(Ok(reading(1)), 45_000);
        assert_eq!(events(&restored), [EventKind::HeaterLinkRestored]);
        assert_eq!(sample_count(&restored), 1);
    }

    #[test]
    fn seq_strictly_increases_across_all_records() {
        let mut rec = recorder();
        let mut all = Vec::new();
        all.push(rec.event(
            EventKind::Boot {
                reset_reason: "PowerOn".into(),
            },
            0,
        ));
        all.extend(rec.on_poll(Ok(reading(1)), 1));
        all.extend(rec.on_poll(Ok(reading(0)), 2));
        all.extend(rec.on_poll(Err(anyhow::anyhow!("x")), 3));
        all.extend(rec.on_poll(Ok(reading(1)), 4));
        all.push(rec.health(
            crate::record::Health {
                rssi_dbm: None,
                free_heap_bytes: 0,
                buffer_depth: 0,
                reset_reason: String::new(),
            },
            5,
        ));

        assert!(all.len() >= 8);
        for pair in all.windows(2) {
            assert!(pair[0].seq < pair[1].seq, "{pair:?}");
        }
    }
}
