"""Synthetic tests for round-3 review models and witness helpers; no original bytes needed.

The optional original-file test runs only when OPENTPW_PPC_BIN_ROOT points to the
identified Feral Mac bin directory.
"""
from __future__ import annotations

import os
import struct
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round3_evidence as r3  # noqa: E402
from review_evidence import Binary, ReviewError  # noqa: E402


class RandModel(unittest.TestCase):
    def test_nonnegative_bound_is_inclusive(self):
        seen = {r3.rand_result(word << 1, 2) for word in range(30)}
        self.assertEqual(seen, {0, 1, 2})

    def test_minus_one_bound_returns_shifted_generator_word(self):
        # The zero divisor is multiplied by zero, so the remainder is x itself.
        for word in (0, 1, 0xFFFFFFFF, 0x80000001, 12345678):
            self.assertEqual(r3.rand_result(word, 0xFFFF), word >> 1)

    def test_more_negative_bounds_stay_nonnegative_and_below_magnitude(self):
        for raw in (0xFFFE, 0xFFF0, 0x8000):
            limit = -r3.s16(raw) - 1
            values = {r3.rand_result(word * 7919, raw) for word in range(5000)}
            self.assertTrue(all(0 <= v < limit for v in values), raw)
        self.assertEqual({r3.rand_result(w << 1, 0xFFFE) for w in range(10)}, {0})

    def test_bound_uses_low_sixteen_bits_sign_extended(self):
        self.assertEqual(r3.rand_result(0x200, 0x4000_0002), r3.rand_result(0x200, 2))


class ResearchModel(unittest.TestCase):
    def test_threshold_word_is_next_array_element(self):
        self.assertEqual([r3.research_threshold_element(g) for g in range(7)], [1, 2, 3, 4, 5, 6, 7])

    def test_shipped_thresholds_differ_from_same_index_reading(self):
        shipped = [0, 0, 80, 85, 85]
        # Opening group 2 from g = 1 at 50 % researched: element 2 (80) blocks it,
        # while a same-index reading (element 1 = 0) would open it immediately.
        self.assertFalse(r3.research_opens_next(50, 100, shipped[r3.research_threshold_element(1)]))
        self.assertTrue(r3.research_opens_next(50, 100, shipped[1]))

    def test_percentage_and_comparison_are_unsigned(self):
        self.assertEqual(r3.research_percentage(0, 0), 0)
        self.assertTrue(r3.research_opens_next(0, 0, 0))
        self.assertFalse(r3.research_opens_next(100, 100, -1))   # negative SAM value never opens
        self.assertEqual(r3.research_percentage(2, 3), 66)        # truncating divwu


class LoanImportModel(unittest.TestCase):
    def test_positive_apr_mac_reading_differs_from_importer_approximation(self):
        self.assertEqual(r3.mac_monthly_repayment(10000, 10, 24), 458)
        self.assertEqual(r3.opentpw_annuity_repayment(10000, 10, 24), 461)

    def test_zero_apr_readings_agree(self):
        for amount, months in [(100000, 36), (65000, 30), (18000, 24)]:
            self.assertEqual(r3.mac_monthly_repayment(amount, 0, months),
                             r3.opentpw_annuity_repayment(amount, 0, months))


class Bf4Model(unittest.TestCase):
    def test_lighter_colour_matches_floor_fifteenths(self):
        for d, c, n in [(0, 255, 15), (10, 200, 7), (100, 100, 15), (0, 1, 15)]:
            self.assertEqual(r3.bf4_channel(d, c, n), d + n * (c - d) // 15)

    def test_darker_colour_lags_one_coverage_step(self):
        self.assertEqual(r3.bf4_channel(255, 0, 15), 17)
        self.assertEqual(r3.bf4_channel(255, 0, 0), 255)


class CalendarModel(unittest.TestCase):
    def test_game_day_ticks(self):
        self.assertAlmostEqual(r3.ticks_per_game_day(), 23.04)
        self.assertAlmostEqual(540 * r3.ticks_per_game_day(), 12441.6)


class WitnessHelpers(unittest.TestCase):
    def fake(self, words: dict[int, int]) -> Binary:
        size = max(words) + 4
        code = bytearray(size)
        for at, word in words.items():
            struct.pack_into('>I', code, at, word)
        b = Binary.__new__(Binary)
        b.code, b.name = bytes(code), 'synthetic'
        b.c = SimpleNamespace()
        return b

    def test_rlwinm_and_x31_mutations_are_rejected(self):
        clrlwi_27_4_31 = 21 << 26 | 4 << 21 | 27 << 16 | 0 << 11 | 31 << 6 | 31 << 1
        good = self.fake({0: clrlwi_27_4_31})
        good.expect(0, *r3._rlwinm(4, 27, 0, 31, 31))
        for flip in (1 << 6, 1 << 1, 1, 1 << 21):
            with self.subTest(flip=flip), self.assertRaises(ReviewError):
                self.fake({0: clrlwi_27_4_31 ^ flip}).expect(0, *r3._rlwinm(4, 27, 0, 31, 31))
        divw = 31 << 26 | 0 << 21 | 3 << 16 | 4 << 11 | 491 << 1
        self.fake({0: divw}).expect(0, *r3._x31(0, 3, 4, 491))
        with self.assertRaises(ReviewError):   # divwu must not pass as divw
            self.fake({0: divw ^ (491 ^ 459) << 1}).expect(0, *r3._x31(0, 3, 4, 491))

    def test_direct_callers_ignore_unlinked_and_absolute_branches(self):
        bl = lambda at, target: 18 << 26 | (target - at) & 0x03FFFFFC | 1  # noqa: E731
        b = self.fake({0: bl(0, 0x40), 8: bl(8, 0x40), 16: bl(16, 0x40) & ~1, 24: bl(24, 0x44), 0x40: 0})
        self.assertEqual(r3._direct_callers(b, 0x40), [0, 8])


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires the identified local Mac bin directory')
class OriginalWitness(unittest.TestCase):
    def test_round3_witness(self):
        result = r3.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(result['glue'], {'containers': 16, 'standard_glue_stubs': 2209})
        self.assertEqual(result['bf4']['magic'], '0x88888889')
        self.assertEqual(result['bf4']['full_coverage_black_on_white'], 17)
        self.assertEqual(result['loan_import_example']['mac_reading'], 458)


if __name__ == '__main__':
    unittest.main()
