# Hardware

## Connector

- Physical connector (UCM side, mates with the Rinnai): **`420B2V12FL0`** —
  12-position, 4.2 mm dual-row **male header**, **solder termination**. No
  crimp tooling required; wires are soldered directly to the pins.
- Rinnai wiring diagram identifies the onboard connector as **CN50 /
  CTA2045**, connected directly to the main control board.

### Connectors we do *not* need for the UCM prototype

EPRI's documentation describes two other related parts that are easy to
confuse with the one above, but both belong to the *SGD-side* socket (i.e.
the part an appliance manufacturer would use), not our UCM plug:

| Part | What it is | Needed for our prototype? |
|------|------------|----------------------------|
| `420B2V12FL0` | Male header, solder pins (mates with Rinnai) | **Yes** |
| `420C2PM12FL0` | Female panel/socket housing (SGD side) | No |
| `420CP-T-2` | Crimp contacts (18–22 AWG) for the female socket | No |

So the full shopping list stays small: `420B2V12FL0` + an isolated
USB/RS-485 adapter + a 3-wire cable. No crimp tooling at all.

## Pinout (confirmed — full 12-pin map)

Independently confirmed via a published CTA-2045 UCM connector pinout
diagram (see `docs/images/cta2045-connector-pinout-ashb.png`, source:
[ASHB — Ken Wacks' Perspectives: Appliances Designed for Energy
Management](https://www.ashb.com/ken-wacks-perspectives-appliances-designed-for-energy-management/)).
This corroborates the pins we identified from the EPRI UCM simulator cable
(1, 7, 8) and additionally identifies the two AC line pins:

| Pin | Signal | Pin | Signal |
|-----|--------|-----|--------|
| 1   | Data- (RS-485) | 7  | Data+ (RS-485) |
| 2   | No connection  | 8  | Signal Ground |
| 3   | Reserved       | 9  | No connection |
| 4   | No connection  | 10 | Earth Ground |
| 5   | **AC Line 2**  | 11 | No connection |
| 6   | No connection  | 12 | **AC Line 1** |

Only pins **1, 7, and 8** are used for our bridge (RS-485 D-, D+, and Signal
Ground). All other pins are left unconnected — **critically, pins 5 and 12
carry actual AC line voltage** and must never be connected to our isolated
RS-485 adapter. Pin 10 (Earth Ground) is distinct from pin 8 (Signal
Ground); do not bond them together without understanding the implications.

```text
420B2V12FL0
   │
   ├── pin 7 ── wire ── RS-485 +
   ├── pin 1 ── wire ── RS-485 -
   └── pin 8 ── wire ── GND
```

## Electrical interface

- RS-485, isolated
- 19,200 baud, 8 data bits, 1 stop bit, no parity (per EPRI documentation)
- This is a mains-adjacent AC-form-factor CTA-2045 interface — always use an
  isolated USB/RS-485 (or isolated UART) adapter.

## ⚠️ Safety: identify pins before connecting anything

The CTA-2045 **AC-form-factor** connector is explicitly designed to allow a
UCM to draw power from the appliance over the same connector, and we now
have a confirmed pinout showing **pins 5 and 12 carry actual AC line
voltage** (see Pinout above). Even with a published pinout in hand, treat it
as a starting hypothesis to verify against your specific unit, not a
substitute for careful handling — wiring can vary by revision, and mistakes
here are line-voltage mistakes.

Recommended procedure, in order:

1. **De-energize the heater completely.**
2. With the unit unpowered, use continuity/resistance tracing against the
   Rinnai wiring diagram to positively identify which physical pins
   correspond to the CTA-2045 / CN50 signals.
3. Re-close/re-energize the unit and, using appropriate measurement
   equipment (meter, isolated scope probe), confirm which contacts are
   actually energized before connecting any adapter.
4. **Never connect a cheap USB-RS485 adapter to an unidentified CTA pin.**
5. Prefer a galvanically isolated RS-485 adapter/transceiver at every stage
   — even if the CTA communications ground turns out to be benign, isolation
   gives a much safer failure boundary.

Only pins 1, 7, and 8 should ever need to be connected (see Pinout above);
leave all nine other pins completely unconnected.

## Bring-up hardware platform

For the **first prototype**, prefer a Raspberry Pi or Linux laptop over an
ESP32/MCU — debugging (logging, REPL, packet capture tooling) is
dramatically easier:

```text
Raspberry Pi / laptop
    │
   USB
    │
isolated RS-485 adapter
    │
CTA-2045 breakout (420B2V12FL0)
    │
  Rinnai
```

For the **permanent/production bridge**, an MCU (ESP32 / RP2040 / STM32)
behind the same isolated RS-485 stage, exposing MQTT/HTTP/Wi-Fi upstream, is
the target (see `firmware/`):

```text
ESP32 / RP2040 / STM32
        │
 isolated RS-485
        │
    CTA-2045
```

## Power

**The CTA-2045 connector does not provide a regulated low-voltage supply
(no 5V/12V logic rail).** Pins 5 and 12 are raw **AC Line 1/2** — i.e. mains
voltage, not clean DC. This matches how the CTA-2045 AC-form-factor UCM spec
is designed: a compliant AC UCM is expected to be a self-contained module
that taps mains off pins 5/12 and performs its own AC→DC conversion
internally. The appliance does not hand the UCM regulated logic-level power.

Since this project deliberately only connects pins 1/7/8 (RS-485 D-, D+,
signal ground) and avoids the AC pins for isolation/safety reasons (see
Safety section above), the bridge board needs to be powered independently
of the CTA-2045 connector:

- **Prototype / bring-up (current plan):** power the Pi/laptop/ESP32 from
  USB, same as the isolated RS-485 adapter. No mains wiring inside the
  bridge enclosure at all — simplest and safest option, and what
  `hardware/rev-a/` should target.
- **Production bridge, external supply:** a small wall-wart / USB power
  adapter feeding the ESP32 board, mounted near (but electrically separate
  from) the CTA-2045 connector.
- **Production bridge, parasitic AC power (future/optional):** a fully
  self-contained module that draws power from pins 5/12 via an isolated
  AC-DC converter (with appropriate fusing, creepage/clearance, and
  enclosure requirements), so the bridge needs no external wall-wart at
  all — closer to how a certified CTA-2045 UCM is designed. This is a
  materially bigger safety/compliance undertaking (mains-to-low-voltage
  conversion inside our own enclosure) and should be treated as a distinct,
  later hardware revision — not part of Rev A.

## Bridge hardware revisions

- `hardware/rev-a/` — first PCB revision (schematic, PCB, gerbers, BOM,
  assembly notes)
- `hardware/enclosure/` — 3D-printable enclosure (OpenSCAD source + STL
  exports + print settings)

## Shopping list (initial)

- `420B2V12FL0` mating connector
- Isolated USB-RS485 adapter
- 3-wire cable (D+, D-, GND)
- USB power supply / cable for the Pi or ESP32 board (the CTA-2045
  connector does not supply regulated power — see Power section above)
- No crimp tooling required for the initial bring-up
