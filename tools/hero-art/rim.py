"""Keep the original button rim (from the anime-upscaled original) and take only the inner face/symbol from the redrawn icon.
usage: rim.py <uitex dir> <map.tsv> <hero dir> <anime dir> <out dir> [inner fraction]"""
import sys, os, numpy as np
from PIL import Image
uitex, mapfile, hero_dir, anime_dir, out_dir = sys.argv[1:6]; inner = float(sys.argv[6]) if len(sys.argv) > 6 else 0.80
m = {}
for l in open(mapfile):
    png, path, _ = l.rstrip('\n').split('\t')
    if path.startswith('ui/textures/'): m[os.path.splitext(os.path.basename(path))[0].lower()] = png
os.makedirs(out_dir, exist_ok=True)
skip = ('b_door', 'b_scroller', 'b_exit', 'b_okay', 'b_retract', 'b_erase', 'b_entpark', 'b_sleft', 'b_sright', 'lob')
for f in sorted(os.listdir(hero_dir)):
    n = f[:-len('.wct.png')]
    hero = Image.open(os.path.join(hero_dir, f)).convert('RGBA')
    if n.startswith(skip) or not os.path.exists(os.path.join(anime_dir, f)):
        hero.save(os.path.join(out_dir, f), optimize=True); print('kept', n); continue
    orig = np.asarray(Image.open(os.path.join(uitex, m[n])).convert('RGBA'))
    K = hero.width // orig.shape[1]
    ys, xs = np.nonzero(orig[..., 3] > 128); x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
    anime = Image.open(os.path.join(anime_dir, f)).convert('RGBA').resize(hero.size, Image.LANCZOS)
    cx, cy = (x0 + x1) / 2 * K, (y0 + y1) / 2 * K; r = min(x1 - x0, y1 - y0) / 2 * K
    yy, xx = np.mgrid[0:hero.height, 0:hero.width]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
    w = np.clip((inner + 0.04 - d) / 0.08, 0, 1)[..., None]
    out = np.asarray(anime).astype(np.float32) * (1 - w) + np.asarray(hero).astype(np.float32) * w
    out[..., 3] = np.asarray(hero)[..., 3]
    Image.fromarray(out.astype(np.uint8), 'RGBA').save(os.path.join(out_dir, f), optimize=True); print('rim', n)
