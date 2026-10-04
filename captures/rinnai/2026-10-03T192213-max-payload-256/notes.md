# 2026-10-03T192213-max-payload-256

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, outside-comm-good, opstate query, GetInformation; never control commands. TX log in transmit.jsonl
- **Scenario:** max-payload-256
- **Started:** 2026-10-03T19:22:13-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

Run: `ucm.py --scenario max-payload-256 --transmit --probe --duration 300`
(app reply delay 1.0 s, max payload indicator `0x07` = 256 bytes).

- **0x07 changed nothing.** Every `19 07` response in steady state got a link ACK,
  and discovery still repeated about every 22 s (stats: 103 RX, 106 TX, 0 unanswered).
- The repeat interval is about 20 s of idle after each round ends. Passive
  discovery had the same ~20 s idle after its ×3 retries. So the repeat looks like the
  heater's normal periodic link check, not a sign that negotiation failed.
- Startup collision: our probe started while the heater was mid-discovery. It sent
  three max-payload queries in a row, and we queued three responses. The first was
  ACKed and the two extras got NAK `15 07`. Fixed: a repeated query now replaces any
  unsent reply.
- Opstate (Running Normal), outside-comm App ACK and GetInformation were unchanged.
