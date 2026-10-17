"""Contact sheets of a generated sprite set at its own resolution, and 1x against 2x comparisons of the sets of ``opentpw_guest_sprites.py``.

Usage (inside Blender, only for its image API and numpy):
  Blender -b --factory-startup --python tools/blender/opentpw_guest_hires_compare.py -- \
      --sheet /tmp/pool-sheet.png --setdir /tmp/hires/guests/tpw/Pool/BE [--slots 3,4,5] [--directions 0,2,4]
  Blender -b --factory-startup --python tools/blender/opentpw_guest_hires_compare.py -- \
      --compare reports/guests-hires-compare.png --lo /tmp/lo/tpw --hi /tmp/hi/tpw --pick Kids/BE,Kids/CH,Kids/TA,Kidsheads/BE [--cells 0:4:0,1:2:2,1:4:0,12:4:2] [--zoom 2]   (default cells: a set per folder)

``--sheet``: every slot of a set folder (``set.json`` plus frames) in rows of directions by frames, each cell as large as the biggest frame
of the set and the hotspot marked with a small cross, on green (dry slots) or pool blue (the slots whose hotspot is at the water line).
``--compare``: for each pick (``<Folder>/<SET>`` below the ``--lo`` and ``--hi`` roots) one row per cell (``slot:direction:frame``), the
1x frame enlarged to the 2x grid with nearest-neighbour sampling on the left and the 2x frame on the right; the two are the same
size in the world (the game scales a frame by 128 / its pixels-per-unit field), so what differs is the detail.
"""
import json
import os
import sys

import bpy
import numpy as np

GREEN = np.array([120, 170, 110], np.float32) / 255.0
POOL = np.array([66, 158, 214], np.float32) / 255.0
GREY = np.array([0.05, 0.05, 0.05], np.float32)


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


def frame_of(meta, slot_no, d, f):
	slot = next((s for s in meta["slots"] if s["slot"] == slot_no), None)
	if slot is None:
		return None, None
	return slot, slot["frames"][min(d, slot["directions"] - 1)][f % slot["framesPerDirection"]]


def blit(cell, img, hot, origin, up=1):
	"""Alpha-blends ``img`` (enlarged ``up`` times, nearest) into ``cell`` with its hotspot at ``hot`` (cell pixels)."""
	if up > 1:
		img = np.kron(img, np.ones((up, up, 1), np.float32))
	x0, y0 = hot[0] + origin[0] * up, hot[1] + origin[1] * up
	h, w = img.shape[:2]
	xa, ya = max(0, x0), max(0, y0)
	xb, yb = min(cell.shape[1], x0 + w), min(cell.shape[0], y0 + h)
	if xb > xa and yb > ya:
		sub = img[ya - y0:yb - y0, xa - x0:xb - x0]
		a = sub[..., 3:4]
		cell[ya:yb, xa:xb] = sub[..., :3] * a + cell[ya:yb, xa:xb] * (1 - a)


def extents(meta):
	"""The union of every frame's rectangle around the hotspot, per hotspot kind: (min x, min y, max x, max y)."""
	boxes = {}
	for slot in meta["slots"]:
		kind = slot.get("hotspot", "water" if slot["source"] == "swim" else "feet")
		for row in slot["frames"]:
			for fr in row:
				b = boxes.setdefault(kind, [0, 0, 0, 0])
				b[0], b[1] = min(b[0], fr["originX"]), min(b[1], fr["originY"])
				b[2], b[3] = max(b[2], fr["originX"] + fr["width"]), max(b[3], fr["originY"] + fr["height"])
	return boxes


def sheet(setdir, path, slots=None, directions=None, scale=1):
	meta, cache = load_set(setdir)
	boxes = extents(meta)
	blocks = []
	for slot in meta["slots"]:
		if slots and slot["slot"] not in slots:
			continue
		kind = slot.get("hotspot", "water" if slot["source"] == "swim" else "feet")
		b = boxes[kind]
		cell_w, cell_h = b[2] - b[0] + 4, b[3] - b[1] + 4
		hot = (2 - b[0], 2 - b[1])
		bg = POOL if kind == "water" else GREEN
		rows = []
		for d in range(slot["directions"]):
			if directions and d not in directions:
				continue
			cells = []
			for f in range(slot["framesPerDirection"]):
				cell = np.tile(bg, (cell_h, cell_w, 1)).astype(np.float32)
				_, fr = frame_of(meta, slot["slot"], d, f)
				blit(cell, cache[fr["file"]], hot, (fr["originX"], fr["originY"]))
				cell[hot[1], max(0, hot[0] - 2):hot[0] + 3] = (1, 0, 0)
				cell[max(0, hot[1] - 2):hot[1] + 3, hot[0]] = (1, 0, 0)
				cells.append(np.kron(cell, np.ones((scale, scale, 1), np.float32)))
			rows.append(np.concatenate(cells, axis=1))
		blocks.append(np.concatenate(rows, axis=0))
	height = max(b.shape[0] for b in blocks)
	save_png(np.concatenate([np.pad(b, ((0, height - b.shape[0]), (4, 0), (0, 0)), constant_values=0.05) for b in blocks], axis=1), path)


DEFAULT_CELLS = {
	"Kids": "0:4:0,1:2:2,1:4:0,6:4:2,12:4:2,11:4:3",
	"Kidsheads": "0:0:0,0:2:1,0:3:2,0:4:3,0:5:4,0:6:5",
	"Pool": "0:4:0,1:2:2,11:4:2,12:4:1,13:4:1,9:2:4",
}


def compare(lo_root, hi_root, picks, cells, path, zoom=1):
	rows = []
	for pick in picks:
		folder, name = pick.split("/")
		lo_meta, lo_cache = load_set(os.path.join(lo_root, folder, name))
		hi_meta, hi_cache = load_set(os.path.join(hi_root, folder, name))
		up = hi_meta.get("scale", 2) // lo_meta.get("scale", 1)
		strip = []
		for spec in (cells or DEFAULT_CELLS.get(folder, DEFAULT_CELLS["Kids"])).split(","):
			slot_no, d, f = (int(v) for v in spec.split(":"))
			pair = []
			for meta, cache, factor in ((lo_meta, lo_cache, up), (hi_meta, hi_cache, 1)):
				slot, fr = frame_of(meta, slot_no, d, f)
				if fr is None:
					pair.append(None)
					continue
				pair.append((cache[fr["file"]], (fr["originX"], fr["originY"]), factor, meta))
			boxes = [(img.shape[1] * fct, img.shape[0] * fct, o[0] * fct, o[1] * fct) for p in pair if p for img, o, fct, _ in [p]]
			if not boxes:
				continue
			x0 = min(b[2] for b in boxes)
			y0 = min(b[3] for b in boxes)
			x1 = max(b[2] + b[0] for b in boxes)
			y1 = max(b[3] + b[1] for b in boxes)
			w, h = x1 - x0 + 6, y1 - y0 + 6
			meta_of = {id(p): p[3] for p in pair if p}
			for p in pair:
				wet = next((sl.get("hotspot") == "water" for sl in meta_of[id(p)].get("slots", []) if sl["slot"] == slot_no), False) if p else False
				cell = np.tile(POOL if wet else GREEN, (h, w, 1)).astype(np.float32)
				if p:
					blit(cell, p[0], (3 - x0, 3 - y0), p[1], p[2])
				strip.append(cell)
				strip.append(np.tile(GREY, (h, 4, 1)))
			strip.append(np.tile(GREY, (h, 10, 1)))
		height = max(s.shape[0] for s in strip)
		rows.append(np.concatenate([np.pad(s, ((0, height - s.shape[0]), (0, 0), (0, 0)), constant_values=0.05) for s in strip], axis=1))
		rows.append(np.tile(GREY, (8, rows[-1].shape[1], 1)))
	width = max(r.shape[1] for r in rows)
	image = np.concatenate([np.pad(r, ((0, 0), (0, width - r.shape[1]), (0, 0)), constant_values=0.05) for r in rows], axis=0)
	save_png(np.kron(image, np.ones((zoom, zoom, 1), np.float32)) if zoom > 1 else image, path)


def main():
	a = sys.argv[sys.argv.index("--") + 1:]
	o = {}
	for i in range(0, len(a), 2):
		o[a[i][2:].replace("-", "_")] = a[i + 1]
	ints = lambda s: [int(v) for v in s.split(",")] if s else None
	if "sheet" in o:
		sheet(o["setdir"], o["sheet"], ints(o.get("slots")), ints(o.get("directions")), int(o.get("scale", 1)))
	if "compare" in o:
		compare(o["lo"], o["hi"], o["pick"].split(","), o.get("cells"), o["compare"], int(o.get("zoom", 1)))


if __name__ == "__main__":
	main()
