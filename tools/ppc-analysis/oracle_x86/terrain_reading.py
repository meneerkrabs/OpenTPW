"""Python statement of the procedural terrain texturer, the plan side of the x86 terrain targets
(docs/reverse/ORACLE.md section 7, X27; docs/LND.md). Pure Python: no executable bytes, nothing imported from the game.

* `ramp` the ramp table 0x55e710 and `weights` the weight plane 0x55e780 (16.16 forward differencing over 4 x 4 sub blocks);
* `build` the head of the build routine 0x55f780: validity, three weight planes, per image windows, per 16 texel run layer decisions
  (A, B, C, BA, CB, CA, CBA), the three layer quirk, bottom-up rows. It is the reading OpenTPW's `ProceduralTerrainTexturer` follows;
* `parse_lnd` a strict parser of `base.lnd` for the tools (the game's own reader is `LndFile`).

A level is a dict: `params` (10 bytes), `width`, `height` (weight grid samples), `grid` (4 bytes per sample), `images` (13 tuples
(w, h, bytes per pixel, data) of the set the texture size N selects).
"""
from __future__ import annotations

import struct

SETS = {64: 0, 32: 1, 16: 2}
GREY = bytes([0, 0x7F, 0x7F, 0x7F])
MODES = ('C', 'B', 'A', 'BA', 'CB', 'CA', 'CBA')


def ramp(centre: int, width: int) -> bytes:
    half = (width + 1) >> 1
    low, high = centre - half, centre + half
    return bytes(0 if i < low else 255 if i > high else (255 * (i - low)) // (width + 1) for i in range(256))


def weights(level: dict, n_size: int, x: int, row: int, k: int) -> bytearray:
    """The N x N plane of weight k (1..3) of cell (x, row), row counted in the file's order."""
    n = n_size // 4
    s = n.bit_length() - 1
    grid, width = level['grid'], level['width']
    out = bytearray(n_size * n_size)
    for j in range(4):
        for i in range(4):
            p = ((4 * row + j) * width + 4 * x + i) * 4 + k
            v00, v10, v01, v11 = grid[p], grid[p + 4], grid[p + 4 * width], grid[p + 4 * width + 4]
            start = v00 << 16
            xs = (v10 - v00) << (16 - s)
            rs = (v01 - v00) << (16 - s)
            dd = ((v11 - v01) << (16 - s)) - xs
            for r in range(n):
                acc = start
                base = (j * n + r) * n_size + i * n
                for c in range(n):
                    out[base + c] = (acc >> 16) & 0xFF
                    acc += xs
                start += rs
                xs += dd >> s
    return out


def build(level: dict, n_size: int, x: int, z: int, quirk: bool = True) -> tuple[bytes, dict]:
    """The N x N texture of cell (x, z) as the build routine writes it ([0, R, G, B] per pixel, rows bottom-up) and the layer mode of
    every (row, run) the cell used. `quirk=False` is the reading in which a three layer run takes w1 per texel (the original does not)."""
    params, grid, width, height = level['params'], level['grid'], level['width'], level['height']
    cells_y = (height - 1) >> 2
    row = cells_y - z - 1
    q = ((4 * row + 1) * width + 4 * x + 1) * 4
    if (grid[q] & grid[q + 4] & grid[q + 4 * width] & grid[q + 4 * width + 4]) != 0xFF:
        return GREY * (n_size * n_size), {}
    t_a, t_b, t_m2 = ramp(params[0], params[1]), ramp(params[2], params[3]), ramp(params[4], params[5])
    t_c, t_m1 = ramp(params[6], params[7]), ramp(params[8], params[9])
    w1, w2, w3 = (weights(level, n_size, x, row, k) for k in (1, 2, 3))
    images = level['images']
    windows = [((n_size * x) % (w - n_size), (n_size * row) % (h - n_size)) for (w, h, _b, _d) in images]

    def colour(i, px, py):
        w, _h, b, data = images[i]
        ox, oy = windows[i]
        o = ((oy + py) * w + ox + px) * b
        return data[o + 1:o + 4]

    def byte(i, px, py):
        w, _h, _b, data = images[i]
        ox, oy = windows[i]
        return data[(oy + py) * w + ox + px]

    def lerp(a, b, t):
        return [(b[c] + (((a[c] - b[c]) * t) >> 8)) & 0xFF for c in range(3)]

    def layer(c0, c1, det, table, px, py, wv):
        return lerp(colour(c0, px, py), colour(c1, px, py), table[(byte(det, px, py) * wv) >> 8])

    out = bytearray(n_size * n_size * 4)
    modes = {}
    for py in range(n_size):
        orow = n_size - 1 - py
        for run in range(0, n_size, 16):
            texels = range(run, run + 16)
            m1 = [max(t_m1[(w3[py * n_size + px] * byte(12, px, py)) >> 8], t_m1[(byte(11, px, py) * w3[py * n_size + px]) >> 8]) for px in texels]
            m2 = []
            if all(v == 255 for v in m1):
                mode = 'C'
            else:
                m2 = [max(t_m2[(w2[py * n_size + px] * byte(10, px, py)) >> 8], t_m2[(byte(9, px, py) * w2[py * n_size + px]) >> 8]) for px in texels]
                all2, any2, any1 = all(v == 255 for v in m2), any(m2), any(m1)
                if not any1:
                    mode = 'B' if all2 else 'A' if not any2 else 'BA'
                else:
                    mode = 'CB' if all2 else 'CA' if not any2 else 'CBA'
            modes[(py, run)] = mode
            for lane, px in enumerate(texels):
                # 0x55eaa0 never advances its weight 1 pointer: a three layer run uses the weight 1 of its first texel
                wv = w1[py * n_size + (run if quirk and mode == 'CBA' else px)]
                if mode == 'C':
                    v = layer(4, 5, 8, t_c, px, py, wv)
                elif mode == 'B':
                    v = layer(2, 3, 7, t_b, px, py, wv)
                elif mode == 'A':
                    v = layer(0, 1, 6, t_a, px, py, wv)
                elif mode == 'BA':
                    v = lerp(layer(2, 3, 7, t_b, px, py, wv), layer(0, 1, 6, t_a, px, py, wv), m2[lane])
                elif mode == 'CB':
                    v = lerp(layer(4, 5, 8, t_c, px, py, wv), layer(2, 3, 7, t_b, px, py, wv), m1[lane])
                elif mode == 'CA':
                    v = lerp(layer(4, 5, 8, t_c, px, py, wv), layer(0, 1, 6, t_a, px, py, wv), m1[lane])
                else:
                    ba = lerp(layer(2, 3, 7, t_b, px, py, wv), layer(0, 1, 6, t_a, px, py, wv), m2[lane])
                    v = lerp(layer(4, 5, 8, t_c, px, py, wv), ba, m1[lane])
                o = (orow * n_size + px) * 4
                out[o] = 0
                out[o + 1:o + 4] = bytes(v)
    return bytes(out), modes


# ------------------------------------------------------------------ the composite tail (0x55f780 with a floor descriptor, issue 829, package F)
# The descriptor is {pixels, width, height, bytes per pixel, word}; the tail starts at 0x5608b0. The base buffer is the output of `build` (the rows bottom-up,
# [0, R, G, B] per pixel); the word's bits 0x78 pick the reading, tested in the order 0x40, 0x08, 0x10, 0x20.

def orientation(word: int, n: int) -> tuple[int, int, int]:
    """(start, texel step, row step) in pixels of the base buffer for the cell word."""
    if not word & 0x40:
        if word & 0x08:
            return n * n - n, -n, 1
        if word & 0x10:
            return n * n - 1, -1, -n
        if word & 0x20:
            return n - 1, n, -1
        return 0, 1, n
    if word & 0x08:
        return n * n - n, 1, -n
    if word & 0x10:
        return n * n - 1, -n, -1
    if word & 0x20:
        return n - 1, -1, n
    return 0, n, 1


def compose(base: bytes, n: int, art: bytes, width: int, height: int, bpp: int, word: int) -> bytes:
    """The N x N composite: the base read in the word's orientation with the art blended over it, point sampled at (row Y * (height / N), column X * (width / N)), integer divisions.
    bpp 4 reads the art dwords as A, R, G, B from bytes 3, 2, 1, 0; bpp 2 reads 4444 words (A = (v >> 8) & 0xF0, R = (v >> 4) & 0xF0, G = v & 0xF0, B = (v << 4) & 0xF0).
    Each channel is (byte)(base + (((art - base) * alpha) >> 8)); the output pixel is [0, R, G, B]."""
    start, texel_step, row_step = orientation(word, n)
    column_step, art_row_step = width // n, height // n
    out = bytearray()
    for y in range(n):
        for x in range(n):
            source = (start + y * row_step + x * texel_step) * 4
            if bpp == 4:
                offset = ((y * art_row_step) * width + x * column_step) * 4
                blue, green, red, alpha = art[offset], art[offset + 1], art[offset + 2], art[offset + 3]
            else:
                offset = ((y * art_row_step) * width + x * column_step) * 2
                value = art[offset] | art[offset + 1] << 8
                alpha, red, green, blue = (value >> 8) & 0xF0, (value >> 4) & 0xF0, value & 0xF0, (value << 4) & 0xFF
            out.append(0)
            for art_channel, base_channel in ((red, base[source + 1]), (green, base[source + 2]), (blue, base[source + 3])):
                out.append((base_channel + (((art_channel - base_channel) * alpha) >> 8)) & 0xFF)
    return bytes(out)


ALPHA_MODES = ('zero', 'full', 'mid', 'random', 'ramp')


def synthetic_art(seed: int, width: int, height: int, alpha_mode: str) -> bytes:
    """Art of `width` x `height` 32-bit pixels (bytes B, G, R, A per pixel) from the LCG: three colour bytes per pixel, the alpha by mode (zero, 255, 127, random, a ramp across the row)."""
    lcg = Lcg(seed)
    out = bytearray()
    for y in range(height):
        for x in range(width):
            blue, green, red = lcg.next(), lcg.next(), lcg.next()
            border = x < 3 or y < 3 or x >= width - 3 or y >= height - 3
            alpha = {'zero': 0, 'full': 255, 'mid': 127, 'ramp': (x * 255) // max(1, width - 1), 'path': 0 if border else 252}.get(alpha_mode)
            out += bytes([blue, green, red, lcg.next() if alpha is None else alpha])
    return bytes(out)


def synthetic_art_16(seed: int, width: int, height: int, alpha_mode: str) -> bytes:
    """The 4444 version: two bytes per pixel (little endian), the nibbles A, R, G, B from the top."""
    lcg = Lcg(seed)
    out = bytearray()
    for y in range(height):
        for x in range(width):
            red, green, blue = lcg.next() & 0xF, lcg.next() & 0xF, lcg.next() & 0xF
            alpha = {'zero': 0, 'full': 15, 'mid': 7, 'ramp': (x * 15) // max(1, width - 1)}.get(alpha_mode)
            alpha = lcg.next() & 0xF if alpha is None else alpha
            value = alpha << 12 | red << 8 | green << 4 | blue
            out += bytes([value & 0xFF, value >> 8])
    return bytes(out)


# the composite cases of the golden: (N, word, art size kind, alpha mode); the cell and the level come from the size index
COMPOSE_WORDS = (0x00, 0x08, 0x10, 0x20, 0x40, 0x48, 0x50, 0x60, 0x281, 0x2C8, 0x18, 0x30, 0x58, 0x78)
COMPOSE_ART_SIZES = ('n', 'double', 'odd', 'big', 'small')


def compose_art_size(kind: str, n: int) -> tuple[int, int]:
    return {'n': (n, n), 'double': (2 * n, 2 * n), 'odd': (n + 7, n + 13), 'big': (128, 128), 'small': (n // 2, n // 2)}[kind]


def compose_specs() -> list[tuple[int, int, int, int, str, int, int, str]]:
    """(level seed, N, cell x, cell z, art size kind, art seed, word, alpha mode) of every composite case: each size with every word, the art sizes and alpha modes in turn, on the 'C' mix
    levels of the build golden."""
    specs = []
    for size_index, n in enumerate((16, 32, 64)):
        seed = 1000 + 0 * 3 + size_index   # the 'C' mix level of this size
        for index, word in enumerate(COMPOSE_WORDS):
            kind = COMPOSE_ART_SIZES[(index + size_index) % len(COMPOSE_ART_SIZES)]
            alpha = ALPHA_MODES[(index * 2 + size_index) % len(ALPHA_MODES)]
            x, z = (index + size_index) % 3, (index // 2 + size_index) % 3
            specs.append((seed, n, x, z, kind, 7000 + 100 * size_index + index, word, alpha))
    return specs


# the words a path tile gives a cell (0x46df50: 0x180, one turn bit for 90, 180 and 270 degrees, the old bits 0x02, 0x04 and 0x200 kept) and the art of a path texture: 128 x 128, opaque (alpha 252) inside a transparent border
PATH_COMPOSE_WORDS = (0x180, 0x1A0, 0x190, 0x188, 0x182, 0x1A4, 0x196, 0x38A)


def path_compose_specs() -> list[tuple[int, int, int, int, str, int, int, str]]:
    """The composite cases of a path cell: each size with every path word, the 128 x 128 art with the 'path' alpha, other cells of the 'C' mix levels."""
    specs = []
    for size_index, n in enumerate((16, 32, 64)):
        seed = 1000 + size_index
        for index, word in enumerate(PATH_COMPOSE_WORDS):
            specs.append((seed, n, (index + 1) % 3, (index // 3 + size_index) % 3, 'big', 9000 + 100 * size_index + index, word, 'path'))
    return specs


def parse_lnd(raw: bytes) -> dict:
    """Strict `base.lnd` parser: version 3, parameters, weight grid, three sets of 13 images; the parse must end at the file size."""
    if raw[0] != 3:
        raise ValueError('base.lnd version is not 3')
    p = 11
    width, height, bpp = struct.unpack_from('<3I', raw, p)
    p += 12
    if bpp != 4:
        raise ValueError('weight grid is not four bytes per sample')
    grid = raw[p:p + width * height * 4]
    p += width * height * 4
    sets = []
    for index in range(3):
        size, = struct.unpack_from('<I', raw, p)
        if size != 64 >> index or raw[p + 4] != index:
            raise ValueError('image sets are not in the order 64, 32, 16')
        p += 5
        images = []
        for _ in range(13):
            w, h, b = struct.unpack_from('<3I', raw, p)
            p += 12
            images.append((w, h, b, raw[p:p + w * h * b]))
            p += w * h * b
        sets.append(images)
    if p != len(raw):
        raise ValueError(f'parse ends at {p}, the file has {len(raw)} bytes')
    return {'params': raw[1:11], 'width': width, 'height': height, 'grid': grid, 'sets': sets}


def level_for(lnd: dict, n_size: int) -> dict:
    return {'params': lnd['params'], 'width': lnd['width'], 'height': lnd['height'], 'grid': lnd['grid'], 'images': lnd['sets'][SETS[n_size]]}


# ---------------------------------------------------------------------------------------------------- reproducible synthetic levels
# Levels that C# (`SyntheticTerrain` in OpenTPW.Tests) and Python both build from the same formulas and one LCG, so that their texture hashes
# from tp.exe's routine can be committed and checked without game data (ProceduralTerrainSyntheticGolden.cs, terrain_synthetic_golden.py).
SYNTHETIC_MIXES = ('C', 'B', 'A', 'BA', 'CB', 'CA', 'CBA', 'random')
SYNTHETIC_MASKS = {'C': ('all', 'all'), 'B': ('none', 'all'), 'A': ('none', 'none'), 'BA': ('none', 'mixed'), 'CB': ('mixed', 'all'),
                   'CA': ('mixed', 'none'), 'CBA': ('mixed', 'mixed'), 'random': ('random', 'random')}   # (mask 1 style, mask 2 style)
SYNTHETIC_CELLS = 3          # 3 x 3 cells, 13 x 13 weight samples
GREY_SAMPLE = (5, 6)         # the sample whose validity byte a grey level breaks: an inner sample of cell (1, file row 1) = (1, z 1)


class Lcg:
    def __init__(self, seed: int):
        self.state = seed & 0xFFFFFFFF

    def next(self) -> int:
        """The next byte: the top byte of state * 1664525 + 1013904223 (mod 2^32)."""
        self.state = (self.state * 1664525 + 1013904223) & 0xFFFFFFFF
        return self.state >> 24


def synthetic_specs() -> list[tuple[int, str, int, bool]]:
    """(seed, mix, N, grey) of every synthetic level: each mix at each size, and an invalid-cell level per size."""
    specs = [(1000 + mix_index * 3 + size_index, mix, size, False)
             for mix_index, mix in enumerate(SYNTHETIC_MIXES) for size_index, size in enumerate((16, 32, 64))]
    return specs + [(2000 + size_index, 'random', size, True) for size_index, size in enumerate((16, 32, 64))]


def synthetic_cells(seed: int, grey: bool) -> list[tuple[int, int]]:
    """Three cells (x, z) of a synthetic level; a grey level's first cell is the invalid one. The last is the column after the grid's last cell (x = 3, z = 2: file row 0),
    which the original builds from the flat grid reads (the real heightfield is one column wider than the weight grid)."""
    if grey:
        return [(1, 1), (0, 0), (3, 2)]
    return [(seed % 3, (seed // 3) % 3), ((seed + 1) % 3, (seed // 3 + 1) % 3), (3, 2)]


def synthetic_level(seed: int, mix: str, n_size: int, grey: bool) -> dict:
    """The level of a spec: the ten parameter bytes from the LCG (mask ramps fixed to a step at 128 unless the mix is 'random'), a weight grid
    with gradient weights, and all three sets of 13 images from the LCG; the dict holds the set for `n_size`."""
    lcg = Lcg(seed)
    params = bytearray(lcg.next() for _ in range(10))
    if mix != 'random':
        params[4:6] = bytes([128, 0])
        params[8:10] = bytes([128, 0])
    mask1, mask2 = SYNTHETIC_MASKS[mix]
    side = 4 * SYNTHETIC_CELLS + 1

    def weight(style: str, x: int, y: int, k: int) -> int:
        if style == 'none':
            return 0
        if style == 'random':
            return (x * (53 if k == 2 else 17) + y * (29 if k == 2 else 61)) & 0xFF
        return 255

    grid = bytearray()
    for y in range(side):
        for x in range(side):
            valid = 0x7F if grey and (x, y) == GREY_SAMPLE else 255
            grid += bytes([valid, (x * 37 + y * 23 + seed) & 0xFF, weight(mask2, x, y, 2), weight(mask1, x, y, 3)])
    chosen = None
    for set_index in range(3):
        size = 64 >> set_index
        images = []
        for image in range(13):
            width, height = size + 1 + lcg.next() % 24, size + 1 + lcg.next() % 24
            style = {9: mask2, 10: mask2, 11: mask1, 12: mask1}.get(image)
            data = bytearray()
            for _ in range(width * height):
                if image < 6:
                    data += bytes([0, lcg.next(), lcg.next(), lcg.next()])
                elif style == 'all':
                    data.append(255)
                elif style == 'none':
                    data.append(0)
                elif style == 'mixed':
                    data.append(255 if lcg.next() & 1 else 0)
                else:
                    data.append(lcg.next())
            images.append((width, height, 4 if image < 6 else 1, bytes(data)))
        if size == n_size:
            chosen = images
    return {'params': bytes(params), 'width': side, 'height': side, 'grid': bytes(grid), 'images': chosen}
