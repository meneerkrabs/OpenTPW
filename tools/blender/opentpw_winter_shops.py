"""Builds the two winter shops of the winter extension (docs/WINTER.md) for each theme and exports one .glb per shop: the winter kiosk (hats, earmuffs and mittens) and the gluhwein stall
(a warm spiced drink). Both are 2 x 2 cell market huts with a pitched roof, a Christmas-market look.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_winter_shops.py -- --out <dir> [--themes jungle,hallow,space,fantasy] [--shops kiosk,gluehwein] [--preview <dir>]

Writes ``<dir>/<theme>-<shop>.glb`` (and, with --preview, ``<dir>/<theme>-<shop>.png``, a workbench render for looking at the result; not part of the content). Everything is generated here, geometry
and textures, so the shops need no game data and contain no original art. It reuses the texture generators, the materials and the geometry builder of the pools' changing room
(``opentpw_changing_room.py``), so the huts match the pool buildings and the brolly shop of their theme.

Footprint: 2 x 2 cells = the Blender X/Y range 0..20 with the Info.Shape (first row = top = +Y)

	**
	2*

so the customer's cell (the entrance, where a guest stands, 0.5 / 0.5 of it) is the lower left one. The hut is a booth with its counter across the front: a back wall, two side walls whose tops
follow the roof, a counter, two posts at the front corners and a pitched roof (a gable along Y with the ridge over X = 10, so the front is an open gable and the counter shows under it). The walls, the counter and the posts stand at least 1.6 units inside the
footprint and the roof reaches 1.0 from the edge, so a walker's sprite is never cut (the pools lesson, docs/POOLS.md "Model inset"; ``[EXT:pools-model-inset]``). The roof slopes face up (snow cover
lands on them with no extra work, W1) and every roof face is a closed prism, so backface culling never shows the inside of the roof. Textures are 64 x 64, tile in both directions and have at most
15-character names (``wk_`` for the kiosk, ``wg_`` for the stall).

What differs per shop: the kiosk has a hat rack on the back wall (bobble hats on pegs in three colours), a garland of mittens under the front of the roof and a stack of folded scarves on the counter;
the stall has a copper pot on a stove, a row of mugs on the counter, a lit sign board and a small chimney on the roof. What differs per theme: only the textures and a few trim parts
(jungle: bamboo and thatch; hallow: mossy dark timber, purple shingles and a pumpkin on the roof; space: riveted metal and cyan light strips; fantasy: pastel candy colours).
"""

import argparse
import math
import os
import sys

import bpy
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin  # noqa: E402
import opentpw_export  # noqa: E402

THEMES = cabin.THEMES
CODES = cabin.CODES
SHOPS = ("kiosk", "gluehwein")
PREFIX = {"kiosk": "wk", "gluehwein": "wg"}
INSET = 1.6                 # walls, counter and posts stand this far inside the footprint
ROOF_EDGE = 1.0             # the roof reaches this far from the footprint edge
EAVE = 7.0                  # the roof's top surface at the eaves
PEAK = 11.8                 # and at the ridge
ROOF_THICKNESS = 0.55
RIDGE_X = 10.0
COUNTER_HEIGHT = 3.0
LO, HI = INSET, 20.0 - INSET


def make_textures(theme, shop):
	"""The textures of a shop in a theme: wall, counter, trim, the roof and its accent, plus the shop's own props."""
	code = CODES[theme]
	n = lambda part: f"{PREFIX[shop]}_{code}_{part}"
	if theme == "jungle":
		parts = {
			"wall": cabin._planks(n("wall"), (0.76, 0.55, 0.30), 201, count=4),
			"counter": cabin._planks(n("counter"), (0.62, 0.42, 0.20), 202, vertical=False, count=4),
			"trim": cabin._planks(n("trim"), (0.50, 0.33, 0.17), 203, count=2, groove=0.45),
			"roof": cabin._thatch(n("roof"), (0.84, 0.74, 0.40), 204),
			"roof2": cabin._plain(n("roof2"), (0.78, 0.18, 0.16), 205, line=(0.95, 0.90, 0.86)),
		}
	elif theme == "hallow":
		parts = {
			"wall": cabin._planks(n("wall"), (0.30, 0.22, 0.17), 211, count=4, groove=0.5),
			"counter": cabin._stone(n("counter"), (0.30, 0.28, 0.34), (0.20, 0.38, 0.18), 212),
			"trim": cabin._plain(n("trim"), (0.42, 0.22, 0.58), 213, line=(0.62, 0.38, 0.80)),
			"roof": cabin._shingles(n("roof"), (0.32, 0.18, 0.40), (0.18, 0.10, 0.24), 214),
			"roof2": cabin._plain(n("roof2"), (0.90, 0.48, 0.10), 215, line=(0.20, 0.12, 0.10)),
		}
	elif theme == "space":
		parts = {
			"wall": cabin._metal(n("wall"), (0.68, 0.74, 0.82), 221),
			"counter": cabin._metal(n("counter"), (0.46, 0.52, 0.62), 222),
			"trim": cabin._plain(n("trim"), (0.22, 0.25, 0.32), 223, line=(0.35, 0.40, 0.50)),
			"roof": cabin._metal(n("roof"), (0.36, 0.42, 0.52), 224, dark_edge=0.5),
			"roof2": cabin._plain(n("roof2"), (0.35, 0.90, 0.96), 225),
		}
	else:
		parts = {
			"wall": cabin._candy(n("wall"), ((1.00, 0.72, 0.84), (1.00, 0.88, 0.93)), 231),
			"counter": cabin._candy(n("counter"), ((0.78, 0.92, 0.88), (0.90, 1.00, 0.96)), 232),
			"trim": cabin._candy(n("trim"), ((0.78, 0.70, 0.95), (0.90, 0.85, 1.00)), 233, diagonal=False),
			"roof": cabin._shingles(n("roof"), (0.88, 0.40, 0.46), (0.70, 0.28, 0.36), 234, scallop=True),
			"roof2": cabin._candy(n("roof2"), ((1.00, 0.97, 0.97), (0.95, 0.80, 0.84)), 235),
		}
	if shop == "kiosk":
		parts["hat1"] = cabin._plain(n("hat1"), (0.84, 0.16, 0.18), 241)
		parts["hat2"] = cabin._plain(n("hat2"), (0.20, 0.42, 0.82), 242)
		parts["hat3"] = cabin._plain(n("hat3"), (0.22, 0.62, 0.30), 243)
		parts["wool"] = cabin._plain(n("wool"), (0.97, 0.96, 0.92), 244)
	else:
		parts["copper"] = cabin._metal(n("copper"), (0.80, 0.46, 0.24), 251, dark_edge=0.65)
		parts["mug"] = cabin._plain(n("mug"), (0.94, 0.90, 0.80), 252, line=(0.72, 0.18, 0.16))
		parts["light"] = cabin._plain(n("light"), (1.00, 0.82, 0.42), 253)
	return parts


# ---- geometry helpers -----------------------------------------------------------------------------------------------

def facing(b, material, points, outward):
	"""A polygon wound so that its normal points the way ``outward`` says (the builder wants counter-clockwise seen from outside)."""
	pts = [mathutils.Vector(p) for p in points]
	normal = mathutils.Vector((0, 0, 0))
	for i in range(len(pts)):
		a, c = pts[i], pts[(i + 1) % len(pts)]
		normal.x += (a.y - c.y) * (a.z + c.z)
		normal.y += (a.z - c.z) * (a.x + c.x)
		normal.z += (a.x - c.x) * (a.y + c.y)
	if normal.dot(mathutils.Vector(outward)) < 0:
		pts.reverse()
	b.poly(material, [tuple(p) for p in pts])


def extrude_y(b, material, profile, y0, y1, end_material=None):
	"""A prism along Y: ``profile`` is a convex polygon of (x, z) points, extruded from y0 to y1."""
	end_material = end_material or material
	cx = sum(p[0] for p in profile) / len(profile)
	cz = sum(p[1] for p in profile) / len(profile)
	for i, (x0, z0) in enumerate(profile):
		x1, z1 = profile[(i + 1) % len(profile)]
		out = ((x0 + x1) / 2 - cx, 0.0, (z0 + z1) / 2 - cz)
		facing(b, material, [(x0, y0, z0), (x1, y0, z1), (x1, y1, z1), (x0, y1, z0)], out)
	facing(b, end_material, [(x, y0, z) for x, z in profile], (0, -1, 0))
	facing(b, end_material, [(x, y1, z) for x, z in profile], (0, 1, 0))


def underside(x):
	"""The z of the roof's underside at an X (the roof is a symmetric gable over X = 10 with its eaves at ROOF_EDGE and 20 - ROOF_EDGE)."""
	span = RIDGE_X - ROOF_EDGE
	d = min(abs(x - RIDGE_X), span)
	return EAVE - ROOF_THICKNESS + (PEAK - EAVE) * (1 - d / span)


def add_roof(b):
	"""The roof: a closed prism (top slopes, undersides, fascias and gable ends) with the ridge along Y over X = 10, so the front is an open gable and the counter shows under it;
	a ridge cap in the accent colour and a bobble on each end of it."""
	x0, x1, xm = ROOF_EDGE, 20.0 - ROOF_EDGE, RIDGE_X
	y0, y1 = ROOF_EDGE, 20.0 - ROOF_EDGE
	t = ROOF_THICKNESS
	facing(b, "roof", [(x0, y0, EAVE), (x0, y1, EAVE), (xm, y1, PEAK), (xm, y0, PEAK)], (-1, 0, 1))                  # the top slopes
	facing(b, "roof", [(x1, y0, EAVE), (x1, y1, EAVE), (xm, y1, PEAK), (xm, y0, PEAK)], (1, 0, 1))
	facing(b, "trim", [(x0, y0, EAVE - t), (x0, y1, EAVE - t), (xm, y1, PEAK - t), (xm, y0, PEAK - t)], (1, 0, -1))   # the undersides
	facing(b, "trim", [(x1, y0, EAVE - t), (x1, y1, EAVE - t), (xm, y1, PEAK - t), (xm, y0, PEAK - t)], (-1, 0, -1))
	facing(b, "roof2", [(x0, y0, EAVE - t), (x0, y1, EAVE - t), (x0, y1, EAVE), (x0, y0, EAVE)], (-1, 0, 0))        # the eave fascias, in the accent colour
	facing(b, "roof2", [(x1, y0, EAVE - t), (x1, y1, EAVE - t), (x1, y1, EAVE), (x1, y0, EAVE)], (1, 0, 0))
	for y in (y0, y1):                                                                                               # the gable ends: two thin chevrons each
		out = (0, -1 if y == y0 else 1, 0)
		facing(b, "roof2", [(x0, y, EAVE - t), (x0, y, EAVE), (xm, y, PEAK), (xm, y, PEAK - t)], out)
		facing(b, "roof2", [(x1, y, EAVE - t), (x1, y, EAVE), (xm, y, PEAK), (xm, y, PEAK - t)], out)
	b.box("roof2", (xm - 0.5, y0, PEAK - 0.05), (xm + 0.5, y1, PEAK + 0.45), skip=("-z",))                           # the ridge cap
	for y in (y0 + 0.2, y1 - 0.2):
		b.box("roof2", (xm - 0.35, y - 0.35, PEAK - 0.1), (xm + 0.35, y + 0.35, PEAK + 0.9), skip=("-z",))


def add_walls(b):
	"""Floor, the back wall (its top follows the roof into the gable), the two side walls (their tops follow the roof underside), the counter and the front posts."""
	b.box("trim", (0.0, 0.0, 0.0), (20.0, 20.0, 0.15), skip=("-z", "-x", "+x", "-y", "+y"))                      # the apron over the whole footprint
	b.box("trim", (LO, LO, 0.0), (HI, HI, 0.4), skip=("-z", "-x", "+x", "-y", "+y"))                              # the floor
	gap = 0.05
	back = [(LO, 0.0), (HI, 0.0), (HI, underside(HI) - gap), (RIDGE_X, underside(RIDGE_X) - gap), (LO, underside(LO) - gap)]
	extrude_y(b, "wall", back, HI - 1.0, HI)                                                                      # the back wall
	front = 7.5
	for x0, x1 in ((LO, LO + 1.0), (HI - 1.0, HI)):                                                               # the side walls, from the counter back
		extrude_y(b, "wall", [(x0, 0.0), (x1, 0.0), (x1, underside(x1) - gap), (x0, underside(x0) - gap)], front, HI - 1.0)
	b.box("counter", (LO + 1.0, 6.0, 0.0), (HI - 1.0, 7.5, COUNTER_HEIGHT), skip=("-z", "+z"))                    # the counter, across the front of the booth
	b.box("trim", (LO + 0.6, 5.6, COUNTER_HEIGHT), (HI - 0.6, 7.9, COUNTER_HEIGHT + 0.3), skip=("-z",))           # its top
	for x in (LO + 0.5, HI - 0.5):                                                                                # two posts carry the roof's front
		b.box("trim", (x - 0.5, INSET, 0.0), (x + 0.5, INSET + 1.0, underside(x) + 0.1), skip=("-z",))
	b.box("roof2", (LO + 1.2, 5.8, 0.4), (HI - 1.2, 6.0, COUNTER_HEIGHT - 0.2), skip=("-z", "+z", "-x", "+x", "+y"))   # an apron cloth on the counter's front


def pyramid(b, material, centre, radius, height, sides=6):
	"""A small cone of ``sides`` faces standing on z = centre.z (a hat, a pumpkin top, a spice heap)."""
	cx, cy, cz = centre
	ring = [(cx + radius * math.cos(2 * math.pi * k / sides), cy + radius * math.sin(2 * math.pi * k / sides), cz) for k in range(sides)]
	for k in range(sides):
		b.poly(material, [ring[k], ring[(k + 1) % sides], (cx, cy, cz + height)])
	b.poly(material, list(reversed(ring)))


def cylinder(b, material, centre, radius, height, sides=8, top=True):
	"""A closed prism with a regular polygon base, standing on z = centre.z (a pot, a mug)."""
	cx, cy, cz = centre
	lo = [(cx + radius * math.cos(2 * math.pi * k / sides), cy + radius * math.sin(2 * math.pi * k / sides), cz) for k in range(sides)]
	hi = [(x, y, z + height) for x, y, z in lo]
	for k in range(sides):
		n = (k + 1) % sides
		b.poly(material, [lo[k], lo[n], hi[n], hi[k]])
	b.poly(material, list(reversed(lo)))
	if top:
		b.poly(material, hi)


def add_kiosk_props(b):
	"""The hat rack, the mitten garland and the scarves."""
	# a board on the back wall with pegs, a bobble hat hanging from each peg
	board_y = HI - 1.0
	b.box("trim", (LO + 1.4, board_y - 0.4, 3.2), (HI - 1.4, board_y, 5.0), skip=("-z", "+y"))
	for i, x in enumerate((6.0, 8.7, 11.3, 14.0)):
		colour = ("hat1", "hat2", "hat3", "hat1")[i]
		b.box("trim", (x - 0.15, board_y - 0.9, 4.5), (x + 0.15, board_y - 0.4, 4.8), skip=("-z",))
		pyramid(b, colour, (x, board_y - 0.8, 3.7), 0.7, 1.4)
		b.box("wool", (x - 0.8, board_y - 1.1, 3.5), (x + 0.8, board_y - 0.5, 3.8), skip=("-z",))                  # the cuff
		b.box("wool", (x - 0.25, board_y - 0.95, 5.0), (x + 0.25, board_y - 0.65, 5.2), skip=("-z",))              # the bobble on the peg side
	# the garland: a string under the front of the roof with mittens hanging from it
	string_y = 3.0
	b.box("trim", (LO + 0.5, string_y - 0.12, 5.9), (HI - 0.5, string_y + 0.12, 6.1), skip=("-z",))
	for i, x in enumerate((4.6, 6.9, 9.2, 11.5, 13.8)):
		colour = ("hat1", "hat2", "hat3", "hat1", "hat2")[i]
		b.box(colour, (x - 0.45, string_y - 0.3, 4.7), (x + 0.45, string_y + 0.3, 5.7), skip=("-z",))              # the mitten
		b.box(colour, (x - 0.85, string_y - 0.3, 5.0), (x - 0.45, string_y + 0.3, 5.4), skip=("-z",))              # its thumb
		b.box("wool", (x - 0.5, string_y - 0.34, 5.5), (x + 0.5, string_y + 0.34, 5.8), skip=("-z",))              # its cuff
	# folded scarves on the counter, three small stacks
	top = COUNTER_HEIGHT + 0.3
	for x, colour in ((6.0, "hat1"), (10.0, "hat2"), (14.0, "hat3")):
		b.box(colour, (x - 1.2, 6.4, top), (x + 1.2, 7.5, top + 0.6), skip=("-z",))
		b.box("wool", (x - 1.2, 6.4, top + 0.6), (x + 1.2, 7.5, top + 0.8), skip=("-z",))
		b.box(colour, (x - 1.0, 6.5, top + 0.8), (x + 1.0, 7.4, top + 1.4), skip=("-z",))


def add_stall_props(b):
	"""The copper pot on its stove, the row of mugs, the lit sign and the chimney."""
	top = COUNTER_HEIGHT + 0.3
	b.box("trim", (12.2, 8.6, 0.0), (16.4, 12.4, 3.2), skip=("-z",))                                               # the stove behind the counter
	cylinder(b, "copper", (14.3, 10.5, 3.2), 1.7, 2.2, sides=8)                                                  # the pot
	cylinder(b, "trim", (14.3, 10.5, 5.4), 1.3, 0.35, sides=8)                                                   # its lid
	b.box("copper", (14.0, 10.2, 5.75), (14.6, 10.8, 6.0), skip=("-z",))                                          # its knob
	b.box("copper", (11.9, 10.2, 3.9), (12.5, 10.8, 4.3), skip=("-z",))                                           # a ladle, hooked on the pot's side
	for i, x in enumerate((4.2, 5.8, 7.4, 9.0, 10.6)):                                                           # mugs on the counter top
		cylinder(b, "mug", (x, 6.7, top), 0.5, 0.9, sides=6)
	for x in (4.6, 6.2, 7.8):                                                                                    # a second row on a low shelf behind
		cylinder(b, "mug", (x, 8.8, 3.4), 0.45, 0.8, sides=6)
	b.box("counter", (LO + 1.2, 8.3, 2.6), (10.0, 9.3, 3.0), skip=("-z",))                                       # the shelf
	b.box("light", (6.5, 2.7, 5.0), (13.5, 2.9, 6.3), skip=("-z",))                                               # a lit sign board hung from the roof's front
	b.box("trim", (6.3, 2.6, 4.9), (13.7, 3.0, 5.0), skip=("-z",))
	b.box("trim", (6.3, 2.6, 6.3), (13.7, 3.0, 6.4), skip=("-z",))
	# a chimney on the right slope of the roof (snow lands on its top, W1)
	cx, cy = 14.5, 13.0
	base = EAVE + (PEAK - EAVE) * (20.0 - ROOF_EDGE - cx) / (20.0 - ROOF_EDGE - RIDGE_X) - 0.4
	b.box("trim", (cx - 1.0, cy - 1.0, base), (cx + 1.0, cy + 1.0, PEAK + 2.0), skip=("-z",))


def add_theme_parts(b, theme, shop):
	"""Trim that differs per theme: bamboo sign (jungle), pumpkin on the roof (hallow), cyan light strips (space), candy-cane posts (fantasy)."""
	if theme == "jungle":
		b.box("trim", (6.0, 5.6, 0.8), (14.0, 6.0, 2.4), skip=("-z", "+y"))
	elif theme == "hallow":
		cylinder(b, "roof2", (RIDGE_X, 6.0, PEAK + 0.5), 1.0, 1.1, sides=8)                                     # a pumpkin on the ridge
		b.box("trim", (RIDGE_X - 0.15, 5.85, PEAK + 1.6), (RIDGE_X + 0.15, 6.15, PEAK + 2.1), skip=("-z",))
	elif theme == "space":
		b.box("roof2", (3.0, 5.9, 0.9), (17.0, 6.0, 1.4), skip=("-z", "+y"))
		b.box("roof2", (LO + 1.0, HI - 1.0, 5.0), (HI - 1.0, HI - 0.9, 5.5), skip=("-z", "+y"))
	else:
		for x in (LO + 0.5, HI - 0.5):                                                                           # a striped cap on each post
			b.box("roof2", (x - 0.6, INSET - 0.1, 5.0), (x + 0.6, INSET + 1.1, 5.6), skip=("-z",))


def build(theme, shop):
	cabin.clear_scene()
	images = make_textures(theme, shop)
	materials = {part: cabin.make_material(f"{PREFIX[shop]}_{CODES[theme]}_{part}", image, self_lit=(part == "light")) for part, image in images.items()}
	b = cabin.Builder(materials)
	add_walls(b)
	add_roof(b)
	if shop == "kiosk":
		add_kiosk_props(b)
	else:
		add_stall_props(b)
	add_theme_parts(b, theme, shop)
	obj = b.build(shop)
	bpy.context.view_layer.objects.active = obj
	return obj


def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_winter_shops")
	parser.add_argument("--out", required=True)
	parser.add_argument("--themes", default=",".join(THEMES))
	parser.add_argument("--shops", default=",".join(SHOPS))
	parser.add_argument("--preview")
	return parser.parse_args(argv)


def main():
	arguments = parse_arguments()
	themes = [t.strip() for t in arguments.themes.split(",") if t.strip()]
	shops = [s.strip() for s in arguments.shops.split(",") if s.strip()]
	for theme in themes:
		if theme not in THEMES:
			raise SystemExit(f"unknown theme {theme}; use {','.join(THEMES)}")
	for shop in shops:
		if shop not in SHOPS:
			raise SystemExit(f"unknown shop {shop}; use {','.join(SHOPS)}")
	os.makedirs(arguments.out, exist_ok=True)
	for theme in themes:
		for shop in shops:
			obj = build(theme, shop)
			print(f"{theme} {shop}: {len(obj.data.polygons)} polygons")
			if not opentpw_export.export(os.path.join(arguments.out, f"{theme}-{shop}.glb")):
				sys.exit(1)
			if arguments.preview:
				os.makedirs(arguments.preview, exist_ok=True)
				cabin.render_preview(os.path.join(arguments.preview, f"{theme}-{shop}.png"))


if __name__ == "__main__":
	main()
