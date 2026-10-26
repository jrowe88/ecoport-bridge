//! Checks the device side against the language-neutral contract in `contracts/device-v1/`.
//!
//! The cloud's tests read the same files, so passing here means both sides agree.

use std::collections::BTreeSet;

use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine;
use serde_json::Value;

use bridge_core::record::{EventKind, Health, HeaterReading, LinkStats, Record, RecordBody};
use bridge_core::sign::sign_request;
use bridge_core::wire::{upload_body, UploadMeta};

const SIGNING_VECTORS: &str = include_str!("../../../contracts/device-v1/signing-vectors.json");
const REQUEST_EXAMPLE: &str =
    include_str!("../../../contracts/device-v1/upload-request.example.json");

#[test]
fn signing_vectors_match() {
    let doc: Value = serde_json::from_str(SIGNING_VECTORS).unwrap();
    let secret = BASE64
        .decode(doc["secretBase64"].as_str().unwrap())
        .unwrap();
    let vectors = doc["vectors"].as_array().unwrap();
    assert_eq!(vectors.len(), 3);

    for v in vectors {
        let body = BASE64.decode(v["bodyBase64"].as_str().unwrap()).unwrap();
        let signature = sign_request(
            &secret,
            v["method"].as_str().unwrap(),
            v["path"].as_str().unwrap(),
            v["timestamp"].as_str().unwrap(),
            &body,
        );
        assert_eq!(signature, v["signature"].as_str().unwrap(), "vector {v}");
    }
}

/// The names of an object's members.
fn keys(value: &Value) -> BTreeSet<String> {
    value.as_object().unwrap().keys().cloned().collect()
}

/// The first record of `kind` (and, for events, `event_type`) in the contract example.
fn example_record<'a>(example: &'a Value, kind: &str, event_type: Option<&str>) -> &'a Value {
    example["records"]
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["kind"] == kind && event_type.is_none_or(|t| r["type"] == t))
        .unwrap()
}

#[test]
fn upload_body_matches_contract_shape() {
    let example: Value = serde_json::from_str(REQUEST_EXAMPLE).unwrap();

    let records = [
        Record {
            seq: 10,
            uptime_ms: 1_000,
            body: RecordBody::Sample {
                reading: HeaterReading {
                    op_state: 1,
                    electricity_rate_w: 242,
                    electricity_cumulative_wh: 0,
                    total_storage_capacity_wh: 12011,
                    present_energy_take_wh: 4202,
                },
                link: LinkStats {
                    ok: true,
                    retries: 0,
                    naks: 0,
                },
            },
        },
        Record {
            seq: 11,
            uptime_ms: 2_000,
            body: RecordBody::Event(EventKind::OpStateChanged { from: 1, to: 0 }),
        },
        Record {
            seq: 12,
            uptime_ms: 3_000,
            body: RecordBody::Health(Health {
                rssi_dbm: Some(-58),
                free_heap_bytes: 182_344,
                buffer_depth: 4,
                reset_reason: "PowerOn".into(),
            }),
        },
    ];
    let meta = UploadMeta {
        device_id: "WH001",
        boot_id: "c4e633a2",
        firmware: "0.2.0",
        simulated: true,
    };

    let body: Value =
        serde_json::from_str(&upload_body(&meta, &records, 1_760_041_799_120)).unwrap();

    assert_eq!(keys(&body), keys(&example));
    assert_eq!(body["deviceId"], "WH001");
    assert_eq!(body["bootId"], "c4e633a2");
    assert_eq!(body["simulated"], true);

    let out = body["records"].as_array().unwrap();
    assert_eq!(out.len(), 3);
    assert_eq!(out[0]["t"], "2025-10-09T20:30:00.120Z");

    let sample = example_record(&example, "sample", None);
    assert_eq!(out[0]["kind"], "sample");
    assert_eq!(keys(&out[0]), keys(sample));
    assert_eq!(keys(&out[0]["commodity"]), keys(&sample["commodity"]));
    assert_eq!(keys(&out[0]["link"]), keys(&sample["link"]));
    assert_eq!(out[0]["commodity"]["presentEnergyTakeWh"], 4202);

    let event = example_record(&example, "event", Some("opStateChanged"));
    assert_eq!(out[1]["kind"], "event");
    assert_eq!(out[1]["type"], "opStateChanged");
    assert_eq!(keys(&out[1]), keys(event));
    assert_eq!(out[1]["data"], event["data"]);

    let health = example_record(&example, "health", None);
    assert_eq!(out[2]["kind"], "health");
    assert_eq!(keys(&out[2]), keys(health));
    assert_eq!(out[2]["rssiDbm"], -58);
}
