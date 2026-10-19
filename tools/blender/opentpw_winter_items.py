"""Renders the warm item sprites of the winter extension (docs/WINTER.md): bobble hats, earmuffs and mittens as a guest wears them, seen from the five directions of the guest sprites (the
game mirrors them to eight), with the camera, lights, grade and dark edge of the 2x kid sets (``opentpw_kid.py``).

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_winter_items.py -- --out /tmp/winter/frames [--scale 2] [--supersample 4]

Output: ``<out>/tpw/Winter/WI/slot<S>_d<D>_f<F>.png`` (RGBA, trimmed, every frame quantised to one 255-colour palette) and a generator-format ``set.json`` (the one
``tools/pools/pack_sprite_sets.py`` reads; ``tools/winter/pack_sprites.py`` packs it).

Slots (one animation slot per colourway, 5 directions, the game mirrors them to 8 like the kids):

	0 1 2   bobble hats: red, blue, striped green and cream      1 frame per direction, hotspot = the top of the head
	3 4     earmuffs: grey band with red cups, navy band with pale blue cups   1 frame, hotspot = the centre of the head (the ear axis)
	5 6 7   mittens: red, blue, green; 2 frames per direction: frame 0 the kid's right mitten, frame 1 the left one   hotspot = the hand end point of the rig

Every frame's hotspot is the attach point of its item, which stands at the world origin of the render, the point the feet of a kid sprite stand on. The item is therefore placed by putting that point
at the anchor of the rig (``opentpw_guest_sprites.py --emit-winter-anchors`` writes the head-top, the head centre and both hand end points for every land slot and frame).

Design (all our own; neither Theme Park World nor Theme Park Inc has any of it):

- hats and earmuffs are rendered over a **holdout head proxy** (the rig's skull ellipsoid): the part of the item that is behind the head is already cut out, so the sprite can simply be drawn
  in front of the guest sprite. The same for the earmuffs' far cup. Mittens wrap the whole hand and are drawn over it;
- a slouchy bobble hat (a lathe of a cuff, a body that rises 0.55 of the head's height above the skull, a pom-pom), earmuffs (a round band over the crown and two fluffy cups) and mittens (a rounded
  mitten, a thumb and a cuff), in a few colourways; the size follows the kid's head (``HEAD`` = 0.84 of the rig's unit head) and hand.
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
import opentpw_umbrella as U  # noqa: E402

DIRECTIONS = 5
HEAD = 0.84                                  # the kid body's head scale (opentpw_kid.BODIES)
SKULL = (0.72 * HEAD, 0.70 * HEAD, 0.67 * HEAD)   # the skull ellipsoid's radii in world units
CANVAS = (48, 48)                            # 1x frame size and the hotspot's pixel in it
ORIGIN = (24, 30)
SIDES = 16

# colours, sRGB: our own palette
RED, BLUE, GREEN = (214, 40, 46), (48, 104, 208), (46, 150, 80)
WOOL, CREAM = (246, 244, 238), (240, 232, 208)
GREY, NAVY, PALE = (74, 78, 90), (30, 50, 110), (160, 200, 240)

# slot -> (name, kind, frames per direction)
SLOTS = {
	0: ("hat-red", "hat", 1), 1: ("hat-blue", "hat", 1), 2: ("hat-striped", "hat", 1),
	3: ("earmuffs-red", "earmuffs", 1), 4: ("earmuffs-blue", "earmuffs", 1),
	5: ("mittens-red", "mittens", 2), 6: ("mittens-blue", "mittens", 2), 7: ("mittens-green", "mittens", 2),
}
HAT_COLOURS = {0: dict(body=(RED, RED), cuff=WOOL, bobble=WOOL), 1: dict(body=(BLUE, BLUE), cuff=WOOL, bobble=WOOL), 2: dict(body=(GREEN, CREAM), cuff=RED, bobble=RED)}
MUFF_COLOURS = {3: dict(band=GREY, cup=RED, pad=WOOL), 4: dict(band=NAVY, cup=PALE, pad=WOOL)}
MITTEN_COLOURS = {5: dict(body=RED), 6: dict(body=BLUE), 7: dict(body=GREEN)}


def parse():
	a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	o = dict(out="/tmp/winter/frames", scale=2, supersample=4, dither=None)
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

def material(name, rgb, rough=0.8, spec=0.05):
	return K._principled(name, K.srgb(*rgb), False, rough, "plain", spec)


def new_object(name, bm, materials):
	mesh = bpy.data.meshes.new(name)
	bm.normal_update()
	bm.to_mesh(mesh)
	bm.free()
	obj = bpy.data.objects.new(name, mesh)
	bpy.context.scene.collection.objects.link(obj)
	for m in materials:
		mesh.materials.append(m)
	for polygon in mesh.polygons:
		polygon.use_smooth = True
	return obj


def lathe(name, profile, materials, sector_materials=None, sides=SIDES, closed_top=True, centre=(0.0, 0.0, 0.0)):
	"""A surface of revolution about the Z axis through ``centre``: ``profile`` is a list of (radius, z) from the lowest ring up; a radius 0 ends in an apex. Faces take the material of their
	sector (``sector_materials`` is one material index per sector, repeated round the axis) or material 0."""
	cx, cy, cz = centre
	bm = bmesh.new()
	rings = []
	for r, z in profile:
		if r <= 1e-6:
			rings.append(bm.verts.new((cx, cy, cz + z)))
		else:
			rings.append([bm.verts.new((cx + r * math.cos(2 * math.pi * k / sides), cy + r * math.sin(2 * math.pi * k / sides), cz + z)) for k in range(sides)])
	for j in range(len(profile) - 1):
		a, b = rings[j], rings[j + 1]
		for k in range(sides):
			n = (k + 1) % sides
			if isinstance(a, list) and isinstance(b, list):
				verts = [a[k], a[n], b[n], b[k]]
			elif isinstance(a, list):
				verts = [a[k], a[n], b]
			else:
				verts = [a, b[n], b[k]]
			try:
				f = bm.faces.new(verts)
			except ValueError:
				continue
			f.material_index = sector_materials[k % len(sector_materials)] if sector_materials else 0
	if closed_top is False:
		pass
	return new_object(name, bm, materials)


def ellipsoid(name, centre, radii, mat, rot=(0.0, 0.0, 0.0), u=14, v=9):
	bm = bmesh.new()
	bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
	bmesh.ops.scale(bm, vec=radii, verts=bm.verts)
	if any(rot):
		bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=mathutils.Euler(rot).to_matrix())
	bmesh.ops.translate(bm, vec=centre, verts=bm.verts)
	return new_object(name, bm, [mat])


def clear_items():
	for o in list(bpy.data.objects):
		if o.type in {"MESH", "EMPTY"}:
			bpy.data.objects.remove(o, do_unlink=True)
	for m in list(bpy.data.materials):
		bpy.data.materials.remove(m)


def head_proxy(centre_z):
	"""The holdout head: everything of the item behind or inside this ellipsoid is cut out of the render."""
	proxy = ellipsoid("headProxy", (0.0, 0.0, centre_z), SKULL, material("proxy", (128, 128, 128)), u=24, v=14)
	proxy.is_holdout = True
	return proxy


def build_hat(slot):
	"""A slouchy bobble hat sitting on the head: the hotspot is the top of the skull (the world origin), so the head's centre is SKULL[2] below it."""
	c = HAT_COLOURS[slot]
	clear_items()
	hz = -SKULL[2]                                         # the head centre's z
	body_a, body_b = c["body"]
	m_body = [material("hat_a", body_a), material("hat_b", body_b)]
	m_cuff, m_bobble = material("hat_cuff", c["cuff"], 0.9), material("hat_bobble", c["bobble"], 0.95)
	# the cuff band sits on the brow line (above the eyebrows, 0.40 of the unit head over the head centre), the body rises from it
	z = lambda value: hz + value
	cuff = [(0.52, z(0.40)), (0.575, z(0.44)), (0.60, z(0.52)), (0.60, z(0.68))]
	body = [(0.565, z(0.68)), (0.545, z(0.74)), (0.50, z(0.84)), (0.43, z(0.96)), (0.33, z(1.06)), (0.21, z(1.13)), (0.09, z(1.18)), (0.0, z(1.20))]
	striped = body_a != body_b
	lathe("hatCuff", cuff, [m_cuff])
	lathe("hatBody", body, m_body, sector_materials=[0, 1] if striped else [0])
	ellipsoid("bobble", (0.0, 0.0, z(1.20) + 0.12), (0.19, 0.19, 0.17), m_bobble, u=10, v=7)
	head_proxy(hz)


def build_earmuffs(slot):
	"""Earmuffs: a band over the crown from ear to ear and a fluffy cup on each ear; the hotspot is the head centre (the world origin)."""
	c = MUFF_COLOURS[slot]
	clear_items()
	m_band, m_cup, m_pad = material("muff_band", c["band"], 0.5, 0.2), material("muff_cup", c["cup"], 0.9), material("muff_pad", c["pad"], 0.95)
	a, b = SKULL[0] * 1.14, SKULL[2] * 1.12
	points = [(a * math.sin(phi), 0.0, b * math.cos(phi)) for phi in (-1.42 + 2.84 * i / 16 for i in range(17))]
	U.tube("muffBand", points, 0.075, m_band, sides=6)
	for s in (-1, 1):
		ellipsoid(f"muffCup{s}", (0.70 * s, -0.02, -0.02), (0.13, 0.30, 0.30), m_cup, u=12, v=8)
		ellipsoid(f"muffPad{s}", (0.80 * s, -0.02, -0.02), (0.06, 0.24, 0.24), m_pad, u=12, v=8)
	head_proxy(0.0)


def build_mitten(slot, side):
	"""One mitten (side +1 the kid's right hand, -1 the left) as the hand hangs at the kid's side; the hotspot is the rig's hand end point (the world origin), 0.04 below the hand's centre.
	A short, elongated knitted mitten that continues the forearm line (a rounded tip at the bottom, a slightly narrower wrist above), a thumb bump on the inner side and a ribbed cuff in the same wool tone."""
	c = MITTEN_COLOURS[slot]
	m_body = material("mit_body", c["body"], 0.9)
	tilt = math.radians(-14 * side)                         # the arm hangs abducted: the mitten's lower end points outwards
	# a rounded tip, the palm (widest at 0.32 of the length from the tip, radius 0.24 so it covers the hand in every frame), then the wrist; flattened front to back like a hand
	body = [(0.0, -0.36), (0.10, -0.34), (0.19, -0.27), (0.235, -0.14), (0.24, 0.0), (0.22, 0.12), (0.17, 0.19)]
	mitten = lathe("mitten", body, [m_body], sides=SIDES)
	mitten.scale = (1.0, 0.82, 1.0)
	# the knit cuff: a ribbed band (rings alternate between two radii), the same wool as the body
	cuff = [(0.17, 0.18)]
	for i in range(4):
		cuff += [(0.21, 0.21 + 0.055 * i), (0.19, 0.237 + 0.055 * i)]
	cuff += [(0.21, 0.21 + 0.055 * 4), (0.12, 0.21 + 0.055 * 4 + 0.015), (0.0, 0.21 + 0.055 * 4 + 0.02)]       # closed on top: the forearm's sprite is drawn under it
	cuff_obj = lathe("cuff", cuff, [m_body], sides=SIDES)
	cuff_obj.scale = (1.0, 0.82, 1.0)
	thumb = ellipsoid("thumb", (-0.21 * side, 0.12, -0.04), (0.09, 0.10, 0.18), m_body, rot=(math.radians(-8), math.radians(-38 * side), 0.0), u=10, v=7)
	for p in (mitten, cuff_obj, thumb):
		p.rotation_euler = (0.0, tilt, 0.0)
	bpy.context.view_layer.update()


# ---- Rendering ---------------------------------------------------------------------------------------------------

def render_set(scale, supersample):
	K.SUPERSAMPLE = supersample
	st = K.setup_scene(CANVAS, ORIGIN, "TPW", scale=scale)
	tmp = os.path.join(os.environ.get("TMPDIR", "/tmp"), f"winter-{os.getpid()}.png")
	frames = {}
	for slot, (name, kind, count) in SLOTS.items():
		for f in range(count):
			if kind == "hat":
				build_hat(slot)
			elif kind == "earmuffs":
				build_earmuffs(slot)
			else:
				clear_items()
				build_mitten(slot, 1 if f == 0 else -1)
			for d in range(DIRECTIONS):
				K.aim_camera(st, d)
				frames[(slot, d, f)] = K.render_frame(st, tmp, binary_alpha=True)
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
	for slot, (name, kind, count) in sorted(SLOTS.items()):
		rows = []
		for d in range(DIRECTIONS):
			row = []
			for f in range(count):
				img, ox, oy = trimmed[(slot, d, f)]
				fname = f"slot{slot:02d}_d{d}_f{f}.png"
				K.write_png(img, os.path.join(base, fname))
				row.append({"file": fname, "originX": ox, "originY": oy, "width": int(img.shape[1]), "height": int(img.shape[0])})
			rows.append(row)
		slots.append({"slot": slot, "name": name, "source": "winter", "directions": DIRECTIONS, "framesPerDirection": count, "frames": rows})
	meta = {"name": "SPR_WI", "set": "WI", "edition": "TPW", "folder": "Winter", "fullDetail": False, "kidFlag": 0, "colours": K.QUANTISE_COLOURS,
		"pixelsPerUnit": 128 * scale, "scale": scale, "slots": slots}
	json.dump(meta, open(os.path.join(base, "set.json"), "w"), indent=1)


def main():
	o = parse()
	frames, st = render_set(o["scale"], o["supersample"])
	write_set(os.path.join(o["out"], "tpw", "Winter", "WI"), frames, o["scale"], o["dither"])
	print("winter set:", len(frames), "frames", flush=True)


if __name__ == "__main__":
	main()
