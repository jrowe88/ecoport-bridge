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
- No regulated low-voltage (5V/12V) supply is present on the connector —
  pins 5/12 are raw AC mains, not clean DC. The bridge board must be powered
  independently (see Power section in `docs/hardware.md`).
- EPRI documents 19,200 baud, 8 data bits, 1 stop bit, no parity
- Rinnai REHP65 is CTA-2045-B / EcoPort capable (per ENERGY STAR listing)
- CTA-2045 defines Basic DR and Intermediate message sets
- Message type bytes (per the ANSI standard preview):
  - **Basic DR** = `0x08 0x01`
  - **Intermediate DR** = `0x08 0x02`
- Rinnai REHP65 traffic matches the ANSI/CTA-2045-B frame format exactly
  (all 204 frames in
  [`2026-10-03T144506-idle-to-dr-on-transition`](../captures/rinnai/2026-10-03T144506-idle-to-dr-on-transition/)
  verify):

  | Bytes | Field |
  |---|---|
  | 2 | Message type (`08 01` Basic DR, `08 02` Intermediate DR, `08 03` Data-Link, `08 04` Commissioning/Network) |
  | 2 | Payload length, big-endian (top 3 bits reserved) |
  | N | Payload |
  | 2 | Fletcher checksum over type + length + payload, seed `0xAA` (CTA-2045-B Appendix C) |

  Implemented in `python/src/ecoport/cta2045/{crc,framing}.py`.
- Link-layer ACK is `06 00`; NAK is `15 <code>` (`03` checksum error, `06`
  unsupported message type, `07` request not supported). Every non-ACK/NAK
  frame must be answered within 40–200 ms (CTA-2045-B §6.1.5.1, §8.1).
- A message with **zero-length payload** is a "Message Type Supported
  Query" for that message type (§8.2). Data-Link opcode `0x18 0x00` is
  "Query: Maximum Payload Length" (§9).
- EPRI's C++ sample application demonstrates the following commands against
  an SGD: present temperature, setpoint, temperature offset, commodity,
  power level, operating state, shed, end shed, load-up. These are a good
  starting checklist for our own protocol test suite (see
  `docs/reverse-engineering.md`).

## Observed

_(fill in as captures are recorded — reference the specific capture under
`captures/rinnai/<date>-<scenario>/`)_

- With the appliance idle and DR enabled, capture
  [`2026-10-03T140340-idle-dr-on-baseline`](../captures/rinnai/2026-10-03T140340-idle-dr-on-baseline/)
  received 720 bytes (111 reconstructed frames) over approximately five
  minutes. The sequence repeats approximately every 32.4 seconds: three
  copies each of `08 01 00 00 7E CD`, `08 02 00 00 7A D0`,
  `08 03 00 00 76 D3`, and `08 03 00 02 18 00 BA 75`, then about 20.4 seconds
  of silence. See that capture's `analysis.md`.
- The immediately preceding DR-off idle capture
  [`2026-10-03T134950-idle-baseline`](../captures/rinnai/2026-10-03T134950-idle-baseline/)
  received zero bytes. This is an observed correlation in one appliance
  state, not yet a universal claim that DR-off operation is always silent.
- Capture
  [`2026-10-03T144506-idle-to-dr-on-transition`](../captures/rinnai/2026-10-03T144506-idle-to-dr-on-transition/)
  recorded the transition: silent while DR was off, then the same 4-frame
  cycle began within seconds of pressing DR (first frame at +72.2 s) and
  ran for the rest of the 10-minute capture (17 identical cycles).
- **Decoded**, that cycle is the SGD discovering a UCM: Message Type
  Supported Query for Basic DR, Intermediate DR, and Data-Link, then a
  Maximum Payload Length query — each sent three times about 1 s apart
  with no ACK/NAK on the bus, followed by ~20 s silence.
- The operator saw the adapter RX LED stop about 15 minutes after the
  transition capture started. This was not recorded (capture had ended).
- After discovery stopped, the heater stayed silent for well over 30
  minutes while the DR icon stayed lit. It did not resume on its own and
  did not turn DR off.
- **First active session**
  ([`2026-10-03T161440-first-ucm-probe`](../captures/rinnai/2026-10-03T161440-first-ucm-probe/analysis.md)):
  the heater link-ACKs our queries in ~10–20 ms, App-ACKs "Outside Comm
  Status: Good", answers the operational-state query with `13 01` (Running
  Normal), and answers GetInformation. Its application replies arrive about
  400 ms after its link ACK.
- GetInformation reply: CTA-2045 version **"A"** (not "B"), vendor ID
  `0x0C22`, device type `0x0003` (Water Heater – Heat Pump), device
  revision 4, capability bitmap `0x00000000`. No model/serial/firmware fields.
- Once a UCM answers, the heater repeats its discovery about every 9–10 s
  (instead of ~32 s). Our `19 06` max-payload response went unanswered when
  sent ~150 ms after our ACK, and the retry got NAK `15 07`. The one ACKed
  attempt was sent ~0.9 s after our ACK.

## Hypotheses

_(explicitly mark these as unconfirmed until verified against documentation
or repeated captures)_

- The Rinnai stops UCM discovery after ~15 minutes with no reply,
  consistent with CTA-2045-B §9.1.3 ("no valid communication for more than
  15 minutes → return to defaults"). Needs a ≥ 25-minute capture to confirm,
  and to see whether discovery resumes later.
- ~~Replying `06 00` (link ACK) to the Basic DR query should be enough for
  the Rinnai to treat us as a UCM~~ — confirmed in part: it communicates,
  but max-payload negotiation doesn't complete yet.
- ~~Max payload failure is timing~~ — confirmed: with a 1.0 s reply delay
  every response is ACKed
  ([`2026-10-03T182455-max-payload-delay`](../captures/rinnai/2026-10-03T182455-max-payload-delay/notes.md)).
- ~~The heater still re-runs discovery because we advertised `0x06`~~ — not
  the cause: with `0x07` it still repeats about every 22 s
  ([`2026-10-03T192213-max-payload-256`](../captures/rinnai/2026-10-03T192213-max-payload-256/notes.md)).
  The ~20 s idle between rounds matches the passive cycle, so this is probably
  a normal periodic link check. Untested alternative: the heater expects us to
  query its max payload too (now part of `--probe`).
- The ~10 s repeating discovery is the heater retrying because negotiation
  didn't complete. It should stop once max payload is ACKed.
- ENERGY STAR lists the REHP65 as CTA-2045-B, but this firmware reports
  version "A". It may still accept B-only messages; test case by case.
- ~~The periodic idle packet appears to contain tank temperature~~ —
  disproved for DR-on idle: the periodic frames are discovery queries with
  no data payload. Temperature must be requested by a UCM (Intermediate DR
  GetPresentTemperature, CTA-2045-B §11.1.7).
- A compressor transition may cause a distinct packet or cadence change;
  capture a normal heating transition before treating this as observed.

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
