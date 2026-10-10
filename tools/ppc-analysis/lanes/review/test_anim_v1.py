"""Review ANIM-V: ANIM-I commit b84bf58 (UV tracks, record bits, channel mixing, the 14 animation opcodes).

- With OPENTPW_PPC_BIN_ROOT naming the identified Feral Mac bin directory, the ANIM-I static reads that
  anim_witness.py does not pin are decoded here with this file's own field split (only pef.py is shared):
  the D7 correction (the four plain triggers and script creation store 0xFFFF to the LOOPANIM token +168,
  the _CH handlers never touch +168), TRIGWAITANIM's first execution not clearing the yield word +152,
  TRIGANIMSPEED's out operand and integer deadline, LOOPANIM's equal-token exit before the deadline store,
  the UV copy-back mask (old clip AND NOT new clip), the UV add/set selection, and the UV key search.
- Two review findings are pinned from the binary: WAITANIM_CH adds its unsigned wait to the clock as a
  wrapping 32-bit add with no float step, so a clip under 300 ms gives a deadline in the past (AV-1); and
  every trigger handler returns 0 ms, not 1000 ms, when the script has no object at +200 (AV-2).
- A wider record-flag scan than anim_witness.py: every function in both containers that loads a clip's
  record table (+44 with the count +18 nearby) is scanned; none tests 0x10/0x20/0x40/0x100/0x800/0x2000/0x8000
  on a record flag word.
- Always (no binaries): the reference arithmetic of AV-1 and AV-2.

Nothing is executed from the original. Nothing here says anything about the PC build.
"""
from __future__ import annotations

import hashlib
import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
ENGINE_SHA256 = 'c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b'
BIN_ROOT = os.environ.get('OPENTPW_PPC_BIN_ROOT')
MASK32 = 0xFFFFFFFF
UNREAD_RECORD_BITS = {0x10, 0x20, 0x40, 0x100, 0x800, 0x2000, 0x8000}
EXIT = 0xb2354  # common handler exit of the RSE switch

# Handler ranges from the switch table (anim_witness.SWITCH order; each ends where the next begins).
HANDLERS = {
    'FLUSHANIM': (0xafa68, 0xafa94), 'TRIGANIM': (0xafa94, 0xafbbc), 'WAITANIM': (0xafbbc, 0xafd00),
    'LOOPANIM': (0xafd00, 0xafd94), 'TRIGWAITANIM': (0xafd94, 0xaff3c), 'GETANIM': (0xaff3c, 0xaffc4),
    'TRIGANIMSPEED': (0xaffc4, 0xb00f4), 'FLUSHANIM_CH': (0xb00f4, 0xb013c), 'TRIGANIM_CH': (0xb013c, 0xb0268),
    'WAITANIM_CH': (0xb0268, 0xb037c), 'LOOPANIM_CH': (0xb037c, 0xb0404), 'TRIGWAITANIM_CH': (0xb0404, 0xb05b8),
    'GETANIM_CH': (0xb05b8, 0xb0658), 'WAIT4ANIM': (0xb07a0, 0xb07e4),
}


def s16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


def d_form(word: int) -> tuple[int, int, int, int]:
    """(primary opcode, rD/rS, rA, signed immediate)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, s16(word & 0xFFFF)


def x_form(word: int) -> tuple[int, int, int, int, int]:
    """(primary opcode, rD/rS, rA, rB, extended opcode)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, (word >> 11) & 31, (word >> 1) & 0x3FF


def rotate_mask(mb: int, me: int) -> int:
    bits = range(mb, me + 1) if mb <= me else [*range(mb, 32), *range(0, me + 1)]
    mask = 0
    for bit in bits:
        mask |= 1 << (31 - bit)
    return mask


def mask_tested(word: int, register: int) -> int | None:
    """Mask of a recording rlwinm with shift 0, andi. or andis. on `register`, else None."""
    op = word >> 26
    if op == 21 and word & 1 and (word >> 21) & 31 == register and not (word >> 11) & 31:
        return rotate_mask((word >> 6) & 31, (word >> 1) & 31)
    if op == 28 and (word >> 21) & 31 == register:
        return word & 0xFFFF
    if op == 29 and (word >> 21) & 31 == register:
        return (word & 0xFFFF) << 16
    return None


def overwrites(word: int, register: int) -> bool:
    op = word >> 26
    if op in (14, 15, 7, 32, 33, 34, 35, 40, 41, 42, 43, 48, 50):  # addi addis mulli lwz lwzu lbz lbzu lhz lhzu lha lhau
        return (word >> 21) & 31 == register and op not in (48, 50)
    if op in (21, 24, 25, 26, 27, 28, 29):  # rlwinm ori oris xori xoris andi. andis.
        return (word >> 16) & 31 == register
    if op == 31:
        return register in ((word >> 21) & 31, (word >> 16) & 31) and x_form(word)[4] not in (151, 215, 407, 0, 32)
    return op == 18 and bool(word & 1)


class Code:
    def __init__(self, path: Path, sha256: str):
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != sha256:
            raise unittest.SkipTest(f'{path.name} is not the identified build')
        container = pef.PEFContainer(raw, path.name)
        self.code = bytes(container.code.data)
        self.words = struct.unpack(f'>{len(self.code) // 4}I', self.code)

    def w(self, at: int) -> int:
        return self.words[at // 4]

    def range(self, start: int, end: int):
        for at in range(start, end, 4):
            yield at, self.w(at)


def deadline_after_waitanim_ch(now: int, returned: int) -> int:
    """0xb0330-0xb0360: unsigned max(ret - 300, 300), then a 32-bit add to the clock (no float step)."""
    wait = (returned - 300) & MASK32
    if wait < 300:
        wait = 300
    return (now + wait) & MASK32


def waitanim_wait(returned: int, speed: float = 1.0) -> int:
    """0xafc78-0xafcc0: unsigned ret - 300 to single, / speed, fctiwz (saturating), at least 300 unsigned."""
    value = struct.unpack('>f', struct.pack('>f', float((returned - 300) & MASK32)))[0] / speed
    value = struct.unpack('>f', struct.pack('>f', value))[0]
    converted = 0x7FFFFFFF if value >= 2147483648.0 else int(value)
    return converted if (converted & MASK32) >= 300 else 300


class ReferenceArithmetic(unittest.TestCase):
    def test_av1_waitanim_ch_short_clip_deadline_is_in_the_past(self):
        # Crys_aM-sized clip (166 ms) at clock 10 s: WAITANIM waits INT32_MAX, WAITANIM_CH continues at once.
        self.assertEqual(waitanim_wait(166), 0x7FFFFFFF)
        deadline = deadline_after_waitanim_ch(10_000, 166)
        self.assertEqual(deadline, 10_000 - 134)
        self.assertFalse(10_000 < deadline, 'the resume compare (cmplw now, deadline) releases at once')
        # Only before 134 ms of clock does the add not wrap below now.
        self.assertGreater(deadline_after_waitanim_ch(100, 166), 100)

    def test_av2_no_object_trigger_returns_zero(self):
        # TRIGANIM-family out = max(0 - 300, 300) signed = 300; WAITANIM with ret 0 waits INT32_MAX.
        self.assertEqual(max(0 - 300, 300), 300)
        self.assertEqual(waitanim_wait(0), 0x7FFFFFFF)
        # OpenTPW's no-host policy uses the missing-member result instead: out 700, WAITANIM 700.
        self.assertEqual(max(1000 - 300, 300), 700)
        self.assertEqual(waitanim_wait(1000), 700)


@unittest.skipUnless(BIN_ROOT, 'set OPENTPW_PPC_BIN_ROOT to the identified Feral Mac bin directory')
class Binary(unittest.TestCase):
    app: Code
    engine: Code

    @classmethod
    def setUpClass(cls):
        root = Path(BIN_ROOT)
        cls.app = Code(root / 'SimThemePark.data', APP_SHA256)
        cls.engine = Code(root / 'libraries' / 'engine_shared.data', ENGINE_SHA256)

    # ---- helpers -------------------------------------------------------------------------
    def stores_to(self, start: int, end: int, displacement: int, base: int = 31) -> list[int]:
        found = []
        for at, word in self.app.range(start, end):
            op, _, ra, d = d_form(word)
            if op in (36, 44) and ra == base and d == displacement:  # stw, sth
                found.append(at)
        return found

    def touches(self, start: int, end: int, displacement: int, base: int = 31) -> list[int]:
        found = []
        for at, word in self.app.range(start, end):
            op, _, ra, d = d_form(word)
            if op in (32, 36, 40, 44) and ra == base and d == displacement:
                found.append(at)
        return found

    def constant_in(self, register: int, before: int, start: int) -> int:
        """Value of `register` built by `lis rY, hi` + `addi register, rY, lo` (or `li`) between start and before."""
        value = None
        for at, word in self.app.range(start, before):
            op, rd, ra, imm = d_form(word)
            if op == 14 and rd == register:
                if ra == 0:
                    value = imm & MASK32
                else:
                    base = self.lis_value(ra, start, at)
                    value = None if base is None else (base + imm) & MASK32
            elif overwrites(word, register):
                value = None
        if value is None:
            self.fail(f'no constant for r{register} before {before:#x}')
        return value

    def lis_value(self, register: int, start: int, before: int) -> int | None:
        value = None
        for at, word in self.app.range(start, before):
            op, rd, ra, imm = d_form(word)
            if op == 15 and rd == register and ra == 0:
                value = (imm << 16) & MASK32
            elif overwrites(word, register):
                value = None
        return value

    def branch_target(self, at: int) -> int:
        word = self.app.w(at)
        self.assertEqual(word >> 26, 16, f'{at:#x} is not bc')
        return at + s16(word & 0xFFFC)

    # ---- D7 correction --------------------------------------------------------------------
    def test_plain_triggers_and_script_creation_reset_the_loop_token(self):
        for name, at in (('TRIGANIM', 0xafbb4), ('WAITANIM', 0xafcf8), ('TRIGWAITANIM', 0xaff24),
                         ('TRIGANIMSPEED', 0xb00ec)):
            start, end = HANDLERS[name]
            self.assertEqual(self.stores_to(start, end, 168), [at], name)
            op, rs, _, _ = d_form(self.app.w(at))
            self.assertEqual(op, 36, name)
            self.assertEqual(self.constant_in(rs, at, start), 0xFFFF, name)
        op, rs, ra, d = d_form(self.app.w(0xb31a0))
        self.assertEqual((op, ra, d), (36, 31, 168))
        self.assertEqual(self.constant_in(rs, 0xb31a0, 0xb3140), 0xFFFF)

    def test_channel_handlers_never_touch_the_loop_token(self):
        for name in ('FLUSHANIM_CH', 'TRIGANIM_CH', 'WAITANIM_CH', 'LOOPANIM_CH', 'TRIGWAITANIM_CH', 'GETANIM_CH'):
            self.assertEqual(self.touches(*HANDLERS[name], 168), [], name)
        readers = [name for name, (start, end) in HANDLERS.items()
                   if any(d_form(w)[0] == 32 and d_form(w)[2] == 31 and d_form(w)[3] == 168
                          for _, w in self.app.range(start, end))]
        self.assertEqual(readers, ['LOOPANIM'])

    # ---- TRIGWAITANIM, TRIGANIMSPEED, LOOPANIM ---------------------------------------------
    def test_trigwaitanim_first_execution_rewinds_without_yielding(self):
        for name, trigger_path in (('TRIGWAITANIM', (0xafe44, 0xaff3c)), ('TRIGWAITANIM_CH', (0xb04cc, 0xb05b8))):
            self.assertEqual(self.stores_to(*trigger_path, 152), [], name)
            rewinds = [at for at, w in self.app.range(*trigger_path) if d_form(w)[0] == 14 and d_form(w)[3] == -4]
            self.assertEqual(len(rewinds), 1, name)
        # Mismatch path: PC - 4 and +152 = 0 (yield).
        self.assertEqual(self.stores_to(0xafe20, 0xafe38, 152), [0xafe30])
        self.assertEqual(self.stores_to(0xb04a8, 0xb04c0, 152), [0xb04b8])
        # Token = first operand + 1 in both.
        self.assertEqual(d_form(self.app.w(0xafefc)), (14, 0, 27, 1))
        self.assertEqual(d_form(self.app.w(0xb0574)), (14, 0, 27, 1))

    def test_triganimspeed_writes_out_and_divides_the_deadline(self):
        self.assertEqual(d_form(self.app.w(0xb007c)), (14, 0, 3, -300))
        self.assertEqual(d_form(self.app.w(0xb0088))[0:2], (11, 0))  # cmpwi: signed clamp
        self.assertEqual(d_form(self.app.w(0xb0088))[3], 300)
        op, rs, ra, rb, xo = x_form(self.app.w(0xb00c4))
        self.assertEqual((op, xo, rs), (31, 151, 4))  # stwx r4 = +72 (the clamped value) to the variable slot
        self.assertEqual(d_form(self.app.w(0xb00b8)), (32, 4, 31, 72))
        self.assertEqual(d_form(self.app.w(0xb00d0)), (32, 5, 31, 72))
        self.assertEqual(d_form(self.app.w(0xb00dc)), (7, 4, 5, 1000))  # mulli
        self.assertEqual(x_form(self.app.w(0xb00e0)), (31, 4, 4, 28, 491))  # divw (signed) by the operand
        self.assertEqual(self.stores_to(*HANDLERS['TRIGANIMSPEED'], 164), [0xb00e8])

    def test_loopanim_equal_token_exits_before_the_deadline_store(self):
        self.assertEqual(d_form(self.app.w(0xafd30)), (32, 4, 31, 168))
        self.assertEqual(x_form(self.app.w(0xafd3c))[4], 32)  # cmplw r4, r26
        self.assertEqual(self.branch_target(0xafd40), EXIT)
        self.assertEqual(self.stores_to(*HANDLERS['LOOPANIM'], 164), [0xafd88])
        self.assertEqual(self.stores_to(*HANDLERS['LOOPANIM_CH'], 164), [0xb03fc])

    # ---- AV-1 and AV-2 ---------------------------------------------------------------------
    def test_av1_waitanim_ch_adds_its_wait_as_a_u32(self):
        start, end = HANDLERS['WAITANIM_CH']
        floats = [at for at, w in self.app.range(start, end) if w >> 26 in (59, 63, 48, 50, 52, 54)]
        self.assertEqual(floats, [0xb030c], 'only `fmr f1, f31` (the trigger speed) is a float op')
        self.assertEqual(d_form(self.app.w(0xb0330)), (14, 0, 3, -300))
        self.assertEqual(d_form(self.app.w(0xb033c))[0], 10)  # cmplwi: unsigned clamp
        self.assertEqual(x_form(self.app.w(0xb035c)), (31, 3, 4, 3, 266))  # add r3 = wait + now
        self.assertEqual(self.stores_to(start, end, 160), [0xb02c8, 0xb0334, 0xb0348, 0xb0360])
        self.assertEqual(x_form(self.app.w(0xb02bc))[4], 32)  # resume: cmplw now, deadline (unsigned)
        # WAITANIM, by contrast, goes through the unsigned double bias and fctiwz.
        self.assertEqual(self.app.w(0xafca0), 0xFC00001E)

    def test_av2_no_object_path_returns_zero(self):
        for name, null_check, zero in (('TRIGANIM', 0xafaf0, 0xafb1c), ('WAITANIM', 0xafc48, 0xafc74),
                                       ('TRIGWAITANIM', 0xafe5c, 0xafe88), ('TRIGANIMSPEED', 0xb0030, 0xb0078),
                                       ('TRIGANIM_CH', 0xb01a8, 0xb01d4), ('WAITANIM_CH', 0xb0300, 0xb032c),
                                       ('TRIGWAITANIM_CH', 0xb04e4, 0xb0510)):
            self.assertEqual(self.branch_target(null_check), zero, name)
            self.assertEqual(d_form(self.app.w(zero)), (14, 3, 0, 0), name)  # li r3, 0
            self.assertEqual(d_form(self.app.w(zero + 4))[0], 14, name)  # falls into addi rX, r3, -300
            self.assertEqual(d_form(self.app.w(zero + 4))[3], -300, name)

    # ---- UV -------------------------------------------------------------------------------
    def test_copy_back_restores_what_the_old_clip_animated_and_the_new_one_does_not(self):
        self.assertEqual(d_form(self.app.w(0xa6948)), (32, 4, 3, 4))  # r4 = old clip (channel +4/+8)
        self.assertEqual(d_form(self.app.w(0xa69a0)), (14, 5, 30, 0))  # r5 = new clip
        self.assertEqual(self.app.w(0xa69a4) >> 26, 18)
        self.assertEqual(self.app.w(0xa589c), 0x7CBD2B78)  # mr r29, r5 (new)
        self.assertEqual(self.app.w(0xa58a4), 0x7C962378)  # mr r22, r4 (old)
        self.assertEqual(x_form(self.app.w(0xa5a00))[4], 151)  # mask[node] = old record flags
        self.assertEqual(x_form(self.app.w(0xa5aa8))[4], 60)  # andc: mask &= ~new record flags
        self.assertEqual(d_form(self.app.w(0xa5b8c)), (32, 26, 22, 44))  # then walk the old clip's records
        self.assertEqual(mask_tested(self.app.w(0xa5bfc), 29), 0x289)
        self.assertEqual(mask_tested(self.app.w(0xa5c64), 29), 0x1000)
        self.assertEqual(mask_tested(self.app.w(0xa5cd4), 29), 0x10000)
        self.assertEqual(d_form(self.app.w(0xa5ce8)), (32, 4, 30, 104))  # stored UVs (+104) of the stored mesh

    def test_uv_pass_adds_for_relative_models_and_sets_otherwise(self):
        self.assertEqual(self.app.w(0xa56d4), 0x5405077A)  # rlwinm r5, r0, 0, 29, 29: header flag 0x4
        self.assertEqual(self.branch_target(0xa840c), 0xa84dc)  # r5 == 0 -> set loop
        add_loop = [at for at, w in self.app.range(0xa8420, 0xa84d8) if w >> 26 == 59 and (w >> 1) & 31 == 21]
        set_loop = [at for at, w in self.app.range(0xa84ec, 0xa8594) if w >> 26 == 59 and (w >> 1) & 31 == 21]
        self.assertEqual(len(add_loop), 2, 'add mode: fadds onto the current U and V')
        self.assertEqual(set_loop, [], 'set mode stores the interpolated U and V')

    def test_uv_key_search(self):
        self.assertEqual(x_form(self.app.w(0xa834c)), (31, 0, 3, 0, 32))  # cmplw tick, ticks[i]
        self.assertEqual(self.branch_target(0xa8350), 0xa83b8)  # tick >= ticks[i]: next key
        self.assertEqual(d_form(self.app.w(0xa8354)), (14, 3, 4, -1))
        self.assertEqual(self.app.w(0xa83ac), 0xEC010024)  # fdivs: fraction in float time
        self.assertEqual(d_form(self.app.w(0xa83c4)), (14, 0, 28, -2))

    # ---- record bits ----------------------------------------------------------------------
    def test_no_record_table_reader_tests_an_unread_bit(self):
        readers = {}
        for name, code in (('app', self.app), ('engine', self.engine)):
            words = code.words
            for i, word in enumerate(words):
                op, _, ra, d = d_form(word)
                if not (op == 32 and d == 44):
                    continue
                if not any(d_form(words[j])[0] == 40 and d_form(words[j])[2] == ra and d_form(words[j])[3] == 18
                           for j in range(max(0, i - 16), min(len(words), i + 16))):
                    continue
                start = i
                while start > 0 and words[start] != 0x7C0802A6:
                    start -= 1
                end = i + 1
                while end < len(words) and words[end] != 0x7C0802A6:
                    end += 1
                readers[(name, start * 4)] = (code, start * 4, end * 4)
        self.assertEqual(sorted(readers), [('app', 0x53fa0), ('app', 0x582fc), ('app', 0xa3e24), ('app', 0xa56f8),
                                           ('app', 0xa5894), ('app', 0xa5d88), ('app', 0xa6398), ('app', 0xa7190),
                                           ('engine', 0x3f9d8)])
        hits = []
        for (name, _), (code, start, end) in sorted(readers.items()):
            for at, word in code.range(start, end):
                op, rd, _, d = d_form(word)
                loaded = (op == 32 and d == 4) or (op == 31 and x_form(word)[4] == 23)  # lwz +4, or lwzx (mask array)
                if not loaded:
                    continue
                for follow, x in code.range(at + 4, min(end, at + 4 + 4 * 24)):
                    mask = mask_tested(x, rd)
                    if mask in UNREAD_RECORD_BITS:
                        hits.append((name, at, follow, mask, d_form(word)[2]))
                    if overwrites(x, rd):
                        break
        # The one hit is the object word (r25 = object in the channel update, passed as r3 to the start 0xa67d8).
        self.assertEqual(hits, [('app', 0xa71c8, 0xa71cc, 0x8000, 25)])
        self.assertEqual(self.app.w(0xa7490), 0x38790000)  # addi r3, r25, 0
        self.assertEqual(self.app.w(0xa74a0) >> 26, 18)


if __name__ == '__main__':
    unittest.main()
