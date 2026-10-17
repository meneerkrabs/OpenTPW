"""Renders swimming kid sprites for a swimming-pool mod, for all eight original kid sprite sets.

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_swim_sprites.py -- --out /tmp/swimart [--edition TPW|TPI] \
      [--sets BE,CH] [--binary-alpha] [--overview-dir /path/to/reports]

Per set it writes ``<out>/<edition>/<SET>/<slot>_d<direction>_f<frame>.png`` (RGBA; 64 x 48 for the TPW sets, 80 x 64 for
the TPI sets, which are rendered larger), where the slot is
  A  front crawl, 8 frames,   B  breaststroke, 8 frames,   C  treading water / idle, 4 frames,
and 5 directions as in the original kid sprites (0 swims away from the camera, 2 faces screen right, 4 faces the camera;
1 and 3 are the diagonals; directions 5 to 7 are the mirror images the game draws itself). The game stores frames
direction-major per slot, so the sprite index of a frame is ``direction * frames + frame``.

The sprite origin (the hotspot) is the edition's ``swim_origin`` pixel of every frame (TPW (32, 27), TPI (40, 36)): the centre of the swimmer's torso, at
the water line. Everything below the water line is tinted blue and partly transparent (the sprite is drawn over the
pool's water); ``--binary-alpha`` makes every pixel fully opaque or fully transparent instead, for palette sprite sets.
It also writes a contact sheet and an animated GIF per set (on a flat pool-blue background, for looking at), and with
``--overview-dir`` the combined overview GIF and contact sheet of all sets.
"""
import math
import os
import subprocess
import sys

import bpy
import bmesh
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_kid as K  # noqa: E402

CANVAS = (64, 48)        # set from the edition in main()
POOL_BACKGROUND = (66, 158, 214)
SLOTS = {"A": ("crawl", 8), "B": ("breaststroke", 8), "C": ("tread", 4)}
DIRECTIONS = 5
FFMPEG = "/opt/homebrew/bin/ffmpeg"


# ---- Poses -------------------------------------------------------------------------------------------------------

def _smooth(t):
	return 0.5 - 0.5 * math.cos(math.pi * t)


def _interp(keys, phase):
	"""Cyclic cosine interpolation between key poses [(phase, {name: value})]; values are numbers."""
	phase %= 1.0
	keys = sorted(keys, key=lambda k: k[0])
	for i, (p0, v0) in enumerate(keys):
		p1, v1 = keys[(i + 1) % len(keys)]
		if i + 1 == len(keys):
			p1 += 1.0
		if p0 <= phase < p1 or (i + 1 == len(keys)):
			t = _smooth((phase - p0) / (p1 - p0))
			return {k: v0[k] + (v1[k] - v0[k]) * t for k in v0}


def crawl_pose(f, n=8):
	ph = f / n
	tau = 2 * math.pi
	pose = {"root": (0, 0, -0.1 + 0.05 * math.sin(tau * 2 * ph)), "pitch": -86, "roll": 26 * math.sin(tau * ph),
		"head_yaw": -55 * max(0.0, math.sin(tau * (ph + 0.12))), "head_pitch": 18, "arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	for s in (-1, 1):
		psi = (ph + (0.0 if s < 0 else 0.5)) % 1.0
		swing = 180 - 360 * psi
		recover = psi >= 0.5
		abd = 10 + (26 * math.sin(math.pi * (psi - 0.5) / 0.5) if recover else 0)
		flex = (-8 - 22 * math.sin(math.pi * psi / 0.5)) if not recover else (-12 - 70 * math.sin(math.pi * (psi - 0.5) / 0.5))
		pose["arm"][s] = (swing, abd)
		pose["elbow"][s] = flex
		kick = tau * (2 * ph + (0.0 if s < 0 else 0.5))
		pose["leg"][s] = (24 * math.sin(kick), 3)
		pose["knee"][s] = 14 + 14 * math.sin(kick - 0.9)
		pose["ankle"][s] = -75
	return pose


# Breaststroke key poses: glide, out-sweep, pull, hands to chest, draw the legs up, kick, glide. Arms are (swing,
# abduction, elbow), legs (swing, abduction, knee).
_BREAST = [
	(0.000, dict(a=172, ab=6, e=0, l=0, lab=4, k=0, hp=22, z=-0.12, pitch=-87)),
	(0.125, dict(a=150, ab=40, e=-6, l=0, lab=4, k=6, hp=32, z=-0.1, pitch=-85)),
	(0.250, dict(a=118, ab=64, e=-26, l=8, lab=6, k=22, hp=55, z=0.0, pitch=-78)),
	(0.375, dict(a=96, ab=40, e=-72, l=30, lab=14, k=70, hp=58, z=0.02, pitch=-78)),
	(0.500, dict(a=130, ab=14, e=-92, l=48, lab=30, k=100, hp=40, z=-0.06, pitch=-84)),
	(0.625, dict(a=158, ab=8, e=-45, l=30, lab=42, k=60, hp=30, z=-0.1, pitch=-86)),
	(0.750, dict(a=172, ab=6, e=-8, l=6, lab=22, k=14, hp=24, z=-0.12, pitch=-88)),
	(0.875, dict(a=176, ab=5, e=0, l=0, lab=6, k=2, hp=22, z=-0.12, pitch=-88)),
]


def breast_pose(f, n=8):
	v = _interp(_BREAST, f / n)
	pose = {"root": (0, 0, v["z"]), "pitch": v["pitch"], "head_pitch": v["hp"], "arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	for s in (-1, 1):
		pose["arm"][s] = (v["a"], v["ab"])
		pose["elbow"][s] = v["e"]
		pose["leg"][s] = (v["l"], v["lab"])
		pose["knee"][s] = v["k"]
		pose["ankle"][s] = -70
	return pose


def tread_pose(f, n=4):
	ph = f / n
	tau = 2 * math.pi
	pose = {"root": (0, 0, -0.12 + 0.05 * math.sin(tau * ph)), "pitch": -4, "head_pitch": 2, "head_yaw": 8 * math.sin(tau * ph),
		"arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	for s in (-1, 1):
		sc = tau * (ph + (0.0 if s < 0 else 0.5))
		pose["arm"][s] = (28 * math.sin(sc), 66 + 6 * math.cos(sc))
		pose["elbow"][s] = -34 - 16 * math.cos(sc)
		pose["leg"][s] = (32 * math.sin(sc + 1.2), 12)
		pose["knee"][s] = 48 + 30 * math.sin(sc)
		pose["ankle"][s] = 0
	return pose


POSES = {"A": crawl_pose, "B": breast_pose, "C": tread_pose}


# ---- Water effects -----------------------------------------------------------------------------------------------

def make_ring(name, material):
	bm = bmesh.new()
	segs = 40
	for i in range(segs):
		a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
		pts = [(math.cos(a0) * 0.88, math.sin(a0) * 0.88), (math.cos(a0), math.sin(a0)), (math.cos(a1), math.sin(a1)), (math.cos(a1) * 0.88, math.sin(a1) * 0.88)]
		bm.faces.new([bm.verts.new((x, y, 0.0)) for x, y in pts])
	me = bpy.data.meshes.new(name)
	bm.to_mesh(me)
	bm.free()
	o = bpy.data.objects.new(name, me)
	me.materials.append(material)
	bpy.context.scene.collection.objects.link(o)
	return o


def place_rings(rings, slot, f, size=1.0):
	"""Foam rings around the body where it crosses the surface; they pulse a little over the cycle."""
	pulse = 1.0 + 0.04 * math.sin(2 * math.pi * f / SLOTS[slot][1])
	inner, outer = rings
	pulse *= size
	if slot == "C":
		inner.scale, outer.scale = (0.95 * pulse, 0.8 * pulse, 1), (1.35 * pulse, 1.1 * pulse, 1)
		inner.location = outer.location = (0, 0, 0.02)
	else:
		inner.scale, outer.scale = (0.78 * pulse, 1.45 * pulse, 1), (1.05 * pulse, 1.95 * pulse, 1)
		inner.location, outer.location = (0, 0.15, 0.02), (0, 0.15, 0.02)


def end_heights(kid, pose_fn, f, n):
	"""World heights of the hands and feet at frame f-1, f, f+1 of the cycle (for splash timing)."""
	out = {}
	for df in (-1, 0, 1):
		K.apply_pose(kid, pose_fn((f + df) % n, n))
		for name, e in kid.ends.items():
			out.setdefault(name, []).append(e.matrix_world.translation.copy())
	return out


def spawn_droplets(kid, pose_fn, f, n, material, slot_seed):
	"""A few droplets where a hand or foot leaves or enters the water on this frame."""
	heights = end_heights(kid, pose_fn, f, n)
	drops = []
	for ni, (name, (p0, p1, p2)) in enumerate(sorted(heights.items())):
		crossing = (p0.z < 0.05) != (p2.z < 0.05)
		if not crossing or abs(p1.z) > 0.6:
			continue
		for k in range(4):
			ox = ((k * 3 + ni + slot_seed) % 5 - 2) * 0.13
			oy = ((k * 5 + ni * 2 + slot_seed) % 5 - 2) * 0.11
			oz = 0.22 + 0.2 * ((k * 7 + ni) % 4)
			c = (p1.x + ox, p1.y + oy, max(0.1, p1.z) + oz)
			bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.085 - 0.012 * (k % 2), location=c)
			o = bpy.context.active_object
			o.data.materials.append(material)
			drops.append(o)
	return drops


# ---- Output helpers ----------------------------------------------------------------------------------------------

def on_pool(a, scale=1):
	bg = np.array(POOL_BACKGROUND, np.float32) / 255.0
	rgb = a[..., :3] * a[..., 3:4] + bg * (1 - a[..., 3:4])
	if scale > 1:
		rgb = np.kron(rgb, np.ones((scale, scale, 1), np.float32))
	return np.dstack([rgb, np.ones(rgb.shape[:2], np.float32)])


def grid(cells, cols, scale):
	"""Arranges equally sized RGBA frames (row-major) in a grid on the pool background."""
	h, w = CANVAS[1] * scale, CANVAS[0] * scale
	rows = (len(cells) + cols - 1) // cols
	out = np.zeros((rows * h, cols * w, 4), np.float32)
	out[...] = np.append(np.array(POOL_BACKGROUND, np.float32) / 255.0, 1.0)
	for i, c in enumerate(cells):
		r, cc = divmod(i, cols)
		out[r * h:(r + 1) * h, cc * w:(cc + 1) * w] = on_pool(c, scale) if c is not None else out[r * h:(r + 1) * h, cc * w:(cc + 1) * w]
	return out


def make_gif(frames, path, fps, workdir):
	os.makedirs(workdir, exist_ok=True)
	for i, fr in enumerate(frames):
		K.write_png(fr, os.path.join(workdir, f"g{i:03d}.png"))
	subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", os.path.join(workdir, "g%03d.png"),
		"-vf", "split[a][b];[a]palettegen=max_colors=255[p];[b][p]paletteuse", "-loop", "0", path], check=True)
	for i in range(len(frames)):
		os.remove(os.path.join(workdir, f"g{i:03d}.png"))


def parse():
	a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	o = dict(out="/tmp/swimart", sets=None, binary=False, overview=None, edition="TPW")
	i = 0
	while i < len(a):
		if a[i] == "--out": o["out"] = a[i + 1]; i += 2
		elif a[i] == "--sets": o["sets"] = a[i + 1].split(","); i += 2
		elif a[i] == "--edition": o["edition"] = a[i + 1]; i += 2
		elif a[i] == "--binary-alpha": o["binary"] = True; i += 1
		elif a[i] == "--overview-dir": o["overview"] = a[i + 1]; i += 2
		else: raise SystemExit(f"unknown argument {a[i]}")
	return o


def main():
	global CANVAS
	o = parse()
	ed = K.EDITIONS[o["edition"]]
	CANVAS = ed["swim_canvas"]
	all_sets = K.sets_of(o["edition"])
	full = o["sets"] is None
	o["sets"] = o["sets"] or all_sets
	tag = o["edition"].lower()
	os.makedirs(os.path.join(o["out"], tag), exist_ok=True)
	st = K.setup_scene(CANVAS, ed["swim_origin"], o["edition"])
	m_foam = K._emissive("foam", (0.9, 0.97, 1.0, 1.0), 0.75)
	m_foam2 = K._emissive("foam2", (0.8, 0.94, 1.0, 1.0), 0.4)
	m_drop = K._emissive("drop", (0.86, 0.95, 1.0, 1.0), 0.9)
	rings = (make_ring("ringInner", m_foam), make_ring("ringOuter", m_foam2))
	frames = {}   # (set, slot, direction, frame) -> array
	tmp = os.path.join(o["out"], "tmp.png")
	for name in o["sets"]:
		kid = K.build_kid(name, underwater=True)
		sd = os.path.join(o["out"], tag, name)
		os.makedirs(sd, exist_ok=True)
		for slot, (anim, n) in SLOTS.items():
			pose_fn = POSES[slot]
			for f in range(n):
				drops = spawn_droplets(kid, pose_fn, f, n, m_drop, ord(slot))
				K.apply_pose(kid, pose_fn(f, n))
				place_rings(rings, slot, f, kid.size)
				for d in range(DIRECTIONS):
					K.aim_camera(st, d)
					a = K.render_frame(st, tmp, binary_alpha=o["binary"])
					frames[(name, slot, d, f)] = a
					K.write_png(a, os.path.join(sd, f"{slot}_d{d}_f{f}.png"))
				for dr in drops:
					bpy.data.objects.remove(dr, do_unlink=True)
		K.clear_kid(kid)
		# Contact sheet: three blocks (slots), each 5 direction rows by up to 8 frame columns.
		cells = []
		for slot, (anim, n) in SLOTS.items():
			for d in range(DIRECTIONS):
				cells += [frames[(name, slot, d, f)] if f < n else None for f in range(8)]
		K.write_png(grid(cells, 8, 2), os.path.join(o["out"], tag, f"{name}-sheet.png"))
		# Animated GIF: one column per direction, one row per slot; every slot loops over 8 ticks.
		gif = []
		for t in range(8):
			cs = [frames[(name, slot, d, t % n)] for slot, (anim, n) in SLOTS.items() for d in range(DIRECTIONS)]
			gif.append(grid(cs, DIRECTIONS, 3))
		make_gif(gif, os.path.join(o["out"], tag, f"{name}-swim.gif"), 8, os.path.join(o["out"], "gifwork"))
		print("done", name, flush=True)
	if full:
		overview = []
		for t in range(8):
			cs = []
			for slot in ("A", "B"):
				for d in (2, 4):
					n = SLOTS[slot][1]
					cs += [frames[(name, slot, d, t % n)] for name in all_sets]
			overview.append(grid(cs, 8, 3))
		# Combined contact sheet: per set one row, direction 2 then direction 4, all slots left to right.
		cells = []
		for d in (2, 4):
			for name in all_sets:
				for slot, (anim, n) in SLOTS.items():
					cells += [frames[(name, slot, d, f)] for f in range(n)]
		sheet = grid(cells, 20, 2)
		K.write_png(sheet, os.path.join(o["out"], f"all-sets-{tag}-sheet.png"))
		make_gif(overview, os.path.join(o["out"], f"all-sets-{tag}-swim.gif"), 8, os.path.join(o["out"], "gifwork"))
		if o["overview"]:
			os.makedirs(o["overview"], exist_ok=True)
			import shutil
			stem = "all-sets" if o["edition"] == "TPW" else "tpi-sets"
			shutil.copy(os.path.join(o["out"], f"all-sets-{tag}-swim.gif"), os.path.join(o["overview"], f"swimmers-{stem}.gif"))
			shutil.copy(os.path.join(o["out"], f"all-sets-{tag}-sheet.png"), os.path.join(o["overview"], f"swimmers-{stem}-sheet.png"))
	if os.path.exists(tmp):
		os.remove(tmp)


main()
