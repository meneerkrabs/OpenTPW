"""Synthetic header bounds plus optional identified PC fixture metadata."""
import os
from pathlib import Path
import struct
import unittest

import save_phase_evidence as phase


class SavePhaseTests(unittest.TestCase):
    @staticmethod
    def fixture():
        return b'RSSE' + struct.pack('<6I', 20, 1, 0xffffffff, 9, 2, 17)

    def test_distinct_pass_allocator_and_list_fields(self):
        self.assertEqual({'initialized_word': 1, 'pass_counter': 0xffffffff,
                          'next_script_id': 9, 'list_count': 2, 'opaque_head_reference': 17},
                         phase.manager_header(self.fixture(), 0))

    def test_embedded_header_uses_explicit_offset(self):
        self.assertEqual(9, phase.manager_header(b'prefix' + self.fixture(), 6)['next_script_id'])

    def test_truncation_and_invalid_offsets_are_rejected(self):
        for size in range(28):
            with self.subTest(size=size), self.assertRaises(ValueError):
                phase.manager_header(self.fixture()[:size], 0)
        for offset in [-1, 1, 29]:
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                phase.manager_header(self.fixture(), offset)

    def test_wrong_marker_or_header_width_is_rejected(self):
        for marker, length in [(b'FAIL', 20), (b'RSSE', 19), (b'RSSE', 21), (b'RSSE', 0xffffffff)]:
            with self.subTest(marker=marker, length=length), self.assertRaises(ValueError):
                phase.manager_header(marker + struct.pack('<6I', length, 1, 0, 1, 0, 0), 0)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_SAVE_PATH'), 'requires identified PC save')
    def test_identified_pc_manager_phase_candidate(self):
        result = phase.inspect(Path(os.environ['OPENTPW_PPC_SAVE_PATH']))
        self.assertEqual(6055, result['manager_fields']['pass_counter'])
        self.assertEqual(16, result['manager_fields']['next_script_id'])
        self.assertEqual(14, result['manager_fields']['list_count'])


if __name__ == '__main__':
    unittest.main()
