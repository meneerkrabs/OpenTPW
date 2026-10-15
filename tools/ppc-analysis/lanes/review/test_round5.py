"""Synthetic tests for round-5 review models; no original bytes needed.

Each case distinguishes a reviewed reading from a competing one. The optional original-file
test runs only when OPENTPW_PPC_BIN_ROOT points to the identified Feral Mac bin directory.
"""
from __future__ import annotations

import math
import os
import random
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round5_evidence as r5  # noqa: E402
from review_evidence import ReviewError  # noqa: E402

INT32 = (-(1 << 31), (1 << 31) - 1)


class RootNameKey(unittest.TestCase):
    def test_known_keys(self):
        self.assertEqual(r5.root_name_key(b'b_buy'), 557179197)
        self.assertEqual(r5.root_name_key(b'\xff'), -47, 'byte is sign-extended before XOR')

    def test_case_sensitive_and_nul_terminated(self):
        self.assertNotEqual(r5.root_name_key(b'CaseRoot'), r5.root_name_key(b'caseroot'))
        self.assertEqual(r5.root_name_key(b'base\0junk'), r5.root_name_key(b'base'))

    def test_unsigned_byte_reading_differs_for_high_bytes(self):
        unsigned = 0
        for byte in b'\xe9t\xe9':
            unsigned = ((unsigned ^ byte) * 47) & r5.U32
        self.assertNotEqual(r5.root_name_key(b'\xe9t\xe9') & r5.U32, unsigned)


class GuestHistory(unittest.TestCase):
    def test_magic_divisions_truncate_toward_zero(self):
        rng = random.Random(5)
        values = [0, 1, -1, 4, -4, 5, -5, 599, -599, *INT32] + [rng.randint(*INT32) for _ in range(5000)]
        for x in values:
            self.assertEqual(r5.magic_div5(x), r5.trunc_div(x, 5), x)
            self.assertEqual(r5.magic_div3(x), r5.trunc_div(x, 3), x)
            self.assertEqual(r5.srawi_addze(x, 2), r5.trunc_div(x, 4), x)
            self.assertEqual(r5.srawi_addze(x, 1), r5.trunc_div(x, 2), x)

    def test_temporary_history_counterexample(self):
        self.assertEqual(r5.native_history(600, 7, [0] * 4, [7] * 4), 5)
        self.assertEqual(r5.first_match_history(600, 7, [0] * 4, [7] * 4), 120)

    def test_used_history_stays_first_match(self):
        self.assertEqual(r5.native_history(600, 7, [7] * 4, [0] * 4), 120)
        self.assertEqual(r5.native_history(600, 7, [0, 7, 7, 0], [0] * 4), 150)

    def test_both_arrays_compose(self):
        self.assertEqual(r5.native_history(600, 7, [7, 0, 0, 0], [0, 0, 7, 7]), 600 // 5 // 3 // 2)

    def test_readings_agree_when_temporary_ids_are_unique(self):
        rng = random.Random(55)
        for _ in range(2000):
            temp = rng.sample(range(1, 20), 4)
            used = [rng.randint(0, 20) for _ in range(4)]
            args = (rng.randint(-10000, 10000), rng.randint(0, 20), used, temp)
            self.assertEqual(r5.native_history(*args), r5.first_match_history(*args))


class SinglePrecisionFma(unittest.TestCase):
    def test_cancellation_distinguishes_fused_from_separate(self):
        a, b, c = 3.0, r5.f32(0.1), r5.f32(-0.3)
        self.assertEqual(r5.fused_single(a, b, c), -2.0 ** -27)
        self.assertEqual(r5.separate_single(a, b, c), 0.0)

    def test_ties_round_to_even(self):
        self.assertEqual(r5.f32_exact(r5.Fraction(1) + r5.Fraction(1, 1 << 24)), 1.0)
        self.assertEqual(r5.f32_exact(r5.Fraction(1) + r5.Fraction(3, 1 << 24)), 1.0 + 2.0 ** -22)


class LayerOne(unittest.TestCase):
    def test_factor_formula(self):
        self.assertEqual(r5.layer1_factor(1, 0), r5.f32(4 / 3))
        self.assertEqual(r5.layer1_factor(14, 3), r5.f32(2 / 32767))

    def test_midpoint_margin(self):
        stored = r5.f32(1 / 3)
        self.assertGreater(r5.midpoint_margin_ulps(1 / 3, stored), 1 << 16)
        midpoint = stored + 2.0 ** (math.frexp(stored)[1] - 25)
        self.assertEqual(r5.midpoint_margin_ulps(midpoint, stored), 0)


class LoanLocator(unittest.TestCase):
    def test_shipped_shaped_offers_pass_both_formulas(self):
        for amount, apr, months in ((100000, 20, 36), (18000, 23, 24), (80000, 18, 48), (10000, 0, 36)):
            for monthly in (r5.mac_loan_monthly(amount, apr, months), r5.annuity_monthly(amount, apr, months)):
                self.assertTrue(r5.locator_accepts(amount, months, monthly), (amount, apr, months, monthly))

    def test_extreme_mod_offer_exceeds_the_four_times_bound(self):
        monthly = r5.mac_loan_monthly(10000, 100, 600)
        self.assertFalse(r5.locator_accepts(10000, 600, monthly))

    def test_reads_only_complete_offers(self):
        text = 'LoanInfo[0].LoanAmount\t\t100\nLoanInfo[0].APRInPercent 5\nLoanInfo[0].RepaymentPeriodInMonths 12\n' \
               'LoanInfo[1].Lendername 4\n'
        self.assertEqual(r5.read_loan_offers(text), [(0, 100, 5, 12)])


class ControllerDispatch(unittest.TestCase):
    CONTRACTS = {'coast_noop_command': 7, 'commands': [
        {'family': 'COAST', 'raw_command': 7, 'accumulator': 'preserve', 'parameter': 'consumed and ignored',
         'direct_controller_calls': []},
        {'family': 'BUMP', 'raw_command': 5, 'accumulator': 'host field +40', 'parameter': 'consumed and ignored',
         'direct_controller_calls': []},
        {'family': 'BUMP', 'raw_command': 13, 'accumulator': 'original input',
         'parameter': 'resolved input multiplied by 30', 'direct_controller_calls': [1]},
        {'family': 'TOUR', 'raw_command': 16, 'accumulator': 'result', 'parameter': 'required variable output',
         'direct_controller_calls': [1]}]}
    SOURCE = '''(Opcode.COAST, 7) => CommandBehavior.NoOperation,
        (Opcode.BUMP, 5) => CommandBehavior.ResultIgnored,
        (Opcode.BUMP, 13 or 14) => CommandBehavior.OriginalInput,
        (Opcode.TOUR, 4 or 16) => CommandBehavior.RequiredOutputResult,'''

    def test_mapping(self):
        expected = r5.expected_controller_table(self.CONTRACTS)
        actual = r5.parse_dispatch_table(self.SOURCE)
        self.assertEqual({k: actual[k] for k in expected}, expected)

    def test_flag_setting_mutator_is_detected(self):
        wrong = self.SOURCE.replace('(Opcode.COAST, 7) => CommandBehavior.NoOperation',
                                    '(Opcode.COAST, 7) => CommandBehavior.ResultIgnored')
        self.assertNotEqual(r5.parse_dispatch_table(wrong)[('COAST', 7)],
                            r5.expected_controller_table(self.CONTRACTS)[('COAST', 7)])

    def test_duplicate_entries_rejected(self):
        with self.assertRaises(ReviewError):
            r5.parse_dispatch_table(self.SOURCE + '\n(Opcode.BUMP, 5) => CommandBehavior.PreserveInput,')


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'identified Mac binaries not configured')
class OriginalWitness(unittest.TestCase):
    def test_round5_witness(self):
        result = r5.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(result['guests']['counterexample']['native'], 5)
        self.assertEqual(result['ui']['b_buy'], 557179197)
        self.assertEqual(result['layer1']['factors'], 882)


if __name__ == '__main__':
    unittest.main()
