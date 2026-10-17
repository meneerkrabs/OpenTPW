"""Contact sheets, animated overviews and original-versus-ours comparisons of the sets written by ``opentpw_guest_sprites.py``.

Usage (inside Blender, only for its image API and numpy):
  Blender -b --factory-startup --python tools/blender/opentpw_guest_compare.py -- --guests /tmp/swimart/guests --edition TPW \
      [--overview-gif reports/guests-all-slots-tpw.gif] [--walk-compare reports/guests-walk-compare.png --reference /tmp/swimart-ref --pick TPW/BE,TPW/CH,TPI/FA,TPI/GR [--slot 1]] \
      [--sheet /tmp/swimart/BE-sheet.png --set BE [--setdir <folder of a set, e.g. a heads set>]]

The reference folder holds the originals exported by a sprite exporter (``<edition>/<Folder>/<SET>/sheet_slotNN.png``: a green
sheet per slot, 72 x 90 cells at 2x with the hotspot at (36, 78)); it is local only and never committed.
"""
import json
import os
import subprocess
import sys

import bpy
import numpy as np

BG = np.array([120, 170, 110], np.float32) / 255.0
POOL = np.array([66, 158, 214], np.float32) / 255.0
CELL = (72, 90)
HOTSPOT_LAND = (36, 78)
HOTSPOT_SWIM = (36, 46)
FFMPEG = "/opt/homebrew/bin/ffmpeg"


def load_png(path):
	img = bpy.data.images.load(path)
	img.colorspace_settings.name = "Non-Color"
	img.alpha_mode = "CHANNEL_PACKED"
	w, h = img.size
	a = np.array(img.pixels[:], np.float32).reshape(h, w, 4)[::-1].copy()
	bpy.data.images.remove(img)
	return a


def save_png(a, path):
	h, w = a.shape[:2]
	img = bpy.data.images.new("o", w, h, alpha=True)
	img.colorspace_settings.name = "Non-Color"
	rgba = np.dstack([a[..., :3], np.ones((h, w), np.float32)])
	img.pixels = np.ascontiguousarray(rgba[::-1]).ravel().tolist()
	img.filepath_raw = path
	img.file_format = "PNG"
	img.save()
	bpy.data.images.remove(img)


def load_set(folder):
	meta = json.load(open(os.path.join(folder, "set.json")))
	cache = {}
	for slot in meta["slots"]:
		for row in slot["frames"]:
			for fr in row:
				cache[fr["file"]] = load_png(os.path.join(folder, fr["file"]))
	return meta, cache


def cell_of(meta, cache, slot_no, d, f, bg=BG, scale=2, hot=None):
	"""A CELL-sized picture (at ``scale``) of one frame with the hotspot at the cell's hotspot."""
	slot = next((s for s in meta["slots"] if s["slot"] == slot_no), None)
	out = np.tile(bg, (CELL[1], CELL[0], 1)).astype(np.float32)
	if slot is not None:
		fr = slot["frames"][min(d, slot["directions"] - 1)][f % slot["framesPerDirection"]]
		img = cache[fr["file"]]
		hx, hy = hot or (HOTSPOT_SWIM if slot["source"] in ("swim", "heads") else HOTSPOT_LAND)
		x0, y0 = hx + fr["originX"], hy + fr["originY"]
		h, w = img.shape[:2]
		xa, ya = max(0, x0), max(0, y0)
		xb, yb = min(CELL[0], x0 + w), min(CELL[1], y0 + h)
		if xb > xa and yb > ya:
			sub = img[ya - y0:yb - y0, xa - x0:xb - x0]
			a = sub[..., 3:4]
			out[ya:yb, xa:xb] = sub[..., :3] * a + out[ya:yb, xa:xb] * (1 - a)
	return np.kron(out, np.ones((scale, scale, 1), np.float32))


def slot_sheet(meta, cache, slot_no, scale=2):
	slot = next(s for s in meta["slots"] if s["slot"] == slot_no)
	rows = [np.concatenate([cell_of(meta, cache, slot_no, d, f, scale=scale) for f in range(slot["framesPerDirection"])], axis=1) for d in range(slot["directions"])]
	return np.concatenate(rows, axis=0)


def make_gif(frames, path, fps, workdir):
	os.makedirs(workdir, exist_ok=True)
	for i, fr in enumerate(frames):
		save_png(fr, os.path.join(workdir, f"g{i:03d}.png"))
	subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", os.path.join(workdir, "g%03d.png"),
		"-vf", "split[a][b];[a]palettegen=max_colors=255[p];[b][p]paletteuse", "-loop", "0", path], check=True)
	for i in range(len(frames)):
		os.remove(os.path.join(workdir, f"g{i:03d}.png"))


def edition_sets(guests, edition):
	root = os.path.join(guests, edition.lower())
	out = []
	for folder in ("Kids", "Parents", "Grandparents"):
		d = os.path.join(root, folder)
		if os.path.isdir(d):
			out += [(name, os.path.join(d, name)) for name in sorted(os.listdir(d))]
	return out


def overview(guests, edition, path, scale=2):
	"""An animated sheet: a row of every set per direction (4 then 2), a column per slot, 8 ticks."""
	sets = [(n, load_set(p)) for n, p in edition_sets(guests, edition)]
	slots = sorted({s["slot"] for _, (m, _c) in sets for s in m["slots"]})
	frames = []
	for t in range(8):
		rows = []
		for name, (meta, cache) in sets:
			for d in (4, 2):
				rows.append(np.concatenate([cell_of(meta, cache, sl, d, t, bg=POOL if sl >= 8 else BG, scale=scale) for sl in slots], axis=1))
		frames.append(np.concatenate(rows, axis=0))
	make_gif(frames, path, 8, os.path.join(os.path.dirname(path) or ".", "gifwork"))


def walk_compare(guests, reference, path, picks, slot=1, dirs=(2, 4)):
	"""Original (left) and ours (right) animations (the walk by default, or ``--slot N``) of some sets (``picks`` like ``TPW/BE``), directions 2 and 4, at 2x."""
	blocks = []
	for pick in picks:
		edition, name = pick.split("/")
		root_ref = os.path.join(reference, edition.lower())
		folder = dict(edition_sets(guests, edition))[name]
		meta, cache = load_set(folder)
		kind = os.path.basename(os.path.dirname(folder))
		ref = load_png(os.path.join(root_ref, kind, f"SPR_{name}", f"sheet_slot{slot:02d}.png"))
		ours = slot_sheet(meta, cache, slot)
		rows = []
		for d in dirs:
			r = ref[d * CELL[1] * 2:(d + 1) * CELL[1] * 2, :, :3]
			o = ours[d * CELL[1] * 2:(d + 1) * CELL[1] * 2, :, :3]
			rows.append(np.concatenate([r, np.tile(np.array([0.05, 0.05, 0.05], np.float32), (r.shape[0], 8, 1)), o], axis=1))
		blocks.append(np.concatenate(rows, axis=0))
		blocks.append(np.tile(np.array([0.05, 0.05, 0.05], np.float32), (8, blocks[-1].shape[1], 1)))
	save_png(np.concatenate(blocks, axis=0), path)


def main():
	a = sys.argv[sys.argv.index("--") + 1:]
	o = dict(guests="/tmp/swimart/guests", edition="TPW", reference="/tmp/swimart-ref", pick="TPW/BE,TPW/CH,TPI/FA,TPI/GR")
	i = 0
	while i < len(a):
		o[a[i][2:].replace("-", "_")] = a[i + 1]
		i += 2
	if "overview_gif" in o:
		overview(o["guests"], o["edition"], o["overview_gif"])
	if "walk_compare" in o:
		walk_compare(o["guests"], o["reference"], o["walk_compare"], o["pick"].split(","), int(o.get("slot", 1)))
	if "sheet" in o:
		candidates = [(o["set"], o["setdir"])] if "setdir" in o else edition_sets(o["guests"], o["edition"])
		for name, folder in candidates:
			if name == o["set"]:
				meta, cache = load_set(folder)
				slots = [s["slot"] for s in meta["slots"]]
				sheets = [slot_sheet(meta, cache, s) for s in slots]
				height = max(s.shape[0] for s in sheets)
				pad = lambda s: np.pad(s, ((0, height - s.shape[0]), (0, 8), (0, 0)), constant_values=0.05)
				save_png(np.concatenate([pad(s) for s in sheets], axis=1), o["sheet"])


if __name__ == "__main__":
	main()
