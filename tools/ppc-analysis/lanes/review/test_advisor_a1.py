"""Advisor lane review A1: ppc-advisor a479cff..526e2bf (phases 6-10).

- Native (OPENTPW_PPC_BIN_ROOT, identified Feral Mac bin): the CMsgEvent switch, the
  advice -> response lookup, the sound seed's writers and readers, the event/sample
  chooser compares and the bank-ordinal fixup. These are decoded independently from
  instruction words and relocations. Nothing is executed and no bytes are written out.
- Response lookup: native 0x6b7c searches the 0x18ff4 table by stored word 0 (stride 32,
  sentinel 9999), not by row position. From row 393 on, 216 rows have an ID that differs
  from their position. For advice 323 this means speech 606 by ID but 638 by position.
- Git (OPENTPW_REVIEW_REPO, a checkout holding ba70ea5 and 526e2bf): the merge-tree
  conflicts, the net8 asset tool against net10 OpenTPW.Files, and the production files
  already on integration.

No claim is made about the PC build or the original runtime order of draws.
"""
from __future__ import annotations

import hashlib
import os
import struct
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
SOUND_SHA = '7132c2f1d772de25b458b9c6e0303e130e6c536d7650a9d2cde5c8cacd6bb94f'
TOC = 0x8000
SEED = 0xc2e4


def s16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


def lookup_by_id(rows, response_id):
    """Native 0x6b7c model: linear search on word 0 until the 9999 sentinel."""
    for row in rows:
        if row[0] == 9999:
            return None
        if row[0] == response_id:
            return row
    return None


class ResponseLookupModel(unittest.TestCase):
    def test_id_lookup_differs_from_position_once_ids_skip(self):
        rows = [(0, 10), (1, 11), (3, 13), (2, 12), (9999, 0)]
        self.assertEqual((2, 12), lookup_by_id(rows, 2))
        self.assertEqual((3, 13), rows[2])
        self.assertIsNone(lookup_by_id(rows, 4))


def load(name: str, digest: str) -> pef.PEFContainer:
    path = Path(os.environ['OPENTPW_PPC_BIN_ROOT']) / name
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != digest:
        raise unittest.SkipTest(f'{name} is not the identified container')
    return pef.load(str(path))


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class NativeAdvisor(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = load('SimThemePark.data', APP_SHA)

    def word(self, at):
        return struct.unpack_from('>I', self.app.code.data, at)[0]

    def toc_target(self, slot_disp):
        target = self.app.relocs[1][TOC + slot_disp]
        self.assertEqual(('section', 1), (target.kind, target.target))
        return target.addend

    def call(self, at):
        word = self.word(at)
        self.assertEqual((18, 1), (word >> 26, word & 3))
        offset = word & 0x3fffffc
        return at + (offset - 0x4000000 if offset & 0x2000000 else offset)

    def test_cmsgevent_switch_routes(self):
        table = self.toc_target(-23724)
        self.assertEqual(0x1e0f4, table)
        self.assertEqual(0x2804000a, self.word(0x94e4))
        targets = [self.app.relocs[1][table + 4 * i].addend for i in range(11)]
        self.assertEqual([0x9524, 0x9e84, 0x98b0, 0x9a64, 0x9c18] + [0x9e84] * 5 + [0x9dcc], targets)
        for literal, call, advice in [(0x9530, 0x9548, 0), (0x98bc, 0x98d4, 106), (0x9a70, 0x9a88, 128),
                                      (0x9c24, 0x9c3c, 129), (0x9708, 0x9720, 323)]:
            self.assertEqual((14, 4, 0, advice), (self.word(literal) >> 26, self.word(literal) >> 21 & 31,
                                                  self.word(literal) >> 16 & 31, self.word(literal) & 0xffff))
            self.assertEqual(0xb6d8, self.call(call))
        self.assertEqual(0x2c000002, self.word(0x96f4))  # advice 323 only when mode word == 2

    def test_response_lookup_is_by_stored_id(self):
        self.assertEqual(0x18ff4, self.toc_target(-30172))
        self.assertEqual(0x82228a24, self.word(0x6b94))  # r17 = response table
        self.assertEqual(0x80040000, self.word(0x6bfc))  # lwz r0,0(r4)
        self.assertEqual(0x7c100000, self.word(0x6c00))  # cmp r16(requested ID), r0
        self.assertEqual(0x38840020, self.word(0x6c0c))  # stride 32
        self.assertEqual(0x2c00270f, self.word(0x6c18))  # sentinel 9999
        self.assertEqual(0x6b7c, self.call(0xb958))      # called with first + variant
        self.assertEqual(0xd468, self.call(0xb944))
        self.assertEqual(0x80650020, self.word(0xd5d8))  # descriptor +32 = first response ID
        self.assertEqual(0x38600266, self.word(0xd5d0))  # 614 when the descriptor is missing

    def test_advice_response_speech_rows(self):
        data = self.app.data_section.data
        rows = [struct.unpack_from('>8i', data, 0x18ff4 + 32 * i) for i in range(610)]
        moved = [i for i, row in enumerate(rows) if row[0] != i]
        self.assertEqual((216, 393), (len(moved), moved[0]))
        descriptors = self.toc_target(-30072)
        by_id = {}
        for i in range(351):
            record = struct.unpack_from('>12i', data, descriptors + 48 * i)
            by_id[record[1]] = record
        expected = {0: [(1, 1, 1)], 106: [(274, 424, 424), (275, 425, 425)],
                    128: [(308, 342, 342), (309, 343, 343)], 129: [(310, 344, 344), (311, 345, 345)],
                    323: [(587, 606, 606)]}
        for advice, triples in expected.items():
            first, count = by_id[advice][8:10]
            got = [lookup_by_id(rows, first + v)[:3] for v in range(count)]
            self.assertEqual(triples, got, advice)
        first = by_id[323][8]
        self.assertEqual((590, 638, 0), rows[first][:3])  # positional indexing would be wrong


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class NativeSound(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.c = load('libraries/sound_shared.data', SOUND_SHA)
        cls.code = cls.c.code.data

    def word(self, at):
        return struct.unpack_from('>I', self.code, at)[0]

    def test_seed_sites_writer_and_neighbours(self):
        sites, writes = [], []
        for at in range(0, len(self.code), 4):
            w = self.word(at)
            op, ra = w >> 26, w >> 16 & 31
            if ra == 2 and (op in (14, 15) or 32 <= op < 56) and TOC + s16(w & 0xffff) in (SEED, 0x348):
                sites.append(at)
        self.assertEqual([0xf404, 0xf548, 0xf764, 0xf988, 0xfce0, 0xff80, 0x118c8, 0x180f8, 0x1939c], sites)
        for site in sites:
            reg = self.word(site) >> 21 & 31
            for at in range(site + 4, site + 64, 4):
                w = self.word(at)
                op = w >> 26
                if op in (36, 37, 38, 39, 44, 45, 47) and w >> 16 & 31 == reg:
                    writes.append(at)
                if op in (16, 18, 19):
                    break
                if 14 <= op <= 15 or 32 <= op <= 35 or op in (21, 31):
                    dest = (w >> 16 & 31) if op in (21,) else (w >> 21 & 31)
                    if dest == reg and at != site:
                        break
        self.assertEqual([0x118cc], writes)
        calls = [at for at in range(0, len(self.code), 4)
                 if self.word(at) >> 26 == 18 and self.word(at) & 1 and self._target(at) == 0x118b4]
        self.assertEqual([0x3c], calls)
        exports = sorted(e.value for e in self.c.exports if e.section == 1 and 0xc2d0 <= e.value <= 0xc2f0)
        self.assertEqual([0xc2d0, 0xc2d4, 0xc2d8, 0xc2e0, 0xc2e4, 0xc2e8], exports)

    def _target(self, at):
        offset = self.word(at) & 0x3fffffc
        return at + (offset - 0x4000000 if offset & 0x2000000 else offset)

    def test_lcg_and_chooser_compares(self):
        self.assertEqual([0x3c600019, 0x3803660d], [self.word(0xff84), self.word(0xff8c)])
        self.assertEqual([0x3c633c6f, 0x3803f35f, 0x5403843e], [self.word(a) for a in (0xffa0, 0xffa4, 0xffa8)])
        self.assertEqual(0x19660d, 25 << 16 | 0x660d)
        self.assertEqual(1013904223, (0x3c6f << 16) + s16(0xf35f))
        # Event and sample: continue while running sum/threshold < draw, so first >= draw is taken.
        self.assertEqual([0x7c081840, 0x41800164], [self.word(0xffc0), self.word(0xffc4)])
        self.assertEqual([0x7c002840, 0x4180007c], [self.word(0xfd14), self.word(0xfd18)])
        # Anti-repeat only above two choices; history bytes +80 / +81.
        self.assertEqual([0x28050002, 0x4081004c, 0x881f0050], [self.word(a) for a in (0x10004, 0x10008, 0x10010)])
        self.assertEqual([0x28080002, 0x40810048, 0x88030051], [self.word(a) for a in (0xfd38, 0xfd3c, 0xfd44)])

    def test_cumulative_weights_and_bank_fixup(self):
        self.assertEqual([0x7cc03378, 0x80c7001e, 0x7c003050, 0x9007001e],
                         [self.word(a) for a in (0x16654, 0x16658, 0x1665c, 0x16660)])
        self.assertEqual([0x381cffff, 0x807f002c, 0x5400103a, 0x7c03002e, 0xb019000c],
                         [self.word(a) for a in range(0x16af4, 0x16b08, 4)])
        self.assertEqual(0x3804ffff, self.word(0x6fa4))


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class AdvisorRangeGit(unittest.TestCase):
    def git(self, *args, check=True):
        result = subprocess.run(['git', '-C', os.environ['OPENTPW_REVIEW_REPO'], *args],
                                capture_output=True, text=True, check=False)
        if check and result.returncode:
            raise unittest.SkipTest(result.stderr.strip() or 'git object missing')
        return result

    def setUp(self):
        for rev in ('a479cff', '526e2bf', 'ba70ea5'):
            self.git('cat-file', '-e', rev + '^{commit}')

    def test_merge_tree_conflicts(self):
        result = self.git('merge-tree', '--write-tree', '--name-only', 'ba70ea5', '526e2bf', check=False)
        self.assertEqual(1, result.returncode)
        files = result.stdout.split('\n\n', 1)[0].splitlines()[1:]
        self.assertEqual(['docs/reverse/PPC-advisor.md', 'source/OpenTPW.Files/Formats/Sound/MP2File.cs'], files)

    def test_asset_tool_framework_against_integration(self):
        tool = self.git('show', '526e2bf:tools/ppc-analysis/lanes/advisor/audio-events/AudioEventAssets.csproj').stdout
        files = self.git('show', 'ba70ea5:source/OpenTPW.Files/OpenTPW.Files.csproj').stdout
        self.assertIn('<TargetFramework>net8.0</TargetFramework>', tool)
        self.assertIn('OpenTPW.Files.csproj', tool)
        self.assertIn('<TargetFramework>net10.0</TargetFramework>', files)

    def test_production_files_and_scope(self):
        changed = self.git('diff', '--name-only', 'a479cff..526e2bf').stdout.split()
        outside = [p for p in changed if not p.startswith(('tools/ppc-analysis/lanes/advisor/', 'docs/reverse/PPC-advisor.md'))]
        self.assertEqual(['source/OpenTPW.Files/Formats/Sound/MP2File.cs',
                          'source/OpenTPW.Files/Formats/Sound/Mp2Decoder.cs',
                          'source/OpenTPW.Tests/MP2FileMetadataTests.cs'], outside)
        only_2b8 = self.git('diff', '--name-only', '2b8e4bf..526e2bf', '--', 'source').stdout.split()
        self.assertEqual([], only_2b8)
        same = self.git('diff', '--name-only', '526e2bf', 'ba70ea5', '--',
                        'source/OpenTPW.Files/Formats/Sound/Mp2Decoder.cs',
                        'source/OpenTPW.Tests/MP2FileMetadataTests.cs').stdout.split()
        self.assertEqual([], same)


if __name__ == '__main__':
    unittest.main()
