//! When to upload next.
//!
//! Normally once a minute, sooner when an event is waiting. After a failure, exponential
//! backoff with jitter, so a fleet of devices doesn't retry in lockstep after an outage.

/// Time between uploads while everything is working.
pub const UPLOAD_INTERVAL_MS: u64 = 60_000;
/// Delay after the first failure in a row; doubles with each further failure.
pub const BACKOFF_INITIAL_MS: u64 = 5_000;
/// Longest backoff delay, before jitter.
pub const BACKOFF_MAX_MS: u64 = 300_000;
/// Jitter of `1.0` stretches a delay by this fraction, `-1.0` shrinks it by the same.
pub const JITTER_FRACTION: f32 = 0.2;

/// Times are milliseconds on any monotonic clock (uptime on the device).
pub struct UploadSchedule {
    next_due_ms: u64,
    /// Failures since the last success.
    failures: u32,
}

impl UploadSchedule {
    /// A schedule that is due straight away.
    pub fn new(now_ms: u64) -> Self {
        Self {
            next_due_ms: now_ms,
            failures: 0,
        }
    }

    pub fn due(&self, now_ms: u64) -> bool {
        now_ms >= self.next_due_ms
    }

    /// Makes the next upload due immediately (an event is waiting).
    ///
    /// Ignored while backing off after a failure: the server or network is already known
    /// to be unhappy, and retrying on every event would defeat the backoff.
    pub fn request_now(&mut self) {
        if self.failures == 0 {
            self.next_due_ms = 0;
        }
    }

    /// The upload worked: next one in [`UPLOAD_INTERVAL_MS`], and backoff starts over.
    pub fn on_success(&mut self, now_ms: u64) {
        self.failures = 0;
        self.next_due_ms = now_ms + UPLOAD_INTERVAL_MS;
    }

    /// The upload failed: wait 5 s, 10 s, 20 s, ... up to 300 s, each stretched or shrunk
    /// by `jitter` (`-1.0..=1.0`, mapped to ±20%).
    pub fn on_failure(&mut self, now_ms: u64, jitter: f32) {
        let doublings = self.failures.min(16);
        let base = (BACKOFF_INITIAL_MS << doublings).min(BACKOFF_MAX_MS);
        let factor = 1.0 + JITTER_FRACTION * jitter.clamp(-1.0, 1.0);

        self.failures = self.failures.saturating_add(1);
        self.next_due_ms = now_ms + (base as f32 * factor).round() as u64;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn due_at_start() {
        assert!(UploadSchedule::new(5_000).due(5_000));
    }

    #[test]
    fn success_waits_one_interval() {
        let mut s = UploadSchedule::new(0);
        s.on_success(0);
        assert!(!s.due(59_999));
        assert!(s.due(60_000));
    }

    /// The delay each failure in a row sets, with no jitter.
    fn backoff_delays(s: &mut UploadSchedule, failures: usize) -> Vec<u64> {
        let mut now = 0;
        (0..failures)
            .map(|_| {
                s.on_failure(now, 0.0);
                let mut delay = 0;
                while !s.due(now + delay) {
                    delay += 1_000;
                }
                now += delay;
                delay / 1_000
            })
            .collect()
    }

    #[test]
    fn failures_back_off_exponentially_up_to_five_minutes() {
        let mut s = UploadSchedule::new(0);
        assert_eq!(
            backoff_delays(&mut s, 8),
            [5, 10, 20, 40, 80, 160, 300, 300]
        );
    }

    #[test]
    fn jitter_stretches_the_delay_by_up_to_20_percent() {
        let mut s = UploadSchedule::new(0);
        s.on_failure(0, 1.0);
        assert!(!s.due(5_999));
        assert!(s.due(6_000));

        let mut s = UploadSchedule::new(0);
        s.on_failure(0, -1.0);
        assert!(!s.due(3_999));
        assert!(s.due(4_000));
    }

    #[test]
    fn request_now_makes_it_due() {
        let mut s = UploadSchedule::new(0);
        s.on_success(0);
        assert!(!s.due(1_000));
        s.request_now();
        assert!(s.due(1_000));
    }

    #[test]
    fn request_now_does_not_cut_a_backoff_short() {
        let mut s = UploadSchedule::new(0);
        s.on_failure(0, 0.0);
        s.request_now();
        assert!(!s.due(1_000));
        assert!(s.due(5_000));
    }

    #[test]
    fn success_resets_backoff() {
        let mut s = UploadSchedule::new(0);
        s.on_failure(0, 0.0);
        s.on_failure(5_000, 0.0);
        s.on_success(15_000);
        s.on_failure(75_000, 0.0);
        assert!(!s.due(79_999));
        assert!(s.due(80_000));
    }
}
