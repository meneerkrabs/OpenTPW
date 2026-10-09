"""Sign record/operand and synthetic channel arithmetic regressions."""
import os
from pathlib import Path
import struct
import unittest

from corpus import wad
from sign_evidence import metadata, native_sign_reader, relief_channel_reference


class SignReferenceTests(unittest.TestCase):
    def test_shared_coefficient_changes_all_source_channels(self):
        self.assertEqual(relief_channel_reference((40, 80, 120), 67, (0.5, 0, 0, 2), 1, 1), (67, 20, 40, 60))
        self.assertEqual(relief_channel_reference((40, 80, 120), 67, (1, 0, 0, 2), 1, 1), (67, 40, 80, 120))
        self.assertEqual(relief_channel_reference((40, 80, 120), 67, (0.5, 0.5, 0, 2), 1, 1), (67, 40, 80, 120))

    def test_specular_adds_white_and_mask_controls_alpha(self):
        self.assertEqual(relief_channel_reference((20, 40, 60), 31, (0, 0, 0.5, 1), 0, 1), (31, 127, 127, 127))
        self.assertEqual(relief_channel_reference((20, 40, 60), 0, (1, 1, 1, 1), 1, 1), (0, 0, 0, 0))
        self.assertEqual(relief_channel_reference((20, 40, 60), 31, (10, 10, 10, 1), 1, 1), (31, 255, 255, 255))
        self.assertEqual(relief_channel_reference((20, 40, 60), 31, (1, 1, 1, 1), -1, -1), (31, 20, 40, 60))

    def test_nonfinite_reference_rejected(self):
        with self.assertRaises(ValueError):
            relief_channel_reference((20, 40, 60), 31, (float('nan'), 1, 1, 1), 1, 1)

    def test_bitmap_bounds_fail_before_payload_read(self):
        data = bytearray(889 + 12)
        struct.pack_into('<I', data, 0, 100)
        struct.pack_into('<3I', data, 889, 0xffffffff, 0xffffffff, 4)
        with self.assertRaisesRegex(ValueError, 'bitmap bounds'):
            metadata(data)


@unittest.skipUnless(os.environ.get('UI_EVIDENCE_BIN_ROOT') and os.environ.get('UI_EVIDENCE_PC_DATA') and os.environ.get('UI_EVIDENCE_MAC_LOBBY'), 'private sign corpus paths not supplied')
class NativeSignTests(unittest.TestCase):
    def test_identified_reader_and_source_consumers(self):
        report = native_sign_reader(Path(os.environ['UI_EVIDENCE_BIN_ROOT']))
        self.assertEqual((report['header_bytes'], report['font_bytes'], report['effect_bytes']), (17, 392, 44))
        self.assertEqual(report['source_rgb_byte_offsets'], [1, 2, 3])
        self.assertIn('generated mask', report['effect_alpha'])

    def test_all84_records_and_four_mac_records(self):
        root = Path(os.environ['UI_EVIDENCE_PC_DATA'])
        paths = list(root.glob('levels/*/rides/*.wad')) + list(root.glob('levels/*/features/*.wad')) + [root / 'lobby.wad']
        reports = [metadata(data) for path in paths for name, data in wad(path).items() if name.lower().endswith('.sgn')]
        self.assertEqual(len(reports), 84)
        self.assertEqual(sum(report['styles'] == (0, 0) for report in reports), 3)
        self.assertEqual(sum(len(report['images']) == 3 for report in reports), 23)
        for report in reports:
            self.assertEqual(report['trailing_bytes'], 0)
            self.assertTrue(all((image['width'], image['height'], image['bytes_per_pixel'], image['payload_bytes']) == (16, 128, 4, 8192) for image in report['images'][:2]))
        mac = wad(Path(os.environ['UI_EVIDENCE_MAC_LOBBY']))
        pc = wad(root / 'lobby.wad')
        names = [name for name in mac if name.lower().endswith('.sgn')]
        self.assertEqual(len(names), 4)
        for name in names:
            self.assertEqual(metadata(mac[name]), metadata(pc[name]))


if __name__ == '__main__':
    unittest.main()
