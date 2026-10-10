"""Derive the golden key progress frames gkey0-gkey2 from the redrawn full key gkey3.
The originals fill the key in thirds along its length: gkey0 is a pale see-through key, gkey1 has a gold ring, gkey2
a gold ring and lower shaft, gkey3 is all gold. Each frame's gold part ends where the original frame's gold ends,
measured along the key's axis, with a straight cut across the key.
usage: keyfill.py <uitex dir> <map.tsv> <hero dir with ui/textures/gkey3.wct.png>
Writes gkey0-gkey2 next to gkey3."""
import sys, os, numpy as np
from PIL import Image
uitex, mapfile, hero = sys.argv[1:4]
m = {}
for l in open(mapfile):
    png, path, _ = l.rstrip('\n').split('\t')
    if path.startswith('ui/textures/'): m[os.path.splitext(os.path.basename(path))[0].lower()] = png
def load_original(name): return np.asarray(Image.open(os.path.join(uitex, m[name])).convert('RGBA')).astype(np.float32)
textures = os.path.join(hero, 'ui', 'textures')
full = np.asarray(Image.open(os.path.join(textures, 'gkey3.wct.png')).convert('RGBA')).astype(np.float32)
def along(alpha, axis=None):
    """Position of every texel along the key's main axis, 0 at the ring end and 1 at the bit end of the shape."""
    ys, xs = np.nonzero(alpha > 128)
    pts = np.stack([xs, ys], 1).astype(np.float32); centre = pts.mean(0)
    if axis is None:
        axis = np.linalg.svd(pts - centre, full_matrices=False)[2][0]
        if axis[1] > 0: axis = -axis  # the bit is at the top
    yy, xx = np.mgrid[0:alpha.shape[0], 0:alpha.shape[1]]
    p = (xx - centre[0]) * axis[0] + (yy - centre[1]) * axis[1]
    lo, hi = p[ys, xs].min(), p[ys, xs].max()
    return (p - lo) / (hi - lo), axis
reference = load_original('gkey3')
_, axis = along(reference[..., 3])
position, _ = along(full[..., 3], axis)
# The see-through key: a pale green-white version of the gold key's shading at the original's opacity (its alpha
# peaks at 197 of 255).
lum = full[..., :3] @ np.array([0.299, 0.587, 0.114], np.float32) / 255
ghost = np.dstack([np.array([214, 240, 214], np.float32) * np.clip(0.55 + 0.6 * lum, 0, 1)[..., None], full[..., 3] * 0.6])
for name in ('gkey0', 'gkey1', 'gkey2'):
    o = load_original(name)
    gold = (np.clip(((o[..., 0] + o[..., 1]) / 2 - o[..., 2] - 60) / 80, 0, 1) > 0.5) & (o[..., 3] > 128)
    if gold.any():
        p, _ = along(reference[..., 3], axis)
        end = p[gold].max()
        w = np.clip((end - position) * 60 + 0.5, 0, 1)[..., None]
    else:
        w = np.zeros_like(full[..., :1])
    out = full * w + ghost * (1 - w)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(textures, name + '.wct.png'), optimize=True)
    print('wrote', name, 'gold to', round(float(end), 2) if gold.any() else 0)
