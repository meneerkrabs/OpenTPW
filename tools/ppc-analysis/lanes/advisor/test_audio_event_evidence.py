"""Synthetic bounds/reference tests; no original asset bytes are included."""
import struct
import unittest

import audio_event_evidence as events


def packed_catalog(sound_id=10000, samples=1, children=0, child_index=1):
    raw = bytearray(28 + 24 + 20 + 42)
    struct.pack_into('<I', raw, 24, 1)
    struct.pack_into('<I', raw, 28, 1)
    struct.pack_into('<II', raw, 52, sound_id, 1)
    struct.pack_into('<II', raw, 72, samples, children)
    raw.extend(b''.join(struct.pack('<IIIHH', 20000 + index, 30000, 40000, 1, 0)
                        for index in range(samples)))
    raw.extend(b''.join(struct.pack('<II', child_index, 50000) for _ in range(children)))
    return raw


def packed_banks(path=b'Synthetic\\Bank\0'):
    raw = bytearray(28 + 11)
    struct.pack_into('<I', raw, 24, 1)
    return raw + struct.pack('<I', len(path)) + path


class CatalogTests(unittest.TestCase):
    def test_sample_catalog_preserves_identifiers_and_opaque_values(self):
        parsed = events.catalog(packed_catalog())
        sound = parsed[0]['sounds'][0]
        self.assertEqual(sound['catalog_id'], 10000)
        self.assertEqual(sound['elements'][0]['samples'], [{'sample_id': 20000,
            'threshold_raw': 30000, 'span_raw': 40000, 'bank_id': 1, 'opaque': 0}])

    def test_multiple_samples_and_one_based_child_reference(self):
        element = events.catalog(packed_catalog(samples=2, children=1))[0]['sounds'][0]['elements'][0]
        self.assertEqual([sample['sample_id'] for sample in element['samples']], [20000, 20001])
        self.assertEqual(element['child_element_indices_one_based'], [1])

    def test_zero_and_excess_child_index_rejected(self):
        for child in (0, 2):
            with self.assertRaises(events.common.pef.PEFError):
                events.catalog(packed_catalog(children=1, child_index=child))

    def test_every_truncation_boundary_rejected(self):
        raw = packed_catalog(children=1)
        for length in (0, 27, 28, 51, 52, 71, 72, 113, 114, 129, 130, len(raw) - 1):
            with self.subTest(length=length), self.assertRaises(events.common.pef.PEFError):
                events.catalog(raw[:length])

    def test_record_count_is_bounded(self):
        for offset in (24, 28, 56):
            raw = packed_catalog()
            struct.pack_into('<I', raw, offset, 65537)
            with self.assertRaises(events.common.pef.PEFError):
                events.catalog(raw)

    def test_unexplained_tail_rejected(self):
        with self.assertRaises(events.common.pef.PEFError):
            events.catalog(packed_catalog() + b'\0')

    def test_empty_catalog_and_empty_sound_are_preserved(self):
        self.assertEqual(events.catalog(bytearray(28)), [])
        raw = bytearray(52)
        struct.pack_into('<I', raw, 24, 1)
        self.assertEqual(events.catalog(raw)[0]['sounds'], [])

    def test_duplicate_catalog_identifiers_rejected(self):
        raw = bytearray(92)
        struct.pack_into('<I', raw, 24, 1)
        struct.pack_into('<I', raw, 28, 2)
        struct.pack_into('<II', raw, 52, 10000, 0)
        struct.pack_into('<II', raw, 72, 10000, 0)
        with self.assertRaises(events.common.pef.PEFError):
            events.catalog(raw)
        struct.pack_into('<I', raw, 72, 10001)
        self.assertEqual([row['catalog_id'] for row in events.catalog(raw)[0]['sounds']], [10000, 10001])

    def test_bank_paths_use_eleven_byte_records(self):
        self.assertEqual(events.banks(packed_banks()), ['Synthetic\\Bank'])

    def test_bank_paths_reject_missing_or_embedded_terminators(self):
        for path in (b'', b'Synthetic', b'A\0B\0', b'\xff\0'):
            with self.assertRaises(events.common.pef.PEFError):
                events.banks(packed_banks(path))

    def test_bank_paths_reject_truncation_tail_and_excess_counts(self):
        raw = packed_banks()
        for invalid in (raw[:27], raw[:38], raw[:42], raw[:-1], raw + b'\0'):
            with self.assertRaises(events.common.pef.PEFError):
                events.banks(invalid)
        struct.pack_into('<I', raw, 24, 65537)
        with self.assertRaises(events.common.pef.PEFError):
            events.banks(raw)


if __name__ == '__main__':
    unittest.main()
