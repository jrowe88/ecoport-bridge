# Rev A outputs

Released files for board revision A (`hardware/board/ecoport-bridge-v1`).
Everything here is generated from the KiCad project; regenerate it when the
board changes rather than editing it by hand.

| Path | What |
|------|------|
| `assembly.md` | Build, bring-up and isolation-test checklist |
| `bom.csv` | Bill of materials (MPN, DigiKey PN, notes) |
| `digikey-6-boards.csv` | DigiKey upload file for 6 boards; trim quantities for parts on hand |
| `ibom.html` | Interactive HTML BOM for hand placement |
| `3d/` | Board STEP model (for the enclosure in `hardware/enclosure/`) |
| `images/` | Renders, photos and other visual assets |
| `gerbers/` | Fab outputs (gerbers + drill, also zipped). OSH Park was ordered from the `.kicad_pcb` directly; these are the record/fallback |
| `pcb/`, `schematic/` | PDF plots of the board and schematic |

The STEP is exported with
`kicad-cli pcb export step --no-dnp --subst-models --drill-origin`. KiCad has
no model for the Keystone 5000 test points, so they're missing from it.
