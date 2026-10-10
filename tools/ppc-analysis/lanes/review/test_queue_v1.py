"""QUEUE-V: independent review of the queue stack cfae05b / 85f0163 / c213e81 (parent 12992e8).

This file has its own PEF reader (section table and pattern-initialised data), its own PowerPC field
decoder and its own float32 models. It does not import pef.py, timer_evidence.py or queue_evidence.py,
so a wrong assumption in the QUEUE-R lane shows up here as a disagreement.

- Models (always run): the queue limit of 0xdcb74, the move delay of 0xef868, the slot depth of 0xddcc4,
  the refund of ClearCell 0x85dcc, the boredom contradiction, and AdmissionCheck.Stalled.
- Binary (OPENTPW_MAC_BIN = the Feral bin directory, SimThemePark.data identified by SHA-256): every
  cited operand is decoded from instruction fields and data constants, and the D-form stores at guest
  +508/+520 are enumerated over the whole code section.
- Git (OPENTPW_REVIEW_REPO, a checkout holding 12992e8, c213e81, 8a88379 and 5ec2622; defaults to this
  repository): the changed GuestTests, the determinism source rules, the canonical-hash coverage of the
  new queue fields, and the merge onto main.

Bounded: static reading only, nothing original is executed. Behaviour claims (count invariants, edits,
breakdown, gate numbers) come from the scratch harnesses described in docs/reverse/REVIEW-QUEUE.md.
"""
from __future__ import annotations

import hashlib
import itertools
import math
import os
import re
import struct
import subprocess
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
DATA_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
TOC = 0x8000
BASE, QUEUE, GATE_MAIN, DET_MAIN = '12992e8', 'c213e81', '8a88379', '5ec2622'


# ---------------------------------------------------------------- float32 models

def f32(value: float) -> float:
    return struct.unpack('>f', struct.pack('>f', value))[0]


def queue_limit(has_queue: bool, qwtc: float, speed: int, init_speed: int, cap: int, dur: int) -> int:
    """0xdcb74: 100 with flag 0x8, else trunc(max(QWTC*(G*CAP)/DUR, 4)) with every step rounded to single."""
    if has_queue:
        return 100
    if speed == 0:
        g = 1.0
    else:
        g = f32(f32(speed) / f32(init_speed)) if init_speed else math.copysign(math.inf, speed)
    product = f32(g * f32(cap))
    product = f32(f32(qwtc) * product)
    try:
        quotient = f32(product / f32(dur))
    except ZeroDivisionError:
        quotient = math.nan if product == 0 else math.inf
    if not quotient > 4.0:  # fcmpo + bf gt: NaN and <= 4 both take 4.0
        quotient = 4.0
    if quotient >= 2.0 ** 32:  # 0x1c3fbc saturates
        return 0xFFFFFFFF
    return int(quotient)


def move_delay(position: int) -> int:
    """0xef868: trunc(1.2f * +497) with the single-precision product."""
    return int(f32(f32(1.2) * f32(position)))


def slot_depth(remaining: int) -> int:
    """0xddcc4: fctiwz(255f * (remaining * 0.25f)), both products single precision."""
    return int(f32(f32(255.0) * f32(f32(remaining) * f32(0.25))))


def refund(scrap_percent: int, queue_cell_cost: int) -> int:
    """ClearCell 0x85dcc: (scrap * cost) unsigned, then / 100 via 0x51EB851F >> 5."""
    product = (scrap_percent * queue_cell_cost) & 0xFFFFFFFF
    return ((product * 0x51EB851F) >> 32) >> 5


def bored(turn: int, entry_508: int, interlude_520: int) -> bool:
    """0xed58c..0xed674: the boredom leave fires only inside the window and past entry + 100."""
    return (turn - interlude_520) & 0xFFFFFFFF <= 30 and turn > entry_508 + 100


def stalled(conditions: bool, head_at_front: bool) -> bool:
    """c213e81 RideVisitorBridge.CheckAdmission: calledNow = head whenever conditions && atFront (head != 0)."""
    called_now = 7 if conditions and head_at_front else 0
    return conditions and head_at_front and called_now == 0


class Models(unittest.TestCase):
    def test_hasqueue_limit_and_formula(self):
        self.assertEqual(100, queue_limit(True, 130, 60, 60, 5, 30))  # Belly Bounce: QWTC unused
        self.assertEqual(21, queue_limit(False, 130, 60, 60, 5, 30))  # 130*5/30 = 21.67 -> 21
        self.assertEqual(4, queue_limit(False, 10, 60, 60, 1, 30))  # floor 4
        self.assertEqual(4, queue_limit(False, 0, 0, 0, 0, 0))  # 0/0 = NaN -> 4
        self.assertEqual(43, queue_limit(False, 130, 120, 60, 5, 30))  # G = 2
        self.assertEqual(0xFFFFFFFF, queue_limit(False, 130, 60, 60, 5, 0))  # x/0 = inf saturates

    def test_single_precision_matters_only_at_the_edge(self):
        # 0.1f * 30 * 1 / 1 in single is 3.0000001 -> floor of 4 hides it; above 4 trunc follows the f32 value.
        self.assertEqual(int(f32(f32(f32(1.4) * f32(5.0)) / f32(1.0))), queue_limit(False, 1.4, 0, 0, 5, 1))

    def test_move_delay(self):
        self.assertEqual([0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 12], [move_delay(p) for p in range(11)])
        self.assertEqual(2, move_delay(2))  # the front guest with a gap of 2 waits at most 2 turns
        self.assertEqual(118, move_delay(99))  # 118.8

    def test_depth_bytes(self):
        self.assertEqual([0, 63, 127, 191], [slot_depth(r) for r in range(4)])

    def test_refund_matches_integer_division(self):
        for scrap in range(0, 101):
            for cost in (0, 1, 20, 75, 99, 1000):
                self.assertEqual(scrap * cost // 100, refund(scrap, cost))

    def test_boredom_needs_contradictory_fields(self):
        # In state 11, +508 >= +520 (every +520 write is followed by a state-11 re-entry writing +508).
        for turn, interlude in itertools.product(range(0, 400, 7), range(0, 400, 11)):
            for entry in range(interlude, turn + 1, 13):
                self.assertFalse(bored(turn, entry, interlude))
        self.assertTrue(bored(200, 50, 180))  # only a +508 older than +520 - 70 revives it

    def test_stalled_is_structurally_false(self):
        self.assertFalse(any(stalled(c, a) for c, a in itertools.product((False, True), repeat=2)))


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
            if op == 3:
                common = packed[pos:pos + count]
                pos += count
            else:
                common = bytes(count)
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


def signed16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


class Code:
    def __init__(self, code: bytes, data: bytes):
        self.code, self.data = code, data

    def word(self, at: int) -> int:
        return struct.unpack_from('>I', self.code, at)[0]

    def d_form(self, at: int) -> tuple[int, int, int, int]:
        w = self.word(at)
        return w >> 26, (w >> 21) & 31, (w >> 16) & 31, signed16(w & 0xFFFF)

    def rlwinm_mask(self, at: int) -> int:
        w = self.word(at)
        assert w >> 26 == 21, hex(at)
        mb, me = (w >> 6) & 31, (w >> 1) & 31
        bits = [(31 - b) for b in (range(mb, me + 1) if mb <= me else list(range(mb, 32)) + list(range(0, me + 1)))]
        return sum(1 << b for b in bits)

    def call(self, at: int) -> int:
        w = self.word(at)
        assert w >> 26 == 18 and w & 1, hex(at)
        li = w & 0x03FFFFFC
        return at + (li - 0x04000000 if li & 0x02000000 else li)

    def cond_branch(self, at: int) -> tuple[int, int, int]:
        w = self.word(at)
        assert w >> 26 == 16, hex(at)
        return (w >> 21) & 31, (w >> 16) & 31, at + signed16(w & 0xFFFC)

    def float_op(self, at: int) -> tuple[int, int]:
        w = self.word(at)
        return w >> 26, (w >> 1) & (31 if w >> 26 == 59 else 0x3FF)

    def toc_f32(self, d: int) -> float:
        return struct.unpack_from('>f', self.data, TOC + d)[0]

    def toc_u64(self, d: int) -> int:
        return struct.unpack_from('>Q', self.data, TOC + d)[0]


STORES = {36: 4, 37: 4, 38: 1, 39: 1, 44: 2, 45: 2, 52: 4, 53: 4, 54: 8, 55: 8}
# Every D-form store (base register not r1) whose bytes overlap guest +508..511 or +520..523, whole code section.
EXPECTED_WRITERS = {
    0x5ec80, 0x690f8, 0x698d4, 0x6aea4, 0x6b11c, 0xe771c, 0xe7728, 0xe79a0, 0xe8c0c, 0xec534,
    0xec568, 0xecf50, 0xef884, 0xefd30, 0xf2a7c, 0xf2a84, 0xf3198, 0xf31a4, 0xf3218, 0xf3ac0,
    0xf4e54, 0xf4e8c, 0x1461b0, 0x14636c, 0x192280, 0x192308, 0x1bf0cc, 0x1bf0e4}
# Writers the QUEUE-plan section 5.3 enumeration does not name (by function range or address).
UNLISTED_WRITERS = {0x5ec80: 'stfs +508 in 0x5eb20 (84-byte record initialiser)',
                    0xf3ac0: 'stw 0,+508 in 0xf39f8 (called from 0xf8dc4; just past the cited 0xf29fc..0xf39f8)',
                    0xf4e54: 'sth +520 in 0xf4e40 (setter of +518/+520)', 0xf4e8c: 'sth +520 in 0xf4e70',
                    0x1461b0: 'sth +510 (overlaps +508 low half) in 0x1460f8', 0x14636c: 'sth +510 in 0x146228'}


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set to the Feral bin directory')
class Binary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        raw = (Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data').read_bytes()
        if hashlib.sha256(raw).hexdigest() != DATA_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.c = Code(*load_sections(raw))

    def test_data_constants(self):
        c = self.c
        self.assertEqual((0.25, 255.0, 4.0, 1.0, 0.0), tuple(c.toc_f32(d) for d in (-11048, -11044, -11032, -11028, -11012)))
        self.assertEqual(f32(1.2), c.toc_f32(-10848))  # data 0x55a0
        self.assertEqual(0x4330000000000000, c.toc_u64(-11024))  # unsigned int -> double magic
        self.assertEqual(0x4330000080000000, c.toc_u64(-11040))  # signed int -> double magic

    def test_queue_limit_0xdcb74(self):
        c = self.c
        self.assertEqual((40, 0, 3, 46), c.d_form(0xdcba4))
        self.assertEqual(0x8, c.rlwinm_mask(0xdcba8))
        self.assertEqual((14, 28, 0, 100), c.d_form(0xdcbb0))
        self.assertEqual((32, 0, 29, 84), c.d_form(0xdcbb8))  # SPEED, == 0 -> G = 1.0 (lfs toc 0x54ec)
        self.assertEqual((48, 0, 2, -11028), c.d_form(0xdcc28))
        self.assertEqual(0x118018, c.call(0xdcbd4))  # type record
        self.assertEqual((34, 0, 29, 76), c.d_form(0xdcbd8))  # level byte, slwi 6 = stride 64
        self.assertEqual((32, 0, 3, 424), c.d_form(0xdcbf4))  # InitSpeed
        self.assertEqual((59, 18), c.float_op(0xdcc1c))  # fdivs: G single
        self.assertEqual((48, 31, 3, 436), c.d_form(0xdcc58))  # lfs QWTC
        self.assertEqual((34, 0, 29, 88), c.d_form(0xdcca4))  # DUR
        self.assertEqual((34, 4, 29, 89), c.d_form(0xdcc9c))  # CAP
        self.assertEqual((59, 25), c.float_op(0xdcccc))  # fmuls G*CAP
        self.assertEqual((59, 25), c.float_op(0xdccd8))  # fmuls QWTC*(G*CAP)
        self.assertEqual((59, 18), c.float_op(0xdccdc))  # fdivs / DUR
        self.assertEqual((63, 32), c.float_op(0xdcce0))  # fcmpo quotient, 4.0
        bo, bi, target = c.cond_branch(0xdcce4)
        self.assertEqual((4, 1, 0xdccec), (bo, bi, target))  # branch if not gt (NaN included) ...
        self.assertEqual((63, 72), c.float_op(0xdccec))  # ... to fmr f1 = 4.0
        self.assertEqual(0x1c3fbc, c.call(0xdccf0))
        self.assertEqual((63, 15), c.float_op(0x1c3ff4))  # the conversion helper truncates (fctiwz)

    def test_move_delay_0xef868_and_gap(self):
        c = self.c
        self.assertEqual((36, 3, 31, 508), c.d_form(0xef884))
        self.assertEqual((34, 3, 31, 497), c.d_form(0xef888))
        self.assertEqual((48, 2, 2, -10848), c.d_form(0xef894))  # 1.2f
        self.assertEqual((59, 25), c.float_op(0xef8a4))
        self.assertEqual(0x1c3fbc, c.call(0xef8a8))
        self.assertEqual((36, 3, 31, 500), c.d_form(0xef8ac))
        self.assertEqual((10, 0, 0, 2), c.d_form(0xed4a4))  # cmplwi gap, 2 (unsigned: negative gaps move)
        self.assertEqual((4, 1, 0xed4e4), c.cond_branch(0xed4a8))  # not gt -> decrement
        self.assertEqual((14, 0, 4, -1), c.d_form(0xed4e4))

    def test_admission_0xe1404(self):
        c = self.c
        self.assertEqual((32, 0, 29, 100), c.d_form(0xe1428))
        variables = [c.d_form(at)[3] for at in (0xe143c, 0xe1468, 0xe147c, 0xe148c, 0xe14d0)]
        self.assertEqual([2, 0, 2, 5, 9], variables)  # CAPACITY (log), LETMEON, CAPACITY, ONRIDE, RUNNING
        self.assertTrue(all(c.call(at) == 0xb5be0 for at in (0xe1440, 0xe146c, 0xe1480, 0xe1490, 0xe14d4)))
        self.assertEqual((12, 0, 0xe14cc), c.cond_branch(0xe1498))  # ONRIDE < CAPACITY skips the bypass test
        self.assertEqual(0xe2fe4, c.call(0xe14a0))  # bypass 1: type record +156 == 3
        self.assertEqual((32, 0, 3, 156), c.d_form(0xe3004))
        self.assertEqual((8, 0, 0, 3), c.d_form(0xe3008))  # subfic 3 then cntlzw: == 3
        self.assertEqual((32, 0, 3, 156), c.d_form(0xe14c0))  # bypass 2: +156 == 2
        self.assertEqual((11, 0, 0, 2), c.d_form(0xe14c4))
        self.assertEqual(0x100, c.rlwinm_mask(0xe14e4))  # RunsContinuously
        self.assertEqual((40, 4, 29, 104), c.d_form(0xe14f0))  # pending visitor
        self.assertEqual((40, 4, 29, 56), c.d_form(0xe1504))  # head
        self.assertEqual(0xee76c, c.call(0xe1544))
        self.assertEqual((11, 0, 0, 11), c.d_form(0xee774))  # head state 11
        self.assertEqual((34, 0, 3, 497), c.d_form(0xee77c))  # head +497 == 0
        self.assertEqual((44, 0, 29, 104), c.d_form(0xe1564))  # pending = head

    def test_slot_0xddcc4(self):
        c = self.c
        self.assertEqual((14, 21, 21, -4), c.d_form(0xddd30))
        self.assertEqual((10, 0, 21, 4), c.d_form(0xddd3c))
        self.assertEqual(((48, 0, 2, -11048), (48, 2, 2, -11044)), (c.d_form(0xddd74), c.d_form(0xddd7c)))
        self.assertEqual([(59, 25), (59, 25), (63, 15)], [c.float_op(at) for at in (0xddd84, 0xddd88, 0xddd8c)])
        self.assertEqual((10, 0, 0, 128), c.d_form(0xdddb4))  # depth > 128 takes the untraced 0xdde74 branch
        self.assertEqual(0x105328, c.call(0xddd98))
        magic = (c.d_form(0xddd9c)[3] << 16) + c.d_form(0xddda4)[3]
        self.assertEqual(0x24924925, magic)
        self.assertEqual((7, 0, 0, 28), c.d_form(0xdddc8))
        self.assertEqual((14, 25, 3, 114), c.d_form(0xdddd0))
        for value in range(0, 1 << 20, 997):  # the magic sequence is value mod 28
            q = (value * magic) >> 32
            self.assertEqual(value % 28, value - 28 * ((((value - q) >> 1) + q) >> 4))

    def test_boredom_branch_is_inside_the_window(self):
        c = self.c
        self.assertEqual((32, 0, 28, 520), c.d_form(0xed590))
        self.assertEqual((10, 0, 0, 30), c.d_form(0xed5a0))
        self.assertEqual((4, 1, 0xed66c), c.cond_branch(0xed5a4))  # w <= 30 -> boredom test
        self.assertEqual((32, 4, 28, 508), c.d_form(0xed66c))
        self.assertEqual((14, 0, 4, 100), c.d_form(0xed670))
        self.assertEqual(1, c.rlwinm_mask(0xed658))  # ProvidesRelief keeps the toilet-needing guest

    def test_writers_of_508_and_520(self):
        c = self.c
        found = set()
        for at in range(0, len(c.code), 4):
            op, _, base, d = c.d_form(at)
            if op in STORES and base != 1:
                if any(d < field + 4 and d + STORES[op] > field for field in (508, 520)):
                    found.add(at)
            elif op == 47 and base != 1:
                self.assertFalse(d <= 523 and d + 4 * (32 - c.d_form(at)[1]) > 508, f'stmw at {at:#x}')
        self.assertEqual(EXPECTED_WRITERS, found)
        plan = (REPO / 'docs/reverse/QUEUE-plan.md').read_text(encoding='utf-8')
        for at in UNLISTED_WRITERS:  # S4 (fixed by QUEUE-FIX): the plan now lists them
            self.assertIn(f'0x{at:x}', plan)

    def test_refund_clear_cell(self):
        c = self.c
        for site in (0x85dcc, 0x85ed4):
            self.assertEqual(0xe2424, c.call(site))  # age-based scrap percentage of the ride
            self.assertEqual(0x7bc70, c.call(site + 8))  # getter of a global (the queue cell cost)
            self.assertEqual((31, 235), (c.word(site + 12) >> 26, (c.word(site + 12) >> 1) & 0x1FF))  # mullw
            magic = ((c.d_form(site + 16)[3] & 0xFFFF) << 16) + c.d_form(site + 20)[3]
            self.assertEqual(0x51EB851F, magic)
            self.assertEqual(0xcbf50, c.call(site + 44))


# ---------------------------------------------------------------- git witnesses

def git(*args, check=True):
    root = os.environ.get('OPENTPW_REVIEW_REPO', str(REPO))
    result = subprocess.run(['git', '-C', root, *args], capture_output=True, check=False)
    if check and result.returncode:
        raise unittest.SkipTest(result.stderr.decode(errors='replace').strip() or 'git object missing')
    return result


def show(rev: str, path: str) -> str:
    return git('show', f'{rev}:{path}').stdout.decode('utf-8')


class Git(unittest.TestCase):
    def setUp(self):
        for rev in (BASE, QUEUE):
            git('cat-file', '-e', rev + '^{commit}')

    def test_changed_guest_tests_keep_their_assertions(self):
        old = show(BASE, 'source/OpenTPW.Tests/GuestTests.cs')
        new = show(QUEUE, 'source/OpenTPW.Tests/GuestTests.cs')
        events = lambda text: re.findall(r'new\[\] \{ ("offer 7"[^}]*) \}', text)[0]
        self.assertEqual(sorted(events(old).split(', ')), sorted(events(new).split(', ')))  # same events, new order
        self.assertLess(events(new).index('"release 7"'), events(new).index('"offer 8"'))
        body = lambda text: text[text.index('public void QueueRespectsItsLimit'):text.index('public void LimboOpcodes')]
        self.assertIn('MaximumQueueLength = 2', body(old))
        self.assertIn('QueueJoinResult.NoRoom', body(new))
        self.assertGreaterEqual(body(new).count('Assert.'), body(old).count('Assert.'))

    def test_queue_code_obeys_the_determinism_rules(self):
        diff = git('diff', BASE, QUEUE, '--', 'source/OpenTPW/World').stdout.decode('utf-8')
        added = '\n'.join(line[1:] for line in diff.splitlines() if line.startswith('+') and not line.startswith('+++'))
        for pattern in (r'new Random\s*\(', r'Random\s+\w+\s*=\s*new\s*\(', r'GetHashCode', r'\bHashCode\.',
                        r'static\s+(?!readonly)(?:int|long|float|double|ulong|uint)\s+\w+\s*[=;]', r'DateTime|Stopwatch|Environment\.TickCount',
                        r'foreach\s*\(\s*var\s+\w+\s+in\s+(?:links|seenQueueEdits)\b', r'(?:links|seenQueueEdits)\.(?:Keys|Values)'):
            self.assertIsNone(re.search(pattern, added), pattern)

    def test_admission_stall_signal_is_vacuous(self):
        bridge = show(QUEUE, 'source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        visitor = show(QUEUE, 'source/OpenTPW/World/Guests/IRideVisitorBridge.cs')
        self.assertIn('public bool Stalled => ConditionsHold && HeadAtFront && CalledGuest == 0;', visitor)
        check = bridge[bridge.index('private void CheckAdmission'):bridge.index('private void MarkBoarded')]
        self.assertIn('&& head != 0;', check)
        block = check[check.index('if ( conditions && atFront )'):check.index('var check = new AdmissionCheck')]
        self.assertIn('calledNow = head;', block)  # C && A  =>  CalledGuest = head != 0  =>  !Stalled

    def test_unregister_leaves_queue_fields_behind(self):
        simulation = show(QUEUE, 'source/OpenTPW/World/Guests/GuestSimulation.cs')
        unregister = simulation[simulation.index('public void Unregister'):simulation.index('private Guest Create()')]
        self.assertNotIn('ClearQueueState', unregister)
        to_path = simulation[simulation.index('private void ReturnToPath'):]
        self.assertNotIn('QueuePosition', to_path[:to_path.index('}')])

    def test_queue_fields_missing_from_the_state_hash(self):
        guest = show(QUEUE, 'source/OpenTPW/World/Guests/Guest.cs')
        simulation = show(QUEUE, 'source/OpenTPW/World/Guests/GuestSimulation.cs')
        hashed = simulation[simulation.index('public ulong ComputeStateHash'):]
        fields = ['QueuePosition', 'QueueJoinTurn', 'LastQueueWaitTurns', 'QueueMoveDelay', 'QueueCalled',
                  'QueueStandingSinceTurn', 'InterludeTurn', 'InQueueInterlude', 'QueueJoinHappiness', 'QueueCellIndex',
                  'QueueTargetIndex', 'QueueTargetX', 'QueueTargetY', 'LastQueueUpdateTurn']
        for field in fields:
            self.assertRegex(guest, rf'\b{field}\b')
        self.assertEqual(['QueuePosition', 'QueueMoveDelay'], [f for f in fields if f'guest.{f} ' in hashed or f'guest.{f})' in hashed])

    def test_merge_onto_det_main(self):
        git('cat-file', '-e', DET_MAIN + '^{commit}')
        result = git('merge-tree', '--write-tree', '--name-only', DET_MAIN, QUEUE, check=False)
        self.assertEqual(1, result.returncode)
        conflicted = set(result.stdout.decode().split('\n\n', 1)[0].splitlines()[1:])
        self.assertEqual({'docs/FIDELITY-REGISTER.md', 'source/OpenTPW/World/Guests/RideVisitorBridge.cs',
                          'source/OpenTPW/World/Level.Objects.cs', 'tools/fidelity_register.py'}, conflicted)
        main_bridge = show(DET_MAIN, 'source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        canonical = main_bridge[main_bridge.index('internal void AddCanonicalState'):]
        self.assertIn('hash.Add( offered );', canonical)  # both fields are removed by c213e81
        self.assertIn('AddList( queue );', canonical)

    def test_merge_onto_gate_main(self):
        git('cat-file', '-e', GATE_MAIN + '^{commit}')
        result = git('merge-tree', '--write-tree', '--name-only', GATE_MAIN, QUEUE, check=False)
        conflicted = set(result.stdout.decode().split('\n\n', 1)[0].splitlines()[1:])
        self.assertEqual({'docs/FIDELITY-REGISTER.md', 'source/OpenTPW/World/Level.Objects.cs', 'tools/fidelity_register.py'}, conflicted)
        gate = show(GATE_MAIN, 'source/OpenTPW/Client/M3Gate.cs')
        self.assertIn('guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.Boarding', gate)  # no MovingUpQueue


if __name__ == '__main__':
    unittest.main()
