"""Golden walks of Theme Park Inc's navigator (docs/reverse/TPI-NAVIGATOR.md, NV2): Game.exe's constructor 0x63ed10, set-up 0x63e3a0 and steering
step 0x63dd20 on one synthetic map at the game thread's control word 0x7f, and its serializer 0x63ad30 on every record they leave.

Three walks over the map below (paths, a queue, walkways with and without the obstacle flag, a hole, unowned cells): after the set-up and after
each steering step the navigator's 177-byte record (every member, in the serializer's order) is printed as hex. Each record also goes through the
serializer's load half into a navigator filled with 0xab and back out through its save half, which must give the same bytes. The script checks
walk_reading.py's statement against the run and prints the C# rows of ThemeParkIncNavigatorTests: per walk the step results, the SHA-256 of all
its records with path_timestamp (+0x48, record bytes 85..88) zeroed, and the full records after the set-up and after every 20th step and the last.

Usage: python3 -I tools/ppc-analysis/oracle_x86/tpi_navigator_golden.py   (needs Unicorn and OPENTPW_TPI_EXE)
"""
from __future__ import annotations

import hashlib
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import walk_reading  # noqa: E402
import x86_targets as T  # noqa: E402
from x86_harness import Image, executable_path, have_unicorn  # noqa: E402

ORIGIN = (40, 40)
# '#' path (links 0x55), 'q' queue (links 0x55), '~' walkway 0x1e, 'O' walkway with the obstacle flag, 'H' type 8; 'u' in UNOWNED marks flag 0x40
ROWS = ["################",
        "#....~O..#.....#",
        "#.##.#####.###.#",
        "#.#H.#...#.#...#",
        "#.#..q...#.#.~.#",
        "#.####.###.#.#.#",
        "#......#...#.#.#",
        "######.#.###.#.#",
        "#....#...#...#.#",
        "################"]
UNOWNED = ["................",
           "...u......u.....",
           "....u...........",
           "....u.......u...",
           "......u.........",
           "................",
           ".u.....u........",
           "........u.......",
           "...........u....",
           "................"]
# (start x, y in 16.16, velocity, mask, caps, target x, y, steps)
WALKS = [((41 << 16) + 0x6000, (41 << 16) + 0x9000, 1200, -800, 0, 13107, 26214, (54 << 16) + 0x5000, (48 << 16) + 0xA000, 140),
         ((46 << 16) + 0x8000, (46 << 16) + 0x4000, 0, 0, 1, 7733, 15466, (44 << 16) + 0x2000, (41 << 16) + 0x7000, 60),
         ((44 << 16) + 0x3000, (44 << 16) + 0xC000, -3000, 2500, 0, 26214, 52428, (53 << 16) + 0x8000, (43 << 16) + 0x8000, 70)]


def cells() -> dict:
    types, links, flags, obstacles = [], [], [], []
    for y, row in enumerate(ROWS):
        for x, symbol in enumerate(row):
            cx, cy = ORIGIN[0] + x, ORIGIN[1] + y
            kind = {'#': 1, 'q': 3, '~': 0x1E, 'O': 0x1E, 'H': 8}.get(symbol, 0)
            if kind:
                types.append([cx, cy, kind])
            if symbol in '#q':
                links.append([cx, cy, 0x55])
            if symbol == 'O':
                obstacles.append([cx, cy])
            if UNOWNED[y][x] == 'u':
                flags.append([cx, cy, 0x40])
    return {'types': types, 'links': links, 'qlinks': [], 'flags': flags, 'obstacles': obstacles}


def walk_input(walk) -> dict:
    x0, y0, vx, vy, mask, speed, force, tx, ty, steps = walk
    x = cells()
    x['fill'] = 0
    x['walk'] = {'x': x0, 'y': y0, 'vx': vx, 'vy': vy, 'mask': mask, 'max_speed': speed, 'max_force': force}
    x['events'] = [('go', tx, ty, 0)] + [('step', 0, 0, 0)] * steps
    return x


def round_trip(sb, record: bytes) -> bytes:
    """The serializer's load half into a navigator filled with 0xab, then its save half: the bytes it writes."""
    stubs = T.tpi_nav_record_stubs(T.TPI_NAV_RECORD_TARGET)
    sb.stubs.update(stubs)
    walk = sb.object(0xC0)
    sb.uc.mem_write(walk, b'\xab' * 0xC0)
    sb.navigator_stream = {'bytes': record, 'at': 0, 'sizes': []}
    assert sb.call(T.TPI_NAV_SERIALIZER, args=(0x10000, 0, 1, 0), this=walk).eax == 0
    sb.navigator_stream = {'bytes': b'', 'at': 0, 'sizes': []}
    assert sb.call(T.TPI_NAV_SERIALIZER, args=(0x10000, 1, 1, 0), this=walk).eax == 0
    return sb.navigator_stream['bytes']


def main() -> int:
    if not have_unicorn() or executable_path('tpi') is None:
        print('needs Unicorn and OPENTPW_TPI_EXE', file=sys.stderr)
        return 2
    image = Image.load(executable_path('tpi'))
    target = T.TPI_NAV_STEERING_TARGET
    allowed = target.allowed + T.TPI_NAV_SERIALIZER_RANGES
    for index, walk in enumerate(WALKS):
        x = walk_input(walk)
        sb = T.Sandbox(image, allowed, target.stubs(target), target.budget)
        sb.reset()
        trace = T.tpi_nav_steering_binary(sb, x, [])
        plan = T.tpi_nav_steering_plan(x)
        if trace != plan:
            print(f'walk {index}: walk_reading.py disagrees with Game.exe', file=sys.stderr)
            return 1
        for _, _, record in trace:
            if round_trip(sb, record) != record:
                print(f'walk {index}: the serializer does not give its record back', file=sys.stderr)
                return 1
        results = ''.join(str(result) for kind, result, _ in trace[1:])
        digest = hashlib.sha256(b''.join(record[:85] + bytes(4) + record[89:] for _, _, record in trace)).hexdigest()
        kept = sorted({0, len(trace) - 1} | set(range(20, len(trace), 20)))
        print(f'\t// walk {index}: set-up {trace[0][1]}')
        print(f'\tnew( {trace[0][1]}, "{results}", "{digest}",')
        print('\t[')
        for k in kept:
            print(f'\t\t({k}, "{trace[k][2].hex()}"),')
        print('\t] ),')
    return 0


if __name__ == '__main__':
    sys.exit(main())
