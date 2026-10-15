"""Identified catalog row factories and display/font binding regressions."""
import os
from pathlib import Path
import unittest

from ui_consumers import inspect


@unittest.skipUnless(os.environ.get('UI_EVIDENCE_BIN_ROOT') and os.environ.get('UI_EVIDENCE_MAC_UITEXT') and os.environ.get('UI_EVIDENCE_MAC_MBTOUNI'), 'private UI identities not supplied')
class CatalogConsumersTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.result = inspect(Path(os.environ['UI_EVIDENCE_BIN_ROOT']),
                             Path(os.environ['UI_EVIDENCE_MAC_UITEXT']), Path(os.environ['UI_EVIDENCE_MAC_MBTOUNI']))

    def test_actual_category_controls_have_distinct_titles_and_ordinals(self):
        self.assertEqual([(row['control_id'], row['category'], row['label']) for row in self.result['categories']],
                         [(507, 0, 'Buy Ride'), (509, 1, 'Buy Shop'), (506, 2, 'Buy Sideshow'), (508, 3, 'Buy Miscellaneous Items')])

    def test_catalog_row_factory_and_initializer_are_original_callbacks(self):
        self.assertEqual(self.result['catalog_control'], 504)
        self.assertEqual([callback['code'] for callback in self.result['callbacks']], [0x163e7c, 0x164140, 0x164268])
        self.assertIn('font pixel height', self.result['row_height'])
        self.assertIn('integer truncation', self.result['row_count'])

    def test_display_selectors_share_the_fontbank_field_and_actual_mac_labels(self):
        self.assertEqual(list(self.result['display_bank_labels'].values()), [' 512 x 384', ' 640 x 480', ' 800 x 600', ' 1024 x 768'])
        self.assertIn('actual display-mode creation consumer', self.result['remaining'])


if __name__ == '__main__':
    unittest.main()
