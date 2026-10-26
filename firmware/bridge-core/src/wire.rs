//! The JSON bodies of `POST /device/v1/records` (`contracts/device-v1/`).
//!
//! The record types in [`crate::record`] are shaped for the device; the private structs here
//! are shaped like the contract, and serde turns them into camelCase JSON.

use anyhow::Context;
use serde::{Deserialize, Serialize};

use crate::record::{EventKind, Health, HeaterReading, LinkStats, Record, RecordBody};
use crate::time::format_rfc3339_millis;

/// The top-level fields of an upload that aren't records.
#[derive(Clone, Copy, Debug)]
pub struct UploadMeta<'a> {
    pub device_id: &'a str,
    /// 8 lowercase hex digits, random per boot.
    pub boot_id: &'a str,
    pub firmware: &'a str,
    /// True when the heater data is replayed rather than read from a heater.
    pub simulated: bool,
}

/// The body of a `200` response.
#[derive(Clone, Debug, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct UploadResponse {
    /// Every record with `seq` up to this one is stored and can be deleted.
    pub ack_seq: u32,
    pub server_time: String,
    pub commands_pending: bool,
}

/// Builds the request body for `records`.
///
/// Each record's `t` is its `uptime_ms + clock_offset_ms`, so `clock_offset_ms` is the Unix
/// time in milliseconds at boot, known once the clock is synced.
pub fn upload_body(meta: &UploadMeta, records: &[Record], clock_offset_ms: i64) -> String {
    let body = WireUpload {
        device_id: meta.device_id,
        boot_id: meta.boot_id,
        firmware: meta.firmware,
        simulated: meta.simulated,
        records: records
            .iter()
            .map(|r| WireRecord::new(r, clock_offset_ms))
            .collect(),
    };
    serde_json::to_string(&body).expect("upload body always serializes")
}

/// Parses the body of a `200` response.
pub fn parse_upload_response(body: &[u8]) -> anyhow::Result<UploadResponse> {
    serde_json::from_slice(body).context("Unexpected upload response body")
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireUpload<'a> {
    device_id: &'a str,
    boot_id: &'a str,
    firmware: &'a str,
    simulated: bool,
    records: Vec<WireRecord<'a>>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireRecord<'a> {
    seq: u32,
    kind: &'static str,
    uptime_ms: u64,
    t: String,
    #[serde(flatten)]
    body: WireBody<'a>,
}

impl<'a> WireRecord<'a> {
    fn new(record: &'a Record, clock_offset_ms: i64) -> Self {
        let (kind, body) = match &record.body {
            RecordBody::Sample { reading, link } => (
                "sample",
                WireBody::Sample(WireSample {
                    op_state: reading.op_state,
                    commodity: WireCommodity::from(reading),
                    link: WireLink::from(link),
                }),
            ),
            RecordBody::Event(event) => ("event", WireBody::Event(WireEvent::from(event))),
            RecordBody::Health(health) => ("health", WireBody::Health(WireHealth::from(health))),
        };

        Self {
            seq: record.seq,
            kind,
            uptime_ms: record.uptime_ms,
            t: format_rfc3339_millis(record.uptime_ms as i64 + clock_offset_ms),
            body,
        }
    }
}

/// The kind-specific fields, flattened into the record object.
#[derive(Serialize)]
#[serde(untagged)]
enum WireBody<'a> {
    Sample(WireSample),
    Event(WireEvent<'a>),
    Health(WireHealth<'a>),
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireSample {
    op_state: u8,
    commodity: WireCommodity,
    link: WireLink,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireCommodity {
    electricity_rate_w: u32,
    electricity_cumulative_wh: u32,
    total_storage_capacity_wh: u32,
    present_energy_take_wh: u32,
}

impl From<&HeaterReading> for WireCommodity {
    fn from(r: &HeaterReading) -> Self {
        Self {
            electricity_rate_w: r.electricity_rate_w,
            electricity_cumulative_wh: r.electricity_cumulative_wh,
            total_storage_capacity_wh: r.total_storage_capacity_wh,
            present_energy_take_wh: r.present_energy_take_wh,
        }
    }
}

#[derive(Serialize)]
struct WireLink {
    ok: bool,
    retries: u32,
    naks: u32,
}

impl From<&LinkStats> for WireLink {
    fn from(l: &LinkStats) -> Self {
        Self {
            ok: l.ok,
            retries: l.retries,
            naks: l.naks,
        }
    }
}

/// An event's `type` (the camelCase variant name) and `data` (the variant's fields).
///
/// Unit-like events are empty struct variants so that `data` is `{}` rather than missing.
#[derive(Serialize)]
#[serde(
    tag = "type",
    content = "data",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
enum WireEvent<'a> {
    Boot { reset_reason: &'a str },
    OpStateChanged { from: u8, to: u8 },
    TimeSynced {},
    HeaterLinkLost { error: &'a str },
    HeaterLinkRestored {},
}

impl<'a> From<&'a EventKind> for WireEvent<'a> {
    fn from(e: &'a EventKind) -> Self {
        match e {
            EventKind::Boot { reset_reason } => Self::Boot { reset_reason },
            EventKind::OpStateChanged { from, to } => Self::OpStateChanged {
                from: *from,
                to: *to,
            },
            EventKind::TimeSynced => Self::TimeSynced {},
            EventKind::HeaterLinkLost { error } => Self::HeaterLinkLost { error },
            EventKind::HeaterLinkRestored => Self::HeaterLinkRestored {},
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WireHealth<'a> {
    rssi_dbm: Option<i8>,
    free_heap_bytes: u32,
    buffer_depth: u32,
    reset_reason: &'a str,
}

impl<'a> From<&'a Health> for WireHealth<'a> {
    fn from(h: &'a Health) -> Self {
        Self {
            rssi_dbm: h.rssi_dbm,
            free_heap_bytes: h.free_heap_bytes,
            buffer_depth: h.buffer_depth,
            reset_reason: &h.reset_reason,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::record::{EventKind, Record, RecordBody};

    #[test]
    fn parses_response() {
        let r = parse_upload_response(br#"{"ackSeq":7,"serverTime":"x","commandsPending":false}"#)
            .unwrap();
        assert_eq!(r.ack_seq, 7);
        assert_eq!(r.server_time, "x");
        assert!(!r.commands_pending);
    }

    #[test]
    fn rejects_garbage_response() {
        assert!(parse_upload_response(b"<html>502 Bad Gateway</html>").is_err());
        assert!(parse_upload_response(b"").is_err());
        assert!(parse_upload_response(br#"{"serverTime":"x"}"#).is_err());
    }

    fn event_json(kind: EventKind) -> serde_json::Value {
        let meta = UploadMeta {
            device_id: "WH001",
            boot_id: "00000000",
            firmware: "0",
            simulated: false,
        };
        let record = Record {
            seq: 1,
            uptime_ms: 0,
            body: RecordBody::Event(kind),
        };
        let body: serde_json::Value =
            serde_json::from_str(&upload_body(&meta, &[record], 0)).unwrap();
        body["records"][0].clone()
    }

    #[test]
    fn event_types_and_data() {
        let boot = event_json(EventKind::Boot {
            reset_reason: "PowerOn".into(),
        });
        assert_eq!(boot["type"], "boot");
        assert_eq!(
            boot["data"],
            serde_json::json!({ "resetReason": "PowerOn" })
        );

        let synced = event_json(EventKind::TimeSynced);
        assert_eq!(synced["type"], "timeSynced");
        assert_eq!(synced["data"], serde_json::json!({}));

        let lost = event_json(EventKind::HeaterLinkLost {
            error: "timeout".into(),
        });
        assert_eq!(lost["type"], "heaterLinkLost");
        assert_eq!(lost["data"], serde_json::json!({ "error": "timeout" }));

        let restored = event_json(EventKind::HeaterLinkRestored);
        assert_eq!(restored["type"], "heaterLinkRestored");
        assert_eq!(restored["data"], serde_json::json!({}));
    }

    #[test]
    fn missing_rssi_is_null() {
        let meta = UploadMeta {
            device_id: "WH001",
            boot_id: "00000000",
            firmware: "0",
            simulated: false,
        };
        let record = Record {
            seq: 1,
            uptime_ms: 0,
            body: RecordBody::Health(crate::record::Health {
                rssi_dbm: None,
                free_heap_bytes: 1,
                buffer_depth: 2,
                reset_reason: "Software".into(),
            }),
        };
        let body: serde_json::Value =
            serde_json::from_str(&upload_body(&meta, &[record], 0)).unwrap();
        let health = &body["records"][0];
        assert!(health.as_object().unwrap().contains_key("rssiDbm"));
        assert!(health["rssiDbm"].is_null());
    }
}
