# Protocol Notes

This document tracks CTA-2045 protocol knowledge for this project. Keep
**Confirmed** facts separate from **Observed** behavior and **Hypotheses** —
don't let a guess quietly become "the spec".

## Confirmed

- Physical connector: `420B2V12FL0` ("UCM Connector" per EPRI)
- Full 12-pin map independently confirmed (see `docs/hardware.md`): pins 1
  (D-) and 7 (D+) are RS-485, pin 8 is signal ground, pin 10 is earth
  ground, pins 5 and 12 carry AC line voltage, pin 3 is reserved, and the
  rest are unused. Only pins 1, 7, 8 are used by our bridge.
- EPRI documents 19,200 baud, 8 data bits, 1 stop bit, no parity
- Rinnai REHP65 is CTA-2045-B / EcoPort capable (per ENERGY STAR listing)
- CTA-2045 defines Basic DR and Intermediate message sets
- Message type bytes (per the ANSI standard preview):
  - **Basic DR** = `0x08 0x01`
  - **Intermediate DR** = `0x08 0x02`
- EPRI's C++ sample application demonstrates the following commands against
  an SGD: present temperature, setpoint, temperature offset, commodity,
  power level, operating state, shed, end shed, load-up. These are a good
  starting checklist for our own protocol test suite (see
  `docs/reverse-engineering.md`).

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

## Capability matrix

CTA-2045 defines a fairly broad interface, but an appliance doesn't
necessarily implement every feature. Track what Rinnai actually supports
here as it's confirmed via captures/experiments, and distinguish:

- **not implemented** — Rinnai doesn't respond to / support the function
- **implemented but undocumented** — works, but isn't mentioned in the
  homeowner-facing manual
- **Rinnai uses the mechanism differently** — responds, but semantics differ
  from the generic CTA-2045 expectation

| Function | CTA-2045 | Rinnai REHP65 |
|---|---|---|
| Link negotiation | ✓ | ? |
| Device information | ✓ | ? |
| Operating state | ✓ | ? |
| Present temperature | ✓ | ? |
| Setpoint | ✓ | ? |
| Temperature offset | ✓ | ? |
| Commodity reading | ✓ | ? |
| Energy consumption | ✓ | ? |
| Shed | ✓ | ? |
| End shed | ✓ | ? |
| Load-up | ✓ | ? |
| Price | ✓ | ? |
| Scheduled events | ✓ | ? |
| Advanced Load Up | ✓ | ? |

Update the `Rinnai REHP65` column as each function is confirmed, with a
pointer to the capture/experiment that confirmed it. Also worth checking
against the **EcoPort certified product database** and **OpenADR Alliance**
certification criteria (see `docs/references.md`) — certification
requirements may hint at expected behavior beyond the homeowner manual.

See `docs/references.md` for source material (EPRI simulator/library,
`python-cta2045`, ANSI preview, EcoPort database, OpenADR).
