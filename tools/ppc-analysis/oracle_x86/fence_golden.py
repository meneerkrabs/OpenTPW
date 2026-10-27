"""Golden cases of the park fence (docs/reverse/ORACLE.md, finding X35; PATH-007): tp.exe's own routines on synthetic maps.

Every case is a 128 x 128 map built here (terrain rectangles, NoModify cells; no original data), a fence laid by the set-up 0x5323F0 from a click list, and then Buy Land rectangles: the validator 0x531A50
answers each (and its price), and an accepted one runs the purchase 0x5327E0. `fence_oracle.FenceOracle` runs the executable's routines in the Unicorn sandbox; this script records, per case, the cell flag words
(run-length coded), a hash of the fence's structure records (type, links, owner word, walk direction) and the counts. OpenTPW.Tests replays every case on `FenceEngine` / `ParkLand` and must reproduce them.

Usage: python3 -I tools/ppc-analysis/oracle_x86/fence_golden.py > source/OpenTPW.Tests/ParkFenceGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares instead of printing)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import FenceOracle  # noqa: E402
from x86_harness import BudgetExceeded, MemoryFault  # noqa: E402

# the shapes: click offsets from post A, east first; the gate runs between post B (west) and post A. The land lies on the right of the walk, as in every shipped fence: the first clicks run east then
# turn south or north, and the posts come back along the far side to post B.
RECTANGLE = [(0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0)]
WIDE = [(0, 0), (2, 0), (2, 14), (-24, 14), (-24, 0), (-8, 0)]
L_SHAPE = [(0, 0), (2, 0), (2, 14), (-10, 14), (-10, 8), (-24, 8), (-24, 0), (-8, 0)]
CONCAVE = [(0, 0), (4, 0), (4, 6), (-4, 6), (-4, 10), (-12, 10), (-12, 0), (-8, 0)]
U_SHAPE = [(0, 0), (4, 0), (4, 10), (0, 10), (0, 4), (-8, 4), (-8, 10), (-12, 10), (-12, 0), (-8, 0)]
LOBED = [(0, 0), (2, 0), (2, -6), (14, -6), (14, 10), (-22, 10), (-22, -6), (-10, -6), (-10, 0), (-8, 0)]
STEPPED = [(0, 0), (2, 0), (2, -4), (18, -4), (18, -8), (26, -8), (26, 16), (-20, 16), (-20, -6), (-12, -6), (-12, 0), (-8, 0)]
SIX_CLICKS = [(0, 0), (2, 0), (2, 4), (-10, 4), (-10, 0), (-8, 0)]

SMALL = ((21, 11), (13, 11))
BIG = ((51, 41), (43, 41))


def case(name, gates, clicks, terrain=(), no_modify=(), buys=()):
    return {'name': name, 'a': gates[0], 'b': gates[1], 'terrain': list(terrain), 'no_modify': list(no_modify), 'clicks': clicks, 'buys': list(buys)}


CASES = [
    case('rectangle', SMALL, RECTANGLE),
    case('wide rectangle', BIG, WIDE),
    case('L shape', BIG, L_SHAPE),
    case('concave corner', SMALL, CONCAVE),
    case('U shape', SMALL, U_SHAPE),
    case('lobes north of the gate', BIG, LOBED),
    case('stepped fence', BIG, STEPPED),
    case('six clicks', BIG, SIX_CLICKS),
    case('water and no terrain on the fence', BIG, WIDE,
         terrain=[(2, 27, 49, 6, 4), (7, 40, 55, 4, 4), (0x1E, 51, 54, 3, 3), (7, 80, 70, 8, 8), (2, 49, 33, 4, 4)],
         no_modify=[(30, 50), (31, 50), (32, 50)]),
    case('buy east and south of a rectangle', SMALL, RECTANGLE, buys=[((25, 11), (33, 21)), ((33, 11), (41, 27)), ((9, 21), (17, 31))]),
    case('buy next to a wide park', BIG, WIDE, buys=[((53, 41), (61, 57)), ((27, 55), (47, 63)), ((19, 41), (27, 57))]),
    case('buy over the open side of the gate (rectangle)', SMALL, RECTANGLE, buys=[((15, 7), (19, 15)), ((11, 7), (23, 15))]),
    case('buy over the open side of the gate (wide)', BIG, WIDE, buys=[((45, 37), (49, 45)), ((41, 37), (53, 47)), ((47, 33), (51, 47))]),
    case('buy rectangles that overlap and merge', BIG, L_SHAPE, buys=[((53, 41), (63, 53)), ((61, 47), (71, 57)), ((57, 33), (73, 45)), ((27, 49), (43, 63))]),
    case('buy along a lobed fence', BIG, LOBED, buys=[((65, 35), (77, 47)), ((29, 31), (41, 45)), ((57, 25), (71, 37)), ((19, 35), (29, 47))]),
    case('buy along a stepped fence', BIG, STEPPED, buys=[((77, 33), (87, 49)), ((31, 25), (47, 41)), ((69, 49), (79, 61))]),
    case('buy across water and no terrain', BIG, WIDE,
         terrain=[(2, 20, 50, 10, 6), (7, 70, 44, 6, 6), (0x1E, 62, 40, 2, 8)],
         buys=[((53, 41), (69, 57)), ((17, 49), (27, 61)), ((55, 37), (65, 49))]),
    case('buy around the short fence', BIG, SIX_CLICKS, buys=[((41, 45), (55, 57)), ((43, 41), (53, 47)), ((37, 37), (57, 49))]),
]


def build(spec, oracle, applied):
    oracle.start(spec['terrain'], spec['no_modify'])
    oracle.lay_fence(spec['a'], spec['b'], spec['clicks'])
    for first, second in applied:
        oracle.purchase(spec['a'], spec['b'], first, second)


def run(spec, oracle):
    build(spec, oracle, [])
    ops = []
    applied = []
    for first, second in spec['buys']:
        accepted, cells = oracle.validate(spec['a'], spec['b'], first, second)
        faults = False
        if accepted:
            try:
                oracle.purchase(spec['a'], spec['b'], first, second)
                applied.append((first, second))
            except (MemoryFault, BudgetExceeded):
                # tp.exe crashes (the fill reads the cell beyond the map's edge) or never ends: the state is what it was before
                faults = True
                build(spec, oracle, applied)
        ops.append((first, second, accepted, cells, faults))
    oracle.pull()
    return {'ops': ops, 'runs': oracle.flag_runs(), 'layer': oracle.layer_sha256(), 'owned': oracle.owned_cells(), 'posts': oracle.post_count()}


def literal(spec, result):
    def pair(p):
        return f'({p[0]}, {p[1]})'
    terrain = ', '.join(f'({k}, {x}, {y}, {w}, {h})' for k, x, y, w, h in spec['terrain'])
    no_modify = ', '.join(pair(p) for p in spec['no_modify'])
    clicks = ', '.join(pair(p) for p in spec['clicks'])
    ops = ', '.join(f'new( {pair(f)}, {pair(s)}, {str(a).lower()}, {c}, {str(x).lower()} )' for f, s, a, c, x in result['ops'])
    runs = ', '.join(str(v) for v in result['runs'])
    return (f'\t\tnew( "{spec["name"]}", {pair(spec["a"])}, {pair(spec["b"])}, [{terrain}], [{no_modify}], [{clicks}], [{ops}],\n'
            f'\t\t\t{result["owned"]}, {result["posts"]}, "{result["layer"]}",\n\t\t\t[{runs}] ),')


def render() -> str:
    oracle = FenceOracle()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/fence_golden.py from tp.exe\'s own fence routines run in the x86 oracle (docs/reverse/ORACLE.md, X35).',
           '// Synthetic 128 x 128 maps and fences, no original data: the cell flag words (run-length: start, length, value), the fence\'s structure records (hash) and the validator\'s verdicts.',
           'namespace OpenTPW.Tests;', '', 'internal static class ParkFenceGolden', '{',
           '\t/// <summary>A Buy Land rectangle the validator 0x531A50 answered (and priced in cells); an accepted one ran the purchase 0x5327E0, which crashes or never ends (Faults) on some.</summary>',
           '\tinternal readonly record struct Buy( (int X, int Y) First, (int X, int Y) Second, bool Accepted, int Cells, bool Faults );', '',
           '\t/// <summary>A case: terrain rectangles (type, x, y, w, h), NoModify path cells, the click offsets from post A, the rectangles bought, and the oracle\'s outcome.</summary>',
           '\tinternal sealed record Case( string Name, (int X, int Y) PostA, (int X, int Y) PostB, (int Type, int X, int Y, int Width, int Height)[] Terrain, (int X, int Y)[] NoModify,',
           '\t\t(int X, int Y)[] Clicks, Buy[] Buys, int OwnedCells, int Posts, string LayerSha256, int[] FlagRuns );', '',
           '\tpublic static readonly Case[] Cases =', '\t[']
    for spec in CASES:
        out.append(literal(spec, run(spec, oracle)))
    out += ['\t];', '}', '']
    return '\n'.join(out)


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        expected = Path(sys.argv[2]).read_text()
        if render() != expected:
            print('ParkFenceGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())
