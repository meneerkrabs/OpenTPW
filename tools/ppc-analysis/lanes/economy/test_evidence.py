"""Synthetic validation of the bounded economy metadata checks."""
import importlib.util
from pathlib import Path
import struct
import tempfile
from types import SimpleNamespace
import unittest

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

    def test_sam_requires_complete_record(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'synthetic.sam'
            path.write_text('LoanInfo[0].LoanAmount 100000\nLoanInfo[0].APRInPercent 20\n'
                            'LoanInfo[0].RepaymentPeriodInMonths 36 // comment\n'
                            'LoanInfo[1].LoanAmount 50000\n')
            rows = evidence.sam_examples(path)['loans']
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0]['mac_formula_payment'], 3651)


if __name__ == '__main__':
    unittest.main()
