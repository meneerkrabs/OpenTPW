"""Renders complete replacement guest sprite sets (every animation slot, plus the swim slots and the rider head sets) from the
parametric people of ``opentpw_kid.py``, ready to be packed into ``.ESP``/``.TPC``/``.FPC`` files (``SpriteSetBuilder``).

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_guest_sprites.py -- --out /tmp/swimart/guests \
      [--edition TPW|TPI] [--sets BE,CH] [--part land,swim,heads] [--slots walk,run] [--outfit clothes|swim] [--no-quantise]

Output, per set: ``<out>/<edition>/<Folder>/<SET>/slot<NN>_d<d>_f<f>.png`` (RGBA, trimmed to the frame's content, the whole set
quantised to one 255-colour palette) and ``set.json`` describing the set for the packer:

  {"name": "SPR_BE", "edition": "TPW", "folder": "Generic/Kids", "fullDetail": true, "kidFlag": 0, "colours": 255,
   "slots": [{"slot": 1, "name": "walk", "source": "land", "directions": 5, "framesPerDirection": 8,
              "frames": [[{"file": "slot01_d0_f0.png", "originX": -12, "originY": -37, "width": 22, "height": 38}, ...], ...]}]}

``originX``/``originY`` follow the sprite reader's convention (``SpriteFrame.OriginX/Y``): the offset of the frame's top left pixel
from the hotspot, so ``(-12, -37)`` means the frame starts 12 pixels left of and 37 above the hotspot. Standing sprites have the
hotspot between the feet on the floor, swimmers the torso centre at the water line, heads the centre of the skull. Frames are
listed direction-major, as the bank stores them. ``fullDetail`` is the ESP byte 0x10C (the game opens the ``.FPC`` bank).

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

DIRECTIONS = 5
LAND_HOTSPOT_DROP = 1      # our soles sit 1 to 2 pixels lower than the originals' (+1..+2 below the hotspot), so the hotspot is 1 pixel lower
LAND_CANVAS = {"TPW": ((100, 90), (50, 78)), "TPI": ((140, 130), (70, 112))}
HEAD_CANVAS = {"TPW": ((64, 64), (32, 32)), "TPI": ((80, 80), (40, 40))}
HEAD_FIT = 1.0             # our heads render a little smaller than the geometry says so their frames match the originals
HEAD_HOTSPOT_DROP = {"TPW": 0, "TPI": 6}   # the shipped TPI heads put the hotspot about 9 px below the head centre (TPW: about 3, which ours already has; measured against ours the net shift is 6)
HEAD_SCALE = 0.685          # the game draws a head sprite at 0.685 of the body's pixel scale (docs/GUESTS.md)
SWIM_SLOTS = {"A": (8, "swim-crawl", 8), "B": (9, "swim-breaststroke", 8), "C": (10, "swim-tread", 4)}

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
	o = dict(out="/tmp/swimart/guests", edition="TPW", sets=None, part="land,swim,heads", slots=None, outfit="clothes", quantise=True, dither=None)
	i = 0
	while i < len(a):
		k = a[i]
		if k == "--no-quantise": o["quantise"] = False; i += 1
		elif k in ("--out", "--edition", "--sets", "--part", "--slots", "--outfit", "--dither"):
			v = a[i + 1]
			o[k[2:]] = v.split(",") if k in ("--sets", "--part", "--slots") else (float(v) if k == "--dither" else v)
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


def write_set(base, name, edition, folder, member, frames, slot_meta, quantise, dither, kid_flag):
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
		slots_json.append({"slot": slot, "name": m["name"], "source": m["source"], "directions": m["directions"], "framesPerDirection": m["framesPerDirection"], "frames": rows})
	meta = {"name": member, "set": name, "edition": edition, "folder": f"Generic/{folder}", "fullDetail": True, "kidFlag": 1 if K.PRESETS[name]["girl"] and folder == "Kids" else 0,
		"colours": K.QUANTISE_COLOURS if quantise else 0, "slots": slots_json}
	if folder.endswith("heads"):
		meta["fullDetail"] = False   # the heads ship a .TPC bank only
	json.dump(meta, open(os.path.join(base, "set.json"), "w"), indent=1)


def main():
	o = parse()
	edition = o["edition"]
	ed = K.EDITIONS[edition]
	sets = o["sets"] or K.sets_of(edition)
	tmp = os.path.join(o["out"], f"tmp-{os.getpid()}.png")
	os.makedirs(o["out"], exist_ok=True)
	for name in sets:
		folder, heads_folder, table = LAYOUT[name]
		if "land" in o["part"] or "swim" in o["part"]:
			frames, meta = {}, {}
			if "land" in o["part"]:
				canvas, origin = LAND_CANVAS[edition]
				st = K.setup_scene(canvas, origin, edition)
				sel = [(s, n, c) for s, n, c in table if not o["slots"] or n in o["slots"]]
				frames.update(render_land(st, name, sel, o["outfit"], tmp))
				for s, n, c in sel:
					meta[s] = {"name": n, "source": "land", "directions": DIRECTIONS, "framesPerDirection": c, "_origin": (origin[0], origin[1] + LAND_HOTSPOT_DROP)}
			if "swim" in o["part"]:
				canvas, origin = ed["swim_canvas"], ed["swim_origin"]
				st = K.setup_scene(canvas, origin, edition)
				frames.update(render_swim(st, name, tmp))
				for key, (slot, n, c) in SWIM_SLOTS.items():
					meta[slot] = {"name": n, "source": "swim", "directions": DIRECTIONS, "framesPerDirection": c, "_origin": origin}
			write_set(os.path.join(o["out"], edition.lower(), folder, name), name, edition, folder, f"SPR_{name}", frames, meta, o["quantise"], o["dither"], 0)
		if "heads" in o["part"]:
			canvas, origin = HEAD_CANVAS[edition]
			st = K.setup_scene(canvas, origin, edition)
			st.cam.data.ortho_scale = max(canvas) / (ed["ppu"] / HEAD_SCALE * HEAD_FIT * K.PRESETS[name].get("head_fit", 1.0))
			frames = render_heads(st, name, tmp)
			meta = {0: {"name": "head", "source": "heads", "directions": 7, "framesPerDirection": 8, "_origin": (origin[0], origin[1] + HEAD_HOTSPOT_DROP[edition])}}
			write_set(os.path.join(o["out"], edition.lower(), heads_folder, name), name, edition, heads_folder, HEAD_MEMBER.get(name, f"SPR_{name}"), frames, meta, o["quantise"], o["dither"], 0)
		print("done", name, flush=True)
	if os.path.exists(tmp):
		os.remove(tmp)


if __name__ == "__main__":
	main()
