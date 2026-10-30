"""tp.exe's object demolition 0x527EE0 and the second layer's record clear 0x53B280 run in the x86 oracle sandbox (docs/reverse/ORACLE.md, findings X36 and X37; ECON-050).

Dev-only tooling like the rest of oracle_x86 (Unicorn and the identified tp.exe, OPENTPW_TPW_EXE, are needed; nothing here is shipped). The sandbox runs the executable's own demolition on a synthetic
128 x 128 map: one placed thing (class 3, its type record with the shape mask and no queue, no track word, so the queue and track replays stay out), the cells of its footprint and the second layer's records
under them. The presentation, the sound and the bank are stubbed (the cell refresh 0x5365D0, the piece models 0x53AFA0, the credit and the debit); the credits are recorded.

`Demolition.run(cells, records, mask, anchor, ...)` returns, for every cell and record named, its fields before and after, and the credits and calls the run made.
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import FenceOracle, MAP  # noqa: E402

THING_TABLE = 0x7CFB90
CELL_FIELDS = (('type', 8, 'I'), ('links', 0xC, 'B'), ('queue', 0xD, 'B'), ('flags', 0xE, 'H'), ('owner', 0x10, 'H'), ('counter', 0x20, 'h'), ('attr', 0x26, 'B'))
RECORD_FIELDS = (('type', 8, 'I'), ('links', 0xC, 'B'), ('flow', 0xD, 'B'), ('flags', 0xE, 'H'), ('parent', 0x10, 'H'), ('counter', 0x20, 'h'))
COST_KART = 40     # 0x81AEBC, the kart track cell cost the stubs of 0x52FB60 read
COST_WATER = 60    # 0x81B43C
SCRAP_PERCENT = 50  # what 0x4E2290 answers


def cell_id(x: int, y: int) -> int:
    return y * MAP + x + 1


class Demolition(FenceOracle):
    """The fence oracle's sandbox with the demolition's thing, type record and stubs."""

    def run(self, cells: dict, records: dict, mask, anchor, moved: int = 0, rotation: int = 0, exit_tile=(200, 200), entry_tile=(201, 201), layout_replay: int = 0,
            replay_flag: int = 0, forced_clear: int = 0) -> dict:
        """cells: {(x, y): dict of CELL_FIELDS}, records: {(x, y): dict of RECORD_FIELDS}, mask: rows of 0/1 (the shape's cells), anchor: the thing's cell.

        The exit and entry tiles are the globals 0x7639E0/E4 and 0x7639E8/EC, the replay flag 0x81B46C and the forced-clear flag 0x81D7A8 the clear op reads, the layout replay byte 0x785A2C the credits.
        """
        self.start([], [])
        sb = self.sandbox
        game = self.game
        log: list = []

        def cell_xy(address: int):
            index = (address - (game + 0x2D8)) // 0x44
            return (index % MAP, index // MAP)

        def record_xy(address: int):
            index = (address - (game + 0x1102D8)) // 0x28
            return (index % MAP, index // MAP)

        stubs = sb.stubs
        stubs[0x5365D0] = lambda s: (log.append(('refresh', cell_xy(s.reg('ecx')))) or 0, 0)
        stubs[0x53AFA0] = lambda s: (log.append(('piece', record_xy(s.reg('ecx')))) or 0, 0)
        stubs[0x4E2290] = lambda s: (SCRAP_PERCENT, 0)
        stubs[0x52F200] = lambda s: (0, 0)
        stubs[0x50B780] = lambda s: (log.append(('thing removed',)) or 0, 0)
        stubs[0x545610] = lambda s: (log.append(('thing model',)) or 0, 4)
        stubs[0x4DE1F0] = lambda s: (log.append(('requeue',)) or 0, 0)
        stubs[0x4D0190] = lambda s: (log.append(('credit', s.arg(0))) or 0, 4)
        stubs[0x4D01F0] = lambda s: (log.append(('debit', s.arg(0))) or 0, 4)
        stubs[0x5195D0] = lambda s: (self.bank, 0)
        stubs[0x4D8C60] = lambda s: (0, 8)
        stubs[0x51BFC0] = lambda s: (0, 0)
        stubs[0x67A830] = lambda s: (0, 0)
        stubs[0x52FB60] = lambda s: (COST_KART, 0)
        stubs[0x52FB80] = lambda s: (COST_WATER, 0)
        # the type record: the shape mask at +0x18 (width, height, 20 columns of 8 bytes per row), no queue (+0x40), no track word (+0x9C)
        info = sb.alloc(0x100)
        shape = sb.alloc(8 + 0xA0 * max(1, len(mask)))
        sb.write(shape, 'I', max(len(row) for row in mask))
        sb.write(shape + 4, 'I', len(mask))
        for row, line in enumerate(mask):
            for column, value in enumerate(line):
                sb.write(shape + 8 + row * 0xA0 + column * 8, 'I', 1 if value else 0)
        sb.write(info + 0x18, 'I', shape)
        stubs[0x4DD4E0] = lambda s: (info, 0)
        for (x, y), value in cells.items():
            for name, offset, fmt in CELL_FIELDS:
                if name in value:
                    struct.pack_into('<' + fmt, self.cells, (y * MAP + x) * 0x44 + offset, value[name])
        for (x, y), value in records.items():
            for name, offset, fmt in RECORD_FIELDS:
                if name in value:
                    struct.pack_into('<' + fmt, self.records, (y * MAP + x) * 0x28 + offset, value[name])
        self.flush()
        sb.write(0x785A2C, 'I', layout_replay)
        sb.write(0x8186D4, 'B', 0)
        sb.write(THING_TABLE, 'I', 1)
        thing = sb.alloc(0x300)
        sb.write(thing + 2, 'B', 3)
        sb.write(thing + 0x10, 'I', rotation)
        sb.write(thing + 0x28, 'I', 0)
        sb.write(thing + 0x34, 'H', cell_id(*anchor))
        sb.write(THING_TABLE + 20, 'I', thing)
        # the cell of the owner word holds the thing (+0x24 = its id, 0x4D99E0)
        sb.write(game + 0x2D8 + (cell_id(*anchor) - 1) * 0x44 + 0x24, 'H', 1)
        for address, value in ((0x7639E0, exit_tile[0]), (0x7639E4, exit_tile[1]), (0x7639E8, entry_tile[0]), (0x7639EC, entry_tile[1]), (0x81B46C, replay_flag), (0x81D7A8, forced_clear)):
            sb.write(address, 'I', value)
        sb.write(0x818C14, 'I', 0)

        def snapshot():
            self.pull()
            return ({key: {name: struct.unpack_from('<' + fmt, self.cells, (key[1] * MAP + key[0]) * 0x44 + offset)[0] for name, offset, fmt in CELL_FIELDS} for key in cells},
                    {key: {name: struct.unpack_from('<' + fmt, self.records, (key[1] * MAP + key[0]) * 0x28 + offset)[0] for name, offset, fmt in RECORD_FIELDS} for key in records})

        before = snapshot()
        sb.executed = 0
        sb.call(0x527EE0, args=(anchor[0], anchor[1], moved))
        after = snapshot()
        return {'cells': {key: (before[0][key], after[0][key]) for key in cells}, 'records': {key: (before[1][key], after[1][key]) for key in records}, 'log': log, 'executed': sb.executed}


def describe(result: dict) -> str:
    lines = []
    for key, (b, a) in sorted(result['cells'].items()):
        changes = {name: (b[name], a[name]) for name in b if b[name] != a[name]}
        lines.append(f'  cell {key}: type {b["type"]} -> {a["type"]}  {changes}')
    for key, (b, a) in sorted(result['records'].items()):
        changes = {name: (b[name], a[name]) for name in b if b[name] != a[name]}
        lines.append(f'  record {key}: type {b["type"]} -> {a["type"]}  {changes}')
    lines.append(f'  log {result["log"]}')
    return '\n'.join(lines)


if __name__ == '__main__':
    oracle = Demolition()
    ax, ay = 40, 40
    owner = cell_id(ax, ay)
    # an add-on over a straight kart tile: the origin (40, 40) and its extension cells, flagged 0x10 on cells and record
    cells = {(ax + dx, ay + dy): {'type': 4, 'owner': owner, 'flags': 0x10} for dx in range(2) for dy in range(2)}
    records = {(ax, ay): {'type': 0xB, 'links': 0x44, 'flags': 0x10, 'parent': owner, 'counter': 0}}
    for dx, dy in ((1, 0), (0, 1), (1, 1)):
        records[(ax + dx, ay + dy)] = {'type': 0xC, 'flags': 0x10, 'parent': cell_id(ax, ay)}
    for moved in (0, 1):
        print('add-on over a tile, moved =', moved)
        print(describe(oracle.run(cells, records, [[1, 1], [1, 1]], (ax, ay), moved=moved)))
