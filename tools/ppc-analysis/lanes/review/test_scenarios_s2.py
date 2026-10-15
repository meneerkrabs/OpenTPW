"""Scenarios lane review S2: the 5224418 merge of ffa87c6 onto main efc090d (round 2 of section 57).

- With OPENTPW_PPC_BIN_ROOT naming the identified Feral Mac bin directory, the UI-041 claim is decoded from
  the instruction words with a separate field split (no llvm-mc): player selection 0x13781c sets GameType
  2 or 0 from the mEasyModeUser byte (+36, whose only direct setter calls sit in CreatePlayer), and the park
  loader 0x11acfc reaches SetGameType 0x12bbf4 through direct calls only via the GameType constructor
  0x12bb64, whose every direct call is behind a construct-once guard. Indirect calls are not followed.
- With OPENTPW_REVIEW_REPO naming a checkout that holds efc090d, ffa87c6, 5224418 and 2902c7d, the merge is
  checked against both parents: the S1-1 resolutions, the S1-2 stand-in, the optional save member, and that
  no assertion line of either parent's tests was dropped beyond the ported ones.

Nothing is executed from the original. Nothing here says anything about the PC build or PC behaviour.
"""
from __future__ import annotations

import bisect
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
MAIN, LANE, MERGE, HEAD = 'efc090d', 'ffa87c6', '5224418', '2902c7d'
SET_GAME_TYPE, GAME_TYPE_CTOR, PARK_LOADER, SELECT_PLAYER = 0x12bbf4, 0x12bb64, 0x11acfc, 0x13781c


def fields(word: int) -> tuple[int, int, int, int]:
    """(primary opcode, rD/rS/BO, rA/BI, signed 16-bit immediate)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, struct.unpack('>h', struct.pack('>H', word & 0xffff))[0]


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class GameTypeIsThePlayersNotTheParks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(os.environ['OPENTPW_PPC_BIN_ROOT']) / 'SimThemePark.data'
        if hashlib.sha256(path.read_bytes()).hexdigest() != APP_SHA256:
            raise unittest.SkipTest('not the identified Feral Mac SimThemePark.data')
        cls.code = bytes(pef.load(str(path)).code.data)
        cls.callers: dict[int, list[int]] = {}
        for at in range(0, len(cls.code), 4):
            word = struct.unpack_from('>I', cls.code, at)[0]
            if word >> 26 == 18 and word & 3 == 1:        # relative bl
                target = cls.target(at, word)
                if 0 <= target < len(cls.code):
                    cls.callers.setdefault(target, []).append(at)
        # A function is approximated as the span from one direct-call target to the next.
        cls.starts = sorted(cls.callers)

    @staticmethod
    def target(at: int, word: int) -> int:
        li = word & 0x03fffffc
        return at + (li - 0x04000000 if li & 0x02000000 else li)

    def w(self, at: int) -> int:
        return struct.unpack_from('>I', self.code, at)[0]

    def bl_target(self, at: int) -> int:
        word = self.w(at)
        self.assertEqual((18, 1), (word >> 26, word & 3), f'bl at {at:#x}')
        return self.target(at, word)

    def function_of(self, at: int) -> int:
        return self.starts[bisect.bisect_right(self.starts, at) - 1]

    def direct_closure(self, root: int) -> set[int]:
        seen, todo = set(), [root]
        while todo:
            start = todo.pop()
            if start in seen:
                continue
            seen.add(start)
            index = bisect.bisect_right(self.starts, start)
            end = self.starts[index] if index < len(self.starts) else len(self.code)
            for at in range(start, end, 4):
                word = self.w(at)
                if word >> 26 == 18 and word & 3 == 1 and 0 <= self.target(at, word) < len(self.code):
                    todo.append(self.target(at, word))
        return seen

    def test_selection_copies_the_easy_mode_byte_into_gametype(self):
        self.assertEqual((34, 3, 3, 36), fields(self.w(0x128f4c)), 'getter: lbz r3,36(r3)')
        self.assertEqual((38, 4, 3, 36), fields(self.w(0x128f54)), 'setter: stb r4,36(r3)')
        self.assertEqual([0x13759c, 0x1375cc], self.callers[0x128f54], 'mEasyModeUser is set only in CreatePlayer')
        self.assertTrue(all(0x13741c <= at < 0x137600 for at in self.callers[0x128f54]))
        self.assertEqual((11, 0, 0, 1), fields(self.w(0x137954)), 'cmpwi GameType,1')
        word = self.w(0x137958)
        self.assertEqual((16, 12, 2), fields(word)[:3], 'online (1) skips the override')
        self.assertEqual(0x128f4c, self.bl_target(0x13798c), 'reads mEasyModeUser')
        self.assertEqual((21, 3, 0), (self.w(0x137990) >> 26, (self.w(0x137990) >> 21) & 31, self.w(0x137990) & 1 ^ 1),
                         'clrlwi. on the low byte')
        self.assertEqual((16, 12, 2), fields(self.w(0x137994))[:3], 'zero byte goes to the GameType 0 call')
        self.assertEqual((14, 4, 0, 2), fields(self.w(0x1379b4)))
        self.assertEqual(SET_GAME_TYPE, self.bl_target(0x1379bc))
        self.assertEqual((14, 4, 0, 0), fields(self.w(0x1379e0)))
        self.assertEqual(SET_GAME_TYPE, self.bl_target(0x1379e8))

    def test_park_loader_reaches_set_game_type_only_through_the_guarded_constructor(self):
        sites = self.callers[SET_GAME_TYPE]
        self.assertEqual([0x971fc, 0x12bb98, 0x12bbb0, 0x12bbc8, 0x12bbd8, 0x1379bc, 0x1379e8, 0x18b13c, 0x1c2a9c, 0x1c2ac8],
                         sites)
        closure = self.direct_closure(PARK_LOADER)
        reached = sorted({self.function_of(at) for at in sites} & closure)
        self.assertEqual([GAME_TYPE_CTOR], reached, 'selection, online entry and main-loop sites are not reached')
        self.assertNotIn(SELECT_PLAYER, closure)
        # The constructor maps the startup flag bits (masks on the word read at 0x12bb84) to 2/1/0, else 0.
        for at, value in ((0x12bb94, 2), (0x12bbac, 1), (0x12bbc4, 0), (0x12bbd4, 0)):
            self.assertEqual((14, 4, 0, value), fields(self.w(at)))
        # Every direct constructor call is skipped once constructed: extsb. on the guard byte, bf eq past the call.
        unguarded = []
        for at in self.callers[GAME_TYPE_CTOR]:
            branch = self.w(at - 8)
            op, bo, bi, imm = fields(branch)
            tested = any(self.w(at - 8 - 4 * k) >> 26 == 31 and (self.w(at - 8 - 4 * k) >> 1) & 0x3ff == 954
                         and self.w(at - 8 - 4 * k) & 1 for k in range(1, 4))
            if not ((op, bo, bi) == (16, 4, 2) and at - 8 + (imm & ~3) > at and tested):
                unguarded.append(at)
        self.assertEqual(95, len(self.callers[GAME_TYPE_CTOR]))
        self.assertEqual([], unguarded)


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class MergeRound2(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.repo = os.environ['OPENTPW_REVIEW_REPO']
        for rev in (MAIN, LANE, MERGE, HEAD):
            if subprocess.run(['git', '-C', cls.repo, 'cat-file', '-e', f'{rev}^{{commit}}'], capture_output=True).returncode:
                raise unittest.SkipTest(f'{rev} not in {cls.repo}')

    def git(self, *args: str) -> str:
        return subprocess.run(['git', '-C', self.repo, *args], check=True, capture_output=True).stdout.decode('utf-8-sig')

    def show(self, rev: str, path: str) -> str:
        return self.git('show', f'{rev}:{path}')

    def test_merge_parents_and_clean_onto_main(self):
        parents = self.git('log', '-1', '--format=%P', MERGE).split()
        self.assertEqual([MAIN, LANE], [p[:7] for p in parents])
        run = subprocess.run(['git', '-C', self.repo, 'merge-tree', '--write-tree', MAIN, HEAD], capture_output=True)
        self.assertEqual(0, run.returncode, 'review head merges onto efc090d without conflicts')

    def test_line_endings_and_whitespace(self):
        self.assertEqual(self.git('diff', '--stat', MAIN, MERGE), self.git('diff', '--stat', '--ignore-cr-at-eol', MAIN, MERGE))
        self.assertEqual('', self.git('diff', '--check', MAIN, HEAD))

    def test_s1_1_resolutions(self):
        flow = self.show(MERGE, 'source/OpenTPW/Client/GameFlow.cs')
        self.assertRegex(flow, r'Level = level;\n\t\tif \( original \)\n\t\t\tGameAudio\.EnterPark\( levelName \);\n\t\tStartKind = level\.Park\?\.Start\.Kind;')
        self.assertIn('[APPROX:UI-041]', flow)
        self.assertIn('StartLevel( entry.Level, original: true, developerPanels: false );', flow)
        self.assertNotIn('Mode == GameMode.InstantAction ? ParkGameMode', flow)
        park = self.show(MERGE, 'source/OpenTPW/World/Original/OriginalPark.cs')
        self.assertIn('[BIN:STP-PPC:0x10137600 player save setup]', park)
        self.assertIn('if ( savePath != null )\n', park)
        self.assertEqual(self.show(MAIN, 'source/OpenTPW/Economy/ParkResearch.cs'), self.show(MERGE, 'source/OpenTPW/Economy/ParkResearch.cs'))
        economy = self.show(MERGE, 'source/OpenTPW/Economy/ParkEconomy.cs')
        self.assertIn('[BIN:STP-PPC:0x10154AA0 loans window]', economy)
        self.assertIn('AvailableLoans => !Features.Loans', economy)
        self.assertRegex(economy, r'\[BIN:STP-PPC:0x10165A0C upgrade list\][^\n]*\n\t\tif \( !Features\.Upgrades \)')
        self.assertIn('if ( turn % GoldenTicketCheckInterval == 0 && Mode == ParkGameMode.FullSimulation )', economy)
        self.assertNotIn('Features.GoldenTickets ? Objectives.CheckGoldenTickets', economy, 'lane month-end ticket check dropped')
        self.assertIn('Features.Challenges ? Objectives.AdvanceDay(', economy)
        grep = subprocess.run(['git', '-C', self.repo, 'grep', '-q', 'includeEasymodePark', MERGE, '--', 'source'])
        self.assertEqual(1, grep.returncode, 'main\'s removed parameter is gone from the source')

    def test_s1_2_stand_in_and_save_member(self):
        economy = self.show(MERGE, 'source/OpenTPW/Economy/ParkEconomy.cs')
        self.assertIn('if ( employed.Count == 0 && SeedResearcherStandIn && Mode == ParkGameMode.InstantAction )', economy)
        self.assertIn('[APPROX:ECON-019] stand-in for the Instant Action seed\'s undecoded researcher', economy)
        runtime = self.show(MERGE, 'source/OpenTPW/Economy/ParkEconomyRuntime.cs')
        self.assertIn('economy.SeedResearcherStandIn = import != null && start.Mode == ParkGameMode.InstantAction;', runtime)
        save = self.show(MERGE, 'source/OpenTPW/Economy/ParkSaveFile.cs')
        self.assertIn('\t\tpublic bool SeedResearcherStandIn { get; init; }', save, 'optional: not required')
        self.assertIn('public const int CurrentVersion = 2;', save, 'no version bump; older version-2 saves still load')
        self.assertIn('{ SeedResearcherStandIn = data.SeedResearcherStandIn }', save)

    def test_no_parent_assertion_dropped_beyond_the_ports(self):
        base = self.git('merge-base', MAIN, LANE).strip()
        allowed = {
            'var full = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle", includeEasymodePark: false ), ParkGameMode.FullSimulation );',
            'var instant = ParkEconomyRuntime.ForOriginalLevel( OriginalPark.Load( "jungle" ), ParkGameMode.InstantAction );',
            'var automatic = EconomyTestData.Park( mode: ParkGameMode.InstantAction );',
            'automatic.Advance( ParkCalendar.TickOfTurn( ParkResearch.TurnsPerResearch ) );',
            'Assert.IsTrue( automatic.Research.GetProgress( automatic.Research.Current( ResearchCategory.Ride )! ) > 0, "Instant Action research is automatic" );',
            'instant.AdvanceDays( 1 );',
        }
        for side in (MAIN, LANE):
            names = self.git('diff', '--name-only', base, side, '--', 'source/OpenTPW.Tests').split()
            added = {line[1:].strip() for line in self.git('diff', base, side, '--', 'source/OpenTPW.Tests').splitlines()
                     if line.startswith('+') and not line.startswith('+++')}
            merged = set()
            for name in names:
                if subprocess.run(['git', '-C', self.repo, 'cat-file', '-e', f'{MERGE}:{name}'], capture_output=True).returncode == 0:
                    merged |= {line.strip() for line in self.show(MERGE, name).splitlines()}
            lost = sorted(line for line in added - merged - allowed if line)
            self.assertEqual([], lost, side)


if __name__ == '__main__':
    unittest.main()
