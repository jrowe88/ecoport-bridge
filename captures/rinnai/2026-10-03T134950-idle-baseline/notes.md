# 2026-10-03T134950-idle-baseline

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 passive capture
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** No serial writes; passive receive-only capture
- **Scenario:** idle-baseline
- **Started:** 2026-10-03T13:49:50-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
| Before capture | Idle; DR disabled | Started passive capture on COM4 | No laptop-to-heater serial writes; no bytes received. |

## Observations

- Five-minute passive capture completed with DR disabled.
- No receive events were recorded; `capture.bin` and `capture.jsonl` are both
  empty.
