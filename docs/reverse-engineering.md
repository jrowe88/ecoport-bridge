# Reverse Engineering Approach

The plan is to solve as much of the CTA-2045 protocol as possible *before*
ever touching the Rinnai heater, by keeping protocol reverse engineering
completely separate from electrical reverse engineering. We can likely solve
80–90% of the protocol without ever touching the heater.

## Phase 0 — Use EPRI as the protocol oracle

Rather than trying to reconstruct the CTA-2045 standard from scratch (or pay
for it), treat EPRI's published material as ground truth first:

- **EPRI CTA-2045 Desktop Simulator** — acts as either a UCM or SGD; supports
  the data-link layer plus Basic and Intermediate DR messages.
- **EPRI CTA-2045 UCM C++ Library** (BSD-3-Clause) — reference protocol
  implementation; sample app demonstrates serial communication with an SGD.
- **`python-cta2045`** (PyPI) — an existing Python implementation with a
  similar architecture to what we want: protocol/message layer kept separate
  from the physical RS-485 transport. Useful as a design reference for our
  own `ecoport.cta2045` package.

See `docs/references.md` for links and more detail on each.

Goal for this phase: get our own CTA-2045 implementation to successfully
perform, against the EPRI simulator (no Rinnai hardware involved):

1. link initialization
2. capability query
3. Basic DR query
4. temperature query
5. setpoint query
6. commodity/energy query

EPRI's sample program additionally demonstrates: present temperature,
setpoint, temperature offset, commodity, power level, operating state, shed,
end shed, load-up. That list doubles as a roadmap for our own test suite
(see `python/tests/cta2045/`).

## Phase 1 — Electrical/pinout reconnaissance

Before transmitting anything, answer: *what exactly is electrically present
on the Rinnai CTA port?* Build a sacrificial breakout cable and positively
identify the RS-485 pair and ground using the safety procedure in
`docs/hardware.md` — don't assume pin function from the wiring diagram
alone, since the AC-form-factor connector can carry line voltage on other
contacts.

## Phase 2 — Passive capture (receive-only)

Once hardware arrives:

- Verify connector pinout and orientation before soldering anything — see
  the safety procedure in `docs/hardware.md` (de-energized continuity trace
  first, then verify energized contacts before connecting any adapter).
- Wire up **receive-only** first — keep the Rinnai firmly in "observe, don't
  poke" mode.
- Once the RS-485 pair is identified, you don't need the full electrical
  spec to start sniffing: an isolated USB-RS485 adapter (plus an
  oscilloscope/logic analyzer and high-impedance differential probe, if
  available) at 19,200/8/N/1 is enough to confirm the heater is talking at
  all before writing any decoding logic.
- Record captures under `captures/rinnai/<date>-<scenario>/` for a variety of
  operating states (idle, heating, setpoint changes, faults if safely
  reproducible).

## Phase 3 — Active queries (Stage B)

Once passive decoding is solid, move to sending only **harmless, read-type**
commands: Get Information, Get Present Temperature, Get Set Point, Get
Operating State, Get Commodity — and confirm responses match hypotheses from
Phase 2.

## Phase 4 — Controlled writes (Stage C)

Only after read-path behavior is well understood and documented, carefully
test write/control commands, roughly in this order:

1. **Setpoint ± 1°F** — a small, easily-verified, low-risk first write test.
2. **Shed** / **End Shed**
3. Eventually controlled DR commands: **Critical Peak**, **Load Up**.

## Building a capability matrix

CTA-2045 defines a broad interface, but Rinnai's firmware won't necessarily
implement every part of it. As functions are confirmed (or ruled out) via
captures/experiments, record them in the capability matrix in
`docs/protocol.md`, distinguishing "not implemented" vs. "implemented but
undocumented" vs. "Rinnai uses the mechanism differently." The **EcoPort
certified product database** and **OpenADR Alliance** certification criteria
(see `docs/references.md`) are additional sources of insight here, since
Rinnai's REHP series is a certified EcoPort product.

## First milestone

Don't start by trying to *control* the water heater. The first goal is
purely observational:

> "I can plug my device into the Rinnai, capture CTA-2045 traffic, decode it
> into human-readable messages, and continuously display the heater's
> state."

For example, a simple status readout:

```text
Rinnai REHP65
─────────────────────────────
CTA-2045: connected
Protocol: CTA-2045-B
Operating state: Running
Water temperature: 127.4°F
Setpoint: 125°F
Power level: 63%
Commodity: Electricity
Energy: 2.31 kWh
DR event: none
```

Once that milestone is reached, control (setpoint changes, shed/DR commands)
is comparatively straightforward.

## Recording results

For each capture, write a `notes.md` describing what was physically
happening on the appliance, e.g.:

```text
Observed:
- Heater idle for 20 minutes
- Packet X appears every ~30 sec
- Packet Y appears immediately after compressor starts
- Setpoint changed from 125°F -> 130°F at 14:32
```

Then roll confirmed conclusions up into `docs/protocol.md`, keeping
Confirmed/Observed/Hypotheses clearly separated.
