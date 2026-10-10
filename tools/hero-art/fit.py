"""Fit an AI-redrawn icon back into its original texture layout, and derive state variants.
usage: fit.py <uitex dir> <map.tsv> <generated image> <base name> <out dir> <scale> [variant names...]
Writes <out>/ui/textures/<name>.wct.png for the base and every variant."""
import sys, os, numpy as np
from PIL import Image, ImageFilter
uitex, mapfile, gen_path, base, out, scale = sys.argv[1:7]; variants = sys.argv[7:]; K = int(scale)
m = {}
for l in open(mapfile):
    png, path, _ = l.rstrip('\n').split('\t')
    if path.startswith('ui/textures/'): m[os.path.splitext(os.path.basename(path))[0].lower()] = png
def load(n): return Image.open(os.path.join(uitex, m[n])).convert('RGBA')
def island(a):
    mask = a[..., 3] > 128
    ys, xs = np.nonzero(mask)
    # the largest blob: restrict to the bounding box of the biggest connected run (textures here hold one icon)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
def crisp_alpha(alpha_small, size):
    big = np.asarray(Image.fromarray(alpha_small).resize(size, Image.BICUBIC)).astype(np.float32) / 255
    # steepen the soft upscaled edge around 0.5 so the outline is sharp but anti-aliased
    return np.clip((big - 0.5) * 3 + 0.5, 0, 1)
orig = load(base); oa = np.asarray(orig)
W, H = orig.size; x0, y0, x1, y1 = island(oa)
mirror = gen_path.endswith(':mirror')
gen_img = Image.open(gen_path[:-7] if mirror else gen_path).convert('RGB')
if mirror: gen_img = gen_img.transpose(Image.FLIP_LEFT_RIGHT)
gen = np.asarray(gen_img).astype(np.float32)
# Background = median of the four corner patches (Gemini uses white or light grey); soft drop shadows stay below the threshold.
corners = np.concatenate([gen[:24, :24].reshape(-1, 3), gen[:24, -24:].reshape(-1, 3), gen[-24:, :24].reshape(-1, 3), gen[-24:, -24:].reshape(-1, 3)])
bgc = np.median(corners, axis=0)
nonwhite = np.sqrt(((gen - bgc) ** 2).sum(axis=2)) > 60
# keep the largest connected blob (the button), dropping stray marks
from collections import deque
lab = np.zeros(nonwhite.shape, np.int32); best = (0, 0); cur = 0
small = nonwhite[::4, ::4]; lab = np.zeros(small.shape, np.int32)
for y0_ in range(small.shape[0]):
    for x0_ in range(small.shape[1]):
        if small[y0_, x0_] and not lab[y0_, x0_]:
            cur += 1; q = deque([(y0_, x0_)]); lab[y0_, x0_] = cur; n = 0
            while q:
                cy, cx = q.popleft(); n += 1
                for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                    ny, nx = cy+dy, cx+dx
                    if 0 <= ny < small.shape[0] and 0 <= nx < small.shape[1] and small[ny, nx] and not lab[ny, nx]:
                        lab[ny, nx] = cur; q.append((ny, nx))
            if n > best[1]: best = (cur, n)
ys, xs = np.nonzero(lab == best[0]); ys = ys * 4; xs = xs * 4
crop = Image.fromarray(gen.astype(np.uint8)).crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
tw, th = (x1 - x0) * K, (y1 - y0) * K
icon = np.asarray(crop.resize((tw, th), Image.LANCZOS)).astype(np.float32)
# Base layer: the original texture upscaled (keeps anything outside the icon), then the redrawn icon on top.
rgb = np.asarray(orig.convert('RGB').resize((W * K, H * K), Image.BICUBIC)).astype(np.float32)
rgb[y0 * K:y1 * K, x0 * K:x1 * K] = icon
os.makedirs(os.path.join(out, 'ui', 'textures'), exist_ok=True)
def shifts(a, fill):
    # the eight 3x3 neighbours of every pixel, padded with fill
    p = np.pad(a, [(1, 1), (1, 1)] + [(0, 0)] * (a.ndim - 2), constant_values=fill)
    h, w = a.shape[:2]
    return [p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w] for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx]
# Generated background: pixels close to bgc that connect to the edge of the icon rectangle (white symbols enclosed by
# the button stay). Where the original mask is wider than the redrawn silhouette, these would show white through alpha.
dist = np.sqrt(((icon - bgc) ** 2).sum(axis=2)); near = dist < 60
outer = np.zeros_like(near); outer[0, :] = near[0, :]; outer[-1, :] = near[-1, :]; outer[:, 0] = near[:, 0]; outer[:, -1] = near[:, -1]
while True:
    grown = outer.copy()
    for n in shifts(outer, False): grown |= n
    grown &= near
    if (grown == outer).all(): break
    outer = grown
background = np.zeros((H * K, W * K), bool)
background[y0 * K:y1 * K, x0 * K:x1 * K] = outer
# the anti-aliased fringe between object and background is still light: widen by a few pixels (edge band only, below)
fringe = background.copy()
for _ in range(3):
    for n in shifts(fringe, False): fringe = fringe | n
# Coverage of the redrawn silhouette near the generated background: where the original mask is wider than the new
# outline, the original alpha would keep a ragged strip of backdrop; the alpha is capped to the new outline there.
coverage = np.ones((H * K, W * K), np.float32)
coverage[y0 * K:y1 * K, x0 * K:x1 * K] = np.clip((dist - 40) / 40, 0, 1)
coverage = np.where(fringe, coverage, 1)
def bleed(rgb_img, alpha):
    # Recolour background and fully transparent texels with the nearest object colour, so neither the white backdrop
    # nor linear filtering of alpha-0 texels lightens the edge. Alpha is kept.
    known = ~(background | (fringe & (alpha < 0.999)) | (alpha <= 0))
    col = np.where(known[..., None], rgb_img, 0).astype(np.float32)
    while not known.all():
        acc = np.zeros_like(col); cnt = np.zeros(known.shape, np.float32)
        for c, k in zip(shifts(col, 0), shifts(known, False)):
            acc += c * k[..., None]; cnt += k
        new = ~known & (cnt > 0)
        if not new.any(): break
        col[new] = acc[new] / cnt[new][:, None]; known = known | new
    return col
def write(name, rgb_img, alpha_small):
    alpha = np.minimum(crisp_alpha(alpha_small, (W * K, H * K)), coverage)
    rgba = np.dstack([np.clip(bleed(rgb_img, alpha), 0, 255), alpha * 255]).astype(np.uint8)
    Image.fromarray(rgba, 'RGBA').save(os.path.join(out, 'ui', 'textures', name + '.wct.png'))
write(base, rgb, oa[..., 3])
# Variants: same layout, recoloured. Fit a smooth colour mapping base -> variant (quadratic in RGB) on the original
# pixels both share, and apply it to the redrawn base; a global mapping carries "darker" or "lit background" without
# local blotches.
def features(c):
    r, g, b = c[..., 0] / 255, c[..., 1] / 255, c[..., 2] / 255
    return np.stack([np.ones_like(r), r, g, b, r * r, g * g, b * b, r * g, r * b, g * b], axis=-1)
for v in variants:
    va = np.asarray(load(v))
    both = (oa[..., 3] > 200) & (va[..., 3] > 200)
    X = features(oa[..., :3].astype(np.float32)[both]); Y = va[..., :3].astype(np.float32)[both] / 255
    suffix = v[len(base):] if v.startswith(base) else v[-1]
    if suffix in ('d', 'g'):
        src = oa[..., :3].astype(np.float32)[both] / 255; dst = Y
        if suffix == 'g':
            # grey state: desaturate the redrawn base, then match the original's brightness per channel
            lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
            work = np.repeat(lum[..., None], 3, axis=2)
            src = np.repeat((src @ np.array([0.299, 0.587, 0.114], np.float32))[:, None], 3, axis=1)
        else:
            work = rgb
        # pressed/grey: one gain per channel (keeps hues), least squares over the shared pixels
        gain = (src * dst).sum(axis=0) / np.maximum((src * src).sum(axis=0), 1e-6)
        mapped = np.clip(work * gain, 0, 255)
    else:
        coef, *_ = np.linalg.lstsq(X, Y, rcond=None)
        mapped = np.clip(features(rgb) @ coef, 0, 1) * 255
    write(v, mapped, va[..., 3])
print('wrote', base, variants)
