"""Builds the swimming-pool changing room (a small cabin, 2 x 2 cells) for each theme and exports one .glb per theme.

Usage:
	blender -b --factory-startup --python tools/blender/opentpw_changing_room.py -- --out <dir> [--themes jungle,hallow,space,fantasy] [--preview <dir>]

Writes ``<dir>/<theme>.glb`` (and, with --preview, ``<dir>/<theme>.png``, an 800 x 600 workbench render from the front, left and above, for looking at the
result; it is not part of the content). Everything is generated here, geometry and textures, so the cabin needs no game data and contains no original art.

Footprint: 2 x 2 cells = the Blender X/Y range 0..20, with the Info.Shape (first row = top = +Y)

	**
	2*

so the entrance cell is the lower left one: the path-side door is in the Y = 0 face over X 0..10, and the pool-side door is in the Y = 20 face, centred at
X = 10. The walls are inset by half a unit, the roof reaches the full 20 x 20. The roof peak is 16 units high (a kid guest is about 10 units tall).

One geometry serves all themes; only the textures (and a few trim parts) differ:
jungle = bamboo and plank walls with a thatched roof, hallow = dark mossy stone with purple trim, space = riveted metal with a cyan light strip,
fantasy = pastel candy colours with a rounded roof. Textures are 64 x 64, tile in both directions and have at most 15-character names.
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_conventions as conventions  # noqa: E402
import opentpw_export  # noqa: E402

SIZE = 20.0                # 2 x 2 cells
INSET = 0.5                # the walls stand this far inside the footprint; the roof overhangs to it
WALL = 1.0                 # wall thickness
WALL_HEIGHT = 10.0
PEAK = 16.0
DOOR_HEIGHT = 8.0
DOOR_WIDTH = 4.0
FRONT_DOOR_X = 5.0         # centre of the entrance cell column
BACK_DOOR_X = 10.0
TEXTURE = 64
TILE = 10.0                # world units one texture repeat covers

THEMES = ("jungle", "hallow", "space", "fantasy")
CODES = {"jungle": "jun", "hallow": "hal", "space": "spc", "fantasy": "fan"}


# --------------------------------------------------------------------------------------------------- textures

def _rng(seed):
	return np.random.default_rng(seed)


def _noise(rng, amount):
	"""Per-pixel luminance noise, tiling trivially (it is white noise)."""
	return 1.0 + (rng.random((TEXTURE, TEXTURE, 1)) - 0.5) * 2 * amount


def _grid():
	y, x = np.mgrid[0:TEXTURE, 0:TEXTURE]
	return x, y


def _finish(name, rgb):
	"""Makes a tiling 64 x 64 image from an (H, W, 3) float array (rows top to bottom as the viewer sees them)."""
	rgb = np.clip(rgb, 0.0, 1.0)
	image = bpy.data.images.new(name, width=TEXTURE, height=TEXTURE, alpha=False)
	pixels = np.ones((TEXTURE, TEXTURE, 4), dtype=np.float32)
	pixels[..., :3] = rgb[::-1]            # Blender stores rows bottom to top
	image.pixels = pixels.ravel().tolist()
	image.file_format = "PNG"
	image.pack()
	return image


def _planks(name, base, seed, vertical=True, count=4, groove=0.55):
	"""Planks (or bamboo stems) with a dark groove between them, wood grain along them and a few knots."""
	rng = _rng(seed)
	x, y = _grid()
	across = (x if vertical else y)
	along = (y if vertical else x)
	width = TEXTURE // count
	plank = across // width
	local = across % width
	colour = np.zeros((TEXTURE, TEXTURE, 3))
	for i in range(count):
		shade = 0.82 + 0.3 * rng.random()
		tint = np.array(base) * shade
		colour[plank == i] = tint
	grain = 1.0 + 0.10 * np.sin(local[..., None] * 1.7 + plank[..., None] * 2.3 + along[..., None] * 0.15)
	colour = colour * grain * _noise(rng, 0.04)
	groove_mask = (local == 0) | (local == width - 1)
	colour[groove_mask] *= groove
	for _ in range(3):                      # knots
		kx, ky = rng.integers(2, TEXTURE - 2, 2)
		colour[max(ky - 1, 0):ky + 2, max(kx - 1, 0):kx + 2] *= 0.7
	return _finish(name, colour)


def _thatch(name, base, seed):
	"""Straw: horizontal bundles with ragged dark lines."""
	rng = _rng(seed)
	x, y = _grid()
	row = y // 8
	colour = np.zeros((TEXTURE, TEXTURE, 3))
	for i in range(TEXTURE // 8):
		colour[row == i] = np.array(base) * (0.85 + 0.25 * rng.random())
	strands = rng.random((TEXTURE, TEXTURE, 1))
	colour = colour * (0.82 + 0.3 * strands)
	colour[(y % 8) == 7] *= 0.55
	colour[(y % 8) == 0] *= 0.8
	return _finish(name, colour)


def _stone(name, base, moss, seed):
	"""Dark stone blocks in running bond with moss creeping into the joints."""
	rng = _rng(seed)
	x, y = _grid()
	row = y // 16
	offset = (row % 2) * 16
	col = ((x + offset) % 32)
	colour = np.zeros((TEXTURE, TEXTURE, 3))
	block = (row * 2 + ((x + offset) // 32)) % 7
	for b in range(7):
		colour[block == b] = np.array(base) * (0.8 + 0.07 * b)
	colour = colour * _noise(rng, 0.10)
	joint = ((y % 16) == 0) | (col == 0)
	colour[joint] = np.array(base) * 0.4
	moss_mask = (rng.random((TEXTURE, TEXTURE)) < 0.18) & (((y % 16) < 4) | (col < 3))
	colour[moss_mask] = np.array(moss) * (0.7 + 0.5 * rng.random((int(moss_mask.sum()), 1)))
	return _finish(name, colour)


def _shingles(name, base, edge, seed, scallop=False):
	"""Roof shingles in rows; rounded scallops when requested."""
	rng = _rng(seed)
	x, y = _grid()
	row = y // 16
	offset = (row % 2) * 8
	colour = np.zeros((TEXTURE, TEXTURE, 3))
	cell = ((x + offset) // 16 + row) % 3
	for i in range(3):
		colour[cell == i] = np.array(base) * (0.88 + 0.12 * i)
	colour = colour * _noise(rng, 0.03)
	ly = y % 16
	lx = (x + offset) % 16
	if scallop:
		dist = np.sqrt((lx - 8.0) ** 2 + (ly - 0.0) ** 2)
		colour[(dist > 11.0)] = np.array(edge)
		colour[(np.abs(dist - 11.0) < 1.0)] *= 0.7
	else:
		colour[ly >= 14] *= 0.6
		colour[lx == 0] *= 0.7
	return _finish(name, colour)


def _metal(name, base, seed, dark_edge=0.6):
	"""Riveted metal panels: a bevelled 32 x 32 panel grid with a rivet in each corner."""
	rng = _rng(seed)
	x, y = _grid()
	lx, ly = x % 32, y % 32
	colour = np.zeros((TEXTURE, TEXTURE, 3)) + np.array(base)
	colour = colour * (0.94 + 0.10 * np.sin(x[..., None] * 0.2)) * _noise(rng, 0.03)
	edge = (lx < 2) | (ly < 2) | (lx > 29) | (ly > 29)
	colour[edge] *= dark_edge
	bevel = ((lx == 2) | (ly == 2))
	colour[bevel] *= 1.25
	for cx in (5, 26):
		for cy in (5, 26):
			disc = (lx - cx) ** 2 + (ly - cy) ** 2 <= 4
			colour[disc] = np.array(base) * 1.35
			hole = (lx - cx - 1) ** 2 + (ly - cy - 1) ** 2 <= 1
			colour[hole] *= 0.6
	return _finish(name, colour)


def _candy(name, colours, seed, diagonal=True):
	"""Candy stripes (8 pixels wide, tiling because 64 is a multiple of the period)."""
	rng = _rng(seed)
	x, y = _grid()
	stripe = (((x + y) if diagonal else x) // 8) % len(colours)
	colour = np.zeros((TEXTURE, TEXTURE, 3))
	for i, c in enumerate(colours):
		colour[stripe == i] = c
	colour = colour * _noise(rng, 0.015)
	return _finish(name, colour)


def _plain(name, base, seed, line=None):
	"""A flat colour with a faint grain, optionally with one line across (a trim bead)."""
	rng = _rng(seed)
	colour = np.zeros((TEXTURE, TEXTURE, 3)) + np.array(base)
	colour = colour * _noise(rng, 0.03)
	if line is not None:
		colour[30:34] = np.array(line)
	return _finish(name, colour)


def make_textures(theme):
	"""The wall, roof, trim and (space only) light textures of a theme."""
	code = CODES[theme]
	n = lambda part: f"cr_{code}_{part}"
	if theme == "jungle":
		return {
			"wall": _planks(n("wall"), (0.74, 0.52, 0.27), 11, count=4),
			"roof": _thatch(n("roof"), (0.80, 0.68, 0.30), 12),
			"trim": _planks(n("trim"), (0.52, 0.35, 0.18), 13, count=2, groove=0.45),
		}
	if theme == "hallow":
		return {
			"wall": _stone(n("wall"), (0.24, 0.24, 0.27), (0.20, 0.38, 0.18), 21),
			"roof": _shingles(n("roof"), (0.30, 0.16, 0.38), (0.16, 0.08, 0.22), 22),
			"trim": _plain(n("trim"), (0.42, 0.22, 0.58), 23, line=(0.62, 0.38, 0.80)),
		}
	if theme == "space":
		return {
			"wall": _metal(n("wall"), (0.64, 0.70, 0.78), 31),
			"roof": _metal(n("roof"), (0.34, 0.38, 0.46), 32, dark_edge=0.5),
			"trim": _plain(n("trim"), (0.22, 0.25, 0.32), 33, line=(0.35, 0.40, 0.50)),
			"light": _plain(n("light"), (0.35, 0.95, 1.00), 34),
		}
	return {
		"wall": _candy(n("wall"), ((1.00, 0.72, 0.84), (1.00, 0.88, 0.93)), 41),
		"roof": _shingles(n("roof"), (0.70, 0.92, 0.82), (0.55, 0.82, 0.72), 42, scallop=True),
		"trim": _candy(n("trim"), ((0.78, 0.70, 0.95), (0.90, 0.85, 1.00)), 43, diagonal=False),
	}


def make_material(name, image, self_lit=False):
	material = bpy.data.materials.new(name)
	material.use_nodes = True
	material.use_backface_culling = True       # the game culls back faces
	shader = material.node_tree.nodes.get("Principled BSDF")
	texture = material.node_tree.nodes.new("ShaderNodeTexImage")
	texture.image = image
	texture.interpolation = "Closest"
	material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
	material[conventions.WRAP_PROPERTY] = True
	material[conventions.SELF_LIT_PROPERTY] = self_lit
	return material


# --------------------------------------------------------------------------------------------------- geometry

class Builder:
	"""Collects faces (each a list of 3D points, counter-clockwise seen from outside) per material and makes one mesh."""

	def __init__(self, materials):
		self.materials = materials          # name -> bpy material
		self.names = list(materials)
		self.faces = []                     # (material name, points)

	def quad(self, material, a, b, c, d):
		self.faces.append((material, [a, b, c, d]))

	def poly(self, material, points):
		self.faces.append((material, list(points)))

	def box(self, material, lo, hi, skip=(), faces_material=None):
		"""An axis-aligned box. ``skip`` names faces to leave out ("-x", "+x", "-y", "+y", "-z", "+z");
		``faces_material`` maps a face name to a different material."""
		x0, y0, z0 = lo
		x1, y1, z1 = hi
		faces_material = faces_material or {}
		sides = {
			"-x": [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],
			"+x": [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
			"-y": [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
			"+y": [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
			"-z": [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)],
			"+z": [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
		}
		for side, points in sides.items():
			if side not in skip:
				self.poly(faces_material.get(side, material), points)

	def build(self, name):
		mesh = bpy.data.meshes.new(name)
		bm = bmesh.new()
		uv_layer = bm.loops.layers.uv.new("UVMap")
		for material_name, points in self.faces:
			verts = [bm.verts.new(p) for p in points]
			try:
				face = bm.faces.new(verts)
			except ValueError:
				continue
			face.material_index = self.names.index(material_name)
			face.normal_update()
			n = face.normal
			ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
			for loop in face.loops:
				p = loop.vert.co
				if az >= ax and az >= ay:
					u, v = p.x, p.y
				elif ax >= ay:
					u, v = p.y, p.z
				else:
					u, v = p.x, p.z
				loop[uv_layer].uv = (u / TILE, v / TILE)
		bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
		bm.to_mesh(mesh)
		bm.free()
		obj = bpy.data.objects.new(name, mesh)
		bpy.context.scene.collection.objects.link(obj)
		for material_name in self.names:
			mesh.materials.append(self.materials[material_name])
		for polygon in mesh.polygons:
			polygon.use_smooth = False
		return obj


def roof_profile(theme):
	"""The roof cross-section across Y as (y, z) points from the front eave over the ridge to the back eave."""
	if theme == "fantasy":
		segments = 8
		points = []
		for i in range(segments + 1):
			angle = math.pi * i / segments
			points.append((SIZE / 2 - math.cos(angle) * SIZE / 2, WALL_HEIGHT + math.sin(angle) * (PEAK - WALL_HEIGHT)))
		return points
	return [(0.0, WALL_HEIGHT), (SIZE / 2, PEAK), (SIZE, WALL_HEIGHT)]


def add_roof(b, theme):
	profile = roof_profile(theme)
	x0, x1 = 0.0, SIZE
	# Outer skin: one quad per profile segment, normal pointing up and out.
	for (ya, za), (yb, zb) in zip(profile, profile[1:]):
		b.quad("roof", (x0, ya, za), (x1, ya, za), (x1, yb, zb), (x0, yb, zb))
	# Under side, seen from inside the cabin: one flat ceiling.
	b.quad("trim", (x0, 0.0, WALL_HEIGHT - 0.0), (x0, SIZE, WALL_HEIGHT), (x1, SIZE, WALL_HEIGHT), (x1, 0.0, WALL_HEIGHT))
	# Gable ends: the profile polygon down to the wall top, in the wall material.
	end = [(y, z) for y, z in profile]
	b.poly("wall", [(x0, y, z) for y, z in end])                  # -x end: the profile runs clockwise in (y, z), whose normal is -x
	b.poly("wall", [(x1, y, z) for y, z in reversed(end)])        # +x end: counter-clockwise, normal +x
	# Eave beam along the front and back, in trim.
	b.box("trim", (x0, 0.0, WALL_HEIGHT - 0.7), (x1, 0.6, WALL_HEIGHT), skip=("+z", "-z", "+y"))
	b.box("trim", (x0, SIZE - 0.6, WALL_HEIGHT - 0.7), (x1, SIZE, WALL_HEIGHT), skip=("+z", "-z", "-y"))


def add_walls(b, theme):
	lo, hi = INSET, SIZE - INSET
	h = WALL_HEIGHT
	door_top = DOOR_HEIGHT
	f0, f1 = FRONT_DOOR_X - DOOR_WIDTH / 2, FRONT_DOOR_X + DOOR_WIDTH / 2
	k0, k1 = BACK_DOOR_X - DOOR_WIDTH / 2, BACK_DOOR_X + DOOR_WIDTH / 2
	# floor slab
	b.box("trim", (lo, lo, 0.0), (hi, hi, 0.4), skip=("-z", "-x", "+x", "-y", "+y"))
	# side walls (full height)
	b.box("wall", (lo, lo, 0.0), (lo + WALL, hi, h), skip=("-z", "+z"))
	b.box("wall", (hi - WALL, lo, 0.0), (hi, hi, h), skip=("-z", "+z"))
	# front wall (path side, Y = 0.5..1.5): the pieces left and right of the door, and the lintel
	b.box("wall", (lo + WALL, lo, 0.0), (f0, lo + WALL, h), skip=("-z", "+z", "-x"))
	b.box("wall", (f1, lo, 0.0), (hi - WALL, lo + WALL, h), skip=("-z", "+z", "+x"))
	b.box("wall", (f0, lo, door_top), (f1, lo + WALL, h), skip=("+z", "-x", "+x"))
	# back wall (pool side): door centred at X = 10
	b.box("wall", (lo + WALL, hi - WALL, 0.0), (k0, hi, h), skip=("-z", "+z", "-x"))
	b.box("wall", (k1, hi - WALL, 0.0), (hi - WALL, hi, h), skip=("-z", "+z", "+x"))
	b.box("wall", (k0, hi - WALL, door_top), (k1, hi, h), skip=("+z", "-x", "+x"))
	# door frames: posts and a lintel board in trim, standing half a unit proud of the wall
	frame = 0.5
	for x_lo, x_hi, y_a, y_b, touching in ((f0, f1, lo - frame, lo, "+y"), (k0, k1, hi, hi + frame, "-y")):
		b.box("trim", (x_lo - frame, y_a, 0.0), (x_lo, y_b, door_top + frame), skip=("-z", touching))
		b.box("trim", (x_hi, y_a, 0.0), (x_hi + frame, y_b, door_top + frame), skip=("-z", touching))
		b.box("trim", (x_lo, y_a, door_top), (x_hi, y_b, door_top + frame), skip=("-x", "+x", touching))
	# corner posts
	post = 1.4
	for px in (lo - 0.2, hi - post + 0.2):
		for py in (lo - 0.2, hi - post + 0.2):
			b.box("trim", (px, py, 0.0), (px + post, py + post, h), skip=("-z", "+z"))


def add_theme_parts(b, theme):
	"""A few trim differences per theme."""
	if theme == "jungle":
		# a bamboo sign board above the front door
		b.box("trim", (2.5, 0.0, WALL_HEIGHT - 0.2), (7.5, INSET, WALL_HEIGHT + 1.6), skip=("-z", "+y"))
	elif theme == "hallow":
		# a small spike on the ridge
		b.box("trim", (9.4, 9.4, PEAK - 0.1), (10.6, 10.6, PEAK + 1.6), skip=("-z",))
	elif theme == "space":
		# cyan light strips over both doors and along the ridge
		b.box("light", (3.0, 0.2, WALL_HEIGHT - 1.4), (7.0, INSET, WALL_HEIGHT - 0.8), skip=("-z", "+y"))
		b.box("light", (8.0, SIZE - INSET, WALL_HEIGHT - 1.4), (12.0, SIZE - 0.2, WALL_HEIGHT - 0.8), skip=("-z", "-y"))
		b.box("light", (0.5, 9.7, PEAK - 0.4), (SIZE - 0.5, 10.3, PEAK + 0.3), skip=("-z",))
	else:
		# a candy ball on top of the roof
		b.box("trim", (9.3, 9.3, PEAK - 0.2), (10.7, 10.7, PEAK + 1.4), skip=("-z",))


def clear_scene():
	for obj in list(bpy.data.objects):
		bpy.data.objects.remove(obj, do_unlink=True)
	for datablock in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
		for item in list(datablock):
			datablock.remove(item)


def build(theme):
	clear_scene()
	images = make_textures(theme)
	materials = {part: make_material(f"cr_{CODES[theme]}_{part}", image, self_lit=(part == "light")) for part, image in images.items()}
	b = Builder(materials)
	add_walls(b, theme)
	add_roof(b, theme)
	add_theme_parts(b, theme)
	obj = b.build("cabin")
	bpy.context.view_layer.objects.active = obj
	return obj


# --------------------------------------------------------------------------------------------------- preview

def render_preview(path):
	scene = bpy.context.scene
	scene.render.engine = "BLENDER_WORKBENCH"
	scene.render.resolution_x, scene.render.resolution_y = 800, 600
	scene.render.film_transparent = False
	shading = scene.display.shading
	shading.light = os.environ.get("CABIN_PREVIEW_LIGHT", "STUDIO")
	shading.color_type = "TEXTURE"
	shading.show_cavity = False
	shading.show_shadows = True
	scene.view_settings.view_transform = "Standard"
	world = bpy.data.worlds.new("preview") if scene.world is None else scene.world
	scene.world = world
	world.color = (0.55, 0.75, 0.55)
	camera_data = bpy.data.cameras.new("cam")
	camera_data.type = "ORTHO"
	camera_data.ortho_scale = 42.0
	camera = bpy.data.objects.new("cam", camera_data)
	scene.collection.objects.link(camera)
	# from the front (-Y), the left (-X) and above: the entrance side and the left wall face the viewer
	target = np.array([10.0, 10.0, 7.0])
	direction = np.array([-0.55, -0.75, 0.62])
	direction /= np.linalg.norm(direction)
	camera.location = tuple(target + direction * 80.0)
	look = -direction
	import mathutils
	camera.rotation_euler = mathutils.Vector(look).to_track_quat("-Z", "Y").to_euler()
	scene.camera = camera
	scene.render.filepath = path
	bpy.ops.render.render(write_still=True)


# --------------------------------------------------------------------------------------------------- main

def parse_arguments():
	argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	parser = argparse.ArgumentParser(prog="opentpw_changing_room")
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
		print(f"{theme}: {len(obj.data.polygons)} polygons, {sum(len(p.vertices) - 2 for p in obj.data.polygons)} triangles")
		if not opentpw_export.export(os.path.join(arguments.out, f"{theme}.glb")):
			sys.exit(1)
		if arguments.preview:
			os.makedirs(arguments.preview, exist_ok=True)
			render_preview(os.path.join(arguments.preview, f"{theme}.png"))


if __name__ == "__main__":
	main()
