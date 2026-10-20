"""Builds the torch of the evening extension (docs/EVENING.md, "Torches") for each theme and exports one .glb per theme.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_torches.py -- --out <dir> [--themes jungle,hallow,space,fantasy] [--preview <dir>]

Writes ``<dir>/<theme>-torch.glb`` (and, with --preview, ``<dir>/<theme>-torch.png``, a workbench render for looking at the result; not part of the content). Everything is generated
here, geometry and textures, so the torches need no game data and contain no original art. The texture generators and the geometry builder are the pools' changing room's
(``opentpw_changing_room.py``).

Footprint: one cell (``Info.Shape`` ``*``), the Blender X/Y range 0..10, the torch standing in its middle (5, 5) on a low footing. Two nodes:

- ``torch``: the footing, the pole and the head (lit like any object);
- ``flame``: the flame, a closed low-poly teardrop with a self-lit gradient (drawn in its own colours, so it shows by day and glows at night). Its node origin is the flame's base,
  so the evening extension can scale it about its base for the flicker (docs/EVENING.md); the lamp table hangs the torch's light on this node.

The four themes (the user's choice of October 19, 2026, "Themavarianten"):

- jungle: a bamboo tiki torch, a woven cup bound to the pole with rope, a warm orange flame;
- hallow: a wrought-iron rod with a carved pumpkin head (its face glows), the flame rising from the pumpkin;
- space: a metal rod with two emitter rings and a blue plasma flame;
- fantasy: a striped candy stick with a sugar cup and a pink flame.

Textures are 64 x 64, tile in both directions and have at most 15-character names (``tt_<theme code>_<part>``).
"""

import argparse
import math
import os
import sys

import bpy
import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_changing_room as cabin  # noqa: E402
import opentpw_export  # noqa: E402

THEMES = cabin.THEMES
CODES = cabin.CODES
CENTRE = 5.0                # the middle of the cell
FOOT = 0.35                 # the footing's height
TEXTURE = cabin.TEXTURE

# where the flame stands (its node origin) and how big it is, per theme
FLAME_BASE = {"jungle": 12.4, "hallow": 12.9, "space": 12.6, "fantasy": 12.3}
FLAME_SIZE = {"jungle": (0.95, 3.4), "hallow": (0.85, 3.0), "space": (0.75, 3.6), "fantasy": (0.85, 3.1)}   # (widest radius, height)


def _flame(name, bottom, middle, top):
	"""A flame gradient: the bottom of the texture is the hot core, going through the middle colour to the tip colour over the lowest 24 rows (the flame's height maps to them, see the
	builder's projection: v = z / 10), and the tip colour above."""
	rng = cabin._rng(sum(map(ord, name)))
	_, y = cabin._grid()
	height = (TEXTURE - 1 - y).astype(float)                    # rows counted from the bottom
	t = np.clip(height / 24.0, 0.0, 1.0)[..., None]
	low = np.array(bottom) * (1 - np.clip(t * 2, 0, 1)) + np.array(middle) * np.clip(t * 2, 0, 1)
	high = np.array(middle) * (1 - np.clip(t * 2 - 1, 0, 1)) + np.array(top) * np.clip(t * 2 - 1, 0, 1)
	colour = np.where(t < 0.5, low, high)
	colour = colour * cabin._noise(rng, 0.02)
	return cabin._finish(name, colour)


def _bamboo(name, base, seed):
	"""Bamboo: a green-gold stem with a darker ring (a node) every 16 rows, faint lengthwise fibres."""
	rng = cabin._rng(seed)
	x, y = cabin._grid()
	colour = np.zeros((TEXTURE, TEXTURE, 3)) + np.array(base)
	colour = colour * (0.94 + 0.08 * np.sin(x[..., None] * 0.9)) * cabin._noise(rng, 0.03)
	colour[(y % 16) == 0] *= 0.55
	colour[(y % 16) == 1] *= 1.18
	return cabin._finish(name, colour)


def make_textures(theme):
	code = CODES[theme]
	n = lambda part: f"tt_{code}_{part}"
	if theme == "jungle":
		return {
			"pole": _bamboo(n("pole"), (0.74, 0.66, 0.32), 301),
			"cup": cabin._thatch(n("cup"), (0.62, 0.44, 0.22), 302),
			"rope": cabin._planks(n("rope"), (0.80, 0.70, 0.48), 303, vertical=False, count=8, groove=0.6),
			"foot": cabin._stone(n("foot"), (0.48, 0.44, 0.38), (0.28, 0.42, 0.18), 304),
			"flame": _flame(n("flame"), (1.00, 0.95, 0.62), (1.00, 0.58, 0.12), (0.92, 0.22, 0.06)),
		}
	if theme == "hallow":
		return {
			"pole": cabin._metal(n("pole"), (0.20, 0.20, 0.24), 311, dark_edge=0.55),
			"cup": cabin._planks(n("cup"), (0.94, 0.48, 0.08), 312, vertical=True, count=4, groove=0.62),
			"rope": cabin._plain(n("rope"), (0.22, 0.42, 0.14), 313),
			"foot": cabin._stone(n("foot"), (0.30, 0.28, 0.34), (0.20, 0.38, 0.18), 314),
			"glow": cabin._plain(n("glow"), (1.00, 0.80, 0.30), 315),
			"flame": _flame(n("flame"), (1.00, 0.92, 0.55), (1.00, 0.50, 0.08), (0.80, 0.16, 0.05)),
		}
	if theme == "space":
		return {
			"pole": cabin._plain(n("pole"), (0.70, 0.75, 0.82), 321, line=(0.45, 0.50, 0.58)),
			"cup": cabin._metal(n("cup"), (0.36, 0.42, 0.52), 322, dark_edge=0.5),
			"rope": cabin._plain(n("rope"), (0.35, 0.90, 0.96), 323),
			"foot": cabin._metal(n("foot"), (0.30, 0.34, 0.42), 324, dark_edge=0.5),
			"flame": _flame(n("flame"), (0.92, 0.98, 1.00), (0.35, 0.72, 1.00), (0.20, 0.30, 0.95)),
		}
	return {
		"pole": cabin._candy(n("pole"), ((0.96, 0.36, 0.56), (1.00, 0.96, 0.97)), 331),
		"cup": cabin._candy(n("cup"), ((0.78, 0.70, 0.95), (0.90, 0.85, 1.00)), 332, diagonal=False),
		"rope": cabin._plain(n("rope"), (1.00, 0.86, 0.36), 333),
		"foot": cabin._candy(n("foot"), ((0.78, 0.92, 0.88), (0.90, 1.00, 0.96)), 334),
		"flame": _flame(n("flame"), (1.00, 0.94, 0.98), (1.00, 0.46, 0.80), (0.86, 0.20, 0.62)),
	}


# ---- geometry helpers -----------------------------------------------------------------------------------------------

def ring(cx, cy, z, radius, sides, turn=0.0):
	return [(cx + radius * math.cos(2 * math.pi * (k + turn) / sides), cy + radius * math.sin(2 * math.pi * (k + turn) / sides), z) for k in range(sides)]


def frustum(b, material, centre, r0, r1, z0, z1, sides=8, bottom=True, top=True, top_material=None):
	"""A closed prism (or frustum) with a regular polygon base from z0 (radius r0) to z1 (radius r1)."""
	cx, cy = centre
	lo, hi = ring(cx, cy, z0, r0, sides), ring(cx, cy, z1, r1, sides)
	for k in range(sides):
		m = (k + 1) % sides
		b.poly(material, [lo[k], lo[m], hi[m], hi[k]])
	if bottom:
		b.poly(material, list(reversed(lo)))
	if top:
		b.poly(top_material or material, hi)


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


def lathe(b, material, centre, profile, sides=8, turn=0.0):
	"""A closed convex solid of revolution from a profile of (z, radius) pairs, bottom to top; a radius of 0 at either end closes it in a point, otherwise it gets a flat cap.
	Every face is wound to face away from the solid's middle."""
	cx, cy = centre
	middle = mathutils.Vector((cx, cy, sum(z for z, _ in profile) / len(profile)))
	rings = [ring(cx, cy, z, r, sides, turn) if r > 0 else [(cx, cy, z)] for z, r in profile]

	def face(points):
		centroid = sum((mathutils.Vector(p) for p in points), mathutils.Vector()) / len(points)
		facing(b, material, points, centroid - middle)

	for lower, upper in zip(rings, rings[1:]):
		for k in range(sides):
			m = (k + 1) % sides
			if len(lower) == 1:
				face([lower[0], upper[k], upper[m]])
			elif len(upper) == 1:
				face([lower[k], lower[m], upper[0]])
			else:
				face([lower[k], lower[m], upper[m], upper[k]])
	if len(rings[0]) > 1:
		facing(b, material, rings[0], (0, 0, -1))
	if len(rings[-1]) > 1:
		facing(b, material, rings[-1], (0, 0, 1))


def footing(b):
	frustum(b, "foot", (CENTRE, CENTRE), 1.9, 1.6, 0.0, FOOT, sides=8, bottom=False)


def add_jungle(b):
	footing(b)
	frustum(b, "pole", (CENTRE, CENTRE), 0.42, 0.36, FOOT, 11.2, sides=6, bottom=False)
	for z in (3.0, 7.0):                                                                    # rope bindings on the stem
		frustum(b, "rope", (CENTRE, CENTRE), 0.52, 0.52, z, z + 0.45, sides=6)
	lathe(b, "cup", (CENTRE, CENTRE), [(10.6, 0.40), (11.2, 0.95), (12.3, 1.25), (12.5, 1.15)], sides=8)   # the woven cup
	frustum(b, "rope", (CENTRE, CENTRE), 1.30, 1.30, 11.6, 11.95, sides=8)                  # a rope band round the cup


def add_hallow(b):
	footing(b)
	frustum(b, "pole", (CENTRE, CENTRE), 0.30, 0.26, FOOT, 10.6, sides=6, bottom=False)
	for z in (2.0, 6.0):                                                                    # wrought collars
		frustum(b, "pole", (CENTRE, CENTRE), 0.48, 0.48, z, z + 0.35, sides=6)
	for angle in (0, 120, 240):                                                             # three curled prongs holding the pumpkin
		a = math.radians(angle)
		x, y = CENTRE + math.cos(a) * 0.9, CENTRE + math.sin(a) * 0.9
		b.box("pole", (x - 0.15, y - 0.15, 9.6), (x + 0.15, y + 0.15, 10.9), skip=("-z",))
	lathe(b, "cup", (CENTRE, CENTRE), [(10.5, 0.6), (10.8, 1.35), (11.7, 1.65), (12.6, 1.35), (12.9, 0.6)], sides=8, turn=0.5)   # the pumpkin, a flat side towards -Y for the face
	frustum(b, "rope", (CENTRE, CENTRE), 0.22, 0.16, 12.9, 13.3, sides=5, top=True)         # its stalk, beside the flame
	# the carved face (a self-lit glow) on the side facing -Y, just proud of the pumpkin
	y = CENTRE - 1.56
	facing(b, "glow", [(CENTRE - 0.85, y, 11.95), (CENTRE - 0.35, y, 11.95), (CENTRE - 0.6, y, 12.35)], (0, -1, 0))       # eyes
	facing(b, "glow", [(CENTRE + 0.35, y, 11.95), (CENTRE + 0.85, y, 11.95), (CENTRE + 0.6, y, 12.35)], (0, -1, 0))
	facing(b, "glow", [(CENTRE - 0.85, y, 11.7), (CENTRE + 0.85, y, 11.7), (CENTRE + 0.5, y, 11.4), (CENTRE - 0.5, y, 11.4)], (0, -1, 0))   # the grin


def add_space(b):
	footing(b)
	frustum(b, "foot", (CENTRE, CENTRE), 0.9, 0.55, FOOT, 1.2, sides=8, bottom=False)       # a base collar
	frustum(b, "pole", (CENTRE, CENTRE), 0.30, 0.30, 1.2, 11.6, sides=8, bottom=False)
	for z in (4.0, 8.0):                                                                    # cyan light bands
		frustum(b, "rope", (CENTRE, CENTRE), 0.38, 0.38, z, z + 0.25, sides=8)
	frustum(b, "cup", (CENTRE, CENTRE), 0.40, 1.05, 11.4, 12.2, sides=8, top=False)          # the emitter dish
	frustum(b, "cup", (CENTRE, CENTRE), 1.05, 1.05, 12.2, 12.4, sides=8, bottom=False)
	frustum(b, "rope", (CENTRE, CENTRE), 0.62, 0.62, 12.2, 12.55, sides=8)                  # its glowing ring


def add_fantasy(b):
	footing(b)
	frustum(b, "pole", (CENTRE, CENTRE), 0.40, 0.40, FOOT, 11.0, sides=8, bottom=False)     # the candy stick
	lathe(b, "cup", (CENTRE, CENTRE), [(10.7, 0.45), (11.2, 1.05), (12.1, 1.15), (12.3, 1.0)], sides=8)   # a sugar cup
	for angle in range(0, 360, 60):                                                         # sugar drops round its rim
		a = math.radians(angle)
		x, y = CENTRE + math.cos(a) * 1.15, CENTRE + math.sin(a) * 1.15
		b.box("rope", (x - 0.18, y - 0.18, 12.05), (x + 0.18, y + 0.18, 12.45), skip=("-z",))


BUILDERS = {"jungle": add_jungle, "hallow": add_hallow, "space": add_space, "fantasy": add_fantasy}


def add_flame(b, theme):
	"""The flame in its node's frame: the origin is its base (the object is moved to the base afterwards). A rounded teardrop of eight sides: it swells just above its base and narrows to a point."""
	radius, height = FLAME_SIZE[theme]
	profile = [(0.0, 0.5), (0.12, 0.9), (0.28, 1.0), (0.46, 0.84), (0.64, 0.58), (0.82, 0.28), (1.0, 0.0)]
	lathe(b, "flame", (0.0, 0.0), [(height * z, radius * r) for z, r in profile], sides=8)


def build(theme):
	cabin.clear_scene()
	images = make_textures(theme)
	materials = {part: cabin.make_material(f"tt_{CODES[theme]}_{part}", image, self_lit=(part in ("flame", "glow"))) for part, image in images.items()}
	body_materials = {part: material for part, material in materials.items() if part != "flame"}
	b = cabin.Builder(body_materials)
	BUILDERS[theme](b)
	torch = b.build("torch")
	f = cabin.Builder({"flame": materials["flame"]})
	add_flame(f, theme)
	flame = f.build("flame")
	flame.location = (CENTRE, CENTRE, FLAME_BASE[theme])
	bpy.context.view_layer.objects.active = torch
	return torch, flame


def render_preview(path):
	scene = bpy.context.scene
	scene.render.engine = "BLENDER_WORKBENCH"
	scene.render.resolution_x, scene.render.resolution_y = 400, 600
	shading = scene.display.shading
	shading.light = "STUDIO"
	shading.color_type = "TEXTURE"
	scene.view_settings.view_transform = "Standard"
	world = bpy.data.worlds.new("preview") if scene.world is None else scene.world
	scene.world = world
	world.color = (0.55, 0.75, 0.55)
	camera_data = bpy.data.cameras.new("cam")
	camera_data.type = "ORTHO"
	camera_data.ortho_scale = 19.0
	camera = bpy.data.objects.new("cam", camera_data)
	scene.collection.objects.link(camera)
	target = np.array([CENTRE, CENTRE, 8.0])
	direction = np.array([-0.45, -0.75, 0.48])
	direction /= np.linalg.norm(direction)
	camera.location = tuple(target + direction * 60.0)
	camera.rotation_euler = mathutils.Vector(-direction).to_track_quat("-Z", "Y").to_euler()
	scene.camera = camera
	scene.render.filepath = path
	bpy.ops.render.render(write_still=True)


def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_torches")
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
		torch, flame = build(theme)
		print(f"{theme} torch: {len(torch.data.polygons)} + {len(flame.data.polygons)} polygons")
		if not opentpw_export.export(os.path.join(arguments.out, f"{theme}-torch.glb")):
			sys.exit(1)
		if arguments.preview:
			os.makedirs(arguments.preview, exist_ok=True)
			render_preview(os.path.join(arguments.preview, f"{theme}-torch.png"))


if __name__ == "__main__":
	main()
