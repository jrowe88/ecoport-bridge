# What EcoPort Bridge can see, infer and control (Rinnai REHP65)

Status as of 2026-10-04. It is based on these captures in `captures/rinnai/`:

- `2026-10-03T203700-survey`
- `2026-10-03T232424-commodity-1`
- `2026-10-04T082724-heating-cycle-1`
- `2026-10-04T123929-panel-modes-1`

Everything in sections 1–3 uses **read-only** CTA-2045 queries. Section 4
(control) is **untested**.

## 1. Raw data the heater gives us

The heater answers only three read requests. Every other spec-defined read
gets NAK 06 or NAK 07 (see `docs/protocol.md`).

| Request | Field | Observed values | Notes |
|---|---|---|---|
| Basic DR opstate (`12 00`) | Operational state | 0 Idle Normal, 1 Running Normal | Curtailed/heightened/opted-out states exist in the spec but appear only during DR events |
| Commodity Read (`08 02 06 00`) | Electricity rate (W) | 0 / 242–286 / ~4510 / ~4840–4862 | Per-component *estimate*, not metered |
| | Electricity cumulative (Wh) | always 0 | Not implemented |
| | Total energy storage capacity (Wh) | 12011 @120 °F, 12408 @121 °F, 13201 @125 °F; 0 in vacation | Changes only with setpoint or vacation |
| | Present energy take (Wh) | ~250 (full) to 4200+ (after shower) | Heat needed to reach setpoint; 18 Wh resolution |
| GetInformation (`01 01`) | Vendor, type, revision | 0x0C22, HPWH, rev 4, CTA-2045 "A" | Static |

There is no direct tank temperature, setpoint, mode, inlet/ambient temperature,
fault code or metered energy.

## 2. What we can infer

| Want to know | Can we? | How | Confidence |
|---|---|---|---|
| **Heat source running now** | **Yes** | Electricity rate: 0 = off, ~240–290 W = compressor, ~4510 W = element, ~4850 W = element + compressor | High (seen once each; confirm the element-only case) |
| **Setpoint** | **Yes, with calibration** | Total capacity is a fixed function of setpoint. It held 12011 Wh at 120 °F for 14 h (idle, shower, recovery). It is not linear (+397 Wh for 120→121, ~198 Wh/°F for 121→125), so we need a lookup table from a setpoint sweep | High once calibrated |
| **Vacation mode** | **Yes** | Commodity Read returns all zeros | High |
| **Other panel mode** (economy, heat pump only, hybrid, e-heater) | **Partly, over time** | Invisible while idle. Inferred from which heat source runs during recovery: heat pump only never shows the element; e-heater shows element only; economy/hybrid show the compressor first, with the element only under high demand. Needs one or more heating cycles, and separating economy from hybrid needs more data | Medium |
| **Tank "state of charge"** | **Yes, coarse** | 1 − take ÷ capacity. ~95–98 % when idle and full; ~65 % after a shower | Medium: take jitters ±18 Wh and shifts while water mixes after heating |
| **Hot water draws** | **Yes** | A jump in take while idle or running, e.g. shower ≈ +3.5 kWh | Medium |
| **Heating start/stop, run time per source** | **Yes** | Opstate plus rate transitions, at 15 s resolution | High |
| **Energy used** | **Estimate only** | Integrate rate × time per source; the heater's own cumulative counter is always 0 | Medium-low until checked against a clamp meter |
| **Recovery time** | **Yes** | From a draw until opstate returns to 0 | High |
| **Efficiency (COP)** | **No (not reliably)** | Take is too noisy and stratification-dependent, and the rate is an estimate | Low |
| **Faults** | **Unknown** | Opstate 5 (SGD Error) is defined but never seen | — |

## 3. What we could log, track and report

Polling every 15 s (one opstate and one Commodity Read) is plenty and has run
cleanly for 30+ minutes.

**Time series to log:** opstate, rate, capacity, take, derived heat source,
derived state of charge, and markers (setpoint change, vacation, DR events).

**Reports and alerts:**

- **Element use:** count, minutes and estimated kWh per day. This is the most
  useful efficiency alert, since every element minute costs about 15× a
  compressor minute.
- Daily heating hours and estimated kWh, split by compressor and element.
- Hot-water draws per day, with an estimated size (kWh) for each.
- Hot water stored (%) and "time until full" during recovery.
- Setpoint and vacation changes, as audit events.
- Load-shift potential: how much energy could be stored now
  (≈ take, plus capacity headroom with a higher setpoint). This is useful for
  time-of-use or solar scheduling.
- Once control works (section 4), a per-event DR report: did it curtail, for how
  long, was there an override, and what was the rebound afterwards.

In Home Assistant this maps to sensors for heat source, power (W), stored
energy (%), setpoint (°F) and vacation (on/off), plus energy-dashboard entries
from the integrated estimates.

## 4. Control: what CTA-2045 offers (untested on the Rinnai)

So far the Rinnai has accepted every Basic DR message we sent ("Outside comm
status" gets an App ACK). That suggests it implements Basic DR, which is the
mandatory CTA-2045 control set. The Intermediate set (setpoint, temperature
offset, Advanced Load Up) returned NAK 07 on reads, so we expect the
corresponding writes to be unsupported too.

| Basic DR command (`08 01`) | Opcode | Expected HPWH behaviour | How we would see it |
|---|---|---|---|
| Shed | `01 dd` | Avoid heating unless the tank gets too cold | Opstate 4 Idle Curtailed / 2 Running Curtailed; rate drops to 0 |
| End Shed / Run Normal | `02 00` | Cancel any event | Opstate back to 0/1 |
| Critical Peak Event | `0A dd` | Deeper shed than Shed | As Shed |
| Grid Emergency | `0B dd` | Deepest shed | As Shed |
| Load Up | `17 dd` | Heat now, possibly above normal | Opstate 3 Running Heightened / 6 Idle Heightened; compressor starts |
| Request for Power Level | `06 pp` | Limit average power to a percentage | Rate / opstate |
| Present / Next Relative Price | `07`, `08`, `09` | Price-responsive behaviour, if implemented | — |
| Customer Override (from heater) | `11 xx` | Sent when the user overrides at the panel | Opstate 11/12 (opted out); a heater-initiated `11` message |

- `dd` is the event duration: seconds = 2 × dd², and `00` means unknown.
  For example, `0x0F` ≈ 7.5 min and `0x1E` = 30 min.
- The heater App-ACKs a command (`03 xx`) or rejects it with a Basic App NAK
  (`04 rr`). Reason `05` means a customer override is in effect.
- An ACK only means "received and supported". The spec says to query opstate
  to confirm the command took effect.

**What control would enable:**

- Shift heating out of peak-price hours (shed during peak, load up before it).
- Soak up surplus solar (load up when exporting).
- Respond to utility DR events.
- Use the tank as a ~12 kWh thermal battery.

**Control test (approved 2026-10-04; a write to the appliance):**

1. Wait until the heater is idle and full.
2. Send **Shed with a short duration** (`01 0F`, ~7.5 min).
3. Poll opstate and Commodity Read. Draw some hot water to see whether it
   refuses to heat.
4. Send **End Shed** (`02 00`). It reverts anyway when the event duration ends.
5. Then try **Load Up** (`17 0F`) on a partly used tank.

Safeguards, implemented in `tools/ucm.py --allow-control` (see
`docs/getting-started.md`):

- An explicit `--allow-control` flag; without it, `/` commands are refused.
- Commands are typed one at a time by the operator. Each is followed by an
  opstate query.
- End Shed is always sent on exit or Ctrl+C.
- Every event has a duration (default 7.5 min, max 120 min), so it ends even if
  End Shed is lost.
- The panel override always works. The CTA-2045 fallback also returns the heater
  to normal if communication stops.

## 5. Open questions and next experiments

1. **Setpoint sweep:** step the setpoint across its range (1 °F steps near the
   usual setting, 5 °F elsewhere) to build the capacity → setpoint table.
2. **Clamp meter** on the heater circuit during compressor and element runs, to
   validate the rate estimates and the energy integration.
3. **Mode fingerprints:** a heating cycle in each panel mode (economy, hybrid,
   heat pump only, e-heater) after a similar draw.
4. **Shed and Load Up tests** (section 4).
5. **Vendor-specific probing** (deferred): it might expose tank temperature or
   mode directly, but it carries risk; discuss first.
