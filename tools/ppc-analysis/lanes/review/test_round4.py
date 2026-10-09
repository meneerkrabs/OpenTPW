"""Synthetic tests for round-4 review models; no original bytes needed.

Each case distinguishes a reviewed reading from a competing one. The optional original-file
test runs only when OPENTPW_PPC_BIN_ROOT points to the identified Feral Mac bin directory.
"""
from __future__ import annotations

import math
import os
import random
import struct
import sys
import unittest
from fractions import Fraction
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round4_evidence as r4  # noqa: E402
from review_evidence import ReviewError  # noqa: E402

GATE = {'flag8': (4, 0x1c230c), 'flag1': (4, 0x1c24c0), 'work': 0x1c230c}
MODES = {'mode0': (12, 0x1c23b4), 'mode2': (4, 0x1c23c0), 'mode1': (4, 0x1c2424),
         'direct': 0x1c23b4, 'mode1_check': 0x1c23c0, 'after_world': 0x1c2424}
U32 = 0xFFFFFFFF


class BranchPolarity(unittest.TestCase):
    def test_bo_encodings(self):
        self.assertTrue(r4.branch_taken(4, False))
        self.assertFalse(r4.branch_taken(4, True))
        self.assertTrue(r4.branch_taken(12, True))
        with self.assertRaises(ValueError):
            r4.branch_taken(20, True)

    def test_gate_is_flag8_or_not_flag1(self):
        for f8 in (False, True):
            for f1 in (False, True):
                self.assertEqual(r4.scheduler_route(GATE, f8, f1) == 'work', f8 or not f1, (f8, f1))

    def test_superseded_a5263bb_gate_disagrees_whenever_flag8_is_set(self):
        old = {(f8, f1): (not f8 and not f1) for f8 in (False, True) for f1 in (False, True)}
        new = {(f8, f1): r4.scheduler_route(GATE, f8, f1) == 'work' for f8 in (False, True) for f1 in (False, True)}
        self.assertEqual({k for k in old if old[k] != new[k]}, {(True, False), (True, True)})

    def test_gametype_routes(self):
        routes = {m: r4.mode_route(MODES, m) for m in range(4)}
        self.assertEqual(routes, {0: 'direct', 1: 'wrapper', 2: 'direct', 3: 'none'})
        # The superseded contract suppressed mode 0, i.e. offline Full Simulation would never tick.
        self.assertNotEqual(routes[0], 'none')

    def test_reading_bo12_as_branch_if_false_reproduces_the_superseded_mode0_claim(self):
        flipped = dict(MODES, mode0=(4, 0x1c23b4))
        self.assertEqual(r4.mode_route(flipped, 0), 'none')
        self.assertEqual(r4.mode_route(flipped, 1), 'direct')


class PackedVertex(unittest.TestCase):
    def test_engine_repack_then_extract_gives_little_endian_fields(self):
        rng = random.Random(4)
        words = [0, U32, 0x3FF, 0x200, 0x1FF << 10, 0x3FF << 20, 0xC0000000] + [rng.getrandbits(32) for _ in range(3000)]
        for word in words:
            self.assertEqual(r4.app_extract(r4.engine_repack(word)), r4.le_fields(word), hex(word))

    def test_top_two_bits_are_discarded_and_low_two_cleared(self):
        self.assertEqual(r4.engine_repack(0xC0000000), 0)
        self.assertEqual(r4.engine_repack(0x12345678) & 3, 0)

    def test_plain_byte_swap_reading_differs(self):
        word = 0x2ABCDEF1 & 0x3FFFFFFF
        swapped = struct.unpack('>I', struct.pack('<I', word))[0]
        self.assertNotEqual(r4.app_extract(swapped), r4.le_fields(word))
        self.assertNotEqual(r4.le_fields(swapped), r4.le_fields(word))

    def test_rotate_and_mask_helpers(self):
        self.assertEqual(r4.mask32(28, 28), 0x8)
        self.assertEqual(r4.mask32(30, 1), 0xC0000003)
        self.assertEqual(r4.rlwinm(0x1, 31, 0, 31), 0x80000000)
        self.assertEqual(r4.rlwimi(0xFFFFFFFF, 0, 0, 30, 31), 0xFFFFFFFC)


class SinglePrecision(unittest.TestCase):
    def test_single_rounding_matches_host_for_doubles(self):
        rng = random.Random(7)
        for _ in range(2000):
            value = rng.uniform(-1e6, 1e6)
            self.assertEqual(r4.f32_round(Fraction(value)), r4.f32(value))

    def test_fused_lerp_differs_from_unfused_somewhere(self):
        diffs = 0
        for cur in (0.1, 1.7, 33.3, -12.6, 1000.01):
            for nxt in (0.3, -4.2, 77.7, 1e3 / 3):
                for t in (0.1, 1 / 3, 0.7, 0.999):
                    c, n, tt = r4.f32(cur), r4.f32(nxt), r4.f32(t)
                    unfused = r4.f32(Fraction(r4.f32(Fraction(c) * Fraction(r4.f32(1 - Fraction(tt)))))
                                     + Fraction(r4.f32(Fraction(n) * Fraction(tt))))
                    diffs += r4.vertex_lerp(c, n, tt) != unfused
        self.assertGreater(diffs, 0)

    def test_bounds_order_is_observable(self):
        found = False
        for lerp in (r4.f32(x / 7) for x in range(1, 4000)):
            scale = r4.f32(0.0123)
            other = r4.f32(Fraction(lerp) - Fraction(r4.f32(Fraction(scale) + Fraction(1, 4))))
            if r4.bounds_lower(lerp, scale) != other:
                found = True
                break
        self.assertTrue(found, 'no case where (lerp - scale) - 0.25 differs from lerp - (scale + 0.25)')

    def test_on_key_time_keeps_prior_segment(self):
        ticks, time = [0, 10, 20], 10.0
        c = 0
        while ticks[c + 1] < time:
            c += 1
        self.assertEqual((c, (time - ticks[c]) / (ticks[c + 1] - ticks[c])), (0, 1.0))


class LoanModel(unittest.TestCase):
    def test_disabled_withdrawals_raise_profit_by_installment_minus_interest(self):
        p, n, m = 10000, 36, 365
        q = (m * n - p) // n
        bal, profit, repaid, _ = r4.loan_month(5000, 0, False, p, n, m, 0)
        self.assertEqual((bal, profit, repaid), (5000, m - q, 1))

    def test_payoff_charges_whole_term_interest(self):
        p, n, m = 10000, 36, 365
        q = (m * n - p) // n
        bal, profit = r4.loan_payoff(100000, 0, True, p, n, m, 10)
        self.assertEqual(bal, 100000 - m * 26)
        self.assertEqual(r4.signed32(profit), -m * 26 + m * 26 - q * n)
        self.assertNotEqual(r4.signed32(profit), -q * 26)

    def test_negative_balance_passes_unsigned_affordability(self):
        self.assertIsNotNone(r4.loan_payoff(-1 & U32, 0, True, 10000, 36, 365, 0))
        self.assertIsNone(r4.loan_payoff(100, 0, True, 10000, 36, 365, 0))

    def test_completion_is_equality_only(self):
        _, _, repaid, active = r4.loan_month(0, 0, True, 10000, 36, 365, 40)
        self.assertEqual((repaid, active), (41, True))

    def test_shipped_positive_apr_offers_are_far_from_integer_boundaries(self):
        # Standard.sam LoanInfo amount/APR/months; host-libm ulp differences cannot move these.
        for p, a, n in [(100000, 20, 36), (50000, 20, 36), (25000, 20, 36), (10000, 20, 36),
                        (18000, 23, 24), (30000, 22, 30), (80000, 18, 48), (65000, 21, 30)]:
            value = p * math.pow(1 + a / 100, n / 12 * 0.5) / n
            frac = value - math.floor(value)
            self.assertGreater(min(frac, 1 - frac), 0.1, (p, a, n))


class ProfitTicket(unittest.TestCase):
    def test_signed_compare_rejects_wrapped_negative_profit(self):
        self.assertFalse(r4.signed32(0xFFFFFFFF) > 15000)
        self.assertTrue(0xFFFFFFFF > 15000)  # what an unsigned reading would accept

    def test_two_zero_apr_loans_can_wrap_year_profit_positive(self):
        profit, loans = 0, [(100000, 36, 2777, 0), (100000, 36, 2777, 0)]
        for _ in range(10):
            new = []
            for p, n, m, k in loans:
                _, profit, k, _ = r4.loan_month(10 ** 9, profit, True, p, n, m, k)
                new.append((p, n, m, k))
            loans = new
        self.assertGreater(r4.signed32(profit), 15000)


class NoWriteScan(unittest.TestCase):
    def _bin(self, words):
        return SimpleNamespace(word=lambda at: words[at // 4])

    def test_detects_loads_moves_and_arithmetic_but_not_stores(self):
        lwz30 = 32 << 26 | 30 << 21 | 2 << 16
        mr30 = 31 << 26 | 3 << 21 | 30 << 16 | 3 << 11 | 444 << 1
        add30 = 31 << 26 | 30 << 21 | 4 << 16 | 5 << 11 | 266 << 1
        stw30 = 36 << 26 | 30 << 21 | 1 << 16
        cmpw30 = 31 << 26 | 30 << 16 | 4 << 11
        for bad in (lwz30, mr30, add30):
            with self.assertRaises(ReviewError):
                r4._no_write_to(self._bin([bad]), 30, 0, 4)
        r4._no_write_to(self._bin([stw30, cmpw30]), 30, 0, 8)


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'identified Mac binaries not configured')
class OriginalWitness(unittest.TestCase):
    def test_round4_witness(self):
        result = r4.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(result['scheduler']['mode_routes'], {'0': 'direct', '1': 'wrapper', '2': 'direct', '3': 'none'})
        self.assertIn('0xa4f44', result['formats']['unreported_flag_writes'])


if __name__ == '__main__':
    unittest.main()
