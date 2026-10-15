"""Advisor runtime review V2: the round-2 stack efc090d..a626cbf (b0562dd cherry-pick, 99b23fc review, a626cbf fixes).

- Git (OPENTPW_REVIEW_REPO, a checkout holding efc090d, b0562dd and a626cbf): the cherry-pick
  changes nothing but the regenerated register, source witnesses for the V1-1..V1-3 fixes and the
  sandbox restriction, the port-versus-helper member diff after the event-10 fix, line endings and
  merge cleanliness against efc090d (fork main at review time).
- The native operands behind the fixes (0xba54/0xbb64/0xbbf0, 0x6bb4, 0x9dd8-0x9e80) are pinned
  by test_advisor_v1.py and are not repeated here.

Bounded: the witnesses read source text; behaviour is covered by the OpenTPW.Tests cases they
name and by the scratch differential harness described in PPC-review.md section 56.1.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from test_advisor_v1 import HELPER, PORT, members  # noqa: E402

BASE = 'efc090d'
PICK = 'b0562dd'
FIX = 'a626cbf'
REGISTER = 'docs/FIDELITY-REGISTER.md'
# LIPS.md rows whose cited line is not the APPROX tag, identical on fork main (pre-existing).
STALE_LIPS_ROWS = ['ADVISOR-003', 'ADVISOR-004', 'ADVISOR-005', 'ADVISOR-006', 'ADVISOR-007',
                   'ADVISOR-008', 'ADVISOR-010', 'ADVISOR-011', 'ADVISOR-012', 'ADVISOR-014']


def event_ten(history: dict) -> dict:
    """Review model of 0x9dd8-0x9e80: +0xe4 = -1, +0xe8 = 0, +0xec = 0; the saved tick +0xe0 stays."""
    return {key: (tick, -1, False, 0) for key, (tick, _variant, _played, _slaps) in history.items()}


class EventTenModel(unittest.TestCase):
    def test_saved_tick_survives(self):
        self.assertEqual({0: (100, -1, False, 0)}, event_ten({0: (100, 3, True, 2)}))


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class AdvisorRuntimeRoundTwo(unittest.TestCase):
    def git(self, *args, check=True):
        result = subprocess.run(['git', '-C', os.environ['OPENTPW_REVIEW_REPO'], *args],
                                capture_output=True, check=False)
        if check and result.returncode:
            raise unittest.SkipTest(result.stderr.decode(errors='replace').strip() or 'git object missing')
        return result

    def show(self, rev, path):
        return self.git('show', f'{rev}:{path}').stdout.decode('utf-8')

    def changed_lines(self, old, new, path):
        diff = self.git('diff', old, new, '--', path).stdout.decode('utf-8', errors='replace')
        return [line for line in diff.splitlines()
                if line[:1] in '+-' and not line.startswith(('+++', '---'))]

    def setUp(self):
        for rev in (BASE, 'be05bb1', 'f8375b2', PICK, FIX):
            self.git('cat-file', '-e', rev + '^{commit}')

    def test_stack_and_clean_merge(self):
        parents = [self.git('rev-parse', '--short=7', f'{FIX}~{n}').stdout.decode().strip() for n in (1, 2, 3)]
        self.assertEqual(['99b23fc', PICK, BASE], parents)
        self.assertEqual(0, self.git('merge-tree', '--write-tree', BASE, FIX, check=False).returncode)

    def test_cherry_pick_changes_only_the_generated_register(self):
        original = self.git('diff', '--name-only', 'be05bb1', 'f8375b2').stdout.decode().split()
        picked = self.git('diff', '--name-only', BASE, PICK).stdout.decode().split()
        self.assertEqual(sorted(original), sorted(picked))
        differing = [path for path in picked
                     if self.changed_lines('be05bb1', 'f8375b2', path) != self.changed_lines(BASE, PICK, path)]
        self.assertEqual([REGISTER], differing)

    def test_v1_1_muted_speech_opens_no_device(self):
        advisor = self.show(FIX, 'source/OpenTPW/World/Advisor.cs')
        self.assertIn('player = CreatePlayer( audio );', advisor)
        self.assertIn('openDevice: GameAudio.Enabled && AudioMixer.Current == null', advisor)
        self.assertNotIn(': new SpeechAudioPlayer( audio );', advisor)
        self.assertIn('MutedSpeechKeepsTheWallClockWithoutOpeningADevice',
                      self.show(FIX, 'source/OpenTPW.Tests/AdvisorTests.cs'))

    def test_v1_2_wrapper_success_and_option_off(self):
        controller = self.show(FIX, 'source/OpenTPW/World/AdvisorController.cs')
        self.assertIn('playbackSucceeded: true, advisorClock(), liveGameTick(), span ?? 0', controller)
        self.assertNotIn('span.HasValue', controller)
        self.assertNotIn('RevalidatedPlaybackScoreAccepts', controller)
        automatic = self.show(FIX, 'source/OpenTPW/Client/AutomaticAdvisor.cs')
        update = automatic.split('public void Update()', 1)[1].split('private uint? Play', 1)[0]
        off = update[update.index('if ( !GameOptions.Current.Advisor )'):]
        off = off[:off.index('return;')]
        # Not covered by OpenTPW.Tests: deleting this call leaves every test green (round-2 mutation).
        self.assertIn('Controller.Update( () => AdvisorClock, () => GameTick, _ => 0u );', off)
        self.assertLess(off.index('Silence()'), off.index('Controller.Update('))
        tests = self.show(FIX, 'source/OpenTPW.Tests/AdvisorControllerTests.cs')
        for name in ('UnplayedResponseStillRecordsHistoryAndReservesTheMinimumAction',
                     'AdvisorOptionOffConsumesTheAdviceSilently'):
            self.assertIn(name, tests)
        self.assertNotIn('FailedPlaybackConsumesTheAdviceWithoutHistoryOrReservation', tests)

    def test_v1_3_event_ten_keeps_the_saved_tick(self):
        queue = self.show(FIX, PORT)
        self.assertNotIn('history.Clear()', queue)
        self.assertIn('AdvisorMessageHistory.Empty with { SavedGameTick = history[id].SavedGameTick }', queue)
        self.assertIn('AdvisorMessageHistory Empty => new( 0, -1, false, 0 )', queue)
        tests = self.show(FIX, 'source/OpenTPW.Tests/AdvisorControllerTests.cs')
        self.assertIn('RepeatIntervalUsesQuarterTicksAndEventTenKeepsTheSavedTick', tests)
        self.assertIn('AdvisorMessageHistory.Empty with { SavedGameTick = 100 }', tests)

    def test_port_members_still_equal_helper_members(self):
        helper = members(self.show('f51e874', HELPER))
        port = members(self.show(FIX, PORT))
        for key in helper:
            self.assertEqual(helper[key].replace('reviewed cyclic', 'shipped cyclic'), port[key], key)
        extra = sorted(key.split()[-1] for key in set(port) - set(helper))
        self.assertEqual(['ClearHistory', 'HasDescriptor', 'IsAttemptOutstanding'], extra)

    def test_sandbox_restriction_leaves_the_manual_advisor_alone(self):
        flow = self.show(FIX, 'source/OpenTPW/Client/GameFlow.cs')
        self.assertIn('if ( AutomaticAdvisorEnabled && original && visit == null && !advisorUnavailable )', flow)
        self.assertIn('[EXT:sandbox]', flow)
        game = self.show(FIX, 'source/OpenTPW/Client/Game.cs').replace('\r\n', '\n')
        manual = game.split('private static Advisor? CreateAdvisor( string[] args )', 1)[1].split('\n\t}\n', 1)[0]
        # The manual presentation reads only the arguments: no level kind, no flow, no automatic advisor.
        for name in ('original:', 'flow', 'AutomaticAdvisor', 'Level'):
            self.assertNotIn(name, manual)
        self.assertIn('new Advisor();', manual)
        self.assertIn('using var advisor = CreateAdvisor( args );', game)

    def test_register_and_approximations(self):
        advisor = self.show(FIX, 'source/OpenTPW/World/Advisor.cs')
        self.assertEqual([f'ADVISOR-{n:03}' for n in range(1, 23)], re.findall(r'\("(ADVISOR-\d{3})"', advisor))
        register = self.show(FIX, REGISTER)
        self.assertIn('**148 unresolved unique APPROX IDs**', register)
        self.assertIn('ADVISOR-022', register)

    def test_line_endings_and_whitespace(self):
        plain = self.git('diff', '--stat', BASE, FIX).stdout
        self.assertEqual(plain, self.git('diff', '--ignore-cr-at-eol', '--stat', BASE, FIX).stdout)
        self.assertEqual(0, self.git('-c', 'core.whitespace=cr-at-eol', 'diff', '--check', BASE, FIX,
                                     check=False).returncode)
        for path in self.git('diff', '--name-only', PICK, FIX).stdout.decode().split():
            new = self.git('show', f'{FIX}:{path}').stdout
            old = self.git('show', f'{PICK}:{path}', check=False)
            if old.returncode:
                continue
            was_crlf = old.stdout.count(b'\r\n') > 0
            self.assertEqual(new.count(b'\n') if was_crlf else 0, new.count(b'\r\n'), path)

    def test_stale_lips_rows_are_pre_existing(self):
        def stale(rev):
            doc = self.show(rev, 'docs/LIPS.md')
            found = []
            for match in re.finditer(r'^\| (ADVISOR-\d+) \| `([^`:]+):(\d+)`', doc, re.M):
                lines = self.show(rev, match.group(2)).splitlines()
                line = int(match.group(3))
                if not (0 < line <= len(lines) and f'APPROX:{match.group(1)}' in lines[line - 1]):
                    found.append(match.group(1))
            return found
        self.assertEqual(STALE_LIPS_ROWS, stale(BASE))
        self.assertEqual(STALE_LIPS_ROWS, stale(FIX))


if __name__ == '__main__':
    unittest.main()
