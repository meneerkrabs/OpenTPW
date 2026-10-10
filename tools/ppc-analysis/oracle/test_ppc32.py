"""Interpreter unit tests on synthetic instruction sequences; nothing here reads the original binary."""
from __future__ import annotations

import math
import random
import struct
import sys
import unittest
from fractions import Fraction
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import asm  # noqa: E402
from ppc32 import (BINARY32, MASK32, RETURN_SENTINEL, BudgetExceeded, Machine, Memory, MemoryFault,  # noqa: E402
                   Undefined, UnknownCall, Unsupported, bits_to_double, double_to_bits, double_to_single_store, fctiw,
                   round_rational, s32, single_bits_to_double)

CODE, DATA, STACK = 0x10000000, 0x20000000, 0x60000000
DATA_SIZE = 0x1000


def single(value: float) -> float:
    return struct.unpack('>f', struct.pack('>f', value))[0]


def machine(words: list[int], functions: dict[int, list[int]] | None = None, stubs=None, budget=10_000) -> Machine:
    """Code at CODE (read-only), extra functions at their own addresses, data at DATA, stack at STACK."""
    memory = Memory()
    memory.map('code', CODE, b''.join(struct.pack('>I', w) for w in words + [0]), writable=False)
    allowed = [(CODE, CODE + 4 * len(words))]
    for address, body in (functions or {}).items():
        memory.map(f'fn{address:x}', address, b''.join(struct.pack('>I', w) for w in body), writable=False)
        allowed.append((address, address + 4 * len(body)))
    memory.map('data', DATA, DATA_SIZE)
    memory.map('stack', STACK, 0x1000)
    m = Machine(memory, allowed, stubs or {}, budget)
    m.gpr[1] = STACK + 0xF00
    return m


def run(words: list[int], gpr: dict | None = None, fpr: dict | None = None, **kw) -> Machine:
    m = machine(words + [asm.blr()], **kw)
    for r, v in (gpr or {}).items():
        m.gpr[r] = v & MASK32
    for r, v in (fpr or {}).items():
        m.set_f(r, v)
    m.call(CODE)
    return m


class IntegerTests(unittest.TestCase):
    def setUp(self):
        self.rng = random.Random('ppc32-integer')

    def words(self):
        return [self.rng.getrandbits(32) for _ in range(200)] + [0, 1, 0x7FFFFFFF, 0x80000000, MASK32]

    def test_immediates_and_carry(self):
        m = run([asm.li(3, -5), asm.lis(4, 0x1234), asm.ori(4, 4, 0xABCD), asm.addic(5, 3, 5), asm.subfic(6, 3, 10)])
        self.assertEqual(m.gpr[3], (-5) & MASK32)
        self.assertEqual(m.gpr[4], 0x1234ABCD)
        self.assertEqual(m.gpr[5], 0)
        self.assertEqual(m.gpr[6], 15)
        self.assertEqual(m.ca, 0)  # subfic: ~0xfffffffb + 10 + 1 = 15, no carry (unsigned 10 < 0xfffffffb borrows)
        self.assertEqual(run([asm.li(3, 3), asm.subfic(6, 3, 10)]).ca, 1)
        self.assertEqual(run([asm.li(3, -1), asm.addic(5, 3, 1)]).ca, 1)

    def test_arithmetic_against_python(self):
        cases = {
            266: lambda a, b: a + b, 40: lambda a, b: b - a, 235: lambda a, b: s32(a) * s32(b),
            75: lambda a, b: (s32(a) * s32(b)) >> 32, 11: lambda a, b: (a * b) >> 32,
        }
        values = self.words()
        for xo_, reference in cases.items():
            for a_, b_ in zip(values, reversed(values)):
                m = run([asm.xo(3, 4, 5, xo_)], {4: a_, 5: b_})
                self.assertEqual(m.gpr[3], reference(a_, b_) & MASK32, f'xo {xo_} {a_:#x} {b_:#x}')

    def test_division_truncates_toward_zero_and_rejects_undefined(self):
        for a_, b_ in [(7, 2), (-7, 2), (7, -2), (-7, -2), (0x7FFFFFFF, 3), (-0x80000000, 7)]:
            m = run([asm.xo(3, 4, 5, 491)], {4: a_, 5: b_})
            q = abs(a_) // abs(b_) * (1 if (a_ < 0) == (b_ < 0) else -1)
            self.assertEqual(s32(m.gpr[3]), q)
        self.assertEqual(run([asm.xo(3, 4, 5, 459)], {4: MASK32, 5: 10}).gpr[3], MASK32 // 10)
        for words, regs in [([asm.xo(3, 4, 5, 491)], {4: 1, 5: 0}), ([asm.xo(3, 4, 5, 491)], {4: 0x80000000, 5: MASK32}),
                            ([asm.xo(3, 4, 5, 459)], {4: 1, 5: 0})]:
            with self.assertRaises(Unsupported):
                run(words, regs)

    def test_carry_chain_and_extended_ops(self):
        # 64-bit add of (r4:r5) + (r6:r7) with addc/adde.
        for _ in range(100):
            a_, b_ = self.rng.getrandbits(64), self.rng.getrandbits(64)
            m = run([asm.xo(9, 5, 7, 10), asm.xo(8, 4, 6, 138)], {4: a_ >> 32, 5: a_, 6: b_ >> 32, 7: b_})
            self.assertEqual((m.gpr[8] << 32) | m.gpr[9], (a_ + b_) & ((1 << 64) - 1))
        # The compiler's unsigned "x >= y" idiom: subfc then subfze.
        for x_, y_ in [(5, 3), (3, 5), (5, 5), (0, MASK32), (MASK32, 0)]:
            m = run([asm.xo(0, 4, 3, 8), asm.addi(0, 0, 0), asm.xo(3, 0, 0, 202)], {3: x_, 4: y_})
            self.assertEqual(m.gpr[3], 1 if x_ >= y_ else 0, f'{x_} {y_}')
        m = run([asm.xo(3, 4, 0, 104, oe=1)], {4: 0x80000000})
        self.assertEqual((m.gpr[3], m.so), (0x80000000, 1))

    def test_logical_shift_rotate(self):
        for v in self.words()[:60]:
            sh = v & 31
            m = run([asm.logical(28, 3, 4, 5), asm.logical(444, 6, 4, 5), asm.logical(316, 7, 4, 5),
                     asm.logical(124, 8, 4, 5), asm.logical(24, 9, 4, 10), asm.logical(536, 11, 4, 10),
                     asm.logical(792, 12, 4, 10), asm.srawi(13, 4, sh), asm.unary(26, 14, 4), asm.unary(954, 15, 4),
                     asm.unary(922, 16, 4), asm.rlwinm(17, 4, sh, 16, 27), asm.rlwnm(18, 4, 10, 0, 31)],
                    {4: v, 5: ~v * 3, 10: sh})
            b_ = (~v * 3) & MASK32
            self.assertEqual(m.gpr[3], v & b_)
            self.assertEqual(m.gpr[6], v | b_)
            self.assertEqual(m.gpr[7], v ^ b_)
            self.assertEqual(m.gpr[8], ~(v | b_) & MASK32)
            self.assertEqual(m.gpr[9], (v << sh) & MASK32)
            self.assertEqual(m.gpr[11], v >> sh)
            self.assertEqual(m.gpr[12], (s32(v) >> sh) & MASK32)
            self.assertEqual(m.gpr[13], (s32(v) >> sh) & MASK32)
            self.assertEqual(m.gpr[14], 32 - v.bit_length())
            self.assertEqual(s32(m.gpr[15]), struct.unpack('b', bytes([v & 0xFF]))[0])
            self.assertEqual(s32(m.gpr[16]), struct.unpack('>h', struct.pack('>H', v & 0xFFFF))[0])
            rotated = ((v << sh) | (v >> (32 - sh))) & MASK32 if sh else v
            self.assertEqual(m.gpr[17], rotated & 0x0000FFF0)
            self.assertEqual(m.gpr[18], rotated)

    def test_shift_amounts_of_32_and_more_and_srawi_carry(self):
        m = run([asm.logical(24, 3, 4, 5), asm.logical(536, 6, 4, 5), asm.logical(792, 7, 4, 5)], {4: 0x80000001, 5: 40})
        self.assertEqual((m.gpr[3], m.gpr[6], m.gpr[7], m.ca), (0, 0, MASK32, 1))
        self.assertEqual(run([asm.srawi(3, 4, 1)], {4: (-3) & MASK32}).ca, 1)
        self.assertEqual(run([asm.srawi(3, 4, 1)], {4: (-4) & MASK32}).ca, 0)

    def test_rlwimi_and_wrapping_mask(self):
        m = run([asm.rlwimi(3, 4, 8, 16, 23)], {3: 0x11223344, 4: 0x000000AB})
        self.assertEqual(m.gpr[3], 0x1122AB44)
        m = run([asm.rlwinm(3, 4, 0, 28, 3)], {4: MASK32})  # mb > me wraps
        self.assertEqual(m.gpr[3], 0xF000000F)

    def test_record_forms_set_cr0(self):
        for v, nibble in [(0, 2), (5, 4), ((-5) & MASK32, 8)]:
            m = run([asm.andi_(3, 4, 0xFFFF), asm.rlwinm(5, 4, 0, 0, 31, rc=1)], {4: v})
            self.assertEqual(m.cr >> 28, nibble)

    def test_compares(self):
        m = run([asm.cmpwi(0, 3, -1), asm.cmplwi(1, 3, 1), asm.cmpw(2, 3, 4), asm.cmplw(3, 3, 4), asm.mfcr(5)],
                {3: MASK32, 4: 1})
        self.assertEqual(m.gpr[5] >> 16, 0x2484)  # cr0 eq; cr1 gt (unsigned); cr2 lt (signed); cr3 gt (unsigned)


class MemoryAndControlTests(unittest.TestCase):
    def test_loads_stores_sign_and_update(self):
        m = run([asm.lis(3, DATA >> 16), asm.li(4, -2), asm.sth(4, 6, 3), asm.lha(5, 6, 3), asm.lhz(6, 6, 3),
                 asm.stb(4, 9, 3), asm.lbz(7, 9, 3), asm.stwu(4, 16, 3), asm.lwz(8, 0, 3), asm.lwbrx(9, 0, 3)])
        self.assertEqual(s32(m.gpr[5]), -2)
        self.assertEqual(m.gpr[6], 0xFFFE)
        self.assertEqual(m.gpr[7], 0xFE)
        self.assertEqual(m.gpr[3], DATA + 16)
        self.assertEqual(m.gpr[8], (-2) & MASK32)
        self.assertEqual(m.gpr[9], 0xFEFFFFFF)

    def test_lmw_stmw_round_trip(self):
        m = run([asm.lis(3, DATA >> 16), asm.li(29, 7), asm.li(30, 8), asm.li(31, 9), asm.stmw(29, 0, 3),
                 asm.li(29, 0), asm.li(30, 0), asm.li(31, 0), asm.lmw(29, 0, 3)])
        self.assertEqual(m.gpr[29:32], [7, 8, 9])

    def test_unmapped_and_read_only_accesses_fault(self):
        with self.assertRaises(MemoryFault):
            run([asm.lwz(3, 0, 0)])  # null page
        with self.assertRaises(MemoryFault):
            run([asm.lis(3, CODE >> 16), asm.stw(3, 0, 3)])  # code is read-only
        with self.assertRaises(MemoryFault):
            run([asm.lis(3, DATA >> 16), asm.lwz(4, DATA_SIZE - 2, 3)])  # straddles the region end

    def test_reset_restores_written_pages(self):
        m = run([asm.lis(3, DATA >> 16), asm.li(4, 77), asm.stw(4, 0x800, 3)])
        self.assertEqual(m.memory.u32(DATA + 0x800), 77)
        m.memory.reset()
        self.assertEqual(m.memory.u32(DATA + 0x800), 0)

    def test_counted_loop_with_bdnz(self):
        m = run([asm.li(3, 0), asm.li(4, 10), asm.mtctr(4), asm.addi(3, 3, 3), asm.bc(16, 0, -4)])
        self.assertEqual((m.gpr[3], m.ctr), (30, 0))

    def test_conditional_return_and_cr_logic(self):
        body = [asm.cmpwi(0, 3, 0), asm.crop(193, 0, 0, 2), asm.li(4, 1), asm.bclr(12, 0), asm.li(4, 2)]
        self.assertEqual(run(body, {3: 5}).gpr[4], 2)        # lt xor eq = 0
        self.assertEqual(run(body, {3: 0}).gpr[4], 1)        # eq -> lt xor eq = 1 -> return early
        self.assertEqual(run(body, {3: MASK32}).gpr[4], 1)   # lt

    def test_call_into_allowed_function_and_return(self):
        callee = 0x10100000
        caller = [asm.mflr(0), asm.stw(0, 8, 1), asm.stwu(1, -64, 1), asm.li(3, 20),
                  asm.b(callee - (CODE + 16), link=1), asm.addi(1, 1, 64), asm.lwz(0, 8, 1), asm.mtlr(0)]
        m = run(caller, functions={callee: [asm.addi(3, 3, 22), asm.blr()]})
        self.assertEqual(m.gpr[3], 42)
        self.assertEqual(m.pc, RETURN_SENTINEL)

    def test_call_outside_allowed_code_aborts(self):
        with self.assertRaises(UnknownCall):
            run([asm.b(0x1000, link=1)])
        with self.assertRaises(UnknownCall):
            run([asm.lis(3, 0x3000), asm.mtctr(3), asm.bctrl()])
        with self.assertRaises(UnknownCall):  # a tail branch out of the routine is a call too
            run([asm.b(0x40)])

    def test_stub_replaces_call_and_returns(self):
        seen = []

        def stub(m: Machine) -> None:
            seen.append(m.gpr[3])
            m.gpr[3] = 99
        target = 0x30000000
        m = run([asm.mflr(31), asm.li(3, 5), asm.lis(12, target >> 16), asm.mtctr(12), asm.bctrl(), asm.mtlr(31)],
                stubs={target: stub})
        self.assertEqual((seen, m.gpr[3]), ([5], 99))

    def test_budget_and_unsupported(self):
        with self.assertRaises(BudgetExceeded):
            run([asm.b(0)], budget=50)
        for word in (asm.sc(), asm.fx(22, 1, 2), asm.mfspr(3, 268), asm.x(31, 0, 3, 4, 86), asm.fp(59, 24, 1, 0, 2)):
            with self.assertRaises(Unsupported) as caught:
                run([word])
            self.assertNotIsInstance(caught.exception, Undefined, f'{word:#010x} is unmodelled, not undefined')
        for a, b in ((5, 0), (0x80000000, MASK32)):
            with self.assertRaises(Undefined):
                run([asm.xo(3, 4, 5, 491)], {4: a, 5: b})
        with self.assertRaises(Undefined):
            run([asm.xo(3, 4, 5, 459)], {4: 5, 5: 0})

    def test_fragment_stop(self):
        """A stop reached by falling through may be the end of the allowed range; a branch to it must be allowed."""
        m = machine([asm.li(3, 7), asm.li(3, 8)])
        m.call(CODE, stop=frozenset({CODE + 4}))
        self.assertEqual((m.gpr[3], m.pc), (7, CODE + 4))
        m = machine([asm.li(3, 7)])
        m.call(CODE, stop=frozenset({CODE + 4}))
        self.assertEqual(m.pc, CODE + 4)
        m = machine([asm.b(8), asm.li(3, 1), asm.li(3, 2)])
        m.call(CODE, stop=frozenset({CODE + 8}))
        self.assertEqual((m.gpr[3], m.pc), (0, CODE + 8))
        m = machine([asm.b(4)])
        with self.assertRaisesRegex(UnknownCall, 'fragment stop'):
            m.call(CODE, stop=frozenset({CODE + 4}))


def fpu(words: list[int], **fregs) -> Machine:
    return run(words, fpr={int(k[1:]): v for k, v in fregs.items()})


class FloatingPointTests(unittest.TestCase):
    def setUp(self):
        self.rng = random.Random('ppc32-float')

    def singles(self, n=300):
        return [single(self.rng.uniform(-1e6, 1e6) * 2.0 ** self.rng.randint(-60, 60)) for _ in range(n)]

    def test_single_ops_match_float32_of_exact_result(self):
        values = self.singles()
        for a_, b_ in zip(values, reversed(values)):
            m = fpu([asm.fp(59, 21, 3, 1, 2), asm.fp(59, 20, 4, 1, 2), asm.fp(59, 25, 5, 1, 0, 2),
                     asm.fp(59, 18, 6, 1, 2)], f1=a_, f2=b_)
            for register, exact in ((3, Fraction(a_) + Fraction(b_)), (4, Fraction(a_) - Fraction(b_)),
                                    (5, Fraction(a_) * Fraction(b_)), (6, Fraction(a_) / Fraction(b_))):
                expected = round_rational(exact, BINARY32) if exact else 0.0
                self.assertEqual(m.f(register), expected, (register, a_, b_))
                self.assertEqual(m.f(register), single(m.f(register)))

    def test_single_add_of_doubles_avoids_double_rounding(self):
        # Exact 1 + 2^-24 + 2^-60 lies above the single midpoint; rounding via double would tie to 1.0.
        m = fpu([asm.fp(59, 21, 3, 1, 2)], f1=1.0 + 2.0 ** -24, f2=2.0 ** -60)
        self.assertEqual(m.f(3), 1.0 + 2.0 ** -23)
        self.assertEqual(single((1.0 + 2.0 ** -24) + 2.0 ** -60), 1.0)

    def test_fused_multiply_add_rounds_once(self):
        a_, c_ = single(1.0 + 2.0 ** -12), single(1.0 - 2.0 ** -12)  # a·c = 1 − 2^-24 exactly
        b_ = -1.0
        m = fpu([asm.fp(59, 29, 3, 1, 2, 4), asm.fp(63, 29, 5, 1, 2, 4), asm.fp(59, 31, 6, 1, 2, 4)], f1=a_, f2=b_, f4=c_)
        self.assertEqual(m.f(3), -(2.0 ** -24))  # unfused would give single(a·c) − 1 = 0
        self.assertEqual(m.f(5), -(2.0 ** -24))
        self.assertEqual(m.f(6), 2.0 ** -24)     # fnmadds

    def test_fused_special_values(self):
        cases = [((2.0, 3.0, math.inf), math.inf), ((math.inf, 0.0, 1.0), math.nan), ((-0.0, 1.0, -0.0), -0.0),
                 ((0.0, -1.0, -0.0), -0.0), ((2.0, 3.0, -6.0), 0.0), ((1e300, 1e300, -math.inf), -math.inf)]
        for (a_, c_, b_), expected in cases:
            r = fpu([asm.fp(63, 29, 3, 1, 2, 4)], f1=a_, f2=b_, f4=c_).f(3)
            if math.isnan(expected):
                self.assertTrue(math.isnan(r))
            else:
                self.assertEqual((r, math.copysign(1.0, r)), (expected, math.copysign(1.0, expected)), (a_, c_, b_))

    def test_special_values(self):
        m = fpu([asm.fp(59, 18, 3, 1, 2), asm.fp(59, 18, 4, 2, 2), asm.fp(59, 21, 5, 6, 7), asm.fp(59, 20, 8, 6, 6)],
                f1=1.0, f2=0.0, f6=math.inf, f7=-math.inf)
        self.assertEqual(m.f(3), math.inf)
        self.assertTrue(math.isnan(m.f(4)))
        self.assertTrue(math.isnan(m.f(5)))
        self.assertTrue(math.isnan(m.f(8)))
        overflow = fpu([asm.fp(59, 25, 3, 1, 0, 1)], f1=3e38)
        self.assertEqual(overflow.f(3), math.inf)

    def test_frsp_ties_to_even_and_rounding_modes(self):
        tie = 1.0 + 2.0 ** -24 + 2.0 ** -23  # midpoint between 1 + 2^-23 and 1 + 2^-22: even is 1 + 2^-22
        self.assertEqual(fpu([asm.fx(12, 3, 1)], f1=tie).f(3), 1.0 + 2.0 ** -22)
        self.assertEqual(fpu([asm.fx(12, 3, 1)], f1=1.0 + 2.0 ** -24).f(3), 1.0)
        toward_zero = fpu([asm.mtfsfi(7, 1), asm.fx(12, 3, 1)], f1=1.0 + 2.0 ** -23 + 2.0 ** -30)
        self.assertEqual(toward_zero.f(3), 1.0 + 2.0 ** -23)
        up = fpu([asm.mtfsfi(7, 2), asm.fx(12, 3, 1)], f1=1.0 + 2.0 ** -30)
        self.assertEqual(up.f(3), 1.0 + 2.0 ** -23)

    def test_fctiwz_saturates_and_nan(self):
        cases = [(2.9, 2), (-2.9, -2), (2147483647.9, 0x7FFFFFFF), (2147483648.0, 0x7FFFFFFF), (-2147483649.0, -0x80000000),
                 (math.nan, -0x80000000), (math.inf, 0x7FFFFFFF), (-math.inf, -0x80000000)]
        for value, expected in cases:
            m = fpu([asm.fx(15, 3, 1), asm.lis(4, DATA >> 16), asm.stfd(3, 0, 4), asm.lwz(5, 4, 4)], f1=value)
            self.assertEqual(s32(m.gpr[5]), expected, value)
        self.assertEqual(fctiw(2.5, 0), 2)
        self.assertEqual(fctiw(3.5, 0), 4)

    def test_int_to_float_magic_conversion(self):
        # The compiler's (double)(int)x: words 0x43300000 and x ^ 0x80000000, minus 2^52 + 2^31.
        for value in [0, 1, -1, 123456789, -0x80000000, 0x7FFFFFFF]:
            words = [asm.lis(3, DATA >> 16), asm.lis(4, 0x4330), asm.stw(4, 0, 3), asm.xori(5, 6, 0),
                     asm.d(27, 6, 5, 0x8000), asm.stw(5, 4, 3), asm.stw(4, 8, 3), asm.lis(7, 0x8000), asm.stw(7, 12, 3),
                     asm.lfd(1, 0, 3), asm.lfd(2, 8, 3), asm.fp(63, 20, 3, 1, 2)]
            m = run(words, {6: value})
            self.assertEqual(m.f(3), float(value))

    def test_load_store_single_conversion(self):
        m = fpu([asm.lis(3, DATA >> 16), asm.stfs(1, 0, 3), asm.lfs(2, 0, 3)], f1=1.0 + 2.0 ** -40)
        self.assertEqual(m.f(2), 1.0)  # stfs truncates bits (no rounding), lfs widens exactly
        self.assertEqual(double_to_single_store(double_to_bits(1.0 + 2.0 ** -23 - 2.0 ** -40)), 0x3F800000)
        self.assertEqual(double_to_single_store(double_to_bits(2.0 ** -130)), 0x00080000)  # single denormal
        self.assertEqual(single_bits_to_double(0x00080000), 2.0 ** -130)

    def test_compare_and_select(self):
        m = fpu([asm.fcmpo(0, 1, 2), asm.fcmpu(1, 1, 3), asm.mfcr(4), asm.fp(63, 23, 5, 1, 6, 7)],
                f1=-1.0, f2=2.0, f3=math.nan, f6=10.0, f7=20.0)
        self.assertEqual(m.gpr[4] >> 24, 0x81)  # cr0 lt, cr1 unordered
        self.assertEqual(m.f(5), 10.0)            # fsel picks frb when fra < 0
        m = fpu([asm.fx(40, 2, 1), asm.fx(264, 3, 2), asm.fx(136, 4, 3), asm.fx(72, 5, 4)], f1=1.5)
        self.assertEqual((m.f(2), m.f(3), m.f(4), m.f(5)), (-1.5, 1.5, -1.5, -1.5))

    def test_record_form_floating_ops_are_rejected(self):
        with self.assertRaises(Unsupported):
            fpu([asm.a(59, 3, 1, 2, 0, 21, rc=1)], f1=1.0, f2=1.0)

    def test_bits_helpers(self):
        self.assertEqual(bits_to_double(double_to_bits(-0.0)), -0.0)
        self.assertEqual(math.copysign(1.0, bits_to_double(double_to_bits(-0.0))), -1.0)


if __name__ == '__main__':
    unittest.main()
