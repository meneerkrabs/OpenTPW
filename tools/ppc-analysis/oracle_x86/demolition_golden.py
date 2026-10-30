"""Golden cases of the second layer's record clear in the object demolition (docs/reverse/ORACLE.md, finding X37; ECON-050): tp.exe's own 0x527EE0 and 0x53B280 on synthetic cells.

Pass 2 of the demolition runs the cell remover 0x5367A0 and then, for every cell of the shape's mask, the record clear 0x53B280(0, 0, 1) on the second layer's record under it (or, for the add-on
object a cleared tile takes with it, only the record's add-on flag, the argument 0x527EE0 takes as `moved`). The cases lay chains of kart and water tiles with an add-on object on one tile and a kart
or water ride's station tiles beside their exit and entry tiles, run the demolition, and write what is left of the cells and records and the credits it gave. OpenTPW.Tests replays them on
ParkCellMap.DemolishFootprint and TrackEditor.ClearCell (`ParkDemolitionTrackTests`).

Usage: python3 -I tools/ppc-analysis/oracle_x86/demolition_golden.py > source/OpenTPW.Tests/ParkDemolitionTrackGolden.cs   (needs Unicorn and OPENTPW_TPW_EXE; `--check FILE` compares)
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from demolition_oracle import Demolition, cell_id  # noqa: E402

KART, WATER = 'kart', 'water'
TILE = {KART: (0xB, 0xC, 0xD, 0xC), WATER: (0x10, 0x11, 0x12, 0x11)}   # origin, extension, station origin, station extension


def tile(records: dict, family: str, x: int, y: int, arms: int, flow: int, station: bool = False, flags: int = 0, owner: int = 0, counter: int = 0) -> None:
    origin, extension, station_origin, station_extension = TILE[family]
    records[(x, y)] = {'type': station_origin if station else origin, 'links': arms, 'flow': flow, 'flags': flags, 'parent': owner, 'counter': counter}
    for dx, dy in ((1, 0), (0, 1), (1, 1)):
        records[(x + dx, y + dy)] = {'type': station_extension if station else extension, 'parent': cell_id(x, y), 'flags': flags}


def chain(family: str, y: int = 40, addon_flags: int = 0x10, addon_index: int = 2, arms=(0x04, 0x44, 0x44, 0x44, 0x40), owner: int = 0, counters=None):
    records: dict = {}
    for index, x in enumerate((30, 32, 34, 36, 38)):
        tile(records, family, x, y, arms[index], 0x04, flags=addon_flags if index == addon_index else 0, owner=owner, counter=(counters or {}).get(index, 0))
    return records


def addon_cells(x: int, y: int) -> dict:
    return {(x + dx, y + dy): {'type': 4, 'owner': cell_id(x, y), 'flags': 0x10} for dx in range(2) for dy in range(2)}


def cases():
    owner = cell_id(31, 41)
    out = []
    two = [[1, 1], [1, 1]]
    for family in (KART, WATER):
        for moved in (0, 1):
            out.append((f'{family} add-on on a straight tile, moved {moved}', (34, 40), moved, (200, 200), (201, 201), two, addon_cells(34, 40), chain(family, owner=owner)))
        out.append((f'{family} add-on on a straight tile that is the exit', (34, 40), 0, (34, 40), (201, 201), two, addon_cells(34, 40), chain(family, owner=owner)))
        out.append((f'{family} add-on on a straight tile that is the entry', (34, 40), 0, (200, 200), (34, 40), two, addon_cells(34, 40), chain(family, owner=owner)))
        out.append((f'{family} add-on on a tile laid twice (counter 2)', (34, 40), 0, (200, 200), (201, 201), two, addon_cells(34, 40), chain(family, owner=owner, counters={2: 2})))
    out.append(('kart add-on on a junction tile', (34, 40), 0, (200, 200), (201, 201), two, addon_cells(34, 40), chain(KART, owner=owner, arms=(0x04, 0x44, 0x45, 0x44, 0x40))))
    out.append(('kart add-on on a corner tile', (34, 40), 0, (200, 200), (201, 201), two, addon_cells(34, 40), chain(KART, owner=owner, arms=(0x04, 0x44, 0x14, 0x44, 0x40))))
    out.append(('kart add-on on an armless tile', (34, 40), 0, (200, 200), (201, 201), two, addon_cells(34, 40), chain(KART, owner=owner, arms=(0x04, 0x44, 0x00, 0x44, 0x40))))
    # a ride's station: two tiles in the footprint's rows 1 and 2, the exit and entry tiles outside it (rotation 0 at the anchor (40, 40))
    for family in (KART, WATER):
        records: dict = {}
        station_owner = cell_id(40, 41)
        tile(records, family, 38, 41, 0x04, 0x04, owner=station_owner)
        tile(records, family, 40, 41, 0x44, 0x04, station=True, owner=station_owner)
        tile(records, family, 42, 41, 0x44, 0x04, station=True, owner=station_owner)
        tile(records, family, 44, 41, 0x40, 0x04, owner=station_owner)
        cells = {(40 + dx, 40 + dy): {'type': 4, 'owner': cell_id(40, 40), 'flags': 0} for dx in range(4) for dy in range(3)}
        out.append((f'{family} station tiles under the footprint', (40, 40), 0, (44, 41), (38, 41), [[1] * 4] * 3, cells, records))
        # a neighbour tile of the circuit that the exit and entry tiles do not hold: arms toward the station
        records2 = dict(records)
        tile(records2, family, 40, 43, 0x01, 0x01, owner=station_owner)
        out.append((f'{family} station with a loose tile beside it', (40, 40), 0, (44, 41), (38, 41), [[1] * 4] * 3, cells, records2))
    return out


def render() -> str:
    oracle = Demolition()
    lines = []
    for name, anchor, moved, exit_tile, entry_tile, mask, cells, records in cases():
        result = oracle.run(cells, records, mask, anchor, moved=moved, exit_tile=exit_tile, entry_tile=entry_tile)
        lines.append(f'case {name}|{anchor[0]} {anchor[1]}|{moved}|{exit_tile[0]} {exit_tile[1]}|{entry_tile[0]} {entry_tile[1]}|{",".join("".join(str(v) for v in row) for row in mask)}')
        for (x, y), (before, after) in sorted(result['cells'].items()):
            fields = ('type', 'links', 'queue', 'flags', 'owner', 'counter')
            lines.append(f'cell {x} {y}|' + ' '.join(str(before[f]) for f in fields) + '|' + ' '.join(str(after[f]) for f in fields))
        for (x, y), (before, after) in sorted(result['records'].items()):
            fields = ('type', 'links', 'flow', 'flags', 'parent', 'counter')
            lines.append(f'rec {x} {y}|' + ' '.join(str(before[f]) for f in fields) + '|' + ' '.join(str(after[f]) for f in fields))
        for entry in result['log']:
            if entry[0] == 'credit':
                lines.append(f'credit {entry[1]}')
    body = ',\n'.join(f'\t\t"{line}"' for line in lines)
    return '\n'.join([
        '// Generated by tools/ppc-analysis/oracle_x86/demolition_golden.py from tp.exe\'s own demolition 0x527EE0 and record clear 0x53B280 run in the x86 oracle (docs/reverse/ORACLE.md, X37).',
        '// Synthetic cells and records, no original data. Per case: the anchor, the `moved` argument, the exit and entry tiles of the track tool, the shape mask; then each primary cell as',
        '// "type links queue flags owner counter" before|after, each second-layer record as "type links flow flags parent counter" before|after, and the credits the clears gave (the kart cost 40 and',
        '// the water cost 60 at a scrap percentage of 50).',
        'namespace OpenTPW.Tests;', '', 'internal static class ParkDemolitionTrackGolden', '{',
        '\tpublic static readonly string[] Lines =', '\t[', body, '\t];', '}', ''])


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == '--check':
        if render() != Path(sys.argv[2]).read_text():
            print('ParkDemolitionTrackGolden.cs differs from the oracle output', file=sys.stderr)
            return 1
        return 0
    print(render(), end='')
    return 0


if __name__ == '__main__':
    sys.exit(main())
