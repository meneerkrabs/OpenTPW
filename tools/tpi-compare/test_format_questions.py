from pathlib import Path
import struct
import tempfile
import unittest

import format_questions as metadata


class FormatQuestionTests(unittest.TestCase):
    def test_cos_groups_by_proven_body_boundary_without_decoding_name(self):
        with tempfile.TemporaryDirectory() as directory:
            paths = []
            for language, marker in [('one', 1), ('two', 2)]:
                path = Path(directory) / language / 'synthetic.cos'
                path.parent.mkdir()
                data = bytearray(140)
                struct.pack_into('<I', data, 0, 2)
                data[4] = marker
                path.write_bytes(data)
                paths.append(path)
            report = metadata.cos_profile(paths)
            self.assertEqual(report['languageIndependentBodyGroups'], 1)
            self.assertEqual(report['groupSizes'], {2: 1})

    def test_sdt_candidate_requires_bounds_and_name_metadata(self):
        data = bytearray(48)
        struct.pack_into('<II', data, 0, 40, 8)
        data[8:13] = b'item0'
        candidate = metadata.header_candidate(data, 0)
        self.assertEqual(candidate['end'], 48)
        self.assertNotIn('name', candidate)
        self.assertIsNone(metadata.header_candidate(data, 1))
        struct.pack_into('<I', data, 4, 9)
        self.assertIsNone(metadata.header_candidate(data, 0))

    def test_complete_audio_blocks_are_compared_by_hash_without_decoding(self):
        with tempfile.TemporaryDirectory() as directory:
            speech = Path(directory) / 'speech.sdt'
            music = Path(directory) / 'music.sdt'
            block = bytearray(48)
            struct.pack_into('<II', block, 0, 40, 8)
            block[8:13] = b'item0'
            data = struct.pack('<II', 1, 8) + block
            speech.write_bytes(data)
            music.write_bytes(data)
            candidate = metadata.header_candidate(data, 8)
            result = metadata.speech_overlap(music, speech, [candidate])
            self.assertEqual(result['completeBlockMatches'], 1)
            self.assertEqual(result['exactHeaderMatches'], 1)


if __name__ == '__main__':
    unittest.main()
