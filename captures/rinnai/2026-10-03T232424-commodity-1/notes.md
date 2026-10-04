# 2026-10-03T232424-commodity-1

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, max payload query, outside-comm-good, opstate query, GetInformation; never control commands or vendor-proprietary types. TX log in transmit.jsonl
- **Scenario:** commodity-1
- **Started:** 2026-10-03T23:24:24-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

- 10-minute `--probe` run with a 60 s keepalive (comm good, opstate, Commodity Read). First run after the echo-filter fix: **0 dropped/junk lines**. Every heater reply is parsed on the first attempt, ~220 ms after its link ACK. Stats: 218 RX, 219 TX, 65 ACKed, 0 unanswered.
- Opstate was 0 (Idle Normal) the whole time.
- Commodity Read every minute: total capacity is steady at 12011 Wh. Present take cycles through **631 / 649 / 667 Wh** (steps of 18 Wh, not monotonic). This looks like a quantised estimate, probably from a tank thermistor, jittering at a full idle tank. Electricity consumed stays 0.
- Discovery still repeats about every 22 s, and every max-payload response is ACKed.
- One collision: at 23:25:24.684 our keepalive "outside comm good" went out between discovery frames 3 and 4 (~300 ms gap). It wasn't ACKed, and the heater repeated its max-payload query. The retry succeeded. Fixed afterwards: requests now wait 0.6 s after any heater-initiated frame (`SGD_QUIET`).
