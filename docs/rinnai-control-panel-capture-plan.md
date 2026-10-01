# Rinnai Control-Panel Passive-Capture Plan

This is an **operator script**, not an automation script. The laptop runs
`tools/sniff.py` in receive-only mode while the operator makes ordinary,
deliberate changes from the Rinnai control panel. The bridge must send **no**
CTA-2045 bytes during every capture in this plan.

The purpose is to identify which byte sequences correlate with a *known
physical event* before attempting to decode or send CTA-2045 messages. Do not
assume every Rinnai control-panel option exists on every REHP65 firmware
revision: record the exact on-screen label shown by this specific heater.

## Safety and operating rules

- Follow the wiring and isolation procedure in
  [`hardware.md`](hardware.md). Connect only pin 1 (Data-), pin 7 (Data+),
  and, where the adapter calls for it, pin 8 (Signal Ground). Never connect
  pins 5 or 12; they are AC line pins.
- Each capture is **passive**. `sniff.py` never writes to the serial port.
  Do not run `query.py`, `interactive.py`, or any future active tool at the
  same time.
- Make only control-panel changes you would be comfortable making during
  normal household operation. Do not defeat safeties, remove covers while
  energized, force a heating cycle by unsafe means, or leave the heater in a
  non-preferred mode when testing ends.
- Keep a capture running for a quiet baseline both before and after an
  action. Heating equipment can delay a state transition by several minutes.
- In `notes.md`, use local timestamps and write the exact display text,
  original setting, new setting, and visible response.

## Preparation

1. Identify the USB-RS485 adapter's Windows COM port in Device Manager.
2. Confirm the adapter is **galvanically isolated** and check its manual for
   its A/B naming. The project pinout calls pin 7 **Data+** and pin 1
   **Data-**; adapter labels are not standardized.
3. Record the initial display: operating mode, setpoint, visible heater
   status, and approximate tank-temperature display if available.
4. Start a five-minute idle capture:

   ```powershell
   cd C:\Dev\jrowe88\ecoport-bridge
   python tools\sniff.py --port COM3 --scenario idle-baseline --duration 300
   ```

   Replace `COM3` with the adapter's actual port. The tool creates
   `captures\rinnai\<timestamp>-idle-baseline\` containing raw bytes,
   timestamped receive events, and a notes template.

## Capture matrix

Run each row as a separate capture. If the named panel option is absent,
write **not available on this unit** in the notes rather than substituting an
unrelated option.

| Scenario | Manual control-panel action | Capture timing | What to record in `notes.md` |
|---|---|---|---|
| Idle baseline | Do nothing; leave the heater in its normal mode. | 5–20 minutes. | Current mode, setpoint, heater status, any periodic display changes. |
| Mode inventory | Open the mode/menu screen but do not select a new mode. | 30–60 seconds before/after opening. | Every mode exactly as named on the display. |
| Heat Pump Only | If present, select **Heat Pump Only** from the panel. | 2 minutes before; 5–10 minutes after. | Previous mode, displayed new mode, acceptance/confirmation behavior. |
| Normal/restored mode | Return to the exact pre-test mode. | 2 minutes before; 5–10 minutes after. | Original mode and confirmation of restoration. |
| Other mode | Select one other available mode, one at a time (for example an energy-saving, high-demand, electric, hybrid, or vacation mode **only if displayed by this unit**). | Separate 5–10 minute capture for each transition. | Exact label, prior mode, resulting status, whether heating behavior changed. |
| Setpoint +1 | Increase the setpoint by **1°F** using the panel. | 2 minutes before; 5–10 minutes after. | Old/new setpoint and whether the display requested confirmation. |
| Setpoint restore | Restore the original setpoint. | 2 minutes before; 5–10 minutes after. | Restored value and observed status. |
| Heating transition | During normal use, capture a legitimate transition from idle to heating. A small +1°F setpoint change may request heat, but does not guarantee immediate compressor activity. | Start 5 minutes before the request; continue until status stabilizes or 30 minutes elapsed. | Trigger, start time, display changes, compressor/fan indications, and any backup/resistance indicator. |
| Heating steady state | Leave the heater in a normal heating state. | 10–20 minutes. | Mode, setpoint, display status, periodic changes. |
| Return to baseline | Restore the original mode and setpoint; allow the unit to settle. | 10 minutes. | Final mode, setpoint, status, and any test cleanup. |

## Suggested commands

Use one command for each row. Examples:

```powershell
python tools\sniff.py --port COM3 --scenario heat-pump-only --duration 600
python tools\sniff.py --port COM3 --scenario setpoint-plus-one --duration 600
python tools\sniff.py --port COM3 --scenario heating-transition --duration 1800
```

For an open-ended observation, omit `--duration` and stop with `Ctrl+C`:

```powershell
python tools\sniff.py --port COM3 --scenario normal-household-cycle
```

## After each capture

1. Complete the generated `notes.md` before starting the next capture.
2. Do **not** rename/edit `capture.bin`; it is the exact source artifact.
3. Compare the `capture.jsonl` event timestamps against the operator
   timeline. This establishes correlations only; it does not establish a
   protocol meaning.
4. Commit useful small captures and their notes under `captures/rinnai/`.
   Keep unusually large raw captures out of Git if necessary, but retain
   their notes and a reproducible way to locate the original file.
5. Promote only repeated, corroborated conclusions into
   [`protocol.md`](protocol.md), clearly marking them Confirmed, Observed, or
   Hypothesis.
