# Enclosure

Two-part 3D-printed case (lid + bottom plate) for the rev-A bridge board
(`hardware/board/ecoport-bridge-v1`, outputs in `hardware/rev-a`).

## Source

The enclosure is modelled in Onshape. The editable source is the public
document, pinned to the version that fits the rev-A board:

**[EcoPort Bridge enclosure, Rev A (Onshape)](https://cad.onshape.com/documents/f7f574f546e65d8827d771d9/v/b11aba0fdb9c864e3161538f/e/fab6b33ee96ef1e3210294c4)**

Anyone can view it or make a copy into their own Onshape account; only the
owner can change it. The link points at a fixed version, so it won't drift as
the workspace is edited. The STEP files below are exported from that version
and are the copy to use if the Onshape link ever goes away.

## Files

| Path | What |
|------|------|
| `step/ecoport-bridge-rev-a-lid.step` | Lid |
| `step/ecoport-bridge-rev-a-bottom-plate.step` | Bottom plate |
| `logo/` | Lid logo outline and the script that generates it |
| `print-settings.md` | Slicer settings |
| `stl/` | Print files are not committed; they're attached to the GitHub release for each board revision |

To check fit, load the STEPs together with the board model in
`hardware/rev-a/3d/ecoport-bridge-v1.step`.

## Hardware

- 4 × M2.5 × 6 mm pan-head screws hold the board. Use **nylon** at the hole
  by J1 / AC_L1 (board coordinates 64.05, 35.15), which is close to mains;
  see `hardware/rev-a/bom.csv`.
- 2 × #8-32 × 2" socket-head cap screws mount the module to the water heater.
  They pass through the lid and the bottom plate, so they also hold the lid
  to the base.

## License

Like the rest of `hardware/`, the enclosure is licensed under the CERN Open
Hardware Licence v2, Strongly Reciprocal (`CERN-OHL-S-2.0`); see
[`hardware/LICENSE`](../LICENSE). You're free to copy, modify and make it.
If you distribute a modified version, or cases made from one, share its
source under the same licence.
