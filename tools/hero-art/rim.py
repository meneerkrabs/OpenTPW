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
def outline(alpha):
    """The original shape's extent (left, top, right, bottom) in texels, where its alpha crosses 50% along the row and
    column through its centre. A bounding box of alpha > 50% would include the soft drop shadow some originals carry
    at the bottom right, shifting the rim circle."""
    a = alpha.astype(np.float32)
    ys, xs = np.nonzero(a > 128); cy, cx = int(round(ys.mean())), int(round(xs.mean()))
    def span(p):
        inside = np.nonzero(p > 128)[0]; i, j = inside.min(), inside.max()
        lo = i + 0.5 - (p[i] - 128) / max(p[i] - (p[i - 1] if i > 0 else 0), 1)
        hi = j + 0.5 + (p[j] - 128) / max(p[j] - (p[j + 1] if j + 1 < len(p) else 0), 1)
        return lo, hi
    (l, r), (t, b) = span(a[cy]), span(a[:, cx])
    return l, t, r, b
skip = ('b_door', 'b_scroller', 'b_exit', 'b_okay', 'b_retract', 'b_erase', 'b_entpark', 'b_sleft', 'b_sright', 'lob')
for f in sorted(os.listdir(hero_dir)):
    n = f[:-len('.wct.png')]
    hero = Image.open(os.path.join(hero_dir, f)).convert('RGBA')
    if n.startswith(skip) or not os.path.exists(os.path.join(anime_dir, f)):
        hero.save(os.path.join(out_dir, f), optimize=True); print('kept', n); continue
    orig = np.asarray(Image.open(os.path.join(uitex, m[n])).convert('RGBA'))
    K = hero.width // orig.shape[1]
    x0, y0, x1, y1 = outline(orig[..., 3])
    anime = Image.open(os.path.join(anime_dir, f)).convert('RGBA').resize(hero.size, Image.LANCZOS)
    cx, cy = (x0 + x1) / 2 * K, (y0 + y1) / 2 * K; r = min(x1 - x0, y1 - y0) / 2 * K
    yy, xx = np.mgrid[0:hero.height, 0:hero.width]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
    w = np.clip((inner + 0.04 - d) / 0.08, 0, 1)[..., None]
    out = np.asarray(anime).astype(np.float32) * (1 - w) + np.asarray(hero).astype(np.float32) * w
    # Outline: a clean anti-aliased ellipse on the original's outline. The upscaled original's own alpha carries its
    # soft drop shadow, which the upscaler turns into a ragged dark fringe.
    ex, ey = (x1 - x0) / 2 * K, (y1 - y0) / 2 * K; ecx, ecy = (x0 + x1) / 2 * K, (y0 + y1) / 2 * K
    e = np.sqrt(((xx + 0.5 - ecx) / ex) ** 2 + ((yy + 0.5 - ecy) / ey) ** 2)
    alpha = np.clip((1 - e) * min(ex, ey) + 0.5, 0, 1)
    # texels outside the outline take the colour just inside it, so filtering does not pull in the dark fringe
    emax = 1 - 1.5 / min(ex, ey); pull = np.where(e > emax, emax / np.maximum(e, 1e-6), 1)
    sx = np.clip(np.round(ecx + (xx + 0.5 - ecx) * pull - 0.5).astype(int), 0, hero.width - 1)
    sy = np.clip(np.round(ecy + (yy + 0.5 - ecy) * pull - 0.5).astype(int), 0, hero.height - 1)
    out = out[sy, sx]
    out[..., 3] = alpha * 255
    Image.fromarray(out.astype(np.uint8), 'RGBA').save(os.path.join(out_dir, f), optimize=True); print('rim', n)
