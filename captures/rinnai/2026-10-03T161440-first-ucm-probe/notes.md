# 2026-10-03T161440-first-ucm-probe

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, outside-comm-good, opstate query, GetInformation; never control commands. TX log in transmit.jsonl
- **Scenario:** first-ucm-probe
- **Started:** 2026-10-03T16:14:40-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
| 16:14:40 | Idle-ish; DR icon on; heater silent >30 min | Started `ucm.py --transmit --probe` | Heater replied immediately; see analysis.md. |
| 16:16:33 | DR on | Stopped with Ctrl+C | |

## Observations

-
