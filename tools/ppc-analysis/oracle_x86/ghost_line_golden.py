"""Golden cases of the path and queue tools' preview line (docs/reverse/ORACLE.md, finding X50; BLUEPRINT-001, BLUEPRINT-003): tp.exe's own line walker 0x536100 with the ghost SetCell 0x532FC0 run on random lines.

Every case is a line of one to eight cells along one axis (some run off the map's edge), random cell types, flags, links, owners (the tool's ride, another ride, a thing of overwrite priority 1 to 3), second-layer
records (track pieces, stations, extensions, posts), ground heights, prices and money; the faces the ghost queues (cell, face code, texture, height step) and the validator's sticky words and the cost after it are
recorded. OpenTPW.Tests replays every case on BlueprintLineGhost. Usage: python3 -I tools/ppc-analysis/oracle_x86/ghost_line_golden.py > source/OpenTPW.Tests/GhostLineGolden.cs (needs Unicorn and OPENTPW_TPW_EXE).
"""
from __future__ import annotations

import base64
import json
import random
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from ghost_oracle import ANCHORS, GhostOracle, TOOL_OWNER  # noqa: E402
from fence_oracle import MAP  # noqa: E402

CASES = 300
TYPES = [0] * 8 + [1] * 4 + [3] * 3 + [4, 4, 9, 10, 7, 7, 30, 30, 2, 0x15]
CLEAN_TYPES = [0] * 6 + [1] * 3 + [3] * 3 + [7, 30, 4]
FLAGS = [0] * 14 + [0x20, 0x20, 0x40, 0x60]
CLEAN_FLAGS = [0] * 20 + [0x20]
OPEN_TYPES = [0] * 6 + [7, 30, 1]
OPEN_RECORDS = [(0, 0)] * 6 + [(0xB, 0x11), (0xB, 0x44), (0x10, 0x11)]
LINKS = [0, 0, 1, 4, 0x10, 0x40, 5, 0x44, 0x11, 0x14]
RECORDS = [(0, 0)] * 14 + [(0xB, 0x11), (0xB, 0x44), (0xB, 0x05), (0xB, 0x15), (0xB, 0x04), (0xD, 0x11), (0x10, 0x44), (0x12, 0x11), (0x19, 0)]
HEIGHTS = [0.0, 4.0, 9.5, 12.0, 25.0, 31.5, 40.0, -3.0, 60.5, 100.0]


def make_case(rng: random.Random) -> dict:
    mode = rng.choice((1, 3))
    kind_of_case = rng.choice(('dirty', 'clean', 'open'))
    clean = kind_of_case != 'dirty'
    length = rng.choice((1, 1, 2, 3, 4, 5, 8))
    axis = rng.choice((0, 1))
    step = rng.choice((1, -1))
    near_edge = rng.random() < 0.12
    x0 = rng.randint(0, 3) if near_edge else rng.randint(20, 90)
    y0 = rng.randint(0, 3) if near_edge else rng.randint(20, 90)
    if near_edge:
        step = -1
    line = [(x0 + (i * step if axis == 0 else 0), y0 + (i * step if axis == 1 else 0)) for i in range(length)]
    inside = [(x, y) for x, y in line if 0 <= x < MAP and 0 <= y < MAP]
    wanted = set()
    for x, y in inside:
        for dx, dy in ((0, 0), (0, -1), (1, 0), (0, 1), (-1, 0)):
            wanted.add((x + dx, y + dy))
    cells = {}
    records = {}
    anchors = {}
    for anchor in (1, 2, 3):
        if rng.random() < 0.7:
            anchors[anchor] = rng.randint(0, 5)
    for x, y in sorted(wanted):
        if not (0 <= x < MAP and 0 <= y < MAP):
            continue
        ctype = rng.choice(OPEN_TYPES if kind_of_case == 'open' else CLEAN_TYPES if clean else TYPES)
        flags = rng.choice(CLEAN_FLAGS if clean else FLAGS)
        if kind_of_case == 'open':
            flags = 0
        links = rng.choice(LINKS) if ctype in (1, 3) else 0
        owner = 0
        if ctype == 3:
            owner = rng.choice((TOOL_OWNER, TOOL_OWNER, TOOL_OWNER + 1))
        elif ctype in (4, 9, 10):
            owner = rng.choice((ANCHORS[1], ANCHORS[2], ANCHORS[3], TOOL_OWNER, 0))
        elif ctype == 0x15:
            owner = rng.choice((TOOL_OWNER, TOOL_OWNER + 1))
        cells[(x, y)] = {'type': ctype, 'flags': flags, 'links': links, 'owner': owner}
        kind, rlinks = rng.choice(OPEN_RECORDS if kind_of_case == 'open' else RECORDS)
        if kind:
            records[(x, y)] = (kind, rlinks, 0)
        elif rng.random() < 0.12:
            # an extension of a track piece whose origin lies two cells away
            px, py = x - 1, y - 1
            if kind_of_case != 'open' and 0 <= px < MAP and 0 <= py < MAP and (px, py) not in cells:
                pkind, plinks = rng.choice(((0xB, 0x11), (0xB, 0x05), (0x10, 0x44), (0x19, 0), (0xB, 0x15)))
                records[(px, py)] = (pkind, plinks, 0)
                records[(x, y)] = (0xC, 0, py * MAP + px + 1)
    # an extension's parent that lies outside the cells the line reads is made a plain record too
    ground = {}
    for x in range(-1, MAP + 1):
        pass
    heights = {key: rng.choice(HEIGHTS) for key in wanted}
    if clean:
        records = {key: value for key, value in records.items() if rng.random() < 0.5}
    bank = rng.choice((None, None, 0, 5, 30, 100, 1000))
    price = (rng.choice((0, 10, 25, 100)), rng.choice((0, 20, 40)))
    return {'mode': mode, 'line': line, 'cells': cells, 'records': records, 'anchors': anchors, 'heights': heights, 'bank': bank, 'price': price,
            'hover_is_start': length == 1 and rng.random() < 0.5 or (length > 1 and rng.random() < 0.1)}


def run(oracle: GhostOracle, case: dict) -> dict:
    heights = case['heights']
    scenario = dict(case)
    scenario['ground'] = lambda x, y: heights.get((x, y), 0.0)
    # the line's anchors map priority by anchor number
    result = oracle.line(scenario)
    return result


def render() -> str:
    oracle = GhostOracle()
    oracle.start([], [])
    rng = random.Random('ghost-line-x48')
    lines = []
    for _ in range(CASES):
        case = make_case(rng)
        result = run(oracle, case)
        line = {
            'mode': case['mode'],
            'line': case['line'],
            'hoverIsStart': case['hover_is_start'],
            'price': case['price'],
            'bank': case['bank'],
            'cells': [[x, y, c['type'], c['flags'], c['links'], c['owner']] for (x, y), c in sorted(case['cells'].items())],
            'records': [[x, y, kind, links, parent] for (x, y), (kind, links, parent) in sorted(case['records'].items())],
            'anchors': {str(ANCHORS[a]): p for a, p in sorted(case['anchors'].items())},
            'heights': [[x, y, h] for (x, y), h in sorted(case['heights'].items())],
            'faces': [list(f[:5]) for f in result['faces']],
            'latches': result['latches'],
            'cost': result['cost'],
        }
        lines.append(json.dumps(line, separators=(',', ':')))
    packed = base64.b64encode(zlib.compress('\n'.join(lines).encode(), 9)).decode()
    out = ['// Generated by tools/ppc-analysis/oracle_x86/ghost_line_golden.py from tp.exe\'s own line walker 0x536100 with the ghost SetCell 0x532FC0 run in the x86 oracle (docs/reverse/ORACLE.md, X50).',
           '// Synthetic maps, no original data. One JSON object per line when unpacked: the tool state, the line, the cells and second-layer records it reads, the ground heights, and the faces the ghost queued',
           '// (cell x, cell y, face code, texture, height step), the validator\'s words 0x816D50, 0x816D58, 0x816D60, 0x816D4C, 0x816D5C, 0x816D48 and the pass\'s cost 0x81AF4C after it.',
           'namespace OpenTPW.Tests;', '', 'internal static class GhostLineGolden', '{',
           f'\t/// <summary>The oracle\'s {len(lines)} cases, packed.</summary>', '\tpublic const string Packed =']
    chunk = 120
    parts = [packed[i:i + chunk] for i in range(0, len(packed), chunk)]
    out += [f'\t\t"{part}"' + (' +' if i + 1 < len(parts) else ';') for i, part in enumerate(parts)]
    out += ['', f'\tpublic const int CaseCount = {len(lines)};', '}', '']
    return '\n'.join(out)


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('GhostLineGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())
