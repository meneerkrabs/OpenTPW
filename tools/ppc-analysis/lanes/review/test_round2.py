"""Synthetic tests for round-2 review models and witness helpers; no original bytes needed.

The optional original-file test runs only when OPENTPW_PPC_BIN_ROOT points to the
identified Feral Mac bin directory.
"""
from __future__ import annotations

import math
import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round2_evidence as r2  # noqa: E402
from review_evidence import ReviewError  # noqa: E402


class LoanProfitModel(unittest.TestCase):
    def test_zero_apr_remainder_wraps_unsigned_divide(self):
        # 10000 over 36 months at 0 %: M = 277, M*N = 9972 < P.
        q = (9972 - 10000 + (1 << 32)) // 36
        self.assertEqual(r2.loan_month_profit_delta(277, 36, 10000), -q)
        self.assertEqual(r2.loan_month_profit_delta(277, 36, 10000), -119304646)
        # The lane's floor rule gives a tiny value with the opposite sign.
        self.assertEqual(r2.naive_month_profit_delta(277, 36, 10000), 1)

    def test_exact_and_positive_interest_match_floor_rule(self):
        self.assertEqual(r2.loan_month_profit_delta(750, 24, 18000), 0)
        for p, apr, n in [(100000, 20, 36), (18000, 23, 24), (80000, 18, 48)]:
            m = math.trunc(p * math.pow(1 + apr / 100, n / 12 * 0.5) / n)
            self.assertGreaterEqual(m * n, p)
            self.assertEqual(r2.loan_month_profit_delta(m, n, p), r2.naive_month_profit_delta(m, n, p))

    def test_disabled_withdrawals_still_adjust_profit(self):
        self.assertEqual(r2.loan_month_profit_delta(3651, 36, 100000, withdrawals_enabled=False),
                         3651 - (3651 * 36 - 100000) // 36)

    def test_zero_term_skips_adjustment(self):
        self.assertEqual(r2.loan_month_profit_delta(0xFFFFFFFF, 0, 1000), 1)  # -(-1) as signed u32

    def test_payoff_charges_whole_loan_interest_regardless_of_progress(self):
        m, n, p = 3651, 36, 100000
        q = (m * n - p) // n
        for repaid in (0, 1, 20, 35):
            self.assertEqual(r2.payoff_profit_delta(m, n, p, repaid), -q * n)


class SkillModel(unittest.TestCase):
    def test_single_precision_equals_integer_rule_on_byte_domain(self):
        for grade in range(0, 6):
            for pct in range(0, 256):
                self.assertEqual(r2.skill_single_precision(grade, pct), (100 * grade + pct) // 5, (grade, pct))

    def test_negative_grade_saturates_to_zero(self):
        self.assertEqual(r2.skill_single_precision(-1, 0), 0)


class GuestQueueModel(unittest.TestCase):
    def test_overfull_queue_goes_negative(self):
        self.assertEqual(r2.queue_match(0, 3), 100)
        self.assertEqual(r2.queue_match(12, 3), 0)
        self.assertEqual(r2.queue_match(24, 3), -100)
        self.assertEqual(r2.queue_match(5, 0), -25)  # field 0 is treated as 1

    def test_unsigned_combination_turns_negative_total_huge(self):
        total = 40 * r2.queue_match(24, 3) + 10 * 50  # weights 40/10
        self.assertLess(total, 0)
        self.assertGreater((total & r2.U32) // 50, 80_000_000)


class AnimationWait(unittest.TestCase):
    def test_floor_of_300_after_subtracting_300(self):
        self.assertEqual(r2.anim_wait_ms(0), 300)
        self.assertEqual(r2.anim_wait_ms(500), 300)
        self.assertEqual(r2.anim_wait_ms(600), 300)
        self.assertEqual(r2.anim_wait_ms(601), 301)
        self.assertEqual(r2.anim_wait_ms(1000), 700)


class _Words:
    def __init__(self, base: int, words: list[int]):
        self.base, self.words = base, words

    def word(self, at: int) -> int:
        return self.words[(at - self.base) // 4]


def _b(at: int, target: int, bo: int | None = None) -> int:
    if bo is None:
        return 0x48000000 | ((target - at) & 0x03FFFFFC)
    return (16 << 26) | (bo << 21) | (2 << 16) | ((target - at) & 0xFFFC)


class WitnessHelpers(unittest.TestCase):
    def test_branch_escape_is_rejected(self):
        nop = 0x60000000
        inside = _Words(0x100, [nop, _b(0x104, 0x10c), nop, nop])
        r2._branch_targets_within(inside, 0x100, 0x110, 0x100, 0x110)
        escape = _Words(0x100, [nop, _b(0x104, 0x200, bo=12), nop, nop])
        with self.assertRaises(ReviewError):
            r2._branch_targets_within(escape, 0x100, 0x110, 0x100, 0x110)
        backwards = _Words(0x100, [_b(0x100, 0xf0), nop])
        with self.assertRaises(ReviewError):
            r2._branch_targets_within(backwards, 0x100, 0x108, 0x100, 0x108)
        ret = _Words(0x100, [0x4E800020])
        with self.assertRaises(ReviewError):
            r2._branch_targets_within(ret, 0x100, 0x104, 0x100, 0x104)

    def test_destination_register_classes(self):
        addi_22 = (14 << 26) | (22 << 21) | (0 << 16) | 5
        mr_22 = (31 << 26) | (3 << 21) | (22 << 16) | (3 << 11) | (444 << 1)  # or r22,r3,r3
        stw_22 = (36 << 26) | (22 << 21) | (1 << 16) | 8                      # store: no destination
        cmpw = (31 << 26) | (0 << 21) | (22 << 16) | (3 << 11)                  # compare: no destination
        add_22 = (31 << 26) | (22 << 21) | (3 << 16) | (4 << 11) | (266 << 1)
        rlwinm_22 = (21 << 26) | (3 << 21) | (22 << 16)
        self.assertEqual(r2._dest_gpr(addi_22), 22)
        self.assertEqual(r2._dest_gpr(mr_22), 22)
        self.assertIsNone(r2._dest_gpr(stw_22))
        self.assertIsNone(r2._dest_gpr(cmpw))
        self.assertEqual(r2._dest_gpr(add_22), 22)
        self.assertEqual(r2._dest_gpr(rlwinm_22), 22)

    def test_signed_view(self):
        self.assertEqual(r2.s32(0xFFFFFFFF), -1)
        self.assertEqual(r2.s32(1 << 31), -(1 << 31))
        self.assertEqual(struct.unpack('>i', struct.pack('>I', 0x80000000))[0], r2.s32(0x80000000))


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires the identified local Mac bin directory')
class OriginalWitness(unittest.TestCase):
    def test_pinned_round2_witness(self):
        result = r2.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']), None, None)
        self.assertEqual(result['loan_profit']['events']['CMsgEndOfMonth']['type'], 12)
        self.assertEqual(result['opcode_names']['checked']['WAITABS']['opcode'], 45)
        self.assertEqual(result['guest_queue']['writers_of_r22'], ['0xe9414'])
        self.assertEqual(result['speed_control']['set']['value'], 1.0)


if __name__ == '__main__':
    unittest.main()
