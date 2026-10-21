# Rev A Assembly Notes

Board: `hardware/board/ecoport-bridge-v1` (KiCad). PCB fab: OSH Park, 2-layer,
1.6 mm, 1 oz. Assembly: hand-placed SMD, toaster-oven reflow, hand-soldered
through-hole.

> ⚠️ **This board carries 240 VAC** on J1 pins 5/12, F1 and U1 (IRM-05-5).
> Everything inside the "Danger High Voltage" outline is live whenever J1 is
> mated to an energized appliance. Do all bring-up you can **before** U1 and
> J1 are fitted (see build order below).

## Build order (overview)

| Stage | What | Method |
|-------|------|--------|
| 1 | All top-side SMD parts (incl. F1, U6, LED strip D2/D3) | Stencil + paste + reflow |
| 2 | Low-voltage through-hole: J2, J5, J6 (wires later), J7, TP1–TP4, TP14 | Hand solder |
| 3 | Low-voltage bring-up: bench 5 V, flash firmware, RS-485 loopback | — |
| 4 | Mains parts: J1 (pins pulled), U1 | Hand solder |
| 5 | Isolation checks, then first mains power-up | — |
| 6 | Break off LED strip, wire it, install in enclosure | — |

All SMD parts are on the top side (F.Cu), so only one reflow pass is needed.
J1 mounts on the **bottom** side and is soldered from the top.

## 1. Preparation

- [ ] Inspect bare boards: no shorts on the mains side, slot and mouse-bites
      cleanly routed, hazard silkscreen legible.
- [ ] Moisture-sensitive parts: if the bag has been open for long or the
      humidity card shows moisture, bake **U6 (ESP32-C6-WROOM-1, MSL 3)** and
      **D2/D3 (SK6812)** per their datasheets before reflow. Popcorning the LEDs
      is the most common toaster-oven failure.
- [ ] Stencil: TODO (e.g. OSH Stencils, polyimide, top paste layer only).
- [ ] Solder paste: TODO. Leaded Sn63/Pb37 (peak ≈ 215–225 °C) is gentler
      on the SK6812s and on a toaster oven than SAC305 (peak ≈ 245 °C).
      Pick one and match the profile below.

## 2. SMD placement and reflow

Open [`ibom.html`](ibom.html) in a browser while placing: click a BOM line to
highlight its pads on the board. Regenerate it (Interactive HTML BOM plugin)
whenever the PCB changes.

Placement tips:

- **Polarized / pin-1 parts:** D1 (SM712), D2/D3 (SK6812 notched corner),
  U2, U3, U4, U5, U7, U8. Check pin-1 against silkscreen before reflow.
- **Fine pitch:** U3 (TSSOP-8, 0.65 mm), U5 and U8 (SC-70, 0.65 mm), J4 (USB-C) —
  go light on paste; inspect for bridges afterwards.
- **U6 ground pad:** don't flood the module's center pads with paste; too
  much paste floats the module and opens the edge castellations.
- **F1 (SF-2410SP100W-2, 1 A time-lag)** reflows like any SMD part. Check the
  marking letter is **E** (1.00 A), not C (0.50 A).
- **J3 (Qwiic)** is the top-entry BM04B-SRSS-TB, matching the footprint.

Reflow profile (toaster oven):

- TODO: oven model, controller, thermocouple placement, and measured profile.
- Placeholder targets (Sn63/Pb37): preheat to ~150 °C over 60–90 s, soak
  150–180 °C for 60–90 s, peak 215–225 °C, time above 183 °C 45–75 s, cool
  with the door cracked — don't shock-cool.
- Don't exceed the lowest peak rating on the board (check the SK6812 and
  ESP32-C6 module datasheets once the paste is chosen).

After reflow:

- [ ] Inspect under magnification: bridges on U3, U5, U8, J4; tombstoned 0603s;
      ESP32 castellations wetted on all sides.
- [ ] Clean flux residue (IPA). Flux residue on the mains side lowers surface
      insulation — clean it thoroughly.

## 3. Low-voltage through-hole

- [ ] J2 (RS-485 termination) and J5 (Vendor pin → GND) 2-pin headers.
- [ ] J7 (JST PH 3-pin, LED connector).
- [ ] TP1–TP4, TP14 (Keystone 5000-series test points).
- [ ] Jumper shunt H1 on **J2** if this bridge is the end of the RS-485 line
      (normally yes: point-to-point to the appliance).
- [ ] J5: leave open unless the Vendor pin needs grounding. TODO: confirm
      the intended default.
- Leave **U1 and J1 off** for now.

## 4. Low-voltage bring-up (no mains)

USB VBUS feeds +5V through U8 (LM66100 ideal diode, ~40 mV drop), so the
board can run from USB alone while U1 is not fitted. Do the first power-on
from a current-limited bench supply anyway, so a short can't take out the
PC's USB port.

1. [ ] Unpowered resistance checks: +5V (TP3) to GND (TP1) and +3V3 (TP4) to
       GND should be well above a short (caps charge up; no hard short).
2. [ ] Bench supply 5.0 V, **current limit ~150 mA**, into TP3 (+5V) / TP1
       (GND).
3. [ ] TP4 reads 3.3 V ± 3 %. Idle current reasonable (record it: ____ mA).
4. [ ] Disconnect bench supply. Connect USB-C to the PC: TP3 reads ~4.9–5.1 V
       (VBUS minus U8's drop) and TP4 reads 3.3 V.
5. [ ] ESP32-C6 enumerates as USB Serial/JTAG. Hold BOOT (SW2), tap RESET
       (SW1) to enter download mode if needed. Flash firmware.
6. [ ] Firmware smoke tests: LED strip data (with strip still attached),
       Qwiic/I²C scan, RS-485 TX/RX (scope on TP5/TP6, or loopback to an
       isolated USB-RS485 adapter). The CC resistors only advertise default
       USB current (500 mA on USB 2.0); a weak port can sag with Wi-Fi TX
       plus LEDs at full white.
7. [ ] Reverse-block check: with USB still connected, apply 5.2 V from the
       bench into TP3. The bench should supply the board's current, and
       VBUS (meter at C16) should stay at the PC's level, not 5.2 V.
       Blocking relies on U8's CE (pin 3) being tied to VOUT (+5V); with CE
       grounded the LM66100 is just an always-on switch and VBUS follows the
       bench. If VBUS rises toward 5.2 V, check U8 pin 3.

## 5. Mains parts

J1 is the EcoPort CTA-2045 connector (`420B2V12FL0`). **Pins 4, 6, 9, 10 and
11 are not used and must be pulled before soldering.** They sit next to the
AC pins (5 and 12) or F1. The footprint has no pads for them. Leaving them in
would put exposed metal next to mains and roughly halve the creepage distance.
Pin 10 is the appliance's earth ground. The bridge has no use for earth (the
IRM-05 is a Class II module and nothing else references it), and pulling it
removes the only protective-earth metal near F1 and the AC traces. Pin 9 is a
no-connect, but its floating pad sat 4.4 mm from F1 and 1.6 mm from pin 8
(signal GND), shortening the mains-to-SELV creepage path.

The connector has no pegs or hold-downs, so the seven remaining solder joints
(1, 2, 3, 5, 7, 8, 12) carry all the mechanical load. The enclosure should
take insertion and rocking forces off J1 (rib/shoulder around the housing).

- [ ] Pull J1 pins **4, 6, 9, 10, 11** (pliers, straight out from the housing).
      Silkscreen on B.Cu reads "Pull Pins".
- [ ] Fit J1 on the **bottom** side; solder from the top. Full fillets on
      pins 5 and 12; trim leads flush, no spikes.
- [ ] Fit U1 (IRM-05-5) and solder all four pins.
- [ ] Clean flux around J1, F1, U1 thoroughly (IPA + brush), let dry.
- [ ] Inspect: no solder balls, wire strands, or flux bridges anywhere in
      the mains zone or across the isolation slot.

## 6. Isolation checks (before first mains power)

With nothing connected to J1:

- [ ] Continuity J1 pin 12 → F1 → U1 AC/N (fuse intact, ~0.08 Ω for 1 A).
- [ ] Continuity J1 pin 5 → U1 AC/L.
- [ ] **Open circuit** from J1 pin 5 and pin 12 to each of: GND (TP1), +5V
      (TP3), +3V3 (TP4), J1 pins 1/7/8 (RS-485), USB-C shell.
- [ ] Optional but recommended: insulation-resistance test at 500 VDC
      between shorted AC pins (5+12) and GND. IRM-05 spec is ≥ 100 MΩ;
      expect similar or better. **Don't hipot above 500 VDC** without
      knowing what you're doing; the IRM-05 is rated 4.2 kVAC but other
      parts may not be.

## 7. First mains power-up

The IRM-05 accepts 85–305 VAC, so do the first power-up on **120 VAC from
an isolation transformer** (and ideally a variac / current-limited bulb
tester), not on the appliance.

- [ ] Board on an insulated surface, nothing touching it, USB disconnected.
- [ ] Apply 120 VAC to J1 pins 5/12 via a test harness. Measure +5V at TP3
      and +3V3 at TP4 with the meter clipped on **before** power is applied.
- [ ] Power off, wait for caps to discharge, then connect to the appliance
      (de-energized), then energize the appliance.

Bench safety:

- **Never connect USB to a PC while J1 is mated to the appliance** until
  you've measured J1 pin 8 (signal ground) to Earth on your unit. The
  RS-485 side isn't isolated, so USB ties the PC's ground to the
  appliance's signal ground. Use an isolated USB hub for live debugging.
- A GFCI doesn't protect against line-to-line contact on 240 V split
  phase.

## 8. LED strip and enclosure

- [ ] Break the LED strip off at the mouse-bites (after functional test);
      file the nubs flush. On the main board the nubs face U1's mains area,
      so file them carefully and clean up debris.
- [ ] Wire strip J6 (5V / GND / DIN) to main-board J7 with a 3-wire lead.
      J7 is JST PH: use a PHR-3 housing with pre-crimped SPH leads (avoids
      needing a crimp tool). Route the lead away from the mains side.
- [ ] Install in enclosure (`hardware/enclosure/`). No exposed mains
      metal reachable with the lid on.

## Open items (from design review, 2026-10-17)

- [x] Fuse rating: 0.5 A → 1 A time-lag (SF-2410SP100W-2) for inrush margin.
- [ ] F1 land pattern to Bourns recommended (3.00 × 2.80 mm pads, 8.60 mm).
- [ ] Copper-to-edge ≥ 0.381 mm for OSH Park (zones, LED_5V, EN). J1 pads 9
      and 10 were removed when those pins were pulled.
- [ ] 3D model `420B2V12FL0-Pulled-Pins.step` still shows pin 9; regenerate
      it with pins 4, 6, 9, 10, 11 removed (cosmetic, affects 3D view only).
- [x] J3 MPN vs footprint: top-entry BM04B-SRSS-TB(LF)(SN).
- [ ] Reflow oven details and measured profile (section 2).
