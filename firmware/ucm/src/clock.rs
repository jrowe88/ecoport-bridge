//! Uptime and wall-clock time.
//!
//! Records are stamped with uptime when they're made, and converted to UTC when uploaded
//! (`bridge_core::wire::upload_body`). The wall clock comes from SNTP; until the first sync
//! it is wrong (it starts at 1970), so nothing is uploaded before then.

use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

use esp_idf_svc::sntp::{EspSntp, SntpConf};

/// Set by the SNTP callback after the first successful sync. Never cleared: later re-syncs
/// only nudge an already correct clock.
static SYNCED: AtomicBool = AtomicBool::new(false);

/// Starts SNTP with the default `pool.ntp.org` servers. Call after Wi-Fi is started, so the
/// network stack exists; syncing begins once the network is up.
///
/// The returned service must be kept alive: dropping it stops SNTP.
pub fn start() -> anyhow::Result<EspSntp<'static>> {
    let sntp = EspSntp::new_with_callback(&SntpConf::default(), |_| {
        if !SYNCED.swap(true, Ordering::Relaxed) {
            log::info!("Clock synced by SNTP");
        }
    })?;
    Ok(sntp)
}

/// Milliseconds since boot.
pub fn uptime_ms() -> u64 {
    // SAFETY: Reads a hardware timer; no preconditions.
    let us = unsafe { esp_idf_svc::sys::esp_timer_get_time() };
    (us / 1000) as u64
}

/// Unix time in milliseconds at boot (wall clock minus uptime), or `None` until SNTP has
/// synced the clock. Adding it to a record's uptime gives the record's UTC time.
pub fn offset_ms() -> Option<i64> {
    if !SYNCED.load(Ordering::Relaxed) {
        return None;
    }
    let now = SystemTime::now().duration_since(UNIX_EPOCH).ok()?;
    Some(now.as_millis() as i64 - uptime_ms() as i64)
}
