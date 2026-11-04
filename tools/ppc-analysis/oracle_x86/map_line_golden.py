"""Golden cases of the park map's anti-aliased line (docs/reverse/ORACLE.md; the map's track layer 0x5F2FD0): tp.exe's own 32 bit line routine 0x5EFB10 run in the x86 oracle.

Every case is a segment and a clip rectangle on a synthetic 128 x 128 surface; the oracle runs the unmodified routine with a white line over a black destination (each written pixel's blended dword identifies its
coverage byte) and records every pixel it writes, in its own order, as x,y,coverage. A second table has blend rows: the routine's own blend of a colour over a random old pixel for one coverage byte
(the surface masks 0x00FF0000, 0x0000FF00, 0x000000FF and an alpha mask of 0 or 0xFF000000). OpenTPW.Tests replays every case on ParkMapLine.Coverage / ParkMapLine.Blend and must reproduce them exactly.
No original data enters: segments, colours and backgrounds are generated here.

Usage: python3 -I tools/ppc-analysis/oracle_x86/map_line_golden.py > source/OpenTPW.Tests/MapLineGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares instead of printing)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from map_line_oracle import (HEIGHT, MASKS, WIDTH, MapLineOracle, coverage, coverage_from_isolated,  # noqa: E402
                             random_segments)
from x86_harness import seeded  # noqa: E402

FULL = (0, 0, WIDTH, HEIGHT)

CURATED = [
    ('horizontal east', (20, 40), (60, 40), FULL),
    ('horizontal west', (60, 40), (20, 40), FULL),
    ('vertical south', (40, 20), (40, 60), FULL),
    ('vertical north', (40, 60), (40, 20), FULL),
    ('diagonal south east', (20, 20), (50, 50), FULL),
    ('diagonal north west', (50, 50), (20, 20), FULL),
    ('diagonal south west', (50, 20), (20, 50), FULL),
    ('diagonal north east', (20, 50), (50, 20), FULL),
    ('zero length', (30, 30), (30, 30), FULL),
    ('one pixel east', (30, 30), (31, 30), FULL),
    ('one pixel south', (30, 30), (30, 31), FULL),
    ('one pixel diagonal', (30, 30), (31, 31), FULL),
    ('shallow east south', (10, 10), (70, 22), FULL),
    ('shallow east north', (10, 40), (70, 28), FULL),
    ('shallow west south', (70, 10), (10, 22), FULL),
    ('shallow west north', (70, 40), (10, 28), FULL),
    ('steep south east', (10, 10), (22, 70), FULL),
    ('steep south west', (40, 10), (28, 70), FULL),
    ('steep north east', (10, 70), (22, 10), FULL),
    ('steep north west', (40, 70), (28, 10), FULL),
    ('slope 1 to 3', (12, 12), (48, 24), FULL),
    ('slope 3 to 1', (12, 12), (24, 48), FULL),
    ('slope 2 to 5', (12, 12), (37, 22), FULL),
    ('long shallow', (2, 5), (125, 31), FULL),
    ('long steep', (6, 2), (31, 125), FULL),
    ('start outside the clip', (4, 30), (60, 34), (20, 0, WIDTH, HEIGHT)),
    ('end outside the clip', (30, 30), (110, 60), (0, 0, 70, HEIGHT)),
    ('both ends outside the clip', (5, 40), (120, 44), (30, 20, 90, 70)),
    ('clip cuts a steep line', (50, 5), (62, 120), (0, 30, WIDTH, 80)),
    ('clip of one pixel width', (20, 20), (60, 40), (30, 0, 31, HEIGHT)),
    ('empty clip', (20, 20), (60, 40), (50, 50, 50, 60)),
    ('line outside the clip', (80, 80), (100, 90), (0, 0, 40, 40)),
    ('negative start', (-12, 30), (40, 38), FULL),
    ('past the right edge', (80, 30), (140, 52), FULL),
    ('above the top', (30, -15), (44, 50), FULL),
    ('the surface corner', (0, 0), (127, 127), FULL),
    ('the bottom row', (0, 127), (127, 127), FULL),
    ('the right column', (127, 0), (127, 127), FULL),
]

RANDOM_CASES = 70
BLEND_RUNS = 12


def cases(oracle: MapLineOracle):
    out = []
    for name, p0, p1, clip in CURATED:
        out.append((name, p0, p1, clip))
    for index, (p0, p1, clip) in enumerate(random_segments(RANDOM_CASES, 'map-line-golden', span=48)):
        out.append((f'random {index}', p0, p1, clip))
    result = []
    for name, p0, p1, clip in out:
        writes = coverage_from_isolated(oracle.line(p0, p1, clip, isolated=True)['writes'])
        if writes != coverage(*p0, *p1, clip):
            raise SystemExit(f'the Python reading differs from the oracle on {name}: run map_line_oracle.py')
        result.append((name, p0, p1, clip, writes))
    return result


def blends(oracle: MapLineOracle):
    """Rows (source pixel, old pixel, coverage, alpha mask, new pixel) from the routine's own writes of coloured lines over random backgrounds (the passive run; the coverage is the Python reading's, which the
    isolated runs confirm, and each row's new dword is the executable's)."""
    rng = seeded('map-line-golden-blend')
    rows = []
    seen = set()
    for run in range(BLEND_RUNS):
        masks = MASKS if run % 2 == 0 else (MASKS[0], MASKS[1], MASKS[2], 0xFF000000)
        pattern = [rng.getrandbits(32) for _ in range(64)]
        colour = rng.getrandbits(24)
        p0 = (rng.randint(20, 100), rng.randint(20, 100))
        p1 = (p0[0] + rng.randint(-30, 30), p0[1] + rng.randint(-30, 30))
        result = oracle.line(p0, p1, FULL, colour=colour, background=lambda x, y: pattern[(x * 7 + y * 13) % 64], masks=masks)
        model = coverage(*p0, *p1, FULL)
        picked = 0
        for (x, y, old, new), (_, _, i) in zip(result['writes'], model):
            key = (old, i)
            if key in seen or picked >= 6 and i not in (0, 255):
                continue
            seen.add(key)
            picked += 1
            rows.append((result['pixel'], old, i, masks[3], new))
    # the extremes on a one step horizontal line: coverage 255 for the walk's pixel and 0 for both neighbours (exactly 1.0 away), over black, white and grey
    for old in (0x00000000, 0x00FFFFFF, 0x00808080):
        result = oracle.line((20, 20), (21, 20), FULL, colour=0xFFFFFF, background=lambda x, y: old)
        for (x, y, before, new), (_, _, i) in zip(result['writes'], coverage(20, 20, 21, 20, FULL)):
            rows.append((result['pixel'], before, i, 0, new))
    return rows


def render() -> str:
    oracle = MapLineOracle()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/map_line_golden.py from tp.exe\'s own map line routine 0x5EFB10 run in the x86 oracle (docs/reverse/ORACLE.md).',
           '// Synthetic segments, clips, colours and backgrounds, no original data. Writes: every pixel the routine wrote, in its order, as "x,y,coverage" separated by spaces.',
           'namespace OpenTPW.Tests;', '', 'internal static class MapLineGolden', '{',
           '\t/// <summary>A segment, a clip rectangle (left, top, right, bottom) and the pixel writes the executable made.</summary>',
           '\tinternal readonly record struct Case( string Name, int X0, int Y0, int X1, int Y1, int ClipLeft, int ClipTop, int ClipRight, int ClipBottom, string Writes );', '',
           '\t/// <summary>One blend the executable made: the colour as a pixel value, the old dword, the coverage byte, the fourth (alpha) mask of the surface and the dword it wrote.</summary>',
           '\tinternal readonly record struct Blend( uint Source, uint Old, int Coverage, uint AlphaMask, uint New );', '',
           '\tpublic static readonly Case[] Cases =', '\t[']
    for name, p0, p1, clip, writes in cases(oracle):
        text = ' '.join(f'{x},{y},{i}' for x, y, i in writes)
        out.append(f'\t\tnew( "{name}", {p0[0]}, {p0[1]}, {p1[0]}, {p1[1]}, {clip[0]}, {clip[1]}, {clip[2]}, {clip[3]}, "{text}" ),')
    out += ['\t];', '', '\tpublic static readonly Blend[] Blends =', '\t[']
    for source, old, i, alpha, new in blends(oracle):
        out.append(f'\t\tnew( 0x{source:08X}u, 0x{old:08X}u, {i}, 0x{alpha:08X}u, 0x{new:08X}u ),')
    out += ['\t];', '}', '']
    return '\n'.join(out)


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        expected = Path(sys.argv[2]).read_text()
        if render() != expected:
            print('MapLineGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())
