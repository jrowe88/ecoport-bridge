# Hardware

## Connector

- Physical connector (UCM side): `420B2V12FL0`
- Appliance socket (SGD side): `420C2PM12FL0`
- Rinnai wiring diagram identifies the connector as **CN50 / CTA2045**,
  connected directly to the main control board.

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
