# Board library

Project-local KiCad library shared by every board revision under
`hardware/board/`. Anything not in KiCad's stock libraries lives here, so a
fresh clone opens without the 3rd-party (SnapEDA / EasyEDA / SamacSys)
libraries installed.

```
library/
  Custom.kicad_sym     symbols
  Custom.pretty/       footprints
  Custom.3dshapes/     STEP/WRL models for the footprints above
```

## Paths

Everything is referenced relative to the open project, so it works from any
sibling board folder (`ecoport-bridge-v1/`, a future `-v2/`, ...):

- Library tables (`sym-lib-table`, `fp-lib-table` in each board folder):
  `${KIPRJMOD}/../library/Custom.kicad_sym` and `.../Custom.pretty`
- 3D models: `${KIPRJMOD}/../library/Custom.3dshapes/<file>`

Never use absolute paths or `${KICAD_3RD_PARTY}` in board files or footprints.

## Adding a part

1. Import it however you like (SnapEDA, EasyEDA, SamacSys, ...).
2. Copy the symbol into `Custom.kicad_sym`, the footprint into
   `Custom.pretty/`, and the model into `Custom.3dshapes/`.
3. Point the footprint's model at
   `${KIPRJMOD}/../library/Custom.3dshapes/<file>` and the symbol's Footprint
   field at `Custom:<footprint>`.

## New board revision

Copy `sym-lib-table` and `fp-lib-table` from an existing board folder into the
new one. They only contain relative paths.

## Board marking

Silkscreen-only footprints (no pads, excluded from BOM and position files):

- `JRowe_Logo_Trace`: maker's mark, "JR" drawn as traces ending in vias, ~8 mm.
- `EcoPort_Bridge_Marking`: the logo plus product, revision, license and
  source-URL text and a blank S/N box, ~37 × 12 mm. The source URL is the
  CERN-OHL-S "Source Location" notice. Update the `REV` line in the footprint
  for a new revision.

Both are drawn on `F.SilkS`; placing one on the back side flips it to
`B.SilkS` automatically.
