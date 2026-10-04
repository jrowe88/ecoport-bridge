# 2026-10-03T182455-max-payload-delay

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, outside-comm-good, opstate query, GetInformation; never control commands. TX log in transmit.jsonl
- **Scenario:** max-payload-delay
- **Started:** 2026-10-03T18:24:55-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

Run: `ucm.py --scenario max-payload-delay --transmit --probe --duration 180`
(app reply delay 1.0 s, max payload indicator `0x06`).

- **The 1.0 s delay fixed the NAK.** All 9 `19 06` max-payload responses got a
  link ACK in 10–20 ms. No `15 07`, nothing left unanswered (stats: 67 RX, 69 TX,
  0 unanswered).
- Outside comm status, opstate query (Running Normal) and GetInformation
  (version A, vendor 0x0C22, HPWH, rev 4) all succeeded again.
- **Discovery still repeats**, once per cycle with no retries, about every
  21–22 s (and about 8 s after our own keepalive traffic). Before: ×3 retries
  every 32 s with no UCM, and every 9–10 s when max payload failed.
- Likely cause: CTA-2045-B §22.4.1.1.4 says a UCM "shall respond with at
  least code 0x07 (256 bytes)". We advertised 0x06 (128 bytes), so the heater
  may treat negotiation as incomplete. `ucm.py` now defaults to `0x07`.
- One collision at 18:25:55: our keepalive went out while the heater was
  mid-discovery. Retry 1 succeeded.
