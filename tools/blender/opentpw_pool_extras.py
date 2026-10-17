"""Builds the swimming-pool extras (ladder, diving board, shower, sun lounger, pool lamp, underwater light) for each theme and exports one .glb each.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_pool_extras.py -- --out <dir> [--themes jungle,hallow,space,fantasy] [--objects ladder,board,...] [--preview <dir>]

Writes ``<dir>/<theme>-<object>.glb`` (and, with --preview, ``<dir>/<theme>-<object>.png``). Everything is generated here, geometry and textures; the
textures reuse the generators of the changing room (``opentpw_changing_room``), so the extras of a theme match its cabin. Nothing comes from the game.

Orientation (as the cabin): the object's back is +Y, the pool side. Every object sits on a deck slab on its land cell(s); the water cell lies beyond the +Y edge, so
anything that reaches over the water (the ladder, the diving board, the underwater light's glow) extends past Y = 10. One park cell is 10 units (a kid guest is about 10 tall).

	ladder, shower, lamp, light, board   1 x 1 cell   (Info.Shape ``*``), X and Y 0..10
	lounger                              2 x 1 cells  (``**``), X 0..20, Y 0..10: two seats
"""

import argparse
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin  # noqa: E402
import opentpw_export  # noqa: E402

# the extras' textures are 32 x 32 (the cabin's are 64 x 64): small objects, and the browser build embeds every file
cabin.TEXTURE = 32

THEMES = cabin.THEMES
CODES = cabin.CODES
OBJECTS = ("ladder", "board", "shower", "lounger", "lamp", "light")
# the texture parts each object uses (the others are not made, so the object folder holds only what it shows)
PARTS = {
	"ladder": ("deck", "frame"),
	"board": ("deck", "frame"),
	"shower": ("deck", "frame", "spray"),
	"lounger": ("deck", "frame", "cushion"),
	"lamp": ("deck", "frame", "glow"),
	"light": ("deck", "frame", "glow"),
}


def make_textures(theme, parts):
	"""The textures of a theme for the parts an object uses (names ex_<code>_<part>, at most 15 characters)."""
	code = CODES[theme]
	n = lambda part: f"ex_{code}_{part}"
	made = {}
	for part in parts:
		if part == "spray":
			made[part] = cabin._plain(n(part), (0.62, 0.86, 1.00), 70)
		elif theme == "jungle":
			made[part] = {
				"deck": lambda: cabin._planks(n("deck"), (0.74, 0.52, 0.27), 51, vertical=False, count=4),
				"frame": lambda: cabin._planks(n("frame"), (0.52, 0.35, 0.18), 52, count=2, groove=0.45),
				"cushion": lambda: cabin._plain(n("cushion"), (0.86, 0.36, 0.24), 53, line=(0.96, 0.82, 0.50)),
				"glow": lambda: cabin._plain(n("glow"), (1.00, 0.95, 0.70), 54),
			}[part]()
		elif theme == "hallow":
			made[part] = {
				"deck": lambda: cabin._stone(n("deck"), (0.28, 0.28, 0.31), (0.20, 0.38, 0.18), 61),
				"frame": lambda: cabin._plain(n("frame"), (0.42, 0.22, 0.58), 62, line=(0.62, 0.38, 0.80)),
				"cushion": lambda: cabin._plain(n("cushion"), (0.55, 0.20, 0.30), 63, line=(0.80, 0.55, 0.30)),
				"glow": lambda: cabin._plain(n("glow"), (0.72, 1.00, 0.55), 64),
			}[part]()
		elif theme == "space":
			made[part] = {
				"deck": lambda: cabin._metal(n("deck"), (0.52, 0.58, 0.66), 71),
				"frame": lambda: cabin._plain(n("frame"), (0.22, 0.25, 0.32), 72, line=(0.35, 0.40, 0.50)),
				"cushion": lambda: cabin._plain(n("cushion"), (0.20, 0.55, 0.72), 73, line=(0.60, 0.90, 1.00)),
				"glow": lambda: cabin._plain(n("glow"), (0.35, 0.95, 1.00), 74),
			}[part]()
		else:
			made[part] = {
				"deck": lambda: cabin._candy(n("deck"), ((1.00, 0.88, 0.93), (0.78, 0.92, 0.88)), 81),
				"frame": lambda: cabin._candy(n("frame"), ((0.78, 0.70, 0.95), (0.90, 0.85, 1.00)), 82, diagonal=False),
				"cushion": lambda: cabin._candy(n("cushion"), ((1.00, 0.55, 0.75), (1.00, 0.80, 0.50)), 83),
				"glow": lambda: cabin._plain(n("glow"), (1.00, 0.90, 1.00), 84),
			}[part]()
	return made


# --------------------------------------------------------------------------------------------------- geometry
# Coordinates: X to the right, Y towards the pool, Z up. The deck slab is the cell's floor; the object stands on it.

def slab(b, x0, x1):
	"""The paving of the land cell(s): a low slab, 0.3 high, inset by 0.2 so neighbouring cells show a joint."""
	b.box("deck", (x0 + 0.2, 0.2, 0.0), (x1 - 0.2, 9.8, 0.3), skip=("-z", "-x", "+x", "-y", "+y"))


def post(b, x, y, z0, z1, width=0.5, material="frame"):
	h = width / 2
	b.box(material, (x - h, y - h, z0), (x + h, y + h, z1), skip=("-z",))


def beam(b, material, p0, p1, width=0.4):
	"""A square bar between two points, as a chain of an X-aligned box (the bars here only run along Y or Z, so two boxes suffice)."""
	x0, y0, z0 = p0
	x1, y1, z1 = p1
	h = width / 2
	b.box(material, (min(x0, x1) - h, min(y0, y1) - h, min(z0, z1) - h), (max(x0, x1) + h, max(y0, y1) + h, max(z0, z1) + h))


def bar(b, material, p0, p1, width=0.4):
	"""A square bar between two points that may be diagonal in the YZ plane (the ladder's rails curve there): four sides and two end caps, every face turned away from the bar's middle."""
	import mathutils
	d = mathutils.Vector(p1) - mathutils.Vector(p0)
	u = mathutils.Vector((1.0, 0.0, 0.0))
	v = d.cross(u).normalized()
	h = width / 2
	ring = [(-u - v), (u - v), (u + v), (-u + v)]
	a = [mathutils.Vector(p0) + c * h for c in ring]
	c = [mathutils.Vector(p1) + k * h for k in ring]
	middle = (mathutils.Vector(p0) + mathutils.Vector(p1)) / 2
	quads = [[a[i], a[(i + 1) % 4], c[(i + 1) % 4], c[i]] for i in range(4)] + [[a[3], a[2], a[1], a[0]], [c[0], c[1], c[2], c[3]]]
	for points in quads:
		centre = sum(points, mathutils.Vector()) / 4
		normal = (points[1] - points[0]).cross(points[2] - points[1])
		if normal.dot(centre - middle) < 0:
			points = list(reversed(points))
		b.poly(material, [tuple(q) for q in points])


# The ladder's measures (units; a cell is 10, Y 0 the back of the cell, 10 its edge, the coping reaches 12.5 and is where the wall begins, the deck is Z 0, the water 0.6 below it, a kid about 10 tall).
LADDER_RAIL_Y = 12.8        # the rails and rungs hang 0.1 off the wall: 0.78 cell from the middle of the land cell, where the climbing guest is (PoolExtraSettings.LadderFootDistance)
LADDER_RAIL_X = (2.9, 7.1)
LADDER_RUNGS = (3.4, 1.7, 0.0, -1.7, -3.4)     # a rung every 1.7 (0.34 engine units), the lowest well under the water
LADDER_BOTTOM = -4.2


def add_ladder(b, theme):
	slab(b, 0.0, 10.0)
	post_y, top, radius = 11.3, 5.2, (LADDER_RAIL_Y - 11.3) / 2
	for x in LADDER_RAIL_X:
		b.box("frame", (x - 0.5, post_y - 0.5, 0.0), (x + 0.5, post_y + 0.5, 0.2), skip=("-z",))     # the foot plate on the coping
		bar(b, "frame", (x, post_y, 0.2), (x, post_y, top))                                              # the post on the coping
		previous = (x, post_y, top)
		for i in range(1, 7):                                                                            # the handrail curves over the coping ...
			angle = math.pi * (1 - i / 6)
			point = (x, post_y + radius + radius * math.cos(angle), top + radius * math.sin(angle) if i < 6 else top)
			bar(b, "frame", previous, point)
			previous = point
		bar(b, "frame", (x, LADDER_RAIL_Y, top), (x, LADDER_RAIL_Y, LADDER_BOTTOM))                      # ... and goes down along the wall into the water
		for z in (-0.8, -3.6):
			b.box("frame", (x - 0.3, 12.5, z - 0.3), (x + 0.3, LADDER_RAIL_Y - 0.2, z + 0.3))             # brackets on the wall
	for z in LADDER_RUNGS:
		beam(b, "frame", (LADDER_RAIL_X[0], LADDER_RAIL_Y, z), (LADDER_RAIL_X[1], LADDER_RAIL_Y, z), width=0.3)


def add_board(b, theme):
	slab(b, 0.0, 10.0)
	# steps up the back of the tower: four treads climbing towards the water
	for i in range(4):
		b.box("frame", (2.5, 1.0 + i * 1.3, 0.3), (7.5, 2.3 + i * 1.3, 1.3 + i * 1.3), skip=("-z",))
	# tower posts and the board, 6.6 above the deck, reaching 1.4 cells over the water
	for x in (2.5, 7.5):
		post(b, x, 6.3, 0.3, 6.0, width=0.7)
	b.box("frame", (3.2, 5.8, 6.0), (6.8, 24.0, 6.6), skip=("-z",))
	b.box("deck", (3.2, 9.5, 6.6), (6.8, 24.0, 6.75), skip=("-z",))      # the board's mat
	b.box("frame", (3.8, 11.0, 4.0), (6.2, 12.4, 6.0), skip=("-z",))       # the spring block under the board's end
	for x in (2.8, 7.2):
		beam(b, "frame", (x, 5.8, 7.4), (x, 9.0, 7.4), width=0.3)           # hand rails on the tower
		post(b, x, 5.8, 6.0, 7.4, width=0.3)
		post(b, x, 9.0, 6.0, 7.4, width=0.3)


def add_shower(b, theme):
	slab(b, 0.0, 10.0)
	# a privacy screen along the back of the cell, a pipe in front of it with a head and a spray column
	b.box("deck", (1.0, 9.0, 0.3), (9.0, 9.8, 7.0), skip=("-z",))
	post(b, 5.0, 8.4, 0.3, 11.0, width=0.6)
	b.box("frame", (3.8, 6.4, 10.4), (6.2, 8.6, 11.2), skip=("-z",))
	b.box("spray", (4.4, 6.9, 3.0), (5.6, 7.9, 10.4), skip=("-z", "+z"))
	b.box("frame", (3.0, 5.0, 0.3), (7.0, 9.0, 0.6), skip=("-z", "-x", "+x", "-y", "+y"))   # the drain plate


def add_lounger(b, theme):
	slab(b, 0.0, 20.0)
	for base in (0.0, 10.0):
		for x in (base + 1.8, base + 8.2):
			for y in (1.8, 8.2):
				post(b, x, y, 0.3, 2.0, width=0.7)
		b.box("frame", (base + 1.2, 1.2, 2.0), (base + 8.8, 8.8, 2.6), skip=("-z",))
		b.box("cushion", (base + 1.6, 1.6, 2.6), (base + 8.4, 7.2, 3.4), skip=("-z",))
		b.box("cushion", (base + 1.6, 1.6, 3.4), (base + 8.4, 2.8, 5.8), skip=("-z",))          # the raised head end (towards the deck)


def add_lamp(b, theme):
	slab(b, 0.0, 10.0)
	post(b, 5.0, 8.0, 0.3, 11.0, width=0.7)
	b.box("frame", (3.8, 6.8, 10.2), (6.2, 9.2, 10.8), skip=("-z",))
	b.box("glow", (4.0, 7.0, 10.8), (6.0, 9.0, 12.4), skip=("-z",))
	b.box("frame", (3.6, 6.6, 12.4), (6.4, 9.4, 12.9), skip=("-z",))


def add_light(b, theme):
	slab(b, 0.0, 10.0)
	b.box("frame", (2.5, 6.5, 0.3), (7.5, 9.5, 2.4), skip=("-z",))
	b.box("glow", (3.2, 9.5, 0.9), (6.8, 9.9, 1.9), skip=("-z", "+y"))       # the lamp face towards the pool
	# the glow in the water: a flat self-lit fan over the first cell of water, just above the floor
	points = []
	for i in range(9):
		angle = math.pi * i / 8
		points.append((5.0 + math.cos(angle) * 4.5, 10.0 + math.sin(angle) * 5.0, 0.15))
	b.poly("glow", [(5.0, 10.0, 0.15)] + points[::-1])


BUILDERS = {"ladder": add_ladder, "board": add_board, "shower": add_shower, "lounger": add_lounger, "lamp": add_lamp, "light": add_light}


def build(theme, name):
	cabin.clear_scene()
	images = make_textures(theme, PARTS[name])
	materials = {part: cabin.make_material(f"ex_{CODES[theme]}_{part}", image, self_lit=(part in ("glow", "spray"))) for part, image in images.items()}
	b = cabin.Builder(materials)
	BUILDERS[name](b, theme)
	obj = b.build(name)
	bpy.context.view_layer.objects.active = obj
	return obj


def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_pool_extras")
	parser.add_argument("--out", required=True)
	parser.add_argument("--themes", default=",".join(THEMES))
	parser.add_argument("--objects", default=",".join(OBJECTS))
	parser.add_argument("--preview")
	return parser.parse_args(argv)


def main():
	arguments = parse_arguments()
	themes = [t.strip() for t in arguments.themes.split(",") if t.strip()]
	names = [o.strip() for o in arguments.objects.split(",") if o.strip()]
	for theme in themes:
		if theme not in THEMES:
			raise SystemExit(f"unknown theme {theme}; use {','.join(THEMES)}")
	for name in names:
		if name not in OBJECTS:
			raise SystemExit(f"unknown object {name}; use {','.join(OBJECTS)}")
	os.makedirs(arguments.out, exist_ok=True)
	for theme in themes:
		for name in names:
			obj = build(theme, name)
			print(f"{theme}/{name}: {len(obj.data.polygons)} polygons")
			if not opentpw_export.export(os.path.join(arguments.out, f"{theme}-{name}.glb")):
				sys.exit(1)
			if arguments.preview:
				os.makedirs(arguments.preview, exist_ok=True)
				cabin.render_preview(os.path.join(arguments.preview, f"{theme}-{name}.png"))


if __name__ == "__main__":
	main()
