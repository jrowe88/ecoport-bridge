# M1: Bench Reporting & Collection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An ESP32-C6 devkit replaying recorded Rinnai data uploads signed telemetry to an Azure Function, which stores it in Table Storage, and a Blazor/MudBlazor dashboard shows the current state and a 1-day chart.

**Architecture:** Device logic that doesn't touch hardware (records, sequence numbers, RAM buffer, JSON, HMAC signing, upload scheduling, replay source) goes in a new host-testable crate `firmware/bridge-core`; `firmware/ucm` only wires ESP-IDF pieces (Wi-Fi, SNTP, NVS, HTTP, LEDs) to it. Cargo features select the board (`board-devkit` / `board-rev-a`) and the heater source (`heater-replay` / `heater-rs485`), so moving from the devkit to the breadboard or the real heater is a flag change. The cloud is a C# Flex Consumption Function App (Device API) plus a Static Web App (Blazor WASM dashboard + managed-functions User API), sharing a `Contracts` library. A language-neutral contract folder (`contracts/device-v1/`) with example payloads and HMAC test vectors is the single source of truth both sides test against.

**Tech Stack:** Rust (std on ESP-IDF, `esp-idf-svc`), `serde`/`serde_json`, `hmac`/`sha2`/`base64`; .NET 10 isolated Azure Functions, `Azure.Data.Tables`, `Azure.Security.KeyVault.Secrets`, xUnit; Blazor WebAssembly + MudBlazor (PWA); Bicep; Azurite + Azure Functions Core Tools for local runs.

**Spec:** [`docs/architecture.md`](../../architecture.md) (sections 2–4, 7–9, 11), as amended by "Milestone decisions" below.

## Milestone decisions (deviations from the full architecture, M1 only)

| Topic | Full architecture | M1 |
|---|---|---|
| Heater data | CTA-2045 over RS-485 | Replay of `captures/rinnai/*/timeline.csv`, flagged `"simulated": true` |
| Derivation | Queue + processor | None. Device API updates `CurrentState` with latest **raw** values in-request. Stored-energy % is computed at read time in the User API |
| Outage buffering | RAM, flash spill after 15 min | RAM only, 256 records; when full drop oldest non-event record. Flash spill is M2 |
| Users | Sign-in + claim codes + `DeviceAccess` | SWA built-in auth; everything requires custom role `bench` (assigned by SWA invitation) |
| Device secrets | Provisioned into NVS | Compiled in from gitignored `firmware/ucm/src/config.rs`; server side in Key Vault (Azure) or `local.settings.json` (local) |
| Commands | Fetch on `commandsPending` | Not built. Server always returns `commandsPending: false` |
| Tables | Section 7 | `Devices`, `Telemetry`, `DeviceEvents`, `CurrentState` only |

## Global Constraints

- Device API base path: `/device/v1/` (Functions `host.json` `routePrefix` = `""`). User API base path: `/api/`.
- Signing: `Base64(HMAC-SHA256(secret, METHOD + "\n" + path + "\n" + X-Timestamp + "\n" + lowercase-hex(SHA256(body))))`; secret is 32 bytes; headers `X-Device-Id`, `X-Timestamp`, `X-Signature`.
- `X-Timestamp` is RFC 3339 UTC with seconds, e.g. `2026-10-09T20:31:02Z`; server rejects skew > 5 minutes either way.
- Record `t` is RFC 3339 UTC with milliseconds, e.g. `2026-10-09T20:30:00.120Z`. All JSON is camelCase.
- At most 100 records and 64 KiB per upload.
- Sample every 15 s; upload every 60 s or immediately after an event; health record every 5 min; upload backoff exponential from 5 s, capped at 300 s, ±20% jitter.
- Sequence high-water mark block size: 1024. NVS namespace `ecoport`, key `seq_hwm`.
- Table keys (exact): `Telemetry` PK `{deviceId}_{yyyyMMdd}`, RK `{HHmmssfff}_{seq:D10}`; `DeviceEvents` PK `{deviceId}`, RK `{yyyyMMddTHHmmssfff}_{seq:D10}`; `CurrentState` and `Devices` PK `DEVICE`, RK `{deviceId}` — all times UTC.
- Connectivity: `online` if last seen ≤ 3 min ago, `delayed` ≤ 15 min, else `offline`.
- .NET projects target `net10.0` except where Task 7 Step 1 forces a lower target for SWA managed functions.
- Rust firmware: keep the existing toolchain pins in `firmware/ucm/rust-toolchain.toml` and `.cargo/config.toml` (target dir `C:/t/ucm`). `bridge-core` and `cta2045` build on stable.
- Cost: Log Analytics daily cap 0.1 GB, 30-day retention; Flex Consumption instance memory 512 MB and the lowest allowed maximum instance count; Key Vault Standard; SWA Free; monthly budget alert of USD 5.
- Azure target is **only** the personal subscription `54c73415-2ebc-4270-8390-46985acfa7a7` ("Azure subscription 1", Default Directory). Every deploy script aborts unless `az account show --query id` equals it; never deploy to any other subscription (the machine also has a work tenant).
- Licenses: software AGPL-3.0-only (`license = "AGPL-3.0-only"` in Cargo, `<PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>` in a `cloud/Directory.Build.props` and `web/Directory.Build.props`).

## Review Focus

1. **Upload retried after a lost response** (same body sent twice): server must write identical rows, return the same `ackSeq`, and not move `CurrentState` backwards. Test: Task 6 `Ingest_SameRequestTwice_IsIdempotent`.
2. **Batch spanning UTC midnight**: records land in two `Telemetry` partitions; the table transaction must be split per partition. Test: Task 6 `Ingest_RecordsSpanningMidnight_WritesTwoPartitions`.
3. **Vacation mode** (`totalStorageCapacityWh` = 0, present in the panel-modes replay): stored-energy % must be `null`, not a divide-by-zero or `NaN`. Test: Task 7 `State_ZeroCapacity_StoredEnergyIsNull`.
4. **Long outage on the bench** (Wi-Fi off > 15 min): the RAM buffer fills; events must survive while samples are dropped, and records must never be removed without an `ackSeq`. Tests: Task 3 `buffer_full_drops_oldest_sample_not_event`, `failed_upload_keeps_records`.
5. **Device clock wrong or wrong secret** (SNTP not yet synced, or a mistyped secret in `config.rs`): server returns 401 with no detail, device backs off and keeps its records instead of losing them. Tests: Task 5 `Verify_TimestampSkewOver5Min_Rejected`, `Verify_WrongSecret_Rejected`; Task 3 `failed_upload_keeps_records`.

---

## Execution lanes

Task 1 is done first (the contract). Then four lanes run in parallel, each in its own git worktree, merged to `main` by the coordinator after review:

| Lane | Tasks | Depends on |
|---|---|---|
| A — firmware | 2, 3, 4 | 1 |
| B — device API | 5, 6 | 1 |
| C — dashboard | 7, 8 | 5 (Contracts project) |
| D — infra | 9 | 1 (names only) |
| Coordinator | 1, 10 | all |

---

### Task 1: Device API v1 contract

**Files:**
- Create: `contracts/device-v1/README.md`
- Create: `contracts/device-v1/upload-request.example.json`
- Create: `contracts/device-v1/upload-response.example.json`
- Create: `contracts/device-v1/signing-vectors.json`

**Interfaces:**
- Produces: the JSON shapes and signing vectors every other task tests against.

- [ ] **Step 1: Write `README.md`** describing: endpoint `POST /device/v1/records`; the headers and signing string from Global Constraints; status codes `200` (stored), `400` (schema invalid; body `{ "error": "<message>" }`), `401` (unknown device, bad signature or skew; empty body), `413` (body > 64 KiB); validation rules from Task 6 Step 3; that `ackSeq` is the highest `seq` in the request once all its records are stored.

- [ ] **Step 2: Write `upload-request.example.json`** — top-level `deviceId`, `bootId` (8 lowercase hex), `firmware`, `simulated` (bool), `records`. Record fields: `seq` (u32), `kind` (`sample` | `event` | `health`), `uptimeMs` (u64), `t` (string, required in M1). Sample adds `opState` (int), `commodity` {`electricityRateW`, `electricityCumulativeWh`, `totalStorageCapacityWh`, `presentEnergyTakeWh`}, `link` {`ok`, `retries`, `naks`}. Event adds `type` (`boot` | `opStateChanged` | `timeSynced` | `heaterLinkLost` | `heaterLinkRestored`) and `data` (object, may be `{}`). Health adds `rssiDbm` (int or null), `freeHeapBytes`, `bufferDepth`, `resetReason` (string). Include one of each kind, using values from the first rows of `captures/rinnai/2026-10-04T082724-heating-cycle-1/timeline.csv`.

- [ ] **Step 3: Write `upload-response.example.json`**: `{ "ackSeq": <last seq>, "serverTime": "<rfc3339 ms>", "commandsPending": false }`.

- [ ] **Step 4: Generate `signing-vectors.json`** with `contracts/device-v1/make_signing_vectors.py` (the reference implementation; equivalent to):

```bash
python -I - <<'EOF' > contracts/device-v1/signing-vectors.json
import base64, hashlib, hmac, json
secret = bytes(range(32))
body = open("contracts/device-v1/upload-request.example.json", "rb").read()
cases = [
  ("POST", "/device/v1/records", "2026-10-09T20:31:02Z", body),
  ("POST", "/device/v1/records", "2026-10-09T20:31:02Z", b""),
  ("GET",  "/device/v1/commands", "2026-01-01T00:00:00Z", b""),
]
out = []
for method, path, ts, b in cases:
    canon = f"{method}\n{path}\n{ts}\n{hashlib.sha256(b).hexdigest()}"
    sig = base64.b64encode(hmac.new(secret, canon.encode(), hashlib.sha256).digest()).decode()
    out.append({"method": method, "path": path, "timestamp": ts,
                "bodyBase64": base64.b64encode(b).decode(), "signature": sig})
print(json.dumps({"secretBase64": base64.b64encode(secret).decode(), "vectors": out}, indent=2))
EOF
```

Expected: file with 3 vectors. Note: `upload-request.example.json` must not change after this, or regenerate.

- [ ] **Step 5: Commit** `git add contracts && git commit -m "Add device API v1 contract and signing vectors"`.

---

### Task 2: `bridge-core` crate — time, records, JSON, signing

**Files:**
- Create: `firmware/bridge-core/Cargo.toml` (deps: `serde` with derive, `serde_json`, `hmac`, `sha2`, `base64`, `anyhow`; same `[package]` style as `firmware/cta2045/Cargo.toml`)
- Create: `firmware/bridge-core/src/lib.rs`, `time.rs`, `record.rs`, `wire.rs`, `sign.rs`
- Test: unit tests in each module; `firmware/bridge-core/tests/contract.rs`
- Modify: `.github/workflows/firmware.yml` (add a `bridge-core` job identical to `cta2045`), `firmware/README.md` (layout table)

**Interfaces:**
- Produces:
  - `time::format_rfc3339_millis(unix_ms: i64) -> String` and `time::format_rfc3339_seconds(unix_ms: i64) -> String`
  - `record::HeaterReading { op_state: u8, electricity_rate_w: u32, electricity_cumulative_wh: u32, total_storage_capacity_wh: u32, present_energy_take_wh: u32 }` (`Clone, Copy, Debug, PartialEq`)
  - `record::LinkStats { ok: bool, retries: u32, naks: u32 }`
  - `record::Health { rssi_dbm: Option<i8>, free_heap_bytes: u32, buffer_depth: u32, reset_reason: String }`
  - `record::EventKind { Boot { reset_reason: String }, OpStateChanged { from: u8, to: u8 }, TimeSynced, HeaterLinkLost { error: String }, HeaterLinkRestored }`
  - `record::RecordBody { Sample { reading: HeaterReading, link: LinkStats }, Event(EventKind), Health(Health) }`
  - `record::Record { seq: u32, uptime_ms: u64, body: RecordBody }` with `fn is_event(&self) -> bool`
  - `wire::UploadMeta<'a> { device_id: &'a str, boot_id: &'a str, firmware: &'a str, simulated: bool }`
  - `wire::upload_body(meta: &UploadMeta, records: &[Record], clock_offset_ms: i64) -> String` (`t` = `uptime_ms + clock_offset_ms` formatted)
  - `wire::UploadResponse { ack_seq: u32, server_time: String, commands_pending: bool }` + `wire::parse_upload_response(body: &[u8]) -> anyhow::Result<UploadResponse>`
  - `sign::sign_request(secret: &[u8], method: &str, path: &str, timestamp: &str, body: &[u8]) -> String`

- [ ] **Step 1: Write failing tests**
  - `time`: `format_rfc3339_millis(1_760_041_800_120) == "2025-10-09T20:30:00.120Z"`; `format_rfc3339_millis(0) == "1970-01-01T00:00:00.000Z"`; leap day `format_rfc3339_millis(1_709_164_800_000) == "2024-02-29T00:00:00.000Z"`; `format_rfc3339_seconds(1_760_041_862_999) == "2025-10-09T20:31:02Z"`.
  - `tests/contract.rs`: `signing_vectors_match` — load `../../contracts/device-v1/signing-vectors.json` via `include_str!`, decode, assert `sign_request(...) == signature` for every vector.
  - `tests/contract.rs`: `upload_body_matches_contract_shape` — build one sample, one `OpStateChanged` event, one health record; parse `upload_body(...)` with `serde_json::Value`; assert the key sets of the top level and of each record equal those in `upload-request.example.json` for the same `kind`, and `records[0]["t"] == "2025-10-09T20:30:00.120Z"` for `uptime_ms = 1_000`, `clock_offset_ms = 1_760_041_799_120`.
  - `wire`: `parse_upload_response(br#"{"ackSeq":7,"serverTime":"x","commandsPending":false}"#)` → `ack_seq == 7`; garbage → `Err`.

- [ ] **Step 2: Run** `cd firmware/bridge-core && cargo test` — expect compile failures.

- [ ] **Step 3: Implement.** Time formatting uses the days-from-civil algorithm (no `chrono`). JSON via private `#[derive(Serialize)]` wire structs with `#[serde(rename_all = "camelCase")]`; event `type` strings are the camelCase variant names; event `data` holds the variant's fields (`{}` for unit variants).

- [ ] **Step 4: Run** `cargo test && cargo clippy --all-targets -- -D warnings && cargo fmt --check` — all pass.

- [ ] **Step 5: Commit** `git commit -m "Add bridge-core: records, wire format, request signing"`.

---

### Task 3: `bridge-core` — sequence, recorder, buffer, scheduler, replay

**Files:**
- Create: `firmware/bridge-core/src/seq.rs`, `recorder.rs`, `buffer.rs`, `schedule.rs`, `heater.rs`, `replay.rs`
- Test: unit tests in each module; `firmware/bridge-core/tests/replay.rs`

**Interfaces:**
- Consumes: Task 2 `Record`, `RecordBody`, `EventKind`, `HeaterReading`, `LinkStats`.
- Produces:
  - `seq::SeqStore` trait: `fn load(&mut self) -> Option<u32>; fn save(&mut self, high_water_mark: u32) -> anyhow::Result<()>;` and `seq::SEQ_BLOCK: u32 = 1024`
  - `seq::SeqAllocator::new(store: Box<dyn SeqStore + Send>) -> Self`, `fn next(&mut self) -> u32` (save failures are logged with `log::warn!` and ignored)
  - `heater::HeaterSource` trait (`Send`): `fn poll(&mut self) -> anyhow::Result<HeaterReading>; fn is_simulated(&self) -> bool;`
  - `recorder::Recorder::new(seq: SeqAllocator) -> Self`; `fn on_poll(&mut self, result: anyhow::Result<HeaterReading>, uptime_ms: u64) -> Vec<Record>`; `fn event(&mut self, kind: EventKind, uptime_ms: u64) -> Record`; `fn health(&mut self, health: Health, uptime_ms: u64) -> Record`
  - `buffer::RecordBuffer::new(capacity: usize) -> Self`, `buffer::DEFAULT_CAPACITY: usize = 256`; `fn push(&mut self, r: Record)`, `fn batch(&self, max: usize) -> Vec<Record>` (oldest first, cloned), `fn ack(&mut self, ack_seq: u32)`, `fn len(&self) -> usize`
  - `schedule::UploadSchedule::new(now_ms: u64) -> Self`; `fn due(&self, now_ms: u64) -> bool`; `fn request_now(&mut self)`; `fn on_success(&mut self, now_ms: u64)` (next due +60 s, backoff reset); `fn on_failure(&mut self, now_ms: u64, jitter: f32)` (`jitter` in −1.0..=1.0 maps to ±20%)
  - `replay::ReplayHeater::from_csvs(csvs: &[&str]) -> anyhow::Result<Self>` implementing `HeaterSource` (`is_simulated() == true`, cycles forever)

- [ ] **Step 1: Write failing tests**
  - `seq`: fresh in-memory store → `next()` yields 0,1,2 and saves `1024` exactly once; after 1024 calls saves `2048`; a new allocator over a store holding `1024` starts at `1024`; a store whose `save` errors still yields increasing numbers.
  - `recorder`: first `Ok` reading → one sample, no event; opstate 1→0 → sample plus `OpStateChanged { from: 1, to: 0 }`; `Err` after `Ok` → `HeaterLinkLost` only (no sample); next `Ok` → `HeaterLinkRestored` plus sample; seq values strictly increasing across all returned records.
  - `buffer`: `ack(5)` removes seq ≤ 5 only; `buffer_full_drops_oldest_sample_not_event` (capacity 3: event, sample, sample, then push → oldest *sample* dropped, event kept); all-events buffer drops the oldest event; `failed_upload_keeps_records` (`batch()` then no `ack` → `len()` unchanged).
  - `schedule`: due at start; after `on_success(0)` not due at 59 999, due at 60 000; failures with jitter 0 back off 5 s, 10 s, 20 s … capped at 300 s; jitter 1.0 on the first failure gives 6 s; `request_now()` makes it due immediately; `on_success` resets backoff.
  - `tests/replay.rs`: `ReplayHeater::from_csvs(&[include_str!("../../../captures/rinnai/2026-10-04T082724-heating-cycle-1/timeline.csv")])` → first poll `op_state 1, electricity_rate_w 242, total_storage_capacity_wh 12011, present_energy_take_wh 4202, electricity_cumulative_wh 0`; polling 120 times wraps back to the first row; the panel-modes CSV parses and contains a row with capacity 0.

- [ ] **Step 2: Run** `cargo test` — failures.

- [ ] **Step 3: Implement.** Replay parsing: skip the header; per line take the first five comma-separated fields; opstate is the leading integer of field 2 (`"1 Running Normal"` → 1); ignore later fields (free-text notes).

- [ ] **Step 4: Run** `cargo test && cargo clippy --all-targets -- -D warnings && cargo fmt --check` — pass.

- [ ] **Step 5: Commit** `git commit -m "Add sequence allocator, recorder, RAM buffer, upload schedule, replay heater"`.

---

### Task 4: `ucm` — board/heater features, config, sampling and uploading

**Files:**
- Modify: `firmware/ucm/Cargo.toml` (features; deps `bridge-core = { path = "../bridge-core" }`, `serde_json`)
- Create: `firmware/ucm/src/board.rs`, `config.example.rs`, `heater.rs`, `clock.rs`, `nvs_seq.rs`, `uploader.rs`, `sampler.rs`
- Modify: `firmware/ucm/src/main.rs`, `status_led.rs`, `ws2812.rs`, `rs485.rs`, `build.rs`
- Delete: `firmware/ucm/src/wifi_credentials.example.rs` (replaced by `config.example.rs`)
- Modify: `.gitignore` (`firmware/ucm/src/config.rs` replaces `wifi_credentials.rs`), `.github/workflows/firmware.yml` (copy `config.example.rs`; add a matrix build for `--no-default-features --features board-rev-a,heater-rs485`), `firmware/README.md`

**Interfaces:**
- Consumes: everything Task 2 and 3 produce.
- Produces (for the bench and later milestones):
  - Features: `default = ["board-devkit", "heater-replay"]`; `board-devkit`, `board-rev-a`, `heater-replay`, `heater-rs485`. `compile_error!` in `board.rs` / `heater.rs` unless exactly one of each group is enabled.
  - `board::LED_COUNT: usize` (1 devkit, 2 rev-A). LED data GPIO8 and RS-485 UART1 TX 18 / RX 19 / DE 20 on both boards.
  - `config.rs` constants: `WIFI_SSID`, `WIFI_PASSWORD`, `API_BASE_URL` (e.g. `http://192.168.1.50:7071` or `https://<app>.azurewebsites.net`), `DEVICE_ID`, `DEVICE_SECRET_B64`.
  - `heater::open(...) -> anyhow::Result<Box<dyn HeaterSource>>`: replay embeds heating-cycle-1 and panel-modes-1 via `include_str!`; rs485 returns a source whose `poll` is `Err("CTA-2045 polling not implemented yet")` and which starts the existing receive-only monitor.
  - `Ws2812::set_colors(&mut self, colors: &[Rgb])`; `StatusLed` drives `LED_COUNT` LEDs: LED 0 shows network/upload status (existing patterns plus new `LedStatus::UploadFailing`, amber breathe); LED 1, when present, shows heater status (`HeaterStatus::Simulated` dim purple, `Ok` dim green, `Fault` red).

- [ ] **Step 1: Features and config.** Add features and `board.rs`; replace `wifi_credentials` with `config` (update `build.rs` existence check and message). Verify both `cargo build --release` and `cargo build --release --no-default-features --features board-rev-a,heater-rs485` compile, and that enabling both board features fails with the `compile_error!` message.

- [ ] **Step 2: LEDs.** Extend `ws2812.rs` to N LEDs and `status_led.rs` to `LED_COUNT` LEDs as specified above. Verify on the devkit: blue pulse while connecting, green breathe when connected.

- [ ] **Step 3: Clock and sequence persistence.** `clock.rs`: start `EspSntp`; `fn offset_ms() -> Option<i64>` (Unix ms minus uptime ms once synced), `fn uptime_ms() -> u64`. `nvs_seq.rs`: `NvsSeqStore` implementing `SeqStore` on namespace `ecoport`, key `seq_hwm`. Boot record: `EventKind::Boot { reset_reason }` from `esp_reset_reason()`; `bootId` = 8 hex digits from `esp_random()`.

- [ ] **Step 4: Sampler thread** (`sampler.rs`): every 15 s call `HeaterSource::poll`, feed `Recorder::on_poll`, push records into a shared `Arc<Mutex<RecordBuffer>>`, call `UploadSchedule::request_now()` if any record is an event; every 5 min push a health record (RSSI, `esp_get_free_heap_size`, buffer depth, reset reason). Update the heater LED status.

- [ ] **Step 5: Uploader thread** (`uploader.rs`): loop every second; skip until Wi-Fi is up and `offset_ms()` is `Some`; when `UploadSchedule::due`, take `batch(100)`, build the body with `upload_body`, sign with `sign_request` over path `/device/v1/records`, POST with `EspHttpConnection` (`crt_bundle_attach` for `https://`, plain for `http://`, 10 s timeout). On HTTP 200 with a parsable body: `ack(ack_seq)`, `on_success`, LED 0 connected. Anything else (transport error, non-200, unparsable): `on_failure` with jitter from `esp_random()`, keep records, LED 0 `UploadFailing`, log status code and response body (first 200 bytes).

- [ ] **Step 6: Verify** `cargo fmt --check && cargo clippy --release -- -D warnings && cargo build --release` for the default features and for `board-rev-a,heater-rs485`.

- [ ] **Step 7: Bench check (needs Task 6 running locally):** set `API_BASE_URL` to the PC's LAN address, `cargo run --release`; serial log shows `Uploaded N records, ackSeq=…` every ~60 s, and a 401 when `DEVICE_SECRET_B64` is deliberately wrong (records retained, backoff growing).

- [ ] **Step 8: Commit** `git commit -m "ucm: board/heater features, replay sampling, signed uploads"`.

---

### Task 5: Cloud solution, Contracts, request authentication

**Files:**
- Create: `cloud/EcoPort.Cloud.slnx`, `cloud/Directory.Build.props` (nullable, implicit usings, warnings as errors, license), `cloud/.gitignore` additions to root `.gitignore` (`bin/`, `obj/`, `local.settings.json`)
- Create: `cloud/src/EcoPort.Cloud.Contracts/` — `UploadRequest.cs`, `DeviceRecord.cs`, `UploadResponse.cs`, `DeviceSummaryDto.cs`, `DeviceStateDto.cs`, `TelemetryPointDto.cs`, `JsonDefaults.cs`
- Create: `cloud/src/EcoPort.Cloud.Core/` — `Auth/DeviceRequestSigner.cs`, `Auth/IDeviceSecretStore.cs`, `Auth/ConfigDeviceSecretStore.cs`, `Auth/KeyVaultDeviceSecretStore.cs`
- Test: `cloud/tests/EcoPort.Cloud.Tests/` (xUnit) — `ContractTests.cs`, `DeviceRequestSignerTests.cs`
- Create: `.github/workflows/cloud.yml` (on `cloud/**`, `contracts/**`: `dotnet build` + `dotnet test`)

**Interfaces:**
- Produces:
  - Contracts (records, `System.Text.Json`, camelCase via `JsonDefaults.Options`): `UploadRequest(string DeviceId, string BootId, string Firmware, bool Simulated, IReadOnlyList<DeviceRecord> Records)`; `DeviceRecord` flat with nullable kind-specific members: `uint Seq, string Kind, ulong UptimeMs, DateTimeOffset? T, int? OpState, CommodityDto? Commodity, LinkDto? Link, string? Type, JsonElement? Data, int? RssiDbm, uint? FreeHeapBytes, uint? BufferDepth, string? ResetReason`; `UploadResponse(uint AckSeq, DateTimeOffset ServerTime, bool CommandsPending)`; `DeviceSummaryDto(string DeviceId, DateTimeOffset? LastSeen, string Connectivity, bool Simulated)`; `DeviceStateDto(string DeviceId, DateTimeOffset? LastSeen, string Connectivity, bool Simulated, DateTimeOffset? SampleTime, int? OpState, int? ElectricityRateW, int? TotalStorageCapacityWh, int? PresentEnergyTakeWh, double? StoredEnergyPercent)`; `TelemetryPointDto(DateTimeOffset T, int OpState, int ElectricityRateW, double? StoredEnergyPercent)`.
  - `DeviceRequestSigner.ComputeSignature(byte[] secret, string method, string path, string timestamp, ReadOnlySpan<byte> body) -> string`
  - `DeviceRequestSigner.Verify(byte[]? secret, string method, string path, string? timestamp, string? signature, ReadOnlySpan<byte> body, DateTimeOffset now) -> bool` (false for null secret, unparsable or > 5 min skew, mismatch; constant-time compare)
  - `IDeviceSecretStore.GetSecretAsync(string deviceId, CancellationToken ct) -> Task<byte[]?>`; `ConfigDeviceSecretStore` reads `DeviceSecrets:{deviceId}` (base64) from `IConfiguration`; `KeyVaultDeviceSecretStore` reads secret `device-{deviceId}` and caches 10 min.

- [ ] **Step 1: Write failing tests**
  - `ContractTests.UploadRequestExample_RoundTrips`: deserialize `contracts/device-v1/upload-request.example.json` (copied to test output via `<None Include="../../../contracts/device-v1/*.json" CopyToOutputDirectory="PreserveNewest" />`) and re-serialize with `JsonDefaults.Options`; every record keeps its `seq`, `kind` and `t`.
  - `DeviceRequestSignerTests.SigningVectors_Match`: every vector in `signing-vectors.json`.
  - `Verify_ValidSignature_Accepted` (now = timestamp + 4 min 59 s); `Verify_TimestampSkewOver5Min_Rejected` (+5 min 1 s and −5 min 1 s); `Verify_WrongSecret_Rejected`; `Verify_NullSecret_Rejected`; `Verify_MissingHeaders_Rejected`.

- [ ] **Step 2: Run** `dotnet test cloud/EcoPort.Cloud.slnx` — failures.

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run** `dotnet test cloud/EcoPort.Cloud.slnx` — pass.

- [ ] **Step 5: Commit** `git commit -m "Add cloud solution, contracts and device request signing"`.

---

### Task 6: Device Function App — `POST /device/v1/records`

**Files:**
- Create: `cloud/src/EcoPort.Cloud.Core/Storage/` — `TableNames.cs`, `TableKeys.cs`, `Entities.cs` (`TelemetryEntity`, `DeviceEventEntity`, `CurrentStateEntity`, `DeviceEntity`), `ITableStore.cs`, `AzureTableStore.cs`
- Create: `cloud/src/EcoPort.Cloud.Core/Ingest/UploadValidator.cs`, `Ingest/RecordIngestor.cs`
- Create: `cloud/src/EcoPort.Cloud.Device/` — `Program.cs`, `host.json` (`"extensions": { "http": { "routePrefix": "" } }`), `RecordsFunction.cs`, `local.settings.example.json`
- Create: `tools/fake_device.py` (stdlib only)
- Test: `cloud/tests/EcoPort.Cloud.Tests/TableKeysTests.cs`, `UploadValidatorTests.cs`, `RecordIngestorTests.cs` (with an in-memory `FakeTableStore`)

**Interfaces:**
- Consumes: Task 5 Contracts, `DeviceRequestSigner`, `IDeviceSecretStore`.
- Produces:
  - `TableKeys.Telemetry(string deviceId, DateTimeOffset t, uint seq) -> (string Pk, string Rk)`, `TableKeys.DeviceEvent(...)` same shape — formats per Global Constraints.
  - `ITableStore`: `Task UpsertBatchAsync(string table, IReadOnlyList<ITableEntity> entities, CancellationToken ct)` (one partition per call, ≤ 100); `Task<CurrentStateEntity?> GetCurrentStateAsync(string deviceId, CancellationToken ct)`; `Task<bool> TryUpsertCurrentStateAsync(CurrentStateEntity entity, ETag ifMatch, CancellationToken ct)` (false on 412); `Task<DeviceEntity?> GetDeviceAsync(string deviceId, CancellationToken ct)`; `IAsyncEnumerable<DeviceEntity> ListDevicesAsync(CancellationToken ct)`; `IAsyncEnumerable<TelemetryEntity> QueryTelemetryAsync(string deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)`.
  - `CurrentStateEntity` properties: `LastSeen`, `LastSeq`, `BootId`, `Firmware`, `Simulated`, `SampleTime`, `OpState`, `ElectricityRateW`, `TotalStorageCapacityWh`, `PresentEnergyTakeWh`.
  - `UploadValidator.Validate(UploadRequest req, string headerDeviceId, DateTimeOffset now) -> string?` (null = valid, else error message).
  - `RecordIngestor.IngestAsync(UploadRequest req, DateTimeOffset now, CancellationToken ct) -> Task<UploadResponse>`.

- [ ] **Step 1: Write failing tests**
  - `TableKeysTests`: `t = 2026-10-09T20:30:00.120Z, seq 12783, device WH001` → Telemetry `("WH001_20261009", "203000120_0000012783")`, DeviceEvent `("WH001", "20261009T203000120_0000012783")`; a non-UTC offset input is converted to UTC first.
  - `UploadValidatorTests` (one test each): 0 records; 101 records; non-increasing `seq`; unknown `kind`; sample missing `opState` or `commodity`; event missing `type`; missing `t`; `t` more than 5 min in the future; `t` older than 30 days; body `deviceId` ≠ header → each returns a non-null message; the contract example is valid.
  - `RecordIngestorTests`: samples go to `Telemetry`, events and health to `DeviceEvents`; `Ingest_SameRequestTwice_IsIdempotent` (same rows, same `AckSeq`, `CurrentState` unchanged on the second call); `Ingest_RecordsSpanningMidnight_WritesTwoPartitions` (23:59:50Z and 00:00:05Z → two `UpsertBatchAsync` calls); `Ingest_OlderSeq_DoesNotRegressCurrentState`; `CurrentState` takes the latest sample's raw values and `LastSeen = now`; `TryUpsertCurrentStateAsync` returning false once triggers one re-read and retry; `AckSeq` = max `seq`; `CommandsPending` false.

- [ ] **Step 2: Run** `dotnet test` — failures.

- [ ] **Step 3: Implement** `UploadValidator`, `RecordIngestor`, `AzureTableStore` (`TableServiceClient` from `Storage:ConnectionString` if set, else `Storage:TableEndpoint` + `DefaultAzureCredential`; create the four tables on first use).

- [ ] **Step 4: Implement `RecordsFunction`** (`[Function("Records")]`, `[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device/v1/records")]`): reject > 64 KiB with 413; read `X-Device-Id`; look up `Devices` row (missing → 401) and secret; `Verify` over method, `req.Url.AbsolutePath`, headers and raw body bytes (fail → 401, empty body); deserialize (fail → 400); validate (fail → 400 `{ "error": … }`); ingest; 200 with `UploadResponse`. `Program.cs` registers `ConfigDeviceSecretStore` when `KeyVault:Uri` is empty, else `KeyVaultDeviceSecretStore`, plus Application Insights worker service.

- [ ] **Step 5: Write `tools/fake_device.py`**: args `--url`, `--device-id`, `--secret-b64`, `--csv` (default heating-cycle-1 timeline), `--interval` (default 60), `--batch` (default 4), `--once`; builds requests exactly as in the contract (seq starting at a random base, `simulated: true`), signs, POSTs, prints status and `ackSeq`.

- [ ] **Step 6: Run locally.** `npx azurite --silent --location %TEMP%\azurite` in one terminal; copy `local.settings.example.json` → `local.settings.json` with `Storage:ConnectionString = UseDevelopmentStorage=true`, `DeviceSecrets:WH001 = <base64>`; add a `Devices` row for WH001 (the function does not auto-register devices — document the one-line `az storage entity insert --connection-string UseDevelopmentStorage=true …` command in `cloud/README.md`); `func start` in `cloud/src/EcoPort.Cloud.Device`. Then `python tools/fake_device.py --url http://localhost:7071 --device-id WH001 --secret-b64 <base64> --once` → `200 ackSeq=…`; with a wrong secret → `401`. Requires Azure Functions Core Tools v4 (`npm i -g azure-functions-core-tools@4` if `func` is missing).

- [ ] **Step 7: Run** `dotnet test` — all pass. **Commit** `git commit -m "Add device records endpoint with table ingest and fake device"`.

---

### Task 7: User API (SWA managed functions)

**Files:**
- Create: `cloud/src/EcoPort.Cloud.Api/` — `Program.cs`, `host.json`, `DevicesFunctions.cs`, `local.settings.example.json`
- Create: `cloud/src/EcoPort.Cloud.Core/Read/Connectivity.cs`, `Read/StoredEnergy.cs`, `Read/TelemetryDownsampler.cs`
- Test: `cloud/tests/EcoPort.Cloud.Tests/ConnectivityTests.cs`, `StoredEnergyTests.cs`, `TelemetryDownsamplerTests.cs`, `DevicesFunctionsTests.cs`

**Interfaces:**
- Consumes: Task 5 DTOs; Task 6 `ITableStore`, entities.
- Produces (HTTP, consumed by Task 8):
  - `GET /api/devices` → `DeviceSummaryDto[]`
  - `GET /api/devices/{deviceId}/state` → `DeviceStateDto` (404 if unknown)
  - `GET /api/devices/{deviceId}/telemetry?from=&to=` (ISO 8601; default last 24 h; range > 24 h → 400) → `TelemetryPointDto[]`, at most 500 points
  - `Connectivity.From(DateTimeOffset? lastSeen, DateTimeOffset now) -> string`; `StoredEnergy.Percent(int capacityWh, int takeWh) -> double?` = `(1 − take/capacity) × 100`, clamped 0–100, `null` when capacity ≤ 0; `TelemetryDownsampler.Downsample(IReadOnlyList<TelemetryPointDto> points, int maxPoints) -> IReadOnlyList<TelemetryPointDto>` (equal time buckets; mean W and %, last opstate; unchanged if already ≤ max).

- [ ] **Step 1: Pick the runtime.** Check the currently supported `platform.apiRuntime` values for SWA managed functions in Microsoft's docs. Use `dotnet-isolated:10.0` if listed; otherwise the highest listed `dotnet-isolated` version, and multi-target `EcoPort.Cloud.Contracts` and `EcoPort.Cloud.Core` to include that TFM. Record the choice in `cloud/README.md`.

- [ ] **Step 2: Write failing tests**: `Connectivity` at 3:00 → online, 3:01 → delayed, 15:00 → delayed, 15:01 → offline, null → offline; `State_ZeroCapacity_StoredEnergyIsNull`; `StoredEnergy.Percent(12011, 4202)` ≈ 65.02; take > capacity → 0; downsampler 5,760 points → 500 with first/last bucket times in range; `DevicesFunctionsTests` with `FakeTableStore`: unknown device → 404, 25-hour range → 400.

- [ ] **Step 3: Run** `dotnet test` — failures. **Step 4: Implement.** Storage settings as in Task 6 (connection string only — managed functions cannot use managed identity).

- [ ] **Step 5: Run** `dotnet test` — pass. **Commit** `git commit -m "Add user API for device list, state and telemetry"`.

---

### Task 8: Blazor WASM + MudBlazor dashboard (PWA)

**Files:**
- Create: `web/EcoPort.Web/` via `dotnet new blazorwasm --pwa --empty -o web/EcoPort.Web` (target per Task 7 Step 1 if it lowered Contracts' TFM; otherwise `net10.0`), add MudBlazor (latest stable) and a project reference to `EcoPort.Cloud.Contracts`
- Create: `web/EcoPort.Web/Services/EcoPortApiClient.cs`, `Pages/Devices.razor`, `Pages/Device.razor`, `wwwroot/staticwebapp.config.json`, `wwwroot/login.html`
- Create: `web/Directory.Build.props`
- Test: `web/EcoPort.Web.Tests/` (xUnit + bUnit) — `DevicePageTests.cs`
- Create: `.github/workflows/web.yml` (on `web/**`, `cloud/src/EcoPort.Cloud.Api/**`, `cloud/src/EcoPort.Cloud.Contracts/**`, `cloud/src/EcoPort.Cloud.Core/**`: build + test; deploy job added in Task 9)

**Interfaces:**
- Consumes: Task 7 HTTP endpoints and DTOs.
- Produces: `EcoPortApiClient.GetDevicesAsync()`, `GetStateAsync(string deviceId)`, `GetTelemetryAsync(string deviceId, DateTimeOffset from, DateTimeOffset to)` (relative URLs, `HttpClient` base = host).

- [ ] **Step 1: Write failing bUnit tests** (`EcoPortApiClient` behind an interface `IEcoPortApi` for faking): state with `StoredEnergyPercent = 65.0` renders "65%"; `StoredEnergyPercent = null` renders "—" plus the text "vacation or no data"; `Simulated = true` renders a "Simulated" chip; connectivity `offline` renders an error-colored chip.

- [ ] **Step 2: Run** `dotnet test web` — failures.

- [ ] **Step 3: Implement.** `Devices.razor` (`/`): MudTable of devices with connectivity chips linking to `/devices/{id}`. `Device.razor` (`/devices/{DeviceId}`): MudCards for power (W), stored energy (%), opstate (0 Idle, 1 Running, others as number), last seen + connectivity, Simulated chip; MudChart line for power and stored energy over the last 24 h; refresh every 30 s with a `PeriodicTimer`, disposed with the component. `staticwebapp.config.json`: `/login.html` and `/.auth/*` anonymous; every other route (including `/api/*`) `allowedRoles: ["bench"]`; `responseOverrides` 401 → redirect `/login.html`, 403 → `/login.html`; `navigationFallback` → `/index.html` excluding `/api/*`, static files; `platform.apiRuntime` from Task 7 Step 1. `login.html`: links to `/.auth/login/aad` and `/.auth/login/github`.

- [ ] **Step 4: Run** `dotnet test web` — pass. Local run: `npx @azure/static-web-apps-cli start http://localhost:5000 --api-location http://localhost:7072` with the API running via `func start --port 7072`, Blazor via `dotnet watch`; log in through the emulator with role `bench`; the page shows the data sent by `tools/fake_device.py` against Azurite.

- [ ] **Step 5: Commit** `git commit -m "Add Blazor/MudBlazor dashboard"`.

---

### Task 9: Bicep infrastructure and deploy scripts

**Files:**
- Create: `infra/bicep/main.bicep`, `infra/bicep/modules/` (`storage.bicep`, `monitoring.bicep`, `keyvault.bicep`, `functionapp.bicep`, `staticwebapp.bicep`, `budget.bicep`), `infra/bicep/dev.bicepparam`
- Create: `infra/deploy-infra.ps1`, `infra/deploy-device-api.ps1`, `infra/deploy-web.ps1`, `infra/provision-device.ps1`, `infra/README.md`
- Modify: `.github/workflows/cloud.yml` (add `az bicep build` + `az bicep lint` step for `infra/**`)

**Interfaces:**
- Consumes: app settings names from Tasks 5–7 (`Storage:TableEndpoint` as `Storage__TableEndpoint`, `KeyVault__Uri`, `APPLICATIONINSIGHTS_CONNECTION_STRING`), table names from Task 6.
- Produces: resource-group deployment with params `env` (`dev`), `location`, `swaLocation` (default `centralus`), `adminPrincipalId` (the user's object id), `budgetEmail`, `budgetStartDate` (first of current month); outputs `functionAppName`, `functionAppHostName`, `staticWebAppName`, `keyVaultName`, `storageAccountName`.

- [ ] **Step 1: Write the modules**:
  - Storage account `Standard_LRS`, TLS 1.2, no public blob access, shared-key access enabled only because SWA managed functions need a connection string; tables `Devices`, `Telemetry`, `DeviceEvents`, `CurrentState`; blob container `deployments`.
  - Log Analytics (`workspaceCapping.dailyQuotaGb: json('0.1')`, retention 30 days) + workspace-based Application Insights; adaptive sampling stays on in the worker SDK.
  - Key Vault Standard, RBAC mode, soft delete on, purge protection off (dev).
  - Flex Consumption plan `FC1` + Function App: `dotnet-isolated` `10.0`, instance memory 512, `maximumInstanceCount` at the lowest allowed value, system-assigned identity, deployment storage = `deployments` container with system identity, app settings above, HTTPS only, FTP off.
  - Static Web App `Free` in `swaLocation`; app setting `Storage__ConnectionString` from the storage account keys.
  - Role assignments: Function App identity → Storage Table Data Contributor, Storage Blob Data Owner (deployment container), Storage Queue Data Contributor, Key Vault Secrets User; `adminPrincipalId` → Key Vault Secrets Officer and Storage Table Data Contributor.
  - Budget (`Microsoft.Consumption/budgets`, resource-group scope): USD 5 monthly, alerts at 80% actual and 100% forecast to `budgetEmail`.

- [ ] **Step 2: Verify offline**: `az bicep build --file infra/bicep/main.bicep` and `az bicep lint --file infra/bicep/main.bicep` — no errors or warnings.

- [ ] **Step 3: Write scripts.** Each script has `-SubscriptionId` defaulting to `54c73415-2ebc-4270-8390-46985acfa7a7`, and starts by comparing it with `az account show --query id -o tsv`; on mismatch it prints both and exits non-zero (no prompt, no `az account set`).
  - `deploy-infra.ps1 -Env dev -ResourceGroup rg-ecoport-dev -Location <region>`: creates the RG if missing, runs `az deployment group what-if` then `create` with `dev.bicepparam`.
  - `deploy-device-api.ps1`: `dotnet publish -c Release` the Device project, zip, `az functionapp deployment source config-zip`.
  - `deploy-web.ps1`: `dotnet publish` the web app; `swa deploy` with the token from `az staticwebapp secrets list`, app = publish `wwwroot`, api = Api project publish folder.
  - `provision-device.ps1 -Env dev -DeviceId WH001`: 32 random bytes → `az keyvault secret set --name device-WH001`; `az storage entity insert --auth-mode login` into `Devices` (`PartitionKey=DEVICE`, `RowKey=WH001`, `CreatedAt`); prints the `DEVICE_ID`, `DEVICE_SECRET_B64` and `API_BASE_URL` lines for `config.rs`. Never writes the secret to disk.
  - `infra/README.md`: order of operations, how to invite yourself to role `bench` (`az staticwebapp users invite --roles bench …`), and the subscription guard.
  - Agents in this lane do **not** run `what-if` or any deployment; the coordinator does that in Task 10.

- [ ] **Step 4: Commit** `git commit -m "Add Bicep infrastructure and deploy scripts"`.

---

### Task 10: Integration and bench bring-up (coordinator, with the user)

- [ ] **Step 1: Merge lanes** A–D into `main`; run `cargo test` (cta2045, bridge-core), both `ucm` feature builds, `dotnet test cloud/EcoPort.Cloud.slnx`, `dotnet test web`, `az bicep build`.
- [ ] **Step 2: Local end-to-end:** Azurite + Device API on the PC (`func start` reachable on the LAN; allow Windows Firewall for port 7071) → devkit with `API_BASE_URL=http://<pc-lan-ip>:7071` → dashboard via SWA CLI shows the replayed heating cycle updating every minute.
- [ ] **Step 3: Azure** (personal subscription only; confirm with the user before the first `deploy-infra.ps1`, since it creates billable resources): `deploy-infra.ps1`, `deploy-device-api.ps1`, `provision-device.ps1`, `deploy-web.ps1`, invite the user to role `bench`; switch `API_BASE_URL` to the Function App's HTTPS host; confirm the dashboard shows the devkit within 2 minutes.
- [ ] **Step 4: Failure drills:** Wi-Fi off 20 min then on (buffer drains, events intact, samples dropped beyond 256); wrong secret (401, backoff to 300 s, recovers after reflashing the right one); stop the Function App (device keeps sampling, catches up).
- [ ] **Step 5: Update docs:** `docs/architecture.md` §14 progress, `firmware/README.md` (feature flags and the breadboard swap: `--no-default-features --features board-rev-a,heater-replay` for the 2-LED strip with fake data, `heater-rs485` once the CTA-2045 poller exists). Commit and push.
