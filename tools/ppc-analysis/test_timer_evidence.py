"""Selected operand decoder tests use synthetic instruction fields only."""
import struct
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest

import pef
from timer_evidence import call_target, d_fields, glue_import, load_identified, require


def code(word):
    return SimpleNamespace(code=SimpleNamespace(data=struct.pack('>I', word)))


def d(opcode, rd, ra, immediate):
    return opcode << 26 | rd << 21 | ra << 16 | immediate & 0xffff


def synthetic_glue(words=None):
    if words is None:
        words = [d(32, 12, 2, -4), d(36, 2, 1, 20), d(32, 0, 12, 0),
                 d(32, 2, 12, 4), 31 << 26 | 9 << 16 | 467 << 1,
                 19 << 26 | 20 << 21 | 528 << 1]
    return SimpleNamespace(code=SimpleNamespace(data=struct.pack('>' + 'I' * len(words), *words)),
                           data_section=SimpleNamespace(index=1, data=bytes(8)),
                           relocs={1: {4: pef.RelocTarget('import', 0, 0)}},
                           imports=[SimpleNamespace(name='synthetic', library='TestLib')])


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

    def test_complete_synthetic_import_glue(self):
        self.assertEqual(glue_import(synthetic_glue(), 0, 8),
                         {'code_offset': 0, 'toc_slot': 4, 'symbol': 'synthetic', 'library': 'TestLib'})

    def test_complete_import_glue_at_nonzero_aligned_address(self):
        candidate = synthetic_glue()
        candidate.code.data = bytes(4) + candidate.code.data
        self.assertEqual(glue_import(candidate, 4, 8)['code_offset'], 4)

    def test_import_load_lookalikes_do_not_prove_full_glue(self):
        original = list(struct.unpack('>6I', synthetic_glue().code.data))
        # Alter each instruction's opcode, source/destination/base, displacement,
        # special register, branch condition/reserved fields or link bit.
        mutations = [(0, 1 << 21), (0, 1 << 16), (1, 1 << 26), (1, 1 << 21),
                     (1, 1 << 16), (1, 4), (2, 1 << 21), (2, 1 << 16), (2, 4),
                     (3, 1 << 21), (3, 1 << 16), (3, 4), (4, 1 << 21),
                     (4, 1 << 16), (4, 1 << 11), (4, 1 << 1), (4, 1), (5, 1 << 21), (5, 1 << 16),
                     (5, 1 << 11), (5, 1 << 1), (5, 1)]
        for index, mask in mutations:
            altered = original.copy()
            altered[index] ^= mask
            with self.subTest(index=index, mask=mask), self.assertRaises(pef.PEFError):
                glue_import(synthetic_glue(altered), 0, 8)

    def test_import_glue_rejects_every_truncated_prefix(self):
        original = synthetic_glue().code.data
        for size in range(len(original)):
            candidate = synthetic_glue()
            candidate.code.data = original[:size]
            with self.subTest(size=size), self.assertRaises(pef.PEFError):
                glue_import(candidate, 0, 8)

    def test_import_glue_rejects_unaligned_or_out_of_range_addresses(self):
        original = synthetic_glue().code.data
        for address in (-4, 1, 2, 4, 24):
            candidate = synthetic_glue()
            # Valid-looking code at an unaligned position must still fail.
            if address in (1, 2):
                candidate.code.data = bytes(address) + original
            with self.subTest(address=address), self.assertRaises(pef.PEFError):
                glue_import(candidate, address, 8)

    def test_import_glue_rejects_bad_toc_or_import_metadata(self):
        for toc in (-4, 7, 16):
            with self.subTest(toc=toc), self.assertRaises(pef.PEFError):
                glue_import(synthetic_glue(), 0, toc)
        for target in [None, pef.RelocTarget('section', 0, 0),
                       pef.RelocTarget('import', -1, 0), pef.RelocTarget('import', 1, 0),
                       pef.RelocTarget('import', 0, 4)]:
            candidate = synthetic_glue()
            candidate.relocs[1][4] = target
            with self.subTest(target=target), self.assertRaises(pef.PEFError):
                glue_import(candidate, 0, 8)

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
