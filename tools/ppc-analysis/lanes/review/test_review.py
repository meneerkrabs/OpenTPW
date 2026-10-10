"""Synthetic review tests; no original bytes are required or stored.

They target interpretation hazards rather than restating the verifier's
expected constants: extended-mnemonic operand order, partial glue matches,
relocation repeat ambiguity, the i64-versus-Mac-order loan record readings and
floating-point operation order of the recovered loan expression.
"""
import math
import struct
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import reloc_audit  # noqa: E402
import review_evidence as rv  # noqa: E402


def xo(rt, ra, rb, xo10):
    return (31 << 26) | (rt << 21) | (ra << 16) | (rb << 11) | (xo10 << 1)


class DecodeTests(unittest.TestCase):
    def test_subtract_from_operand_order(self):
        # "subc r0,r7,r5" (r7 - r5) is encoded as subfc rD=0, rA=5, rB=7.
        self.assertEqual(rv.decode(xo(0, 5, 7, 8)), ('x31', 0, 5, 7, 8, 0))
        self.assertNotEqual(rv.decode(xo(0, 5, 7, 8)), ('x31', 0, 7, 5, 8, 0))

    def test_logical_compare_immediate_is_unsigned(self):
        word = (10 << 26) | (0 << 21) | (0 << 16) | 0xFFFF
        self.assertEqual(rv.decode(word), ('cmpli', 0, 0, 0xFFFF))
        signed = (11 << 26) | 0xFFFF
        self.assertEqual(rv.decode(signed), ('cmpi', 0, 0, -1))

    def test_conditional_branch_targets(self):
        self.assertEqual(rv.decode((16 << 26) | (12 << 21) | (1 << 16) | 8, 0x100), ('bc', 12, 1, 0x108))
        self.assertEqual(rv.decode((16 << 26) | (12 << 21) | 0xFFF0, 0x100), ('bc', 12, 0, 0xF0))

    def test_glue_requires_all_six_words(self):
        import pef
        stub = struct.pack('>6I', 0x81820010, *rv.GLUE_TAIL)
        partial = struct.pack('>6I', 0x81820010, *rv.GLUE_TAIL[:-1], 0x4E800020)
        imports = [SimpleNamespace(name='target')]
        rel = {0x10: pef.RelocTarget('import', 0, 0)}
        for code, ok in ((stub, True), (partial, False)):
            b = rv.Binary.__new__(rv.Binary)
            b.code, b.rel, b.toc, b.name = code, rel, 0, 'synthetic'
            b.c = SimpleNamespace(imports=imports)
            if ok:
                self.assertEqual(b.glue(0), 'target')
            else:
                with self.assertRaises(rv.ReviewError):
                    b.glue(0)


    def test_unknown_binary_is_rejected_before_parsing(self):
        from tempfile import TemporaryDirectory
        with TemporaryDirectory() as directory:
            (Path(directory) / 'SimThemePark.data').write_text('synthetic, not a game binary')
            with self.assertRaisesRegex(rv.ReviewError, 'identity'):
                rv.Binary(Path(directory), 'SimThemePark.data', 'SimThemePark.data', rv.APP_TOC)


class RelocationRepeatTests(unittest.TestCase):
    def test_single_chunk_block_is_unambiguous(self):
        self.assertEqual(reloc_audit.audit_instrs([0x4000, 0x9000]), {'repeat_blocks': 1, 'ambiguous_blocks': 0})

    def test_block_with_32_bit_instruction_is_ambiguous(self):
        # RelocSetPosition (2 chunks) followed by a one-chunk-block repeat.
        self.assertEqual(reloc_audit.audit_instrs([0xA000, 0x0004, 0x9000]),
                         {'repeat_blocks': 1, 'ambiguous_blocks': 1})

    def test_nested_and_short_blocks_are_ambiguous(self):
        self.assertEqual(reloc_audit.audit_instrs([0x4000, 0x9000, 0x9100])['ambiguous_blocks'], 1)
        self.assertEqual(reloc_audit.audit_instrs([0x9100])['ambiguous_blocks'], 1)


def bank_words(apr):
    words = [25, 87987, 0, 1, 87787, 0, -12013]
    for i, (amount, months) in enumerate([(100000, 36), (50000, 36), (25000, 36), (10000, 36),
                                          (18000, 24), (30000, 30), (80000, 48), (65000, 30)]):
        monthly = math.trunc(amount * math.pow(1 + apr / 100, months / 12 * 0.5) / months)
        words += [int(amount <= 10000), amount, apr, months, monthly, 0, 0, i]
    return words


class LoanRecordTests(unittest.TestCase):
    def test_zero_apr_cannot_distinguish_the_two_readings(self):
        words = bank_words(0)
        self.assertEqual([rv.legacy_i64_amount(words, i) for i in range(8)],
                         [l['amount_available'] for l in rv.bank_record_mac_order(words)['loans']])

    def test_positive_apr_separates_the_readings(self):
        words = bank_words(20)
        mac = rv.bank_record_mac_order(words)['loans']
        legacy = [rv.legacy_i64_amount(words, i) for i in range(8)]
        self.assertEqual(mac[0]['amount_available'], 100000)
        self.assertEqual(legacy[0], (20 << 32) | 100000)
        # SaveEconomyRecords.IsLoanRecord rejects amounts above 100,000,000.
        self.assertTrue(all(value > 100_000_000 for value in legacy))

    def test_trailing_word_is_next_availability_flag(self):
        mac = rv.bank_record_mac_order(bank_words(0))
        flags = [l['loan_available'] for l in mac['loans']]
        self.assertEqual(flags, [0, 0, 0, 1, 0, 0, 0, 0])
        # Reading 8 words from the amount word puts flag i+1 in the last slot of record i.
        self.assertEqual(flags[1:], [0, 0, 1, 0, 0, 0, 0])


class ArithmeticModelTests(unittest.TestCase):
    def test_exponent_operation_order_is_exactly_months_over_24(self):
        for months in range(1, 601):
            self.assertEqual(months / 12.0 * 0.5, months / 24.0)

    def test_saturating_conversion_model_cases(self):
        def u32sat(x):
            if x < 0:
                return 0
            if not x < 2.0 ** 32:
                return 0xFFFFFFFF
            return math.trunc(x)
        self.assertEqual(u32sat(-0.5), 0)
        self.assertEqual(u32sat(float('nan')), 0xFFFFFFFF)
        self.assertEqual(u32sat(float('inf')), 0xFFFFFFFF)  # amount > 0, months = 0
        self.assertEqual(u32sat(2.0 ** 31 + 0.75), 2 ** 31)

    def test_calendar_seconds_are_exact_for_divisible_rate(self):
        for tick in (0, 1, 23, 24, 10 ** 6):
            self.assertEqual(tick * 15000 // 4, tick * 3750)
        self.assertNotEqual(7 * 15001 // 4, 7 * 15001 / 4)


if __name__ == '__main__':
    unittest.main()
