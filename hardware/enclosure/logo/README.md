# EcoBridge lid logo

Water drop with Wi-Fi arcs plus the lowercase `ecobridge` wordmark, set side by
side. It's recessed into the front of the lid.

| File | What |
|------|------|
| `ecobridge-logo-b1.dxf` | Flat outline, 45 × 14.6 mm, millimetres, centered on the origin |
| `ecobridge-logo-b1-flat.step` | The same outline as zero-thickness faces, in case DXF import gives trouble |
| `make_logo.py` | Generates both files |

Both files use true curves: splines for the letters and the drop, and arcs and
a circle for the Wi-Fi symbol. Straight lines appear only where the letters
are actually straight.

## Using it in Onshape

1. Start a sketch on the outer front face of the lid.
2. **DXF:** use *Insert DXF/DWG* in the sketch, with units in millimetres.
   **STEP:** import it as a part, then *Use* (project) its edges into the
   sketch.
3. Center it below the screws. Mirror it if it reads backwards from outside.
4. Extrude-remove 0.5 mm into the lid. That leaves 1.5 mm of the 2 mm wall.

Print the lid face-down. The recess forms in the first few layers and needs no
supports.

## Regenerating

```
pip install --user build123d fonttools uharfbuzz
python make_logo.py --width 45
```

It uses Saira Condensed Bold (SIL Open Font License) from the per-user Windows
fonts folder. Pass `--font` to point at another copy. The lettering is converted
to outlines, so neither file needs the font.

At 45 mm wide the thinnest stroke (the Wi-Fi arcs) is 0.94 mm and the narrowest
gap is 0.80 mm. Don't go much smaller with a 0.4 mm nozzle.
