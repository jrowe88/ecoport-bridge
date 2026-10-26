//! Records waiting to be uploaded.
//!
//! Records stay here until the server acknowledges them, so a failed or lost upload just
//! sends the same records again (the server treats repeats as no-ops). When the buffer is
//! full during a long outage, samples are sacrificed before events.

use std::collections::VecDeque;

use crate::record::Record;

/// Capacity used by the firmware: about an hour of 15-second samples.
pub const DEFAULT_CAPACITY: usize = 256;

/// A bounded queue of records, oldest first.
pub struct RecordBuffer {
    records: VecDeque<Record>,
    capacity: usize,
}

impl RecordBuffer {
    pub fn new(capacity: usize) -> Self {
        Self {
            records: VecDeque::with_capacity(capacity),
            capacity,
        }
    }

    /// Adds a record. If the buffer is full, first drops the oldest record that isn't an
    /// event, or the oldest event if there is nothing else.
    pub fn push(&mut self, record: Record) {
        if self.capacity == 0 {
            return;
        }
        if self.records.len() >= self.capacity {
            let victim = self.records.iter().position(|r| !r.is_event()).unwrap_or(0);
            self.records.remove(victim);
        }
        self.records.push_back(record);
    }

    /// Copies of up to `max` of the oldest records. Nothing is removed until [`Self::ack`].
    pub fn batch(&self, max: usize) -> Vec<Record> {
        self.records.iter().take(max).cloned().collect()
    }

    /// Removes every record with `seq <= ack_seq`: the server has stored them.
    pub fn ack(&mut self, ack_seq: u32) {
        self.records.retain(|r| r.seq > ack_seq);
    }

    pub fn len(&self) -> usize {
        self.records.len()
    }

    pub fn is_empty(&self) -> bool {
        self.records.is_empty()
    }

    /// True if any waiting record is an event (these are uploaded without waiting).
    pub fn contains_event(&self) -> bool {
        self.records.iter().any(Record::is_event)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::record::{EventKind, HeaterReading, LinkStats, Record, RecordBody};

    fn sample(seq: u32) -> Record {
        Record {
            seq,
            uptime_ms: u64::from(seq),
            body: RecordBody::Sample {
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
            },
        }
    }

    fn event(seq: u32) -> Record {
        Record {
            seq,
            uptime_ms: u64::from(seq),
            body: RecordBody::Event(EventKind::TimeSynced),
        }
    }

    fn seqs(buffer: &RecordBuffer) -> Vec<u32> {
        buffer.batch(usize::MAX).iter().map(|r| r.seq).collect()
    }

    #[test]
    fn batch_is_oldest_first_and_limited() {
        let mut buf = RecordBuffer::new(10);
        for seq in 1..=5 {
            buf.push(sample(seq));
        }

        assert_eq!(
            buf.batch(3).iter().map(|r| r.seq).collect::<Vec<_>>(),
            [1, 2, 3]
        );
        assert_eq!(buf.len(), 5);
    }

    #[test]
    fn ack_removes_up_to_ack_seq_only() {
        let mut buf = RecordBuffer::new(10);
        for seq in 3..=8 {
            buf.push(sample(seq));
        }

        buf.ack(5);
        assert_eq!(seqs(&buf), [6, 7, 8]);

        buf.ack(2);
        assert_eq!(seqs(&buf), [6, 7, 8]);
    }

    #[test]
    fn buffer_full_drops_oldest_sample_not_event() {
        let mut buf = RecordBuffer::new(3);
        buf.push(event(1));
        buf.push(sample(2));
        buf.push(sample(3));

        buf.push(sample(4));

        assert_eq!(seqs(&buf), [1, 3, 4]);
        assert_eq!(buf.len(), 3);
    }

    #[test]
    fn all_events_buffer_drops_oldest_event() {
        let mut buf = RecordBuffer::new(3);
        for seq in 1..=4 {
            buf.push(event(seq));
        }

        assert_eq!(seqs(&buf), [2, 3, 4]);
    }

    #[test]
    fn failed_upload_keeps_records() {
        let mut buf = RecordBuffer::new(DEFAULT_CAPACITY);
        for seq in 1..=4 {
            buf.push(sample(seq));
        }

        let batch = buf.batch(100);
        assert_eq!(batch.len(), 4);
        // The upload fails, so nothing is acknowledged.
        assert_eq!(buf.len(), 4);
        assert_eq!(seqs(&buf), [1, 2, 3, 4]);
    }

    #[test]
    fn reports_waiting_events() {
        let mut buf = RecordBuffer::new(10);
        assert!(buf.is_empty());
        buf.push(sample(1));
        assert!(!buf.contains_event());
        buf.push(event(2));
        assert!(buf.contains_event());
        buf.ack(2);
        assert!(!buf.contains_event());
        assert!(buf.is_empty());
    }
}
