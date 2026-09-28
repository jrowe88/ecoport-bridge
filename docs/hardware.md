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

## Pinout (confirmed)

Only 3 of the connector's pins are used:

| Pin | Signal |
|-----|--------|
| 1   | RS-485 D- |
| 7   | RS-485 D+ |
| 8   | Signal ground |

All other pins are left unconnected.

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
UCM to draw power from the appliance over the same connector — the standard
distinguishes AC and low-voltage form factors, and the AC interface can
carry line voltage on some contacts. Do not assume the pins are D+/D-/GND
just because that's the expected wiring.

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

## Bridge hardware revisions

- `hardware/rev-a/` — first PCB revision (schematic, PCB, gerbers, BOM,
  assembly notes)
- `hardware/enclosure/` — 3D-printable enclosure (OpenSCAD source + STL
  exports + print settings)

## Shopping list (initial)

- `420B2V12FL0` mating connector
- Isolated USB-RS485 adapter
- 3-wire cable (D+, D-, GND)
- No crimp tooling required for the initial bring-up
