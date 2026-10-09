"""Synthetic boundary checks for the identity-bounded planning witness."""
from pathlib import Path
import struct
import tempfile
import unittest
import zlib
import save_bridge_evidence as bridge


class SaveBridgeEvidenceTests(unittest.TestCase):
    @staticmethod
    def container(payload, declared=None, trailer=b''):
        raw = bytearray(bridge.PAYLOAD_OFFSET)
        struct.pack_into('<I', raw, 0, 500)
        raw[0x608] = 133
        raw[bridge.CHUNK_OFFSET:bridge.CHUNK_OFFSET + 4] = b'BILZ'
        raw += zlib.compress(payload) + trailer
        struct.pack_into('<II', raw, bridge.CHUNK_OFFSET + 4,
                         len(payload) if declared is None else declared, len(raw) - bridge.CHUNK_OFFSET)
        return bytes(raw)

    def test_container_exact_decode_and_no_trailing_or_oversize(self):
        self.assertEqual(bridge.decode_container(self.container(b'synthetic')), b'synthetic')
        for raw in [self.container(b'synthetic', declared=8), self.container(b'synthetic', declared=10),
                    self.container(b'synthetic', trailer=b'trailing'), self.container(b'x')[:-1], bytes(10)]:
            with self.assertRaises((ValueError, zlib.error)):
                bridge.decode_container(raw)
        with self.assertRaises(ValueError):
            bridge.decode_container(self.container(b'x', declared=bridge.LIMIT + 1))

    def test_world_prefix_uses_action_size_and_keeps_tick_unsigned(self):
        payload = bytearray(8 + 3 + 64)
        struct.pack_into('<II', payload, 0, 1, 3)
        struct.pack_into('<I', payload, 11, 2)
        struct.pack_into('<H', payload, 21, 41)
        struct.pack_into('<I', payload, 25, 0xffffffff)
        struct.pack_into('<i', payload, 33, -3)
        result = bridge.world_prefix(payload)
        self.assertEqual(result['world_prefix_offset'], 11)
        self.assertEqual(result['bank_id'], {'offset': 21, 'value': 41})
        self.assertEqual(result['game_tick'], {'offset': 25, 'value': 0xffffffff})
        self.assertEqual(result['park_closed_word']['value'], -3)
        for raw in [payload[:-1], struct.pack('<II', 0, 0xffffffff), struct.pack('<II', 2, 0) + bytes(64)]:
            with self.assertRaises(ValueError):
                bridge.world_prefix(raw)

    def test_calendar_candidates_keep_caches_and_do_not_normalize(self):
        calendar = struct.pack('<QQiiI', bridge.FUNNY_EPOCH, 17, -5, 44, bridge.RATE)
        payload = b'abc' + calendar
        self.assertEqual(bridge.calendar_candidates(payload), [bridge.calendar_at(payload, 3)])
        self.assertEqual(bridge.calendar_at(payload, 3)['month_cache'], -5)
        self.assertEqual(bridge.calendar_at(payload, 3)['day_cache'], 44)
        self.assertEqual(len(bridge.calendar_candidates(payload + calendar)), 2)
        self.assertEqual(bridge.calendar_candidates(payload[:-1]), [])
        changed_rate = calendar[:-4] + struct.pack('<I', bridge.RATE + 1)
        self.assertEqual(bridge.calendar_candidates(changed_rate), [])
        for offset in [-1, 4]:
            with self.assertRaises(ValueError):
                bridge.calendar_at(payload, offset)

    def test_unknown_file_identity_fails_before_decompression(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'synthetic.TPWI'
            path.write_bytes(self.container(b'well formed but unidentified'))
            with self.assertRaisesRegex(ValueError, 'identified PC save'):
                bridge.inspect_save(path)


if __name__ == '__main__':
    unittest.main()
