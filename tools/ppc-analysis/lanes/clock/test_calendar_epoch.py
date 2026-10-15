"""Calendar field/domain separation; synthetic records and optional identified originals."""
import os
from pathlib import Path
import struct
import unittest

import calendar_epoch_evidence as calendar


class CalendarEpochTests(unittest.TestCase):
    def test_saved_funny_and_session_epochs_are_independent_fields(self):
        record = calendar.calendar_record(struct.pack('<QQiiI', 111, 222, -1, 31, 15000), 0)
        self.assertEqual({'funny_start': 111, 'session_start': 222, 'month_cache': -1,
                          'day_cache': 31, 'rate': 15000}, record)

    def test_conversion_uses_funny_epoch_not_host_session_timestamp(self):
        first = {'funny_start': 111, 'session_start': 222, 'rate': 15000}
        second = {**first, 'session_start': 999999999}
        expected = {'epoch_units': 111, 'added_seconds': 3750}
        self.assertEqual(expected, calendar.conversion_terms(first, 1))
        self.assertEqual(expected, calendar.conversion_terms(second, 1))

    def test_loaded_custom_funny_epoch_is_not_reset_to_constructor_default(self):
        record = calendar.calendar_record(struct.pack('<QQiiI', 1234567, 99, 0, -1, 15000), 0)
        self.assertEqual(1234567, calendar.conversion_terms(record, 0)['epoch_units'])

    def test_unsigned_product_divides_by_four_before_timestamp_scaling(self):
        self.assertEqual(2831250, calendar.conversion_terms({'funny_start': 0, 'rate': 15000}, 755)['added_seconds'])
        self.assertEqual(3, calendar.conversion_terms({'funny_start': 0, 'rate': 7}, 2)['added_seconds'])
        self.assertEqual(0, calendar.conversion_terms({'funny_start': 0, 'rate': 0}, 0xffffffff)['added_seconds'])

    def test_invalid_register_and_timestamp_domains_are_rejected(self):
        for turn, rate, epoch in [(-1, 15000, 0), (1 << 32, 15000, 0),
                                  (0, -1, 0), (0, 1 << 32, 0), (0, 15000, -1), (0, 15000, 1 << 64),
                                  (1.5, 15000, 0), (0, 15000.5, 0), (0, 15000, 1.5)]:
            with self.subTest(turn=turn, rate=rate, epoch=epoch), self.assertRaises(ValueError):
                calendar.conversion_terms({'funny_start': epoch, 'rate': rate}, turn)

    def test_calendar_candidate_span_checks_preserve_unaligned_offsets(self):
        payload = struct.pack('<QQiiI', 111, 222, 0, -1, 15000)
        self.assertEqual(222, calendar.calendar_record(b'x' + payload, 1)['session_start'])
        for length in range(28):
            with self.subTest(length=length), self.assertRaises(ValueError):
                calendar.calendar_record(payload[:length], 0)
        with self.assertRaises(ValueError):
            calendar.calendar_record(payload, -1)

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires identified original PEFs')
    def test_original_constructor_host_receiver_and_virtual_date_input(self):
        result = calendar.inspect(Path(os.environ['OPENTPW_PPC_BIN_ROOT']))
        self.assertEqual([2000, 1, 1, 0, 0, 0, 0], result['constructor_funny_civil_arguments'])
        self.assertEqual(8, result['host_local_time_receiver_offset'])
        self.assertEqual(0, result['virtual_date_receiver_offset'])
        self.assertEqual('mSessionStart', result['field_labels']['16'])
        self.assertEqual(6, len(result['region_sha256']))

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_SAVE_PATH'), 'requires identified original PC save')
    def test_original_saved_epochs_are_distinct_and_rate_is_divided(self):
        result = calendar.inspect_save(Path(os.environ['OPENTPW_PPC_SAVE_PATH']))
        self.assertEqual(125911584000000000, result['fields']['funny_start'])
        self.assertEqual(125850128932900000, result['fields']['session_start'])
        self.assertNotEqual(result['fields']['funny_start'], result['fields']['session_start'])
        self.assertEqual(2831250, result['conversion_terms_at_world_turn755']['added_seconds'])


if __name__ == '__main__':
    unittest.main()
