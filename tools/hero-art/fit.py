"""Fit an AI-redrawn icon back into its original texture layout, and derive state variants.
usage: fit.py <uitex dir> <map.tsv> <generated image> <base name> <out dir> <scale> [variant names...]
Writes <out>/ui/textures/<name>.wct.png for the base and every variant."""
import sys, os, numpy as np
from PIL import Image, ImageDraw, ImageFilter
uitex, mapfile, gen_path, base, out, scale = sys.argv[1:7]; variants = sys.argv[7:]; K = int(scale)
m = {}
for l in open(mapfile):
    png, path, _ = l.rstrip('\n').split('\t')
    if path.startswith('ui/textures/'): m[os.path.splitext(os.path.basename(path))[0].lower()] = png
def load(n): return Image.open(os.path.join(uitex, m[n])).convert('RGBA')
def outline(alpha):
    """The original shape's extent (left, top, right, bottom) in texels, where its alpha crosses 50% along the row and
    column through its centre. A bounding box of alpha > 50% would include the soft drop shadow some originals carry
    at the bottom right, stretching the redrawn icon past the original outline."""
    a = alpha.astype(np.float32)
    ys, xs = np.nonzero(a > 128); cy, cx = int(round(ys.mean())), int(round(xs.mean()))
    def span(p):
        inside = np.nonzero(p > 128)[0]; i, j = inside.min(), inside.max()
        lo = i + 0.5 - (p[i] - 128) / max(p[i] - (p[i - 1] if i > 0 else 0), 1)
        hi = j + 0.5 + (p[j] - 128) / max(p[j] - (p[j + 1] if j + 1 < len(p) else 0), 1)
        return lo, hi
    (l, r), (t, b) = span(a[cy]), span(a[:, cx])
    # Shapes that are not round (a diagonal key, a bent ticket) do not reach their extent through the centre; use the
    # bounding box of alpha > 50% for them.
    if r - l < 0.85 * (xs.max() + 1 - xs.min()) or b - t < 0.85 * (ys.max() + 1 - ys.min()):
        return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
    return l, t, r, b
orig = load(base); oa = np.asarray(orig)
W, H = orig.size
# the icon rectangle in output pixels
X0, Y0, X1, Y1 = [int(round(v * K)) for v in outline(oa[..., 3])]
mirror = gen_path.endswith(':mirror')
gen_img = Image.open(gen_path[:-7] if mirror else gen_path).convert('RGB')
if mirror: gen_img = gen_img.transpose(Image.FLIP_LEFT_RIGHT)
gen = np.asarray(gen_img).astype(np.float32)
# Background = median of the four corner patches (Gemini uses white or light grey).
corners = np.concatenate([gen[:24, :24].reshape(-1, 3), gen[:24, -24:].reshape(-1, 3), gen[-24:, :24].reshape(-1, 3), gen[-24:, -24:].reshape(-1, 3)])
bgc = np.median(corners, axis=0)
# Backdrop = everything reachable from the image border through pixels that are near bgc or part of the soft grey drop
# shadow the image model draws under an icon (unsaturated, not dark). Enclosed white or grey parts of the icon are not
# reachable and stay. The rest is the redrawn silhouette.
chroma = gen.max(axis=2) - gen.min(axis=2)
backdropish = (np.sqrt(((gen - bgc) ** 2).sum(axis=2)) < 60) | ((chroma < 28) & (gen.mean(axis=2) > 110))
fill = Image.fromarray(np.where(backdropish, 255, 0).astype(np.uint8)).copy()  # writable copy for floodfill
gh, gw = backdropish.shape
for sx, sy in [(0, 0), (gw - 1, 0), (0, gh - 1), (gw - 1, gh - 1), (gw // 2, 0), (gw // 2, gh - 1), (0, gh // 2), (gw - 1, gh // 2)]:
    if fill.getpixel((sx, sy)) == 255: ImageDraw.floodfill(fill, (sx, sy), 128)
silhouette = np.asarray(fill) != 128
# Drop the outermost pixel (the anti-aliased blend of the outline into the backdrop) and, with it, JPEG ringing specks
# that touch the outline only through such pixels.
silhouette = np.asarray(Image.fromarray(silhouette.astype(np.uint8) * 255).filter(ImageFilter.MinFilter(3))) > 0
# keep the largest connected blob (the button), dropping stray marks
from collections import deque
small = silhouette[::4, ::4]; lab = np.zeros(small.shape, np.int32); best = (0, 0); cur = 0
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
seed_y, seed_x = [int(v[0]) * 4 for v in np.nonzero(lab == best[0])]
blob = Image.fromarray(silhouette.astype(np.uint8) * 255).copy()
ImageDraw.floodfill(blob, (seed_x, seed_y), 128)
silhouette = np.asarray(blob) == 128
ys, xs = np.nonzero(silhouette)
box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
# Paint the backdrop around the silhouette with the nearest outline colour before resampling, so the downscale cannot
# mix white into the edge texels.
pad = 24
bx0, by0, bx1, by1 = max(box[0] - pad, 0), max(box[1] - pad, 0), min(box[2] + pad, gw), min(box[3] + pad, gh)
region = gen[by0:by1, bx0:bx1].copy(); known = silhouette[by0:by1, bx0:bx1].copy()
def neighbours(a, fill):
    p = np.pad(a, [(1, 1), (1, 1)] + [(0, 0)] * (a.ndim - 2), constant_values=fill)
    h, w = a.shape[:2]
    return [p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w] for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx]
for _ in range(pad):
    acc = np.zeros_like(region); cnt = np.zeros(known.shape, np.float32)
    for c, k in zip(neighbours(np.where(known[..., None], region, 0), 0), neighbours(known, False)):
        acc += c * k[..., None]; cnt += k
    new = ~known & (cnt > 0)
    region[new] = acc[new] / cnt[new][:, None]; known = known | new
painted = gen.copy(); painted[by0:by1, bx0:bx1] = region
crop = Image.fromarray(np.clip(painted, 0, 255).astype(np.uint8)).crop(box)
tw, th = X1 - X0, Y1 - Y0
icon = np.asarray(crop.resize((tw, th), Image.LANCZOS)).astype(np.float32)
# Smooth outline of the redrawn icon at the target size (JPEG noise along the edge blurred away, then anti-aliased).
sil = Image.fromarray(silhouette.astype(np.uint8) * 255).crop(box).filter(ImageFilter.GaussianBlur(2))
sil = np.asarray(sil.point(lambda v: 255 if v >= 128 else 0).resize((tw, th), Image.BOX)).astype(np.float32) / 255
# Backdrop enclosed by the outline (the hole of a key ring) is a hole where the original is transparent too; white
# parts of the icon itself lie on opaque original texels and stay.
hole = Image.fromarray((np.sqrt(((gen - bgc) ** 2).sum(axis=2)) < 60).astype(np.uint8) * 255).crop(box)
hole = np.asarray(hole.resize((tw, th), Image.BOX)).astype(np.float32) / 255
original_alpha = np.asarray(Image.fromarray(oa[..., 3]).resize((W * K, H * K), Image.BICUBIC)).astype(np.float32)[Y0:Y1, X0:X1] / 255
# Decide per enclosed region (so its edge stays the redrawn one): a hole when the original is mostly transparent there.
regions = Image.fromarray(((hole > 0.5) & (sil > 0.5)).astype(np.uint8) * 255).copy()
label = 1
for y, x in zip(*np.nonzero(np.asarray(regions) == 255)):
    if regions.getpixel((int(x), int(y))) != 255: continue
    ImageDraw.floodfill(regions, (int(x), int(y)), label)
    inside = np.asarray(regions) == label
    if original_alpha[inside].mean() < 0.5:
        grown = np.asarray(Image.fromarray(inside.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(5))) > 0
        sil = sil * (1 - np.where(grown, hole, 0))
    label = label % 250 + 1
# Base layer: the original texture upscaled (keeps anything outside the icon), then the redrawn icon on top.
rgb = np.asarray(orig.convert('RGB').resize((W * K, H * K), Image.BICUBIC)).astype(np.float32)
rgb[Y0:Y1, X0:X1] = icon
os.makedirs(os.path.join(out, 'ui', 'textures'), exist_ok=True)
def shifts(a, fill):
    # the eight 3x3 neighbours of every pixel, padded with fill
    p = np.pad(a, [(1, 1), (1, 1)] + [(0, 0)] * (a.ndim - 2), constant_values=fill)
    h, w = a.shape[:2]
    return [p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w] for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx]
# Alpha = the redrawn outline, placed on the original outline. (Capping it with the original mask cut into the new rim
# wherever the two outlines differ by a texel.)
coverage = np.zeros((H * K, W * K), np.float32)
coverage[Y0:Y1, X0:X1] = sil
def bleed(rgb_img, alpha):
    # Recolour every texel outside the solid outline with the nearest solid icon colour, so neither the backdrop nor
    # linear filtering of alpha-0 texels lightens the edge. Alpha is kept.
    known = alpha >= 0.999
    col = np.where(known[..., None], rgb_img, 0).astype(np.float32)
    while not known.all():
        acc = np.zeros_like(col); cnt = np.zeros(known.shape, np.float32)
        for c, k in zip(shifts(col, 0), shifts(known, False)):
            acc += c * k[..., None]; cnt += k
        new = ~known & (cnt > 0)
        if not new.any(): break
        col[new] = acc[new] / cnt[new][:, None]; known = known | new
    return col
def write(name, rgb_img):
    rgba = np.dstack([np.clip(bleed(rgb_img, coverage), 0, 255), coverage * 255]).astype(np.uint8)
    Image.fromarray(rgba, 'RGBA').save(os.path.join(out, 'ui', 'textures', name + '.wct.png'))
write(base, rgb)
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
    write(v, mapped)
print('wrote', base, variants)
