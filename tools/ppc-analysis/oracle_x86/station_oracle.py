"""tp.exe's object placement of a kart or water station run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X38; ECON-026).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The sandbox of the park fence (fence_oracle.FenceOracle: a
synthetic 128 x 128 map, the cells at G + 0x294 + id * 0x44 and the second-layer structure records at G + (id + 0x6cde) * 0x28, id = y * 128 + x + 1) runs the click handler's placement
block of tool mode 4 (0x525340..0x5257DA) around the real routines, so no original data enters the checkout:

* the placement commit 0x528A70 (the footprint's first-layer types through 0x532FC0, the owner word, the flags) over a synthetic type record whose footprint table the caller gives (the symbols of
  Info.Shape: '*' type 4, '2' an entrance (9), 'N' an exit (10), '+' an add-on cell (11, stored as 4)); the object creation 0x529E10 is replaced by a thing at the placement cell (it writes no cell: it builds
  the thing and its controller), and the thing list read 0x4815A0 answers with it, so that the real 0x5276D0 finds the object as the handler does;
* the station line 0x52A050 / 0x52A1A0 (the two station tiles and, with the thing's record in 0x8186C4 as 0x5276D0 sets it, their type 12 extension cells), then the exit tile and the entry tile with the
  handler's own op lists through the line walker 0x53AAC0 (0x87, the type op, 0x80, 0x82, 0x83, 0x85, 0x86, 0x81) and the entry's arm writes by hand (0x522700, 0x5227E0, 0x50C3E0);
* the net worth 0x4C7DB0 over the cells (no objects), whose cell loop is what ECON-026 follows.

Only the presentation callees are stubbed (the ones fence_oracle stubs, the event post 0x403D00, the "current thing" refresh 0x4DE1F0).
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import fence_oracle  # noqa: E402
from fence_oracle import MAP, FenceOracle  # noqa: E402

# the net worth reads the game object's fields up to +0x1F0F0 and the object list head at +0x1DA746
fence_oracle.GAME_SIZE = 0x1F4000

TYPE_ID = 1150
# Bumper.*Adjust of the shipped kart and water rides in the type record's words +0xA4..+0xC0: North (-2, 1), East (1, 4), South (4, 1), West (1, -2)
ADJUST = [(-2, 1), (1, 4), (4, 1), (1, -2)]
# the symbols of Info.Shape that matter here: (cell type, direction byte)
SYMBOLS = {'+': (11, 0), '#': (16, 0), '*': (4, 0), '.': (0, 0), '2': (9, 0x10), '8': (9, 1), '6': (9, 4), '4': (9, 0x40), 'N': (10, 1), 'S': (10, 0x10), 'E': (10, 4), 'W': (10, 0x40)}


class StationOracle(FenceOracle):
    """One sandbox over tp.exe with a synthetic map; every method runs the executable's routines."""

    def __init__(self, exe: Path | None = None, budget: int = 40_000_000):
        super().__init__(exe, budget)
        stubs = self.sandbox.stubs
        stubs[0x412E90] = lambda sb: (self.type_record, 4)      # the type registry: the synthetic record
        stubs[0x529E10] = self._create_thing                   # the object creation
        stubs[0x4DE1F0] = lambda sb: (0, 0)
        stubs[0x4815A0] = lambda sb: (self.thing, 0)           # the newest thing of the list
        stubs[0x403D00] = lambda sb: (0, 8)                    # an event post
        stubs[0x41A960] = lambda sb: (0, 0)                    # the net worth's cache stamp
        self.type_record = 0
        self.thing = 0
        self.exit = self.entry = (0, 0)
        self.heading = 0
        self.trace: list[tuple[str, int, int]] = []

    def _create_thing(self, sb):
        """0x529E10(rotation): the thing at the placement cell (the cell word 0x818C3C, which the commit set), turned by the rotation."""
        cell = sb.read(0x818C3C, 'H') - 1
        sb.write(self.thing + 5, 'B', cell & 0x7F)
        sb.write(self.thing + 7, 'B', cell >> 7)
        sb.write(self.thing + 0x10, 'I', sb.arg(0))
        return self.thing, 0

    # ------------------------------------------------------------------ state

    def build_type(self, rows) -> None:
        """The synthetic type record: rows are the shape's text rows, the entrance row (the last text row of Info.Shape) first; the table has 20 cells per row, 8 bytes each (type dword, direction byte)."""
        sb = self.sandbox
        width = max(len(row) for row in rows)
        block = sb.alloc(8 * (1 + 20 * 20))
        sb.write(block, 'i', width)
        sb.write(block + 4, 'i', len(rows))
        for row, text in enumerate(rows):
            for col, symbol in enumerate(text):
                cell_type, direction = SYMBOLS[symbol]
                base = block + 8 + (col + row * 20) * 8
                sb.write(base, 'I', cell_type)
                sb.write(base + 4, 'I', direction)
        record = sb.alloc(0x600)
        sb.write(record + 0x18, 'I', block)
        sb.write(record + 0xA0, 'I', 1)
        for index, (adjust_x, adjust_y) in enumerate(ADJUST):
            sb.write(record + 0xA4 + 8 * index, 'i', adjust_x)
            sb.write(record + 0xA8 + 8 * index, 'i', adjust_y)
        self.type_record = record
        self.thing = sb.alloc(0x100)
        sb.write(0x8186E0, 'H', TYPE_ID)

    def own_all(self) -> None:
        """Clears the unowned flag 0x40 on every cell: the net worth counts the owned cells only."""
        for index in range(MAP * MAP):
            struct.pack_into('<H', self.cells, index * 0x44 + 0xE, self.cell_flags(index) & ~0x40)
        self.flush()

    # ------------------------------------------------------------------ the placement

    def _op(self, op: int, x: int, y: int) -> int:
        sb = self.sandbox
        pointers = sb.alloc(8)
        sb.write(pointers, 'i', x)
        sb.write(pointers + 4, 'i', y)
        return sb.call(0x53AAC0, args=(op, x, y, pointers, pointers + 4)).eax

    def _globals(self, rotation: int, track_type: int) -> None:
        sb = self.sandbox
        for address, fmt, value in ((0x8186D4, 'B', 1), (0x8186C0, 'I', 0), (0x8186C4, 'I', 0), (0x8186CC, 'I', 0), (0x81D7A4, 'I', rotation), (0x81B0CC, 'I', 1), (0x81ECAC, 'I', track_type),
                                    (0x81AE2C, 'I', 0xB if track_type == 1 else 0x10), (0x7639AC, 'I', rotation), (0x81EDE4, 'i', -1), (0x81EDE8, 'i', -1), (0x820A8C, 'I', 0), (0x8186E0, 'H', TYPE_ID)):
            sb.write(address, fmt, value)

    def place_station(self, x: int, y: int, rotation: int, track_type: int) -> None:
        """The click handler's mode 4 block for a kart (1) or water (2) ride clicked at (x, y): the commit, the station line, the exit tile and the entry tile (0x5253E9..0x5257DA)."""
        sb = self.sandbox
        sb.executed = 0
        self.flush()
        self._globals(rotation, track_type)
        pointer = sb.call(0x528A70, args=(x, y, 4, rotation, 1, 0)).eax
        self.trace = []
        sb.write(0x8186C0, 'I', sb.call(0x52A050, args=(pointer, 0x7639B8, 0x7639BC)).eax)
        line = sb.call(0x52A1A0, args=(pointer, 0x81EDE4, 0x81EDE8, track_type)).eax
        sb.write(0x8186C4, 'I', line)
        word = sb.read(line, 'H')
        exit_x, exit_y = (word - 1) & 0x7F, (word - 1) >> 7
        sb.write(0x820A8C, 'I', 0)
        mode = sb.read(0x81AE2C, 'I')
        for op in (0x87, mode, 0x80, 0x82, 0x83, 0x85, 0x86, 0x81):
            self.trace.append(('exit', op, self._op(op, exit_x, exit_y)))
        cell = sb.read(0x818698, 'H') - 1
        for address, value in ((0x7639E0, cell & 0x7F), (0x7639E8, cell & 0x7F), (0x7639E4, cell >> 7), (0x7639EC, cell >> 7)):
            sb.write(address, 'i', value)
        turn = sb.read(0x7639AC, 'I')
        sb.call(0x52A3A0, args=(0x7639E0, 0x7639E4, turn))
        sb.call(0x52A420, args=(0x7639E8, 0x7639EC, turn))
        entry_x, entry_y = sb.read(0x7639E8, 'i'), sb.read(0x7639EC, 'i')
        beyond_x, beyond_y = sb.read(0x7639E0, 'i'), sb.read(0x7639E4, 'i')
        if beyond_x - entry_x < 0:
            heading = 0x40
        elif beyond_x - entry_x > 0:
            heading = 4
        else:
            heading = 1 if beyond_y - entry_y < 0 else (0x10 if beyond_y - entry_y > 0 else 0)
        for op in (0x87, mode, 0x80):
            self.trace.append(('entry', op, self._op(op, entry_x, entry_y)))
        record = self.game + (entry_y * MAP + entry_x + 1 + 0x6CDE) * 0x28
        sb.call(0x522700, args=(heading,), this=record)
        sb.call(0x5227E0, args=(heading,), this=record)
        second = sb.call(0x50C3E0, args=(heading, 2), this=record).eax
        if second:
            sb.call(0x522700, args=(sb.call(0x4D8C00, args=(heading,)).eax,), this=second)
        for op in (0x82, 0x83, 0x85, 0x86, 0x81):
            self.trace.append(('entry', op, self._op(op, entry_x, entry_y)))
        self.exit, self.entry, self.heading = (beyond_x, beyond_y), (entry_x, entry_y), heading
        self.pull()

    def place_add_on(self, x: int, y: int, rotation: int, rows) -> None:
        """An add-on's commit (the same 0x528A70 with the add-on's type record) over the ride's tiles."""
        sb = self.sandbox
        self.build_type(rows)
        sb.executed = 0
        self.flush()
        for address, fmt, value in ((0x8186D4, 'B', 1), (0x81D7A4, 'I', rotation), (0x7639AC, 'I', rotation), (0x81ECAC, 'I', 0), (0x8186E0, 'H', TYPE_ID)):
            sb.write(address, fmt, value)
        sb.call(0x528A70, args=(x, y, 4, rotation, 1, 0))
        self.pull()

    # ------------------------------------------------------------------ comparable output

    def net_worth(self) -> int:
        """tp.exe 0x4C7DB0 over the cells with no objects: the cell points (the cache flag +0x1F0F0 is clear, the object list head +0x1DA746 is 0)."""
        sb = self.sandbox
        sb.executed = 0
        self.flush()
        sb.write(self.game + 0x1F0F0, 'B', 0)
        sb.write(self.game + 0x1DA746, 'H', 0)
        sb.budget = 200_000_000
        return sb.call(0x4C7DB0, args=(0,), this=self.game).eax

    def anchor(self) -> tuple[int, int]:
        """The placed thing's cell (the placement's anchor)."""
        cell = self.sandbox.read(0x818698, 'H') - 1
        return cell & 0x7F, cell >> 7

    def cells_around(self, anchor: tuple[int, int], radius: int = 8) -> list[tuple]:
        """Every cell near the anchor (the placed thing's cell, see anchor()) that holds something, relative to the anchor: (dx, dy, type, flags, links, queue link, record type, record arms, record link, parent dx, parent dy).
        The parent of a type 12 record is the cell its +0x10 word names."""
        anchor_x, anchor_y = anchor
        rows = []
        for y in range(anchor_y - radius, anchor_y + radius + 1):
            for x in range(anchor_x - radius, anchor_x + radius + 1):
                index = y * MAP + x
                cell = self.cells[index * 0x44:(index + 1) * 0x44]
                record = self.records[index * 0x28:(index + 1) * 0x28]
                cell_type = struct.unpack_from('<I', cell, 8)[0]
                record_type = struct.unpack_from('<I', record, 8)[0]
                if cell_type == 0 and record_type == 0 and cell[0xC] == 0 and cell[0xD] == 0:
                    continue
                parent = struct.unpack_from('<H', record, 0x10)[0] - 1
                parent_dx, parent_dy = (((parent & 0x7F) - x, (parent >> 7) - y) if record_type == 12 else (0, 0))
                flags = struct.unpack_from('<H', cell, 0xE)[0] & 0xFFBF
                rows.append((x - anchor_x, y - anchor_y, cell_type, flags, cell[0xC], cell[0xD], record_type, record[0xC], record[0xD], parent_dx, parent_dy))
        return rows
