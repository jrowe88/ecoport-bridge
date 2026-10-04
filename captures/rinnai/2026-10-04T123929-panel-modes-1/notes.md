# 2026-10-04T123929-panel-modes-1

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 active UCM session
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** TRANSMITS: link ACK/NAK, max payload response, discovery, max payload query, outside-comm-good, opstate query, GetInformation; never control commands or vendor-proprietary types. TX log in transmit.jsonl
- **Scenario:** panel-modes-1
- **Started:** 2026-10-04T12:39:29-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
| 12:39 | Idle, economy mode, setpoint 120 °F | none | baseline |
| 12:42 | | setpoint 120 → 125 °F | compressor heard ~12:43 |
| 12:54 | | force element | |
| ~12:59 | idle | | operator: compressor stopped |
| 13:00 | | setpoint → 121 °F | |
| 13:01–13:04 | idle | modes: heat pump only → vacation → hybrid → e-heater → economy | |

## Observations

Timeline: `timeline.csv` (`python tools\ucm_timeline.py <this dir>`). Link was clean: 1077 RX / 1077 TX, 0 unanswered.

- **Setpoint → total capacity (Wh), changes within one 15 s poll.** Present take moves by the same amount:

  | Setpoint | Capacity |
  |---|---|
  | 120 °F | 12011 |
  | 121 °F | 12408 |
  | 125 °F | 13201 |

  Not linear: +397 Wh for 120→121 but ~198 Wh/°F for 121→125. It may be a lookup table, or depend on a measured cold-water reference. More setpoints are needed before the setpoint can be inferred reliably.
- **Electricity rate identifies the heat source** (opstate is just 1 Running Normal for all of them):

  | Rate | Meaning |
  |---|---|
  | 0 W | idle |
  | 242 W, then 286 W after ~3 min | compressor |
  | 4840–4862 W | element forced while compressor still running (≈ 4.5 kW element + ~330 W compressor) |
  | 4510 W | element alone, last minute before idle |

  The rate is a per-component estimate, not a meter reading, but it is good enough to tell compressor from element.
- **Recovery shows in present take:**
  - Compressor only: take barely moved over 11 min (1839 → 1767 Wh).
  - Element: take fell ~100–180 Wh per 15 s (1767 → 955 Wh in 4.5 min).
  - After heating stopped, take rose again by ~160 Wh (865 → 1028 Wh), probably thermal mixing/stratification settling.
- **Vacation mode → Commodity Read returns all zeros** (capacity 0, take 0, rate 0). This is detectable.
- Heat pump only, hybrid, e-heater and economy modes look identical while idle: no visible change in any field. They would probably differ only by which rate appears when heating starts.
- Opstate stayed 0 Idle Normal / 1 Running Normal; no other opstate values appeared.
- Discovery rounds now repeat every 15 s, in step with our 15 s keepalive (they were ~22 s with a 60 s keepalive).
