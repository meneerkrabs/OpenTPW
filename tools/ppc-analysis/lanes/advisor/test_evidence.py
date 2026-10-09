"""Synthetic regression witnesses for advisor interpretation and asset bounds."""
import importlib.util
from pathlib import Path
import random
import struct
import sys
import unittest

spec = importlib.util.spec_from_file_location('advisor_evidence', Path(__file__).with_name('evidence.py'))
evidence = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = evidence
spec.loader.exec_module(evidence)


class AdvisorInterpretationTests(unittest.TestCase):
    def test_queue_priority_retains_earliest_equal_score(self):
        scores = (None, 25, 70, 70, 25, None)
        self.assertEqual(evidence.priority_slot(scores, True), 2)
        self.assertEqual(evidence.priority_slot(scores, False), 1)
        self.assertEqual(evidence.priority_slot((None,) * 8, True), -1)

    def test_mark_magic_matches_integer_division(self):
        values = [0, 1, 999, 1000, 1001, 2226893, 2812380, 4058820,
                  2147483647, 0x80000000, 0xFFFFFFFF]
        rng = random.Random(1729)
        values.extend(rng.randrange(1 << 32) for _ in range(10000))
        for word in values:
            signed = word if word < 0x80000000 else word - 0x100000000
            expected = abs(signed) // 1000 * (-1 if signed < 0 else 1)
            self.assertEqual(evidence.signed_mark_ms(word), expected, word)

    def test_exact_deadline_waits_for_strictly_later_clock(self):
        cursor = evidence.LipCursor((1000, 2000, 3000, 0xFFFFFFFF))
        self.assertTrue(cursor.update(1))
        self.assertFalse(cursor.update(2))
        self.assertEqual(cursor.index, 2)

    def test_one_expired_mark_per_update(self):
        cursor = evidence.LipCursor((1000, 2000, 3000, 0xFFFFFFFF))
        self.assertFalse(cursor.update(99))
        self.assertTrue(cursor.update(99))
        self.assertFalse(cursor.update(99))
        self.assertFalse(cursor.active)
        self.assertEqual(cursor.index, 4)

    def test_zero_mark_waits_at_start(self):
        cursor = evidence.LipCursor((0, 100000, 200000, 0xFFFFFFFF), start_ms=500)
        self.assertTrue(cursor.update(500))
        self.assertFalse(cursor.update(501))

    def test_terminator_ends_talking(self):
        cursor = evidence.LipCursor((1000, 0xFFFFFFFF))
        self.assertFalse(cursor.update(2))
        self.assertFalse(cursor.active)
        self.assertEqual(cursor.deadline, -1)
        self.assertFalse(cursor.update(10000))

    def test_fractional_mark_truncates(self):
        cursor = evidence.LipCursor((1999, 0xFFFFFFFF), start_ms=100)
        self.assertEqual(cursor.deadline, 101)

    def test_model_requires_terminator(self):
        with self.assertRaises(ValueError):
            evidence.LipCursor((1000,))


class AssetBoundaryTests(unittest.TestCase):
    def test_valid_lip_preserves_zero_and_raw_mark(self):
        self.assertEqual(evidence.lip_words(struct.pack('<4I', 0, 7, 1000, 0xFFFFFFFF)),
                         (0, 7, 1000, 0xFFFFFFFF))

    def test_lip_rejects_bad_shape_and_order(self):
        for words in [(1,), (1, 2), (2, 1, 3, 0xFFFFFFFF), (1, 0xFFFFFFFF, 2, 0xFFFFFFFF)]:
            with self.subTest(words=words), self.assertRaises(evidence.pef.PEFError):
                evidence.lip_words(struct.pack('<' + 'I' * len(words), *words))

    def test_lip_rejects_oversize(self):
        with self.assertRaises(evidence.pef.PEFError):
            evidence.lip_words(bytes(65544))

    def test_wad_bounds(self):
        bad = bytearray(88)
        bad[:4] = b'DWFB'
        struct.pack_into('<I', bad, 72, 1)
        with self.assertRaises(evidence.pef.PEFError):
            evidence.wad_members(bad)

    def test_wad_uncompressed_zero_expanded_field(self):
        raw = bytearray(144)
        raw[:4] = b'DWFB'
        struct.pack_into('<I', raw, 72, 1)
        struct.pack_into('<7I', raw, 88, 0, 128, 8, 136, 8, 0, 0)
        raw[128:136] = b'abc.LIP\0'
        self.assertEqual(evidence.wad_members(raw), {'abc.LIP': bytes(8)})
        struct.pack_into('<I', raw, 88 + 12, 141)
        with self.assertRaises(evidence.pef.PEFError):
            evidence.wad_members(raw)

    def test_sdt_bounds(self):
        with self.assertRaises(evidence.pef.PEFError):
            evidence.sdt_entries(struct.pack('<II', 1, 100))

    def test_sdt_mpeg_layer_metadata(self):
        raw = bytearray(52)
        struct.pack_into('<II', raw, 0, 1, 8)
        struct.pack_into('<II', raw, 8, 40, 4)
        raw[16:24] = b'test.mp2'
        # Synthetic MPEG-2 Layer II mono header; no original asset payload.
        header = (0x7FF << 21) | (2 << 19) | (2 << 17) | (3 << 6)
        struct.pack_into('>I', raw, 48, header)
        entries = evidence.sdt_entries(raw)
        self.assertEqual(entries[0]['layer'], 2)
        self.assertEqual(entries[0]['mpeg_version_id'], 2)
        self.assertEqual(entries[0]['channel_mode'], 3)
        raw[48] = 0
        with self.assertRaises(evidence.pef.PEFError):
            evidence.sdt_entries(raw)


if __name__ == '__main__':
    unittest.main()
