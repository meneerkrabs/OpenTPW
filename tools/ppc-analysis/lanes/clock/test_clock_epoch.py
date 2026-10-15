"""Original-input witnesses for distinct shared saved clock epochs."""
import os
from pathlib import Path
import unittest

import clock_epoch_evidence as epoch


class ClockEpochTests(unittest.TestCase):
    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires identified original PEF')
    def test_native_epoch_producer_getter_alignment(self):
        result = epoch.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual(['scaled', 'unscaled'], result['saved_pair_order'])
        self.assertEqual(10, len(result['region_sha256']))
        self.assertEqual((12, 2, 0x11b3dc), tuple(result['post_load_skip_branch'][key] for key in ('bo', 'bi', 'target')))

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_SAVE_PATH'), 'requires identified PC fixture')
    def test_actual_saved_shared_epoch_and_near_deadline(self):
        result = epoch.inspect_save(Path(os.environ['OPENTPW_PPC_SAVE_PATH']))
        self.assertEqual((114374804, 114876286),
                         (result['saved_adjusted_scaled'], result['saved_adjusted_unscaled']))
        deadline = next(record for record in result['nonzero_deadlines'] if record['script_id'] == 3)
        self.assertEqual(114374867, deadline['word'])
        self.assertEqual(63, deadline['signed_modular_distance_from_saved_scaled'])
        self.assertEqual(4, len(result['nonzero_deadlines']))
        self.assertEqual(114938044, result['kolc_raw_clock_word'])
        self.assertEqual([114374806, 114374775, 114374589, 6055],
                         result['tnav_prefix_three_stamps_and_phase'])


if __name__ == '__main__':
    unittest.main()
