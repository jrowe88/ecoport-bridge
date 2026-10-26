# Device API v1 contract

The format the UCM firmware and the cloud Device API agree on. Both sides test against the
files in this folder, so change them deliberately and update both sides together. The design
background is in [`docs/architecture.md`](../../docs/architecture.md) §4 and §9.1.

| File | What it is |
|---|---|
| `upload-request.example.json` | A valid upload with one record of each kind |
| `upload-response.example.json` | The matching `200` response |
| `signing-vectors.json` | Known-good request signatures. Regenerate (see below) if `upload-request.example.json` changes |

## `POST /device/v1/records`

### Headers

| Header | Value |
|---|---|
| `Content-Type` | `application/json` |
| `X-Device-Id` | The device ID, e.g. `WH001` |
| `X-Timestamp` | Request time, RFC 3339 UTC with whole seconds: `2026-10-09T20:31:02Z` |
| `X-Signature` | Base64 HMAC-SHA256, see below |

### Signature

```text
canonical = METHOD + "\n" + PATH + "\n" + X-Timestamp + "\n" + lowercase_hex(SHA256(body))
signature = Base64( HMAC-SHA256( secret, UTF8(canonical) ) )
```

- `secret` is the device's 32-byte secret (exchanged as Base64).
- `PATH` is the URL path only, no scheme, host or query: `/device/v1/records`.
- `body` is the exact bytes sent. An empty body hashes to SHA256 of zero bytes.
- The server rejects the request if `X-Timestamp` is more than **5 minutes** from its clock.

### Body

Top level: `deviceId` (must equal `X-Device-Id`), `bootId` (8 lowercase hex digits, random per
boot), `firmware` (version string), `simulated` (true when the data is replayed, not from a
heater), `records` (array). All names are camelCase.

Every record has:

| Field | Type | Notes |
|---|---|---|
| `seq` | u32 | Strictly increasing within a request, and across requests from a device |
| `kind` | string | `sample`, `event` or `health` |
| `uptimeMs` | u64 | Milliseconds since boot when the record was made |
| `t` | string | RFC 3339 UTC with milliseconds, `2026-10-09T20:30:00.120Z`. Required in v1: the device does not upload until its clock is synced |

`sample` adds `opState` (int, CTA-2045 operational state), `commodity` (`electricityRateW`,
`electricityCumulativeWh`, `totalStorageCapacityWh`, `presentEnergyTakeWh`; raw values as the
heater reports them) and `link` (`ok`, `retries`, `naks`).

`event` adds `type` and `data` (an object, `{}` when there is nothing to add):

| `type` | `data` |
|---|---|
| `boot` | `{ "resetReason": string }` |
| `opStateChanged` | `{ "from": int, "to": int }` |
| `timeSynced` | `{}` |
| `heaterLinkLost` | `{ "error": string }` |
| `heaterLinkRestored` | `{}` |

`health` adds `rssiDbm` (int or `null`), `freeHeapBytes`, `bufferDepth`, `resetReason`.

### Validation (server)

A request is rejected with `400` if any of these fail:

- 1 to 100 records.
- `seq` strictly increasing within the request.
- `kind` is one of the three values; a `sample` has `opState` and `commodity`; an `event` has `type`.
- Every record has `t`, no more than 5 minutes in the future and no more than 30 days in the past.
- Body `deviceId` equals `X-Device-Id`.

### Responses

| Status | When | Body |
|---|---|---|
| `200` | All records stored | `{ "ackSeq", "serverTime", "commandsPending" }` |
| `400` | Not valid JSON, or validation failed | `{ "error": "<message>" }` |
| `401` | Unknown device, bad signature, or timestamp skew | Empty, on purpose |
| `413` | Body larger than 64 KiB | Empty |

`ackSeq` is the highest `seq` in the request; every record up to it is stored, so the device
can delete them. Sending the same request again is safe: the server writes the same rows and
returns the same `ackSeq`. `commandsPending` is always `false` in milestone 1.

## Regenerating `signing-vectors.json`

Run from the repository root (Python 3, standard library only):

```bash
python -I contracts/device-v1/make_signing_vectors.py > contracts/device-v1/signing-vectors.json
```
