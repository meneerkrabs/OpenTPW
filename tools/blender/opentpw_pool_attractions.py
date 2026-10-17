"""Builds the swimming-pool water attractions (rope swing, water jets, wave machine, spa) for each theme and exports one .glb per kind and theme.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_pool_attractions.py -- --out <dir> [--kinds ropeswing,jets,wavemachine,spa]
		[--themes jungle,hallow,space,fantasy] [--preview <dir>]

Writes ``<dir>/<kind>_<theme>.glb`` (and, with --preview, ``<dir>/<kind>_<theme>.png``, an 800 x 600 workbench render from the water side, for looking at the
result; it is not part of the content). Everything is generated here, geometry and textures, so the objects need no game data and contain no original art.

Footprints (Blender X/Y range 0..10 per cell, seen from above exactly as the Info.Shape text reads, first row = top = +Y):

	ropeswing    *    one cell,   two posts and an arm that reaches over the water, a rope with a handle hangs from it
	jets         *    one cell,   a pedestal with an angled nozzle
	wavemachine  **   two cells,  a housing with a grille and a paddle
	spa          *    one cell,   an octagonal tub with eight seats and a water disc

The water side of ropeswing, jets and wavemachine is Blender +Y (the shape's north, like the changing room's pool-side back); the spa has no water side.
The arm of the rope swing and the paddle of the wave machine reach up to 8 units beyond the +Y edge of the footprint. Every object stands on a one-unit
coping-like slab; a kid guest is about 10 units tall.

One geometry serves all four themes of a kind; only the textures and a few trim parts differ (as for the cabin): jungle = bamboo, planks and thatch colours,
hallow = dark mossy stone with purple trim, space = riveted metal with cyan lights, fantasy = pastel candy. Textures are 64 x 64, tile in both directions and have
at most 15-character names (``<kind prefix>_<theme code>_<part>``, for example ``rs_jun_wood``).
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin  # noqa: E402  (its texture helpers and Builder)
import opentpw_export  # noqa: E402

KINDS = ("ropeswing", "jets", "wavemachine", "spa")
PREFIX = {"ropeswing": "rs", "jets": "jt", "wavemachine": "wm", "spa": "sp"}
THEMES = cabin.THEMES
CODES = cabin.CODES
SLAB = 1.0                  # the coping-like base slab


# --------------------------------------------------------------------------------------------------- textures

def _rope(name, base, seed):
	"""A twisted rope: diagonal strands, tiling because 64 is a multiple of the period."""
	rng = cabin._rng(seed)
	x, y = cabin._grid()
	strand = 0.78 + 0.32 * (((x + y) // 4) % 2)
	colour = np.zeros((cabin.TEXTURE, cabin.TEXTURE, 3)) + np.array(base)
	colour = colour * strand[..., None] * cabin._noise(rng, 0.04)
	return cabin._finish(name, colour)


def _water(name, base, seed):
	"""Flat water with lighter ripple lines (it is drawn self-lit, so the colour is what you see)."""
	rng = cabin._rng(seed)
	x, y = cabin._grid()
	ripple = 0.5 + 0.5 * np.sin((x * 0.5 + np.sin(y * 0.35) * 2.5) * (2 * math.pi / 8) * 2.0)
	colour = np.zeros((cabin.TEXTURE, cabin.TEXTURE, 3)) + np.array(base)
	colour = colour * (0.88 + 0.22 * ripple[..., None]) * cabin._noise(rng, 0.02)
	return cabin._finish(name, colour)


def make_textures(kind, theme, parts):
	"""The textures of ``parts`` for a kind and theme, named ``<kind prefix>_<theme code>_<part>``."""
	code = CODES[theme]
	n = lambda part: f"{PREFIX[kind]}_{code}_{part}"
	seed = 100 * (KINDS.index(kind) + 1) + 10 * THEMES.index(theme)
	if theme == "jungle":
		table = {
			"wood": lambda: cabin._planks(n("wood"), (0.74, 0.52, 0.27), seed + 1, count=4),
			"trim": lambda: cabin._planks(n("trim"), (0.52, 0.35, 0.18), seed + 2, count=2, groove=0.45),
			"rope": lambda: _rope(n("rope"), (0.80, 0.68, 0.38), seed + 3),
			"light": lambda: cabin._plain(n("light"), (0.95, 0.85, 0.35), seed + 4),
			"water": lambda: _water(n("water"), (0.20, 0.62, 0.62), seed + 5),
			"vent": lambda: cabin._planks(n("vent"), (0.25, 0.16, 0.08), seed + 6, vertical=False, count=4, groove=0.3),
		}
	elif theme == "hallow":
		table = {
			"wood": lambda: cabin._stone(n("wood"), (0.24, 0.24, 0.27), (0.20, 0.38, 0.18), seed + 1),
			"trim": lambda: cabin._plain(n("trim"), (0.42, 0.22, 0.58), seed + 2, line=(0.62, 0.38, 0.80)),
			"rope": lambda: _rope(n("rope"), (0.45, 0.32, 0.55), seed + 3),
			"light": lambda: cabin._plain(n("light"), (0.78, 0.40, 1.00), seed + 4),
			"water": lambda: _water(n("water"), (0.36, 0.30, 0.62), seed + 5),
			"vent": lambda: cabin._plain(n("vent"), (0.08, 0.06, 0.12), seed + 6, line=(0.30, 0.18, 0.42)),
		}
	elif theme == "space":
		table = {
			"wood": lambda: cabin._metal(n("wood"), (0.64, 0.70, 0.78), seed + 1),
			"trim": lambda: cabin._plain(n("trim"), (0.22, 0.25, 0.32), seed + 2, line=(0.35, 0.40, 0.50)),
			"rope": lambda: _rope(n("rope"), (0.30, 0.34, 0.42), seed + 3),
			"light": lambda: cabin._plain(n("light"), (0.35, 0.95, 1.00), seed + 4),
			"water": lambda: _water(n("water"), (0.15, 0.62, 0.85), seed + 5),
			"vent": lambda: cabin._plain(n("vent"), (0.07, 0.09, 0.12), seed + 6, line=(0.25, 0.30, 0.38)),
		}
	else:
		table = {
			"wood": lambda: cabin._candy(n("wood"), ((1.00, 0.72, 0.84), (1.00, 0.88, 0.93)), seed + 1),
			"trim": lambda: cabin._candy(n("trim"), ((0.78, 0.70, 0.95), (0.90, 0.85, 1.00)), seed + 2, diagonal=False),
			"rope": lambda: _rope(n("rope"), (1.00, 0.62, 0.80), seed + 3),
			"light": lambda: cabin._plain(n("light"), (1.00, 0.95, 0.55), seed + 4),
			"water": lambda: _water(n("water"), (0.55, 0.72, 1.00), seed + 5),
			"vent": lambda: cabin._plain(n("vent"), (0.55, 0.45, 0.75), seed + 6, line=(0.80, 0.72, 0.95)),
		}
	return {part: table[part]() for part in parts}


# --------------------------------------------------------------------------------------------------- geometry

class Shapes(cabin.Builder):
	"""The cabin's builder plus the solids the attractions need (convex solids, tilted parts, octagonal prisms and rings)."""

	def convex(self, material, points, faces):
		"""A convex solid: ``faces`` lists point indices; each face is turned to point away from the solid's centre."""
		centre = np.mean(np.array(points, dtype=float), axis=0)
		for face in faces:
			pts = [np.array(points[i], dtype=float) for i in face]
			normal = np.cross(pts[1] - pts[0], pts[2] - pts[0])
			if np.dot(normal, np.mean(pts, axis=0) - centre) < 0:
				pts.reverse()
			self.poly(material, [tuple(p) for p in pts])

	def bar(self, material, start, end, width_u, width_w, up=(0.0, 0.0, 1.0)):
		"""A square-ish beam from ``start`` to ``end`` (3D points) with the given cross-section, tilted freely."""
		start, end = np.array(start, dtype=float), np.array(end, dtype=float)
		axis = end - start
		axis /= np.linalg.norm(axis)
		u = np.cross(axis, np.array(up))
		if np.linalg.norm(u) < 1e-6:
			u = np.array([1.0, 0.0, 0.0])
		u /= np.linalg.norm(u)
		w = np.cross(u, axis)
		pts = []
		for centre in (start, end):
			for su, sw in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
				pts.append(tuple(centre + u * su * width_u / 2 + w * sw * width_w / 2))
		self.convex(material, pts, [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])

	def prism(self, material, cx, cy, radius, z0, z1, sides=8, phase=0.0, top=True, side_material=None):
		"""A regular prism (octagon by default) standing on z0..z1; ``phase`` is the angle of the first corner in degrees."""
		corners = [(cx + radius * math.cos(math.radians(phase + 360.0 * i / sides)), cy + radius * math.sin(math.radians(phase + 360.0 * i / sides))) for i in range(sides)]
		for i in range(sides):
			(xa, ya), (xb, yb) = corners[i], corners[(i + 1) % sides]
			self.quad(side_material or material, (xa, ya, z0), (xb, yb, z0), (xb, yb, z1), (xa, ya, z1))
		if top:
			self.poly(material, [(x, y, z1) for x, y in corners])
		self.poly(material, [(x, y, z0) for x, y in reversed(corners)])

	def ring(self, outer_material, top_material, cx, cy, outer, inner, z0, z1, sides=8, phase=0.0, inner_material=None):
		"""A prism ring (a tub wall): outer wall, inner wall, flat top; no bottom."""
		def corners(radius):
			return [(cx + radius * math.cos(math.radians(phase + 360.0 * i / sides)), cy + radius * math.sin(math.radians(phase + 360.0 * i / sides))) for i in range(sides)]
		out, inn = corners(outer), corners(inner)
		for i in range(sides):
			j = (i + 1) % sides
			(xa, ya), (xb, yb) = out[i], out[j]
			self.quad(outer_material, (xa, ya, z0), (xb, yb, z0), (xb, yb, z1), (xa, ya, z1))
			(xa, ya), (xb, yb) = inn[i], inn[j]
			self.quad(inner_material or outer_material, (xb, yb, z0), (xa, ya, z0), (xa, ya, z1), (xb, yb, z1))
			self.quad(top_material, (out[i][0], out[i][1], z1), (out[j][0], out[j][1], z1), (inn[j][0], inn[j][1], z1), (inn[i][0], inn[i][1], z1))


def base_slab(b, width):
	"""The coping-like slab all of them stand on: trim sides, wood top."""
	b.box("trim", (0.0, 0.0, 0.0), (width, 10.0, SLAB), skip=("-z",), faces_material={"+z": "wood"})


def post_cap(b, theme, x, y, z, size=1.2):
	"""A theme ornament on top of a post: a thatch tuft, a spike, a cyan light or a candy ball."""
	h = size / 2
	if theme == "jungle":
		b.box("trim", (x - h * 1.5, y - h * 1.5, z), (x + h * 1.5, y + h * 1.5, z + 0.5), skip=("-z",))
	elif theme == "hallow":
		b.box("trim", (x - h * 0.5, y - h * 0.5, z), (x + h * 0.5, y + h * 0.5, z + 1.8), skip=("-z",))
	elif theme == "space":
		b.box("light", (x - h, y - h, z), (x + h, y + h, z + 0.7), skip=("-z",))
	else:
		b.box("trim", (x - h, y - h, z), (x + h, y + h, z + size), skip=("-z",))


def add_ropeswing(b, theme):
	base_slab(b, 10.0)
	# two posts on the water-side edge, a cross beam over them and an arm over the water
	for x in (2.4, 7.6):
		b.box("wood", (x - 0.7, 7.8, SLAB), (x + 0.7, 9.2, 14.0), skip=("-z",))
		post_cap(b, theme, x, 8.5, 14.0)
		b.bar("trim", (x, 8.5, SLAB + 1.0), (x, 8.5 - 2.4, SLAB), 1.0, 1.0)         # a foot brace to the back
	b.box("wood", (1.0, 7.9, 12.4), (9.0, 9.1, 14.0), skip=())
	b.bar("wood", (5.0, 6.4, 13.0), (5.0, 18.0, 13.2), 1.4, 1.4)                   # the arm: reaches 8 units beyond the +Y edge
	b.bar("trim", (5.0, 8.6, 11.4), (5.0, 12.5, 13.0), 0.7, 0.7)                   # a brace under the arm
	# the rope and its handle hang from the arm tip
	b.box("rope", (4.75, 16.0, 4.6), (5.25, 16.5, 12.5), skip=("-z",))
	b.box("trim", (3.8, 15.8, 4.0), (6.2, 16.7, 4.7))
	b.box("light", (4.5, 15.8, 4.7), (5.5, 16.7, 5.4))                             # the knot above the handle
	post_cap(b, theme, 5.0, 17.4, 13.2 + 0.7, 1.0)


def add_jets(b, theme):
	base_slab(b, 10.0)
	b.prism("trim", 5.0, 4.5, 3.9, SLAB, SLAB + 1.0, phase=22.5)
	b.prism("wood", 5.0, 4.5, 3.2, SLAB + 1.0, 6.0, phase=22.5, side_material="wood")
	b.prism("wood", 5.0, 4.5, 3.5, 6.0, 6.7, phase=22.5)
	# the nozzle: a tapering barrel pointing up and towards the water (+Y)
	tilt = math.radians(32.0)
	direction = np.array([0.0, math.cos(tilt), math.sin(tilt)])
	side = np.array([1.0, 0.0, 0.0])
	lift = np.array([0.0, -math.sin(tilt), math.cos(tilt)])
	base = np.array([5.0, 4.5, 6.7])
	tip = base + direction * 6.0
	points = []
	for centre, half in ((base, 1.4), (tip, 0.9)):
		for su, sl in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
			points.append(tuple(centre + side * su * half + lift * sl * half))
	b.convex("trim", points, [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])
	b.bar("light", tuple(tip), tuple(tip + direction * 0.7), 1.3, 1.3)               # the glowing mouth
	# a few droplets of the jet, in the water colour
	for step, (dy, dz) in enumerate(((2.0, 1.2), (4.6, 1.9), (7.4, 1.2))):
		centre = tip + direction * 0.7 + np.array([0.0, dy, dz])
		b.box("water", (centre[0] - 0.5, centre[1] - 0.5, centre[2] - 0.5), (centre[0] + 0.5, centre[1] + 0.5, centre[2] + 0.5))
	# a valve box at the back
	b.box("trim", (3.8, 0.8, SLAB), (6.2, 2.0, 4.2), skip=("-z",))
	post_cap(b, theme, 5.0, 1.4, 4.2, 1.0)


def add_wavemachine(b, theme):
	base_slab(b, 20.0)
	b.box("wood", (1.0, 1.5, SLAB), (19.0, 8.0, 9.5), skip=("-z",))
	b.box("trim", (0.4, 1.0, 9.5), (19.6, 8.6, 10.5), skip=("-z",))                  # roof cap
	b.box("trim", (1.0, 8.0, SLAB), (19.0, 8.3, 2.0), skip=("-z", "-y"))             # a foot band along the front
	# the grille on the +Y face: dark slots between bars
	b.box("vent", (3.0, 8.0, 3.0), (17.0, 8.3, 8.4), skip=("-z", "+z", "-x", "+x", "-y"))
	for i in range(5):
		z = 3.4 + i * 1.05
		b.box("trim", (3.0, 8.3, z), (17.0, 8.55, z + 0.45), skip=("-y",))
	# the paddle: a panel on two arms, tilted into the water side
	for x in (3.5, 16.5):
		b.bar("trim", (x, 8.3, 4.5), (x, 11.0, 4.2), 1.0, 1.0)
	b.bar("light", (10.0, 11.3, 2.2), (10.0, 12.2, 6.8), 14.0, 0.9)
	post_cap(b, theme, 2.0, 5.0, 10.5)
	post_cap(b, theme, 18.0, 5.0, 10.5)
	if theme == "space":
		b.box("light", (9.0, 1.2, 10.5), (11.0, 8.0, 10.9), skip=("-z",))                    # a light strip along the roof


def add_spa(b, theme):
	base_slab(b, 10.0)
	phase = 22.5                                                                      # flat sides face the compass directions
	b.ring("wood", "trim", 5.0, 5.0, 4.7, 3.7, SLAB, 5.6, phase=phase, inner_material="trim")
	b.ring("trim", "trim", 5.0, 5.0, 3.7, 2.5, SLAB, 4.8, phase=phase)                # the bench ring
	for i in range(8):                                                                # eight seats: a divider between each pair
		angle = math.radians(phase + 45.0 * i)
		cx, cy = 5.0 + 3.1 * math.cos(angle), 5.0 + 3.1 * math.sin(angle)
		b.prism("wood", cx, cy, 0.45, 4.8, 5.4, sides=4, phase=45.0 + math.degrees(angle))
	b.poly("water", [(5.0 + 2.5 * math.cos(math.radians(phase + 45.0 * i)), 5.0 + 2.5 * math.sin(math.radians(phase + 45.0 * i)), 4.3) for i in range(8)])
	# the theme prop in the corner outside the tub: a torch, crystal, antenna or lollipop
	px, py = 1.3, 1.3
	b.box("trim", (px - 0.3, py - 0.3, SLAB), (px + 0.3, py + 0.3, 5.0), skip=("-z",))
	post_cap(b, theme, px, py, 5.0, 1.2)
	if theme in ("jungle", "hallow", "space"):
		b.box("light", (px - 0.5, py - 0.5, 5.0), (px + 0.5, py + 0.5, 5.8), skip=("-z",))


BUILDERS = {"ropeswing": add_ropeswing, "jets": add_jets, "wavemachine": add_wavemachine, "spa": add_spa}
PARTS = {
	"ropeswing": ("wood", "trim", "rope", "light"),
	"jets": ("wood", "trim", "light", "water"),
	"wavemachine": ("wood", "trim", "light", "vent"),
	"spa": ("wood", "trim", "light", "water"),
}
CENTRES = {"ropeswing": (5.0, 10.0, 7.0), "jets": (5.0, 7.0, 4.0), "wavemachine": (10.0, 8.0, 5.0), "spa": (5.0, 5.0, 3.0)}


def build(kind, theme):
	cabin.clear_scene()
	images = make_textures(kind, theme, PARTS[kind])
	materials = {part: cabin.make_material(images[part].name, images[part], self_lit=(part in ("light", "water"))) for part in images}
	b = Shapes(materials)
	BUILDERS[kind](b, theme)
	obj = b.build(kind)
	bpy.context.view_layer.objects.active = obj
	return obj


# --------------------------------------------------------------------------------------------------- preview

def render_preview(kind, path):
	import mathutils
	scene = bpy.context.scene
	scene.render.engine = "BLENDER_WORKBENCH"
	scene.render.resolution_x, scene.render.resolution_y = 800, 600
	shading = scene.display.shading
	shading.light = "STUDIO"
	shading.color_type = "TEXTURE"
	shading.show_cavity = False
	shading.show_shadows = True
	scene.view_settings.view_transform = "Standard"
	world = bpy.data.worlds.new("preview") if scene.world is None else scene.world
	scene.world = world
	world.color = (0.40, 0.65, 0.85)                                                  # the water side is +Y, so the backdrop is pool blue
	camera_data = bpy.data.cameras.new("cam")
	camera_data.type = "ORTHO"
	camera_data.ortho_scale = 46.0 if kind != "wavemachine" else 54.0
	camera = bpy.data.objects.new("cam", camera_data)
	scene.collection.objects.link(camera)
	# from the water side (+Y), the left (-X) and above
	target = np.array(CENTRES[kind])
	direction = np.array([-0.55, 0.75, 0.62])
	direction /= np.linalg.norm(direction)
	camera.location = tuple(target + direction * 80.0)
	camera.rotation_euler = mathutils.Vector(-direction).to_track_quat("-Z", "Y").to_euler()
	scene.camera = camera
	scene.render.filepath = path
	bpy.ops.render.render(write_still=True)


# --------------------------------------------------------------------------------------------------- main

def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_pool_attractions")
	parser.add_argument("--out", required=True)
	parser.add_argument("--kinds", default=",".join(KINDS))
	parser.add_argument("--themes", default=",".join(THEMES))
	parser.add_argument("--preview")
	return parser.parse_args(argv)


def main():
	arguments = parse_arguments()
	kinds = [k.strip() for k in arguments.kinds.split(",") if k.strip()]
	themes = [t.strip() for t in arguments.themes.split(",") if t.strip()]
	for kind in kinds:
		if kind not in KINDS:
			raise SystemExit(f"unknown kind {kind}; use {','.join(KINDS)}")
	for theme in themes:
		if theme not in THEMES:
			raise SystemExit(f"unknown theme {theme}; use {','.join(THEMES)}")
	os.makedirs(arguments.out, exist_ok=True)
	for kind in kinds:
		for theme in themes:
			obj = build(kind, theme)
			print(f"{kind}_{theme}: {len(obj.data.polygons)} polygons, {sum(len(p.vertices) - 2 for p in obj.data.polygons)} triangles")
			if not opentpw_export.export(os.path.join(arguments.out, f"{kind}_{theme}.glb")):
				sys.exit(1)
			if arguments.preview:
				os.makedirs(arguments.preview, exist_ok=True)
				render_preview(kind, os.path.join(arguments.preview, f"{kind}_{theme}.png"))


if __name__ == "__main__":
	main()
