"""PATH-V: independent review of the path stack d323398 (PATH-R) / d21fb4a (PATH-I) on 952ab0f.

This file has its own PEF reader (section table and pattern-initialised data) and its own PowerPC field
decoder. It does not import pef.py, ppcdis.py or path_evidence.py, so a wrong assumption in the PATH-R lane
shows up here as a disagreement.

- Models (always run): CanChangeCellType, the SetCellType money test, the snap and LayLine walk, and the
  commit's push/skip rule, each as decoded below; plus a synthetic mutation check of the decoders.
- Binary (OPENTPW_MAC_BIN = the Feral bin directory, SimThemePark.data identified by SHA-256): every cited
  operand is decoded from instruction fields. TOC slots are read as raw data words; they hold data-section
  offsets (the section relocation adds the section base), which the review cross-checked once against the
  repository's relocation reader.
- Git (OPENTPW_REVIEW_REPO, a checkout holding 952ab0f, d323398, d21fb4a and 5ec2622; defaults to this
  repository): the register, the hash pins, line endings and the two review findings that live in the
  source (the ending click commits; the front-end smoke sends one Back while the queue tool is active).

Bounded: static reading only, nothing original is executed. Behaviour claims (raw guest hashes, the pin
decomposition, smokes, probes) come from the scratch runs described in docs/reverse/REVIEW-PATH.md.
"""
from __future__ import annotations

import hashlib
import os
import re
import struct
import subprocess
import unittest
from pathlib import Path

REPO = Path(os.environ.get('OPENTPW_REVIEW_REPO') or Path(__file__).resolve().parents[4])
DATA_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
TOC = 0x8000
BASE, RESEARCH, IMPL, MAIN = '952ab0f', 'd323398', 'd21fb4a', '5ec2622'


# ---------------------------------------------------------------- models of the decoded routines

def can_change_cell_type(old: int, new: int, last_cell: bool) -> bool:
    """0x8408c, rule by rule in instruction order."""
    if new == 0:
        return True
    if new == 4 and old == 4:
        return False
    if new == 21 and old == 21:
        return True
    if new == 1 and old == 3:
        return True
    if new == old or old == 0:
        return True
    if new == 4 and old == 1:
        return True
    return new == 3 and old == 1 and last_cell


def affordable(balance: int, cost: int) -> bool:
    """0x82bc8..0x82bdc: subf r5 = balance - cost; srawi r3 = r5 >> 31; subfc (CA = 1); adde r4 = r3 + 0 + CA."""
    r5 = (balance - cost) & 0xFFFFFFFF
    sign = 0xFFFFFFFF if r5 & 0x80000000 else 0
    return (sign + 0 + 1) & 0xFFFFFFFF == 1


def snap(start: tuple[int, int], cursor: tuple[int, int]) -> tuple[int, int]:
    """0x6f4d8 / 0x71010: |dx| < |dy| skips the y reset, so a tie keeps X; the second compare resets x."""
    (sx, sy), (cx, cy) = start, cursor
    if not abs(cx - sx) < abs(cy - sy):
        cy = sy
    if not abs(cx - sx) > abs(cy - sy):
        cx = sx
    return cx, cy


def lay_line(start: tuple[int, int], end: tuple[int, int]) -> list[tuple[int, int]]:
    """0x84ea4: strict |dx| > |dy| walks X on row y0, else Y on column x0; the end coordinate off-axis is ignored."""
    (x0, y0), (x1, y1) = start, end
    if abs(x1 - x0) > abs(y1 - y0):
        step = -1 if x0 > x1 else 1
        return [(x, y0) for x in range(x0, x1 + step, step)]
    step = -1 if y0 > y1 else 1
    return [(x0, y) for y in range(y0, y1 + step, step)]


def in_cell_array(x: int, y: int) -> bool:
    """0xd70d8: 0 <= x < 128 and 0 <= y < 128 (LayLine skips a cell that fails, it does not stop)."""
    return 0 <= x < 128 and 0 <= y < 128


def commit_lays_line(start: tuple[int, int], end: tuple[int, int], vertex_count: int) -> bool:
    """0x7105c..0x71084: the commit LayLine runs when end != start, or when exactly one vertex is stored."""
    return end != start or vertex_count == 1


class Models(unittest.TestCase):
    def test_can_change_cell_type_for_path_and_queue(self):
        self.assertTrue(can_change_cell_type(0, 1, False))
        self.assertTrue(can_change_cell_type(1, 1, False))
        self.assertTrue(can_change_cell_type(3, 1, False), 'path over queue passes 0x8408c (the validator refuses it)')
        self.assertFalse(can_change_cell_type(1, 3, False))
        self.assertTrue(can_change_cell_type(1, 3, True), 'queue over path only with the last-cell flag')
        self.assertFalse(can_change_cell_type(4, 4, False))
        for old in (2, 4, 5, 7, 9, 10, 11, 16, 21, 24, 30):
            self.assertFalse(can_change_cell_type(old, 1, True), old)

    def test_money_test_is_signed_balance_minus_cost_at_least_zero(self):
        self.assertTrue(affordable(20, 20))
        self.assertFalse(affordable(19, 20))
        self.assertTrue(affordable(-5, -10))
        self.assertFalse(affordable(-1, 0))

    def test_snap_tie_keeps_x_and_lines_are_straight(self):
        self.assertEqual(snap((5, 5), (8, 8)), (8, 5))
        self.assertEqual(snap((5, 5), (6, 9)), (5, 9))
        self.assertEqual(snap((5, 5), (9, 6)), (9, 5))
        self.assertEqual(lay_line((5, 5), (8, 9)), [(5, y) for y in range(5, 10)])
        self.assertEqual(lay_line((5, 5), (5, 5)), [(5, 5)])
        self.assertEqual(lay_line((8, 2), (5, 2)), [(8, 2), (7, 2), (6, 2), (5, 2)])

    def test_cell_array_bound_and_commit_skip(self):
        self.assertTrue(in_cell_array(127, 0))
        self.assertFalse(in_cell_array(128, 0))
        self.assertFalse(in_cell_array(0, -1))
        self.assertTrue(commit_lays_line((1, 1), (1, 1), 1), 'first click on the start lays one cell')
        self.assertFalse(commit_lays_line((4, 1), (4, 1), 2), 'the ending click lays nothing')


# ---------------------------------------------------------------- independent PEF and PPC decoding

def read_arg(packed: bytes, pos: int) -> tuple[int, int]:
    value = 0
    while True:
        byte = packed[pos]
        pos += 1
        value = (value << 7) | (byte & 0x7F)
        if not byte & 0x80:
            return value, pos


def unpack_pattern_data(packed: bytes, size: int) -> bytes:
    """PEF pattern-initialised data (opcodes 0..4), written from the format description."""
    out = bytearray()
    pos = 0
    while pos < len(packed):
        byte = packed[pos]
        pos += 1
        op, count = byte >> 5, byte & 0x1F
        if count == 0:
            count, pos = read_arg(packed, pos)
        if op == 0:
            out += bytes(count)
        elif op == 1:
            out += packed[pos:pos + count]
            pos += count
        elif op == 2:
            repeat, pos = read_arg(packed, pos)
            block = packed[pos:pos + count]
            pos += count
            out += block * (repeat + 1)
        elif op in (3, 4):
            custom, pos = read_arg(packed, pos)
            repeat, pos = read_arg(packed, pos)
            common = packed[pos:pos + count] if op == 3 else bytes(count)
            pos += count if op == 3 else 0
            out += common
            for _ in range(repeat):
                out += packed[pos:pos + custom]
                pos += custom
                out += common
        else:
            raise ValueError(f'pattern opcode {op}')
    if len(out) != size:
        raise ValueError(f'unpacked {len(out)} bytes, header says {size}')
    return bytes(out)


def load_sections(raw: bytes) -> tuple[bytes, bytes]:
    if raw[:12] != b'Joy!peffpwpc':
        raise ValueError('not a PowerPC PEF container')
    count = struct.unpack_from('>H', raw, 32)[0]
    code = data = None
    for index in range(count):
        _, _, _, unpacked, packed, offset, kind = struct.unpack_from('>iIIIIIB', raw, 40 + 28 * index)
        body = raw[offset:offset + packed]
        if kind == 0 and code is None:
            code = body
        elif kind == 2 and data is None:
            data = unpack_pattern_data(body, unpacked)
    return code, data


def s16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


class Code:
    def __init__(self, code: bytes, data: bytes):
        self.code, self.data = code, data

    def word(self, at: int) -> int:
        return struct.unpack_from('>I', self.code, at)[0]

    def fields(self, at: int) -> tuple[int, int, int, int]:
        """(opcode, rt/bo, ra/bi, signed 16-bit immediate)."""
        w = self.word(at)
        return w >> 26, (w >> 21) & 31, (w >> 16) & 31, s16(w & 0xFFFF)

    def cmpwi(self, at: int) -> tuple[int, int]:
        op, crf, ra, imm = self.fields(at)
        assert op == 11 and crf >> 2 == 0, hex(at)
        return ra, imm

    def cmpw(self, at: int) -> tuple[int, int]:
        w = self.word(at)
        assert w >> 26 == 31 and (w >> 1) & 0x3FF == 0, hex(at)
        return (w >> 16) & 31, (w >> 11) & 31

    def li(self, at: int) -> tuple[int, int]:
        op, rt, ra, imm = self.fields(at)
        assert op == 14 and ra == 0, hex(at)
        return rt, imm

    def call(self, at: int) -> int:
        w = self.word(at)
        assert w >> 26 == 18 and w & 1, hex(at)
        li = w & 0x03FFFFFC
        return at + (li - 0x04000000 if li & 0x02000000 else li)

    def cond(self, at: int) -> tuple[int, int, int]:
        """(BO, BI, target)."""
        w = self.word(at)
        assert w >> 26 == 16, hex(at)
        return (w >> 21) & 31, (w >> 16) & 31, at + s16(w & 0xFFFC)

    def toc_slot(self, at: int) -> int:
        """lwz rX, d(r2): the data-section offset the slot points at."""
        op, _, ra, d = self.fields(at)
        assert op == 32 and ra == 2, hex(at)
        return struct.unpack_from('>I', self.data, TOC + d)[0]

    def calls_in(self, start: int, end: int) -> set[int]:
        return {self.call(at) for at in range(start, end, 4) if self.word(at) >> 26 == 18 and self.word(at) & 1}


BRANCH_IF_FALSE, BRANCH_IF_TRUE = 4, 12
LT, GT, EQ = 0, 1, 2


def can_change_problems(c: Code) -> list[str]:
    """CanChangeCellType 0x8408c: (new compare, old compare, result) per rule, in order."""
    problems = []
    expected = [
        (0x8408c, None, (4, 0), None, 1),
        (0x8409c, 0x840a8, (4, 4), (0, 4), 0),
        (0x840b8, 0x840c4, (4, 21), (0, 21), 1),
        (0x840d4, 0x840e0, (4, 1), (0, 3), 1),
    ]
    for new_at, old_at, new_cmp, old_cmp, result in expected:
        if c.cmpwi(new_at) != new_cmp:
            problems.append(f'{new_at:#x} new compare {c.cmpwi(new_at)}')
        if old_at is not None and c.cmpwi(old_at) != old_cmp:
            problems.append(f'{old_at:#x} old compare {c.cmpwi(old_at)}')
        result_at = (old_at or new_at) + 8
        if c.li(result_at) != (3, result):
            problems.append(f'{result_at:#x} result {c.li(result_at)}')
    if c.cmpw(0x840f4) != (4, 0) or c.cmpwi(0x840fc) != (0, 0) or c.li(0x84104) != (3, 1):
        problems.append('rule 5 (same type or empty)')
    if c.cmpwi(0x8410c) != (4, 4) or c.cmpwi(0x84114) != (0, 1) or c.li(0x8411c) != (3, 1):
        problems.append('rule 6 (4 over path)')
    if c.cmpwi(0x84124) != (4, 3) or c.cmpwi(0x8412c) != (0, 1) or c.toc_slot(0x84134) != 0x84b2c or c.li(0x84144) != (3, 1):
        problems.append('rule 7 (queue over path with data:0x84b2c)')
    if c.li(0x8414c) != (3, 0):
        problems.append('default refusal')
    return problems


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set to the Feral bin directory')
class Binary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        raw = (Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data').read_bytes()
        if hashlib.sha256(raw).hexdigest() != DATA_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.c = Code(*load_sections(raw))

    def test_can_change_cell_type_seven_rules(self):
        self.assertEqual(can_change_problems(self.c), [])

    def test_decoder_catches_a_mutated_rule(self):
        code = bytearray(self.c.code)
        struct.pack_into('>I', code, 0x840e0, (self.c.word(0x840e0) & 0xFFFF0000) | 9)  # path over 9, not over queue
        self.assertTrue(can_change_problems(Code(bytes(code), self.c.data)))
        struct.pack_into('>I', code, 0x840e0, self.c.word(0x840e0))
        struct.pack_into('>I', code, 0x8414c, (self.c.word(0x8414c) & 0xFFFF0000) | 1)  # default allows
        self.assertTrue(can_change_problems(Code(bytes(code), self.c.data)))

    def test_set_cell_type_counter_prices_and_money(self):
        c = self.c
        self.assertEqual(c.call(0x82af4), 0x8408c)
        # Same nonzero type: lha +32, +1, sth +32, then success (no Spend on this branch).
        self.assertEqual(c.cmpw(0x82b0c), (0, 26))
        self.assertEqual(c.fields(0x82b14), (42, 3, 25, 32))
        self.assertEqual(c.fields(0x82b18), (14, 0, 3, 1))
        self.assertEqual(c.fields(0x82b1c), (44, 0, 25, 32))
        self.assertEqual(c.word(0x82b20) >> 26, 18)
        # Type 1 priced by 0x7bc58 (slot -> data:0x84ad4), type 3 by 0x7bc70 (-> data:0x84ad0), both unless data:0x7de2d.
        self.assertEqual(c.cmpwi(0x82b3c), (26, 1))
        self.assertEqual(c.call(0x82b50), 0x7bc58)
        self.assertEqual(c.cmpwi(0x82b58), (26, 3))
        self.assertEqual(c.call(0x82b6c), 0x7bc70)
        self.assertEqual(c.toc_slot(0x82ad4), 0x7de2d)
        self.assertEqual(c.toc_slot(0x7bc58), 0x84ad4)
        self.assertEqual(c.toc_slot(0x7bc70), 0x84ad0)
        # Loader 0x10f29c: balance +1472 -> setter 0x7bc4c (data:0x84ad4), +1468 -> 0x7bc64 (data:0x84ad0).
        self.assertEqual(c.fields(0x10f3cc)[3], 1472)
        self.assertEqual(c.call(0x10f3d0), 0x7bc4c)
        self.assertEqual(c.fields(0x10f3d4)[3], 1468)
        self.assertEqual(c.call(0x10f3d8), 0x7bc64)
        self.assertEqual(c.toc_slot(0x7bc4c), 0x84ad4)
        self.assertEqual(c.toc_slot(0x7bc64), 0x84ad0)
        # Money: game+36 gate, balance 0xcbf48, subf / srawi / subfc / adde (signed balance - cost >= 0).
        self.assertEqual(c.fields(0x82bb0)[3], 36)
        self.assertEqual(c.call(0x82bc4), 0xcbf48)
        self.assertEqual(c.word(0x82bc8), 0x7CBB1850)  # subf r5, r27, r3
        self.assertEqual(c.word(0x82bd0), 0x7CA3FE70)  # srawi r3, r5, 31
        self.assertEqual(c.word(0x82bd8), 0x7C002810)  # subfc r0, r0, r5
        self.assertEqual(c.word(0x82bdc), 0x7C832114)  # adde r4, r3, r4
        # Spend 0xcbfdc for type 1 and type 3.
        self.assertEqual(c.call(0x82cec), 0xcbfdc)
        self.assertEqual(c.call(0x82d2c), 0xcbfdc)

    def test_lay_line_axis_and_skip(self):
        c = self.c
        self.assertEqual(c.cmpw(0x84f08), (3, 26))
        bo, bi, target = c.cond(0x84f0c)
        self.assertEqual((bo, bi & 3, target), (BRANCH_IF_FALSE, GT, 0x85038), 'strict |dx| > |dy| walks X')
        for at, skip in ((0x84f3c, 0x84fa0), (0x85060, 0x850c4)):
            self.assertEqual(c.call(at), 0xd70d8)
            bo, bi, target = c.cond(at + 8)
            self.assertEqual((bo, bi & 3, target), (BRANCH_IF_TRUE, EQ, skip), 'a cell outside the array is skipped')
        self.assertEqual(c.cmpwi(0xd7100), (3, 128))
        self.assertEqual(c.cmpwi(0xd7114), (4, 128))
        self.assertEqual(c.toc_slot(0x84eb4), 0x84b2c)
        self.assertEqual(c.toc_slot(0x84eac), 0x84b30)
        self.assertNotIn(0xcbfdc, c.calls_in(0x84ea4, 0x85174))

    def test_snap_tie_keeps_x_in_preview_and_commit(self):
        c = self.c
        for compare, branch, reset_y in ((0x6f4d8, 0x6f4dc, 0x6f4e8), (0x71010, 0x71014, 0x71020)):
            self.assertEqual(c.cmpw(compare), (3, 19 if compare < 0x70000 else 20))
            bo, bi, target = c.cond(branch)
            self.assertEqual((bo, bi & 3, target), (BRANCH_IF_TRUE, LT, reset_y), '|dx| < |dy| skips end.y = start.y')

    def test_commit_push_skip_and_end_flag(self):
        c = self.c
        # end == start and count != 1 jumps past the push and every LayLine to the mode-3 test.
        self.assertEqual(c.call(0x7107c), 0x7bce8)
        self.assertEqual(c.cmpwi(0x71080), (3, 1))
        bo, bi, target = c.cond(0x71084)
        self.assertEqual((bo, bi & 3, target), (BRANCH_IF_FALSE, EQ, 0x71174))
        self.assertEqual(c.call(0x71090), 0x7bcf4)
        self.assertEqual(c.call(0x710c0), 0x84ea4, 'the raw-mode commit LayLine')
        # The session-end flag (sp+2380) is cntlzw of the LAST LayLine's result: the ghost clear li r3, 0x81.
        self.assertEqual(c.li(0x71160), (3, 0x81))
        self.assertEqual(c.call(0x71164), 0x84ea4)
        self.assertEqual(c.word(0x71168), 0x7C600034)  # cntlzw r0, r3
        self.assertEqual(c.fields(0x71170), (36, 0, 1, 2380))
        self.assertEqual(c.fields(0x7123c)[3], 2380)

    def test_vertex_stack_bound(self):
        c = self.c
        self.assertEqual(c.toc_slot(0x7bcf4), 0x84ab8)
        self.assertEqual(c.toc_slot(0x7bcf8), 0x82ab8)
        self.assertEqual(c.cmpwi(0x7bd00), (0, 1024))
        bo, bi, target = c.cond(0x7bd04)
        self.assertEqual((bo, bi & 3, target), (BRANCH_IF_FALSE, GT, 0x7bd10), 'refused only when count > 1024')
        self.assertEqual(0x82ab8 + 1024 * 8, 0x84ab8, 'a 1025th push lands on the count word')

    def test_clear_cell_path_case(self):
        c = self.c
        self.assertEqual(c.call(0x85a2c), 0x6e110)
        self.assertEqual(c.word(0x85a0c), 0x540006B5)  # rlwinm. r0, r0, 0, 26, 26 (flag 0x20)
        self.assertEqual(c.li(0x85aac), (0, -1))
        self.assertEqual(c.fields(0x85ab0), (44, 0, 31, 32))
        self.assertEqual(c.call(0x85c90), 0x82ac4)
        calls = c.calls_in(0x85a9c, 0x85ca0)
        self.assertNotIn(0xcbf50, calls, 'no Earn in the path case')
        self.assertIn(0xdd57c, calls)
        # 0x6e110 counts all eight bits of the link byte (+12), not linked path neighbours.
        bits = set()
        for at in range(0x6e140, 0x6e198, 12):
            w = c.word(at)
            mb, me = (w >> 6) & 31, (w >> 1) & 31
            self.assertEqual((w >> 26, mb, w & 1), (21, me, 1), hex(at))
            bits.add(31 - mb)
            self.assertEqual(c.fields(at + 8)[:3], (14, 5, 5) if at > 0x6e140 else (14, 5, 0), hex(at + 8))
        self.assertEqual(bits, set(range(8)))

    def test_initial_path_is_type_1_with_nomodify(self):
        c = self.c
        self.assertEqual(c.fields(0x855c4), (34, 31, 29, 38))
        self.assertEqual(c.word(0x8560c), 0x57E00739)  # rlwinm. r0, r31, 0, 28, 28 (bit 0x08)
        self.assertEqual(c.li(0x85614), (0, 1))
        self.assertEqual(c.call(0x85630), 0x82d6c)
        self.assertEqual(c.li(0x85634), (0, 32))
        self.assertEqual(c.fields(0x85638), (44, 0, 29, 14))
        self.assertEqual(c.word(0x85668), 0x60630040)  # ori r3, r3, 0x40

    def test_free_byte_is_a_transient_tool_flag(self):
        c = self.c
        # Hover routine: r13 = slot 0x1188 (data:0x7de2d); mode 4 stores 1 through it.
        self.assertEqual(c.toc_slot(0x6f3b0), 0x7de2d)
        self.assertEqual(c.li(0x6f5dc), (3, 4))
        self.assertEqual(c.fields(0x6f5f4), (38, 0, 13, 0))
        for load, li, store, value in ((0x71324, 0x71328, 0x71330, 1), (0x71cac, 0x71cb0, 0x71cb8, 1),
                                       (0x70c9c, 0x70c98, 0x70ca4, 0), (0x71c8c, 0x71c90, 0x71c94, 0), (0x722e4, 0x722e8, 0x722ec, 0)):
            self.assertEqual(c.toc_slot(load), 0x7de2d)
            op, rs, ra, d = c.fields(store)
            self.assertEqual((op, ra, d), (38, c.fields(load)[1], 0), hex(store))
            self.assertEqual(c.li(li), (rs, value), hex(li))
        self.assertEqual(c.li(0x71c9c), (3, 59))


# ---------------------------------------------------------------- repository checks

def git(*args: str) -> str:
    return subprocess.run(['git', '-C', str(REPO), *args], check=True, capture_output=True).stdout.decode('utf-8', 'replace')


def show(rev: str, path: str) -> str:
    return git('show', f'{rev}:{path}')


def has_revisions() -> bool:
    try:
        for rev in (BASE, RESEARCH, IMPL, MAIN):
            git('cat-file', '-e', f'{rev}^{{commit}}')
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False


@unittest.skipUnless(has_revisions(), 'the review revisions are not in this checkout')
class Git(unittest.TestCase):
    def test_stack_shape(self):
        self.assertEqual(git('rev-parse', f'{IMPL}^').strip(), git('rev-parse', RESEARCH).strip())
        self.assertEqual(git('rev-parse', f'{RESEARCH}^').strip(), git('rev-parse', BASE).strip())

    def test_register_and_pins(self):
        register = show(IMPL, 'source/OpenTPW/World/PathApproximations.cs')
        self.assertEqual(re.findall(r'\("(PATH-\d{3})"', register), [f'PATH-{n:03}' for n in range(1, 11)])
        self.assertIn('"PATH": "source/OpenTPW/World/PathApproximations.cs"', show(IMPL, 'tools/fidelity_register.py'))
        self.assertIn('**186 unresolved unique APPROX IDs**', show(IMPL, 'docs/FIDELITY-REGISTER.md'))
        self.assertIn('**176 unresolved unique APPROX IDs**', show(BASE, 'docs/FIDELITY-REGISTER.md'))
        self.assertIn('0xE67AA45A94F4B20CUL', show(BASE, 'source/OpenTPW.Tests/DeterminismTests.cs'))
        self.assertIn('0x10A80C328A477CBEUL', show(IMPL, 'source/OpenTPW.Tests/DeterminismTests.cs'))
        self.assertIn('SchemaVersion = 5;', show(IMPL, 'source/OpenTPW/World/WorldStateHash.cs'))
        self.assertIn('public int Version => Cells.Version;', show(IMPL, 'source/OpenTPW/World/Guests/GuestPathGrid.cs'))
        self.assertIn('hash.Add( seenGridVersion );', show(IMPL, 'source/OpenTPW/World/Guests/GuestSimulation.cs'))

    def test_line_endings_kept(self):
        plain = git('diff', '--numstat', BASE, IMPL)
        ignoring = git('diff', '--numstat', '--ignore-cr-at-eol', BASE, IMPL)
        self.assertEqual(plain, ignoring)
        for path in ('source/OpenTPW/Client/Game.cs', 'source/OpenTPW/Client/SandboxSmokeTest.cs'):
            self.assertIn('\r\n', show(BASE, path))
            self.assertIn('\r\n', show(IMPL, path))

    def test_finding_ending_click_commits(self):
        """B2: CellBuildTool.Click commits LayLine(start, start) even when the original skips it (0x71084)."""
        tool = show(IMPL, 'source/OpenTPW/World/CellBuildTool.cs')
        self.assertIn('if ( (end != start || vertices.Count == 1) && vertices.Count < VertexCapacity )', tool)
        self.assertIn('var result = Writer.Commit( start, end );', tool)
        self.assertLess(tool.index('if ( (end != start || vertices.Count == 1)'), tool.index('var result = Writer.Commit( start, end );'))

    def test_finding_front_end_smoke_sends_one_back(self):
        """B1: since QUEUE-I, a menu-chosen HasQueue ride enters the queue tool and the smoke's single Back closes it."""
        smoke = show(IMPL, 'source/OpenTPW/FrontEnd/FrontEndSmokeTest.cs')
        block = smoke[smoke.index('HUD door closes the selected original object'):smoke.index('Escape opens the pause menu')]
        self.assertEqual(block.count('UiKeys.Back'), 1)
        self.assertNotIn('CellTool', block)
        self.assertIn('level.EnterQueueTool( item.Visitors )', show(IMPL, 'source/OpenTPW/Hud/ParkHud.cs'))
        self.assertIn('level.QueueToolRide = item.Visitors;', show(BASE, 'source/OpenTPW/Hud/ParkHud.cs'))
        self.assertNotIn('QueueToolRide', show(MAIN, 'source/OpenTPW/Hud/ParkHud.cs'))

    def test_finding_remove_tool_click_starts_the_path_tool(self):
        """S1: UpdateCellTool does not stand aside for IsRemovingObjects, so a remove click on a path enters the tool."""
        hud = show(IMPL, 'source/OpenTPW/Hud/ParkHud.cs')
        body = hud[hud.index('private bool UpdateCellTool'):hud.index('private void DrawCellToolGhost')]
        self.assertIn('if ( paused || level.BuildEntry != null || level.IsPlacing )', body)
        self.assertNotIn('IsRemovingObjects', body)

    def test_python_paths_are_posix(self):
        evidence = show(IMPL, 'tools/ppc-analysis/lanes/path/path_evidence.py')
        self.assertIn('.as_posix()', evidence)
        self.assertNotIn('str(Path(__file__)', evidence)


if __name__ == '__main__':
    unittest.main()
