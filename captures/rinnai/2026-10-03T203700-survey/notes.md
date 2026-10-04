# 2026-10-03T203700-survey

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, max payload query, outside-comm-good, opstate query, GetInformation, survey of spec-defined reads (supported queries 08 04 / 09 01-0C, Intermediate Get* requests); never control commands or vendor-proprietary types. TX log in transmit.jsonl
- **Scenario:** survey
- **Started:** 2026-10-03T20:37:00-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

- Commissioning (`08 04`) and pass-through `09 01`–`09 0C` supported queries → NAK `15 06` (unsupported message type).
- Every Intermediate Get → NAK `15 07` (request not supported), except **Commodity Read `06 00`**:
  - Electricity consumed: rate 0 W, cumulative 0 Wh (estimated).
  - Total energy storage capacity: 12011 Wh.
  - Present energy storage (take) capacity: 432 Wh, later 450 Wh. The tank is nearly full, consistent with opstate Idle Normal.
- The first Commodity Read reply was corrupted by our echo filter. Its header matched our TX prefix `08 02 00`, so it was stripped. The heater's retry parsed with a valid checksum.
- Echo-filter bug: the FTDI adapter does not echo. The `echo` records in `transmit.jsonl` are really heater reply headers that were stripped by mistake. Fixed afterwards: the filter is now opt-in via `--echo-filter`.
- Discovery still repeats about every 22 s (periodic link check).
