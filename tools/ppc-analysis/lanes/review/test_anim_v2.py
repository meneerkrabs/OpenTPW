"""Review ANIM-V2: ANIM-F commit 65433c2 (fixes for the ANIM-V findings AV-1..AV-6) on its parent e92fb23.

- With OPENTPW_PPC_BIN_ROOT naming the identified Feral Mac bin directory, this file's own field split
  (only pef.py is shared) settles the three doubts ANIM-F raised:
  1. Which clock: WAITANIM, WAITANIM_CH, their resume checks and GETTIME pass r30 to 0x10e844, and r30 is
     the global loaded from TOC slot 0xa28 in the interpreter prologue (0xaf55c), not a script field. GETTIME
     stores the result as it is (0xaf6e0): there is no creation timestamp. So the original's 32-bit clock is
     the shared scheduler clock and does not start at script creation.
  2. The folded re-execute step: the trigger execution of WAITANIM(_CH) rewinds three words and clears the
     yield word +152; the next execution resumes on `cmplw now, deadline` and continues in the same slice. A
     deadline word of 0 means "not waiting" and triggers again.
  3. The compare word of TRIGWAITANIM without an object: the third operand's raw code word stored at
     576(r1) (0xafdcc), which the null branch (0xafdec) leaves in place for the compare (0xafe0c-0xafe18);
     variables carry the 0xC0 tag (0xae6ec), literals are sign-extended low halves.
- Always (no binaries): the reference arithmetic of the u32 deadline, and source checks for the findings of
  this review (expected failures until ANIM-F2 fixed them):
  AW-1 the scheduler clock is ported and its remaining approximation (start value) is registered;
  AW-2 VisitorRideScriptEffects forwards IRideAnimationQuery, so a visitor-wrapped object host resumes
  TRIGWAITANIM; AW-3 the closed-Totem idle assertion is back on the hosted object.

Nothing is executed from the original. Nothing here says anything about the PC build.
"""
from __future__ import annotations

import hashlib
import os
import re
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
BIN_ROOT = os.environ.get('OPENTPW_PPC_BIN_ROOT')
REPO = Path(__file__).resolve().parents[4]
MASK32 = 0xFFFFFFFF
CLOCK = 0x10e844  # scheduler milliseconds (docs/reverse/PPC-clock.md)
OPERAND = 0xaf3bc  # fetch the next raw code word
EXIT = 0xb2354  # common handler exit


def s16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


def d_form(word: int) -> tuple[int, int, int, int]:
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, s16(word & 0xFFFF)


def x_form(word: int) -> tuple[int, int, int, int, int]:
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, (word >> 11) & 31, (word >> 1) & 0x3FF


def writes_register(word: int, register: int) -> bool:
    """Integer instructions whose destination is `register` (D-form rD, logical/rotate rA, X-form rD or rA)."""
    op = word >> 26
    if op in (7, 12, 13, 14, 15, 32, 33, 34, 35, 40, 41, 42, 43, 46):  # mulli addic addic. addi addis loads lmw
        return (word >> 21) & 31 == register or (op == 46 and (word >> 21) & 31 <= register)
    if op in (20, 21, 23, 24, 25, 26, 27, 28, 29):  # rlwimi rlwinm rlwnm ori oris xori xoris andi. andis.
        return (word >> 16) & 31 == register
    if op == 31:
        _, rd, ra, _, xo = x_form(word)
        if xo in (151, 183, 215, 247, 407, 439, 150, 662, 918, 0, 32, 86, 470, 54, 278, 1014):  # stores, cmps, caches
            return False
        logical = xo in (28, 60, 124, 284, 316, 412, 444, 476, 24, 536, 792, 824, 26, 922, 954)
        return (ra if logical else rd) == register
    return False


def call_target(at: int, word: int) -> int | None:
    if word >> 26 == 18 and word & 1:
        offset = word & 0x03FFFFFC
        if offset & 0x02000000:
            offset -= 0x04000000
        return at + offset
    return None


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


# ---- reference arithmetic -----------------------------------------------------------------------------
def waitanim_ch_wait(returned: int) -> int:
    wait = (returned - 300) & MASK32
    return wait if wait >= 300 else 300


def resumes_next_execution(now: int, returned: int, later: int) -> bool | None:
    """Original WAITANIM_CH: None = deadline word 0 (triggers again); else whether `later` passes `cmplw`."""
    deadline = (now + waitanim_ch_wait(returned)) & MASK32
    if deadline == 0:
        return None
    return not (later & MASK32) < deadline


class ReferenceArithmetic(unittest.TestCase):
    def test_short_clip_resumes_at_the_next_execution_once_the_clock_passed_the_shortfall(self):
        # 166 ms clip: 134 ms short of 300. A clock at or beyond 134 ms wraps the deadline into the past.
        self.assertTrue(resumes_next_execution(10_000, 166, 10_016))
        self.assertTrue(resumes_next_execution(135, 166, 151))
        self.assertIsNone(resumes_next_execution(134, 166, 150), 'exactly at the shortfall the sum is 0: trigger again')
        self.assertFalse(resumes_next_execution(133, 166, 150), 'below the shortfall the add does not wrap')
        # 50 ms clip at clock 250: 250 + (2^32 - 250) = 0, the "not waiting" word, so the next execution triggers again.
        self.assertIsNone(resumes_next_execution(250, 50, 266))

    def test_the_clock_value_decides_the_outcome(self):
        # The same script instruction at the same script age: a clock that starts at script creation (OpenTPW)
        # waits ~49.7 days, the scheduler clock of a park that has run for a minute continues (AW-1).
        script_age = 16
        self.assertFalse(resumes_next_execution(script_age, 166, script_age + 16))
        self.assertTrue(resumes_next_execution(60_000 + script_age, 166, 60_000 + script_age + 16))

    def test_anim_model_matches(self):
        sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'anim'))
        import anim_model as m  # noqa: E402
        for now in (0, 16, 133, 134, 10_000, MASK32 - 5):
            for returned in (0, 50, 166, 299, 300, 600, 1000, 3750):
                self.assertEqual(m.waitanim_ch_deadline(now, returned), (now + waitanim_ch_wait(returned)) & MASK32)


@unittest.skipUnless(BIN_ROOT, 'set OPENTPW_PPC_BIN_ROOT to the identified Feral Mac bin directory')
class Binary(unittest.TestCase):
    app: Code

    @classmethod
    def setUpClass(cls):
        cls.app = Code(Path(BIN_ROOT) / 'SimThemePark.data', APP_SHA256)

    def clock_call(self, at: int) -> None:
        """`mr r3, r30` (or `addi r3, r30, 0`) directly before `bl 0x10e844`."""
        self.assertEqual(call_target(at, self.app.w(at)), CLOCK, f'{at:#x}')
        word = self.app.w(at - 4)
        is_mr = word >> 26 == 31 and x_form(word)[4] == 444 and x_form(word)[2] == 3 and x_form(word)[1] == 30
        is_addi = d_form(word) == (14, 3, 30, 0)
        self.assertTrue(is_mr or is_addi, f'{at - 4:#x} does not pass r30')

    # ---- doubt 1: the clock -----------------------------------------------------------------------------
    def test_r30_is_a_global_loaded_in_the_interpreter_prologue(self):
        self.assertEqual(d_form(self.app.w(0xaf55c)), (32, 30, 2, -30168))  # lwz r30, toc[0xa28]
        self.assertEqual(d_form(self.app.w(0xaf550)), (14, 31, 3, 0))  # r31 = the script
        # No handler before 0xb1650 writes r30 (the animation handlers 0xafa68-0xb07e4 and GETTIME 0xaf6c8).
        writes = [at for at, w in self.app.range(0xaf560, 0xb1650) if writes_register(w, 30)]
        self.assertEqual(writes, [])

    def test_waits_and_gettime_read_the_scheduler_clock(self):
        for at in (0xafbfc, 0xafcc8, 0xb02b4, 0xb0350, 0xaf6d8, 0xafedc):
            self.clock_call(at)

    def test_gettime_has_no_creation_offset(self):
        # bl 0x10e844, then the result goes straight to the accumulator +72.
        self.assertEqual(d_form(self.app.w(0xaf6e0)), (36, 3, 31, 72))
        between = [self.app.w(at) for at in range(0xaf6dc, 0xaf6e0, 4)]
        self.assertTrue(all(not writes_register(w, 3) for w in between))

    # ---- doubt 2: the re-execute step --------------------------------------------------------------------
    def test_trigger_execution_rewinds_three_words_and_yields(self):
        for name, rewind, yield_store in (('WAITANIM', 0xafce8, 0xafcf0), ('WAITANIM_CH', 0xb0368, 0xb0370)):
            self.assertEqual(d_form(self.app.w(rewind)), (14, 3, 3, -3), name)
            op, rs, ra, d = d_form(self.app.w(yield_store))
            self.assertEqual((op, ra, d), (36, 31, 152), name)

    def test_later_execution_resumes_in_the_same_slice(self):
        for name, test_zero, compare, branch, clear, to_exit in (
                ('WAITANIM', 0xafbf0, 0xafc04, 0xafc08, 0xafc10, 0xafc14),
                ('WAITANIM_CH', 0xb02a8, 0xb02bc, 0xb02c0, 0xb02c8, 0xb02cc)):
            self.assertEqual(d_form(self.app.w(test_zero))[0], 10, name)  # cmplwi +160, 0: 0 = not waiting
            self.assertEqual(x_form(self.app.w(compare))[4], 32, name)  # cmplw now, deadline
            self.assertEqual(self.app.w(branch) >> 21 & 31, 12, name)  # blt: still waiting -> rewind, yield
            self.assertEqual(d_form(self.app.w(clear))[2:], (31, 160), name)  # passed: +160 = 0 ...
            word = self.app.w(to_exit)
            self.assertEqual(to_exit + (word & 0x03FFFFFC), EXIT, name)  # ... and exit without a yield

    # ---- doubt 3: TRIGWAITANIM without an object ---------------------------------------------------------
    def test_trigwaitanim_null_object_compares_the_out_operand_code_word(self):
        self.assertEqual(call_target(0xafdc8, self.app.w(0xafdc8)), OPERAND)  # third operand: raw word
        self.assertEqual(d_form(self.app.w(0xafdcc)), (36, 3, 1, 576))  # stw r3, 576(r1)
        self.assertEqual(d_form(self.app.w(0xafde8))[0], 10)  # cmplwi object, 0
        self.assertEqual(0xafdec + s16(self.app.w(0xafdec) & 0xFFFC), 0xafe0c)  # null: skip the query
        self.assertEqual(call_target(0xafe08, self.app.w(0xafe08)), 0xa7dcc)  # query writes 576(r1) (r4)
        self.assertEqual(d_form(self.app.w(0xafdfc)), (14, 4, 1, 576))
        self.assertEqual(d_form(self.app.w(0xafe0c)), (32, 3, 1, 576))
        self.assertEqual(d_form(self.app.w(0xafe14)), (14, 3, 3, 1))
        self.assertEqual(d_form(self.app.w(0xafe10)), (32, 0, 31, 188))
        self.assertEqual(x_form(self.app.w(0xafe18))[4], 0)  # cmpw (signed, equality used)

    def test_operand_tags(self):
        # 0xae6ec: top byte 0xC0 -> variable slot (xoris 0x4000), otherwise extsh of the word.
        self.assertEqual(self.app.w(0xae6ec), 0x5485000E)  # rlwinm r5, r4, 0, 0, 7
        self.assertEqual(d_form(self.app.w(0xae6f0)), (15, 0, 5, -16384))  # addis r0, r5, -0x4000
        self.assertEqual(self.app.w(0xae710), 0x7C830734)  # extsh r3, r4


class Source(unittest.TestCase):
    @staticmethod
    def read(relative: str) -> str:
        return (REPO / relative).read_text(encoding='utf-8')

    def test_anim_f_fix_is_in_place(self):
        animation = self.read('source/OpenTPW/VM/Handlers/Animation.cs')
        self.assertIn('NoObjectMilliseconds = 0', animation)
        self.assertIn('WaitUntilDeadline( vm, wait )', animation)
        self.assertIn('[BIN:TPW-EXE:0x5535c7 TRIGWAITANIM compare]', animation)  # RIDES-037 retired
        self.assertIn('[APPROX:RIDES-038]', animation)

    def test_aw1_script_clock_versus_scheduler_clock_is_registered(self):
        # The original's waits and GETTIME read the shared scheduler clock (0x10e844 on the global r30); OpenTPW's
        # clock starts at VM creation. ANIM-F's u32 deadline makes the clock value observable (a WAITANIM_CH on a
        # clip under 300 ms in the first 300 - ret ms of a script). Expect an APPROX tag naming it.
        sources = self.read('source/OpenTPW/VM/RideVM.cs') + self.read('source/OpenTPW/VM/Handlers/Scheduling.cs') \
            + self.read('source/OpenTPW/VM/Handlers/Animation.cs')
        tags = re.findall(r'\[APPROX:[A-Z]+-\d+\][^\n]*', sources)
        self.assertTrue(any('scheduler' in tag or '0x10e844' in tag.lower() or '0x1010e844' in tag.lower()
                            for tag in tags), tags)

    def test_aw2_visitor_wrapper_forwards_the_animation_query(self):
        bridge = self.read('source/OpenTPW/World/Guests/RideVisitorBridge.cs')
        declaration = re.search(r'public sealed class VisitorRideScriptEffects\([^)]*\)\s*:\s*([^\n{]+)', bridge)
        self.assertIsNotNone(declaration)
        self.assertIn('IRideAnimationQuery', declaration.group(1))

    def test_aw3_closed_totem_idle_is_still_pinned(self):
        # TotemScriptTraceIsPinned also ran a closed Totem (VAR_RIDECLOSED = 1: idles in its ENDSLICE loop after
        # WAITANIM, VAR_RUNNING stays 0); ANIM-F removed it without a hosted replacement.
        corpus = self.read('source/OpenTPW.Tests/RideVMCorpusTests.cs')
        self.assertRegex(corpus, r'VAR_RIDECLOSED\s*\]\s*=\s*1|nameof\(\s*RideVariables\.VAR_RIDECLOSED\s*\),\s*1')


if __name__ == '__main__':
    unittest.main()
