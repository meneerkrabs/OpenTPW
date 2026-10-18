"""Renders the parametric kid standing, for every set, in the original's sprite size and camera, and compares it with a
reference sheet of the original standing frames (viewed locally, never committed).

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_kid_calibrate.py -- --out /tmp/swimart [--reference all_stand.png] [--sets BE,CH] [--edition TPW|TPI] [--copy-to reports/swimmers-calibration-tpw.png] [--no-quantise] [--dither 0.03] [--outfit clothes|swim]

The reference is an enlarged sheet on a flat background (``--zoom``, default 4; TPI sheets are 3x): the edition's sets as
columns (TPW: BE, BI, CH, FR, KI, SA, SU, TA; TPI: BD, GB, GK, KB, FA, MO, GM, GR), rows = directions 0..4, each cell the
edition's sprite cell (TPW 48x56, TPI 64x80). Ours are quantised to one 255-colour palette per set (the way a sprite bank
stores them) unless ``--no-quantise`` is given, so the comparison shows what the game would draw. Without ``--reference`` only our own sheet is written. The pixel heights it prints are
the calibration numbers recorded in docs/CONTENT-KIT.md.
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_kid as K  # noqa: E402

BACKGROUND = (120, 170, 110)


def args():
	a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	o = dict(out="/tmp/swimart", reference=None, sets=None, edition="TPW", zoom=4, copy_to=None, quantise=True, dither=None, outfit="clothes")
	i = 0
	while i < len(a):
		if a[i] == "--out": o["out"] = a[i + 1]; i += 2
		elif a[i] == "--reference": o["reference"] = a[i + 1]; i += 2
		elif a[i] == "--sets": o["sets"] = a[i + 1].split(","); i += 2
		elif a[i] == "--edition": o["edition"] = a[i + 1]; i += 2
		elif a[i] == "--zoom": o["zoom"] = int(a[i + 1]); i += 2
		elif a[i] == "--copy-to": o["copy_to"] = a[i + 1]; i += 2
		elif a[i] == "--no-quantise": o["quantise"] = False; i += 1
		elif a[i] == "--outfit": o["outfit"] = a[i + 1]; i += 2
		elif a[i] == "--dither": o["dither"] = float(a[i + 1]); i += 2
		else: raise SystemExit(f"unknown argument {a[i]}")
	return o


def compose(a, bg=BACKGROUND):
	bgc = np.array(bg, dtype=np.float32) / 255.0
	return a[..., :3] * a[..., 3:4] + bgc * (1 - a[..., 3:4])


def bbox(a):
	ys, xs = np.where(a[..., 3] >= 0.5)
	return ys.min(), ys.max(), xs.min(), xs.max()


def main():
	o = args()
	o["sets"] = o["sets"] or K.sets_of(o["edition"])
	ed = K.EDITIONS[o["edition"]]
	CELL, FEET, ZOOM = ed["cell"], ed["feet"], o["zoom"]
	os.makedirs(o["out"], exist_ok=True)
	st = K.setup_scene(CELL, FEET, o["edition"])   # the reference sheet is made from the TPC frames the game draws
	ours = {}
	for name in o["sets"]:
		kid = K.build_kid(name, underwater=False, outfit=o["outfit"])
		K.apply_pose(kid, K.standing_pose(kid))
		for d in range(5):
			K.aim_camera(st, d)
			ours[(name, d)] = K.render_frame(st, os.path.join(o["out"], f"tmp-{os.getpid()}.png"), binary_alpha=True)
		if o["quantise"]:
			q, _ = K.quantise_set([ours[(name, d)] for d in range(5)], dither=o["dither"])
			for d in range(5):
				ours[(name, d)] = q[d]
		K.clear_kid(kid)
	cols = len(o["sets"])
	sheet = np.zeros((5 * CELL[1], cols * CELL[0], 3), np.float32)
	for c, name in enumerate(o["sets"]):
		for d in range(5):
			sheet[d * CELL[1]:(d + 1) * CELL[1], c * CELL[0]:(c + 1) * CELL[0]] = compose(ours[(name, d)])
	big = np.kron(sheet, np.ones((ZOOM, ZOOM, 1), np.float32))
	print("height (rows incl. outline) of direction 4 / direction 2 / direction 0, ours:")
	for name in o["sets"]:
		print(name, *(f"d{d}={bbox(ours[(name, d)])[1] - bbox(ours[(name, d)])[0] + 1}" for d in (4, 2, 0)),
			f"width d4={bbox(ours[(name, 4)])[3] - bbox(ours[(name, 4)])[2] + 1}")
	out = big
	if o["reference"]:
		import bpy
		img = bpy.data.images.load(o["reference"])
		w, h = img.size
		ref = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3]
		bgc = ref[2, 2]
		mask = np.abs(ref - bgc).sum(2) > 0.08
		cw, ch = w // 8, h // 5
		ref = ref[:, :cols * cw]
		gap = np.tile(np.array([0.05, 0.05, 0.05], np.float32), (big.shape[0], 8, 1))
		out = np.concatenate([ref[:big.shape[0]], gap, big], axis=1)
		print("height (rows) of the original, direction 4 / 2 / 0:")
		for c, name in enumerate(o["sets"]):
			hs = []
			for d in (4, 2, 0):
				m = mask[d * ch:(d + 1) * ch, c * cw:(c + 1) * cw]
				ys, xs = np.where(m)
				hs.append(f"d{d}={(ys.max() - ys.min() + 1) // ZOOM}")
			print(name, *hs)
	rgba = np.dstack([out, np.ones(out.shape[:2], np.float32)])
	path = os.path.join(o["out"], f"compare-stand-{o['edition'].lower()}.png")
	K.write_png(rgba, path)
	print("wrote", path)
	tmp = os.path.join(o["out"], f"tmp-{os.getpid()}.png")
	if os.path.exists(tmp):
		os.remove(tmp)
	if o["copy_to"]:
		import shutil
		shutil.copy(path, o["copy_to"])


main()
