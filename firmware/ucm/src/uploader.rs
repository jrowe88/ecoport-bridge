//! Uploads buffered records to the device API (`POST /device/v1/records`).
//!
//! Records leave the buffer only when the server acknowledges them, so a failed upload (no
//! network, wrong secret, clock not synced, server down) loses nothing: the same records go
//! again after a backoff. See `contracts/device-v1/README.md` for the request format.

use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use bridge_core::buffer::{RecordBuffer, DEFAULT_CAPACITY};
use bridge_core::record::Record;
use bridge_core::schedule::UploadSchedule;
use bridge_core::sign::sign_request;
use bridge_core::time::format_rfc3339_seconds;
use bridge_core::wire::{parse_upload_response, upload_body, UploadMeta};
use esp_idf_svc::http::client::{Configuration, EspHttpConnection};
use esp_idf_svc::http::Method;

use crate::clock;
use crate::status_led::{LedStatus, StatusLed};
use crate::wifi;

/// The upload endpoint's path. Also part of the signature.
const RECORDS_PATH: &str = "/device/v1/records";
/// Most records the server accepts in one upload.
const MAX_BATCH: usize = 100;
/// Largest body the server accepts.
const MAX_BODY_BYTES: usize = 64 * 1024;
/// Gives up on a request (connect, send or receive) after this long.
const HTTP_TIMEOUT: Duration = Duration::from_secs(10);
/// How often the thread checks whether an upload is due.
const TICK: Duration = Duration::from_secs(1);
/// How much of an error response body to log.
const LOG_BODY_BYTES: usize = 200;
/// Stack for the upload thread. TLS handshakes need much more than plain HTTP.
const TASK_STACK_SIZE: usize = 16 * 1024;

/// Records waiting to go, and when to send them. Shared by the sampler, which fills it, and
/// the uploader, which empties it.
pub struct Outbox {
    buffer: Mutex<RecordBuffer>,
    schedule: Mutex<UploadSchedule>,
}

impl Outbox {
    pub fn new() -> Self {
        Self {
            buffer: Mutex::new(RecordBuffer::new(DEFAULT_CAPACITY)),
            schedule: Mutex::new(UploadSchedule::new(clock::uptime_ms())),
        }
    }

    /// Queues records, asking for an upload straight away if any is an event.
    pub fn push(&self, records: impl IntoIterator<Item = Record>) {
        let mut any_event = false;
        {
            let mut buffer = self.buffer.lock().unwrap();
            for record in records {
                any_event |= record.is_event();
                buffer.push(record);
            }
        }
        if any_event {
            self.schedule.lock().unwrap().request_now();
        }
    }

    /// Records waiting to be uploaded.
    pub fn depth(&self) -> usize {
        self.buffer.lock().unwrap().len()
    }
}

impl Default for Outbox {
    fn default() -> Self {
        Self::new()
    }
}

/// What every upload from this boot has in common.
pub struct UploaderConfig {
    /// `http://host:port` or `https://host`, no trailing slash.
    pub base_url: String,
    pub device_id: String,
    pub secret: Vec<u8>,
    pub boot_id: String,
    pub firmware: String,
    pub simulated: bool,
}

/// Spawns the upload thread.
pub fn spawn(config: UploaderConfig, outbox: Arc<Outbox>, led: StatusLed) -> anyhow::Result<()> {
    thread::Builder::new()
        .name("uploader".into())
        .stack_size(TASK_STACK_SIZE)
        .spawn(move || run(&config, &outbox, &led))?;
    Ok(())
}

/// Body of the upload thread. Never returns.
fn run(config: &UploaderConfig, outbox: &Outbox, led: &StatusLed) {
    let url = format!("{}{RECORDS_PATH}", config.base_url.trim_end_matches('/'));
    let meta = UploadMeta {
        device_id: &config.device_id,
        boot_id: &config.boot_id,
        firmware: &config.firmware,
        simulated: config.simulated,
    };

    loop {
        thread::sleep(TICK);

        // Records get their UTC time at upload, so wait for the clock as well as the network.
        if !wifi::link_up() {
            continue;
        }
        let Some(offset_ms) = clock::offset_ms() else {
            continue;
        };
        let now = clock::uptime_ms();
        if !outbox.schedule.lock().unwrap().due(now) {
            continue;
        }

        let mut batch = outbox.buffer.lock().unwrap().batch(MAX_BATCH);
        if batch.is_empty() {
            outbox.schedule.lock().unwrap().on_success(now);
            continue;
        }
        let mut body = upload_body(&meta, &batch, offset_ms);
        // 100 records are far below 64 KiB, but never send something the server must reject.
        while body.len() > MAX_BODY_BYTES && batch.len() > 1 {
            batch.truncate(batch.len() / 2);
            body = upload_body(&meta, &batch, offset_ms);
        }
        let last_seq = batch.last().map_or(0, |r| r.seq);

        match post(config, &url, body.as_bytes(), offset_ms) {
            Ok(ack_seq) => {
                // Only ever delete what was sent, whatever the server claims.
                let ack_seq = ack_seq.min(last_seq);
                let (remaining, event_waiting) = {
                    let mut buffer = outbox.buffer.lock().unwrap();
                    buffer.ack(ack_seq);
                    (buffer.len(), buffer.contains_event())
                };
                {
                    let mut schedule = outbox.schedule.lock().unwrap();
                    schedule.on_success(clock::uptime_ms());
                    // Catch up straight away after an outage, or on an event that arrived
                    // during this upload.
                    if remaining >= MAX_BATCH || event_waiting {
                        schedule.request_now();
                    }
                }
                led.set_status(LedStatus::Connected);
                log::info!(
                    "Uploaded {} records, ackSeq={ack_seq}, {remaining} waiting",
                    batch.len()
                );
            }
            Err(e) => {
                outbox
                    .schedule
                    .lock()
                    .unwrap()
                    .on_failure(clock::uptime_ms(), random_jitter());
                led.set_status(LedStatus::UploadFailing);
                log::warn!(
                    "Upload of {} records failed, keeping them: {e:#}",
                    batch.len()
                );
            }
        }
    }
}

/// Sends one signed upload and returns the server's `ackSeq`.
///
/// Fails on transport errors, any status other than 200, and unparsable responses.
fn post(config: &UploaderConfig, url: &str, body: &[u8], offset_ms: i64) -> anyhow::Result<u32> {
    let timestamp = format_rfc3339_seconds(clock::uptime_ms() as i64 + offset_ms);
    let signature = sign_request(&config.secret, "POST", RECORDS_PATH, &timestamp, body);
    let content_length = body.len().to_string();
    let headers = [
        ("Content-Type", "application/json"),
        // A Content-Length makes the client send a plain body rather than chunked encoding.
        ("Content-Length", content_length.as_str()),
        ("X-Device-Id", config.device_id.as_str()),
        ("X-Timestamp", timestamp.as_str()),
        ("X-Signature", signature.as_str()),
    ];

    let mut conn = EspHttpConnection::new(&Configuration {
        timeout: Some(HTTP_TIMEOUT),
        // Trust the public CAs in ESP-IDF's certificate bundle for https:// URLs.
        crt_bundle_attach: if url.starts_with("https://") {
            Some(esp_idf_svc::sys::esp_crt_bundle_attach)
        } else {
            None
        },
        ..Default::default()
    })?;
    conn.initiate_request(Method::Post, url, &headers)?;
    conn.write_all(body)?;
    conn.initiate_response()?;

    let status = conn.status();
    let response = read_body(&mut conn)?;

    if status != 200 {
        let shown = &response[..response.len().min(LOG_BODY_BYTES)];
        anyhow::bail!(
            "HTTP {status}: {:?}",
            String::from_utf8_lossy(shown).as_ref()
        );
    }
    Ok(parse_upload_response(&response)?.ack_seq)
}

/// Reads the whole response body. Responses here are tiny; anything past 4 KiB is dropped.
fn read_body(conn: &mut EspHttpConnection) -> anyhow::Result<Vec<u8>> {
    const LIMIT: usize = 4096;
    let mut body = Vec::new();
    let mut chunk = [0u8; 512];
    loop {
        let n = conn.read(&mut chunk)?;
        if n == 0 {
            return Ok(body);
        }
        if body.len() < LIMIT {
            body.extend_from_slice(&chunk[..n.min(LIMIT - body.len())]);
        }
    }
}

/// A uniformly distributed value in `-1.0..=1.0` from the hardware random number generator.
fn random_jitter() -> f32 {
    // SAFETY: No preconditions. With Wi-Fi running, the values are truly random.
    let r = unsafe { esp_idf_svc::sys::esp_random() };
    (r as f64 / u32::MAX as f64 * 2.0 - 1.0) as f32
}
