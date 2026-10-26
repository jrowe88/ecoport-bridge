//! Polls the heater and records what happens.
//!
//! The sampler thread owns the [`Recorder`], so every record is numbered here: the boot
//! event, a sample every 15 seconds (plus any events the poll reveals), `timeSynced` when the
//! clock first syncs, and a health record every 5 minutes. Records go into the shared
//! [`Outbox`] for the uploader.

use std::sync::Arc;
use std::thread;
use std::time::Duration;

use bridge_core::heater::HeaterSource;
use bridge_core::record::{EventKind, Health};
use bridge_core::recorder::Recorder;
use bridge_core::seq::SeqAllocator;

use crate::clock;
use crate::status_led::{HeaterStatus, StatusLed};
use crate::uploader::Outbox;
use crate::wifi;

const SAMPLE_INTERVAL_MS: u64 = 15_000;
const HEALTH_INTERVAL_MS: u64 = 5 * 60_000;
/// How often the thread wakes to check whether anything is due.
const TICK: Duration = Duration::from_secs(1);
/// Stack for the sampler thread. Mostly headroom for log formatting.
const TASK_STACK_SIZE: usize = 8 * 1024;

/// Spawns the sampler thread. It records the boot event first.
pub fn spawn(
    heater: Box<dyn HeaterSource>,
    seq: SeqAllocator,
    outbox: Arc<Outbox>,
    led: StatusLed,
    reset_reason: String,
) -> anyhow::Result<()> {
    thread::Builder::new()
        .name("sampler".into())
        .stack_size(TASK_STACK_SIZE)
        .spawn(move || run(heater, Recorder::new(seq), &outbox, &led, reset_reason))?;
    Ok(())
}

/// Body of the sampler thread. Never returns.
fn run(
    mut heater: Box<dyn HeaterSource>,
    mut recorder: Recorder,
    outbox: &Outbox,
    led: &StatusLed,
    reset_reason: String,
) {
    let boot = EventKind::Boot {
        reset_reason: reset_reason.clone(),
    };
    outbox.push([recorder.event(boot, clock::uptime_ms())]);

    let mut next_sample = clock::uptime_ms();
    // The first health record a minute after boot, once Wi-Fi is normally up (so it has an
    // RSSI), then every 5 minutes.
    let mut next_health = next_sample + 60_000;
    let mut time_synced = false;

    loop {
        let now = clock::uptime_ms();

        if !time_synced && clock::offset_ms().is_some() {
            time_synced = true;
            outbox.push([recorder.event(EventKind::TimeSynced, now)]);
        }

        if now >= next_sample {
            next_sample += SAMPLE_INTERVAL_MS;
            let result = heater.poll();
            led.set_heater_status(match &result {
                Ok(_) if heater.is_simulated() => HeaterStatus::Simulated,
                Ok(_) => HeaterStatus::Ok,
                Err(_) => HeaterStatus::Fault,
            });
            outbox.push(recorder.on_poll(result, now));
        }

        if now >= next_health {
            next_health += HEALTH_INTERVAL_MS;
            let health = Health {
                rssi_dbm: wifi::rssi_dbm(),
                // SAFETY: No preconditions.
                free_heap_bytes: unsafe { esp_idf_svc::sys::esp_get_free_heap_size() },
                buffer_depth: outbox.depth() as u32,
                reset_reason: reset_reason.clone(),
            };
            outbox.push([recorder.health(health, now)]);
        }

        thread::sleep(TICK);
    }
}
