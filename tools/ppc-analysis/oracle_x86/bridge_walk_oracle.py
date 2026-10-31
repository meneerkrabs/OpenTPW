"""tp.exe's bridge walks 0x52C6F0 (forward) and 0x52CCB0 (reverse) and the track line's path clearing run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X47; TRACK-010).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The station oracle's sandbox (a synthetic 128 x 128 map, the
cells at G + 0x294 + id * 0x44 and the second-layer structure records at G + (id + 0x6cde) * 0x28, id = y * 128 + x + 1) places a kart or water ride; this module then

* lays synthetic second-layer records around it (`BridgeWalkOracle.lay`: tiles of kind 11/16 or station kinds 13/18 with arms +0xC, flow link +0xD, flag word +0xE and the ride word +0x10, and the type 12
  extension cells of each) and runs the op 0x86 pair as the line walker does: the forward walk 0x52C6F0(thing, 1, 0, 0) and, when it answers 0, the reverse walk 0x52CCB0(thing, 0, 0, 0)
  (`BridgeWalkOracle.walk`), reporting the answers and the flag word of every record;
* runs a track tool line through the walker 0x53AAC0 with the click handler's op list (0x87, the mode's type op, 0x80, 0x82, 0x83, 0x85, 0x86, 0x81) over paths laid under it (`BridgeWalkOracle.line`).

Only the presentation callees are stubbed (the ones fence_oracle and station_oracle stub; the mesh refresh 0x53AFA0 writes nothing here, the instance word of every record is 0).
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

import unicorn
from unicorn import x86_const

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import MAP  # noqa: E402
from station_oracle import StationOracle  # noqa: E402
from x86_harness import BudgetExceeded  # noqa: E402

KART = ['*2N*', '****', '****']
WATER = ['*2N*', '****', '****', '****']
UP, RIGHT, DOWN, LEFT = 1, 4, 0x10, 0x40
STEP = {UP: (0, -1), RIGHT: (1, 0), DOWN: (0, 1), LEFT: (-1, 0)}
OPPOSITE = {UP: DOWN, DOWN: UP, LEFT: RIGHT, RIGHT: LEFT}
ORIGIN_KINDS = (11, 16)
STATION_KINDS = (13, 18)


def cell_word(x: int, y: int) -> int:
    return y * MAP + x + 1


class BridgeWalkOracle(StationOracle):
    """A placed kart or water ride whose second-layer records the caller rewrites, and the walks over them."""

    def place(self, click, rotation: int, track_type: int) -> None:
        """A fresh own-all map and the ride's placement as the click handler runs it (kart 1, water 2)."""
        self.start([], [])
        self.own_all()
        self.build_type(KART if track_type == 1 else WATER)
        self.place_station(click[0], click[1], rotation, track_type)

    # ------------------------------------------------------------------ records

    def _record(self, x: int, y: int) -> int:
        return (y * MAP + x) * 0x28

    def lay(self, tiles) -> None:
        """tiles: dicts with x, y, kind, arms, flags, word (+0x10 of the origin) and optional link (+0xD). Each tile's three other cells get a type 12 extension record naming the origin's cell word."""
        self.pull()
        for tile in tiles:
            x, y, kind = tile['x'], tile['y'], tile['kind']
            base = self._record(x, y)
            struct.pack_into('<I', self.records, base + 8, kind)
            self.records[base + 0xC] = tile['arms']
            self.records[base + 0xD] = tile.get('link', 0)
            struct.pack_into('<H', self.records, base + 0xE, tile['flags'])
            struct.pack_into('<H', self.records, base + 0x10, tile['word'])
            if kind in ORIGIN_KINDS + STATION_KINDS:
                for dx, dy in ((1, 0), (0, 1), (1, 1)):
                    extension = self._record(x + dx, y + dy)
                    struct.pack_into('<I', self.records, extension + 8, 12)
                    self.records[extension + 0xC] = 0
                    self.records[extension + 0xD] = 0
                    struct.pack_into('<H', self.records, extension + 0xE, 0)
                    struct.pack_into('<H', self.records, extension + 0x10, cell_word(x, y))
        self.flush()

    def clear_records(self, cells) -> None:
        """Blank the records of the given cells (type 0, no arms, no flags, no word)."""
        self.pull()
        for x, y in cells:
            base = self._record(x, y)
            self.records[base + 8:base + 0x28] = bytes(0x20)
        self.flush()

    def state(self, cells) -> list[tuple[int, int, int, int, int]]:
        """(kind, arms, link, flags, word) of each cell's record."""
        self.pull()
        out = []
        for x, y in cells:
            base = self._record(x, y)
            out.append((struct.unpack_from('<I', self.records, base + 8)[0], self.records[base + 0xC], self.records[base + 0xD],
                        struct.unpack_from('<H', self.records, base + 0xE)[0], struct.unpack_from('<H', self.records, base + 0x10)[0]))
        return out

    # ------------------------------------------------------------------ the walks

    def thing_at(self, anchor, rotation: int) -> int:
        """A synthetic ride thing with the anchor cell (+5, +7) and the rotation (+0x10): all the two walks read of it."""
        sb = self.sandbox
        thing = sb.alloc(0x300)
        sb.write(thing + 5, 'B', anchor[0])
        sb.write(thing + 7, 'B', anchor[1])
        sb.write(thing + 0x10, 'I', rotation)
        return thing

    def walk(self, anchor, rotation: int) -> tuple[int, int | None]:
        """The op 0x86 pair of 0x538BF0..0x538CCB: the forward walk, and the reverse walk when it answers 0. Returns the two answers (the reverse one None when it did not run)."""
        sb = self.sandbox
        sb.executed = 0
        self.flush()
        thing = self.thing_at(anchor, rotation)
        self._watch_records()
        self.escaped = False
        forward = sb.call(0x52C6F0, args=(thing, 1, 0, 0)).eax & 0xFF
        reverse = None
        if not forward:
            reverse = sb.call(0x52CCB0, args=(thing, 0, 0, 0)).eax & 0xFF
        self.pull()
        if self.escaped:
            raise BudgetExceeded('a walk read a record past the structure array (undefined in the executable)')
        return forward, reverse

    def _watch_records(self) -> None:
        """Note when a walk reads a record outside the 128 x 128 structure array: the executable reads on into other game memory there, so the case is undefined and the golden leaves it out."""
        sb = self.sandbox
        if getattr(self, '_watched', None) is sb:
            return
        self._watched = sb
        records = sb.read(0x7CF83C, 'I')
        first = records + (1 + 0x6CDE) * 0x28
        last = records + (MAP * MAP + 0x6CDE) * 0x28

        def hook(uc, address, size, user):
            if address == 0x52C7F7 or address == 0x52CDB4:
                pointer = uc.reg_read(x86_const.UC_X86_REG_ECX)
                if not first <= pointer <= last:
                    self.escaped = True

        sb.uc.hook_add(unicorn.UC_HOOK_CODE, hook)

    # ------------------------------------------------------------------ the track line

    def lay_paths(self, cells, links=None) -> None:
        """Path cells (type 1) with the given link bytes (default: none); flag 0x40 cleared (the player owns them)."""
        self.pull()
        for x, y in cells:
            index = y * MAP + x
            struct.pack_into('<I', self.cells, index * 0x44 + 8, 1)
            struct.pack_into('<H', self.cells, index * 0x44 + 0xE, 0)
            self.cells[index * 0x44 + 0xC] = (links or {}).get((x, y), 0)
        self.flush()

    def line(self, start, end) -> list[tuple[int, int]]:
        """One track tool line from `start` to `end` (the end snapped by the walker): the click handler's op list through 0x53AAC0, the previous-step sentinels reset and the tool's start stored."""
        sb = self.sandbox
        sb.executed = 0
        self.flush()
        pointers = sb.alloc(8)
        sb.write(pointers, 'i', end[0])
        sb.write(pointers + 4, 'i', end[1])
        sb.write(0x763B00, 'I', 0x80)
        sb.write(0x763B04, 'I', 0x80)
        sb.write(0x8186D4, 'B', 0)
        mode = sb.read(0x81AE2C, 'I')
        answers = []
        for op in (0x87, mode, 0x80, 0x82, 0x83, 0x85, 0x86, 0x81):
            sb.write(0x81EDE4, 'i', start[0])
            sb.write(0x81EDE8, 'i', start[1])
            answers.append((op, sb.call(0x53AAC0, args=(op, start[0], start[1], pointers, pointers + 4)).eax))
        self.pull()
        return answers

    def cell_state(self, cells) -> list[tuple[int, int]]:
        """(type +8, links +0xC) of each map cell."""
        self.pull()
        out = []
        for x, y in cells:
            index = y * MAP + x
            out.append((struct.unpack_from('<I', self.cells, index * 0x44 + 8)[0], self.cells[index * 0x44 + 0xC]))
        return out
