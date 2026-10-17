"""Builds a before/after comparison sheet of two renders of the swimmer sprites (for reviewing a change of the generator).

Usage:
  Blender -b --factory-startup --python tools/blender/opentpw_swim_compare.py -- --old /tmp/swimart-old --new /tmp/swimart \
      --out reports/swimmers-before-after.png [--sets BE,CH,FA,GR]

``--old`` and ``--new`` are output folders of ``opentpw_swim_sprites.py`` (each holding ``tpw/`` and/or ``tpi/``). Per set
one band at 1x and one at 4x (nearest neighbour), each showing old | new for the crawl from direction 2, the crawl from
direction 0 (the back) and treading water from direction 4, on the flat pool colour.
"""
import os
import sys
import numpy as np
import bpy

POOL = np.array([66, 158, 214], np.float32) / 255.0
CELL = (80, 64)
PICKS = [("A", 2, 2), ("A", 0, 2), ("C", 4, 0)]


def load(path):
	img = bpy.data.images.load(path)
	img.colorspace_settings.name = "Non-Color"
	img.alpha_mode = "CHANNEL_PACKED"
	w, h = img.size
	a = np.array(img.pixels[:], np.float32).reshape(h, w, 4)[::-1]
	bpy.data.images.remove(img)
	return a


def cell(a, zoom):
	out = np.tile(POOL, (CELL[1], CELL[0], 1))
	h, w = a.shape[:2]
	y0, x0 = (CELL[1] - h) // 2, (CELL[0] - w) // 2
	out[y0:y0 + h, x0:x0 + w] = a[..., :3] * a[..., 3:] + POOL * (1 - a[..., 3:])
	return np.kron(out, np.ones((zoom, zoom, 1), np.float32))


def find(root, name, fname):
	for ed in ("tpw", "tpi"):
		p = os.path.join(root, ed, name, fname)
		if os.path.exists(p):
			return p
	raise SystemExit(f"{name}/{fname} not found under {root}")


def main():
	a = sys.argv[sys.argv.index("--") + 1:]
	o = dict(old="/tmp/swimart-old", new="/tmp/swimart", out="/tmp/swimart/before-after.png", sets="BE,CH,FA,GR")
	for i in range(0, len(a), 2):
		o[a[i][2:]] = a[i + 1]
	bands = []
	for name in o["sets"].split(","):
		for zoom in (1, 4):
			cells = []
			for slot, d, f in PICKS:
				for root in (o["old"], o["new"]):
					cells.append(cell(load(find(root, name, f"{slot}_d{d}_f{f}.png")), zoom))
			row = np.concatenate(cells, axis=1)
			bands.append(row)
	width = max(b.shape[1] for b in bands)
	padded = []
	for b in bands:
		full = np.tile(POOL, (b.shape[0], width, 1))
		full[:, :b.shape[1]] = b
		padded.append(full)
	bands = padded
	sheet = np.concatenate(bands, axis=0)
	img = bpy.data.images.new("out", sheet.shape[1], sheet.shape[0], alpha=True)
	img.colorspace_settings.name = "Non-Color"
	rgba = np.dstack([sheet, np.ones(sheet.shape[:2], np.float32)])
	img.pixels = np.ascontiguousarray(rgba[::-1]).ravel().tolist()
	img.filepath_raw = o["out"]
	img.file_format = "PNG"
	img.save()
	print("wrote", o["out"])


main()
