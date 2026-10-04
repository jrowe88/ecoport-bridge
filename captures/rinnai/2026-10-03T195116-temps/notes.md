# 2026-10-03T195116-temps

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, max payload query, outside-comm-good, opstate query, GetInformation, GetSetPoint, GetPresentTemperature; never control commands. TX log in transmit.jsonl
- **Scenario:** temps
- **Started:** 2026-10-03T19:51:16-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

Run: `ucm.py --scenario temps --transmit --probe --duration 300`.

- The heater's own max payload is `19 05`, i.e. **64 bytes**.
- **GetSetPoint (`03 03`) and GetPresentTemperature (`03 04`) both got link NAK
  `15 07`** (request not supported), including every 60 s keepalive repeat. This
  matches GetInformation's capability bitmap of 0. These reads are not implemented,
  and the temperature query was removed from the keepalive.
- Discovery still repeats about every 22 s even after we queried its max payload. That
  rules out the "UCM must query too" idea; the repeat is a periodic link check.
- Opstate Running Normal; outside-comm status App-ACKed; GetInformation unchanged.
