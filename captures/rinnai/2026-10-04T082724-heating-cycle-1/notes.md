# 2026-10-04T082724-heating-cycle-1

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, max payload query, outside-comm-good, opstate query, GetInformation; never control commands or vendor-proprietary types. TX log in transmit.jsonl
- **Scenario:** heating-cycle-1
- **Started:** 2026-10-04T08:27:24-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

- Context: the operator took a shower just before the start; the compressor was already running at 08:27.
- Clean link: 1324 RX / 1324 TX, 0 retries, 0 unanswered, 0 dropped bytes. The 15 s keepalive (comm good, opstate, Commodity Read) never collided with discovery.
- Opstate was **1 (Running Normal)** for all 122 reads; heating had not finished by 08:57.
- Commodity Read time series is in `timeline.csv` (from `tools/ucm_timeline.py`) (120 samples):
  - Electricity consumed rate: **242 W, constant**. Cumulative stays 0.
  - Present energy take: **4202 Wh at 08:27 → 3066 Wh at 08:57**, a steady decline of ~2.3 kWh/h (18 Wh quantisation steps). Idle full tank last night was ~650 Wh, so the shower drew roughly 3.5 kWh from the tank.
  - Total capacity: unchanged at 12011 Wh.
- 2.3 kWh/h of stored heat from 242 W electric would mean a COP of ~9.5, which is implausible. Either the 242 W is a fixed nominal/estimated value (the record is flagged "estimated") or the take figure isn't pure compressor output. Verify with a clamp meter.
- At this rate the tank would be back to ~650 Wh about 10:00, roughly 1.5 h after the shower.
