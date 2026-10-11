"""Corpus-invariant tests: synthetic members exercise each relation and its failure; the private
corpus runs only with OPENTPW_PC_DATA (the PC `Data` directory)."""
import os
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import anim_corpus as ac  # noqa: E402

MESH = 0xB8  # one 160-byte mesh record right after the header


def geometry(positions=4, corners=6, node_word=0x1):
    data = bytearray(MESH + 160)
    struct.pack_into('<3I', data, 0, ac.MAGIC, 221, 203)
    struct.pack_into('<H', data, 0x44, 1)
    struct.pack_into('<I', data, 0x70, MESH)
    struct.pack_into('<I', data, 0x78, MESH)
    struct.pack_into('<I', data, MESH, node_word)
    struct.pack_into('<I', data, MESH + 80, 0)  # node index 0
    struct.pack_into('<H', data, MESH + 88, positions)
    struct.pack_into('<H', data, MESH + 94, corners)
    return ac.Member('x.wad', 'thing.MD2', bytes(data))


def animation(flags, clip_ticks=20, corners=6, tracks=None, count12=4, index12=None):
    """A clip with one record on node 0: a UV block (+44) and/or a 12-byte block (+40)."""
    tracks = tracks or [[0, 10, 20]] * corners
    data = bytearray(0x400)
    struct.pack_into('<3I', data, 0, ac.MAGIC, 221, 203)
    trailer, table = 0x100, 0x150
    struct.pack_into('<I', data, 0x98, trailer)
    struct.pack_into('<I', data, trailer + 8, clip_ticks)
    struct.pack_into('<H', data, trailer + 18, 1)
    struct.pack_into('<I', data, trailer + 44, table)
    struct.pack_into('<I', data, table + 4, flags)
    struct.pack_into('<H', data, table + 20, 0)
    uv, pairs, ticks = 0x1a0, 0x1c0, 0x200
    keys_total = sum(len(t) for t in tracks)
    keys = ticks + 2 * keys_total + (2 * keys_total) % 4
    struct.pack_into('<5I', data, uv, len(tracks), pairs, keys_total, ticks, keys)
    first = 0
    for corner, track in enumerate(tracks):
        struct.pack_into('<2H', data, pairs + 4 * corner, first, len(track))
        for k, tick in enumerate(track):
            struct.pack_into('<H', data, ticks + 2 * (first + k), tick)
        first += len(track)
    data += bytes(8 * keys_total)
    struct.pack_into('<I', data, table + 44, uv)
    block12, indices = 0x300, 0x310
    floats = indices + 4 * count12
    struct.pack_into('<3I', data, block12, indices, count12, floats)
    for i in range(count12):
        struct.pack_into('<I', data, indices + 4 * i, (index12 or list(range(count12)))[i])
    data += bytes(12 * count12)
    struct.pack_into('<I', data, table + 40, block12)
    return ac.Member('x.wad', 'thingm1.MD2', bytes(data))


def violations(check):
    report = ac.Report()
    check(report)
    return set(report.violations), report


class SyntheticInvariantTests(unittest.TestCase):
    def uv(self, clip, base=None):
        return violations(lambda r: ac.check_uv(r, clip, next(clip.records()), base or geometry(), 'synthetic'))

    def b12(self, clip, flags, base=None):
        return violations(lambda r: ac.check_block12(r, clip, next(clip.records()), flags, base or geometry(),
                                                     'synthetic'))

    def test_uv_relations_hold(self):
        found, report = self.uv(animation(0x10000))
        self.assertEqual(found, set())
        self.assertEqual(report.counts['uv.corner_tracks'], 6)

    def test_uv_relations_fail(self):
        self.assertIn('uv.corner_count_equals_mesh_corners', self.uv(animation(0x10000, corners=5))[0])
        self.assertIn('uv.track_reaches_clip_end', self.uv(animation(0x10000, clip_ticks=21))[0])
        self.assertIn('uv.track_starts_at_tick_0', self.uv(animation(0x10000, tracks=[[1, 20]] * 6))[0])
        self.assertIn('uv.ticks_strictly_increase', self.uv(animation(0x10000, tracks=[[0, 20, 20]] * 6))[0])
        self.assertIn('uv.track_has_two_keys', self.uv(animation(0x10000, tracks=[[20]] * 6))[0])

    def test_block12_relations(self):
        self.assertEqual(self.b12(animation(0x5000), 0x5000)[0], set())
        self.assertIn('block12.count_equals_mesh_positions', self.b12(animation(0x5000, count12=3), 0x5000)[0])
        self.assertIn('block12.indices_in_range',
                      self.b12(animation(0x5000, index12=[0, 1, 2, 4]), 0x5000)[0])
        self.assertIn('block12.has_vertex_flag_0x1000', self.b12(animation(0x4000), 0x4000)[0])

    def test_record_bit_sets_partition_the_word(self):
        self.assertEqual(ac.KNOWN_MASK, 0x3ffff & ~0x6)  # bits 0x2 and 0x4 are never set or read
        self.assertFalse(ac.ADVISOR_ALLOWED_MASK & 0x4000)
        self.assertFalse(ac.ADVISOR_ALLOWED_MASK & 0x600)

    def test_pairing_prefers_longest_prefix_in_same_directory(self):
        a = ac.Member('w.wad', 'spider.MD2', b'')
        b = ac.Member('w.wad', 'spiderb.MD2', b'')
        c = ac.Member('other.wad', 'spiderb1.MD2', b'')
        self.assertIs(ac.pair(ac.Member('w.wad', 'spiderb1.MD2', b''), [a, b, c]), b)
        self.assertIsNone(ac.pair(ac.Member('w.wad', 'spider.MD2', b''), [a]))


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'OPENTPW_PC_DATA (PC Data directory) not set')
class PrivateCorpusTests(unittest.TestCase):
    def test_invariants_hold_over_the_corpus(self):
        result = ac.run(Path(os.environ['OPENTPW_PC_DATA']))
        self.assertEqual(result['violations'], {})
        counts = result['counts']
        # Relations between the counts, not literal values.
        self.assertEqual(counts['member.current'] + counts['member.other_version'], counts['member.magic.checked'])
        self.assertEqual(counts['other_version.below_221.checked'], counts['member.other_version'])
        self.assertGreater(counts['other_version.loose'], 0)
        self.assertEqual(counts['records'], counts['record.bits_known.checked'])
        self.assertLessEqual(counts['uv.paired_blocks'] + counts.get('uv.unpaired_or_not_mesh', 0),
                             counts['records.with_bit_0x10000'])
        self.assertEqual(counts['block12.paired_records'] + counts.get('block12.unpaired_or_not_mesh', 0),
                         counts['records.with_bit_0x4000'])
        self.assertEqual(counts['records.with_bit_0x100'], counts['records.with_bit_0x80'])
        self.assertGreater(counts['advisor.global_clips_use_decoded_kinds_only.checked'], 0)


if __name__ == '__main__':
    unittest.main()
