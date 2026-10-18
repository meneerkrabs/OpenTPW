"""Renders the umbrella sprites of the umbrellas extension (docs/UMBRELLAS.md): an open umbrella as a guest carries it, in three colourways, seen from the five
directions of the guest sprites (the game mirrors them to eight), with the camera, lights, grade and dark edge of the 2x kid sets (``opentpw_kid.py``).

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_umbrella.py -- --out /tmp/umbrellas/frames [--scale 2] [--supersample 4]

Output: ``<out>/tpw/Umbrella/UM/slot<C>_d<D>_f0.png`` (C the colourway 0 to 2, D the direction 0 to 4; RGBA, trimmed, the 15 frames quantised to one 255-colour palette) and a
generator-format ``set.json`` (the one ``tools/pools/pack_sprite_sets.py`` reads; ``tools/umbrellas/pack_sprites.py`` packs it). The hotspot of every frame is the **handle tip**: the lowest point of the
umbrella's crook, which stands at the world origin of the render, the point the feet of a kid sprite stand on. The umbrella is therefore placed by putting that tip at the guest's hand
(``--emit-anchors`` of ``opentpw_guest_sprites.py`` writes where the hand lands for every kid set and direction), then pulled towards the camera so it covers the guest.

Design (all our own; the original Theme Park World has no umbrella and Theme Park Inc's is not copied, its numbers only inform the size):

- an eight-panel canopy with scalloped edges, a dome that rises 0.6 of its radius, a ferrule at the top and a rim band; the panels are the colourway: 0 plain (one sky blue), 1 rainbow (eight hues),
  2 striped (coral and cream, alternating panels);
- a shaft with a hooked crook, tilted from the vertical by the angle that brings the canopy centre over the head of a kid whose hand is at its side (the rig's hand, ``HAND_X`` from the kid's body)
  and canopy perpendicular to the shaft, as a person carries it in the rain;
- the size: the canopy is 0.8 of a kid's height across (Theme Park Inc's ratio after its 1.25 sprite scale, docs/UMBRELLAS.md) and the shaft puts its rim just over the head of the kid.
"""
import json
import math
import os
import sys

import bmesh
import bpy
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_kid as K  # noqa: E402
import opentpw_guest_sprites as G  # noqa: E402

DIRECTIONS = 5
COLOURWAYS = ("plain", "rainbow", "striped")
SLOT_NAMES = {0: "plain", 1: "rainbow", 2: "striped"}
KID_HEIGHT = 3.45          # a standing kid's top in Blender units (the head centre at 2.54 plus its radius; the standing frame is about 38 pixels at 11.1 per unit)
CANOPY_RADIUS = 0.8 * KID_HEIGHT / 2            # the canopy is 0.8 of a kid's height across
CANOPY_RISE = 0.6 * CANOPY_RADIUS               # the dome's height above the rim
HAND_X = 0.86              # the rig's hand at the kid's side (opentpw_kid.py: shoulder x plus the arm's abduction)
HAND_Z = 0.96              # and its height above the feet
RIM_ABOVE_HEAD = 0.25      # the canopy's rim clears the head by this much
SHAFT_LENGTH = (KID_HEIGHT + RIM_ABOVE_HEAD + CANOPY_RISE * 0.9) - HAND_Z   # handle tip to the apex: the rim hangs 0.9 of the rise below it
TILT = math.atan2(HAND_X, SHAFT_LENGTH)         # towards the body: the canopy centre ends up above the head
CROOK_RADIUS = 0.2
SHAFT_RADIUS = 0.055
RIBS = 8
RING_SCALE = 0.42          # the inner ring of the canopy's panels, as a fraction of the radius
SCALLOP = 0.93             # the edge of a panel dips to this fraction of the radius between two ribs

# colours, sRGB: our own palette
PLAIN = (46, 138, 216)
RAINBOW = ((226, 52, 52), (240, 140, 40), (244, 210, 50), (84, 176, 70), (44, 176, 176), (50, 100, 214), (136, 74, 190), (226, 96, 160))
STRIPE_A, STRIPE_B = (232, 92, 76), (250, 238, 214)
METAL = (196, 200, 208)
WOOD = (122, 76, 40)
RIM = (36, 40, 60)

CANVAS = (64, 64)          # 1x frame size and the hotspot's pixel in it: the umbrella stands above the tip and its canopy reaches to both sides
ORIGIN = (32, 52)


def parse():
	a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	o = dict(out="/tmp/umbrellas/frames", scale=2, supersample=4, dither=None)
	i = 0
	while i < len(a):
		k = a[i]
		if k in ("--out", "--scale", "--supersample", "--dither"):
			v = a[i + 1]
			o[k[2:]] = int(v) if k in ("--scale", "--supersample") else (float(v) if k == "--dither" else v)
			i += 2
		else:
			raise SystemExit(f"unknown argument {k}")
	return o


# ---- Geometry ----------------------------------------------------------------------------------------------------

def material(name, rgb, rough=0.55, spec=0.2):
	return K._principled(name, K.srgb(*rgb), False, rough, "plain", spec)


def canopy_mesh(name, panel_materials, rim_material, slots):
	"""The canopy in its own frame: the apex at the origin, the axis down -Z, the rim at Z = -CANOPY_RISE. Panel k lies between ribs k and k + 1. Returns the object; ``panel_materials`` is one material
	slot index per panel, the last slot the rim band."""
	r, rise = CANOPY_RADIUS, CANOPY_RISE
	dome = lambda radius, lift=0.0: -rise * (radius / r) ** 1.7 + lift
	mesh = bpy.data.meshes.new(name)
	bm = bmesh.new()
	apex = bm.verts.new((0, 0, 0.0))
	tips, mids, inner = [], [], []
	for k in range(RIBS):
		angle = 2 * math.pi * k / RIBS
		half = math.pi / RIBS
		tips.append(bm.verts.new((r * math.cos(angle), r * math.sin(angle), dome(r))))
		mids.append(bm.verts.new((r * SCALLOP * math.cos(angle + half), r * SCALLOP * math.sin(angle + half), dome(r * SCALLOP, 0.08 * rise))))
		inner.append(bm.verts.new((r * RING_SCALE * math.cos(angle), r * RING_SCALE * math.sin(angle), dome(r * RING_SCALE))))
	skirt = [bm.verts.new((v.co.x, v.co.y, v.co.z - 0.16)) for v in tips]
	skirt_mid = [bm.verts.new((v.co.x, v.co.y, v.co.z - 0.16)) for v in mids]

	def face(verts, index):
		try:
			f = bm.faces.new(verts)
		except ValueError:
			return
		f.material_index = index
		f.smooth = False

	for k in range(RIBS):
		n = (k + 1) % RIBS
		face([apex, inner[k], inner[n]], slots[k])
		face([inner[k], tips[k], mids[k]], slots[k])
		face([inner[k], mids[k], inner[n]], slots[k])
		face([inner[n], mids[k], tips[n]], slots[k])
		# the rim band under the scalloped edge, outward normals: tip -> mid -> next tip along the edge
		face([tips[k], skirt[k], skirt_mid[k], mids[k]], len(panel_materials))
		face([mids[k], skirt_mid[k], skirt[n], tips[n]], len(panel_materials))
	bm.normal_update()
	bm.to_mesh(mesh)
	bm.free()
	obj = bpy.data.objects.new(name, mesh)
	bpy.context.scene.collection.objects.link(obj)
	for m in panel_materials:
		mesh.materials.append(m)
	mesh.materials.append(rim_material)
	# every normal points outward (the construction's winding is not trusted): panels away from a point inside the dome, the rim band away from the axis
	bm = bmesh.new()
	bm.from_mesh(mesh)
	for f in bm.faces:
		centre = f.calc_center_median()
		inside = mathutils.Vector((0, 0, -2 * rise)) if f.material_index < len(panel_materials) else mathutils.Vector((0, 0, centre.z))
		if f.normal.dot(centre - inside) < 0:
			f.normal_flip()
	bm.to_mesh(mesh)
	bm.free()
	return obj


def tube(name, points, radius, mat, sides=6, cap=True):
	"""A round tube along a polyline (parallel-transported rings, flat caps)."""
	bm = bmesh.new()
	rings = []
	previous = None
	for i, p in enumerate(points):
		p = mathutils.Vector(p)
		t = (mathutils.Vector(points[min(i + 1, len(points) - 1)]) - mathutils.Vector(points[max(i - 1, 0)])).normalized()
		if previous is None:
			helper = mathutils.Vector((1, 0, 0)) if abs(t.x) < 0.9 else mathutils.Vector((0, 1, 0))
			normal = t.cross(helper).normalized()
		else:
			normal = (previous - t * previous.dot(t)).normalized()
		previous = normal
		binormal = t.cross(normal)
		rings.append([bm.verts.new(p + (normal * math.cos(a) + binormal * math.sin(a)) * radius) for a in (2 * math.pi * j / sides for j in range(sides))])
	for a, b in zip(rings, rings[1:]):
		for j in range(sides):
			bm.faces.new([a[j], a[(j + 1) % sides], b[(j + 1) % sides], b[j]])
	if cap:
		bm.faces.new(list(reversed(rings[0])))
		bm.faces.new(rings[-1])
	bm.normal_update()
	mesh = bpy.data.meshes.new(name)
	bm.to_mesh(mesh)
	bm.free()
	obj = bpy.data.objects.new(name, mesh)
	bpy.context.scene.collection.objects.link(obj)
	mesh.materials.append(mat)
	for polygon in mesh.polygons:
		polygon.use_smooth = True
	return obj


def umbrella_points():
	"""The crook as the polyline from the hook's end, down and round the tip at the origin, then up the shaft to the apex (in the umbrella's frame, before the tilt)."""
	c = CROOK_RADIUS
	points = []
	steps = 10
	# a semicircle in the YZ plane about (0, c, c): from the hook end (0, 2c, c) round the bottom (0, c, 0) to (0, 0, c): the straight shaft starts there
	for i in range(steps + 1):
		a = math.pi * i / steps          # 0: hook end, pi: the shaft's foot
		points.append((0.0, c + c * math.cos(a), c - c * math.sin(a)))
	# the bottom of the semicircle is at a = pi/2: (0, c, 0); shift so that point is the origin
	points = [(x, y - c, z) for x, y, z in points]
	points.append((0.0, -c, c + 0.3))
	points.append((0.0, -c, SHAFT_LENGTH))
	return points


def build_umbrella(colourway):
	"""Builds the umbrella of a colourway at the world origin (handle tip), tilted as carried, and returns its objects."""
	for o in list(bpy.data.objects):
		if o.type == "MESH":
			bpy.data.objects.remove(o, do_unlink=True)
	for m in list(bpy.data.materials):
		bpy.data.materials.remove(m)
	if colourway == "plain":
		panels = [material("um_plain", PLAIN)] * RIBS
	elif colourway == "rainbow":
		panels = [material(f"um_rainbow{i}", c) for i, c in enumerate(RAINBOW)]
	else:
		a, b = material("um_stripe_a", STRIPE_A), material("um_stripe_b", STRIPE_B)
		panels = [a if k % 2 == 0 else b for k in range(RIBS)]
	unique = []
	for m in panels:
		if m not in unique:
			unique.append(m)
	slots = [unique.index(m) for m in panels]
	rim = material("um_rim", RIM, 0.6, 0.1)
	canopy = canopy_mesh("canopy", unique, rim, slots)
	metal, wood = material("um_metal", METAL, 0.35, 0.5), material("um_wood", WOOD, 0.7, 0.1)
	pts = umbrella_points()
	shaft = tube("shaft", pts, SHAFT_RADIUS, metal)
	# the crook's handle in wood: the lower part of the polyline, a little thicker
	handle = tube("handle", pts[:12], SHAFT_RADIUS * 1.35, wood)
	ferrule = tube("ferrule", [(0, -CROOK_RADIUS, SHAFT_LENGTH), (0, -CROOK_RADIUS, SHAFT_LENGTH + 0.28)], SHAFT_RADIUS * 0.75, metal, sides=5)
	apex = mathutils.Vector((0, -CROOK_RADIUS, SHAFT_LENGTH))
	canopy.location = apex
	root = bpy.data.objects.new("umbrella", None)
	bpy.context.scene.collection.objects.link(root)
	for o in (canopy, shaft, handle, ferrule):
		o.parent = root
	# the tilt: about the world Y axis, towards the body (-X, as the kid's right hand is at +X), through the handle tip at the origin
	root.rotation_euler = (0.0, -TILT, 0.0)
	bpy.context.view_layer.update()
	return [root, canopy, shaft, handle, ferrule]


# ---- Rendering ---------------------------------------------------------------------------------------------------

def render_set(scale, supersample):
	K.SUPERSAMPLE = supersample
	st = K.setup_scene(CANVAS, ORIGIN, "TPW", scale=scale)
	tmp = os.path.join(os.environ.get("TMPDIR", "/tmp"), f"umbrella-{os.getpid()}.png")
	frames = {}
	for slot, colourway in SLOT_NAMES.items():
		build_umbrella(colourway)
		for d in range(DIRECTIONS):
			K.aim_camera(st, d)
			frames[(slot, 0, d)] = K.render_frame(st, tmp, binary_alpha=True)
	if os.path.exists(tmp):
		os.remove(tmp)
	return frames, st


def write_set(base, frames, scale, dither):
	"""Quantises (one palette), trims and writes the frames and the generator-format set.json."""
	os.makedirs(base, exist_ok=True)
	keys = sorted(frames)
	arrays, _ = K.quantise_set([frames[k] for k in keys], dither=dither)
	origin = (ORIGIN[0] * scale, ORIGIN[1] * scale)
	trimmed = {k: G.trim(a, origin) for k, a in zip(keys, arrays)}
	slots = []
	for slot in sorted(SLOT_NAMES):
		rows = []
		for d in range(DIRECTIONS):
			img, ox, oy = trimmed[(slot, 0, d)]
			fname = f"slot{slot:02d}_d{d}_f0.png"
			K.write_png(img, os.path.join(base, fname))
			rows.append([{"file": fname, "originX": ox, "originY": oy, "width": int(img.shape[1]), "height": int(img.shape[0])}])
		slots.append({"slot": slot, "name": SLOT_NAMES[slot], "source": "umbrella", "directions": DIRECTIONS, "framesPerDirection": 1, "frames": rows})
	meta = {"name": "SPR_UM", "set": "UM", "edition": "TPW", "folder": "Umbrellas", "fullDetail": False, "kidFlag": 0, "colours": K.QUANTISE_COLOURS,
		"pixelsPerUnit": 128 * scale, "scale": scale, "slots": slots}
	json.dump(meta, open(os.path.join(base, "set.json"), "w"), indent=1)


def main():
	o = parse()
	frames, st = render_set(o["scale"], o["supersample"])
	write_set(os.path.join(o["out"], "tpw", "Umbrella", "UM"), frames, o["scale"], o["dither"])
	print("umbrella set:", len(frames), "frames, tilt", round(math.degrees(TILT), 1), "degrees, shaft", round(SHAFT_LENGTH, 2), "units", flush=True)


if __name__ == "__main__":
	main()
