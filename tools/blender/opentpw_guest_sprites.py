"""Renders complete replacement guest sprite sets (every animation slot, plus the swim slots and the rider head sets) from the
parametric people of ``opentpw_kid.py``, ready to be packed into ``.ESP``/``.TPC``/``.FPC`` files (``SpriteSetBuilder``).

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- --out /tmp/swimart/guests \
      [--edition TPW|TPI] [--sets BE,CH] [--part land,swim,pool,heads] [--slots walk,run] [--outfit clothes|swim] [--no-quantise] \
      [--scale 2] [--supersample 4] [--dither 0.05] [--emit-anchors]

Output, per set: ``<out>/<edition>/<Folder>/<SET>/slot<NN>_d<d>_f<f>.png`` (RGBA, trimmed to the frame's content, the whole set
quantised to one 255-colour palette) and ``set.json`` describing the set for the packer:

  {"name": "SPR_BE", "edition": "TPW", "folder": "Generic/Kids", "fullDetail": true, "kidFlag": 0, "colours": 255,
   "slots": [{"slot": 1, "name": "walk", "source": "land", "directions": 5, "framesPerDirection": 8,
              "frames": [[{"file": "slot01_d0_f0.png", "originX": -12, "originY": -37, "width": 22, "height": 38}, ...], ...]}]}

``originX``/``originY`` follow the sprite reader's convention (``SpriteFrame.OriginX/Y``): the offset of the frame's top left pixel
from the hotspot, so ``(-12, -37)`` means the frame starts 12 pixels left of and 37 above the hotspot. Standing sprites have the
hotspot between the feet on the floor, swimmers the torso centre at the water line, heads the centre of the skull. Frames are
listed direction-major, as the bank stores them. ``fullDetail`` is the ESP byte 0x10C (the set ships an ``.FPC`` too; the game draws the ``.TPC``).

``--scale N`` renders every frame at N times the pixel size (canvas, hotspot offsets and pixels per Blender unit all N times larger, so the
sprite covers the same world area with N times the detail) and writes ``"pixelsPerUnit": 128 * N`` into ``set.json``; the packer puts that
into every frame's pixels-per-unit fields (``SpriteFrame.PixelsPerUnitX/Y``) and the game scales its quad by 128 / the field, so a 2x set
is drawn at the same size as the 1x one. ``--part pool`` renders the swimwear table of the pools mod (``opentpw_pool_poses.POOL_SLOTS``: stand,
walk, ladder, dive, lying, shower, slide, the three strokes, splash-down, spa sitting, ring floating, rope swing) into ``<out>/<edition>/Pool/<SET>/``.

``--emit-anchors`` (umbrellas extension, docs/UMBRELLAS.md) additionally writes ``<out>/<edition>/anchors.json``: for every kid set and direction 0 to 4 the point where the
kid's right hand (the rig's ``handEnd`` end point) lands in the land frames, in sprite pixels from the feet hotspot at the set's pixels per unit (x to the right, y down, so a hand above the
feet has a negative y), for the standing frame (slot 0), every walking frame (slot 1) and the waiting frames (slot 14), plus the hand's height above the feet in Blender units. It is the same camera
as the frames, so the numbers are exact by construction; nothing is rendered (``--part none --emit-anchors`` writes only the table, in seconds). Directions 5 to 7 are the mirror of 3 to 1 (the game flips the
frame), so their hand x is the negated one.

``--emit-winter-anchors`` (winter extension, docs/WINTER.md) writes ``<out>/<edition>/winter-anchors.json`` (schema 2, a separate file: the umbrellas' ``anchors.json`` and its emission above are unchanged
byte for byte): for **every** land slot and frame of every kid set and each direction 0 to 4 the head-top point and the head centre, both hand end points with the forearm's screen angle, and a
visibility flag per hand (a ray from the hand towards the camera against the body), plus the head's yaw and pitch per frame. Same camera as the frames, nothing rendered.

Slots: the original table's slots keep their numbers, frame counts and directions (see ``opentpw_guest_anims.py``); the swimmers
take the free slots 8 (front crawl, 8 frames), 9 (breaststroke, 8) and 10 (treading water, 4), all with 5 directions. Heads
(``Kidsheads``, ``Parentsheads``, ``Grandparentsheads``) have one slot 0 of 7 elevation rows by 8 azimuths.
"""
import json
import math
import os
import sys

import bpy
import mathutils
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_kid as K  # noqa: E402
import opentpw_guest_anims as A  # noqa: E402
import opentpw_swim_sprites as SW  # noqa: E402
import opentpw_pool_poses as PP  # noqa: E402

DIRECTIONS = 5
LAND_HOTSPOT_DROP = 0      # the hotspot is the floor point the render puts at the world origin: a standing kid's soles then end 3 to 5 pixels below
                           # it at 1x, as the originals' TPC (mean 3.9 against 4.0 to 4.2); it was 1 against the FPC, whose soles end 1 to 2 below
LAND_CANVAS = {"TPW": ((100, 90), (50, 78)), "TPI": ((140, 130), (70, 112))}
HEAD_CANVAS = {"TPW": ((64, 64), (32, 32)), "TPI": ((80, 80), (40, 40))}
TORSO_CANVAS = {"TPW": ((96, 96), (48, 48)), "TPI": ((120, 120), (60, 60))}   # slots whose hotspot is the torso centre (a body may point up or down the frame)
HEAD_FIT = 1.0             # our heads render a little smaller than the geometry says so their frames match the originals
HEAD_HOTSPOT_DROP = {"TPW": 0, "TPI": 6}   # the shipped TPI heads put the hotspot about 9 px below the head centre (TPW: about 3, which ours already has; measured against ours the net shift is 6)
HEAD_SCALE = 0.685          # the game draws a head sprite at 0.685 of the body's pixel scale (docs/GUESTS.md)
HEAD_PPU = {"TPW": 11.7, "TPI": 14.4}   # the heads' own pixels per unit: they match the original head sets (TPC only) at the scale the bodies
                           # had before October 18, 2026; the TPW bodies are 11.1 since then, measured against the TPC, which would make the heads 6 % small
SWIM_SLOTS = {"A": (8, "swim-crawl", 8), "B": (9, "swim-breaststroke", 8), "C": (10, "swim-tread", 4)}
SWIM_KEY = {8: "A", 9: "B", 10: "C"}      # slot -> the swim generator's pose key
RING_COLOURS = {"BE": (255, 130, 30), "BI": (255, 215, 40), "CH": (255, 255, 255), "FR": (255, 255, 255), "KI": (255, 120, 170), "SA": (255, 215, 40), "SU": (255, 130, 30), "TA": (255, 255, 255)}   # float ring colour per kid

# set -> (Folder, heads folder, slot table, member name of the set, member name of the head set)
LAYOUT = {
	"BE": ("Kids", "Kidsheads", A.TPW_KID_SLOTS), "BI": ("Kids", "Kidsheads", A.TPW_KID_SLOTS), "CH": ("Kids", "Kidsheads", A.TPW_KID_SLOTS),
	"FR": ("Kids", "Kidsheads", A.TPW_KID_SLOTS), "KI": ("Kids", "Kidsheads", A.TPW_KID_SLOTS), "SA": ("Kids", "Kidsheads", A.TPW_KID_SLOTS),
	"SU": ("Kids", "Kidsheads", A.TPW_KID_SLOTS), "TA": ("Kids", "Kidsheads", A.TPW_KID_SLOTS),
	"BD": ("Kids", "Kidsheads", A.TPI_KID_SLOTS), "GB": ("Kids", "Kidsheads", A.TPI_KID_SLOTS), "GK": ("Kids", "Kidsheads", A.TPI_KID_SLOTS),
	"KB": ("Kids", "Kidsheads", A.TPI_KID_SLOTS), "FA": ("Parents", "Parentsheads", A.TPI_ADULT_SLOTS), "MO": ("Parents", "Parentsheads", A.TPI_ADULT_SLOTS),
	"GM": ("Grandparents", "Grandparentsheads", A.TPI_ADULT_SLOTS), "GR": ("Grandparents", "Grandparentsheads", A.TPI_ADULT_SLOTS),
}
HEAD_MEMBER = {"BD": "Spr_bd", "GB": "Spr_gb"}      # the TPI kid heads of BD and GB ship with these member names


def parse():
	a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	o = dict(out="/tmp/swimart/guests", edition="TPW", sets=None, part="land,swim,heads", slots=None, outfit="clothes", quantise=True, dither=None, scale=1, supersample=None, emit_anchors=False, emit_winter_anchors=False)
	i = 0
	while i < len(a):
		k = a[i]
		if k == "--no-quantise": o["quantise"] = False; i += 1
		elif k == "--emit-anchors": o["emit_anchors"] = True; i += 1
		elif k == "--emit-winter-anchors": o["emit_winter_anchors"] = True; i += 1
		elif k in ("--out", "--edition", "--sets", "--part", "--slots", "--outfit", "--dither", "--scale", "--supersample"):
			v = a[i + 1]
			o[k[2:]] = v.split(",") if k in ("--sets", "--part", "--slots") else (float(v) if k == "--dither" else (int(v) if k in ("--scale", "--supersample") else v))
			i += 2
		else:
			raise SystemExit(f"unknown argument {k}")
	if isinstance(o["part"], str):
		o["part"] = o["part"].split(",")
	return o


def snap_alpha(a):
	"""Fully opaque, fully transparent or the water's translucency: a handful of alpha values keeps the palette for colours."""
	alpha = a[..., 3]
	out = a.copy()
	out[..., 3] = np.where(alpha < 0.3, 0.0, np.where(alpha >= 0.87, 1.0, SW.K.SUBMERGED_ALPHA))
	out[out[..., 3] == 0, :3] = 0
	return out


def trim(a, origin):
	"""Crops a canvas frame to its content; returns (cropped, originX, originY) with the origin relative to the hotspot pixel."""
	ys, xs = np.nonzero(a[..., 3] > 0)
	x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
	return a[y0:y1, x0:x1], int(x0 - origin[0]), int(y0 - origin[1])


def render_land(st, name, slots, outfit, tmp):
	"""Renders the land slots of a set; returns {(slot, d, f): array} on the land canvas."""
	kid = K.build_kid(name, underwater=False, outfit=outfit)
	out = {}
	for slot, slot_name, n in slots:
		fn = A.SLOT_POSES[slot_name]
		for f in range(n):
			K.apply_pose(kid, fn(kid, f, n))
			for d in range(DIRECTIONS):
				K.aim_camera(st, d)
				out[(slot, d, f)] = K.render_frame(st, tmp, binary_alpha=True)
	K.clear_kid(kid)
	return out


def render_swim(st, name, tmp):
	"""Renders the swim slots (swimwear, wet hair, the water line) on the swim canvas, like ``opentpw_swim_sprites.py``."""
	m_foam = K._emissive("foam", (0.9, 0.97, 1.0, 1.0), 0.75)
	m_foam2 = K._emissive("foam2", (0.8, 0.94, 1.0, 1.0), 0.4)
	m_drop = K._emissive("drop", (0.86, 0.95, 1.0, 1.0), 0.9)
	rings = (SW.make_ring("ringInner", m_foam), SW.make_ring("ringOuter", m_foam2))
	kid = K.build_kid(name, underwater=True, swim=True)
	out = {}
	for key, (slot, slot_name, n) in SWIM_SLOTS.items():
		pose_fn = SW.POSES[key]
		for f in range(n):
			drops = SW.spawn_droplets(kid, pose_fn, f, n, m_drop, ord(key))
			K.apply_pose(kid, pose_fn(f, n))
			SW.place_rings(rings, key, f, kid.size)
			for d in range(DIRECTIONS):
				K.aim_camera(st, d)
				out[(slot, d, f)] = snap_alpha(K.render_frame(st, tmp))
			for dr in drops:
				bpy.data.objects.remove(dr, do_unlink=True)
	K.clear_kid(kid)
	return out


def _each_direction(st, tmp, out, slot, f, wet):
	for d in range(DIRECTIONS):
		K.aim_camera(st, d)
		frame = K.render_frame(st, tmp, binary_alpha=not wet)
		out[(slot, d, f)] = snap_alpha(frame) if wet else frame


def render_pool_feet(st, name, tmp):
	"""Swimwear on the deck, the hotspot between the feet: stand (0), walk (1) and the shower (6, with falling drops)."""
	m_drop = K._emissive("drop", (0.86, 0.95, 1.0, 1.0), 0.9)
	kid = K.build_kid(name, underwater=False, outfit="swim")
	out = {}
	for slot, slot_name, n, _, _ in [s for s in PP.POOL_SLOTS if s[4] == "feet"]:
		for f in range(n):
			if slot_name == "shower":
				K.apply_pose(kid, PP.shower(kid, f, n))
				drops = PP.spawn_drops(PP.shower_drops(f, kid.size), 0.1, m_drop)
			else:
				K.apply_pose(kid, A.SLOT_POSES[slot_name](kid, f, n))
				drops = []
			_each_direction(st, tmp, out, slot, f, False)
			for dr in drops:
				bpy.data.objects.remove(dr, do_unlink=True)
	K.clear_kid(kid)
	return out


def render_pool_torso(st, name, tmp):
	"""The swimwear slots whose hotspot is the torso centre (ladder, dive, lying, slide, rope: dry) and the ones whose hotspot is the torso at the
	water line (the strokes, splash-down, spa, ring: wet hair, the water's tint and translucency below the line)."""
	out = {}
	dry = K.build_kid(name, underwater=False, outfit="swim")
	for slot, slot_name, n, kind, hot in PP.POOL_SLOTS:
		if kind != "dry" or hot != "torso":
			continue
		for f in range(n):
			K.apply_pose(dry, PP.POSES[slot_name](dry, f, n))
			_each_direction(st, tmp, out, slot, f, False)
	K.clear_kid(dry)
	m_foam = K._emissive("foam", (0.9, 0.97, 1.0, 1.0), 0.75)
	m_foam2 = K._emissive("foam2", (0.8, 0.94, 1.0, 1.0), 0.4)
	m_drop = K._emissive("drop", (0.86, 0.95, 1.0, 1.0), 0.9)
	m_ring = K._principled("floatRing", K.srgb(*RING_COLOURS.get(name, (255, 130, 30))), True, 0.5, "plain", 0.5, painted=False)
	rings = (SW.make_ring("ringInner", m_foam), SW.make_ring("ringOuter", m_foam2))
	wet = K.build_kid(name, underwater=True, swim=True)
	for slot, slot_name, n, kind, hot in PP.POOL_SLOTS:
		if kind != "wet":
			continue
		for f in range(n):
			props = []
			if slot in SWIM_KEY:
				key = SWIM_KEY[slot]
				pose_fn = SW.POSES[key]
				props += SW.spawn_droplets(wet, pose_fn, f, n, m_drop, ord(key))
				K.apply_pose(wet, pose_fn(f, n))
				SW.place_rings(rings, key, f, wet.size)
			else:
				K.apply_pose(wet, PP.POSES[slot_name](wet, f, n))
				place_pool_rings(rings, slot_name, f, wet.size)
				if slot_name == "splash":
					props += PP.spawn_drops(PP.splash_drops(f, wet.size), 0.12, m_drop)
				if slot_name == "ring":
					props.append(PP.make_torus("floatRing", m_ring, 0.98 * wet.size, 0.27 * wet.size, location=(0, 0.04, -0.05 + 0.04 * math.sin(PP.TAU * f / n))))
			_each_direction(st, tmp, out, slot, f, True)
			for pr in props:
				bpy.data.objects.remove(pr, do_unlink=True)
	K.clear_kid(wet)
	return out


def place_pool_rings(rings, slot_name, f, size):
	"""Foam rings for the new water slots: the splash-down's grow from the landing, the spa's and the ring's lap gently round the body."""
	inner, outer = rings
	if slot_name == "splash":
		g = (0.35, 0.8, 1.25, 1.6)[f % 4] * size
		inner.scale, outer.scale = (g, g * 0.85, 1), (g * 1.45, g * 1.2, 1)
	elif slot_name == "ring":
		inner.scale, outer.scale = (1.25 * size, 1.1 * size, 1), (1.7 * size, 1.45 * size, 1)
	else:
		pulse = 1.0 + 0.05 * math.sin(PP.TAU * f / 2)
		inner.scale, outer.scale = (1.0 * pulse * size, 0.9 * pulse * size, 1), (1.4 * pulse * size, 1.2 * pulse * size, 1)
	inner.location = outer.location = (0, 0, 0.02)


def aim_head(st, centre, row, col):
	"""Puts the camera at elevation ``row`` * 30 degrees from the head's up pole and azimuth ``col`` * 45 degrees from its front,
	turning so that column 2 sees the head's left side (face to the left of the picture, as in the original's heads, docs/GUESTS.md
	"Riders' heads"), looking at ``centre``. The sprite's up is the head's up, and at the poles (rows 0 and 6) it follows the azimuth."""
	def direction(theta):
		phi = math.radians(45 * col)
		return mathutils.Vector((-math.sin(theta) * math.sin(phi), math.sin(theta) * math.cos(phi), math.cos(theta)))
	theta = math.radians(30 * row)
	d = direction(theta)
	t2 = min(max(theta, 0.05), math.pi - 0.05)      # the up vector is taken slightly off the poles
	d2 = direction(t2)
	up = mathutils.Vector((0, 0, 1)) - d2 * d2.z
	up.normalize()
	z = d.normalized()
	x = up.cross(z).normalized()
	y = z.cross(x)
	rot = mathutils.Matrix(((x.x, y.x, z.x), (x.y, y.y, z.y), (x.z, y.z, z.z)))
	st.cam.rotation_euler = rot.to_euler()
	st.cam.location = centre + d * 40.0
	bpy.context.view_layer.update()


def render_heads(st, name, tmp):
	"""The 7 x 8 head frames of a set (dry hair, hats on), keyed (0, row, col)."""
	kid = K.build_kid(name, underwater=False, outfit="clothes")
	K.apply_pose(kid, A.stand(kid))
	head = kid.pivots["head"]
	keep = set()
	for o in kid.objects:
		p = o
		while p is not None and p != head:
			p = p.parent
		if p == head:
			keep.add(o.name)
	for o in kid.objects:
		if o.name not in keep:
			o.hide_render = True
	centre = head.matrix_world.translation.copy()
	out = {}
	for row in range(7):
		for col in range(8):
			aim_head(st, centre, row, col)
			out[(0, row, col)] = K.render_frame(st, tmp, binary_alpha=True)
	K.clear_kid(kid)
	return out


ANCHOR_SLOTS = {0: "stand", 1: "walk", 14: "wait"}   # the land slots the umbrella follows: standing, walking, waiting in a queue
ANCHOR_HAND = 1                                     # the rig's +X hand: the right hand of a kid facing +Y


def hand_anchor(st, kid):
	"""Where the hand end point of the current pose lands, from the feet hotspot (the world origin), in the frame's pixels at the stage's scale: (dx right, dy down)."""
	rot = st.cam.matrix_world.to_3x3()
	v = kid.ends[f"hand{ANCHOR_HAND}"].matrix_world.translation
	right, up = rot @ mathutils.Vector((1, 0, 0)), rot @ mathutils.Vector((0, 1, 0))
	return round(st.ppu * v.dot(right), 2), round(-st.ppu * v.dot(up), 2)


def emit_anchors(st, names, edition, scale):
	"""The hand anchors of the land kids (``stand``, ``walk`` and ``wait`` slots, five directions, every frame) of the given sets: the numbers of ``hand_anchor`` for the poses the
	land frames use, nothing rendered. The table also names the camera, so a stale table is detectable."""
	sets = {}
	for name in names:
		kid = K.build_kid(name, underwater=False, outfit="clothes")
		per_slot = {}
		height = None
		for slot, slot_name, n in LAYOUT[name][2]:
			if slot not in ANCHOR_SLOTS:
				continue
			fn = A.SLOT_POSES[slot_name]
			frames = []
			for f in range(n):
				K.apply_pose(kid, fn(kid, f, n))
				if slot == 0:
					height = round(kid.ends[f"hand{ANCHOR_HAND}"].matrix_world.translation.z, 3)
				row = []
				for d in range(DIRECTIONS):
					K.aim_camera(st, d)
					row.append(list(hand_anchor(st, kid)))
				frames.append(row)
			per_slot[ANCHOR_SLOTS[slot]] = {"slot": slot, "frames": [[frames[f][d] for f in range(n)] for d in range(DIRECTIONS)]}
		sets[name] = {"handHeight": height, **per_slot}
		K.clear_kid(kid)
	return {"schema": 1, "generator": "tools/blender/opentpw_guest_sprites.py --emit-anchors", "edition": edition, "pixelsPerUnit": 128 * scale, "scale": scale, "elevation": st.elevation,
		"directions": DIRECTIONS, "hand": "right", "units": "pixels at pixelsPerUnit from the feet hotspot, x right and y down; handHeight in Blender units above the feet; directions 5 to 7 mirror 3 to 1 with x negated",
		"sets": sets}


# ---- Winter anchors (schema 2): the head and both hands for every land slot ----------------------------------------------------------

WINTER_FIELDS = ["headTopX", "headTopY", "headX", "headY", "rightHandX", "rightHandY", "rightHandAngle", "leftHandX", "leftHandY", "leftHandAngle", "visible"]
HEAD_TOP_LOCAL = 0.67      # the skull's top in the head pivot's own units (``opentpw_kid.build_kid``: the skull radii are (0.72, 0.7, 0.67) times the head's scale)
HAND_RAY_START = 0.35      # the visibility ray starts this far from the hand end point towards the camera, outside the hand's own sphere
HAND_RAY_LENGTH = 40.0
OWN_ARM = ("hand{s}", "foreArm{s}", "elbowBall{s}", "upperArm{s}", "shoulderBall{s}")


def _screen(st, point):
	"""A world point as (x right, y down) pixels from the feet hotspot at the stage's scale."""
	rot = st.cam.matrix_world.to_3x3()
	right, up = rot @ mathutils.Vector((1, 0, 0)), rot @ mathutils.Vector((0, 1, 0))
	return st.ppu * point.dot(right), -st.ppu * point.dot(up)


def hand_visible(st, kid, side):
	"""Whether the hand end point of ``side`` (-1 left, +1 right) is seen from the camera: a ray from just in front of the hand towards the camera hits nothing but the hand's own arm."""
	depsgraph = bpy.context.evaluated_depsgraph_get()
	towards = (st.cam.matrix_world.to_3x3() @ mathutils.Vector((0, 0, 1))).normalized()
	origin = kid.ends[f"hand{side}"].matrix_world.translation + towards * HAND_RAY_START
	own = tuple(pattern.format(s=side) for pattern in OWN_ARM)
	remaining = HAND_RAY_LENGTH
	while remaining > 0:
		hit, location, _, _, obj, _ = st.scene.ray_cast(depsgraph, origin, towards, distance=remaining)
		if not hit:
			return True
		if not obj.name.startswith(own):
			return False
		step = (location - origin).length + 0.01
		origin = location + towards * 0.01
		remaining -= step
	return True


def winter_record(st, kid, tops):
	"""The 11 numbers of ``WINTER_FIELDS`` for the current pose and camera."""
	top = _screen(st, tops["top"].matrix_world.translation)
	centre = _screen(st, kid.pivots["head"].matrix_world.translation)
	record = [top[0], top[1], centre[0], centre[1]]
	flags = 0
	for bit, side in ((0, 1), (1, -1)):
		hand = _screen(st, kid.ends[f"hand{side}"].matrix_world.translation)
		elbow = _screen(st, kid.pivots[f"elbow{side}"].matrix_world.translation)
		angle = math.degrees(math.atan2(hand[0] - elbow[0], hand[1] - elbow[1]))      # 0 = the forearm points straight down the picture, positive towards the right
		record += [hand[0], hand[1], angle]
		if hand_visible(st, kid, side):
			flags |= 1 << bit
	return [round(value, 1) for value in record] + [flags]


def emit_winter_anchors(st, names, edition, scale):
	"""Schema 2 of the anchors: every land slot of the kid sets with every frame and direction (see the module docstring)."""
	sets = {}
	for name in names:
		kid = K.build_kid(name, underwater=False, outfit="clothes")
		head = kid.pivots["head"]
		top = bpy.data.objects.new("headTop", None)
		bpy.context.scene.collection.objects.link(top)
		top.parent = head
		top.location = (0, 0, HEAD_TOP_LOCAL)
		kid.objects.append(top)
		tops = {"top": top}
		slots = {}
		for slot, slot_name, n in LAYOUT[name][2]:
			fn = A.SLOT_POSES[slot_name]
			points = [[] for _ in range(DIRECTIONS)]
			yaw, pitch = [], []
			for f in range(n):
				pose = fn(kid, f, n)
				K.apply_pose(kid, pose)
				yaw.append(round(pose.get("head_yaw", 0.0), 1))
				pitch.append(round(pose.get("head_pitch", 0.0), 1))
				for d in range(DIRECTIONS):
					K.aim_camera(st, d)
					points[d].append(winter_record(st, kid, tops))
			slots[slot_name] = {"slot": slot, "frames": n, "headYaw": yaw, "headPitch": pitch, "points": points}
		sets[name] = {"slots": slots}
		K.clear_kid(kid)
	return {"schema": 2, "generator": "tools/blender/opentpw_guest_sprites.py --emit-winter-anchors", "edition": edition, "pixelsPerUnit": 128 * scale, "scale": scale, "elevation": st.elevation,
		"directions": DIRECTIONS, "fields": WINTER_FIELDS,
		"units": "pixels at pixelsPerUnit from the feet hotspot, x right and y down; rightHandAngle and leftHandAngle in degrees, the forearm (elbow to hand) rotated from straight down the picture, positive towards the right; "
			"visible bit 0 = the kid's right hand is seen, bit 1 = the left hand; headYaw and headPitch in degrees per frame; directions 5 to 7 mirror 3 to 1 with every x negated and every angle negated",
		"headTop": "the top of the skull, where a hat's hotspot goes; headX and headY is the head centre, where the earmuffs' hotspot goes; the hands' points are the rig's hand end points, where a mitten's hotspot goes",
		"skullRadii": [round(0.72 * 0.84, 3), round(0.70 * 0.84, 3), round(0.67 * 0.84, 3)],
		"sets": sets}


def dump_compact(value, level=0):
	"""JSON with the structure indented and every list of plain numbers on one line (the winter anchors have tens of thousands of numbers)."""
	pad = " " * (level + 1)
	if isinstance(value, dict):
		inner = (",\n").join(f"{pad}{json.dumps(key)}: {dump_compact(item, level + 1)}" for key, item in value.items())
		return "{\n" + inner + "\n" + " " * level + "}"
	if isinstance(value, list):
		if all(isinstance(item, (int, float)) and not isinstance(item, bool) for item in value):
			return json.dumps(value, separators=(",", ":"))
		inner = (",\n").join(pad + dump_compact(item, level + 1) for item in value)
		return "[\n" + inner + "\n" + " " * level + "]"
	return json.dumps(value)


def write_set(base, name, edition, folder, member, frames, slot_meta, quantise, dither, scale=1, folder_label=None):
	"""Quantises (one palette for the set), trims and writes the frames and set.json."""
	os.makedirs(base, exist_ok=True)
	keys = sorted(frames)
	arrays = [frames[k] for k in keys]
	if quantise:
		arrays, _ = K.quantise_set(arrays, dither=dither)
	trimmed = {k: trim(a, slot_meta[k[0]]["_origin"]) for k, a in zip(keys, arrays)}
	slots_json = []
	for slot in sorted(slot_meta):
		m = slot_meta[slot]
		rows = []
		for d in range(m["directions"]):
			row = []
			for f in range(m["framesPerDirection"]):
				img, ox, oy = trimmed[(slot, d, f)]
				fname = f"slot{slot:02d}_d{d}_f{f}.png"
				K.write_png(img, os.path.join(base, fname))
				row.append({"file": fname, "originX": ox, "originY": oy, "width": int(img.shape[1]), "height": int(img.shape[0])})
			rows.append(row)
		entry = {"slot": slot, "name": m["name"], "source": m["source"], "directions": m["directions"], "framesPerDirection": m["framesPerDirection"], "frames": rows}
		if "hotspot" in m:
			entry["hotspot"] = m["hotspot"]
		slots_json.append(entry)
	meta = {"name": member, "set": name, "edition": edition, "folder": folder_label or f"Generic/{folder}", "fullDetail": True, "kidFlag": 1 if K.PRESETS[name]["girl"] and folder == "Kids" else 0,
		"colours": K.QUANTISE_COLOURS if quantise else 0, "pixelsPerUnit": 128 * scale, "scale": scale, "slots": slots_json}
	if folder.endswith("heads"):
		meta["fullDetail"] = False   # the heads ship a .TPC bank only
	json.dump(meta, open(os.path.join(base, "set.json"), "w"), indent=1)


def main():
	o = parse()
	edition = o["edition"]
	ed = K.EDITIONS[edition]
	S = o["scale"]
	K.SUPERSAMPLE = o["supersample"] or (6 if S == 1 else 4)
	sets = o["sets"] or K.sets_of(edition)
	tmp = os.path.join(o["out"], f"tmp-{os.getpid()}.png")
	os.makedirs(o["out"], exist_ok=True)
	if o["emit_anchors"]:
		canvas, origin = LAND_CANVAS[edition]
		st = K.setup_scene(canvas, origin, edition, scale=S)
		anchors = emit_anchors(st, [name for name in sets if K.PRESETS[name]["edition"] == edition and LAYOUT[name][0] == "Kids"], edition, S)
		os.makedirs(os.path.join(o["out"], edition.lower()), exist_ok=True)
		with open(os.path.join(o["out"], edition.lower(), "anchors.json"), "w") as handle:
			json.dump(anchors, handle, indent=1)
			handle.write("\n")
		print("anchors", len(anchors["sets"]), "sets", flush=True)
	if o["emit_winter_anchors"]:
		canvas, origin = LAND_CANVAS[edition]
		st = K.setup_scene(canvas, origin, edition, scale=S)
		winter = emit_winter_anchors(st, [name for name in sets if K.PRESETS[name]["edition"] == edition and LAYOUT[name][0] == "Kids"], edition, S)
		os.makedirs(os.path.join(o["out"], edition.lower()), exist_ok=True)
		with open(os.path.join(o["out"], edition.lower(), "winter-anchors.json"), "w") as handle:
			handle.write(dump_compact(winter) + "\n")
		print("winter anchors", len(winter["sets"]), "sets", flush=True)
	for name in sets:
		folder, heads_folder, table = LAYOUT[name]
		if "land" in o["part"] or "swim" in o["part"]:
			frames, meta = {}, {}
			if "land" in o["part"]:
				canvas, origin = LAND_CANVAS[edition]
				st = K.setup_scene(canvas, origin, edition, scale=S)
				sel = [(s, n, c) for s, n, c in table if not o["slots"] or n in o["slots"]]
				frames.update(render_land(st, name, sel, o["outfit"], tmp))
				for s, n, c in sel:
					meta[s] = {"name": n, "source": "land", "directions": DIRECTIONS, "framesPerDirection": c, "_origin": ((origin[0]) * S, (origin[1] + LAND_HOTSPOT_DROP) * S)}
			if "swim" in o["part"]:
				canvas, origin = ed["swim_canvas"], ed["swim_origin"]
				st = K.setup_scene(canvas, origin, edition, scale=S)
				frames.update(render_swim(st, name, tmp))
				for key, (slot, n, c) in SWIM_SLOTS.items():
					meta[slot] = {"name": n, "source": "swim", "directions": DIRECTIONS, "framesPerDirection": c, "_origin": (origin[0] * S, origin[1] * S)}
			write_set(os.path.join(o["out"], edition.lower(), folder, name), name, edition, folder, f"SPR_{name}", frames, meta, o["quantise"], o["dither"], S)
		if "pool" in o["part"]:
			frames, meta = {}, {}
			feet_canvas, feet_origin = LAND_CANVAS[edition]
			st = K.setup_scene(feet_canvas, feet_origin, edition, scale=S)
			frames.update(render_pool_feet(st, name, tmp))
			torso_canvas, torso_origin = TORSO_CANVAS[edition]
			st = K.setup_scene(torso_canvas, torso_origin, edition, scale=S)
			frames.update(render_pool_torso(st, name, tmp))
			for slot, slot_name, n, kind, hot in PP.POOL_SLOTS:
				origin = (feet_origin[0] * S, (feet_origin[1] + LAND_HOTSPOT_DROP) * S) if hot == "feet" else (torso_origin[0] * S, torso_origin[1] * S)
				meta[slot] = {"name": slot_name, "source": "pool", "directions": DIRECTIONS, "framesPerDirection": n, "_origin": origin, "hotspot": hot}
			write_set(os.path.join(o["out"], edition.lower(), "Pool", name), name, edition, folder, f"SPR_{name}", frames, meta, o["quantise"], o["dither"], S, "Swim")
			print("pool", name, flush=True)
		if "heads" in o["part"]:
			canvas, origin = HEAD_CANVAS[edition]
			st = K.setup_scene(canvas, origin, edition, scale=S)
			st.cam.data.ortho_scale = max(canvas) / (HEAD_PPU[edition] / HEAD_SCALE * HEAD_FIT * K.PRESETS[name].get("head_fit", 1.0))
			frames = render_heads(st, name, tmp)
			meta = {0: {"name": "head", "source": "heads", "directions": 7, "framesPerDirection": 8, "_origin": (origin[0] * S, (origin[1] + HEAD_HOTSPOT_DROP[edition]) * S)}}
			write_set(os.path.join(o["out"], edition.lower(), heads_folder, name), name, edition, heads_folder, HEAD_MEMBER.get(name, f"SPR_{name}"), frames, meta, o["quantise"], o["dither"], S)
		print("done", name, flush=True)
	if os.path.exists(tmp):
		os.remove(tmp)


if __name__ == "__main__":
	main()
