"""Measures the camera elevation and pixel scale an original guest sprite bank was rendered at: renders the parametric kid
standing over a sweep of elevations and scales and compares its silhouettes with the original standing frames.

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_kid_elevation.py -- --reference /tmp/kids-stand.json \
      [--bank TPC|FPC] [--sets BE,KI] [--elevations 25:70:5] [--scales 0.9:1.1:0.05] [--out /tmp/kid-elevation]

The reference is a local JSON export of the original standing frames (``"<SET>.<BANK>"``: five directions, each with ``width``,
``height``, ``hotspot`` [x, y] and ``rows`` of ``#`` for an opaque pixel and ``.`` for a clear one). It holds original art and is
never committed. The score of an elevation and scale is the mean intersection over union of our standing silhouette and the
original's, both placed by their hotspots, over the sets and the five directions; the best is printed with its per-direction
heights and widths. The silhouettes are drawn with the Workbench engine (only their outline counts), at ``ppu`` times the scale
factor. Measured on October 18, 2026 (TPW, eight sets): the TPC peaks at 44 to 48 degrees and ppu 11.0 to 11.2 (IoU 0.77), the FPC at
15 degrees or lower and ppu 11.7.
"""
import json
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import opentpw_kid as K  # noqa: E402

CANVAS = (64, 84)
ORIGIN = (32, 72)


def sweep(text):
	start, stop, step = (float(x) for x in text.split(":"))
	values = []
	value = start
	while value <= stop + 1e-6:
		values.append(round(value, 4))
		value += step
	return values


def args():
	a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
	o = dict(reference=None, bank="TPC", sets=None, elevations=sweep("25:70:5"), scales=sweep("0.9:1.1:0.05"), out="/tmp/kid-elevation")
	i = 0
	while i < len(a):
		if a[i] == "--reference": o["reference"] = a[i + 1]; i += 2
		elif a[i] == "--bank": o["bank"] = a[i + 1].upper(); i += 2
		elif a[i] == "--sets": o["sets"] = a[i + 1].split(","); i += 2
		elif a[i] == "--elevations": o["elevations"] = sweep(a[i + 1]); i += 2
		elif a[i] == "--scales": o["scales"] = sweep(a[i + 1]); i += 2
		elif a[i] == "--out": o["out"] = a[i + 1]; i += 2
		else: raise SystemExit(f"unknown argument {a[i]}")
	if not o["reference"]:
		raise SystemExit("--reference is required")
	return o


def reference_mask(frame):
	"""The original frame as a boolean mask on our canvas, its hotspot on our origin (pixels outside the canvas are dropped)."""
	mask = np.zeros((CANVAS[1], CANVAS[0]), bool)
	hx, hy = frame["hotspot"]
	for y, row in enumerate(frame["rows"]):
		cy = y - hy + ORIGIN[1]
		if not 0 <= cy < CANVAS[1]:
			continue
		for x, cell in enumerate(row):
			cx = x - hx + ORIGIN[0]
			if cell == "#" and 0 <= cx < CANVAS[0]:
				mask[cy, cx] = True
	return mask


def extent(mask):
	ys, xs = np.where(mask)
	return (0, 0) if len(ys) == 0 else (int(ys.max() - ys.min() + 1), int(xs.max() - xs.min() + 1))


def main():
	o = args()
	with open(o["reference"]) as f:
		reference = json.load(f)
	sets = o["sets"] or sorted({key.split(".")[0] for key in reference if key.endswith("." + o["bank"])})
	os.makedirs(o["out"], exist_ok=True)
	st = K.setup_scene(CANVAS, ORIGIN, "TPW")
	st.scene.render.engine = "BLENDER_WORKBENCH"
	base_ppu = st.ppu
	tmp = os.path.join(o["out"], f"tmp-{os.getpid()}.png")
	scores = {}
	shapes = {}
	for name in sets:
		frames = reference[f"{name}.{o['bank']}"]
		targets = [reference_mask(frame) for frame in frames]
		kid = K.build_kid(name, underwater=False, outfit="clothes")
		K.apply_pose(kid, K.standing_pose(kid))
		for elevation in o["elevations"]:
			st.elevation = elevation
			for scale in o["scales"]:
				st.cam.data.ortho_scale = max(CANVAS) / (base_ppu * scale)
				for d in range(5):
					K.aim_camera(st, d)
					ours = K.render_frame(st, tmp, binary_alpha=True)[..., 3] >= 0.5
					union = np.logical_or(ours, targets[d]).sum()
					iou = np.logical_and(ours, targets[d]).sum() / union if union else 0.0
					scores.setdefault((elevation, scale), []).append(iou)
					shapes[(name, elevation, scale, d)] = (extent(ours), extent(targets[d]))
		K.clear_kid(kid)
	if os.path.exists(tmp):
		os.remove(tmp)
	mean = {key: float(np.mean(values)) for key, values in scores.items()}
	print(f"mean silhouette IoU against the {o['bank']} standing frames of {','.join(sets)} (rows: elevation, columns: scale)")
	print("elev  " + "  ".join(f"{scale:5.2f}" for scale in o["scales"]))
	for elevation in o["elevations"]:
		print(f"{elevation:4.1f}  " + "  ".join(f"{mean[(elevation, scale)]:5.3f}" for scale in o["scales"]))
	best = max(mean, key=mean.get)
	print(f"best: elevation {best[0]} degrees, scale {best[1]} (ppu {base_ppu * best[1]:.3f}), IoU {mean[best]:.3f}")
	print("height x width per direction at the best, ours / original:")
	for name in sets:
		print(name, "  ".join(f"d{d} {shapes[(name, best[0], best[1], d)][0][0]}x{shapes[(name, best[0], best[1], d)][0][1]}"
			f"/{shapes[(name, best[0], best[1], d)][1][0]}x{shapes[(name, best[0], best[1], d)][1][1]}" for d in range(5)))


main()
