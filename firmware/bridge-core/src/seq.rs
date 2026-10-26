//! Record sequence numbers that keep increasing across reboots.
//!
//! Writing the counter to flash for every record would wear it out, so the allocator
//! persists a *high-water mark* instead: before handing out a number at or above the saved
//! mark, it saves a mark one block higher. After a reboot it resumes from the saved mark,
//! skipping whatever was left of the previous block. Every number ever issued is below the
//! saved mark, so numbers never repeat.

/// How many sequence numbers each saved high-water mark covers.
pub const SEQ_BLOCK: u32 = 1024;

/// Persistent storage for the high-water mark (NVS on the device).
pub trait SeqStore {
    /// The saved mark, or `None` if nothing has been saved yet.
    fn load(&mut self) -> Option<u32>;
    fn save(&mut self, high_water_mark: u32) -> anyhow::Result<()>;
}

/// Hands out sequence numbers.
pub struct SeqAllocator {
    store: Box<dyn SeqStore + Send>,
    next: u32,
    /// Numbers below this are covered by the last save (or attempted save).
    reserved_until: u32,
}

impl SeqAllocator {
    /// Resumes from the store's saved mark, or starts at 0.
    pub fn new(mut store: Box<dyn SeqStore + Send>) -> Self {
        let start = store.load().unwrap_or(0);
        Self {
            store,
            next: start,
            reserved_until: start,
        }
    }

    /// The next sequence number.
    ///
    /// If saving a new mark fails, the failure is logged and numbering carries on: losing
    /// uniqueness after a reboot is better than stopping the device. The next save is tried
    /// at the following block.
    // Not an `Iterator`: it never ends, and saving is a side effect callers should notice.
    #[allow(clippy::should_implement_trait)]
    pub fn next(&mut self) -> u32 {
        if self.next >= self.reserved_until {
            let mark = self.next.saturating_add(SEQ_BLOCK);
            if let Err(e) = self.store.save(mark) {
                log::warn!("Couldn't save sequence high-water mark {mark}: {e:#}");
            }
            self.reserved_until = mark;
        }

        let seq = self.next;
        self.next = self.next.saturating_add(1);
        seq
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Mutex};

    /// An in-memory store that records every save, shared with the test through an `Arc`.
    #[derive(Clone, Default)]
    struct MemStore {
        value: Arc<Mutex<Option<u32>>>,
        saves: Arc<Mutex<Vec<u32>>>,
        fail: bool,
    }

    impl SeqStore for MemStore {
        fn load(&mut self) -> Option<u32> {
            *self.value.lock().unwrap()
        }

        fn save(&mut self, high_water_mark: u32) -> anyhow::Result<()> {
            anyhow::ensure!(!self.fail, "flash write failed");
            *self.value.lock().unwrap() = Some(high_water_mark);
            self.saves.lock().unwrap().push(high_water_mark);
            Ok(())
        }
    }

    #[test]
    fn fresh_store_starts_at_zero_and_reserves_one_block() {
        let store = MemStore::default();
        let mut seq = SeqAllocator::new(Box::new(store.clone()));

        assert_eq!([seq.next(), seq.next(), seq.next()], [0, 1, 2]);
        assert_eq!(*store.saves.lock().unwrap(), [SEQ_BLOCK]);
    }

    #[test]
    fn reserves_the_next_block_when_one_runs_out() {
        let store = MemStore::default();
        let mut seq = SeqAllocator::new(Box::new(store.clone()));

        for expected in 0..SEQ_BLOCK {
            assert_eq!(seq.next(), expected);
        }
        assert_eq!(*store.saves.lock().unwrap(), [1024]);

        assert_eq!(seq.next(), 1024);
        assert_eq!(*store.saves.lock().unwrap(), [1024, 2048]);
    }

    #[test]
    fn resumes_from_the_saved_high_water_mark() {
        let store = MemStore::default();
        *store.value.lock().unwrap() = Some(1024);
        let mut seq = SeqAllocator::new(Box::new(store.clone()));

        assert_eq!(seq.next(), 1024);
        assert_eq!(seq.next(), 1025);
        assert_eq!(*store.saves.lock().unwrap(), [2048]);
    }

    #[test]
    fn save_failures_do_not_stop_numbering() {
        let store = MemStore {
            fail: true,
            ..MemStore::default()
        };
        let mut seq = SeqAllocator::new(Box::new(store));

        let mut last = seq.next();
        for _ in 0..3000 {
            let n = seq.next();
            assert!(n > last);
            last = n;
        }
    }
}
