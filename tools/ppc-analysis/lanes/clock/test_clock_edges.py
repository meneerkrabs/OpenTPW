"""Regression checks for bounded analytical clock slices, no original execution."""
import unittest
import clock_edges as edges


class ClockEdgeTests(unittest.TestCase):
    def test_unsigned_conversion_preserves_high_half(self):
        self.assertEqual(0x80000001, edges.clock_uint32(2147483649.9))

    def test_unsigned_conversion_clamps_and_truncates(self):
        for value, expected in [(-1.0, 0), (1.9, 1), (4294967295.9, 0xffffffff),
                                (4294967296.0, 0xffffffff)]:
            with self.subTest(value=value):
                self.assertEqual(expected, edges.clock_uint32(value))

    def test_nan_and_infinity_remain_outside_model(self):
        for value in [float('nan'), float('inf'), -float('inf')]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                edges.clock_uint32(value)

    def test_source_rollover_is_unsigned_delta(self):
        self.assertEqual((3.0, 3), edges.accumulate(0xfffffffe, 1, 0))

    def test_accumulator_output_saturates_after_source_wrap(self):
        total, output = edges.accumulate(0xfffffffe, 1, 4294967295.0)
        self.assertEqual(4294967298.0, total)
        self.assertEqual(0xffffffff, output)

    def test_scale_retains_fractional_accumulator(self):
        self.assertEqual((3.75, 3), edges.accumulate(10, 13, 0, 1.25))

    def test_scheduler_advances_on_any_positive_gap(self):
        result = edges.catchup_prefix(100, 101)
        self.assertEqual((1, 131), (result['steps'], result['previous']))

    def test_catchup_turn_cap_does_not_cap_script_passes(self):
        result = edges.catchup_prefix(0, 2000)
        self.assertEqual((65, 65, 3, 5), (result['steps'], result['script_passes'],
                                        result['turns'], result['missed_turn_phases']))
        self.assertFalse(result['still_comparing_now_greater'])

    def test_excess_backlog_is_dropped_before_catchup(self):
        result = edges.catchup_prefix(0, 5000)
        self.assertEqual((3000, 65, 5015),
                         (result['backlog_dropped_ms'], result['steps'], result['previous']))

    def test_world_exclusion_still_advances_scheduler_phase(self):
        result = edges.catchup_prefix(0, 2000, world_allowed=False)
        self.assertEqual((65, 65, 0, 0), (result['steps'], result['phase'],
                                        result['script_passes'], result['turns']))

    def test_signed_timestamp_boundary_is_bounded_not_run_to_completion(self):
        result = edges.catchup_prefix(0x7ffffffe, 0x7fffffff, limit=2)
        self.assertEqual(2, result['steps'])
        self.assertTrue(result['still_comparing_now_greater'])

    def test_script_phase_counter_increments_before_matching(self):
        self.assertEqual([1, 9], edges.ordinary_script_passes(1, 0, 16))
        self.assertEqual([8, 16], edges.ordinary_script_passes(0, 0, 16))
        self.assertEqual([1, 9], edges.ordinary_script_passes(0, 7, 16))

    def test_negative_bounds_rejected(self):
        with self.assertRaises(ValueError):
            edges.catchup_prefix(0, 1, limit=-1)
        with self.assertRaises(ValueError):
            edges.ordinary_script_passes(0, 0, -1)


if __name__ == '__main__':
    unittest.main()
