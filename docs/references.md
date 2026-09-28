# References & Prior Art

Sources and tools identified during initial project planning. Treat vendor
docs/marketing pages as directional, not authoritative — cross-check against
captures and EPRI's reference implementation where possible.

## Rinnai / appliance

- **ENERGY STAR** product listing for the Rinnai REHP65 — identifies it as
  CTA-2045-B / EcoPort capable with built-in communications architecture.
- **Rinnai installation/service manual & wiring diagram** — identifies the
  onboard connector as **CN50 / CTA2045**, wired directly to the main control
  board. Confirms the CTA module plugs into this port but (in the
  homeowner-level manual) does not itself give the full pin assignment.
- **EcoPort certified product database** (public) — Rinnai's REHP series is
  listed as a certified EcoPort product. Certification test results/criteria
  may reveal expected compliant behavior beyond the homeowner manual.
- **OpenADR Alliance** — administers the EcoPort certification program;
  states the EcoPort test program verifies a product's sending/receiving of
  CTA-2045 messages.

## CTA-2045 standard & reference implementations

- **ANSI Webstore** — CTA-2045 standard preview. Confirms the AC-form-factor
  connector has explicit RS-485 connections and a mechanical/pinout section,
  though the full standard (and detailed pin figures) is paywalled. Also
  confirms Basic DR message type = `0x08 0x01` and Intermediate DR message
  type = `0x08 0x02`.
- **[ASHB — Ken Wacks' Perspectives: Appliances Designed for Energy
  Management](https://www.ashb.com/ken-wacks-perspectives-appliances-designed-for-energy-management/)**
  — publishes a full 12-pin CTA-2045 UCM connector pinout diagram
  (reproduced at `docs/images/cta2045-connector-pinout-ashb.png`). This
  independently corroborates the RS-485/ground pins identified from the
  EPRI simulator cable, and additionally identifies pins 5 and 12 as AC
  line voltage, pin 3 as reserved, and pin 10 as earth ground (distinct from
  pin 8 signal ground). See `docs/hardware.md` for the full table.
- **EPRI CTA-2045 Desktop Simulator** — acts as either a UCM or SGD; supports
  the data-link layer plus Basic and Intermediate DR messages. Primary
  "protocol oracle" for developing and testing our implementation without
  appliance hardware.
- **EPRI CTA-2045 UCM C++ Library** (GitHub, **BSD-3-Clause**) — reference
  implementation of the CTA-2045 protocol; sample app demonstrates
  communication with an SGD over a serial port.
- **EPRI overview / REST service docs** — state the AC UCM interface uses
  RS-485 and normally operates at **19.2 kbps** initially; describes the
  distinction between AC and low-voltage CTA-2045 form factors.
- **`python-cta2045`** (PyPI) — a Python CTA-2045 implementation covering
  Basic DR, Intermediate DR, message encoding/decoding, frame parsing,
  CRC/checksum, device/application enums, temperature, setpoint,
  commodity/energy info, `GetInformation`, and Advanced Load Up. Deliberately
  keeps the physical RS-485 layer separate from the protocol layer — this is
  the architectural split `ecoport-bridge` follows (see
  `docs/architecture.md`). Used as a design reference; not currently a
  runtime dependency of this repo.

## Connectors (DigiKey)

- **`420B2V12FL0`** — 12-position, 4.2 mm dual-row **male header**,
  **solder-cup/through-hole termination**. This is the part that mates with
  the Rinnai's onboard CTA-2045 port. No crimp tooling required — wires are
  soldered directly to the header pins.
- **`420C2PM12FL0`** — female panel-mount **socket housing**. This is the
  SGD-side part EPRI uses when building a socket for a UCM to plug into. Not
  needed for our prototype (we are building the UCM side, not simulating an
  SGD).
- **`420CP-T-2`** — crimp contacts (18–22 AWG) for the `420C2PM12FL0` female
  socket. Not needed for our prototype, for the same reason as above.

## Why we probably don't need to buy the CTA-2045 standard

Between EPRI's C++ implementation, EPRI's simulator, EPRI's documentation,
`python-cta2045`, the ANSI preview, the Rinnai service manual, the real
appliance, our own packet captures, the EcoPort certification database, and
OpenADR documentation, most of what's needed to build a working
implementation is already publicly available — without reproducing the
proprietary standard text itself.
