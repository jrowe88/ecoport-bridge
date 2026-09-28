# EcoPort Bridge

EcoPort Bridge is a hobby reverse-engineering and hardware project to build a
communication bridge for appliances using the **CTA-2045 "EcoPort"** interface
(AC-form-factor UCM connector, RS-485 @ 19,200 baud) — starting with a
**Rinnai REHP65** heat pump water heater (CTA-2045-B / SGD, connector CN50,
physical connector part `420B2V12FL0`).

The goal is to expose the appliance's CTA-2045 data (tank temperature,
setpoint, operating state, energy, demand-response/shed status, faults, etc.)
to home automation systems (MQTT, Matter, Home Assistant) via a small
ESP32-class bridge device, built on top of a Python implementation of the
CTA-2045 protocol.

## Design principles

- **Generic CTA-2045 core, separate from appliance-specific logic.** The
  `cta2045` layer implements the protocol (framing, messages, CRC, link,
  Basic DR / Intermediate messages). Appliance-specific behavior (e.g. Rinnai
  REHP65 quirks and capability mappings) lives under `appliances/rinnai` (or
  `appliance/rinnai` in firmware) so other CTA-2045/EcoPort appliances can be
  supported later without untangling Rinnai assumptions from the protocol
  implementation.
- **Protocol reverse engineering is kept separate from electrical reverse
  engineering.** Most of the protocol can be worked out via the EPRI UCM
  simulator without ever touching the heater.
- **Facts vs. hypotheses are recorded separately** in `docs/protocol.md` and
  per-capture notes, so reverse-engineering guesses don't quietly turn into
  "the spec" later.
- **Captures are first-class project artifacts.** Raw captures + notes live
  under `captures/` as a reproducible dataset, not a pile of Wireshark
  screenshots.

## Architecture

```text
                   ┌───────────────────────────────┐
                   │       Your application        │
                   │ Home Assistant / MQTT / etc.  │
                   └───────────────┬───────────────┘
                                   │
                         Python CTA-2045 API
                                   │
                   ┌───────────────▼───────────────┐
                   │       CTA-2045 protocol       │
                   │ link + Basic DR + Intermediate│
                   └───────────────┬───────────────┘
                                   │
                              RS-485 UART
                                   │
                   ┌───────────────▼───────────────┐
                   │       Isolated RS-485         │
                   │       transceiver             │
                   └───────────────┬───────────────┘
                                   │
                         CTA-2045 AC connector
                                   │
                   ┌───────────────▼───────────────┐
                   │       Rinnai REHP65           │
                   │          SGD / CN50           │
                   └───────────────────────────────┘
```

## Hardware notes (confirmed)

- Physical connector: `420B2V12FL0` (EPRI's "UCM Connector")
- Only pins **1 (D-), 7 (D+), 8 (GND)** are used; all other pins are left
  unconnected.
- EPRI documents 19,200 baud, 8 data bits, 1 stop bit, no parity.
- This is a mains-adjacent AC-form-factor interface — treat it with a
  deliberate electrical isolation strategy (isolated USB/RS-485 adapter).

See `docs/hardware.md` and `docs/protocol.md` for details, and `captures/`
for recorded traffic.

## Repository layout

```text
ecoport-bridge/
│
├── README.md
├── LICENSE
├── CONTRIBUTING.md
│
├── docs/                 # architecture, hardware, protocol, HOWTOs
├── hardware/             # PCB revisions + 3D-printed enclosure
├── firmware/             # ESP32 bridge firmware (PlatformIO)
├── python/               # ecoport Python package (protocol + tooling)
├── tools/                # CLI tools: sniff, decode, query, interactive
├── captures/             # recorded protocol traffic + notes (EPRI + Rinnai)
├── test/                 # cross-cutting test fixtures/integration tests
└── scripts/              # dev setup / flashing helpers
```

## Status

Early planning / hardware on order. See `docs/reverse-engineering.md` and
`captures/` as the project progresses.

## License

MIT — see [LICENSE](LICENSE).
