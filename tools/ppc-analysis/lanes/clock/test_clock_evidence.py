"""Synthetic PowerPC operand/control-edge tests; no original binaries needed."""
import struct
import unittest

import clock_evidence as evidence
import pef


def d(op, rd, ra, immediate):
    return struct.pack('>I', op << 26 | rd << 21 | ra << 16 | immediate & 0xffff)


def branch(delta, conditional=False, absolute=False, linked=False):
    mask = 0xfffc if conditional else 0x3fffffc
    return struct.pack('>I', (16 if conditional else 18) << 26 |
                       delta & mask | int(absolute) << 1 | int(linked))


class ClockEvidenceTests(unittest.TestCase):
    def test_signed_immediate_and_register_fields(self):
        self.assertEqual((12, 2, -22772), evidence.d_operand(d(32, 12, 2, -22772), 0, 32))
        self.assertEqual((0, 3, 31), evidence.d_operand(d(14, 0, 3, 31), 0, 14))

    def test_rejects_wrong_operation(self):
        with self.assertRaises(pef.PEFError):
            evidence.d_operand(d(14, 0, 3, 31), 0, 32)

    def test_rejects_unaligned_and_out_of_bounds(self):
        for offset in (-4, 1, 4):
            with self.subTest(offset=offset), self.assertRaises(pef.PEFError):
                evidence.word_at(d(14, 0, 3, 31), offset)

    def test_relative_linked_branch_sign_extension(self):
        self.assertEqual(-16, evidence.branch_target(branch(-16, linked=True), 0, True))
        self.assertEqual(20, evidence.branch_target(bytes(4) + branch(16, linked=True), 4, True))

    def test_conditional_back_edge(self):
        self.assertEqual(-492, evidence.branch_target(bytes(4) + branch(-496, conditional=True), 4))

    def test_absolute_branch_ignores_current_offset(self):
        self.assertEqual(16, evidence.branch_target(bytes(4) + branch(16, absolute=True), 4))

    def test_rejects_call_when_only_jump_expected(self):
        with self.assertRaises(pef.PEFError):
            evidence.branch_target(branch(16, linked=True), 0)

    def test_rejects_nonbranch_as_control_edge(self):
        with self.assertRaises(pef.PEFError):
            evidence.branch_target(d(14, 0, 3, 31), 0)


if __name__ == '__main__':
    unittest.main()
