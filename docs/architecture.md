# Architecture

EcoPort Bridge has two parts:

1. **The bridge (UCM).** An ESP32-C6 plugged into the heater's CTA-2045 port.
   It talks to the appliance, buffers readings, and connects out to the cloud.
2. **The cloud service.** A low-cost Azure backend that stores history, derives
   insights, serves a multi-user web dashboard, and queues commands for the
   bridge.

This document covers both: the software layering (section 1) and the
device ↔ cloud system design (sections 2 onwards).

> **Status:** plan. The protocol research in `tools/` and `captures/` is
> working. The firmware and cloud service are not built yet. The data model
> uses the fields the Rinnai REHP65 actually exposes today (see
> [`capabilities.md`](capabilities.md)). New fields may be added later without
> changing this architecture.

---

## 1. Software layering

The protocol and appliance code is organized in five layers, from the
electrical interface up to integrations:

```text
                    ECOport Bridge
                         │
          ┌──────────────┴──────────────┐
          │                             │
     CTA-2045 core                 Appliance layer
          │                             │
          │                    ┌────────┴─────────┐
          │                    │                  │
          │                 Rinnai            Other SGD
          │                    │
          └───────────┬────────┘
                      │
                  Transport
                      │
                 RS-485 / USB
```

1. **Transport.** Physical RS-485 UART, isolated transceiver, and byte-level
   framing. See `python/src/ecoport/transport/` and `firmware/src/transport/`.
2. **CTA-2045 core (protocol).** Generic CTA-2045 link layer, Basic DR and
   Intermediate message types, CRC and enums. It has no appliance-specific
   knowledge. See `python/src/ecoport/cta2045/` and `firmware/src/cta2045/`.
3. **Appliance layer.** Appliance-specific capability mappings and quirks
   (e.g. Rinnai REHP65). See `python/src/ecoport/appliances/rinnai/` and
   `firmware/src/appliance/rinnai/`.
4. **Bridge / network.** Wi-Fi, time sync, HTTPS client, the local record
   buffer and device identity. See `firmware/src/network/`.
5. **Integrations.** Outward-facing publishers. The **cloud integration**
   (section 2 onwards) comes first. Local MQTT / Home Assistant / Matter is an
   optional later add-on. See `firmware/src/integrations/`.

Keeping the CTA-2045 protocol free of Rinnai assumptions means a second
EcoPort appliance (e.g. another manufacturer's CTA-2045 water heater) can be
added as a new `appliances/<vendor>` module without touching the protocol core.
The cloud has the same split: each appliance model gets an **appliance
profile** (section 6.4) that holds its calibration and derivation rules.

---

## 2. System overview

```mermaid
flowchart LR
    subgraph Home["Home network (behind NAT)"]
        WH["Rinnai REHP65<br/>(SGD)"]
        subgraph UCM["ESP32-C6 UCM (bridge firmware)"]
            LINK["CTA-2045 link<br/>+ 15 s poll"]
            BUF[("Flash ring buffer<br/>records + seq")]
            UP["Uploader"]
            POLL["Command poller<br/>+ executor"]
        end
        WH <-->|"RS-485<br/>CTA-2045"| LINK
        LINK --> BUF --> UP
        POLL -->|"Basic DR"| LINK
    end

    subgraph Azure
        subgraph FA["Device Function App (C# isolated, Flex Consumption)"]
            DAPI["Device API<br/>/device/v1/*<br/>HMAC auth"]
            PROC["Processor<br/>(queue trigger)<br/>derivation pipeline"]
            TMR["Timers<br/>expiry · rollups · heartbeat"]
        end
        subgraph ST["Storage account"]
            RAW[("Telemetry<br/>DeviceEvents<br/>(raw, immutable)")]
            Q[["process-device queue"]]
            DER[("CurrentState<br/>DerivedEvents<br/>Rollups")]
            CMD[("Commands")]
            REG[("Devices<br/>DeviceAccess")]
        end
        KV[("Key Vault<br/>device secrets")]
        PROF["Appliance profiles<br/>(versioned JSON)"]
        subgraph SWA["Static Web App (Free)"]
            WEB["Dashboard"]
            UAPI["User API<br/>/api/* (managed functions)"]
        end
        AI["Application Insights"]
    end

    USER(["Users<br/>GitHub / Microsoft sign-in"])
    GH(["Google Home<br/>(later phase)"])

    UP -->|"HTTPS POST records"| DAPI
    POLL -->|"HTTPS GET commands<br/>POST results"| DAPI
    DAPI --> RAW
    DAPI --> Q
    DAPI <--> CMD
    DAPI -.->|"verify HMAC"| KV
    DAPI -.-> REG
    Q --> PROC
    PROC --> RAW
    PROC --> DER
    PROF -.-> PROC
    TMR --> CMD
    TMR --> DER
    USER --> WEB --> UAPI
    UAPI --> DER
    UAPI --> RAW
    UAPI --> CMD
    UAPI --> REG
    GH -.-> DAPI
    FA -.-> AI
```

The UCM opens every connection. Nothing in Azure connects to the device.

### 2.1 Design principles

1. **The UCM is authoritative for the physical heater.** It is the only thing
   that talks CTA-2045, and it enforces its own safety limits.
2. **Keep the UCM thin.** The firmware reads, buffers, publishes, polls and
   reacts. It sends **raw CTA-2045 values** plus cheap facts it already has.
   Anything that can be derived later is derived **in the cloud**, because
   cloud algorithms are easy to change and firmware is not.
3. **Outbound connections only.** The UCM opens every connection, so there is
   no inbound port, NAT setup or public device endpoint.
4. **Raw data is immutable. Derived data can be rebuilt.** When an algorithm or
   calibration changes, derived tables can be regenerated from the raw records.
5. **A command is only a request** until the heater confirms it (App ACK plus
   the expected opstate).
6. **Survive outages.** Records are buffered in flash. Commands expire. The
   heater falls back to normal operation on its own, and the panel always works.
7. **Stay cheap and simple** for a few devices across a few households, with a
   clear path to scale (section 12).

### 2.2 Decisions

| Topic | Decision |
|---|---|
| Device ↔ cloud transport | HTTPS POST (records, results) and short-poll GET (commands). No MQTT, IoT Hub or long polling in v1 |
| Where derivation happens | Cloud, on intake (asynchronous processor). UCM sends raw values |
| Telemetry cadence | Sample every 15 s, upload a batch every ~60 s, upload immediately on significant events |
| Storage | Azure Table Storage (plus a Storage Queue) |
| Backend | Azure Functions, C# (.NET isolated worker) |
| Dashboard | Azure Static Web Apps with built-in auth |
| Tenancy | Multi-user: many users and devices, with access granted per device |
| Automation (TOU / solar / DR schedules) | Deferred. Manual commands only in v1, but the design leaves room (section 13) |
| Local MQTT / Home Assistant | Optional later integration. The cloud is primary |
| Google Home | Later phase, designed for but not built first |
| Infrastructure as code | Terraform |

### 2.3 End-to-end flows

**Telemetry: from the heater to the dashboard**

```mermaid
sequenceDiagram
    autonumber
    participant WH as Rinnai (SGD)
    participant U as ESP32-C6 UCM
    participant D as Device API
    participant T as Tables (raw)
    participant Q as Queue
    participant P as Processor
    participant S as CurrentState / Derived / Rollups
    participant W as Dashboard (User API)

    loop every 15 s
        U->>WH: Basic DR opstate (12 00)
        WH-->>U: opstate
        U->>WH: Commodity Read (08 02 06 00)
        WH-->>U: rate W, capacity Wh, take Wh
        U->>U: append sample (seq++) to flash buffer
    end
    Note over U: event (opstate change, override, NAK)<br/>is appended too and triggers an immediate upload

    U->>D: POST /device/v1/records (batch, HMAC-signed)
    D->>D: verify HMAC + timestamp, validate schema
    D->>T: upsert raw rows (idempotent keys)
    D->>Q: enqueue process(deviceId)
    D-->>U: 200 { ackSeq, serverTime, commandsPending }
    U->>U: delete buffered records ≤ ackSeq

    Q->>P: process(deviceId)
    P->>S: read ProcessedThroughSeq + derivation state
    P->>T: read raw records > ProcessedThroughSeq
    P->>P: derive heat source, setpoint, vacation,<br/>SoC, draws, cycles (appliance profile)
    P->>S: write DerivedEvents, Rollups, CurrentState (ETag)

    W->>S: GET /api/devices/{id}/state, /telemetry, /events
    S-->>W: current + historical data
```

**Command: from a user to the heater and back**

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant W as Dashboard / User API
    participant C as Commands table
    participant D as Device API
    participant U as ESP32-C6 UCM
    participant WH as Rinnai (SGD)

    User->>W: Shed 30 min
    W->>W: check DeviceAccess (owner), allowlist, limits
    W->>C: insert command (pending, expiresAt = +5 min)
    W-->>User: 202 { commandId }

    Note over U,D: next 20 s poll, or immediately if the<br/>last upload returned commandsPending
    U->>D: GET /device/v1/commands
    D->>C: oldest pending, not expired → delivered
    D-->>U: 200 { commandId, action: shed, durationMinutes: 30 }

    U->>U: check expiry, processed-ID cache, allowlist, limits
    U->>WH: Basic DR Shed (01 dd)
    WH-->>U: App ACK (or App NAK + reason)
    loop up to ~3 × over ~45 s
        U->>WH: opstate query
        WH-->>U: opstate (expect 2 or 4 = curtailed)
    end
    U->>U: cache commandId + result
    U->>D: POST /device/v1/command-results
    D->>C: status = succeeded / acknowledged / rejected / failed

    User->>W: poll command status
    W->>C: read
    W-->>User: succeeded (confirmed opstate 4)
```

If the result is lost, the command is delivered again after the redelivery
timeout. The UCM finds the ID in its cache and **re-reports** the result
without running the command again.

---

## 3. UCM (bridge firmware) responsibilities

Target hardware is an **ESP32-C6**. `firmware/platformio.ini` still targets
`esp32dev` and needs updating when firmware work starts.

The firmware only does these things:

| Responsibility | Notes |
|---|---|
| CTA-2045 link | Handshake, max-payload negotiation, App ACK/NAK, echo filtering. Ported from `tools/ucm.py` |
| Poll the heater | Every 15 s: Basic DR opstate (`12 00`) and Commodity Read (`08 02 06 00`). GetInformation (`01 01`) at boot and when the link (re)connects |
| Notice heater-initiated messages | e.g. Customer Override (`11 xx`), unsolicited NAKs. Record them as events |
| Buffer records | Flash-backed ring buffer (section 3.2) |
| Upload | Batched HTTPS POST, oldest first, deleted only after the server acknowledges them |
| Poll and execute commands | Validate, send Basic DR, confirm through opstate, report the result |
| Report outside comm status | Send CTA-2045 *Outside Communication Connection Status* to the heater so it knows whether the UCM has cloud connectivity |
| Health | Wi-Fi RSSI, uptime, free heap, reset reason, buffer depth, link error counters |

It deliberately **does not** do any of these: setpoint calibration, heat-source
classification beyond simple flags, state-of-charge, draw detection, energy
integration, scheduling or rollups.

```mermaid
flowchart TB
    subgraph FW["UCM firmware tasks"]
        direction TB
        POLLH["Heater poller<br/>every 15 s: opstate + Commodity Read"]
        RX["Heater RX handler<br/>override, NAK, unsolicited"]
        REC["Record writer<br/>assign seq, bootId, uptimeMs"]
        RING[("Flash ring buffer")]
        UPL["Uploader<br/>~60 s or on event · backoff"]
        CP["Command poller<br/>20 s or on commandsPending"]
        EXE["Executor<br/>validate → Basic DR → confirm"]
        CACHE[("Processed-ID cache (NVS)")]
        OCS["Outside Comm Status<br/>to heater"]
    end
    POLLH --> REC
    RX --> REC
    REC --> RING --> UPL
    UPL -->|"ackSeq → trim"| RING
    CP --> EXE
    EXE <--> CACHE
    EXE -->|"result event"| REC
    UPL -. "cloud reachable?" .-> OCS
```

### 3.1 Record types

The UCM produces a single stream of **records**. Every record gets the next
value of a **persistent sequence number**: a 32-bit counter that keeps
increasing across reboots. A high-water mark is stored in NVS, and the counter
jumps forward on boot to stay safe.

| Kind | When | Raw content |
|---|---|---|
| `sample` | Every 15 s | Opstate, Commodity Read fields, link quality |
| `event` | When it happens | Opstate change, Customer Override, App NAK, link lost or restored, boot, time sync, heater info (GetInformation) |
| `health` | Every ~5 min and at boot | RSSI, heap, uptime, reset reason, buffer depth, firmware version |

### 3.2 Local buffer

- Use a flash ring buffer in its own partition. Records are compact binary,
  about 32 B for a sample. At 5,760 samples/day, a 1 MB partition holds about
  five days.
- Flash writes are batched (e.g. once a minute, or right away for events) to
  limit wear.
- Each record stores `bootId` and `uptimeMs`. The device converts these to
  UTC when it uploads, using the SNTP offset for that boot. Uploading needs a
  network connection, and SNTP syncs as soon as there is one. Records from a
  boot that never synced time are uploaded with `timeQuality: "unsynced"`. The
  cloud keeps them but leaves them out of time-based analysis.
- When the buffer is full, the oldest `sample` and `health` records are
  dropped before any `event` records.
- Records are removed only up to the `ackSeq` that the server returns.

### 3.3 Upload cadence

- Normally, upload one batch about every 60 s (about four samples, plus any
  events).
- Upload **right away** when an event is recorded (opstate change, override or
  command result), so the dashboard reacts within seconds.
- After an outage, drain the buffer oldest first in chunks of up to 100
  records. Leave a short gap between chunks.
- On failure, back off exponentially with jitter (cap ~5 min). Keep sampling
  the whole time.

---

## 4. Device API

All device endpoints are on the **Device Function App**, are versioned under
`/device/v1/`, and require HMAC authentication (section 9.1).

### 4.1 Upload records

```http
POST /device/v1/records
Content-Type: application/json
X-Device-Id: WH001
X-Timestamp: 2026-10-09T20:31:02Z
X-Signature: <base64 HMAC-SHA256>
```

```json
{
  "deviceId": "WH001",
  "bootId": "c4e633a2",
  "firmware": "0.3.0",
  "records": [
    {
      "seq": 12783,
      "kind": "sample",
      "t": "2026-10-09T20:30:00.120Z",
      "opState": 1,
      "commodity": {
        "electricityRateW": 268,
        "electricityCumulativeWh": 0,
        "totalStorageCapacityWh": 12011,
        "presentEnergyTakeWh": 1450
      },
      "link": { "ok": true, "retries": 0, "naks": 0 }
    },
    {
      "seq": 12784,
      "kind": "event",
      "t": "2026-10-09T20:30:07.880Z",
      "type": "opStateChanged",
      "data": { "from": 1, "to": 0 }
    }
  ]
}
```

Notes:

- The `commodity` object stores the CTA-2045 Commodity Read values exactly as
  received, keyed by commodity type, so the cloud keeps everything the heater
  reports. That includes fields that are always 0 today, such as
  `electricityCumulativeWh`.
- Fields the heater does not provide (tank temperature, setpoint, mode, faults)
  are **not** sent. Setpoint and mode are derived in the cloud. If later
  research finds a direct source, it is added as a new optional field.
- Optional `raw` hex frames can be attached to events for unrecognized
  messages, for remote reverse engineering. This is off by default and turned
  on per device.

Response:

```json
{ "ackSeq": 12784, "serverTime": "2026-10-09T20:31:02.410Z", "commandsPending": true }
```

- `ackSeq` is the highest sequence number for which **this record and every
  earlier record in the request** has been stored. The device can then delete
  those records.
- `commandsPending` lets the device poll for commands right away instead of
  waiting for its next poll.
- `serverTime` lets the device spot clock drift.

Server steps: authenticate, validate the schema, upsert the raw rows
(idempotent; section 7), enqueue a `process(deviceId)` message, update
`LastSeen`, then respond. **No derivation happens inside the request.** That
keeps device calls quick and reliable.

### 4.2 Poll for commands

```http
GET /device/v1/commands
```

The UCM polls every **20 s** while online, and immediately after an upload
response with `commandsPending: true`. It backs off on errors.

- `204 No Content`: nothing is pending.
- `200`: returns the oldest command that is still `pending` and not expired.
  The server marks it `delivered` and records `deliveredAt`. If no result
  arrives within a redelivery timeout (e.g. 2 min), the same command is
  delivered again. The UCM's processed-ID cache makes this safe.

```json
{
  "commandId": "01J9V6WR9M6P8Q2T1XK4H3B7ZD",
  "action": "shed",
  "parameters": { "durationMinutes": 30 },
  "createdAt": "2026-10-09T20:31:00Z",
  "expiresAt": "2026-10-09T20:36:00Z"
}
```

### 4.3 Report command result

```http
POST /device/v1/command-results
```

```json
{
  "commandId": "01J9V6WR9M6P8Q2T1XK4H3B7ZD",
  "status": "succeeded",
  "heaterResponse": { "type": "appAck", "opcode": "0x01" },
  "confirmedOpState": 4,
  "completedAt": "2026-10-09T20:31:21Z"
}
```

This is idempotent by `commandId`. The UCM also records the result as an
`event`, so it appears in the timeline even if this call is lost.

---

## 5. Commands

### 5.1 Supported actions (v1)

The only control the heater offers is **CTA-2045 Basic DR** (see
[`capabilities.md` §4](capabilities.md)). Setpoint, mode and vacation **cannot**
be changed over CTA-2045 on the Rinnai, so they are not commands.

| Action | Basic DR | Parameters | Expected confirmation (opstate) |
|---|---|---|---|
| `shed` | `01 dd` | `durationMinutes` | 2 or 4 (curtailed) |
| `endShed` | `02 00` | — | 0 or 1 (normal) |
| `loadUp` | `17 dd` | `durationMinutes` | 3 or 6 (heightened) |
| `criticalPeak` | `0A dd` | `durationMinutes` | 2 or 4 |
| `gridEmergency` | `0B dd` | `durationMinutes` | 2 or 4 |
| `powerLevel` | `06 pp` | `percent` | App ACK only (no opstate signature known) |

The duration is encoded as `dd = ceil(sqrt(seconds / 2))`.

The **confirmation column is a hypothesis** until the control test in
`capabilities.md` §4 has run. The confirmation rules live in the cloud's
appliance profile. The UCM only reports what it saw (ACK/NAK and opstate), so
the rules can be corrected without a firmware change.

Price messages (`07`/`08`/`09`) are left out until there is a reason to add
them.

### 5.2 Validation (both sides)

| Check | Cloud | UCM |
|---|---|---|
| User may control this device | ✔ (`owner` role) | — |
| Action is on the allowlist | ✔ | ✔ |
| Duration 1–120 min; every event command has a duration | ✔ | ✔ (hard limit) |
| Not expired (`expiresAt`, default 5 min after creation) | ✔ | ✔ |
| Not already processed | — | ✔ (persistent cache of recent IDs and results) |

### 5.3 Execution on the UCM

1. Check expiry, the processed-ID cache and the allowlist with its limits.
2. Send the Basic DR message and wait for the App ACK or NAK.
3. If ACKed, query opstate again (up to ~3 tries over ~45 s).
4. Report the result. Put the ID and result in the persistent cache. If the
   same command is delivered again, the cached result is re-reported and the
   command is **not** re-executed.

### 5.4 Status lifecycle

```mermaid
stateDiagram-v2
    [*] --> pending: user / API creates
    pending --> delivered: UCM polls
    pending --> canceled: user cancels
    pending --> expired: expiresAt passes
    delivered --> delivered: redelivered (no result within timeout)
    delivered --> succeeded: ACK + expected opstate
    delivered --> acknowledged: ACK, opstate not confirmed / not checkable
    delivered --> rejected: heater App NAK (e.g. 05 override)
    delivered --> failed: UCM error, link down, timeout
    delivered --> expired: no result before expiresAt
    acknowledged --> succeeded: processor confirms from later samples
    succeeded --> [*]
    acknowledged --> [*]
    rejected --> [*]
    failed --> [*]
    expired --> [*]
    canceled --> [*]
```

The cloud can **re-check a result** later. For example, it can upgrade
`acknowledged` to `succeeded`, or flag an early end, by comparing the result
with the opstate samples that follow. That check is done by the processor.

### 5.5 Safety

- Every event command has a bounded duration, so it ends even if End Shed is
  lost.
- If the UCM stops talking to the heater, the CTA-2045 fallback returns the
  heater to normal.
- The UCM tells the heater when it loses cloud connectivity (*Outside Comm
  Status*).
- The panel override always works. Overrides show up as `customerOverride`
  events and as opted-out opstates.

---

## 6. Cloud processing (derivation on intake)

### 6.1 Flow

```text
POST /records ─► upsert raw ─► enqueue process(deviceId) ─► 200 ackSeq
                                       │
                                       ▼
         Processor (queue trigger, one device at a time via ETag)
           1. Read CurrentState.ProcessedThroughSeq
           2. Read raw records > that seq, in seq order
           3. Run the appliance-profile derivation pipeline
           4. Write DerivedEvents + Rollups
           5. Update CurrentState (ETag-guarded) incl. ProcessedThroughSeq
```

- The queue message only **wakes the processor up**. The processor reads raw
  data from tables, so out-of-order or duplicate messages do no harm.
- If two processors run for the same device, ETag concurrency on
  `CurrentState` stops both from committing. The one that loses retries.
- Derivation is **stateful**. Draw detection, for example, needs the previous
  samples. That state is kept in `CurrentState.DerivationStateJson`.

### 6.2 Derived values

These are based on today's capabilities (see `capabilities.md` §2):

| Derived value | Source | Stored in |
|---|---|---|
| Heat source (off / compressor / element / both) | `electricityRateW` bands | CurrentState, Rollups |
| Setpoint (°F) | `totalStorageCapacityWh` and the profile's calibration table | CurrentState, DerivedEvents (on change) |
| Vacation | Commodity Read returns all zeros | CurrentState, DerivedEvents |
| State of charge (%) | 1 − take ÷ capacity | CurrentState, Rollups |
| Hot water draws (time, estimated kWh) | Jumps in take | DerivedEvents |
| Heating cycles (start, end, source minutes, estimated kWh) | Opstate and rate transitions | DerivedEvents, Rollups |
| Estimated energy (kWh by source) | Rate integrated over time | Rollups |
| Inferred panel mode | Heat-source pattern during recovery | CurrentState (with confidence) |
| DR event outcome | Command, opstate and rate after it, overrides | DerivedEvents |
| Connectivity (online / delayed / offline) | `LastSeen` against configurable thresholds | Computed at read time |

Each derived row records `derivationVersion` and `profileVersion`.

### 6.3 Reprocessing

If an algorithm or calibration changes, an admin-triggered job resets
`ProcessedThroughSeq` (or picks a time range), deletes the derived rows for
that range, and runs the processor again. Raw data is never modified.

### 6.4 Appliance profiles

A profile holds the per-model rules and is keyed by the GetInformation data
(vendor `0x0C22`, device type HPWH, revision):

- Calibration from capacity to setpoint (lookup table; not linear)
- Electricity-rate bands for classifying the heat source
- Opstate confirmation rules for commands
- Thresholds for draw and cycle detection

Profiles are versioned JSON files kept in the repo (`cloud/profiles/`) and
deployed with the Function App. A device may override part of its profile, for
example its own calibration.

### 6.5 Golden tests

The derivation code is ported from the Python research tools (such as
`tools/ucm_timeline.py`) to C#. The captures in `captures/rinnai/` become test
fixtures: replay a capture through the C# pipeline and check the expected
heating cycles, draws, setpoint and vacation transitions.

---

## 7. Storage (Azure Table Storage)

One storage account holds all the tables below, plus one queue
(`process-device`).

| Table | PartitionKey | RowKey | Purpose |
|---|---|---|---|
| `Devices` | `DEVICE` | `{deviceId}` | Registry: claim-code hash, secret version, heater info, firmware, `LastSeen`, created/claimed dates |
| `DeviceAccess` | `{userId}` | `{deviceId}` | Grants a user a role (`owner` / `viewer`) on a device |
| `DeviceAccessByDevice` | `{deviceId}` | `{userId}` | Reverse lookup, used for sharing and admin |
| `Telemetry` | `{deviceId}_{yyyyMMdd}` | `{HHmmssfff}_{seq:D10}` | Raw `sample` records (immutable) |
| `DeviceEvents` | `{deviceId}` | `{yyyyMMddTHHmmssfff}_{seq:D10}` | Raw `event` and `health` records (immutable) |
| `CurrentState` | `DEVICE` | `{deviceId}` | Latest raw and derived values, `ProcessedThroughSeq`, `DerivationStateJson`, active DR event and expiry |
| `DerivedEvents` | `{deviceId}` | `{yyyyMMddTHHmmss}_{type}_{seq}` | Heating cycles, draws, setpoint, vacation and mode changes, DR outcomes |
| `Rollups` | `{deviceId}_H` / `{deviceId}_D` | `{yyyyMMddHH}` / `{yyyyMMdd}` | Hourly and daily stats: heat-source minutes, estimated kWh, draws, min/avg/max SoC |
| `Commands` | `{deviceId}` | `{ULID}` | Command, parameters, status, timestamps, `RequestedBy`, result JSON |

Notes:

- **Idempotent writes.** Raw row keys are built only from record contents
  (time and seq), so if the device retries, the upsert writes the same row.
  Table batch transactions (up to 100 entities in one partition) cover a
  typical upload.
- **Daily telemetry partitions** let a one-day chart come from one partition
  query. Longer ranges read `Rollups`.
- **Volume.** About 5,760 samples per device per day, roughly 2 M rows per
  year. That is a few hundred MB per device per year, which costs pennies in
  Table Storage. Raw data is kept indefinitely in v1, and a retention policy
  can be added later.
- **Tenant isolation** is enforced in the User API by checking `DeviceAccess`
  on every request. Partitions are per device, not per tenant, because devices
  can be shared.

---

## 8. User API and dashboard

### 8.1 Hosting

- **Azure Static Web Apps (Free plan)** hosts the dashboard and the **User
  API** as managed Functions (C# isolated, HTTP only). Its built-in auth
  (GitHub and Microsoft accounts) fills in `x-ms-client-principal`.
- A **separate Function App** (Flex Consumption) hosts the Device API, the
  queue processor and timers. Device traffic never goes through SWA auth, and
  the two can be scaled, secured and deployed separately.
- Both use a shared C# library (`EcoPort.Cloud.Core`) for models, table access
  and derivation.

> Alternative: SWA **Standard** with a linked backend would put everything in
> one Function App. It costs about $9/month per app. This is worth switching to
> if custom auth providers (e.g. Entra External ID for Google Home account
> linking) are needed.

### 8.2 Endpoints

```http
GET    /api/me                                     # profile + devices I can access
POST   /api/devices/claim                          # { claimCode } -> become owner
GET    /api/devices/{id}/state                     # CurrentState (raw + derived + connectivity)
GET    /api/devices/{id}/telemetry?from&to&res     # raw (≤1 day) or Rollups (longer); server-side downsampling
GET    /api/devices/{id}/events?from&to&types      # DerivedEvents + selected raw events
GET    /api/devices/{id}/rollups?from&to&grain=H|D
POST   /api/devices/{id}/commands                  # owner only
GET    /api/devices/{id}/commands?status&from
GET    /api/devices/{id}/commands/{commandId}
POST   /api/devices/{id}/commands/{commandId}/cancel
POST   /api/devices/{id}/access                    # owner shares (viewer/owner) — later
```

### 8.3 Dashboard (v1)

- **Now:** heat source, power (W), stored energy (%), setpoint (°F, derived),
  vacation, opstate, active DR event and time left, last contact.
- **Charts:** power and heat source over time, stored energy, and draw and
  heating-cycle markers.
- **Daily:** element minutes and estimated kWh (the main efficiency alert),
  compressor hours, and draws.
- **Control:** Shed, Load Up, End Shed (and other DR actions) with a duration,
  plus command history and status.
- **Caveats:** estimated and inferred values are labelled as such.

---

## 9. Identity and security

### 9.1 Device authentication

- Each device has a unique `deviceId` and a **256-bit secret**, set when it is
  provisioned. The secret is stored in NVS on the device (encrypted NVS if
  practical) and in **Key Vault** on the server. The server caches secrets in
  memory for a short time.
- Every request is signed:
  `HMAC-SHA256(secret, method \n path \n X-Timestamp \n SHA256(body))`.
- The server rejects a timestamp more than ±5 min from its own clock. Within
  that window, a replay does no harm because every endpoint is idempotent
  (records by seq, results by commandId, and polling is read-only). This
  avoids storing nonces. A nonce can be added if a non-idempotent device
  endpoint ever appears.
- **Rotation:** the `Devices` row stores the current and previous secret
  versions. The server accepts both while a rotation is in progress. The new
  secret is delivered over the authenticated channel (as a `rotateSecret`
  system command) and is confirmed before the old one is retired.
- The device never gets storage keys or any general Azure credential, and it
  never writes to storage directly.
- *Alternative considered:* per-device X.509 client certificates (mTLS). They
  are stronger, but harder to provision and rotate on the ESP32 and in
  Functions. Revisit when moving to IoT Hub or Event Grid MQTT.

### 9.2 Provisioning and claiming (multi-user)

1. **Provision (admin or bench script):** generate `deviceId`, the secret and a
   one-time **claim code**. Write the secret to Key Vault. Write the `Devices`
   row with the claim code hashed. Flash the ID and secret to NVS. Print the
   claim code on a label or show it on the serial console.
2. **Wi-Fi onboarding:** ESP-IDF provisioning over BLE or SoftAP, done on the
   device.
3. **Claim:** the user signs in and enters the claim code. The server writes
   the `DeviceAccess` rows (owner) and marks the code as used.
4. **Unclaim / transfer:** the owner releases the device, or an admin resets
   it. A new claim code is generated.

### 9.3 User authentication and authorization

- SWA built-in auth with GitHub and Microsoft. Any user can sign in, but
  access to a device comes only from `DeviceAccess`.
- Roles: `owner` can view, send commands and share. `viewer` can only view.
  There is also an app-level `admin` role, assigned through SWA role
  invitations, for provisioning and reprocessing.
- Every User API handler loads the principal, checks `DeviceAccess` for the
  device in the route, and then reads or writes.

### 9.4 Secrets and configuration

- The Function Apps use **managed identity** for Storage and Key Vault, so
  there are no connection strings with keys.
- HTTPS only, with TLS 1.2 or later. The firmware pins the root CA bundle,
  not a leaf certificate.

---

## 10. Reliability and idempotency (summary)

| Concern | Mechanism |
|---|---|
| Duplicate uploads | Deterministic raw row keys and upsert. Device deletes only up to `ackSeq` |
| Lost upload response | Device retries. Server writes the same rows again |
| Outage (Wi-Fi or cloud) | Flash ring buffer. Events are kept before samples. Drained oldest first |
| Reboot without a time source | `bootId` + `uptimeMs`, converted to UTC once synced, otherwise flagged `unsynced` |
| Duplicate command delivery | Persistent processed-ID cache on the UCM. The result is re-reported, not re-executed |
| Lost command result | Result is also recorded as an event. Redelivery timeout. Commands expire |
| Concurrent processors | Queue is only a wake-up. ETag on `CurrentState` |
| Algorithm or calibration bug | Raw data is immutable. Versioned derivation. Reprocessing |
| Device offline | `LastSeen` thresholds (configurable). Shown on the dashboard. Alerting later |

---

## 11. Azure resources and deployment

Terraform (`infra/terraform/`) creates one environment per workspace
(`dev`, `prod`):

- Resource group
- Storage account: the tables and queue listed in section 7
- Function App (Flex Consumption, .NET isolated) with a system-assigned
  managed identity, holding the Device API, the processor and timers
- Static Web App (Free) with managed Functions for the User API
- Key Vault (RBAC mode) for device secrets
- Application Insights and a Log Analytics workspace (with a daily cap)
- Role assignments: Function App → Storage Table/Queue Data Contributor and
  Key Vault Secrets Officer. SWA managed functions use an app setting
  connection (managed functions cannot use a managed identity; review this
  when deciding between SWA Free and Standard)

Timers on the Function App:

- Expire stale `pending` / `delivered` commands
- Daily rollups and fixing up late data
- Heartbeat sweep (marks devices offline, used for future alerts)

CI/CD (GitHub Actions): build and test the .NET projects, run `terraform plan`
on PRs, deploy the Function App and SWA when `main` changes.

**Cost:** for a handful of devices, about 6k Device API calls per device per
day (uploads plus 20 s polls), around 175k a month. That should sit within or
near the free grants for Functions and Storage. Application Insights ingestion
is the cost most likely to grow, so it has sampling and a daily cap. Check
with the Azure pricing calculator before going to production.

### Proposed repository layout additions

```text
cloud/
  src/
    EcoPort.Cloud.Core/        # models, table access, auth, derivation, profiles
    EcoPort.Cloud.Device/      # Function App: Device API, processor, timers
    EcoPort.Cloud.Api/         # SWA managed functions: User API
  profiles/                    # versioned appliance profiles (JSON)
  tests/                       # unit + golden tests (replay captures/)
web/                           # static dashboard
infra/terraform/               # Azure infrastructure
firmware/src/integrations/cloud/   # record buffer, uploader, command poller
```

---

## 12. Evolution path

Reconsider the transport and platform when any of these happen:

| Trigger | Option |
|---|---|
| Command latency of 20 s is too slow, or polling cost grows with the fleet | **Azure Event Grid MQTT broker** (per-device X.509 identity, publish and subscribe) or IoT Hub |
| Many devices, managed provisioning, device twins, fleet OTA | **Azure IoT Hub** + DPS |
| Heavy analytics or long-range queries over raw data | Export raw data to Data Explorer / Fabric, or Parquet in Blob |
| Need for sign-in without GitHub/Microsoft, or Google account linking | Entra External ID with SWA Standard |

The device protocol is versioned (`/device/v1/`) and the firmware keeps the
transport inside `integrations/cloud/`, so changing the transport does not
touch the CTA-2045 or appliance layers.

---

## 13. Later phases (designed for, not built in v1)

- **Automation (TOU / solar / DR schedules).** Preferred model: the cloud
  works out a schedule and sends it as a command (`setSchedule`). The UCM
  stores it and carries it out locally, so it keeps working through outages.
  This adds one action and a small scheduler on the device. The rest of this
  architecture stays the same.
- **Google Home.** Cloud-to-cloud fulfillment on the Device Function App
  (`/google/fulfillment`). It needs an OAuth 2.0 account-linking server
  (Entra External ID), which maps Google users to `DeviceAccess`. Queries read
  `CurrentState`, and commands insert into `Commands` (Shed / Load Up exposed
  as modes). Water-heater temperature control is **not** possible because
  setpoint is read-only.
- **Local MQTT / Home Assistant.** A second publisher in
  `firmware/src/integrations/`. It publishes the same raw records to a local
  broker, and an optional Home Assistant add-on runs the derivation
  locally.
- **Firmware OTA.** The device reports its firmware version (already in
  `health`). A `/device/v1/firmware` manifest endpoint and signed images in
  Blob storage allow `esp_https_ota` updates, triggered by a system command.
- **Alerts.** Device offline, element use above a threshold, customer override
  during a DR event, unexpected opstate (e.g. 5 SGD Error). Delivered by email
  or push.

---

## 14. Implementation sequence

1. Port the CTA-2045 link and the 15 s poll loop to the ESP32-C6 (no network
   yet). Check it against `tools/ucm.py` captures.
2. Flash record buffer and persistent sequence number.
3. Terraform for the base resources, plus `EcoPort.Cloud.Core` table models.
4. `POST /device/v1/records` with HMAC auth, raw tables and the queue.
5. Processor with heat source, vacation, SoC and setpoint (calibration
   pending), plus golden tests from the captures.
6. Dashboard: sign-in, claim, current state, one-day chart.
7. Commands: table, User API, device poll and result endpoints, UCM execution
   with confirmation. **Run the control test from `capabilities.md` §4 first.**
8. Draws, heating cycles, rollups and daily reports.
9. Hardening: secret rotation, offline detection, App Insights dashboards,
   CI/CD.
10. Later phases (section 13).

## 15. Open questions

- **Opstate signatures for confirming commands**: waiting on the Shed /
  Load Up test.
- **Setpoint calibration table**: waiting on the setpoint sweep.
- Whether to use **encrypted NVS / flash encryption** on the C6 for the device
  secret (affects provisioning workflow).
- **SWA Free** (managed functions use connection strings) or **Standard**
  (linked backend with managed identity, custom auth), once the security and
  cost tradeoff is clearer.
- **Raw retention policy** (indefinite in v1).
