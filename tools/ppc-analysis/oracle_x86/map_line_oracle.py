"""tp.exe's 32 bit anti-aliased map line 0x5EFB10 run in the x86 oracle sandbox (docs/reverse/ORACLE.md; the map's track layer 0x5F2FD0).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The routine runs unpatched and unhooked on a synthetic
128 x 128 destination (pitch 512) and a synthetic surface description; the sandbox only watches the destination buffer's writes (a passive memory hook) and returns, per call, every pixel the routine wrote
in its own order with the coverage byte it blended with.

The routine is 0x5EFB10(arg1 scratch, arg2 surface description, arg3 clip rectangle, arg4 colour record, arg5 start point, arg6 end point), cdecl, the caller cleans 0x18:

* surface description (the map object's [map + 0x2C] block): +0x00 / +0x04 / +0x08 the three channel masks, +0x0C the fourth mask (kept from the old pixel), +0x10 bits per pixel, +0x14 a flag word,
  +0x18 the buffer pointer, +0x24 the pitch in bytes;
* clip rectangle: int left, top, right, bottom (a pixel is drawn when left <= x < right and top <= y < bottom);
* colour record: a dword 0x00RRGGBB that 0x5FD050 turns into the surface's pixel value (arbitrary formats go through the generic shift path; the executable's static format caches stay zero here);
* start and end point: two ints each (x, y).

Two ways to read the coverage byte of every write (`isolated`):

* passive (default): the writes arrive in order and every write's old dword is read at the write; a white line over black gives the coverage byte directly for a pixel's first write, and later writes of the same
  pixel are checked against the blend formula (several coverage bytes can give one blended value there);
* isolated: a memory hook zeroes each written pixel right before the routine's next pixel read (the destination framebuffer only; no code, register or routine data is touched), so every write blends over
  black and the coverage byte is read exactly.
"""
from __future__ import annotations

import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

try:  # dev-only dependency, like x86_harness: the module still imports without it
    import unicorn
except ImportError:  # pragma: no cover
    unicorn = None

from x86_harness import Image, Sandbox, executable_path, have_unicorn, seeded  # noqa: E402

ENTRY = 0x5EFB10
CONVERT = 0x5FD050
CODE = [(0x401000, 0x6FD000)]
WIDTH = HEIGHT = 128
PITCH = 512
CONTROL_WORD = 0x7F      # the game thread's word (24 bit significand), see docs/reverse/ORACLE.md X40; the oracle can run any other
MASKS = (0x00FF0000, 0x0000FF00, 0x000000FF, 0)

ONE = 1.0
SCALE = 255.0


def f32(value: float) -> float:
    return struct.unpack('<f', struct.pack('<f', value))[0]


def blend_channel(src: int, dst: int, coverage: int, mask: int) -> int:
    """One channel the way the routine does it on the masked, unshifted values (all 32 bit)."""
    v = ((src & mask) * coverage + (dst & mask) * (255 - coverage)) & 0xFFFFFFFF
    return ((((v >> 8) + v + 1) & 0xFFFFFFFF) >> 8) & mask


def blend(src: int, dst: int, coverage: int, masks=MASKS) -> int:
    out = 0
    for mask in masks[:3]:
        out |= blend_channel(src, dst, coverage, mask)
    return (out | (dst & masks[3])) & 0xFFFFFFFF


class MapLineOracle:
    """One sandbox over tp.exe; `line` runs the real 0x5EFB10 on a synthetic surface."""

    def __init__(self, exe: Path | None = None, budget: int = 400_000):
        if not have_unicorn():
            raise RuntimeError('needs Unicorn')
        path = exe or executable_path('tpw')
        if path is None:
            raise RuntimeError('OPENTPW_TPW_EXE is not set')
        self.image = Image.load(path)
        self.sandbox = Sandbox(self.image, CODE, budget=budget)

    def _prepare(self, p0, p1, clip, colour, background, masks, bpp, flag, control_word):
        """Reset the sandbox and lay the synthetic surface out; the heap is pristine after reset(), so every call allocates at the same addresses."""
        sb = self.sandbox
        sb.reset()
        sb.set_fpcw(control_word)
        buf = sb.alloc(PITCH * HEIGHT + 64)
        before = bytearray(PITCH * HEIGHT)
        for y in range(HEIGHT):
            for x in range(WIDTH):
                value = background(x, y) if background else 0
                struct.pack_into('<I', before, y * PITCH + x * 4, value & 0xFFFFFFFF)
        sb.uc.mem_write(buf, bytes(before))
        surface = sb.alloc(0x40)
        for offset, value in ((0x0, masks[0]), (0x4, masks[1]), (0x8, masks[2]), (0xC, masks[3]), (0x10, bpp), (0x14, flag), (0x18, buf), (0x1C, WIDTH), (0x20, HEIGHT), (0x24, PITCH)):
            sb.write(surface + offset, 'I', value & 0xFFFFFFFF)
        clip_ptr = sb.alloc(16)
        sb.uc.mem_write(clip_ptr, struct.pack('<iiii', *clip))
        colour_ptr = sb.alloc(16)
        sb.write(colour_ptr, 'I', colour & 0xFFFFFFFF)
        a_ptr = sb.alloc(8)
        b_ptr = sb.alloc(8)
        sb.uc.mem_write(a_ptr, struct.pack('<ii', *p0))
        sb.uc.mem_write(b_ptr, struct.pack('<ii', *p1))
        scratch = sb.alloc(16)
        return buf, bytes(before), surface, clip_ptr, colour_ptr, a_ptr, b_ptr, scratch

    def line(self, p0, p1, clip=(0, 0, WIDTH, HEIGHT), colour=0xFFFFFF, background=None, masks=MASKS, bpp=32, flag=0,
             control_word=CONTROL_WORD, isolated=False) -> dict:
        """Run one segment. `background` is a function (x, y) -> dword (default black). Returns the destination before and after, the colour
        record's pixel value (0x5FD050 run on its own first), and the write list [(x, y, old, new)] in the routine's order."""
        sb = self.sandbox
        args = (p0, p1, clip, colour, background, masks, bpp, flag, control_word)
        buf, before, surface, clip_ptr, colour_ptr, a_ptr, b_ptr, scratch = self._prepare(*args)
        pixel_value = sb.call(CONVERT, (surface,), this=colour_ptr).eax
        buf, before, surface, clip_ptr, colour_ptr, a_ptr, b_ptr, scratch = self._prepare(*args)

        events: list[tuple[int, int]] = []     # (address, byte) of every destination write, in order
        pending: list[int] = []                # pixel addresses to zero before the next read (isolated mode)
        end = buf + PITCH * HEIGHT

        def on_write(uc, access, address, size, value, user):
            for k in range(size):
                events.append((address + k, (value >> (8 * k)) & 0xFF))
            if isolated and len(events) % 4 == 0:
                pending.append(address - 3)

        def on_read(uc, access, address, size, value, user):
            while pending:
                uc.mem_write(pending.pop(), b'\0\0\0\0')

        handles = [sb.uc.hook_add(unicorn.UC_HOOK_MEM_WRITE, on_write, None, buf, end - 1)]
        if isolated:
            handles.append(sb.uc.hook_add(unicorn.UC_HOOK_MEM_READ, on_read, None, buf, end - 1))
        try:
            result = sb.call(ENTRY, (scratch, surface, clip_ptr, colour_ptr, a_ptr, b_ptr))
        finally:
            for handle in handles:
                sb.uc.hook_del(handle)
        after = bytes(sb.uc.mem_read(buf, PITCH * HEIGHT))
        if len(events) % 4:
            raise RuntimeError('a destination write that is not a whole pixel')
        writes = []
        current = bytearray(before)
        for k in range(0, len(events), 4):
            group = events[k:k + 4]
            base = group[0][0]
            if [a for a, _ in group] != [base + i for i in range(4)] or (base - buf) % 4:
                raise RuntimeError(f'unexpected write pattern {group}')
            offset = base - buf
            y, rem = divmod(offset, PITCH)
            x = rem // 4
            old = struct.unpack_from('<I', current, offset)[0]
            new = struct.unpack('<I', bytes(b for _, b in group))[0]
            writes.append((x, y, old, new))
            struct.pack_into('<I', current, offset, 0 if isolated else new)
        return {'writes': writes, 'before': before, 'after': after, 'pixel': pixel_value, 'esp_delta': result.esp_delta, 'executed': result.executed}


_WHITE_OVER_BLACK = {blend(0xFFFFFF, 0, i): i for i in range(256)}
assert len(_WHITE_OVER_BLACK) == 256   # the blend of white over black is injective in the coverage byte


def coverage_from_isolated(writes):
    """White over black: the blended dword of a write identifies the coverage byte (the blend is injective); an unknown dword raises."""
    out = []
    for x, y, old, new in writes:
        if old != 0 or new not in _WHITE_OVER_BLACK:
            raise RuntimeError(f'({x}, {y}) wrote {new:#x} over {old:#x}: not a white blend over black')
        out.append((x, y, _WHITE_OVER_BLACK[new]))
    return out


def consistent_coverages(old: int, new: int, src: int, masks=MASKS) -> list[int]:
    """The coverage bytes that blend `src` over `old` to `new`."""
    return [i for i in range(256) if blend(src, old, i, masks) == new]


# ---------------------------------------------------------------------------------------------------------------- the segment generator shared by the checks and the golden file

def random_segments(count: int, seed: str = 'map-line', span: int = 40, clip_cases: bool = True):
    """Mixed segments: all octants, horizontal, vertical, diagonal, zero length, reversed, off-clip starts and ends; the clip is inside the 128 x 128 buffer."""
    rng = seeded(seed)
    cases = []
    full = (0, 0, WIDTH, HEIGHT)
    while len(cases) < count:
        kind = len(cases) % 12
        cx, cy = rng.randint(30, 98), rng.randint(30, 98)
        if kind == 0:
            p0, p1 = (cx, cy), (cx + rng.randint(-span, span), cy)
        elif kind == 1:
            p0, p1 = (cx, cy), (cx, cy + rng.randint(-span, span))
        elif kind == 2:
            n = rng.randint(1, span)
            sx, sy = rng.choice((1, -1)), rng.choice((1, -1))
            p0, p1 = (cx, cy), (cx + sx * n, cy + sy * n)
        elif kind == 3:
            p0 = (cx, cy)
            p1 = p0 if rng.random() < 0.6 else (cx + rng.randint(-1, 1), cy + rng.randint(-1, 1))
        else:
            p0 = (cx, cy)
            p1 = (cx + rng.randint(-span, span), cy + rng.randint(-span, span))
        if rng.random() < 0.3:
            p0, p1 = p1, p0
        clip = full
        if clip_cases and kind in (4, 5, 6, 7, 8, 9):
            left, top = rng.randint(0, 60), rng.randint(0, 60)
            clip = (left, top, rng.randint(left + 1, WIDTH), rng.randint(top + 1, HEIGHT))
        if clip_cases and kind in (10, 11):
            # endpoints outside the clip, and a clip that cuts the line in the middle
            clip = (cx - rng.randint(2, 12), cy - rng.randint(2, 12), cx + rng.randint(2, 12), cy + rng.randint(2, 12))
            clip = (max(0, clip[0]), max(0, clip[1]), min(WIDTH, clip[2]), min(HEIGHT, clip[3]))
        # keep every point a little away from the buffer edge so that the unclipped pixel pointers stay inside the heap
        if all(-20 <= v <= WIDTH + 20 for v in (*p0, *p1)):
            cases.append((p0, p1, clip))
    return cases


# ---------------------------------------------------------------------------------------------------------------- the pure Python reading

def coverage(x0: int, y0: int, x1: int, y1: int, clip, precision: int = 24):
    """The writes of 0x5EFB10 in the routine's order: [(x, y, coverage byte)], including the coverage 0 writes (distance exactly 1.0), which leave the pixel as it was.

    Every x87 result is rounded to `precision` significand bits (24: the game thread's word 0x7F; 53 or 64 for the other words, where 64 is rounded as a double here). The integer
    inputs (fild) are exact; the fistp truncates.
    """
    left, top, right, bottom = clip
    if precision == 24:
        r = f32
    else:
        def r(value):
            return value
    dx, dy = x1 - x0, y1 - y0
    adx, ady = abs(dx), abs(dy)
    sx = 1 if dx >= 0 else -1
    sy = 1 if dy >= 0 else -1
    out = []

    def draw(x, y, distance):
        if left <= x < right and top <= y < bottom and distance <= ONE:
            out.append((x, y, int(r(r(ONE - distance) * SCALE))))

    if adx >= ady:
        # x major: the start is the point with the smaller x; a reversed segment is walked from its end with the y sign flipped
        if dx < 0:
            x0, x1, y0 = x1, x0, y1
            dy, sx, sy = -dy, -sx, -sy
        major = adx
        if x0 == x1:
            return out
        a_step, b_step = 2 * ady, 2 * (ady - major)
        d = a_step - major
        length = r(math.sqrt(dx * dx + dy * dy))
        inv = r(ONE / (2.0 * length))
        k = 2.0 * r(major * inv)
        u, x, y = 0, x0, y0
        while True:
            t = r(u * inv)
            draw(x, y, abs(t))
            draw(x, y + sy, abs(r(k - t)))
            draw(x, y - sy, abs(r(k + t)))
            if d < 0:
                u, d = major + d, d + a_step
            else:
                u, d, y = d - major, d + b_step, y + sy
            x += 1
            if x == x1:
                return out
    # y major: the start is the point with the smaller y
    if dy < 0:
        x0, y0, y1 = x1, y1, y0
        dx, sx, sy = -dx, -sx, -sy
    major = ady
    a_step, b_step = 2 * adx, 2 * (adx - major)
    d = a_step - major
    length = r(math.sqrt(dx * dx + dy * dy))
    inv = r(ONE / (2.0 * length))
    k = 2.0 * r(major * inv)
    u, x, y = 0, x0, y0
    while True:
        t = r(u * inv)
        draw(x, y, abs(t))
        draw(x + sx, y, abs(r(k - t)))
        draw(x - sx, y, abs(r(k + t)))
        if d < 0:
            u, d = major + d, d + a_step
        else:
            u, d, x = d - major, d + b_step, x + sx
        y += 1
        if y == y1:
            return out


def check_blend(oracle: MapLineOracle, cases, seed: str = 'map-line-blend') -> tuple[int, int, int]:
    """Coloured lines over a random background, the unmodified run (passive hook): every write must be the blend formula applied by the model's coverage byte to the pixel's old dword, and the final
    buffer must equal the model's replay. Returns (segments, writes, mismatches). Surfaces with and without a fourth (alpha) mask are used; the colour record is converted by 0x5FD050 itself."""
    rng = seeded(seed)
    bad = writes = 0
    for index, (p0, p1, clip) in enumerate(cases):
        masks = MASKS if index % 2 == 0 else (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000)
        pattern = [rng.getrandbits(32) for _ in range(64)]
        colour = rng.getrandbits(24)
        r = oracle.line(p0, p1, clip, colour=colour, background=lambda x, y: pattern[(x * 7 + y * 13) % 64], masks=masks)
        model = coverage(*p0, *p1, clip)
        got = r['writes']
        writes += len(got)
        if [(x, y) for x, y, _, _ in got] != [(x, y) for x, y, _ in model]:
            bad += 1
            continue
        replay = bytearray(r['before'])
        ok = True
        for (x, y, old, new), (_, _, i) in zip(got, model):
            expected = blend(r['pixel'], old, i, masks)
            ok &= expected == new
            struct.pack_into('<I', replay, y * PITCH + x * 4, expected)
        if not ok or bytes(replay) != r['after']:
            bad += 1
    return len(cases), writes, bad


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--cases', type=int, default=400)
    parser.add_argument('--seed', default='map-line')
    parser.add_argument('--control-word', type=lambda v: int(v, 0), default=CONTROL_WORD)
    arguments = parser.parse_args()
    oracle = MapLineOracle()
    cases = random_segments(arguments.cases, arguments.seed)
    precision = 24 if arguments.control_word & 0x300 == 0 else 53
    bad = 0
    writes_total = 0
    for p0, p1, clip in cases:
        got = coverage_from_isolated(oracle.line(p0, p1, clip, isolated=True, control_word=arguments.control_word)['writes'])
        want = coverage(*p0, *p1, clip, precision)
        writes_total += len(got)
        if got != want:
            bad += 1
            if bad <= 5:
                print('MISMATCH', p0, p1, clip, got[:6], want[:6], len(got), len(want))
    print(f'coverage (white over black, isolated writes): {len(cases)} segments, {writes_total} writes, {bad} mismatches (control word {arguments.control_word:#x})')
    if arguments.control_word == CONTROL_WORD:
        segments, writes, blend_bad = check_blend(oracle, cases[:max(60, arguments.cases // 4)])
        print(f'blend (coloured over a random background, passive writes): {segments} segments, {writes} writes, {blend_bad} mismatches')
        bad += blend_bad
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
