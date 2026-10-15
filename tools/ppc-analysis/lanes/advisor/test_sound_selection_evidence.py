"""Selection boundaries and parameter semantics; optional private witnesses."""
import os
from pathlib import Path
import unittest

import sound_selection_evidence as selection


def seed_for(candidate):
    return ((candidate - 1013904223) * pow(1664525, -1, 1 << 32)) & selection.U32


class SoundSelectionTests(unittest.TestCase):
    def test_seed_is_an_explicit_snapshot_not_advanced_per_draw(self):
        self.assertEqual(selection.candidate(0), 1013904223)
        seed = seed_for(500 << 16)
        self.assertEqual(selection.choose_sample([499, 500], seed), (1, 1))
        self.assertEqual(selection.choose_sample([499, 500], seed), (1, 1))

    def test_cumulative_disk_values_are_differenced_before_event_selection(self):
        self.assertEqual(selection.runtime_weights([10, 30, 60], True), [10, 20, 30])
        self.assertEqual(selection.runtime_weights([10, 30, 60], False), [10, 30, 60])
        self.assertEqual(selection.runtime_weights([0xffffffff, 1], True), [0xffffffff, 2])

    def test_event_weight_equality_and_zero_boundary(self):
        self.assertEqual(selection.choose_event([10, 20], seed_for(10 << 16)), (0, 0, True))
        self.assertEqual(selection.choose_event([10, 20], seed_for(11 << 16)), (1, 1, True))
        self.assertEqual(selection.choose_event([0, 20], seed_for(0)), (0, 0, True))

    def test_event_exhaustion_preserves_history_and_skips_selector_refresh(self):
        self.assertEqual(selection.choose_event([10, 20], seed_for(31 << 16), 1), (0, 1, False))

    def test_sample_threshold_equality_and_exhaustion_have_no_fallback(self):
        self.assertEqual(selection.choose_sample([10, 30], seed_for(30 << 16)), (1, 1))
        self.assertEqual(selection.choose_sample([10, 30], seed_for(31 << 16), 1), (None, 1))
        self.assertEqual(selection.choose_sample([], seed_for(0), 4), (None, 4))

    def test_singletons_ignore_threshold_weight_and_leave_previous_index(self):
        self.assertEqual(selection.choose_sample([0], seed_for(65535 << 16), 3, True), (0, 3))
        self.assertEqual(selection.choose_event([0], seed_for(65535 << 16), 3, True), (0, 3, False))

    def test_anti_repeat_only_above_two_choices_and_wraps(self):
        seed = seed_for(20 << 16)
        self.assertEqual(selection.choose_sample([10, 20], seed, 1, True), (1, 1))
        self.assertEqual(selection.choose_sample([5, 10, 20], seed, 2, True), (0, 0))
        self.assertEqual(selection.choose_event([5, 5, 10], seed, 2, True), (0, 0, True))
        self.assertEqual(selection.choose_event([5, 5, 10], seed, 2, False), (2, 2, True))

    def test_signed_byte_model_bound_is_explicit(self):
        with self.assertRaises(ValueError):
            selection.avoid_repeat(127, 128, 127, True)
        with self.assertRaises(ValueError):
            selection.choose_event([], 0)

    def test_branch_filters_inclusive_ranges_before_weighting(self):
        links = [(9, 10, 0, 20), (4, 20, 20, 40), (8, 1000, 41, 99)]
        self.assertEqual(selection.choose_branch(links, 20, seed_for(10)), 9)
        self.assertEqual(selection.choose_branch(links, 20, seed_for(11)), 4)
        self.assertEqual(selection.choose_branch(links, 40, seed_for(0)), 4)
        self.assertIsNone(selection.choose_branch(links, 100, seed_for(0)))
        self.assertIsNone(selection.choose_branch(links, 256, seed_for(0)))

    def test_branch_zero_eligible_weight_is_a_native_divisor_dependency(self):
        with self.assertRaises(ValueError):
            selection.choose_branch([(1, 0, 0, 100)], 50, 0)

    def test_volume_random_range_swaps_and_excludes_upper_endpoint(self):
        self.assertEqual(selection.range_value(85, 17, seed_for(67)), 84)
        self.assertEqual(selection.range_value(17, 85, seed_for(68)), 17)
        self.assertEqual(selection.range_value(255, 255, 0), 255)

    def test_pitch_bytes_are_signed_and_random_upper_endpoint_excluded(self):
        self.assertEqual(selection.range_value(36, 232, seed_for(59), signed=True), 35)
        self.assertEqual(selection.range_value(232, 36, seed_for(60), signed=True), -24)
        self.assertEqual(selection.range_value(128, 128, 0, signed=True), -128)

    def test_parameter_interpolation_includes_upper_and_does_not_clamp(self):
        self.assertEqual(selection.range_value(17, 85, 0, 100), 85)
        self.assertEqual(selection.range_value(232, 36, 0, 100, signed=True), 36)
        self.assertEqual(selection.range_value(17, 85, 0, 255), 190)
        self.assertEqual(selection.range_value(0, 255, 0, 0xffffffff), 42949670)

    def test_parameter_codes_values_truncate_to_bytes_and_update_all_matches(self):
        self.assertEqual(selection.update_parameters([0, 19, 19, 20], [1, 2, 3, 4], 275, 300),
                         ([1, 44, 44, 4], True))
        self.assertEqual(selection.update_parameters([0, 19, 20, 0], [1, 2, 3, 4], 9, 100),
                         ([1, 2, 3, 4], False))
        self.assertEqual(selection.update_parameters([0, 19, 20, 21], [1, 2, 3, 4], 0, 5),
                         ([5, 2, 3, 4], False))

    def test_parameter_masks_choose_first_matching_source(self):
        self.assertEqual(selection.parameter_value(3, 3, [0, 40, 60, 0], 2), 40)
        self.assertEqual(selection.parameter_value(1, 2, [0, 40, 60, 0], 2), 60)
        self.assertIsNone(selection.parameter_value(0, 0, [0, 40, 60, 0], 1))

    def test_pitch_ratio_has_native_zero_special_case_and_offset(self):
        self.assertEqual(selection.pitch_ratio(0), 1.0)
        self.assertEqual(selection.pitch_ratio(95), 2.0)
        self.assertEqual(selection.pitch_ratio(-95), .5)
        self.assertAlmostEqual(selection.pitch_ratio(1), 2 ** (2 / 96))
        self.assertAlmostEqual(selection.pitch_ratio(-1), 2 ** (-2 / 96))

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'supplied Mac binaries not selected')
    def test_native_operands_loader_rng_boundary_parameter_and_frequency(self):
        result = selection.native(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(result['seed_data'], 0xc2e4)
        self.assertEqual(result['seed_relocated_alias_slots'], [0x348])
        self.assertEqual(len(result['seed_direct_sites']), 9)

    @unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'supplied PC corpus not selected')
    def test_actual_pc_loaded_weight_and_parameter_metadata(self):
        report = selection.inspect_corpus(Path(os.environ['OPENTPW_PC_DATA']))
        self.assertEqual(report['counts']['catalogs'], 31)
        self.assertEqual(report['counts']['events'], 1595)
        self.assertEqual(report['counts']['sample_arrays_below_full_draw_domain'], 201)
        self.assertEqual(report['counts']['nonmonotone_sample_threshold_arrays'], 0)
        rows = [r for r in report['selected'] if r['catalog'] == 'levels/fantasy/Sound/cat_ridesSFX.map' and r['catalog_id'] == 145]
        self.assertEqual([r['runtime_weight'] for r in rows], [10922] * 6)
        self.assertEqual(rows[0]['volume'], [17, 85])
        self.assertEqual(rows[0]['pitch_index'], [-24, 36])
        self.assertEqual(rows[0]['parameter_selectors'], [19, 0])
        self.assertEqual(rows[0]['parameter_masks'], [3, 0])

    @unittest.skipUnless(os.environ.get('OPENTPW_MAC_DATA'), 'selected extracted Mac corpus not supplied')
    def test_actual_mac_selected_metadata_matches_pc_numeric_records(self):
        report = selection.inspect_corpus(Path(os.environ['OPENTPW_MAC_DATA']))
        self.assertEqual(report['counts']['catalogs'], 4)
        self.assertEqual(report['counts']['events'], 400)
        self.assertEqual(report['counts']['sample_arrays_below_full_draw_domain'], 73)
        self.assertEqual(report['counts']['nonmonotone_sample_threshold_arrays'], 0)
        rows = [r for r in report['selected'] if r['catalog'] == 'levels/jungle/Sound/cat_ridesSFX.map' and r['catalog_id'] == 204]
        self.assertEqual([r['runtime_weight'] for r in rows], [13107] * 5)
        self.assertEqual(rows[0]['parameter_selectors'], [20, 0])


if __name__ == '__main__':
    unittest.main()
