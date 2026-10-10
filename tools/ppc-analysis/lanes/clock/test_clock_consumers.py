"""Finite consumer arithmetic examples plus optional identified-native witness."""
import os
from pathlib import Path
import unittest

import clock_consumers as consumers


class ConsumerTests(unittest.TestCase):
    def test_timer_deadline_signed_clamp_and_equality(self):
        for deadline, now, expected in [(1300, 1000, 300), (1000, 1000, 0),
                                        (999, 1000, 0), (0x80000010, 0, 0),
                                        (0x7fffffff, 0, 0x7fffffff)]:
            with self.subTest(deadline=deadline, now=now):
                self.assertEqual(expected, consumers.timer_remaining(deadline, now))

    def test_timer_wrap_can_remain_positive(self):
        self.assertEqual(32, consumers.timer_remaining(0x10, 0xfffffff0))
        self.assertEqual(0, consumers.timer_remaining(0xfffffff0, 0x10))

    def test_manager_initialization_not_list_cardinality_controls_phase(self):
        self.assertEqual(7, consumers.manager_phase(7, False))
        self.assertEqual(8, consumers.manager_phase(7, True))

    def test_manager_phase_wrap_preserves_low_three_bits(self):
        self.assertEqual(0, consumers.manager_phase(0xffffffff, True))
        self.assertEqual(0xffffffff, consumers.manager_phase(0xffffffff, False))

    def test_animation_uses_selected_timestamp_and_channel_speed(self):
        self.assertEqual(30, consumers.animation_frame(1100, 100, 1))
        self.assertEqual(15, consumers.animation_frame(1100, 100, 0.5))
        self.assertEqual(-30, consumers.animation_frame(1100, 100, -1))

    def test_animation_elapsed_is_unsigned_wrapped_word(self):
        self.assertEqual(consumers.animation_frame(32, 0, 1),
                         consumers.animation_frame(0x10, 0xfffffff0, 1))
        # A backwards timestamp is not clamped: unsigned construction rounds
        # 0xffffff9c to 2^32 in binary32 before the multiply and divide.
        self.assertEqual(128849016.0, consumers.animation_frame(100, 200, 1))

    def test_animation_rounds_elapsed_before_multiply(self):
        self.assertEqual(consumers.animation_frame(16777216, 0, 1),
                         consumers.animation_frame(16777217, 0, 1))

    def test_nonfinite_animation_model_is_explicitly_unsupported(self):
        for speed in [float('nan'), float('inf'), -float('inf')]:
            with self.subTest(speed=speed), self.assertRaises(ValueError):
                consumers.animation_frame(1000, 0, speed)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires identified local PEF')
    def test_identified_native_dependency_witness(self):
        result = consumers.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual({'4': '0x1c1378', '10': '0x1c2264'}, result['callback_state_dispatch'])
        self.assertEqual(8, len(result['region_sha256']))
        self.assertEqual(4, len(result['c_runtime_region_sha256']))
        self.assertEqual(4, len(result['script_lifecycle_region_sha256']))
        self.assertEqual('tm_year', result['host_civil_opcodes']['97']['field'])
        self.assertEqual('tm_hour', result['host_civil_opcodes']['100']['field'])


if __name__ == '__main__':
    unittest.main()
