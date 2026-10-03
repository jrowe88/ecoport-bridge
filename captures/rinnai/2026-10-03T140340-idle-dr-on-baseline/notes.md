# 2026-10-03T140340-idle-dr-on-baseline

## Capture metadata

- **Source:** Rinnai REHP65 CTA-2045 passive capture
- **Serial Port:** COM4
- **Serial Settings:** 19200 baud, 8 data bits, no parity, 1 stop bit
- **Transmit Policy:** No serial writes; passive receive-only capture
- **Scenario:** idle-dr-on-baseline
- **Started:** 2026-10-03T14:03:40-06:00

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
| Before capture | Idle; DR enabled | Started passive capture on COM4 | No laptop-to-heater serial writes. |

## Observations

- Five-minute passive capture completed with DR enabled.
- See [analysis.md](analysis.md) for the reconstructed frame sequence and
  comparison with the DR-disabled baseline.
