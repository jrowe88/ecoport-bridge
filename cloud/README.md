# EcoPort cloud

C# (.NET 10, isolated worker) backend for the EcoPort bridge. Design: [`docs/architecture.md`](../docs/architecture.md)
§4 and §7–9; milestone scope: [`docs/superpowers/plans/2026-10-25-m1-bench-reporting.md`](../docs/superpowers/plans/2026-10-25-m1-bench-reporting.md).
The device contract is [`contracts/device-v1/`](../contracts/device-v1/).

| Project | What it is |
|---|---|
| `src/EcoPort.Cloud.Contracts` | JSON DTOs shared by the Functions and the Blazor dashboard (`JsonDefaults.Options`: camelCase, strict) |
| `src/EcoPort.Cloud.Core` | Request signing, secret stores, table storage, ingest, read-side helpers |
| `src/EcoPort.Cloud.Device` | Device API Function App (Flex Consumption): `POST /device/v1/records` |
| `src/EcoPort.Cloud.Api` | User API, deployed as Static Web Apps managed functions: `GET /api/devices…` |
| `tests/EcoPort.Cloud.Tests` | xUnit tests, including the shared contract files and signing vectors |

```bash
dotnet test cloud/EcoPort.Cloud.slnx
```

## Runtime: .NET 10 everywhere

Every project targets `net10.0`. Static Web Apps managed functions support .NET 10 isolated:
the [supported languages and runtimes](https://learn.microsoft.com/en-us/azure/static-web-apps/languages-runtimes)
page (`ms.date` 2026-10-05, checked 2026-10-25) lists `apiRuntime` value `dotnet-isolated:10.0`
(Windows, Functions 4.x, no end-of-support date), next to `dotnet-isolated:8.0` and `:9.0`, which
end support on 2026-11-10. So the dashboard's `staticwebapp.config.json` uses:

```json
"platform": { "apiRuntime": "dotnet-isolated:10.0" }
```

The Device Function App (Flex Consumption) uses `dotnet-isolated` `10.0` too.

`Microsoft.ApplicationInsights.WorkerService` is held at 2.x (2.23.0): the current
`Microsoft.Azure.Functions.Worker.ApplicationInsights` (2.51.0) fails at startup against
ApplicationInsights 3.x (`TypeLoadException: ITelemetryInitializer`).

## Configuration

App settings (environment variables use `__` for the `:` in configuration keys):

| Setting | Used by | Meaning |
|---|---|---|
| `Storage__ConnectionString` | Device (local), Api | Table storage connection string. Wins when set. `UseDevelopmentStorage=true` for Azurite |
| `Storage__TableEndpoint` | Device (Azure) | `https://<account>.table.core.windows.net`, used with the managed identity when no connection string is set |
| `KeyVault__Uri` | Device (Azure) | When set, device secrets come from Key Vault secret `device-{deviceId}` (Base64 of the 32 bytes), cached 10 min |
| `DeviceSecrets__{deviceId}` | Device (local) | Base64 device secret, used when `KeyVault__Uri` is empty |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | both | Application Insights (worker telemetry) |

The tables `Devices`, `Telemetry`, `DeviceEvents` and `CurrentState` are created on first use if
missing. Devices are **not** auto-registered: an upload from a device without a `Devices` row
(PartitionKey `DEVICE`, RowKey = device ID) gets `401`.

## Run the Device API locally

Needs Node.js (for Azurite) and Azure Functions Core Tools v4
(`npm i -g azure-functions-core-tools@4` if `func` is missing).

1. Start Azurite (keep it running):

   ```bash
   npx azurite --silent --location "$TEMP/azurite"
   ```

2. Create the settings file (gitignored) and fill in a secret:

   ```bash
   cp cloud/src/EcoPort.Cloud.Device/local.settings.example.json cloud/src/EcoPort.Cloud.Device/local.settings.json
   python -c "import os,base64;print(base64.b64encode(os.urandom(32)).decode())"   # -> DeviceSecrets__WH001
   ```

3. Register the device in Azurite. The Azure CLI does not expand `UseDevelopmentStorage=true` for
   tables, so give Azurite's well-known development connection string explicitly:

   ```bash
   AZURITE="DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
   az storage table create --name Devices --connection-string "$AZURITE"
   az storage entity insert --connection-string "$AZURITE" --table-name Devices --entity PartitionKey=DEVICE RowKey=WH001
   ```

4. Start the Function App (listens on port 7071; the route prefix is empty, so the path is
   exactly `/device/v1/records`):

   ```bash
   cd cloud/src/EcoPort.Cloud.Device && func start
   ```

5. Send replayed data with the fake device (same signing as the firmware):

   ```bash
   python tools/fake_device.py --url http://localhost:7071 --device-id WH001 --secret-b64 <secret> --once
   # 200 ackSeq=1975824823 (5 records)
   python tools/fake_device.py --url http://localhost:7071 --device-id WH001 --secret-b64 <wrong secret> --once
   # 401
   ```

   Without `--once` it uploads every `--interval` seconds (default 60). For the devkit, point
   `API_BASE_URL` in `firmware/ucm/src/config.rs` at `http://<pc-lan-ip>:7071` and allow port 7071
   through Windows Firewall.

## Run the User API locally

With Azurite running and some data sent by the fake device:

```bash
cp cloud/src/EcoPort.Cloud.Api/local.settings.example.json cloud/src/EcoPort.Cloud.Api/local.settings.json
cd cloud/src/EcoPort.Cloud.Api && func start --port 7072
curl http://localhost:7072/api/devices
curl http://localhost:7072/api/devices/WH001/state
curl "http://localhost:7072/api/devices/WH001/telemetry?from=2026-10-25T00:00:00Z&to=2026-10-25T12:00:00Z"
```

Called directly like this there is no sign-in. Behind Static Web Apps (or the SWA CLI emulator,
`swa start … --api-location http://localhost:7072`) the route rules in `staticwebapp.config.json`
require role `bench` for `/api/*`.

| Endpoint | Result |
|---|---|
| `GET /api/devices` | `DeviceSummaryDto[]`, ordered by device ID |
| `GET /api/devices/{deviceId}/state` | `DeviceStateDto`; `404` if the device is not in `Devices` |
| `GET /api/devices/{deviceId}/telemetry?from=&to=` | `TelemetryPointDto[]` (ISO 8601 times; default last 24 h; at most 500 points, equal-time buckets). Range > 24 h, unparsable or `from` ≥ `to` → `400 {"error"}`; unknown device → `404` |

Read-time values: `connectivity` is `online` (last seen ≤ 3 min ago), `delayed` (≤ 15 min) or
`offline`; `storedEnergyPercent` = `(1 − take / capacity) × 100` clamped to 0–100, and `null`
when capacity is 0 (vacation mode) or unknown.

## Device API behaviour

- Order of checks: body > 64 KiB → `413`; missing/malformed `X-Device-Id`, unknown device, no
  secret, bad signature or > 5 min skew → `401` with an empty body (the reason is only logged);
  bad JSON or failed validation → `400 {"error": "..."}`; otherwise the records are stored and
  `200 {"ackSeq", "serverTime", "commandsPending": false}`.
- Raw rows are upserts keyed by record time and seq, so a retried upload rewrites identical rows
  and gets the same `ackSeq`. Samples are written in one transaction per UTC-day partition.
- `CurrentState` holds the latest **raw** sample values. It moves forward only when the upload's
  latest sample has a later `t` than the stored `SampleTime` (time, not seq, so a device whose
  NVS was erased and restarts at seq 0 still updates), with an ETag check and re-read on conflict.
  `LastSeen` (server time) only moves forward.
