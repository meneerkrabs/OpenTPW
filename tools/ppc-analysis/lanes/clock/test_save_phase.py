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

    @staticmethod
    def script_fixture():
        script = bytearray(244)
        struct.pack_into('<I', script, 8, 9)
        struct.pack_into('<i', script, 60, -10000)
        struct.pack_into('<i', script, 148, 17)
        struct.pack_into('<I', script, 160, 0xffffffff)
        struct.pack_into('<I', script, 164, 123)
        script[184] = 1
        struct.pack_into('<h', script, 192, -50)
        struct.pack_into('<I', script, 196, 456)
        return SavePhaseTests.fixture() + b'PAD_' * 5 + struct.pack('<II', 2, 244) + script

    def test_first_script_fields_keep_distinct_native_words(self):
        result = phase.first_script_header(self.script_fixture(), 0)
        self.assertEqual(2, result['declared_script_count'])
        self.assertEqual({'script_id': 9, 'program_word_index': -10000, 'slice_budget': 17,
                          'wait_deadline': 0xffffffff, 'animation_wait_deadline': 123,
                          'phase_override': 1, 'speed_bias': -50, 'timer_deadline': 456},
                         result['first_script_fields'])

    def test_first_fixed_record_requires_its_entire_span(self):
        for size in [0, 27, 48, 55, 56, 299]:
            with self.subTest(size=size), self.assertRaises(ValueError):
                phase.first_script_header(self.script_fixture()[:size], 0)

    def test_first_script_requires_padding_count_and_fixed_size(self):
        for offset, data in [(28, b'FAIL'), (44, b'FAIL'),
                             (48, struct.pack('<I', 0)), (52, struct.pack('<I', 243))]:
            changed = bytearray(self.script_fixture())
            changed[offset:offset + 4] = data
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                phase.first_script_header(changed, 0)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_SAVE_PATH'), 'requires identified PC save')
    def test_identified_pc_manager_phase_candidate(self):
        result = phase.inspect(Path(os.environ['OPENTPW_PPC_SAVE_PATH']))
        self.assertEqual(6055, result['manager_fields']['pass_counter'])
        self.assertEqual(16, result['manager_fields']['next_script_id'])
        self.assertEqual(14, result['manager_fields']['list_count'])
        self.assertEqual(15, result['first_script_candidate']['first_script_fields']['script_id'])


if __name__ == '__main__':
    unittest.main()
