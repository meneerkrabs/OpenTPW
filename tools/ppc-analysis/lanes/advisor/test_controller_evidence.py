"""Synthetic controller edge witnesses; no proprietary fixture data."""
from pathlib import Path
import struct
import sys
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent))
import controller_evidence as controller


def function(primary, rd, ra, immediate, return_word=None):
    # Construct generic synthetic PowerPC D-form and return metadata.
    load = primary << 26 | rd << 21 | ra << 16 | immediate
    branch_lr = 19 << 26 | 20 << 21 | 16 << 1
    return SimpleNamespace(code=SimpleNamespace(data=struct.pack('>II', load,
        branch_lr if return_word is None else return_word)))


class ControllerWitnessTests(unittest.TestCase):
    def test_direct_balance_getter_identifies_configured_field(self):
        c = function(32, 3, 3, 160)
        self.assertEqual(controller.literal_score(c, 0, {160: {'key': 'Welcome.Score'}}),
                         {'kind': 'configured_field', 'field': 160, 'key': 'Welcome.Score'})

    def test_unrelated_register_getter_is_not_balance_proof(self):
        c = function(32, 3, 4, 160)
        self.assertEqual(controller.literal_score(c, 0, {160: {'key': 'Welcome.Score'}})['kind'],
                         'conditional_or_computed')

    def test_nonreturn_continuation_is_not_literal_score(self):
        c = function(32, 3, 3, 160, return_word=0)
        self.assertEqual(controller.literal_score(c, 0, {160: {'key': 'Welcome.Score'}})['kind'],
                         'conditional_or_computed')

    def test_negative_constant_score_preserved(self):
        c = function(14, 3, 0, 65535)
        self.assertEqual(controller.literal_score(c, 0, {}), {'kind': 'constant', 'value': -1})

    def test_cyclic_variants_start_first_and_wrap(self):
        self.assertEqual([controller.cyclic_variant(previous, 3) for previous in (-1, 0, 1, 2)],
                         [0, 1, 2, 0])
        self.assertEqual(controller.cyclic_variant(0, 1), 0)
        with self.assertRaises(ValueError):
            controller.cyclic_variant(0, 0)

    def test_cyclic_variant_preserves_signed_increment_wrap(self):
        self.assertEqual(controller.cyclic_variant(0x7FFFFFFF, 3), -0x80000000)

    def test_history_truncates_each_counter_before_subtraction(self):
        self.assertEqual(controller.history_elapsed(4, 3), 1)
        self.assertEqual(controller.history_elapsed(7, 4), 0)
        # This is not elapsed raw-counter subtraction divided by four.
        self.assertNotEqual(controller.history_elapsed(4, 3), (4 - 3) >> 2)

    def test_history_wrap_is_not_smooth_seconds(self):
        self.assertEqual(controller.history_elapsed(0, 0xFFFFFFFC), 0xC0000001)

    def test_deferred_scheduler_preserves_separate_margins(self):
        result = controller.derived_scheduler(1000, 2000, True, 2600, 200)
        self.assertEqual(result, {'animation_budget': 2500, 'lip_base': 1800,
            'speech_deadline': 1800, 'controller_reserved_span': 4800})
        immediate = controller.derived_scheduler(1000, 2000, False, 2600, 200)
        self.assertEqual(immediate['lip_base'], 800)
        self.assertIsNone(immediate['speech_deadline'])

    def test_original_window_mapping_covers_entire_half_window(self):
        coordinates = controller.original_window_coordinates()
        self.assertEqual(len(coordinates), 272)
        covered = {min(index, 512 - index) for _, _, index in coordinates}
        self.assertEqual(covered, set(range(257)))
        with self.assertRaises(ValueError):
            controller.synthesis_window([0] * 256)

    def test_callback_copy_rejects_unreviewed_operations(self):
        c = SimpleNamespace(code=SimpleNamespace(data=bytearray(0x16F38)))
        with self.assertRaises(controller.common.pef.PEFError):
            controller.callback_bindings(c)


if __name__ == '__main__':
    unittest.main()
