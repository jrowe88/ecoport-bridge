"""Build the EcoBridge lid logo (B1 drop + lowercase wordmark) as flat curve geometry.

Everything is real curves, not polylines: glyphs keep the font's quadratic Beziers,
the drop outline is an exact offset of its Bezier/arc centerline, and the Wi-Fi
arcs and dot are circular arcs. Output is a DXF (splines, arcs, lines) and a flat
STEP (zero-thickness faces) to drop into an Onshape sketch and extrude.

The artwork is drawn in the same 120-unit grid as the mockup page, then scaled
to WIDTH mm, centered on the origin, reading correctly from +Z.

    python make_logo.py [--width 45] [--font path/to/SairaCondensed-Bold.ttf]

Needs: build123d, fonttools, uharfbuzz (pip install --user ...).
"""
import argparse
import math
import os
from pathlib import Path

import uharfbuzz as hb
from build123d import (Compound, Edge, ExportDXF, Face, Kind, Pos, Unit, Vector, Wire,
                       export_step)
from fontTools.pens.basePen import BasePen
from fontTools.ttLib import TTFont
from OCP.BRepBuilderAPI import BRepBuilderAPI_NurbsConvert
from OCP.ShapeFix import ShapeFix_Shape

HERE = Path(__file__).parent
DEFAULT_FONT = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft/Windows/Fonts/SairaCondensed-Bold.ttf"

# Artwork in the mockup's units. Print weights: thinnest stroke 7, every gap >= 6.
DROP_STROKE = 9
ARC_STROKE = 7
ARCS = [16, 30]                 # Wi-Fi arc radii about (60, 92), spanning -130..-50 degrees
ARC_SPAN = (-130, -50)
DOT_R = 6
WORD = "ecobridge"
WORD_X, WORD_BASELINE, WORD_SIZE, WORD_TRACK = 116, 99, 72, -1


def P(x, y):
    """Mockup coordinates are y-down; CAD is y-up."""
    return Vector(x, -y, 0)


def drop():
    # Centerline: Bezier from the tip down the left side, round bottom (r34 about (60,76)),
    # Bezier back up to the tip. Offset both ways for the stroke. The SVG repeats the tip
    # as a control point; here it's nudged 1.5 units along the curve so the tangent at the
    # tip isn't zero-length, which the offset can't handle. The shape doesn't visibly change.
    center = Wire([
        Edge.make_bezier(P(60, 10), P(59.03, 11.14), P(26, 50), P(26, 76)),
        Edge.make_three_point_arc(P(26, 76), P(60, 110), P(94, 76)),
        Edge.make_bezier(P(94, 76), P(94, 50), P(60.97, 11.14), P(60, 10)),
    ])
    outer = center.offset_2d(DROP_STROKE / 2, kind=Kind.ARC)
    inner = center.offset_2d(-DROP_STROKE / 2, kind=Kind.INTERSECTION)
    # Offsets come back as OCC offset curves, which the STEP and DXF writers drop.
    # Convert to plain B-splines (still exact curves, not polylines).
    nurbs = BRepBuilderAPI_NurbsConvert(Face(outer, [inner]).wrapped, True).Shape()
    fix = ShapeFix_Shape(nurbs)
    fix.Perform()
    return Face(fix.Shape())


def arc_slot(r, a0, a1, half):
    """Circular arc stroke with round caps, as four circular arcs."""
    cx, cy = 60, 92

    def pt(rad, a):
        return P(cx + rad * math.cos(math.radians(a)), cy + rad * math.sin(math.radians(a)))

    mid = (a0 + a1) / 2
    cap = math.degrees(half / r)        # cap apex sits one half-width past the arc end
    edges = [
        Edge.make_three_point_arc(pt(r + half, a0), pt(r + half, mid), pt(r + half, a1)),
        Edge.make_three_point_arc(pt(r + half, a1), _cap_apex(cx, cy, r, a1, half, +1), pt(r - half, a1)),
        Edge.make_three_point_arc(pt(r - half, a1), pt(r - half, mid), pt(r - half, a0)),
        Edge.make_three_point_arc(pt(r - half, a0), _cap_apex(cx, cy, r, a0, half, -1), pt(r + half, a0)),
    ]
    return Face(Wire(edges))


def _cap_apex(cx, cy, r, a, half, direction):
    """Point on the round cap: centerline end pushed `half` along the arc's tangent."""
    rad = math.radians(a)
    ex, ey = cx + r * math.cos(rad), cy + r * math.sin(rad)
    tx, ty = -math.sin(rad) * direction, math.cos(rad) * direction
    return P(ex + tx * half, ey + ty * half)


def dot():
    return Face(Wire([Edge.make_circle(DOT_R).moved(Pos(P(60, 92)))]))


class EdgePen(BasePen):
    """Turns a glyph into build123d edges, keeping quadratic Beziers exact."""

    def __init__(self, glyphset, xf):
        super().__init__(glyphset)
        self.xf, self.contours, self.edges, self.start = xf, [], [], None

    def _moveTo(self, p):
        self.start = self.last = p

    def _lineTo(self, p):
        if p != self.last:
            self.edges.append(Edge.make_line(self.xf(self.last), self.xf(p)))
        self.last = p

    def _qCurveToOne(self, p1, p2):
        self.edges.append(Edge.make_bezier(self.xf(self.last), self.xf(p1), self.xf(p2)))
        self.last = p2

    def _curveToOne(self, p1, p2, p3):
        self.edges.append(Edge.make_bezier(self.xf(self.last), self.xf(p1), self.xf(p2), self.xf(p3)))
        self.last = p3

    def _closePath(self):
        if self.last != self.start:
            self._lineTo(self.start)
        if self.edges:
            self.contours.append(Wire(self.edges))
        self.edges = []

    _endPath = _closePath


def glyph_faces(wires):
    """Group contours into faces: the largest enclosing contour is the outline, others are holes."""
    faces = [Face(w) for w in wires]
    order = sorted(range(len(wires)), key=lambda i: -faces[i].area)
    outers, holes = [], {}
    for i in order:
        host = next((o for o in outers
                     if faces[o].is_inside(wires[i].center()) and faces[o].area > faces[i].area), None)
        if host is None:
            outers.append(i)
            holes[i] = []
        else:
            holes[host].append(i)
    return [Face(wires[o], [wires[h] for h in holes[o]]) for o in outers]


def wordmark(font_path):
    blob = hb.Blob.from_file_path(str(font_path))
    hbfont = hb.Font(hb.Face(blob))
    buf = hb.Buffer()
    buf.add_str(WORD)
    buf.guess_segment_properties()
    hb.shape(hbfont, buf, {"kern": True, "liga": True})

    tt = TTFont(str(font_path))
    k = WORD_SIZE / tt["head"].unitsPerEm
    glyphset, names = tt.getGlyphSet(), tt.getGlyphOrder()

    faces, pen_x = [], 0.0
    for info, pos in zip(buf.glyph_infos, buf.glyph_positions):
        ox, oy = pen_x + pos.x_offset, pos.y_offset

        def xf(p, ox=ox, oy=oy):
            return P(WORD_X + (p[0] + ox) * k, WORD_BASELINE - (p[1] + oy) * k)

        pen = EdgePen(glyphset, xf)
        glyphset[names[info.codepoint]].draw(pen)
        faces += glyph_faces(pen.contours)
        pen_x += pos.x_advance + WORD_TRACK / k
    return faces


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--width", type=float, default=45.0, help="overall logo width, mm")
    ap.add_argument("--font", type=Path, default=DEFAULT_FONT)
    ap.add_argument("--out", type=Path, default=HERE / "ecobridge-logo-b1")
    a = ap.parse_args()

    faces = [drop(), *(arc_slot(r, *ARC_SPAN, ARC_STROKE / 2) for r in ARCS), dot(), *wordmark(a.font)]
    art = Compound(faces)
    bb = art.bounding_box()
    s = a.width / bb.size.X
    art = art.scale(s)
    bb = art.bounding_box()
    art = art.moved(Pos(-bb.center().X, -bb.center().Y, 0))
    bb = art.bounding_box()

    export_step(art, str(a.out.with_name(a.out.name + "-flat").with_suffix(".step")))
    dxf = ExportDXF(unit=Unit.MM)
    dxf.add_shape(art)
    dxf.write(str(a.out.with_suffix(".dxf")))

    print(f"{bb.size.X:.2f} x {bb.size.Y:.2f} mm, {len(art.faces())} islands")
    print(f"arc stroke {ARC_STROKE * s:.2f} mm, narrowest gap {6 * s:.2f} mm, drop stroke {DROP_STROKE * s:.2f} mm")


if __name__ == "__main__":
    main()
