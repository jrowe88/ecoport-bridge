# Reverse Engineering Approach

The plan is to solve as much of the CTA-2045 protocol as possible *before*
ever touching the Rinnai heater, by keeping protocol reverse engineering
separate from electrical reverse engineering.

## Phase 1 — EPRI simulator (no appliance hardware required)

EPRI provides a UCM simulator/reference app that implements the CTA-2045
protocol. This is the primary source of ground truth for message framing,
CRC, and the Basic DR / Intermediate message sets. Goal: get the Python
CTA-2045 implementation talking to the simulator and decoding traffic
cleanly.

## Phase 2 — Passive capture (receive-only)

Once hardware arrives:

- Verify connector pinout and orientation before soldering anything.
- Wire up **receive-only** first — keep the Rinnai in "observe, don't poke"
  mode.
- Record captures under `captures/rinnai/<date>-<scenario>/` for a variety of
  operating states (idle, heating, setpoint changes, faults if safely
  reproducible).

## Phase 3 — Active queries

Once passive decoding is solid, move to sending read-only queries (Get
Information, Get Present Temperature, Get Set Point, Get Operating State,
Get Commodity) and confirm responses match hypotheses from Phase 2.

## Phase 4 — Write commands

Only after read-path behavior is well understood and documented, carefully
test write/control commands (Set Point, Shed, Critical Peak, Load Up, End
Shed) — these directly affect appliance behavior.

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
