# 2026-10-03T144506-idle-to-dr-on-transition

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 passive capture
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** No serial writes; passive receive-only capture
- **Scenario:** idle-to-dr-on-transition
- **Started:** 2026-10-03T14:45:06-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
| 14:45:06 | Idle; DR off | Started passive capture on COM4 | No bytes received. |
| ~14:46:18 | Idle | Pressed DR to turn it on (≈ 30–60 s after start, per operator) | First frame at +72.2 s. |
| 14:46:18–14:55:03 | Idle; DR on | None | Repeating 4-frame discovery cycle, ~32 s period, until capture ended. |
| ~15:00 | Idle; DR on | None (capture already ended) | Operator saw adapter RX LED stop about 15 min after capture start. Not recorded. |

## Observations

- See `analysis.md`. Frames decode as CTA-2045 Message Type Supported Queries plus a Maximum Payload Length query; all checksums valid.
