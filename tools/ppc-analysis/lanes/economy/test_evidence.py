"""Synthetic validation of the bounded economy metadata checks."""
import importlib.util
from pathlib import Path
import struct
import tempfile
from types import SimpleNamespace
import unittest
import schema

spec = importlib.util.spec_from_file_location('economy_evidence', Path(__file__).with_name('evidence.py'))
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


class EconomyEvidenceTests(unittest.TestCase):
    def test_standard_loan_examples_distinguish_annuity(self):
        offers = [(100000, 20, 36), (50000, 20, 36), (25000, 20, 36),
                  (10000, 20, 36), (18000, 23, 24), (30000, 22, 30),
                  (80000, 18, 48), (65000, 21, 30)]
        expected = [3651, 1825, 912, 365, 922, 1282, 2320, 2749]
        self.assertEqual([evidence.monthly_payment(*offer) for offer in offers], expected)
        self.assertEqual(evidence.monthly_payment(100000, 0, 36), 2777)

    def test_invalid_terms_rejected(self):
        for offer in [(1, 1, 0), (1, 1, -1), (-1, 1, 1), (1, -1, 1)]:
            with self.assertRaises(ValueError):
                evidence.monthly_payment(*offer)

    def test_unsigned_interest_accounting_retains_underflow(self):
        self.assertEqual(evidence.profit_interest(100000, 3651, 36), 873)
        self.assertEqual(evidence.profit_interest(100000, 2777, 36), 119304646)
        self.assertEqual(evidence.profit_interest(120000, 5000, 24), 0)
        with self.assertRaises(ValueError):
            evidence.profit_interest(1, 1, 0)

    def test_changed_file_identity_rejected_before_parse(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'synthetic.data'
            path.write_text('synthetic unrelated fixture')
            with self.assertRaises(evidence.pef.PEFError):
                evidence.load(path, evidence.APP_SHA)

    def test_xform_wrong_register_and_operation_fail(self):
        word = (31 << 26) | (3 << 21) | (4 << 16) | (235 << 1)
        c = SimpleNamespace(code=SimpleNamespace(data=struct.pack('>I', word)))
        evidence.xform(c, 0, 31, 235, (3, 4, 0))
        for operation, regs in [(491, (3, 4, 0)), (235, (3, 5, 0))]:
            with self.assertRaises(evidence.pef.PEFError):
                evidence.xform(c, 0, 31, operation, regs)

    def test_rating_branch_must_use_signed_less_than_gate_and_right_target(self):
        words = bytes(8) + struct.pack('>I', (16 << 26) | (4 << 21) | 0xfffc)
        c = SimpleNamespace(code=SimpleNamespace(data=words))
        evidence.below_cap_branch(c, 8, 4)
        with self.assertRaises(evidence.pef.PEFError):
            evidence.below_cap_branch(c, 8, 12)
        c.code.data = bytes(8) + struct.pack('>I', (16 << 26) | (12 << 21) | 4)
        with self.assertRaises(evidence.pef.PEFError):
            evidence.below_cap_branch(c, 8, 12)

    def test_sam_requires_complete_record(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'synthetic.sam'
            path.write_text('LoanInfo[0].LoanAmount 100000\nLoanInfo[0].APRInPercent 20\n'
                            'LoanInfo[0].RepaymentPeriodInMonths 36 // comment\n'
                            'LoanInfo[1].LoanAmount 50000\n')
            rows = evidence.sam_examples(path)['loans']
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0]['mac_formula_payment'], 3651)

    @staticmethod
    def descriptor(kind, name='', count=0, minimum=0, maximum=0):
        result = bytearray(60)
        struct.pack_into('>I', result, 0, kind)
        result[4:4 + len(name)] = name.encode('ascii')
        struct.pack_into('>ii', result, 36, minimum, maximum)
        struct.pack_into('>I', result, 52, count)
        return bytes(result)

    def test_schema_array_stride_count_word_and_embedded_shift(self):
        make = self.descriptor
        data = b''.join([make(0), make(5, 'A'), make(1, 'Group'), make(2),
                         make(5, 'B'), make(6, 'C', minimum=0, maximum=100),
                         make(3, 'Rows', count=3), make(5, 'D'), make(12)])
        rows = schema.fields(data, 0, embedded_offset=4)
        self.assertEqual([row['runtime_offset'] for row in rows], [16, 20, 24, 48])
        self.assertEqual([row['path'] for row in rows], ['Group.A', 'Rows.B', 'Rows.C', 'D'])
        self.assertEqual(rows[1]['array_stride'], 8)
        self.assertEqual(rows[1]['array_capacity'], 3)
        self.assertEqual((rows[2]['minimum'], rows[2]['maximum']), (0, 100))

    def test_schema_malformed_records_rejected(self):
        make = self.descriptor
        fixtures = [make(5, 'A'), make(13), make(1, 'Bad'),
                    b''.join([make(2), make(2), make(12)]),
                    b''.join([make(2), make(5, 'A'), make(3, 'Rows', count=0), make(12)]),
                    make(5)[:59]]
        for fixture in fixtures:
            with self.assertRaises(ValueError):
                schema.fields(fixture, 0)


if __name__ == '__main__':
    unittest.main()
