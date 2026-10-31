"""tp.exe's queue corner walk 0x530120 and a ride's demolition with a queue run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X44; QUEUE-015, ECON-050, QUEUE-003).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The sandbox of the park fence
(fence_oracle.FenceOracle: a synthetic 128 x 128 map, the cells at G + 0x294 + id * 0x44, id = y * 128 + x + 1) runs, on synthetic queue layouts:

* `QueueWalk.walk`: the corner walk 0x530120 on a thing whose access cell (+0x36) is the entrance cell, with the stack at 0x81B740 (count 0x820A8C), the cell counter +0x20 writes (0x5228A0), the
  cell link clears (0x522730) and the line start 0x81EDE4/8 it leaves;
* `QueueWalk.demolish`: the ride demolition 0x527EE0 of a ride with a queue: the walk, the forced-clear flag 0x81D7A8, Back 0x52FE50 until it returns 0, and the click handler's mode 0x34 line clear
  (the executable's own queue line walker 0x536100 with the passes 0x32 and 0x86), the cell remover 0x5367A0 and the footprint passes of 0x527EE0.

Only the presentation callees are stubbed (the cell refresh 0x5365D0, the piece models 0x53AFA0, the message posts, the mode setters 0x52F200 and 0x52F580, which only write the mode, the previous mode
and the line start; the bank and the credit and debit, which are recorded).
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import FenceOracle, MAP  # noqa: E402

CELL_FIELDS = (('type', 8, 'I'), ('links', 0xC, 'B'), ('queue', 0xD, 'B'), ('flags', 0xE, 'H'), ('owner', 0x10, 'H'), ('counter', 0x20, 'h'), ('attr', 0x26, 'B'))
STACK = 0x81B740
COUNT = 0x820A8C
START_X, START_Y = 0x81EDE4, 0x81EDE8
MODE, PREVIOUS_MODE = 0x81AE2C, 0x81AF64
FORCED_CLEAR = 0x81D7A8
THING_TABLE = 0x7CFB90
TYPE_PATH, TYPE_QUEUE, TYPE_ENTRANCE, TYPE_EXIT, TYPE_OBJECT = 1, 3, 9, 10, 4
UP, RIGHT, DOWN, LEFT = 1, 4, 0x10, 0x40          # link bits: -y, +x, +y, -x
STEP = {UP: (0, -1), RIGHT: (1, 0), DOWN: (0, 1), LEFT: (-1, 0)}
OPPOSITE = {UP: DOWN, DOWN: UP, LEFT: RIGHT, RIGHT: LEFT}
COST_QUEUE = 20     # what the stub of the queue cell cost 0x52FB40 answers
SCRAP_PERCENT = 50  # what 0x4E2290 answers


def cell_id(x: int, y: int) -> int:
    return y * MAP + x + 1


class QueueWalk(FenceOracle):
    """The fence oracle's sandbox with a ride thing, its type record and the stubs of the queue tool."""

    def _write_cells(self, cells: dict) -> None:
        for (x, y), value in cells.items():
            for name, offset, fmt in CELL_FIELDS:
                if name in value:
                    struct.pack_into('<' + fmt, self.cells, (y * MAP + x) * 0x44 + offset, value[name])

    def _snapshot(self, keys) -> dict:
        self.pull()
        return {key: {name: struct.unpack_from('<' + fmt, self.cells, (key[1] * MAP + key[0]) * 0x44 + offset)[0] for name, offset, fmt in CELL_FIELDS} for key in keys}

    def _setup(self, cells: dict, entrance: tuple, rotation: int, has_queue: bool, anchor=None, mask=None, cached_size: int = 0, owner: int = 0):
        self.start([], [])
        sb = self.sandbox
        stubs = sb.stubs
        log: list = []
        self.log = log

        def cell_xy(address: int):
            index = (address - (self.game + 0x2D8)) // 0x44
            return (index % MAP, index // MAP)

        stubs.pop(0x527940, None)   # the fence oracle stubs it with a callee pop; it is a cdecl routine that writes the tool word 0x816D48
        stubs[0x5365D0] = lambda s: (log.append(('refresh', cell_xy(s.reg('ecx')))) or 0, 0)
        stubs[0x53AFA0] = lambda s: (0, 0)
        stubs[0x4E2290] = lambda s: (SCRAP_PERCENT, 0)
        stubs[0x50B780] = lambda s: (log.append(('thing removed',)) or 0, 0)
        stubs[0x545610] = lambda s: (0, 4)
        stubs[0x4DE1F0] = lambda s: (log.append(('requeue', s.reg('ecx'))) or 0, 0)
        stubs[0x4D0190] = lambda s: (log.append(('credit', s.arg(0))) or 0, 4)
        stubs[0x4D01F0] = lambda s: (log.append(('debit', s.arg(0))) or 0, 4)
        stubs[0x5195D0] = lambda s: (self.bank, 0)
        stubs[0x4D8C60] = lambda s: (0, 8)
        stubs[0x51BFC0] = lambda s: (0, 0)
        stubs[0x67A830] = lambda s: (0, 0)
        stubs[0x52FB40] = lambda s: (COST_QUEUE, 0)
        stubs[0x52FBD0] = lambda s: (log.append(('no queue debit',)) or 0, 0)
        stubs[0x403D00] = lambda s: (0, 8)
        stubs[0x52F200] = self._set_mode
        stubs[0x52F580] = self._set_mode_quiet
        # the type record: the shape mask at +0x18 (width, height, 20 columns of 8 bytes per row), the queue word at +0x40, no track word (+0x9C)
        info = sb.alloc(0x100)
        mask = mask or [[1]]
        shape = sb.alloc(8 + 0xA0 * len(mask))
        sb.write(shape, 'I', max(len(row) for row in mask))
        sb.write(shape + 4, 'I', len(mask))
        for row, line in enumerate(mask):
            for column, value in enumerate(line):
                sb.write(shape + 8 + row * 0xA0 + column * 8, 'I', 1 if value else 0)
        sb.write(info + 0x18, 'I', shape)
        sb.write(info + 0x40, 'I', 1 if has_queue else 0)
        stubs[0x4DD4E0] = lambda s: (info, 0)
        self._write_cells(cells)
        self.flush()
        sb.write(0x785A2C, 'I', 0)
        sb.write(0x8186D4, 'B', 0)
        sb.write(THING_TABLE, 'I', 1)
        thing = sb.alloc(0x300)
        anchor = anchor or entrance
        sb.write(thing + 2, 'B', 3)
        sb.write(thing + 5, 'B', anchor[0])
        sb.write(thing + 7, 'B', anchor[1])
        sb.write(thing + 0x10, 'I', rotation)
        sb.write(thing + 0x28, 'I', 0)
        sb.write(thing + 0x32, 'B', 8 if has_queue else 0)
        sb.write(thing + 0x34, 'H', cell_id(*anchor))
        sb.write(thing + 0x36, 'H', cell_id(*entrance))
        sb.write(thing + 0x38, 'H', cell_id(*entrance))
        sb.write(thing + 0x3A, 'H', 0)
        sb.write(thing + 0x40, 'I', max(cached_size, 0))
        sb.write(THING_TABLE + 20, 'I', thing)
        sb.write(self.game + 0x2D8 + (cell_id(*anchor) - 1) * 0x44 + 0x24, 'H', 1)
        for address in (0x7639E0, 0x7639E4, 0x7639E8, 0x7639EC):
            sb.write(address, 'I', 200)
        sb.write(0x81B46C, 'I', 0)
        sb.write(FORCED_CLEAR, 'I', 0)
        sb.write(0x818C14, 'I', 0)
        sb.write(0x8186AC, 'I', thing)
        sb.write(COUNT, 'I', 0)
        sb.write(START_X, 'i', -1)
        sb.write(START_Y, 'i', -1)
        sb.write(MODE, 'I', 0)
        sb.executed = 0
        return thing

    def _set_mode(self, sb):
        """0x52F200(mode, flag): the mode, the previous mode and a cleared line start (the stack is kept for a nonzero mode)."""
        sb.write(PREVIOUS_MODE, 'I', sb.read(MODE, 'I'))
        sb.write(MODE, 'I', sb.arg(0))
        sb.write(START_X, 'i', -1)
        sb.write(START_Y, 'i', -1)
        self.log.append(('mode', sb.arg(0)))
        return 0, 0

    def _set_mode_quiet(self, sb):
        """0x52F580(mode, flag): the mode and the previous mode only."""
        sb.write(PREVIOUS_MODE, 'I', sb.read(MODE, 'I'))
        sb.write(MODE, 'I', sb.arg(0))
        self.log.append(('mode quiet', sb.arg(0)))
        return 0, 0

    def stack(self) -> list:
        sb = self.sandbox
        count = sb.read(COUNT, 'i')
        return [(sb.read(STACK + 8 * index, 'i'), sb.read(STACK + 8 * index + 4, 'i')) for index in range(max(0, min(count, 0x402)))]

    def walk(self, cells: dict, entrance: tuple, rotation: int = 0) -> dict:
        """Runs the corner walk 0x530120 of a thing whose access cell is `entrance`: the stack, the return value, the line start and every cell named, before and after."""
        thing = self._setup(cells, entrance, rotation, True)
        keys = list(cells)
        before = self._snapshot(keys)
        sb = self.sandbox
        result = sb.call(0x530120, args=(thing,))
        after = self._snapshot(keys)
        return {'stack': self.stack(), 'count': sb.read(COUNT, 'i'), 'result': result.eax & 0xFF, 'start': (sb.read(START_X, 'i'), sb.read(START_Y, 'i')), 'cells': {key: (before[key], after[key]) for key in keys},
                'log': self.log, 'executed': sb.executed}

    def demolish(self, cells: dict, entrance: tuple, anchor: tuple, rotation: int = 0, mask=None, cached_size: int = 1) -> dict:
        """Runs the ride demolition 0x527EE0 of a ride with a queue (type record +0x40 set, access cell `entrance`, cached queue size `cached_size`): the cells named, the credits and the calls it made."""
        thing = self._setup(cells, entrance, rotation, True, anchor=anchor, mask=mask, cached_size=cached_size)
        keys = list(cells)
        sb = self.sandbox
        if cached_size < 0:
            # the cache the game would hold: the chain walk 0x4DE130 on the thing (+0x3A the back cell, +0x40 the count), as the last 0x4DE1F0 left it
            out = sb.alloc(8)
            sb.call(0x4DE130, args=(out,), this=thing)
            cached_size = sb.read(thing + 0x40, 'I')
        else:
            sb.write(thing + 0x40, 'I', cached_size)
        sb.write(thing + 0x3A, 'H', sb.read(thing + 0x3A, 'H'))
        before = self._snapshot(keys)
        back = sb.read(thing + 0x3A, 'H')
        sb.executed = 0
        sb.call(0x527EE0, args=(anchor[0], anchor[1], 0))
        after = self._snapshot(keys)
        credits = [entry[1] for entry in self.log if entry[0] == 'credit']
        debits = [entry[1] for entry in self.log if entry[0] == 'debit']
        return {'cells': {key: (before[key], after[key]) for key in keys}, 'log': self.log, 'executed': sb.executed, 'stack': self.stack(), 'credits': credits, 'debits': debits,
                'size': cached_size, 'back': back}


def describe(result: dict) -> str:
    lines = [f'  stack {result["stack"]}' + (f' count {result["count"]} result {result["result"]} start {result["start"]}' if 'count' in result else '')]
    for key, (b, a) in sorted(result['cells'].items()):
        changes = {name: (b[name], a[name]) for name in b if b[name] != a[name]}
        if changes:
            lines.append(f'  cell {key}: {changes}')
    lines.append(f'  log {result["log"]}')
    return '\n'.join(lines)


def chain_cells(owner: int, entrance: tuple, front_direction: int, steps: list, back_path: bool = False) -> dict:
    """An entrance cell (type 9, links toward the front cell) and a queue chain from the front cell: `steps` are the link bits the chain takes from cell to cell; each queue cell links back to the
    one before it (its queue link) and on to the next. With `back_path` the back cell links on to a type-1 path cell."""
    cells = {entrance: {'type': TYPE_ENTRANCE, 'links': front_direction, 'queue': front_direction, 'flags': 0x20, 'owner': owner}}
    previous = entrance
    direction = front_direction
    position = (entrance[0] + STEP[direction][0], entrance[1] + STEP[direction][1])
    for index in range(len(steps) + 1):
        links = OPPOSITE[direction]
        cells[position] = {'type': TYPE_QUEUE, 'links': links, 'queue': OPPOSITE[direction], 'flags': 0x20 if index == 0 else 0, 'owner': owner}
        if index < len(steps):
            direction = steps[index]
            cells[position]['links'] |= direction
            position = (position[0] + STEP[direction][0], position[1] + STEP[direction][1])
    if back_path:
        cells[position] = {'type': TYPE_PATH, 'links': OPPOSITE[direction], 'queue': 0, 'flags': 0, 'owner': owner}
        cells[(position[0] - STEP[direction][0], position[1] - STEP[direction][1])]['links'] |= direction
    return cells


if __name__ == '__main__':
    oracle = QueueWalk()
    owner = cell_id(40, 40)
    cells = chain_cells(owner, (50, 50), RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    print(describe(oracle.walk(cells, (50, 50))))
    cells = chain_cells(owner, (50, 50), RIGHT, [RIGHT, DOWN, DOWN, LEFT])
    cells[(49, 50)] = {'type': 4, 'owner': owner}
    print(describe(oracle.demolish(cells, (50, 50), (49, 50), mask=[[1, 1]], cached_size=5)))
