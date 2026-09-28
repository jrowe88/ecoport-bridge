# Protocol Notes

This document tracks CTA-2045 protocol knowledge for this project. Keep
**Confirmed** facts separate from **Observed** behavior and **Hypotheses** —
don't let a guess quietly become "the spec".

## Confirmed

- Physical connector: `420B2V12FL0` ("UCM Connector" per EPRI)
- Only pins 1 (D-), 7 (D+), 8 (GND) are used
- EPRI documents 19,200 baud, 8 data bits, 1 stop bit, no parity
- Rinnai REHP65 is CTA-2045-B / EcoPort capable (per ENERGY STAR listing)
- CTA-2045 defines Basic DR and Intermediate message sets

## Observed

_(fill in as captures are recorded — reference the specific capture under
`captures/rinnai/<date>-<scenario>/`)_

- Rinnai sends a periodic status packet approximately every ~30 seconds while
  idle
- A distinct packet appears immediately after the compressor starts

## Hypotheses

_(explicitly mark these as unconfirmed until verified against documentation
or repeated captures)_

- The periodic idle packet appears to contain tank temperature

## Candidate data points of interest

Based on typical CTA-2045 SGD capabilities:

```text
water_temperature
setpoint
operating_mode
power_level
energy
shed_state
faults
capabilities
```

## Commands of interest

```text
Get Information
Get Present Temperature
Get Set Point
Get Commodity
Get Operating State
```

```text
Shed
Critical Peak
Load Up
Set Point
End Shed
```
