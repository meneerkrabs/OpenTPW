"""Synthetic tests for the code-derived format models; no original data is used."""
import random
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import models  # noqa: E402
from ppcfields import rlwimi, rlwinm  # noqa: E402


def loader_converts(little_endian_word: int) -> int:
    """engine_shared 0x41cac applied to one packed word (field rotations read by the witness)."""
    b0, b1, b2, b3 = little_endian_word.to_bytes(4, 'little')
    big = int.from_bytes(bytes((b0, b1, b2, b3)), 'big')  # bytes as they sit in memory
    r5 = rlwimi(b0, b1, 8, 22, 23)
    half0 = rlwimi(big >> 16, r5, 6, 16, 25) & 0xffff
    big = (half0 << 16) | (big & 0xffff)
    r0 = rlwimi(rlwinm(b2, 6, 22, 25), b1, 30, 26, 31) & 0xffff
    big = rlwimi(big, r0, 12, 10, 19)
    r5 = rlwimi(rlwinm(b3, 4, 22, 27), b2, 28, 28, 31) & 0xffff
    half1 = rlwimi(big & 0xffff, r5, 2, 20, 29) & 0xffff
    big = (big & 0xffff0000) | half1
    return rlwimi(big, 0, 0, 30, 31)


def sampler_extracts(big: int) -> tuple[int, int, int]:
    """SimThemePark 0xa43a8: rotate, mask the top 11 bits, then arithmetic shift by 22."""
    def arithmetic(value):
        value -= 1 << 32 if value & 0x80000000 else 0
        return value >> 22

    def sign_half(value):
        return value | 0xffff0000 if value & 0x8000 else value

    x = arithmetic(rlwinm(sign_half(big >> 16), 16, 0, 10))
    y = arithmetic(rlwinm(big, 10, 0, 10))
    z = arithmetic(rlwinm(sign_half(big & 0xffff), 20, 0, 10))
    return x, y, z


class VertexPackingTests(unittest.TestCase):
    def test_loader_conversion_and_sampler_match_little_endian_signed_fields(self):
        rng = random.Random(7)
        words = [0, 0xffffffff, 0x3ff, 0x200, 0x1ff, 0x000ffc00, 0x3ff00000, 0xc0000000] + \
            [rng.getrandbits(32) for _ in range(2000)]
        for value in words:
            with self.subTest(word=hex(value)):
                self.assertEqual(sampler_extracts(loader_converts(value)), models.unpack_vertex_key(value))

    def test_signed_ranges_and_dequantisation(self):
        self.assertEqual(models.unpack_vertex_key(0x200 | (0x1ff << 10) | (0x3ff << 20)), (-512, 511, -1))
        self.assertEqual(models.vertex_key_value((2, -1, 0), (1.0, 2.0, 3.0), (0.5, 0.25, 1.0)), (2.0, 1.75, 3.0))
        self.assertEqual(models.lerp((0, 0, 0), (2, 4, 6), 0.5), (1.0, 2.0, 3.0))
        self.assertEqual(models.group0_vectors((1, 1, 1), (1, 1, 1), (2, 3, 4), (4, 3, 2), 0.5, (0.5, 0.5, 0.5)),
                         ((0.25, 0.25, 0.25), (3.75, 3.75, 3.75)))

    def test_vertex_cursor_stays_on_the_earlier_segment_at_a_key(self):
        self.assertEqual(models.vertex_cursor([0, 4, 10], 2), (0, 0.5))
        self.assertEqual(models.vertex_cursor([0, 4, 10], 4), (0, 1.0))
        self.assertEqual(models.vertex_cursor([0, 4, 10], 7), (1, 0.5))
        self.assertEqual(models.vertex_cursor([0, 4, 10], 7, cursor=1), (1, 0.5))
        self.assertEqual(models.vertex_cursor([2, 4], 1), (0, -0.5))  # before the first key: extrapolates
        with self.assertRaises(ValueError):
            models.vertex_cursor([0, 4, 10], 10.5)

    def test_texture_frames_and_toggles_scan_backwards(self):
        keys = [(2, 3), (5, 0), (5, 1)]
        self.assertEqual([models.texture_frame_at(keys, t) for t in (1.9, 2, 4.99, 5)], [None, 3, 3, 1])
        self.assertEqual([models.toggle_state([0, 3, -7], t) for t in (0, 2.9, 3, 7)], [True, True, False, True])
        self.assertIsNone(models.toggle_state([2, 3], 1))
        self.assertIsNone(models.toggle_state([-32768], 40000))


class LoaderTests(unittest.TestCase):
    def test_version_gates(self):
        cases = [((221, 203, False, 0), ('Ok', True)), ((221, 203, True, 2), ('Ok', True)),
                 ((222, 203, False, 0), ('OldCode', False)), ((207, 201, False, 0), ('DeadMesh', False)),
                 ((207, 201, True, 2), ('DeadMesh', False)), ((207, 201, False, 1), ('OldMesh', True)),
                 ((221, 203, True, 0), ('Master is Anim', False)), ((221, 203, False, 2), ('Anim is Master', False)),
                 ((221, 204, True, 2), ('OldCode', True)), ((221, 201, True, 2), ('DeadAnim', True))]
        for arguments, expected in cases:
            with self.subTest(arguments=arguments):
                self.assertEqual(models.load_status(*arguments), expected)
        self.assertEqual(models.load_status(221, 203, False, 0, magic=0), ('BadFileType', False))


class SamplingTests(unittest.TestCase):
    def test_key_search(self):
        ticks = [10, 20, 40]
        self.assertIsNone(models.find_key(ticks, 9.9, True))
        self.assertEqual(models.find_key(ticks, 30.0, False), (1, 2, 0.5))
        self.assertEqual(models.find_key(ticks, 20.7, False)[:2], (1, 2))
        self.assertEqual(models.find_key(ticks, 40.5, False), (2, 2, 0.5))
        last, following, fraction = models.find_key(ticks, 45.0, True)
        self.assertEqual((last, following), (2, 0))
        self.assertAlmostEqual(fraction, 5 / -30)
        self.assertEqual(models.rotation_pair(2, 3), (2, 2))
        self.assertEqual(models.rotation_pair(1, 3), (1, 2))

    def test_easing_curve(self):
        table = bytes((10, 20, 40, 80, 120, 160, 200, 240))
        self.assertEqual(models.ease(table, 0.0), 0.0)
        self.assertAlmostEqual(models.ease(table, 1 / models.EASING_SCALE), table[0] / 255.0)
        self.assertAlmostEqual(models.ease(table, 4.5 / models.EASING_SCALE), (80 + 120) / 2 / 255.0)
        end = models.ease(table, 1.0)
        self.assertGreater(end, 0.999)
        self.assertLess(end, 1.0)
        with self.assertRaises(ValueError):
            models.ease(b'\0' * 7, 0.5)

    def test_bezier_segments_and_wrap(self):
        points = [(float(i), 0.0, 0.0) for i in range(7)]
        self.assertEqual(models.bezier(points, 1, 0.0), (0.0, 0.0, 0.0))
        self.assertEqual(models.bezier(points, 4, 0.0), (3.0, 0.0, 0.0))
        self.assertAlmostEqual(models.bezier(points, 1, 0.5)[0], 1.5)
        self.assertEqual(models.bezier(points, 1, 1.0), (3.0, 0.0, 0.0))
        self.assertEqual(models.bezier(points, 7, 0.0), (6.0, 0.0, 0.0))  # wraps modulo the point count

    def test_clock(self):
        self.assertEqual(models.animation_time(1000, 1.0), 30.0)
        self.assertEqual(models.animation_time(500, 2.0), 30.0)
        self.assertAlmostEqual(models.remaining_ms(30.0, 0.0), 1000.0, places=4)


class FontBlendTests(unittest.TestCase):
    def test_non_negative_difference_is_floor_division(self):
        for destination in range(0, 256, 17):
            for colour in range(destination, 256, 15):
                for coverage in range(16):
                    self.assertEqual(models.bf4_blend_channel(destination, colour, coverage),
                                     destination + coverage * (colour - destination) // 15)

    def test_negative_difference_lags_one_step(self):
        self.assertEqual(models.bf4_blend_channel(255, 0, 15), 17)
        self.assertEqual(models.bf4_blend_channel(255, 0, 1), 255)
        self.assertEqual(models.bf4_blend_channel(255, 0, 0), 255)
        self.assertEqual(models.bf4_blend_channel(0, 255, 15, alpha=128), 119)
        with self.assertRaises(ValueError):
            models.bf4_blend_channel(0, 0, 16)


class SaveTests(unittest.TestCase):
    def test_trailing_delimiters(self):
        payload = bytearray()
        for index, (_, tag) in enumerate(models.SECTION_ORDER):
            payload += bytes([index]) * index + tag
        payload += b'\0' * 4
        blocks, tail = models.split_sections(bytes(payload))
        self.assertEqual([end - start for _, _, start, end in blocks], list(range(len(models.SECTION_ORDER))))
        self.assertEqual(blocks[0][:2], ('World', 'DLRW'))
        self.assertEqual(tail[1] - tail[0], 4)
        with self.assertRaises(ValueError):
            models.split_sections(b'DLRW')

    def test_world_prefix_and_vars(self):
        record = b'abc'
        world = b''.join(struct.pack('<I' if width == 4 else '<H', i) for i, (_, width) in
                         enumerate(models.WORLD_VAR_FIELDS))
        payload = struct.pack('<II', 1, len(record)) + record + world
        head, offset, values, end = models.parse_world_vars(payload)
        self.assertEqual(head, {'mLoadedPublishedPark': 1, 'recording_size': 3})
        self.assertEqual((offset, end), (11, len(payload)))
        self.assertEqual(values['mGameTick'], 6)
        with self.assertRaises(ValueError):
            models.parse_world_vars(payload[:-1])

    def test_cell_record_sizes(self):
        self.assertEqual(models.layout_size(models.CELL_BASE_FIELDS), 29)
        self.assertEqual(1 + models.layout_size(models.MAP_CELL_FIELDS) + models.layout_size(models.TRACK_CELL_FIELDS), 84)
        self.assertEqual(models.layout_size(models.REGION_EFFECT_FIELDS), 10)
        record = bytearray(94)
        record[0] = 7
        record[1 + 45] = 0x91  # map mStatusFlags
        record[82:84] = b'\xff\xff'  # track mSegmentNumber (record +82)
        cell, end = models.parse_cell(bytes(record), 0)
        self.assertEqual(end, 94)
        self.assertEqual(cell['map']['mStatusFlags'], 0x91)
        self.assertEqual(cell['track']['mSegmentNumber'], 0xffff)
        self.assertEqual(models.parse_cell(bytes([0]), 0)[1], 1)
        with self.assertRaises(ValueError):
            models.parse_cell(bytes([8]), 0)
        with self.assertRaises(ValueError):
            models.parse_cell(bytes([1]) + b'\0' * 10, 0)


class MapTests(unittest.TestCase):
    def test_row_is_x_and_column_is_y(self):
        self.assertEqual(models.map_chunk_cell(5, 3), 3 * 128 + 5)
        self.assertIsNone(models.map_chunk_cell(128, 0))
        self.assertIsNone(models.map_chunk_cell(2, 1, origin_x=3))
        self.assertEqual(models.map_chunk_cell(4, 1, origin_x=3, origin_y=1), 1)


if __name__ == '__main__':
    unittest.main()
