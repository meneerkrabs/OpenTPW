"""Selected operand decoder tests use synthetic instruction fields only."""
import struct
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest

import pef
from timer_evidence import call_target, d_fields, load_identified, require


def code(word):
    return SimpleNamespace(code=SimpleNamespace(data=struct.pack('>I', word)))


class TimerEvidenceTests(unittest.TestCase):
    def test_signed_d_operand(self):
        instruction = (14 << 26) | (6 << 21) | (3 << 16) | 0xfffe
        self.assertEqual(d_fields(code(instruction), 0, 14), (6, 3, -2))
        with self.assertRaises(pef.PEFError):
            d_fields(code(instruction), 0, 32)

    def test_relative_and_absolute_linked_branch(self):
        instruction = (18 << 26) | 0x03fffffc | 1
        self.assertEqual(call_target(code(instruction), 0), -4)
        self.assertEqual(call_target(code((18 << 26) | 0x24 | 3), 0), 0x24)
        with self.assertRaises(pef.PEFError):
            call_target(code(18 << 26), 0)

    def test_identity_rejects_unknown_binary_before_parsing(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'SimThemePark.data'
            path.write_text('synthetic input, not a game binary')
            with self.assertRaisesRegex(pef.PEFError, 'identity'):
                load_identified(path)

    def test_evidence_checks_are_not_disabled_by_python_optimization(self):
        self.assertEqual(require(1000, 1000, 'divisor'), 1000)
        with self.assertRaisesRegex(pef.PEFError, 'divisor'):
            require(60, 1000, 'divisor')


if __name__ == '__main__':
    unittest.main()
