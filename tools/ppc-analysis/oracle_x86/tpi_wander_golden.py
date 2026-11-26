"""Golden wanders of Theme Park Inc's persons (docs/reverse/TPI-NAVIGATOR.md, NV4): Game.exe's wander and patrol destination 0x609f70 with the
patrol rectangle test 0x62c610 and the fallback 0x62c720, on the map of tpi_navigator_golden.py at the game thread's control word 0x7f.

The map's path and queue cells link to their path and queue neighbours only (the navigator script's cells link every way), as a park's paths do.
Each case is one person (a guest, or a staff member of a kind with a patrol rectangle), a start point, the stamp counter, a stranded mark and the world
generator's state. Per case the script prints the answer, the generator's state after the draws, the counter, the mark, the walk mask, the thoughts
asked for and the SHA-256 of the navigator's 177-byte record, after checking walk_reading.py's statement (Wanderer.wander) against the run; these are
the C# rows of ThemeParkIncWanderTests.

The second part (the lead's NV3 review) runs Game.exe's route requests on the same map: 0x60b7f0 to a cell word, 0x60b8e0 to a point and the staff route
0x62eb10, with stranded marks held and stale (block stamps around the person's cell), and prints per request the answer, the counter, the mark, the walk
mask and the record's SHA-256: the rows that drive RequestRoute, RouteToTpiPoint and ThemeParkIncPatrol.StaffRoute.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_wander_golden.py   (needs Unicorn and OPENTPW_TPI_EXE)
"""
from __future__ import annotations

import hashlib
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import tpi_navigator_golden as G  # noqa: E402
import x86_targets as T  # noqa: E402
from x86_harness import Image, executable_path, have_unicorn  # noqa: E402

WHOLE = (0, 0, 0x7F, 0x7F)
# (name, kind, start x, y in 16.16, counter, mark, patrol rectangle (x0, y0, x1, y1), world generator state, park closed)
CASES = [
    ('guest', 1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 7, 0, WHOLE, 0x12345678, 0),
    ('guest-held', 1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 15, WHOLE, 0x0BADF00D, 0),
    ('guest-stale-mark', 1, (47 << 16) + 0x4000, (40 << 16) + 0xC000, 30, 50, WHOLE, 0x00C0FFEE, 0),
    ('guest-off-path', 1, (42 << 16) + 0x8000, (41 << 16) + 0x8000, 3, 0, WHOLE, 0x7FFFFFFF, 0),
    ('handyman', 5, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 11, 0, WHOLE, 0x2468ACE0, 0),
    ('gardener-area', 0x14, (45 << 16) + 0x8000, (40 << 16) + 0x8000, 11, 0, (45, 40, 50, 42), 0x13579BDF, 0),
    ('guest-junction', 1, (49 << 16) + 0x5000, (42 << 16) + 0xA000, 12, 0, WHOLE, 0x0D15EA5E, 0),
    ('mechanic-outside', 4, (41 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (50, 44, 55, 49), 0x0F1E2D3C, 0),
    ('mechanic-outside-reversed', 4, (41 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (54, 49, 50, 45), 0x0F1E2D3C, 0),
    ('guard-reversed-inside', 7, (42 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (44, 40, 41, 40), 0x55555555, 0),
    ('entertainer-reversed-outside', 6, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 4, 0, (44, 40, 41, 40), 0x31415926, 0),
    ('researcher-one-cell', 8, (40 << 16) + 0x8000, (40 << 16) + 0x8000, 9, 0, (40, 40, 40, 40), 0x27182818, 0),
    ('guard-off-path', 7, (42 << 16) + 0x8000, (41 << 16) + 0x8000, 2, 0, WHOLE, 0x1F2E3D4C, 0),
    ('handyman-closed', 5, (52 << 16) + 0x8000, (48 << 16) + 0x8000, 6, 0, WHOLE, 0x600DCAFE, 1),
]


def linked_cells() -> dict:
    """The navigator script's cells with each path or queue cell's links (+0xc) set towards its path and queue neighbours only."""
    x = G.cells()
    kinds = {(cx, cy): kind for cx, cy, kind in x['types']}
    links = []
    for (cx, cy), kind in sorted(kinds.items()):
        if kind in (1, 3):
            value = 0
            for bit, (dx, dy) in ((1, (0, -1)), (4, (1, 0)), (0x10, (0, 1)), (0x40, (-1, 0))):
                if kinds.get((cx + dx, cy + dy)) in (1, 3):
                    value |= bit
            links.append([cx, cy, value])
    x['links'] = links
    return x


def case_input(case) -> dict:
    _, kind, x0, y0, counter, mark, rect, rng, closed = case
    x = linked_cells()
    x['walk'] = {'x': x0, 'y': y0, 'vx': 0, 'vy': 0, 'mask': 0, 'max_speed': 13107, 'max_force': 26214}
    x['person'] = {'kind': kind, 'mark': mark}
    x['counter'] = counter
    x['stamps'] = []
    x['behind'] = []
    x['rect'] = list(rect)
    x['rng'] = rng
    x['closed'] = closed
    return x


# (name, kind, start x, y in 16.16, counter, mark, block stamps [(bx, by, stamp)], park closed, requests: ('cell', x, y) | ('point', x word, y word) | ('staff', x word, y word))
REQUESTS = [
    ('guest-cell', 1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 5, 0, [], 0, [('cell', 49, 44), ('cell', 42, 41), ('cell', 55, 48)]),
    ('guest-point', 1, (49 << 16) + 0x5000, (42 << 16) + 0xA000, 40, 0, [], 0, [('point', (54 << 8) | 0x33, (48 << 8) | 0xC0), ('point', (40 << 8) | 0x05, (49 << 8) | 0x7B)]),
    ('guest-held', 1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 15, [(2, 2, 9)], 0, [('cell', 49, 44), ('point', (44 << 8) | 0x80, (46 << 8) | 0x80)]),
    ('guest-released', 1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 15, [(2, 2, 17)], 0, [('cell', 49, 44)]),
    ('guest-stale', 1, (41 << 16) + 0x6000, (40 << 16) + 0x9000, 20, 90, [], 0, [('point', (52 << 8) | 0x40, (41 << 8) | 0x40)]),
    ('staff-open', 5, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 3, 0, [], 0, [('staff', (55 << 8) | 0x80, (49 << 8) | 0x80), ('staff', (42 << 8) | 0x80, (41 << 8) | 0x80)]),
    ('staff-closed', 7, (47 << 16) + 0x8000, (40 << 16) + 0x8000, 3, 0, [], 1, [('staff', (0x2F << 8) | 0x80, (0x11 << 8) | 0x80), ('staff', (0x30 << 8) | 0x10, (0x11 << 8) | 0xF0), ('staff', (44 << 8) | 0x80, (46 << 8) | 0x80)]),
]


def request_input(case) -> dict:
    _, kind, x0, y0, counter, mark, stamps, closed, requests = case
    x = linked_cells()
    x['walk'] = {'x': x0, 'y': y0, 'vx': 0, 'vy': 0, 'mask': 0, 'max_speed': 13107, 'max_force': 26214}
    x['person'] = {'kind': kind, 'mark': mark}
    x['counter'] = counter
    x['stamps'] = [list(stamp) for stamp in stamps]
    x['behind'] = []
    x['closed'] = closed
    x['requests'] = [('cell', b * 128 + a + 1) if kind == 'cell' else (kind, a, b) for kind, a, b in requests]
    return x


def main() -> int:
    if not have_unicorn() or executable_path('tpi') is None:
        print('needs Unicorn and OPENTPW_TPI_EXE', file=sys.stderr)
        return 2
    image = Image.load(executable_path('tpi'))
    target = T.TPI_WANDER_TARGET
    for case in CASES:
        x = case_input(case)
        sb = target.sandbox(image)
        result = T.run_binary(target, sb, x)
        if result != T.tpi_wander_plan(x):
            print(f'{case[0]}: walk_reading.py disagrees with Game.exe', file=sys.stderr)
            return 1
        answer, rng, thoughts, counter, mark, _, _, _, mask, record = result
        digest = hashlib.sha256(record).hexdigest()
        print(f'\tnew( "{case[0]}", {answer}, 0x{rng:08X}, {counter}, {mark}, {mask}, "{",".join(f"{t:x}" for _, t in thoughts)}", "{digest}" ),')
    print()
    target = T.TPI_ROUTE_TARGET
    for case in REQUESTS:
        x = request_input(case)
        sb = target.sandbox(image)
        trace = T.run_binary(target, sb, x)
        if trace != T.tpi_route_plan(x):
            print(f'{case[0]}: walk_reading.py disagrees with Game.exe', file=sys.stderr)
            return 1
        rows = [f'{answer}:{counter}:{mark}:{mask}:{hashlib.sha256(record).hexdigest()[:16]}' for answer, counter, mark, _, _, mask, record in trace]
        print(f'\tnew( "{case[0]}", "{",".join(rows)}" ),')
    return 0


if __name__ == '__main__':
    sys.exit(main())
