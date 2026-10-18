"""Builds the brolly shop (an umbrella kiosk, 2 x 2 cells) of the umbrellas extension for each theme and exports one .glb per theme.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_brolly_shop.py -- --out <dir> [--themes jungle,hallow,space,fantasy] [--preview <dir>]

Writes ``<dir>/<theme>.glb`` (and, with --preview, ``<dir>/<theme>.png``, a workbench render for looking at the result; not part of the content). Everything is generated here, geometry and
textures, so the shop needs no game data and contains no original art (docs/UMBRELLAS.md). It reuses the generators, materials and the geometry builder of the changing room
(``opentpw_changing_room.py``), so the shops match the pool buildings of their theme.

Footprint: 2 x 2 cells = the Blender X/Y range 0..20 with the Info.Shape (first row = top = +Y)

	**
	2*

so the customer's cell (the entrance, where a guest stands, 0.5 / 0.5 of it) is the lower left one. The shop is a booth: a back wall and two side walls, a counter across the front of the
booth with three open umbrellas on it, and a roof in the shape of an open umbrella (an eight-panel dome in two alternating textures with a finial) that reaches over the counter and the customer's cell. The walls, the counter and the posts stand
at least 1.6 units inside the footprint (the pools lesson: a walker is a sprite and a wall flush with the cell edge cuts it, docs/POOLS.md "Model inset"); the roof reaches 1.0 from the edge. Textures are 64 x 64,
tile in both directions and have at most 15-character names.
"""

import argparse
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin  # noqa: E402
import opentpw_export  # noqa: E402

THEMES = cabin.THEMES
CODES = cabin.CODES
INSET = 1.6                 # walls, counter and posts stand this far inside the footprint
WALL_HEIGHT = 8.0
EAVE = 8.0                  # the roof's rim
ROOF_RADIUS = 9.0           # reaches 1.0 from the footprint edge (the centre is (10, 10))
RING_RADIUS = 5.2
RING_HEIGHT = 11.6
PEAK = 13.6
COUNTER_HEIGHT = 3.0
SIDES = 8
CENTRE = (10.0, 10.0)


def make_textures(theme):
	"""The textures of a theme: wall, counter, trim, the two roof panels and the umbrellas' three colours."""
	code = CODES[theme]
	n = lambda part: f"br_{code}_{part}"
	umbrellas = {
		"u1": cabin._plain(n("u1"), (0.18, 0.54, 0.85), 91),
		"u2": cabin._plain(n("u2"), (0.90, 0.28, 0.24), 92),
		"u3": cabin._plain(n("u3"), (0.96, 0.82, 0.22), 93),
	}
	if theme == "jungle":
		parts = {
			"wall": cabin._planks(n("wall"), (0.74, 0.52, 0.27), 101, count=4),
			"counter": cabin._planks(n("counter"), (0.62, 0.42, 0.20), 102, vertical=False, count=4),
			"trim": cabin._planks(n("trim"), (0.52, 0.35, 0.18), 103, count=2, groove=0.45),
			"roof": cabin._thatch(n("roof"), (0.80, 0.68, 0.30), 104),
			"roof2": cabin._plain(n("roof2"), (0.30, 0.62, 0.24), 105),
		}
	elif theme == "hallow":
		parts = {
			"wall": cabin._stone(n("wall"), (0.24, 0.24, 0.27), (0.20, 0.38, 0.18), 111),
			"counter": cabin._stone(n("counter"), (0.30, 0.28, 0.34), (0.20, 0.38, 0.18), 112),
			"trim": cabin._plain(n("trim"), (0.42, 0.22, 0.58), 113, line=(0.62, 0.38, 0.80)),
			"roof": cabin._shingles(n("roof"), (0.30, 0.16, 0.38), (0.16, 0.08, 0.22), 114),
			"roof2": cabin._plain(n("roof2"), (0.88, 0.46, 0.10), 115),
		}
	elif theme == "space":
		parts = {
			"wall": cabin._metal(n("wall"), (0.64, 0.70, 0.78), 121),
			"counter": cabin._metal(n("counter"), (0.46, 0.52, 0.62), 122),
			"trim": cabin._plain(n("trim"), (0.22, 0.25, 0.32), 123, line=(0.35, 0.40, 0.50)),
			"roof": cabin._metal(n("roof"), (0.34, 0.38, 0.46), 124, dark_edge=0.5),
			"roof2": cabin._plain(n("roof2"), (0.35, 0.90, 0.96), 125),
			"light": cabin._plain(n("light"), (0.35, 0.95, 1.00), 126),
		}
	else:
		parts = {
			"wall": cabin._candy(n("wall"), ((1.00, 0.72, 0.84), (1.00, 0.88, 0.93)), 131),
			"counter": cabin._candy(n("counter"), ((0.78, 0.92, 0.88), (0.90, 1.00, 0.96)), 132),
			"trim": cabin._candy(n("trim"), ((0.78, 0.70, 0.95), (0.90, 0.85, 1.00)), 133, diagonal=False),
			"roof": cabin._shingles(n("roof"), (0.70, 0.92, 0.82), (0.55, 0.82, 0.72), 134, scallop=True),
			"roof2": cabin._candy(n("roof2"), ((1.00, 0.55, 0.75), (1.00, 0.85, 0.92)), 135),
		}
	parts.update(umbrellas)
	return parts


def ring(radius, z, k):
	angle = 2 * math.pi * k / SIDES
	return (CENTRE[0] + radius * math.cos(angle), CENTRE[1] + radius * math.sin(angle), z)


def add_roof(b):
	"""The umbrella-shaped roof: eight panels in two alternating textures from the rim over a middle ring to the apex, a fascia band, a flat ceiling and a finial."""
	for k in range(SIDES):
		n = (k + 1) % SIDES
		material = "roof" if k % 2 == 0 else "roof2"
		b.quad(material, ring(ROOF_RADIUS, EAVE, k), ring(ROOF_RADIUS, EAVE, n), ring(RING_RADIUS, RING_HEIGHT, n), ring(RING_RADIUS, RING_HEIGHT, k))
		b.poly(material, [ring(RING_RADIUS, RING_HEIGHT, k), ring(RING_RADIUS, RING_HEIGHT, n), (CENTRE[0], CENTRE[1], PEAK)])
		b.quad("trim", ring(ROOF_RADIUS, EAVE - 0.7, k), ring(ROOF_RADIUS, EAVE - 0.7, n), ring(ROOF_RADIUS, EAVE, n), ring(ROOF_RADIUS, EAVE, k))
	b.poly("trim", [ring(ROOF_RADIUS, EAVE, k) for k in reversed(range(SIDES))])          # the ceiling, seen from below
	b.box("trim", (CENTRE[0] - 0.4, CENTRE[1] - 0.4, PEAK - 0.1), (CENTRE[0] + 0.4, CENTRE[1] + 0.4, PEAK + 1.6), skip=("-z",))   # the finial


def add_booth(b, theme):
	lo, hi = INSET, 20.0 - INSET
	b.box("trim", (0.0, 0.0, 0.0), (20.0, 20.0, 0.15), skip=("-z", "-x", "+x", "-y", "+y"))          # the apron over the whole footprint
	b.box("trim", (lo, lo, 0.0), (hi, hi, 0.4), skip=("-z", "-x", "+x", "-y", "+y"))                  # the floor
	b.box("wall", (lo, hi - 1.0, 0.0), (hi, hi, WALL_HEIGHT), skip=("-z", "+z"))                      # the back wall
	b.box("wall", (lo, 7.5, 0.0), (lo + 1.0, hi - 1.0, WALL_HEIGHT), skip=("-z", "+z"))               # the side walls, from the counter back
	b.box("wall", (hi - 1.0, 7.5, 0.0), (hi, hi - 1.0, WALL_HEIGHT), skip=("-z", "+z"))
	b.box("counter", (lo + 1.0, 6.0, 0.0), (hi - 1.0, 7.5, COUNTER_HEIGHT), skip=("-z", "+z"))          # the counter, across the front of the booth
	b.box("trim", (lo + 0.6, 5.6, COUNTER_HEIGHT), (hi - 0.6, 7.9, COUNTER_HEIGHT + 0.3), skip=("-z",))   # its top
	for x in (lo + 0.5, hi - 0.5):                                                                       # two posts carry the roof's front
		b.box("trim", (x - 0.5, INSET, 0.0), (x + 0.5, INSET + 1.0, EAVE - 0.7), skip=("-z", "+z"))
	# a rack of umbrellas on the counter: three open umbrellas on short poles in the shop's three colours
	top = COUNTER_HEIGHT + 0.3
	for x, colour in ((5.5, "u1"), (10.0, "u2"), (14.5, "u3")):
		b.box("trim", (x - 0.15, 6.55, top), (x + 0.15, 6.85, top + 2.6), skip=("-z",))
		for k in range(SIDES):
			n = (k + 1) % SIDES
			a0, a1 = 2 * math.pi * k / SIDES, 2 * math.pi * n / SIDES
			p0 = (x + 1.5 * math.cos(a0), 6.7 + 1.5 * math.sin(a0), top + 2.6)
			p1 = (x + 1.5 * math.cos(a1), 6.7 + 1.5 * math.sin(a1), top + 2.6)
			b.poly(colour, [p0, p1, (x, 6.7, top + 3.8)])
	if theme == "space":     # cyan light strips under the roof rim and on the counter's front
		b.box("light", (3.0, 5.9, 1.0), (17.0, 6.0, 1.5), skip=("-z", "+y"))
		b.box("light", (lo + 1.0, hi - 1.0, WALL_HEIGHT - 1.2), (hi - 1.0, hi - 0.9, WALL_HEIGHT - 0.7), skip=("-z", "+y"))
	elif theme == "jungle":  # a bamboo sign board on the counter's face
		b.box("trim", (6.0, 5.6, 1.0), (14.0, 6.0, 2.4), skip=("-z", "+y"))


def build(theme):
	cabin.clear_scene()
	images = make_textures(theme)
	materials = {part: cabin.make_material(f"br_{CODES[theme]}_{part}", image, self_lit=(part == "light")) for part, image in images.items()}
	b = cabin.Builder(materials)
	add_booth(b, theme)
	add_roof(b)
	obj = b.build("brolly")
	bpy.context.view_layer.objects.active = obj
	return obj


def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_brolly_shop")
	parser.add_argument("--out", required=True)
	parser.add_argument("--themes", default=",".join(THEMES))
	parser.add_argument("--preview")
	return parser.parse_args(argv)


def main():
	arguments = parse_arguments()
	themes = [t.strip() for t in arguments.themes.split(",") if t.strip()]
	for theme in themes:
		if theme not in THEMES:
			raise SystemExit(f"unknown theme {theme}; use {','.join(THEMES)}")
	os.makedirs(arguments.out, exist_ok=True)
	for theme in themes:
		obj = build(theme)
		print(f"{theme}: {len(obj.data.polygons)} polygons")
		if not opentpw_export.export(os.path.join(arguments.out, f"{theme}.glb")):
			sys.exit(1)
		if arguments.preview:
			os.makedirs(arguments.preview, exist_ok=True)
			cabin.render_preview(os.path.join(arguments.preview, f"{theme}.png"))


if __name__ == "__main__":
	main()
