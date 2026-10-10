"""Scenarios lane review S1: 9e47f37, cb8dca2, db3ae4f operands and the ffa87c6 merge onto ae886cf.

- With OPENTPW_PPC_BIN_ROOT naming the identified Feral Mac bin directory, one sampled operand chain per
  unreviewed commit is decoded from the instruction words with a separate field split (no llvm-mc):
  the research point sink (lane claim behind dropping ECON-019 from the runtime), the Instant Action
  easymode copy gate (9e47f37), the gms.dat version write and unsigned read gate (cb8dca2), and the
  Keys() formula and the lobby door compare (db3ae4f).
- With OPENTPW_REVIEW_REPO naming a checkout that holds ae886cf and ffa87c6, `git merge-tree` is run
  read-only. It pins the 13 conflicted paths, the auto-merged `includeEasymodePark` line that does not
  compile against the lane's OriginalPark.Load signature, and the two production behaviours main and the
  lane disagree on (staffless Instant Action research, the park music hook inside a conflict hunk).

Nothing is executed from the original. Nothing here says anything about the PC build or PC behaviour.
"""
from __future__ import annotations

import hashlib
import os
import re
import struct
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
MAIN, LANE = 'ae886cf', 'ffa87c6'


def fields(word: int) -> tuple[int, int, int, int]:
    """(primary opcode, rD/rS/BO, rA/BI, signed 16-bit immediate)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, struct.unpack('>h', struct.pack('>H', word & 0xffff))[0]


def xo(word: int) -> int:
    return (word >> 1) & 0x3ff


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class MacOperands(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(os.environ['OPENTPW_PPC_BIN_ROOT']) / 'SimThemePark.data'
        if hashlib.sha256(path.read_bytes()).hexdigest() != APP_SHA256:
            raise unittest.SkipTest('not the identified Feral Mac SimThemePark.data')
        cls.container = pef.load(str(path))
        cls.code = bytes(cls.container.code.data)
        cls.callers: dict[int, list[int]] = {}
        for i in range(len(cls.code) // 4):
            word = struct.unpack_from('>I', cls.code, 4 * i)[0]
            if word >> 26 == 18 and word & 3 == 1:        # relative bl
                li = word & 0x03fffffc
                cls.callers.setdefault(4 * i + (li - 0x04000000 if li & 0x02000000 else li), []).append(4 * i)

    def w(self, at: int) -> int:
        return struct.unpack_from('>I', self.code, at)[0]

    def bl_target(self, at: int) -> int:
        word = self.w(at)
        self.assertEqual((18, 1), (word >> 26, word & 3), f'bl at {at:#x}')
        li = word & 0x03fffffc
        return at + (li - 0x04000000 if li & 0x02000000 else li)

    def bc(self, at: int) -> tuple[int, int, int]:
        """(BO, BI, target) of a relative bc without link."""
        word = self.w(at)
        op, bo, bi, imm = fields(word)
        self.assertEqual((16, 0), (op, word & 3), f'bc at {at:#x}')
        return bo, bi, at + (imm & ~3)

    def test_research_points_come_only_from_the_researcher_cycle(self):
        # 0xf0df0 is the only point sink; its one direct caller sits in 0xf0728, called only from 0xf02e4.
        self.assertEqual([0xf0788], self.callers.get(0xf0df0))
        self.assertEqual([0xf02e4], self.callers.get(0xf0728))
        self.assertEqual(0xf0728, self.bl_target(0xf02e4))
        self.assertEqual((32, 0, 3, 484), fields(self.w(0xf0740)), 'lwz r0,484(r3): the researcher grade')
        self.assertEqual((7, 0, 0, 12), fields(self.w(0xf0748)), 'mulli r0,r0,12: 12 bytes per grade')
        self.assertEqual((32, 31, 5, 1052), fields(self.w(0xf0750)), 'lwz r31,1052(r5): ResearchAbility')
        self.assertEqual(0x7fe4fb78, self.w(0xf0784), 'mr r4,r31: the ability is the sink argument')
        relocated = [t.addend for m in self.container.relocs.values() for t in m.values()
                     if t.kind != 'import' and t.addend in (0xf0df0, 0xf0728)]
        self.assertEqual([], relocated, 'no descriptor or pointer names either routine (direct calls only)')

    def test_easymode_copy_runs_only_for_the_easy_flag(self):
        self.assertEqual(b'easymode\0', self.code[0x1d9a62:0x1d9a6b])
        self.assertEqual((32, 29, 2, -17488), fields(self.w(0x137608)), 'easymode string loaded in 0x137600')
        self.assertEqual((14, 28, 6, 0), fields(self.w(0x137438)), 'CreatePlayer keeps its r6 flag in r28')
        self.assertEqual((14, 5, 28, 0), fields(self.w(0x137540)))
        self.assertEqual(0x137600, self.bl_target(0x137548))
        self.assertEqual(0x7cb82b78, self.w(0x137614), 'mr r24,r5')
        self.assertEqual((11, 0, 24, 0), fields(self.w(0x1376b0)), 'cmpwi r24,0')
        bo, bi, target = self.bc(0x1376b4)
        self.assertEqual((12, 2), (bo, bi), 'branch if equal (flag zero)')
        copy = 0x1377b8
        self.assertGreater(target, copy, 'the zero-flag branch skips LbFile_Copy')
        self.assertEqual(0x1c62ac, self.bl_target(copy), 'LbFile_Copy import glue')

    def test_gms_version_written_as_12_and_read_unsigned(self):
        self.assertEqual((14, 0, 0, 12), fields(self.w(0x128fc0)), 'li r0,12 before the write')
        self.assertEqual((10, 0, 6, 12), fields(self.w(0x129108)), 'cmplwi r6,12 (unsigned)')
        bo, bi, target = self.bc(0x12910c)
        self.assertEqual((4, 0, 0x129128), (bo, bi, target), 'not less than 12 goes on to the members')
        self.assertEqual((14, 3, 0, 0), fields(self.w(0x129120)), 'below 12 returns 0')

    def test_keys_is_extra_keys_plus_a_third_of_earned_and_never_reads_spent(self):
        self.assertEqual((15, 3, 0, 0x5555), fields(self.w(0x128c6c)))
        self.assertEqual((14, 0, 3, 0x5556), fields(self.w(0x128c74)))
        self.assertEqual((31, 75), (self.w(0x128c7c) >> 26, xo(self.w(0x128c7c)) & 0x1ff), 'mulhw (signed)')
        self.assertEqual((32, 4, 29, 32), fields(self.w(0x128c78)), 'mExtraKeys at +32')
        self.assertEqual(0x4e800020, self.w(0x128ca4))
        loads_28 = [at for at in range(0x128b60, 0x128ca8, 4)
                    if fields(self.w(at))[0] in (32, 40, 42, 34) and fields(self.w(at))[2:] == (29, 28)]
        self.assertEqual([], loads_28, 'no load of mSpentTickets (+28) from the player record')

    def test_door_enters_iff_signed_cost_at_most_keys_after_the_gametype_bypass(self):
        self.assertEqual((11, 0, 0, 2), fields(self.w(0x096528)), 'cmpwi GameType,2')
        self.assertEqual((12, 2), self.bc(0x09652c)[:2], 'GameType 2 enters before any key read')
        self.assertEqual(0x128b60, self.bl_target(0x0965cc), 'Keys()')
        self.assertEqual(0x12a4c8, self.bl_target(0x0965d8), 'cost getter after Keys()')
        word = self.w(0x0965dc)
        self.assertEqual((31, 0, 3, 31, 0), (word >> 26, (word >> 21) & 31, (word >> 16) & 31, (word >> 11) & 31, xo(word)),
                         'cmpw cr0,r3(cost),r31(keys): signed')
        self.assertEqual((12, 1), self.bc(0x0965e0)[:2], 'cost > keys skips the entry')


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class MergeOntoMain(unittest.TestCase):
    CONFLICTS = sorted([
        'docs/ECONOMY.md', 'docs/FIDELITY-REGISTER.md', 'docs/UI.md',
        'source/OpenTPW.Tests/ParkEconomyTests.cs', 'source/OpenTPW/Client/GameFlow.cs',
        'source/OpenTPW/Economy/ParkEconomy.cs', 'source/OpenTPW/Economy/ParkEconomyRuntime.cs',
        'source/OpenTPW/Economy/ParkResearch.cs', 'source/OpenTPW/FrontEnd/FrontEndMenu.cs',
        'source/OpenTPW/FrontEnd/FrontEndSmokeTest.cs', 'source/OpenTPW/UI/Original/UiApproximations.cs',
        'source/OpenTPW/World/Level.cs', 'source/OpenTPW/World/Original/OriginalPark.cs'])

    @classmethod
    def setUpClass(cls):
        cls.repo = os.environ['OPENTPW_REVIEW_REPO']
        for rev in (MAIN, LANE):
            if subprocess.run(['git', '-C', cls.repo, 'cat-file', '-e', f'{rev}^{{commit}}'], capture_output=True).returncode:
                raise unittest.SkipTest(f'{rev} not in {cls.repo}')
        run = subprocess.run(['git', '-C', cls.repo, 'merge-tree', '--write-tree', '--name-only', MAIN, LANE],
                             capture_output=True, text=True)
        cls.exit = run.returncode
        lines = run.stdout.split('\n')
        cls.tree = lines[0]
        cls.conflicted = sorted(line for line in lines[1:lines.index('')] if line)

    def show(self, rev: str, path: str) -> str:
        return subprocess.run(['git', '-C', self.repo, 'show', f'{rev}:{path}'], check=True,
                              capture_output=True).stdout.decode('utf-8-sig')

    def test_thirteen_paths_conflict(self):
        self.assertEqual(1, self.exit)
        self.assertEqual(self.CONFLICTS, self.conflicted)

    def test_auto_merged_line_names_mains_removed_parameter(self):
        merged = self.show(self.tree, 'source/OpenTPW/World/Original/OriginalPark.cs')
        outside = re.sub(r'<<<<<<< .*?>>>>>>> [^\n]*\n', '', merged, flags=re.S)
        self.assertIn('bool readShippedSave = true', merged)
        self.assertIn('if ( savePath != null && includeEasymodePark )', outside,
                      'clean side of the merge still uses main\'s parameter; taking the lane signature breaks the build')

    def test_main_and_lane_disagree_on_staffless_instant_action_research(self):
        main = self.show(MAIN, 'source/OpenTPW/Economy/ParkEconomy.cs')
        lane = self.show(LANE, 'source/OpenTPW/Economy/ParkEconomy.cs')
        self.assertIn('abilities.Count == 0 && Mode == ParkGameMode.InstantAction', main)
        self.assertIn('automatic: false', lane)

    def test_park_music_hook_sits_in_a_conflict_hunk(self):
        merged = self.show(self.tree, 'source/OpenTPW/Client/GameFlow.cs')
        hunks = re.findall(r'<<<<<<< .*?>>>>>>> [^\n]*\n', merged, flags=re.S)
        self.assertTrue(any('GameAudio.EnterPark' in h and 'StartKind = level.Park?.Start.Kind' in h for h in hunks),
                        'taking either side alone drops the mixer hook or the start record')
        self.assertNotIn('GameAudio.EnterPark', self.show(LANE, 'source/OpenTPW/Client/GameFlow.cs'))

    def test_lane_changes_no_line_endings(self):
        names = subprocess.run(['git', '-C', self.repo, 'diff', '--name-only', f'{MAIN}...{LANE}', '--', '*.cs'],
                               check=True, capture_output=True, text=True).stdout.split()
        base = subprocess.run(['git', '-C', self.repo, 'merge-base', MAIN, LANE], check=True,
                              capture_output=True, text=True).stdout.strip()
        for name in names:
            after = subprocess.run(['git', '-C', self.repo, 'show', f'{LANE}:{name}'], check=True, capture_output=True).stdout
            before = subprocess.run(['git', '-C', self.repo, 'show', f'{base}:{name}'], capture_output=True).stdout
            self.assertEqual(before.count(b'\r\n') > 0, after.count(b'\r\n') > 0, name)
            if b'\r\n' in after:
                self.assertEqual(after.count(b'\n'), after.count(b'\r\n'), f'{name} mixed endings')


if __name__ == '__main__':
    unittest.main()
