"""Renders swimming kid sprites for a swimming-pool mod, for all eight original kid sprite sets.

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_swim_sprites.py -- --out /tmp/swimart [--edition TPW|TPI] \
      [--sets BE,CH] [--binary-alpha] [--overview-dir /path/to/reports] [--no-quantise] [--dither 0.03] [--verify]

Per set it writes ``<out>/<edition>/<SET>/<slot>_d<direction>_f<frame>.png`` (RGBA; 64 x 48 for the TPW sets, 80 x 64 for
the TPI sets, which are rendered larger), where the slot is
  A  front crawl, 8 frames,   B  breaststroke, 8 frames,   C  treading water / idle, 4 frames,
and 5 directions as in the original kid sprites (0 swims away from the camera, 2 faces screen right, 4 faces the camera;
1 and 3 are the diagonals; directions 5 to 7 are the mirror images the game draws itself). The game stores frames
direction-major per slot, so the sprite index of a frame is ``direction * frames + frame``.

The sprite origin (the hotspot) is the edition's ``swim_origin`` pixel of every frame (TPW (32, 27), TPI (40, 36)): the centre of the swimmer's torso, at
the water line. Everything below the water line is tinted blue and partly transparent (the sprite is drawn over the
pool's water); ``--binary-alpha`` makes every pixel fully opaque or fully transparent instead, for palette sprite sets. The frames of a
set are quantised to one shared 255-colour palette (weighted median cut over RGBA, like SpriteSetBuilder, optionally with
an ordered ``--dither``), as a sprite bank stores them, and that is what the sheets and GIFs show; the unquantised
frames are kept in ``<SET>/truecolor/`` (``--no-quantise`` writes only those). ``--verify`` checks the crawl's roll and
head turn numerically and the back-view visibility of the arms (see ``verify_crawl`` and ``visibility_check``).
It also writes a contact sheet and an animated GIF per set (on a flat pool-blue background, for looking at), and with
``--overview-dir`` the combined overview GIF and contact sheet of all sets.
"""
import math
import os
import subprocess
import sys

import bpy
import bmesh
import mathutils
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
	pose = {"root": (0, 0, 0.2 + 0.06 * math.sin(tau * 2 * ph)), "pitch": -78, "roll": -44 * math.sin(tau * ph),
		"head_yaw": -55 * max(0.0, math.sin(tau * (ph + 0.12))), "head_pitch": 18, "arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	for s in (-1, 1):
		psi = (ph + (0.0 if s < 0 else 0.5)) % 1.0
		swing = 180 - 360 * psi
		recover = psi >= 0.5
		abd = 10 + (66 * math.sin(math.pi * (psi - 0.5) / 0.5) if recover else 0)
		flex = (-8 - 30 * math.sin(math.pi * psi / 0.5)) if not recover else (-14 - 80 * math.sin(math.pi * (psi - 0.5) / 0.5))
		pose["arm"][s] = (swing, abd)
		pose["elbow"][s] = flex
		kick = tau * (2 * ph + (0.0 if s < 0 else 0.5))
		pose["leg"][s] = (-6 + 36 * math.sin(kick), 4)
		pose["knee"][s] = 18 + 18 * math.sin(kick - 0.9)
		pose["ankle"][s] = -75
	return pose


# Breaststroke key poses: glide, out-sweep, pull, hands to chest, draw the legs up, kick, glide. Arms are (swing,
# abduction, elbow), legs (swing, abduction, knee).
_BREAST = [
	(0.000, dict(a=172, ab=6, e=0, l=0, lab=4, k=0, hp=22, z=-0.02, pitch=-87)),
	(0.125, dict(a=150, ab=40, e=-6, l=0, lab=4, k=6, hp=32, z=0.00, pitch=-85)),
	(0.250, dict(a=118, ab=64, e=-26, l=8, lab=6, k=22, hp=55, z=0.10, pitch=-78)),
	(0.375, dict(a=96, ab=40, e=-72, l=30, lab=14, k=70, hp=58, z=0.12, pitch=-78)),
	(0.500, dict(a=130, ab=14, e=-92, l=48, lab=30, k=100, hp=40, z=0.04, pitch=-84)),
	(0.625, dict(a=158, ab=8, e=-45, l=30, lab=42, k=60, hp=30, z=0.00, pitch=-86)),
	(0.750, dict(a=172, ab=6, e=-8, l=6, lab=22, k=14, hp=24, z=-0.02, pitch=-88)),
	(0.875, dict(a=176, ab=5, e=0, l=0, lab=6, k=2, hp=22, z=-0.02, pitch=-88)),
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
	pose = {"root": (0, 0, -0.04 + 0.05 * math.sin(tau * ph)), "pitch": -4, "head_pitch": 2, "head_yaw": 8 * math.sin(tau * ph),
		"arm": {}, "elbow": {}, "leg": {}, "knee": {}, "ankle": {}}
	for s in (-1, 1):
		sc = tau * (ph + (0.0 if s < 0 else 0.5))
		pose["arm"][s] = (34 * math.sin(sc), 62 + 10 * math.cos(sc))
		pose["elbow"][s] = -50 - 24 * math.cos(sc)
		pose["leg"][s] = (40 * math.sin(sc + 1.2), 14)
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
		speed = abs(p2.z - p0.z) / 2
		if not ((crossing and abs(p1.z) < 0.9) or (speed > 0.3 and -0.2 < p1.z < 0.8)):
			continue
		for k in range(7):
			ox = ((k * 3 + ni + slot_seed) % 5 - 2) * 0.17
			oy = ((k * 5 + ni * 2 + slot_seed) % 5 - 2) * 0.15
			oz = 0.25 + 0.22 * ((k * 7 + ni) % 4)
			c = (p1.x + ox, p1.y + oy, max(0.1, p1.z) + oz)
			bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.12 - 0.02 * (k % 3), location=c)
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
	o = dict(out="/tmp/swimart", sets=None, binary=False, overview=None, edition="TPW", quantise=True, dither=None, verify=False)
	i = 0
	while i < len(a):
		if a[i] == "--out": o["out"] = a[i + 1]; i += 2
		elif a[i] == "--sets": o["sets"] = a[i + 1].split(","); i += 2
		elif a[i] == "--edition": o["edition"] = a[i + 1]; i += 2
		elif a[i] == "--no-quantise": o["quantise"] = False; i += 1
		elif a[i] == "--dither": o["dither"] = float(a[i + 1]); i += 2
		elif a[i] == "--verify": o["verify"] = True; i += 1
		elif a[i] == "--binary-alpha": o["binary"] = True; i += 1
		elif a[i] == "--overview-dir": o["overview"] = a[i + 1]; i += 2
		else: raise SystemExit(f"unknown argument {a[i]}")
	return o


def verify_crawl(kid):
	"""Per frame of the crawl: the recovering arm must be on the side whose shoulder is rolled up, and the face must be turned towards that side when the head is turned. Returns (frames checked, roll
	ok, head ok)."""
	n = SLOTS["A"][1]
	checked = roll_ok = head_ok = 0
	for f in range(n):
		K.apply_pose(kid, crawl_pose(f, n))
		rec = 1 if f / n < 0.5 else -1      # the arm in its recovery half (see crawl_pose)
		checked += 1
		sz = {s: kid.pivots[f"shoulder{s}"].matrix_world.translation.z for s in (-1, 1)}
		roll_ok += sz[rec] >= sz[-rec] - 0.02          # equal at the frames where the roll passes through zero
		fwd = kid.pivots["head"].matrix_world.to_3x3() @ mathutils.Vector((0, 1, 0))
		turned = abs(fwd.x) > 0.25
		side = kid.pivots[f"shoulder{rec}"].matrix_world.translation.x
		head_ok += (not turned) or fwd.x * side > 0
	return checked, roll_ok, head_ok


def _mask_material(name, above, below):
	m = bpy.data.materials.new(name)
	m.use_nodes = True
	nt = m.node_tree
	for n in list(nt.nodes):
		nt.nodes.remove(n)
	out = nt.nodes.new("ShaderNodeOutputMaterial")
	em = nt.nodes.new("ShaderNodeEmission")
	geo = nt.nodes.new("ShaderNodeNewGeometry")
	sep = nt.nodes.new("ShaderNodeSeparateXYZ")
	gt = nt.nodes.new("ShaderNodeMath")
	gt.operation = "GREATER_THAN"
	gt.inputs[1].default_value = 0.02
	mix = nt.nodes.new("ShaderNodeMix")
	mix.data_type = "RGBA"
	mix.inputs[6].default_value = (*below, 1)
	mix.inputs[7].default_value = (*above, 1)
	nt.links.new(geo.outputs["Position"], sep.inputs[0])
	nt.links.new(sep.outputs["Z"], gt.inputs[0])
	nt.links.new(gt.outputs[0], mix.inputs[0])
	nt.links.new(mix.outputs[2], em.inputs["Color"])
	nt.links.new(em.outputs[0], out.inputs["Surface"])
	m.surface_render_method = "DITHERED"
	return m


def visibility_check(st, names, rings):
	"""Renders colour-coded masks (arms red, shoulder balls blue, the rest grey; bright when above the water) of the crawl
	from directions 0 and 1 and counts, per frame, the visible arm and shoulder pixels (at sprite size). The acceptance
	rule: arms or shoulders visible (arm >= 6 px or shoulder >= 4 px above the water) in at least half of the frames."""
	arm_m, sh_m, other = _mask_material("mArm", (1, 0, 0), (0.25, 0, 0)), _mask_material("mSh", (0, 0, 1), (0, 0, 0.25)), _mask_material("mOther", (0.2, 0.2, 0.2), (0.2, 0.2, 0.2))
	for r in rings:
		r.hide_render = True
	n = SLOTS["A"][1]
	results = {}
	tmp = f"/tmp/swimart-mask-{os.getpid()}.png"
	for name in names:
		kid = K.build_kid(name, underwater=False, swim=True)
		for o in kid.objects:
			if o.type == "MESH":
				o.data.materials.clear()
				kind = other
				if o.name.startswith(("upperArm", "foreArm", "hand")):
					kind = arm_m
				elif o.name.startswith("shoulderBall"):
					kind = sh_m
				o.data.materials.append(kind)
		for d in (0, 1):
			ok_arm = ok_any = 0
			arm_px = []
			for f in range(n):
				K.apply_pose(kid, crawl_pose(f, n))
				K.aim_camera(st, d)
				st.scene.render.filepath = tmp
				bpy.ops.render.render(write_still=True)
				hi = K._read_png(tmp)
				red = ((hi[..., 0] > 0.9) & (hi[..., 1] < 0.15) & (hi[..., 2] < 0.15) & (hi[..., 3] > 0.5)).sum() / K.SUPERSAMPLE ** 2
				blue = ((hi[..., 2] > 0.9) & (hi[..., 0] < 0.15) & (hi[..., 1] < 0.15) & (hi[..., 3] > 0.5)).sum() / K.SUPERSAMPLE ** 2
				arm_px.append(int(round(red)))
				ok_arm += red >= 6
				ok_any += (red >= 6) or (blue >= 4)
			results[(name, d)] = (ok_arm, ok_any, arm_px)
		K.clear_kid(kid)
	for r in rings:
		r.hide_render = False
	return results


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
	tmp = os.path.join(o["out"], f"tmp-{os.getpid()}.png")
	if o["verify"]:
		for name in o["sets"]:
			kid = K.build_kid(name, underwater=True, swim=True)
			c, r, h = verify_crawl(kid)
			print(f"verify {name}: recovering frames {c}, roll ok {r}/{c}, head turn ok {h}/{c}", flush=True)
			K.clear_kid(kid)
		for (name, d), (ok_arm, ok_any, px) in visibility_check(st, o["sets"], rings).items():
			print(f"visible {name} d{d}: arm >= 6 px in {ok_arm}/8 frames, arm or shoulder in {ok_any}/8 (arm px {px})", flush=True)
		return
	for name in o["sets"]:
		kid = K.build_kid(name, underwater=True, swim=True)
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
					if not o["quantise"]:
						K.write_png(a, os.path.join(sd, f"{slot}_d{d}_f{f}.png"))
				for dr in drops:
					bpy.data.objects.remove(dr, do_unlink=True)
		K.clear_kid(kid)
		if o["quantise"]:
			keys = sorted(k for k in frames if k[0] == name)
			q, _ = K.quantise_set([frames[k] for k in keys], dither=o["dither"])
			os.makedirs(os.path.join(sd, "truecolor"), exist_ok=True)
			for k, a in zip(keys, q):
				K.write_png(frames[k], os.path.join(sd, "truecolor", f"{k[1]}_d{k[2]}_f{k[3]}.png"))
				K.write_png(a, os.path.join(sd, f"{k[1]}_d{k[2]}_f{k[3]}.png"))
				frames[k] = a
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
	if full:   # crawl check: per set the crawl from the front (direction 4) and from the back (direction 0), 8 frames each
		cells = []
		for name in all_sets:
			cells += [frames[(name, "A", 4, f)] for f in range(8)] + [frames[(name, "A", 0, f)] for f in range(8)]
		K.write_png(grid(cells, 16, 2), os.path.join(o["out"], f"crawl-check-{tag}.png"))
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
