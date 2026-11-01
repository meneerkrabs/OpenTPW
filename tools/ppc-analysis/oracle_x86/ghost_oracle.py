"""tp.exe's ghost faces run in the x86 oracle sandbox (docs/reverse/ORACLE.md, finding X48; BLUEPRINT-001, BLUEPRINT-003).

Dev-only tooling like the rest of oracle_x86: Unicorn and the identified tp.exe (OPENTPW_TPW_EXE, SHA-256 pinned) are needed, nothing here is shipped. The sandbox runs the hover's line preview, the line
walker 0x536100 with the ghost SetCell 0x532FC0 and the cell validator 0x535670 behind it, on a synthetic 128 x 128 map. Only the drawing is replaced: the face queue 0x53C8D0 records the faces it is
handed (cell, face code, texture, height step) and the cell ground 0x452AE0 answers from a table of heights (it pushes the float the callers' conversion 0x67A830 reads).

Layout (see fence_oracle.py): the map cell of (x, y) at G + 0x2D8 + (y * 128 + x) * 0x44 (type +8, link byte +0xC, flags +0xE, owner word +0x10, thing list head word +0x24) and its second-layer
record at G + 0x1102D8 + (y * 128 + x) * 0x28 (type +8, links +0xC, flags +0xE, owner or parent word +0x10). The things behind an owner word are the executable's own table at 0x7CFB90 (20-byte entries,
class byte +2, next word +0xA, type id word +0xE), the type records its 0x412E90 lookup answers are stubbed (priority +0x54 = the type id).
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from fence_oracle import FenceOracle, MAP  # noqa: E402
from unicorn import x86_const as x86  # noqa: E402

TOOL_OWNER = 2996
ANCHORS = {1: 120 * MAP + 100 + 1, 2: 120 * MAP + 101 + 1, 3: 120 * MAP + 102 + 1}   # cell words of the owners' anchor cells; anchor k holds one class-3 thing of priority k
LATCHES = {'l48': 0x816D48, 'l4c': 0x816D4C, 'l50': 0x816D50, 'l54': 0x816D54, 'l58': 0x816D58, 'l5c': 0x816D5C, 'l60': 0x816D60}


def push_x87(uc, value: float) -> None:
    """Pushes a number on the emulated x87 stack, as the callee `fld` would leave it."""
    fpsw = uc.reg_read(x86.UC_X86_REG_FPSW)
    top = (fpsw >> 11) & 7
    new_top = (top - 1) & 7
    uc.reg_write(x86.UC_X86_REG_FPSW, (fpsw & ~0x3800) | (new_top << 11))
    uc.reg_write(x86.UC_X86_REG_FPTAG, uc.reg_read(x86.UC_X86_REG_FPTAG) & ~(3 << (2 * new_top)))
    bits = struct.unpack('<Q', struct.pack('<d', float(value)))[0]
    sign, exponent, fraction = bits >> 63, (bits >> 52) & 0x7FF, bits & ((1 << 52) - 1)
    if exponent == 0 and fraction == 0:
        mantissa, extended = 0, 0
    else:
        mantissa, extended = (1 << 63) | (fraction << 11), exponent - 1023 + 16383
    uc.reg_write(x86.UC_X86_REG_FP0 + new_top, (mantissa, extended | (sign << 15)))   # the registers are the physical ones: ST(0) is the one TOP names


def as_float(bits: int) -> float:
    return struct.unpack('<f', struct.pack('<I', bits))[0]


class GhostOracle(FenceOracle):
    """The fence sandbox with the face queue and the cell ground replaced by recorders."""

    def __init__(self):
        super().__init__()
        self.faces: list[tuple] = []
        self.ground = lambda x, y: 0.0
        stubs = self.sandbox.stubs
        stubs[0x53C8D0] = self._face
        stubs[0x452AE0] = self._ground
        stubs[0x412E90] = self._type_record
        stubs[0x527940] = self._refusal_word
        self.type_records: dict[int, int] = {}

    def _face(self, sb):
        a = [sb.arg(i) for i in range(11)]
        # arguments: x, y, z (floats), face code, texture, direction byte, ..., size flag
        self.faces.append((round(as_float(a[0]) / 10), round(as_float(a[2]) / 10), a[3] & 0xFF, a[4], round(as_float(a[1]) - 1.5), a[5] & 0xFF, a[9]))
        return 0, 0

    def _ground(self, sb):
        push_x87(sb.uc, self.ground(sb.arg(0), sb.arg(1)))
        return 0, 0

    def _refusal_word(self, sb):
        # 0x527940(value) only writes the click gate's refusal word 0x816D48; a cdecl routine (the fence sandbox's stub pops its argument, which the ghost's callers do themselves)
        sb.write(0x816D48, 'I', sb.arg(0))
        return 0, 0

    def _type_record(self, sb):
        priority = sb.arg(0) & 0xFFFF
        if priority not in self.type_records:
            self.type_records[priority] = sb.alloc(0x100)
            sb.write(self.type_records[priority] + 0x54, 'I', priority)
        return self.type_records[priority], 4

    # ------------------------------------------------------------------ the scenario

    def line(self, case) -> dict:
        """One hover over a line: `case` has mode, line (cells in order), cells ({(x, y): fields}), records, ground, price, bank (None = free) and tool state."""
        sb = self.sandbox
        self.start([], [])
        self.faces = []
        self.type_records = {}
        self.ground = case['ground']
        for k in range(MAP * MAP):
            struct.pack_into('<H', self.cells, k * 0x44 + 0xE, 0)      # every cell is the player's land unless a case says so
        for (x, y), fields in case['cells'].items():
            base = (y * MAP + x) * 0x44
            struct.pack_into('<I', self.cells, base + 8, fields['type'])
            struct.pack_into('<H', self.cells, base + 0xE, fields['flags'])
            self.cells[base + 0xC] = fields['links']
            struct.pack_into('<H', self.cells, base + 0x10, fields['owner'])
        for (x, y), (kind, links, parent) in case['records'].items():
            base = (y * MAP + x) * 0x28
            struct.pack_into('<I', self.records, base + 8, kind)
            self.records[base + 0xC] = links
            struct.pack_into('<H', self.records, base + 0x10, parent)
        for anchor, priority in case['anchors'].items():
            struct.pack_into('<H', self.cells, (ANCHORS[anchor] - 1) * 0x44 + 0x24, anchor)
        self.flush()
        for anchor, priority in case['anchors'].items():
            thing = sb.alloc(0x40)
            sb.write(thing + 2, 'B', 3)
            sb.write(thing + 0xE, 'H', priority)
            sb.write(0x7CFB90 + anchor * 20, 'I', thing)
        sb.write(0x7CFB90, 'I', sb.alloc(0x40))                         # thing 0: a thing of no class, for owners that have none
        mode = case['mode']
        sb.write(0x81AE2C, 'I', mode)
        sb.write(0x818698, 'I', TOOL_OWNER)
        sb.write(0x818C10, 'I', 3 if mode == 3 else 2)
        sb.write(0x818C14, 'I', 0)
        for address in LATCHES.values():
            sb.write(address, 'I', 0)
        line = case['line']
        start, end = line[0], line[-1]
        sb.write(0x818688, 'I', 1 if case['hover_is_start'] else 0)
        sb.write(0x81B6DC, 'I', case['price'][0])
        sb.write(0x81EA14, 'I', case['price'][1])
        sb.write(0x785A2C, 'I', 1 if case['bank'] is None else 0)
        sb.write(self.bank + 0xC, 'i', case['bank'] or 0)
        for address in (0x81AF4C, 0x8186D4, 0x81B604, 0x8187F8, 0x81B4CC, 0x8186D0):
            sb.write(address, 'I', 0)
        pointers = sb.alloc(8)
        sb.write(pointers, 'i', end[0])
        sb.write(pointers + 4, 'i', end[1])
        sb.executed = 0
        sb.call(0x536100, args=(mode | 0x100, start[0], start[1], pointers, pointers + 4))
        return {'faces': list(self.faces), 'latches': [sb.read(LATCHES[name], 'I') for name in ('l50', 'l58', 'l60', 'l4c', 'l5c', 'l48')], 'cost': sb.read(0x81AF4C, 'i')}
