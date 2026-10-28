"""Golden cases of track records on the park fence's purchase edges (docs/reverse/ORACLE.md, finding X37; PATH-007): tp.exe's own routines on synthetic maps.

Every case is a 128 x 128 map built here (terrain rectangles, NoModify cells; no original data) with the fence of a case of fence_golden.py laid by the set-up 0x5323F0, then synthetic second-layer track records
(fence_track_oracle: origins 0xB / 0x10, stations 0xD / 0x12 and their 0xC extensions, with a ride word, a flag byte, a link and a counter each), then Buy Land rectangles: the validator 0x531A50 answers each and an accepted
one runs the purchase 0x5327E0, whose lattice and ring cross the tiles. `fence_track_oracle.TrackFenceOracle` runs the executable's routines in the Unicorn sandbox; this script records, per case, the cell flag words
(run-length coded), a hash of the fence's structure records without the cells that hold a track record (type, links, owner word, walk direction) and, for every track record laid, the fields the fence routines read or
write (type +8, mask +0xC, link +0xD, flags +0xE, word +0x10, fence links +0x22, counter +0x20) afterwards. OpenTPW.Tests replays every case on `FenceEngine` / `ParkLand` with the tiles on the cell map's track layer.

The tile layouts were picked from seeded random layouts (tiles on the lattice of a purchase edge and next to it, kinds 0xB / 0x10 / 0xD / 0x12, ride words, flag bytes, links, counters, terrain types under them) by what
the purchases change; the comment above each case names the fields.

Usage: python3 -I tools/ppc-analysis/oracle_x86/fence_track_golden.py > source/OpenTPW.Tests/ParkFenceTrackGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares instead of printing)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_track_oracle import TrackFenceOracle, run  # noqa: E402


def spec(name, a, b, clicks, terrain, no_modify, tracks, buys):
    return {'name': name, 'a': a, 'b': b, 'clicks': clicks, 'terrain': terrain, 'no_modify': no_modify, 'tracks': tracks, 'buys': buys}


CASES = [
    # fields the fence changes: counter flags link type word
    spec('tracks 01', (51, 41), (43, 41), [(0, 0), (2, 0), (2, -6), (14, -6), (14, 10), (-22, 10), (-22, -6), (-10, -6), (-10, 0), (-8, 0)],
         terrain=[(2, 34, 65, 2, 3)], no_modify=[],
         tracks=[(32, 48, 18, 64, 0, 1, 1, 0), (33, 48, 12, 64, 6177, 1, 1, 0), (32, 49, 12, 64, 6177, 1, 1, 3), (33, 49, 12, 64, 6177, 1, 1, 3)],
         buys=[((27, 49), (43, 63))]),
    # fields the fence changes: counter link links22 type word
    spec('tracks 02', (21, 11), (13, 11), [(0, 0), (4, 0), (4, 10), (0, 10), (0, 4), (-8, 4), (-8, 10), (-12, 10), (-12, 0), (-8, 0)],
         terrain=[(2, 25, 25, 1, 1)], no_modify=[],
         tracks=[(27, 24, 16, 2, 0, 21, 16, 1), (28, 24, 12, 2, 3100, 21, 16, 1), (27, 25, 12, 2, 3100, 21, 16, 0), (28, 25, 12, 2, 3100, 21, 16, 0), (27, 13, 18, 65, 0, 80, 1, 0), (28, 13, 12, 65, 1692, 80, 1, 3), (27, 14, 12, 65, 1692, 80, 1, 3), (28, 14, 12, 65, 1692, 80, 1, 0)],
         buys=[((13, 13), (29, 23))]),
    # fields the fence changes: counter link links22 type word
    spec('tracks 03', (51, 41), (43, 41), [(0, 0), (2, 0), (2, -6), (14, -6), (14, 10), (-22, 10), (-22, -6), (-10, -6), (-10, 0), (-8, 0)],
         terrain=[(2, 31, 29, 3, 1), (0, 41, 55, 3, 2)], no_modify=[],
         tracks=[(33, 28, 13, 64, 0, 0, 16, 0), (34, 28, 12, 64, 3618, 0, 16, 3), (33, 29, 12, 64, 3618, 0, 16, 1), (34, 29, 12, 64, 3618, 0, 16, 0), (43, 56, 16, 0, 6190, 80, 0, 3), (44, 56, 12, 0, 7212, 80, 0, 3), (43, 57, 12, 0, 7212, 80, 0, 0), (44, 57, 12, 0, 7212, 80, 0, 3), (31, 27, 11, 65, 0, 65, 0, 1), (32, 27, 12, 65, 3488, 65, 0, 0), (31, 28, 12, 65, 3488, 65, 0, 0), (32, 28, 12, 65, 3488, 65, 0, 3)],
         buys=[((59, 39), (75, 49)), ((31, 27), (37, 35)), ((43, 47), (49, 55))]),
    # fields the fence changes: flags link type word
    spec('tracks 04', (51, 41), (43, 41), [(0, 0), (2, 0), (2, 14), (-10, 14), (-10, 8), (-24, 8), (-24, 0), (-8, 0)],
         terrain=[(30, 28, 46, 1, 2), (1, 26, 51, 2, 1), (7, 29, 43, 2, 1), (7, 36, 50, 3, 3)], no_modify=[],
         tracks=[(30, 46, 16, 16, 14616, 80, 16, 0), (31, 46, 12, 16, 5919, 80, 16, 1), (30, 47, 12, 16, 5919, 80, 16, 0), (31, 47, 12, 16, 5919, 80, 16, 3), (30, 43, 13, 65, 0, 17, 1, 0), (31, 43, 12, 65, 5535, 17, 1, 0), (30, 44, 12, 65, 5535, 17, 1, 1), (31, 44, 12, 65, 5535, 17, 1, 3)],
         buys=[((29, 43), (37, 53))]),
    # fields the fence changes: counter link links22 word
    spec('tracks 05', (51, 41), (43, 41), [(0, 0), (2, 0), (2, -4), (18, -4), (18, -8), (26, -8), (26, 16), (-20, 16), (-20, -6), (-12, -6), (-12, 0), (-8, 0)],
         terrain=[(30, 44, 43, 3, 3), (7, 34, 34, 3, 1)], no_modify=[],
         tracks=[(44, 44, 18, 0, 0, 80, 16, 0), (45, 44, 12, 0, 5677, 80, 16, 0), (44, 45, 12, 0, 5677, 80, 16, 0), (45, 45, 12, 0, 5677, 80, 16, 0), (33, 34, 13, 1, 0, 0, 16, 0), (34, 34, 12, 1, 4386, 0, 16, 0), (33, 35, 12, 1, 4386, 0, 16, 0), (34, 35, 12, 1, 4386, 0, 16, 0), (39, 29, 13, 2, 0, 80, 0, 0), (40, 29, 12, 2, 3752, 80, 0, 0), (39, 30, 12, 2, 3752, 80, 0, 0), (40, 30, 12, 2, 3752, 80, 0, 0), (35, 32, 18, 16, 0, 5, 0, 0), (36, 32, 12, 16, 4132, 5, 0, 0), (35, 33, 12, 16, 4132, 5, 0, 0), (36, 33, 12, 16, 4132, 5, 0, 0)],
         buys=[((39, 27), (47, 35)), ((45, 45), (61, 59)), ((27, 27), (35, 35))]),
    # fields the fence changes: flags type word
    spec('tracks 06', (21, 11), (13, 11), [(0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0)],
         terrain=[(30, 14, 18, 2, 1)], no_modify=[],
         tracks=[(15, 15, 16, 0, 0, 17, 4, 0), (16, 15, 12, 0, 1936, 17, 4, 1), (15, 16, 12, 0, 1936, 17, 4, 3), (16, 16, 12, 0, 1936, 17, 4, 3), (16, 17, 13, 65, 3995, 68, 0, 0), (17, 17, 12, 65, 2193, 68, 0, 0), (16, 18, 12, 65, 2193, 68, 0, 3), (17, 18, 12, 65, 2193, 68, 0, 3)],
         buys=[((7, 17), (23, 23)), ((7, 19), (17, 29))]),
    # fields the fence changes: counter links22 word
    spec('tracks 07', (51, 41), (43, 41), [(0, 0), (2, 0), (2, -6), (14, -6), (14, 10), (-22, 10), (-22, -6), (-10, -6), (-10, 0), (-8, 0)],
         terrain=[(30, 50, 43, 1, 2), (7, 48, 31, 3, 1)], no_modify=[],
         tracks=[(49, 43, 11, 0, 5611, 1, 1, 1), (50, 43, 12, 0, 5554, 1, 1, 3), (49, 44, 12, 0, 5554, 1, 1, 3), (50, 44, 12, 0, 5554, 1, 1, 0), (40, 41, 11, 64, 4872, 21, 16, 3), (41, 41, 12, 64, 5289, 21, 16, 3), (40, 42, 12, 64, 5289, 21, 16, 0), (41, 42, 12, 64, 5289, 21, 16, 0), (50, 33, 18, 32, 0, 80, 4, 0), (51, 33, 12, 32, 4275, 80, 4, 0), (50, 34, 12, 32, 4275, 80, 4, 3), (51, 34, 12, 32, 4275, 80, 4, 0)],
         buys=[((23, 49), (33, 63)), ((41, 35), (49, 47)), ((39, 47), (53, 55))]),
    # fields the fence changes: flags links22
    spec('tracks 08', (21, 11), (13, 11), [(0, 0), (4, 0), (4, 6), (-4, 6), (-4, 10), (-12, 10), (-12, 0), (-8, 0)],
         terrain=[(0, 10, 17, 2, 2)], no_modify=[],
         tracks=[(12, 18, 13, 1, 0, 0, 0, 0), (13, 18, 12, 1, 2317, 0, 0, 0), (12, 19, 12, 1, 2317, 0, 0, 0), (13, 19, 12, 1, 2317, 0, 0, 0)],
         buys=[((13, 19), (27, 27))]),
    # fields the fence changes: flags links22
    spec('tracks 09', (21, 11), (13, 11), [(0, 0), (4, 0), (4, 6), (-4, 6), (-4, 10), (-12, 10), (-12, 0), (-8, 0)],
         terrain=[(2, 12, 18, 1, 1), (1, 28, 12, 3, 2)], no_modify=[],
         tracks=[(12, 18, 16, 2, 1761, 1, 64, 0), (13, 18, 12, 2, 2317, 1, 64, 0), (12, 19, 12, 2, 2317, 1, 64, 0), (13, 19, 12, 2, 2317, 1, 64, 0)],
         buys=[((17, 9), (29, 15)), ((13, 15), (25, 29))]),
    # fields the fence changes: flags link
    spec('tracks 10', (51, 41), (43, 41), [(0, 0), (2, 0), (2, 14), (-10, 14), (-10, 8), (-24, 8), (-24, 0), (-8, 0)],
         terrain=[(3, 43, 49, 3, 1)], no_modify=[],
         tracks=[(43, 51, 18, 1, 0, 20, 0, 0), (44, 51, 12, 1, 6572, 20, 0, 0), (43, 52, 12, 1, 6572, 20, 0, 0), (44, 52, 12, 1, 6572, 20, 0, 0)],
         buys=[((27, 49), (41, 65)), ((41, 43), (57, 51))]),
    # fields the fence changes: counter word
    spec('tracks 11', (51, 41), (43, 41), [(0, 0), (2, 0), (2, 14), (-24, 14), (-24, 0), (-8, 0)],
         terrain=[(2, 45, 57, 1, 1), (7, 39, 42, 2, 2), (1, 39, 42, 1, 3)], no_modify=[],
         tracks=[(46, 57, 13, 4, 0, 1, 0, 0), (47, 57, 12, 4, 7343, 1, 0, 3), (46, 58, 12, 4, 7343, 1, 0, 3), (47, 58, 12, 4, 7343, 1, 0, 0), (39, 42, 18, 0, 0, 21, 4, 0), (40, 42, 12, 0, 5416, 21, 4, 0), (39, 43, 12, 0, 5416, 21, 4, 0), (40, 43, 12, 0, 5416, 21, 4, 0), (39, 44, 11, 0, 0, 1, 1, 3), (40, 44, 12, 0, 5672, 1, 1, 3), (39, 45, 12, 0, 5672, 1, 1, 3), (40, 45, 12, 0, 5672, 1, 1, 1)],
         buys=[((33, 29), (41, 45)), ((39, 39), (49, 49)), ((33, 49), (45, 61))]),
    # fields the fence changes: flags link
    spec('tracks 12', (51, 41), (43, 41), [(0, 0), (2, 0), (2, 14), (-10, 14), (-10, 8), (-24, 8), (-24, 0), (-8, 0)],
         terrain=[(1, 47, 41, 2, 3), (3, 46, 49, 2, 3)], no_modify=[],
         tracks=[(46, 42, 11, 4, 0, 68, 0, 0), (47, 42, 12, 4, 5423, 68, 0, 0), (46, 43, 12, 4, 5423, 68, 0, 0), (47, 43, 12, 4, 5423, 68, 0, 0), (47, 51, 18, 4, 0, 0, 16, 0), (48, 51, 12, 4, 6576, 0, 16, 0), (47, 52, 12, 4, 6576, 0, 16, 0), (48, 52, 12, 4, 6576, 0, 16, 0)],
         buys=[((41, 47), (47, 59)), ((47, 43), (59, 53)), ((43, 41), (53, 51))]),
    # fields the fence changes: flags link
    spec('tracks 13', (51, 41), (43, 41), [(0, 0), (2, 0), (2, 14), (-24, 14), (-24, 0), (-8, 0)],
         terrain=[(0, 43, 37, 2, 1)], no_modify=[],
         tracks=[(49, 39, 18, 8, 0, 1, 0, 0), (50, 39, 12, 8, 5042, 1, 0, 0), (49, 40, 12, 8, 5042, 1, 0, 0), (50, 40, 12, 8, 5042, 1, 0, 0), (47, 53, 13, 16, 0, 80, 16, 0), (48, 53, 12, 16, 6832, 80, 16, 0), (47, 54, 12, 16, 6832, 80, 16, 0), (48, 54, 12, 16, 6832, 80, 16, 0), (42, 38, 11, 1, 12197, 1, 0, 0), (43, 38, 12, 1, 4907, 1, 0, 0), (42, 39, 12, 1, 4907, 1, 0, 0), (43, 39, 12, 1, 4907, 1, 0, 0)],
         buys=[((49, 29), (55, 39)), ((45, 41), (57, 55)), ((43, 39), (51, 53))]),
    # fields the fence changes: pocket grid, link
    spec('tracks 14', (21, 11), (13, 11), [(0, 0), (4, 0), (4, 10), (0, 10), (0, 4), (-8, 4), (-8, 10), (-12, 10), (-12, 0), (-8, 0)],
         terrain=[], no_modify=[],
         tracks=[(26, 16, 16, 17, 0, 21, 16, 0), (27, 16, 12, 17, 2075, 21, 16, 0), (26, 17, 12, 17, 2075, 21, 16, 0), (27, 17, 12, 17, 2075, 21, 16, 0), (36, 10, 13, 17, 9302, 0, 64, 0), (37, 10, 12, 17, 1317, 0, 64, 0), (36, 11, 12, 17, 1317, 0, 64, 0), (37, 11, 12, 17, 1317, 0, 64, 0), (13, 14, 18, 16, 0, 17, 64, 0), (14, 14, 12, 16, 1806, 17, 64, 0), (13, 15, 12, 16, 1806, 17, 64, 0), (14, 15, 12, 16, 1806, 17, 64, 0)],
         buys=[((13, 13), (29, 27)), ((23, 7), (37, 17))]),
    # fields the fence changes: owner word
    spec('tracks 15', (51, 41), (43, 41), [(0, 0), (2, 0), (2, -6), (14, -6), (14, 10), (-22, 10), (-22, -6), (-10, -6), (-10, 0), (-8, 0)],
         terrain=[], no_modify=[],
         tracks=[(30, 47, 11, 4, 0, 21, 1, 0), (31, 47, 12, 4, 6047, 21, 1, 0), (30, 48, 12, 4, 6047, 21, 1, 0), (31, 48, 12, 4, 6047, 21, 1, 0)],
         buys=[((39, 39), (53, 51)), ((23, 39), (31, 47))]),
    # fields the fence changes: owner word
    spec('tracks 16', (51, 41), (43, 41), [(0, 0), (2, 0), (2, -6), (14, -6), (14, 10), (-22, 10), (-22, -6), (-10, -6), (-10, 0), (-8, 0)],
         terrain=[], no_modify=[],
         tracks=[(32, 33, 11, 1, 11114, 1, 64, 0), (33, 33, 12, 1, 4257, 1, 64, 0), (32, 34, 12, 1, 4257, 1, 64, 0), (33, 34, 12, 1, 4257, 1, 64, 0)],
         buys=[((35, 35), (41, 41)), ((51, 27), (59, 43)), ((29, 33), (39, 41))]),
]


def literal(case, result):
    def pair(p):
        return f'({p[0]}, {p[1]})'
    terrain = ', '.join(f'({k}, {x}, {y}, {w}, {h})' for k, x, y, w, h in case['terrain'])
    no_modify = ', '.join(pair(p) for p in case['no_modify'])
    clicks = ', '.join(pair(p) for p in case['clicks'])
    tracks = ', '.join(f'new( {x}, {y}, {t}, {f}, {w}, {m}, {l}, {c} )' for x, y, t, f, w, m, l, c in case['tracks'])
    states = ', '.join(f'new( {t}, {m}, {l}, {f}, {w}, {k}, {c} )' for t, m, l, f, w, k, c in result['tracks'])
    ops = ', '.join(f'new( {pair(f)}, {pair(s)}, {str(a).lower()}, {c}, {str(x).lower()} )' for f, s, a, c, x in result['ops'])
    runs = ', '.join(str(v) for v in result['runs'])
    return (f'\t\tnew( "{case["name"]}", {pair(case["a"])}, {pair(case["b"])}, [{terrain}], [{no_modify}], [{clicks}],\n'
            f'\t\t\t[{tracks}],\n\t\t\t[{states}],\n\t\t\t[{ops}],\n'
            f'\t\t\t{result["owned"]}, {result["posts"]}, "{result["layer"]}",\n\t\t\t[{runs}] ),')


def render() -> str:
    oracle = TrackFenceOracle()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/fence_track_golden.py from tp.exe\'s own fence routines run in the x86 oracle (docs/reverse/ORACLE.md, X37).',
           '// Synthetic 128 x 128 maps, fences and track records, no original data: the cell flag words (run-length: start, length, value), the fence\'s structure records (hash, without the cells that hold a track record),',
           '// the track records after the purchases and the validator\'s verdicts.',
           'namespace OpenTPW.Tests;', '', 'internal static class ParkFenceTrackGolden', '{',
           '\t/// <summary>A track record laid on the second layer: the type (+8), the flag byte (+0xE), the word (+0x10), the mask (+0xC), the link (+0xD) and the counter (+0x20).</summary>',
           '\tinternal readonly record struct Laid( int X, int Y, int Type, int Flags, int Word, int Mask, int Link, int Counter );', '',
           '\t/// <summary>What the oracle left in a laid record: type, mask, link, flags, word, the fence links (+0x22) and the counter.</summary>',
           '\tinternal readonly record struct After( int Type, int Mask, int Link, int Flags, int Word, int FenceLinks, int Counter );', '',
           '\t/// <summary>A purchase rectangle the validator answered (and priced); an accepted one ran the purchase, which crashes or never ends (Faults) on some.</summary>',
           '\tinternal readonly record struct Buy( (int X, int Y) First, (int X, int Y) Second, bool Accepted, int Cells, bool Faults );', '',
           '\tinternal sealed record Case( string Name, (int X, int Y) PostA, (int X, int Y) PostB, (int Type, int X, int Y, int Width, int Height)[] Terrain, (int X, int Y)[] NoModify,',
           '\t\t(int X, int Y)[] Clicks, Laid[] Tracks, After[] Records, Buy[] Buys, int OwnedCells, int Posts, string LayerSha256, int[] FlagRuns );', '',
           '\tpublic static readonly Case[] Cases =', '\t[']
    for case in CASES:
        out.append(literal(case, run(case, oracle)))
    out += ['\t];', '}', '']
    return '\n'.join(out)


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        expected = Path(sys.argv[2]).read_text()
        if render() != expected:
            print('ParkFenceTrackGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())
