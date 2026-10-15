"""Synthetic tests for the round-6 rides selector and SDT metadata models; no original bytes needed.

Each case separates the reviewed reading from a competing one. Original-file tests run only
when OPENTPW_PPC_BIN_ROOT (identified Feral Mac bin) or OPENTPW_PC_DATA (TPW Data) is set.
"""
from __future__ import annotations

import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round6_evidence as r6  # noqa: E402
from review_evidence import ReviewError  # noqa: E402


def header(version: int, layer: int, bitrate_index: int, rate_index: int, mode: int, padding: int = 0) -> bytes:
    word = 0x7FF << 21 | version << 19 | (4 - layer) << 17 | 1 << 16 | bitrate_index << 12
    word |= rate_index << 10 | padding << 9 | mode << 6
    return struct.pack('>I', word)


def entry(rate: int, payload: bytes) -> bytes:
    """A 40-byte entry header whose rate is a 32-bit little-endian word at offset 24."""
    return struct.pack('<ii16sIIIIII', 40, len(payload), b'x', rate, 16, 36, 0, 0, 0)[:40] + payload


class RidesSelectors(unittest.TestCase):
    def test_room_query_is_a_maximum(self):
        self.assertEqual(r6.room_query(100, 2, 0, 3), 100, 'global wins when larger')
        self.assertEqual(r6.room_query(1, 20, 4, 5), 11, 'remaining room wins when larger')
        self.assertNotEqual(r6.room_query(100, 2, 0, 3), min(100, 2 - 3), 'min reading differs')

    def test_room_query_is_signed_32_bit(self):
        self.assertEqual(r6.room_query(-5, 0, 0x7FFFFFFF, 1), -5,
                         'held + queued wraps to INT_MIN and 0 - INT_MIN wraps back to INT_MIN')
        self.assertEqual(r6.room_query(-1, -2147483648, 0, 1), 0x7FFFFFFF, 'INT_MIN - 1 wraps to INT_MAX')

    def test_capacity_plan_raises_then_clamps(self):
        self.assertEqual(r6.capacity_plan(10, 100, 100, 100), 100)
        self.assertEqual(r6.capacity_plan(10, 5, 100, 100), 10, 'global is a floor, not a ceiling')
        self.assertEqual(r6.capacity_plan(10, 500, 100, 40), 40)
        self.assertNotEqual(r6.capacity_plan(10, 100, 100, 100), min(10, 100, 100, 100))

    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'set OPENTPW_PPC_BIN_ROOT to the Feral Mac bin')
    def test_pinned_native_selectors(self):
        from review_evidence import Binary, APP_TOC
        app = Binary(Path(os.environ['OPENTPW_PPC_BIN_ROOT']), 'SimThemePark.data', 'SimThemePark.data', APP_TOC)
        result = r6.rides_selector_audit(app)
        self.assertIn('max(global', result['room_query_0x3dd14'])


class FrameFormat(unittest.TestCase):
    def test_layer1_slots_are_four_bytes(self):
        rate, channels, layer, size = r6.frame_format(header(2, 1, 8, 0, 3))
        self.assertEqual((rate, channels, layer), (22050, 1, 1))
        self.assertEqual(size, (12 * 128000 // 22050) * 4, 'Layer I counts 4-byte slots')
        self.assertNotEqual(size, 144 * 128000 // 22050, 'Layer II sizing differs')

    def test_lsf_layer2_uses_lsf_bitrates(self):
        _, _, _, size = r6.frame_format(header(2, 2, 8, 0, 0))
        self.assertEqual(size, 144 * 64000 // 22050, 'MPEG-2 index 8 is 64 kbit/s, not 128')

    def test_mpeg1_44100(self):
        self.assertEqual(r6.frame_format(header(3, 1, 4, 0, 3))[:3], (44100, 1, 1))

    def test_rejects_free_format_reserved_and_no_sync(self):
        self.assertIsNone(r6.frame_format(header(2, 2, 0, 0, 0)), 'free format')
        self.assertIsNone(r6.frame_format(header(2, 2, 15, 0, 0)), 'reserved bitrate')
        self.assertIsNone(r6.frame_format(header(1, 2, 8, 0, 0)), 'reserved version')
        self.assertIsNone(r6.frame_format(header(2, 2, 8, 3, 0)), 'reserved rate')
        self.assertIsNone(r6.frame_format(b'\x00\x00\x00\x00'))
        self.assertIsNone(r6.frame_format(b'\xff\xf3'))


class LegacyField(unittest.TestCase):
    def test_int16_wraps_44100(self):
        self.assertEqual(r6.legacy_container_rate(entry(44100, b'')), -21436, 'int16 read of 44100')
        self.assertEqual(r6.legacy_container_rate(entry(22050, b'')), 22050)


class Corpus(unittest.TestCase):
    def test_bank_bounds_fail_closed(self):
        with self.assertRaises(ReviewError):
            list(r6.sdt_entries(struct.pack('<ii', 1, 8)))

    @unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'set OPENTPW_PC_DATA to a TPW Data directory')
    def test_shipped_banks(self):
        result = r6.sdt_audit([Path(os.environ['OPENTPW_PC_DATA'])])
        self.assertEqual(result['fallback_entries'], 0)
        self.assertEqual(result['fixed_22050_wrong'], result['formats'].get('layer1/44100Hz/1ch', 0))


if __name__ == '__main__':
    unittest.main()
