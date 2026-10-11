"""ORACLE-V round 1: independent review of the PowerPC differential oracle (16bb38e on c6c19ea).

- Interpreter (no binary): the risky instructions of tools/ppc-analysis/oracle/ppc32.py against reference
  formulas written here from the PowerPC architecture, not from ppc32.py: carry/overflow/record bits,
  rotate masks incl. wrap masks, divw undefined cases, srawi/sraw carry, cntlzw 0, bc BO/BI incl. CTR forms,
  fmadds single rounding (brute-force nearest single), frsp, fctiw[z] rounding/saturation/NaN, fcmpu/fcmpo,
  lmw/stmw. Two known deviations (exact-zero sign under round-toward-minus-infinity, NaN operand order of
  fmadd) are expected failures (OV-6).
- Sandbox (no binary): every escape path stops the run.
- Runner and bridge contracts (no binary): OV-2, OV-3 and OV-4 are expected failures until fixed.
- Writers and findings (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT = the Feral bin directory, SHA-256 checked by
  the oracle's Image): the writer of ride +46 bit 0x8 (F1) and of data:0x84b2c (F3) run as isolated fragments
  with every call stubbed; F5, F7 and F8 re-checked through the oracle targets and a static call-site scan.

Only inputs, decoded fields and results are pinned; no original bytes or listings. See
docs/reverse/REVIEW-ORACLE.md.
"""
from __future__ import annotations

import math
import random
import re
import struct
import sys
import unittest
from fractions import Fraction
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[2]
REPO = TOOLS.parents[1]
ORACLE = TOOLS / 'oracle'
sys.path.insert(0, str(ORACLE))
sys.path.insert(0, str(TOOLS / 'lanes' / 'economy'))

import ppc32  # noqa: E402
from ppc32 import MASK32, Machine, Memory, MemoryFault, UnknownCall, Unsupported, BudgetExceeded  # noqa: E402

CODE, DATA, STACK = 0x10000000, 0x20000000, 0x60000000
BLR = 0x4E800020


# ------------------------------------------------------------------ independent encoders and references
def xo31(rt, ra, rb, xo, rc=0, oe=0):
    return (31 << 26) | (rt << 21) | (ra << 16) | (rb << 11) | (oe << 10) | (xo << 1) | rc


def dform(op, rt, ra, imm):
    return (op << 26) | (rt << 21) | (ra << 16) | (imm & 0xFFFF)


def aform(op, t, a, b, c, xo):
    return (op << 26) | (t << 21) | (a << 16) | (b << 11) | (c << 6) | (xo << 1)


def xform63(t, a, b, xo):
    return (63 << 26) | (t << 21) | (a << 16) | (b << 11) | (xo << 1)


def signed(v):
    v &= MASK32
    return v - (1 << 32) if v >> 31 else v


def d2b(x):
    return struct.unpack('>Q', struct.pack('>d', x))[0]


def b2d(b):
    return struct.unpack('>d', struct.pack('>Q', b))[0]


def f32bits(x):
    return struct.unpack('>I', struct.pack('>f', x))[0]


def from32(b):
    return struct.unpack('>f', struct.pack('>I', b))[0]


def nearest_single(q: Fraction) -> float:
    """Round-to-nearest-even of a nonzero rational to binary32 by comparing neighbouring patterns."""
    negative, mag = q < 0, abs(q)
    approx = min(float(mag), 3.4028234663852886e38)
    centre = f32bits(approx)
    cands = [from32(b) for b in range(max(0, centre - 3), min(0x7F7FFFFF, centre + 3) + 1)]
    lo = max((c for c in cands if Fraction(c) <= mag), default=0.0)
    hi = min((c for c in cands if Fraction(c) >= mag), default=math.inf)
    if lo == hi:
        r = lo
    elif hi == math.inf:
        r = math.inf if mag >= Fraction(2 ** 128) - Fraction(2 ** 103) else lo
    else:
        dl, dh = mag - Fraction(lo), Fraction(hi) - mag
        r = lo if dl < dh else hi if dh < dl else (lo if f32bits(lo) % 2 == 0 else hi)
    return -r if negative else r


def run(words, gpr=None, fpr=None, xer=0, cr=0, ctr=0, fpscr=0, data=b''):
    memory = Memory()
    memory.map('code', CODE, b''.join(struct.pack('>I', w) for w in words + [BLR]), writable=False)
    memory.map('data', DATA, bytearray(data) + bytearray(0x1000 - len(data)))
    memory.map('stack', STACK, 0x1000)
    m = Machine(memory, [(CODE, CODE + 4 * (len(words) + 1))], {}, 1000)
    m.gpr[1] = STACK + 0xF00
    for r, v in (gpr or {}).items():
        m.gpr[r] = v & MASK32
    for r, v in (fpr or {}).items():
        m.fpr[r] = v
    m.xer, m.cr, m.ctr, m.fpscr = xer, cr, ctr, fpscr
    m.call(CODE)
    return m


def cr0(value, so):
    v = signed(value)
    return (8 if v < 0 else 4 if v > 0 else 2) | so


class IntegerSemantics(unittest.TestCase):
    def setUp(self):
        rng = random.Random('oracle-v1-review-int')
        self.rng = rng
        self.values = [0, 1, 2, 0x7FFFFFFF, 0x80000000, MASK32, 0xFFFFFFFE, 0x80000001] + \
            [rng.getrandbits(32) for _ in range(120)]

    def test_extended_arithmetic_carry_overflow_and_record(self):
        for a in self.values:
            for b in self.rng.sample(self.values, 4) + [a]:
                for so in (0, 1):
                    for ca in (0, 1):
                        xer = (so << 31) | (ca << 29)
                        m = run([xo31(3, 4, 5, 138, 1, 1)], {4: a, 5: b}, xer=xer)  # addeo.
                        full = a + b + ca
                        ov = int(not -2 ** 31 <= signed(a) + signed(b) + ca < 2 ** 31)
                        self.assertEqual((m.gpr[3], m.ca, (m.xer >> 30) & 1, m.so, m.cr >> 28),
                                         (full & MASK32, full >> 32, ov, so | ov, cr0(full, so | ov)), (a, b, ca, so))
                        m = run([xo31(3, 4, 5, 136, 0, 1)], {4: a, 5: b}, xer=xer)  # subfeo
                        full = b + (~a & MASK32) + ca
                        ov = int(not -2 ** 31 <= signed(b) - signed(a) - 1 + ca < 2 ** 31)
                        self.assertEqual((m.gpr[3], m.ca, (m.xer >> 30) & 1), (full & MASK32, full >> 32, ov))
                        m = run([xo31(3, 4, 0, 200, 0, 1)], {4: a}, xer=xer)  # subfzeo
                        full = (~a & MASK32) + ca
                        self.assertEqual((m.gpr[3], m.ca), (full & MASK32, full >> 32))
                        m = run([xo31(3, 4, 0, 234)], {4: a}, xer=xer)  # addme
                        full = a + MASK32 + ca
                        self.assertEqual((m.gpr[3], m.ca), (full & MASK32, full >> 32))
            imm = self.rng.randrange(-32768, 32768)
            m = run([dform(8, 3, 4, imm)], {4: a})  # subfic
            self.assertEqual((m.gpr[3], m.ca), ((imm - a) & MASK32, int((imm & MASK32) >= a)))
            m = run([dform(12, 3, 4, imm)], {4: a})  # addic
            self.assertEqual(m.ca, int(a + (imm & MASK32) > MASK32))
            m = run([xo31(3, 4, 0, 104, 1, 1)], {4: a})  # nego.
            self.assertEqual((m.gpr[3], (m.xer >> 30) & 1), ((-a) & MASK32, int(a == 0x80000000)))

    def test_summary_overflow_is_sticky(self):
        m = run([xo31(3, 4, 5, 266, 0, 1), xo31(6, 7, 8, 266, 0, 1)], {4: 0x7FFFFFFF, 5: 1, 7: 1, 8: 1})
        self.assertEqual(((m.xer >> 31) & 1, (m.xer >> 30) & 1), (1, 0))

    def test_multiply_divide(self):
        for a in self.values:
            b = self.rng.choice(self.values)
            m = run([xo31(3, 4, 5, 235, 0, 1)], {4: a, 5: b})
            p = signed(a) * signed(b)
            self.assertEqual((m.gpr[3], (m.xer >> 30) & 1), (p & MASK32, int(not -2 ** 31 <= p < 2 ** 31)))
            self.assertEqual(run([xo31(3, 4, 5, 75)], {4: a, 5: b}).gpr[3], (p >> 32) & MASK32)
            self.assertEqual(run([xo31(3, 4, 5, 11)], {4: a, 5: b}).gpr[3], (a * b) >> 32)
            if b and not (a == 0x80000000 and b == MASK32):
                q = int(Fraction(signed(a), signed(b)))  # truncation toward zero
                m = run([xo31(3, 4, 5, 491, 1)], {4: a, 5: b})
                self.assertEqual((m.gpr[3], m.cr >> 28), (q & MASK32, cr0(q, 0)))
                self.assertEqual(run([xo31(3, 4, 5, 459)], {4: a, 5: b}).gpr[3], a // b)
        for a, b in ((0x80000000, MASK32), (5, 0), (0, 0)):
            with self.assertRaises(Unsupported):
                run([xo31(3, 4, 5, 491)], {4: a, 5: b})

    def test_shifts_carry_and_cntlzw(self):
        for a in self.values:
            for n in (0, 1, 31, 32, 33, 63, 64, self.rng.getrandbits(32)):
                sh = n & 63
                m = run([xo31(4, 3, 5, 792, 1)], {4: a, 5: n})
                expect = (signed(a) >> sh) & MASK32 if sh < 32 else (MASK32 if a >> 31 else 0)
                carry = int(a >> 31 == 1 and (sh >= 32 or a & ((1 << sh) - 1) != 0))
                self.assertEqual((m.gpr[3], m.ca), (expect, carry), (a, n))
            for sh in range(32):
                m = run([xo31(4, 3, sh, 824, 1)], {4: a})
                self.assertEqual((m.gpr[3], m.ca), ((signed(a) >> sh) & MASK32,
                                                    int(a >> 31 == 1 and a & ((1 << sh) - 1) != 0)), (a, sh))
            m = run([xo31(4, 3, 0, 26, 1)], {4: a})
            self.assertEqual((m.gpr[3], m.cr >> 28), (32 - a.bit_length(), cr0(32 - a.bit_length(), 0)))
        self.assertEqual(run([xo31(4, 3, 0, 26)], {4: 0}).gpr[3], 32)

    def test_rotates_with_wrap_masks(self):
        for a in self.values:
            b = self.rng.getrandbits(32)
            for _ in range(4):
                sh, mb, me = self.rng.randrange(32), self.rng.randrange(32), self.rng.randrange(32)
                mask = sum(1 << (31 - i) for i in range(32)
                           if (mb <= me and mb <= i <= me) or (mb > me and (i >= mb or i <= me)))
                rot = ((a << sh) | (a >> (32 - sh))) & MASK32 if sh else a
                word = (4 << 21) | (3 << 16) | (sh << 11) | (mb << 6) | (me << 1)
                m = run([(21 << 26) | word | 1], {4: a})
                self.assertEqual((m.gpr[3], m.cr >> 28), (rot & mask, cr0(rot & mask, 0)), (sh, mb, me))
                self.assertEqual(run([(20 << 26) | word], {4: a, 3: b}).gpr[3], (rot & mask) | (b & ~mask & MASK32))


class BranchAndMemory(unittest.TestCase):
    def test_bc_bo_bi_and_ctr_forms(self):
        for bo in range(32):
            for bit in (0, 1):
                for ctr in (0, 1, 2):
                    words = [(16 << 26) | (bo << 21) | (6 << 16) | 8, dform(14, 3, 0, 1), dform(14, 4, 0, 2)]
                    m = run(words, cr=bit << (31 - 6), ctr=ctr)
                    after = (ctr - 1) & MASK32 if not bo & 4 else ctr
                    ctr_ok = bool(bo & 4) or ((after != 0) != bool(bo & 2))
                    cond_ok = bool(bo & 16) or bit == (bo >> 3) & 1
                    self.assertEqual((m.gpr[3] == 0, m.ctr), (ctr_ok and cond_ok, after), (bo, bit, ctr))

    def test_bdnzlr_and_bcctr_decrement(self):
        m = run([dform(14, 3, 0, 2), xo31(3, 9, 0, 467), 0x4E000020, dform(14, 4, 0, 7)])
        self.assertEqual((m.gpr[4], m.ctr), (0, 1))
        with self.assertRaises(Unsupported):
            run([0x4E000420])

    def test_lmw_stmw(self):
        values = [random.Random('lmw').getrandbits(32) for _ in range(6)]
        m = run([dform(46, 26, 6, 0), dform(47, 26, 6, 0x40), dform(32, 8, 6, 0x40 + 20)], {6: DATA},
                data=b''.join(struct.pack('>I', v) for v in values))
        self.assertEqual(([m.gpr[r] for r in range(26, 32)], m.gpr[8]), (values, values[5]))


class FloatingPoint(unittest.TestCase):
    def test_fmadds_rounds_once_to_single(self):
        rng = random.Random('oracle-v1-review-fma')

        def single():
            return math.ldexp(rng.getrandbits(24) | 1 << 23, rng.randrange(-40, 40) - 23) * rng.choice((1, -1))
        checked = 0
        for i in range(1500):
            a, c = single(), single()
            b = -(a * c) + math.ldexp(rng.choice((1, -1)), rng.randrange(-70, -20)) if i % 2 else single()
            q = Fraction(a) * Fraction(c) + Fraction(b)
            if q == 0:
                continue
            m = run([aform(59, 1, 2, 3, 4, 29)], fpr={2: d2b(a), 3: d2b(b), 4: d2b(c)})
            self.assertEqual(m.fpr[1], d2b(nearest_single(q)), (a, b, c))
            checked += 1
        self.assertGreater(checked, 1000)  # absorbed near-cancellations give exact zeros
        # Exact a*c + b = 1 + 2^-23 + 2^-24 - 2^-60: rounding to double first lands on the tie and goes to even.
        a, c, b = 2.0 ** -12 * (1 + 2.0 ** -18), 2.0 ** -12 * (1 - 2.0 ** -18), 1 + 2.0 ** -23
        m = run([aform(59, 1, 2, 3, 4, 29)], fpr={2: d2b(a), 3: d2b(b), 4: d2b(c)})
        self.assertEqual(m.fpr[1], d2b(1 + 2.0 ** -23))

    def test_frsp_and_stfs_bit_selection(self):
        rng = random.Random('oracle-v1-review-frsp')
        for _ in range(500):
            x = math.ldexp(rng.getrandbits(53) | 1 << 52, rng.randrange(-180, 120) - 52)
            m = run([xform63(1, 0, 2, 12)], fpr={2: d2b(x)})
            self.assertEqual(m.fpr[1], d2b(nearest_single(Fraction(x))), x)
        # stfs of a value that is not a single selects bits (truncates); it does not round to nearest.
        x = 1.0 + 0.75 * 2.0 ** -23
        m = run([dform(52, 2, 6, 0), dform(32, 3, 6, 0)], {6: DATA}, fpr={2: d2b(x)})
        self.assertEqual(m.gpr[3], 0x3F800000)

    def test_fctiw_rounding_saturation_nan(self):
        def reference(x, mode):
            if math.isnan(x):
                return 0x80000000
            if math.isinf(x):
                return 0x7FFFFFFF if x > 0 else 0x80000000
            f = Fraction(x)
            fl = math.floor(f)
            r = {0: fl if f - fl < Fraction(1, 2) else fl + 1 if f - fl > Fraction(1, 2) else fl + (fl & 1),
                 1: int(f), 2: math.ceil(f), 3: fl}[mode]
            return max(-2 ** 31, min(2 ** 31 - 1, r)) & MASK32
        for x in (0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 3.7, -3.7, 2147483647.4, 2147483647.5, 2147483648.0,
                  -2147483648.5, -2147483649.0, 1e300, -1e300, math.inf, -math.inf, math.nan, -0.0):
            for mode in range(4):
                self.assertEqual(run([xform63(1, 0, 2, 14)], fpr={2: d2b(x)}, fpscr=mode).fpr[1] & MASK32,
                                 reference(x, mode), (x, mode))
                self.assertEqual(run([xform63(1, 0, 2, 15)], fpr={2: d2b(x)}, fpscr=mode).fpr[1] & MASK32,
                                 reference(x, 1), (x, mode))

    def test_fcmpu_fcmpo_and_fsel_with_nan(self):
        for a, b, nibble in ((1.0, 2.0, 8), (2.0, 1.0, 4), (0.0, -0.0, 2), (math.nan, 1.0, 1), (1.0, math.nan, 1)):
            for xo in (0, 32):
                m = run([xform63(3 << 2, 2, 3, xo)], fpr={2: d2b(a), 3: d2b(b)})
                self.assertEqual(((m.cr >> 16) & 0xF, m.cr & ~(0xF << 16), (m.fpscr >> 12) & 0xF), (nibble, 0, nibble))
        m = run([aform(63, 1, 2, 3, 4, 23)], fpr={2: d2b(math.nan), 3: d2b(5.0), 4: d2b(7.0)})
        self.assertEqual(b2d(m.fpr[1]), 5.0)

    def test_exact_zero_difference_is_negative_when_rounding_down(self):
        """OV-6: IEEE/PowerPC give -0 for x - x under round toward -infinity; ppc32 returns +0."""
        m = run([aform(63, 1, 2, 2, 0, 20)], fpr={2: d2b(1.5)}, fpscr=3)
        self.assertEqual(m.fpr[1], d2b(-0.0))

    def test_fmadd_propagates_frb_nan_before_frc(self):
        """OV-6: with FRA a number and FRB, FRC both NaN the architecture returns FRB; ppc32 returns FRC."""
        nb, nc = 0x7FF8000000000002, 0x7FF8000000000003
        m = run([aform(63, 1, 2, 3, 4, 29)], fpr={2: d2b(1.0), 3: nb, 4: nc})
        self.assertEqual(m.fpr[1], nb)


class SandboxEscapes(unittest.TestCase):
    def stops(self, words, error, **kw):
        memory = Memory()
        memory.map('code', CODE, b''.join(struct.pack('>I', w) for w in words), writable=False)
        memory.map('data', DATA, 0x1000)
        memory.map('stack', STACK, 0x1000)
        m = Machine(memory, kw.get('allowed', [(CODE, CODE + 4 * len(words))]), kw.get('stubs', {}), 1000)
        m.gpr[1] = STACK + 0xF00
        with self.assertRaises(error):
            m.call(CODE)

    def test_every_escape_stops(self):
        self.stops([0x48000101], UnknownCall)                                   # bl outside allowed code
        self.stops([dform(15, 3, 0, 0x2000), xo31(3, 8, 0, 467), BLR], UnknownCall)    # blr into data
        self.stops([dform(15, 3, 0, 0x2000), xo31(3, 9, 0, 467), 0x4E800420], UnknownCall)  # bctr into data
        self.stops([0x60000000], UnknownCall)                                   # falls off the allowed range
        self.stops([0x44000002], Unsupported)                                   # sc
        self.stops([0x7FE00008], Unsupported)                                   # tw
        self.stops([xo31(3, 12, 8, 371)], Unsupported)                          # mftb
        self.stops([xo31(0, 0, 3, 1014)], Unsupported)                          # dcbz
        self.stops([dform(15, 3, 0, 0x1000), dform(36, 3, 3, 0), BLR], MemoryFault)  # store into code
        self.stops([0x48000000], BudgetExceeded)

        def bad(m):
            m.lr = DATA
        self.stops([0x48000009, BLR, BLR], UnknownCall, allowed=[(CODE, CODE + 8)], stubs={CODE + 8: bad})


class RunnerAndBridgeContracts(unittest.TestCase):
    def test_oracle_variable_is_controlled_by_the_runner(self):
        """OV-2: harness.bin_root reads OPENTPW_MAC_BIN, which run_evidence_checks neither sets nor strips."""
        harness = (ORACLE / 'harness.py').read_text(encoding='utf-8')
        runner = (TOOLS / 'run_evidence_checks.py').read_text(encoding='utf-8')
        variables = set(re.findall(r"environ\.get\('([A-Z_]+)'\)", harness))
        fixture = set(re.search(r'FIXTURE_VARIABLES = \(([^)]*)\)', runner).group(1).replace("'", '').replace(
            '\n', '').replace(' ', '').split(','))
        self.assertLessEqual(variables, fixture)

    def test_bridge_digest_covers_every_referenced_project(self):
        """OV-4: the cache digest omits OpenTPW.Common, .ModKit and .Online, which OpenTPW.csproj references."""
        bridge = (ORACLE / 'bridge.py').read_text(encoding='utf-8')
        csproj = (REPO / 'source' / 'OpenTPW' / 'OpenTPW.csproj').read_text(encoding='utf-8')
        referenced = set(re.findall(r'ProjectReference Include="\.\.[\\/](OpenTPW\.[A-Za-z]+)[\\/]', csproj))
        self.assertTrue(referenced)
        hashed = set(re.findall(r"'source' / '(OpenTPW[.A-Za-z]*)'", bridge))
        self.assertLessEqual(referenced, hashed)

    def test_unusable_sdk_skips_only_the_csharp_side(self):
        """OV-3: a bridge that cannot build (e.g. a net8-only SDK) errors the whole Oracle class, plan pins included."""
        import differential
        import targets

        class Broken:
            def capabilities(self):
                raise RuntimeError('oracle bridge build failed')
        comparison = differential._csharp(targets.UNBOUNCE, 'csharp', None, [], [], True, 3, Broken())
        self.assertIsNotNone(comparison.skipped)


# ------------------------------------------------------------------ binary-gated: writers and findings
def _root():
    from harness import bin_root
    return bin_root()


@unittest.skipUnless(_root(), 'requires the identified Feral bin directory (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT)')
class Writers(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from harness import Image
        cls.image = Image.load(_root())

    def test_f1_ride_flag_0x8_is_info_hasqueue(self):
        """Build fragment 0xdab6c..0xdacbc ORs ride +46 from type-record booleans; +64 (Info.HasQueue) gives 0x8."""
        from harness import Sandbox
        from schema import fields
        rows = {row['runtime_offset']: row['path'] for row in fields(self.image.container.data_section.data, 0x39B64, 4)}
        self.assertEqual(rows[64], 'Info.HasQueue')
        sandbox = Sandbox(self.image, [(0xDAB6C, 0xDACC0)], {0xD7340: lambda m: None})  # map bookkeeping only
        bits = {}
        for offset in (None, 60, 64, 72, 240, 244, 248, 252, 260, 272):
            sandbox.reset()
            record = sandbox.struct(512, {} if offset is None else {offset: ('I', 1)})
            ride = sandbox.struct(512)
            sandbox.call(0xDAB6C, {28: record, 31: ride, 25: sandbox.struct(4)}, stop=0xDACBC)
            bits[offset] = sandbox.read(ride + 46, 'H')
        self.assertEqual(bits, {None: 0, 60: 0x4, 64: 0x8, 72: 0x100, 240: 0x10, 244: 0x1, 248: 0x40, 252: 0x2,
                                260: 0x20, 272: 0x80})
        self.assertEqual((rows[72], rows[244]), ('Info.RunsContinuously', 'UsageInfo.ProvidesRelief'))

    def test_f1_queue_limit_is_100_without_hasqueue(self):
        import targets
        sandbox = targets.QUEUE.sandbox(self.image)
        x = {'qwtc_bits': targets.bits(130.0), 'speed': 0, 'init_speed': 60, 'capacity': 5, 'duration': 30,
             'level': 0, 'count': 0}
        with_queue = targets.run_binary(targets.QUEUE, sandbox, {**x, 'flag8': 1})
        without = targets.run_binary(targets.QUEUE, sandbox, {**x, 'flag8': 0})
        self.assertEqual((with_queue['limit'], without['limit']), (21, 100))  # Belly Bounce numbers: trunc(130*5/30)

    def test_f3_word_0x84b2c_is_raised_only_for_the_last_cell(self):
        """0x84ea4 (line tool) clears the word, raises it to 1 around the final cell's 0x80c50 call, clears it."""
        from harness import Sandbox
        from ppc32 import s32
        flag = self.image.memory.u32(self.image.toc - 27980)
        self.assertEqual(flag - self.image.data_base, 0x84B2C)
        seen = []

        def place(m):
            seen.append(self.image.memory.u32(flag))
            m.gpr[3] = 1

        def absolute(m):
            m.gpr[3] = abs(s32(m.gpr[3])) & MASK32

        def in_bounds(m):
            m.gpr[3] = 1
        sandbox = Sandbox(self.image, [(0x84EA4, 0x85174), (0x1C4E9C, 0x1C4EB4)],
                          {'abs': absolute, 0xD70D8: in_bounds, 0x80C50: place})
        for start, end, cells in (((2, 3), (6, 4), 5), ((5, 1), (5, 4), 4), ((4, 4), (4, 4), 1), ((6, 2), (2, 1), 5)):
            sandbox.reset()
            seen.clear()
            sandbox.write(self.image.memory.u32(self.image.toc - 30080), 'I', sandbox.alloc(728 + 68 * 0x4000))
            sandbox.write(flag, 'I', 7)
            r = sandbox.call(0x84EA4, {3: 0, 4: start[0], 5: start[1], 6: sandbox.struct(4, {0: ('i', end[0])}),
                                       7: sandbox.struct(4, {0: ('i', end[1])})})
            self.assertEqual((r.r3, seen, self.image.memory.u32(flag)), (1, [0] * (cells - 1) + [1], 0), (start, end))

    def test_f3_queue_over_path_refused_only_on_the_last_cell(self):
        import targets
        sandbox = targets.CAN_CHANGE_TARGET.sandbox(self.image)
        self.assertEqual([targets.run_binary(targets.CAN_CHANGE_TARGET, sandbox, (1, 3, flag)) for flag in (0, 1)],
                         [True, False])

    def test_f5_strict_deadline_and_200ms_window(self):
        import targets
        sandbox = targets.UNBOUNCE.sandbox(self.image)
        out = [targets.run_binary(targets.UNBOUNCE, sandbox, {'now': now, 'slots': [(7, 31000, 1000)]})['guest']
               for now in (31000, 31001, 31199, 31200, 32000)]
        self.assertEqual(out, [0, 7, 7, 0, 7])

    def test_f7_single_call_site_and_f8_both_flags(self):
        import targets
        code = self.image.container.code.data
        calls, toc_reads = [], {-29472: set(), -29476: set()}
        for at in range(0, len(code) - 3, 4):
            w = struct.unpack_from('>I', code, at)[0]
            if w >> 26 == 18 and w & 3 == 1:
                d = w & 0x03FFFFFC
                d = d - 0x04000000 if d & 0x02000000 else d
                if at + d == 0x415B4:
                    calls.append(at)
            if w >> 26 == 32 and (w >> 16) & 31 == 2 and ppc32.signed16(w & 0xFFFF) in toc_reads:
                toc_reads[ppc32.signed16(w & 0xFFFF)].add(at)
        self.assertEqual(calls, [0x3FFA8])
        # Previous force / bank: only the reset 0x41510 region and the step read their TOC slots (COAST-plan's
        # "never written afterwards" holds for TOC references, so the oracle's zero previous values are faithful).
        self.assertEqual({k: sorted(v) for k, v in toc_reads.items()},
                         {-29472: [0x4154C, 0x415BC], -29476: [0x41568, 0x416A8]})
        sandbox = targets.COAST_FINAL.sandbox(self.image)
        x = {'S': 1.0, 'E': 1.0, 'peak_s': 1.0, 'peak_e': 1.0, 'length_a': 1.0, 'length_b': 1.0, 'max_y': 1.0,
             'max_speed': 1.0, 'sections': [0, 0x40, 0x80, 0xC0, 0x1C0]}
        self.assertEqual(targets.run_binary(targets.COAST_FINAL, sandbox, x)['sections'], 2)


if __name__ == '__main__':
    unittest.main()
