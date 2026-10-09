"""Round-9 tests. The clock model and the payload/header helpers are synthetic; the operand audits run
only when OPENTPW_PPC_BIN_ROOT names the identified Feral Mac bin directory, and the fixture walk only
when OPENTPW_PC_DATA names a TPW Data directory holding the identified jungle Easymode.TPWI.
"""
from __future__ import annotations

import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round9_evidence as r9  # noqa: E402
from review_evidence import APP_TOC, Binary, ReviewError  # noqa: E402


class ClockModel(unittest.TestCase):
    def test_original_rate_cases(self):
        self.assertIn('conclusion', r9.clock_cases())

    def test_equal_frame_is_not_past_the_end(self):
        channel = r9.Channel(30)
        self.assertEqual(channel.advance(1000), 30.0)
        self.assertEqual(channel.start, 0)

    def test_carry_is_capped_and_truncated(self):
        self.assertEqual(r9.carry_ms(100.0, 30.0, 30.0), 1000)
        self.assertEqual(r9.carry_ms(0.02, 10.0, 30.0), 0)
        self.assertEqual(r9.carry_ms(1.5, 30.0, 15.0), 100)

    def test_rate_is_not_the_original_at_fifteen(self):
        # The same 2,100 ms is 3 ticks of carry at 30 Hz but 1.5 at 15 Hz.
        a, b = r9.Channel(30), r9.Channel(30, 15)
        a.advance(1000); a.advance(100)
        b.advance(2000); b.advance(100)
        self.assertEqual((a.tick, b.tick), (3.0, 1.5))


class PayloadHelpers(unittest.TestCase):
    def test_coaster_body_lies_between_markers(self):
        payload = b'x' * 8 + b'EMAK' + struct.pack('<4I', 0, 0, 0, 1) + b'SAOC' + b'tail'
        body = r9.coaster_body(payload)
        self.assertEqual((body['body'], body['words']), ([12, 28], [0, 0, 0, 1]))

    def test_missing_trailing_marker_fails(self):
        with self.assertRaises(ReviewError):
            r9.coaster_body(b'EMAK' + bytes(16))

    def test_legacy_offsets_miss_the_shifted_header(self):
        raw = bytearray(0x700)
        struct.pack_into('<I', raw, 0, 500)
        raw[4] = 1
        struct.pack_into('<I', raw, 5, 19)
        raw[0x608 + 24] = 133
        raw[0x60d + 24:0x611 + 24] = b'BILZ'
        out = r9.header_offsets(bytes(raw))
        self.assertEqual((out['shift'], out['shifted_version'], out['shifted_bilz']), (24, 133, True))
        self.assertEqual((out['legacy_0x608'], out['legacy_bilz_at_0x60d']), (0, False))
        self.assertEqual(out['legacy_reader_outcome'], 'rejects as version 0')

    def test_scientist_walk_rejects_cycles(self):
        payload = bytearray(4000)
        struct.pack_into('<I', payload, 4, 0)
        struct.pack_into('<I', payload, 8, 2)
        struct.pack_into('<I', payload, 100, 5)
        struct.pack_into('<II', payload, 104, 5, 1)    # actor 5 links to itself
        with self.assertRaises(ReviewError):
            r9.scientist_walk(bytes(payload), head=100)


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'set OPENTPW_PPC_BIN_ROOT for the operand audit')
class OriginalOperands(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = Binary(Path(os.environ['OPENTPW_PPC_BIN_ROOT']), 'SimThemePark.data', 'SimThemePark.data', APP_TOC)

    def test_channel_clock(self):
        self.assertIn('0x40', r9.channel_clock(self.app)['start_clock'])

    def test_copy_back_gate(self):
        self.assertIn('0x00100000', r9.copy_back_gate(self.app)['flag_chain'])

    def test_scientist_tail(self):
        self.assertEqual(len(r9.scientist_tail(self.app)['staff_tail']), 12)

    def test_coaster_save(self):
        self.assertIn('trailing', r9.coaster_save(self.app)['boundary'])


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'set OPENTPW_PC_DATA to a TPW Data directory')
class PcFixture(unittest.TestCase):
    def test_scientist_and_empty_coaster_body(self):
        payload = r9.pc_payload(Path(os.environ['OPENTPW_PC_DATA']) / 'levels' / 'jungle' / 'Easymode.TPWI')
        walk = r9.scientist_walk(payload)
        self.assertEqual((walk['scientist'], walk['first_researcher'], walk['actors'], walk['next_used']), (30, 30, 13, 29))
        self.assertEqual(walk['body'], [1391929, 1392430])
        body = r9.coaster_body(payload)
        self.assertEqual((body['body'], body['words']), ([1606446, 1606462], [0, 0, 0, 1]))


if __name__ == '__main__':
    unittest.main()
