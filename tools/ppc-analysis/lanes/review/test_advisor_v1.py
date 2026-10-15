"""Advisor runtime review V1: production commit f8375b2 (automatic advisor) against fork main f51e874.

- Native (OPENTPW_PPC_BIN_ROOT, identified Feral Mac bin): the game-type global shared by the
  event-0 check and the player selector, the main-loop 10-then-0 events, the event-10 history
  reset, the cyclic variant, the controller's playback wrapper 0xba54 and the advisor-option
  test in the response player 0x6b7c. Decoded from instruction words and relocations only.
  Nothing is executed and no bytes are written out.
- Data (OPENTPW_PC_DATA, a TPW Data directory): the Advisor.sam values behind the five bound
  messages.
- Git (OPENTPW_REVIEW_REPO, a checkout holding f51e874 and f8375b2): merge cleanliness, line
  endings, the port-versus-helper member diff and source witnesses for blockers V1-1..V1-3.
  Witnesses read f8375b2 itself, so they stay true after a fix lands in a later commit.

Bounded: the general-interval scan (ADVISOR-019) covers the 13 functions called with the balance
object in r3 and the direct TOC loads of it; it does not prove that no other reader exists.
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

APP_SHA = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
ADVISOR_SAM_SHA = 'e905df3d0a12ea5d7580ce87a7dd3a6c7e690e2f072b9eafef8cbfdc88f00df3'
TOC = 0x8000
BALANCE = 0x53dc0
BALANCE_ACCESSORS = (0xcfd4, 0xd0b4, 0xd280, 0xd468, 0xd5ec, 0xd770, 0xd8f8,
                     0xda94, 0xdc44, 0xddc8, 0xdf4c, 0xe354, 0xe4ec)
HELPER = 'tools/ppc-analysis/lanes/advisor/OriginalAdvisorScoreQueue.cs'
PORT = 'source/OpenTPW/World/AdvisorScoreQueue.cs'


def s16(value: int) -> int:
    return value - 0x10000 if value & 0x8000 else value


def history_reset_offsets(words):
    """Store displacements (mod the 16-byte history stride) of the event-10 loop body."""
    return {s16(w & 0xffff) & 0xf for w in words if w >> 26 in (36, 38)}


class HistoryResetModel(unittest.TestCase):
    def test_offsets_mod_stride(self):
        # stw r4,0xec(r7); stw r3,0xe4(r7); stb r4,0xe8(r7)
        self.assertEqual({4, 8, 0xc}, history_reset_offsets([0x908700ec, 0x906700e4, 0x988700e8]))
        self.assertIn(0, history_reset_offsets([0x908700e0]))


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class NativeAdvisorRuntime(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(os.environ['OPENTPW_PPC_BIN_ROOT']) / 'SimThemePark.data'
        if hashlib.sha256(path.read_bytes()).hexdigest() != APP_SHA:
            raise unittest.SkipTest('SimThemePark.data is not the identified container')
        cls.app = pef.load(str(path))
        cls.code = cls.app.code.data

    def word(self, at):
        return struct.unpack_from('>I', self.code, at)[0]

    def words(self, start, end):
        return [self.word(at) for at in range(start, end, 4)]

    def toc_target(self, slot_disp):
        target = self.app.relocs[1][TOC + slot_disp]
        self.assertEqual(('section', 1), (target.kind, target.target))
        return target.addend

    def call(self, at):
        word = self.word(at)
        self.assertEqual((18, 1), (word >> 26, word & 3))
        offset = word & 0x3fffffc
        return at + (offset - 0x4000000 if offset & 0x2000000 else offset)

    def callers(self, target):
        found = []
        for at in range(0, len(self.code), 4):
            word = self.word(at)
            if word >> 26 == 18 and word & 3 == 1 and self.call(at) == target:
                found.append(at)
        return found

    def test_event0_check_and_player_selector_share_the_game_type_global(self):
        self.assertEqual(0x83228a48, self.word(0x94e8))   # event handler: lwz r25,-30136(r2)
        self.assertEqual(0x83a28a48, self.word(0x13782c))  # player selector: lwz r29,-30136(r2)
        self.assertEqual(-30136, s16(0x8a48))
        self.assertEqual(0x53d98, self.toc_target(-30136))
        self.assertEqual([0x80190000, 0x2c000002], self.words(0x96f0, 0x96f8))  # lwz r0,0(r25); cmpwi 2
        # Selector: online (1) is kept; else mEasyModeUser (byte +0x24) picks 2 or 0.
        self.assertEqual([0x801d0000, 0x2c000001], self.words(0x137950, 0x137958))
        self.assertEqual(0x128f4c, self.call(0x13798c))
        self.assertEqual(0x88630024, self.word(0x128f4c))
        self.assertEqual([0x38800002, 0x387d0000], self.words(0x1379b4, 0x1379bc))
        self.assertEqual([0x38800000, 0x387d0000], self.words(0x1379e0, 0x1379e8))
        self.assertEqual(0x12bbf4, self.call(0x1379bc))
        self.assertEqual(0x12bbf4, self.call(0x1379e8))
        # Setter: r30 = value, r29 = object, stw r30,0(r29).
        self.assertEqual([0x3bc40000, 0x3ba30000, 0x93dd0000],
                         [self.word(0x12bc04), self.word(0x12bc10), self.word(0x12bc40)])

    def test_main_loop_raises_event_10_then_event_0(self):
        self.assertEqual(0x3880000a, self.word(0x1c2104))
        self.assertEqual(0x116528, self.call(0x1c2108))
        self.assertEqual(0x38800000, self.word(0x1c216c))
        self.assertEqual(0x116528, self.call(0x1c2174))

    def test_event10_resets_variant_played_and_slaps_but_keeps_the_saved_tick(self):
        self.assertEqual(0x7c7f1b78, self.word(0x94f0))   # r31 = controller
        self.assertEqual(0x9dcc, self.app.relocs[1][self.toc_target(-23724) + 40].addend)
        self.assertEqual(0xa90, self.call(0x9dd4))        # argument-spill stub, then the loop
        self.assertEqual([0x3800002b, 0x38ff0000], self.words(0x9dd8, 0x9de0))  # 43 x 8 records from r31
        self.assertEqual(0x2c05015f, self.word(0x9e68))  # remainder up to 351
        body = self.words(0x9df0, 0x9e54) + self.words(0x9e70, 0x9e7c)
        self.assertEqual({4, 8, 0xc}, history_reset_offsets(body))
        self.assertEqual(0x3860ffff, self.word(0x9dec))  # variant -1
        # +0xe0 is the saved tick: setter target and elapsed reader.
        self.assertEqual([0x386300e0], [self.word(0x8a20)])
        self.assertEqual(0x121098, self.call(0x8a28))
        self.assertEqual([0x3c84001e, 0x8004a70c, 0x90030000], self.words(0x1210a0, 0x1210ac))
        self.assertEqual(0x1da70c, (0x1e << 16) + s16(0xa70c))
        self.assertEqual(0x3afa00e0, self.word(0x9090))  # eligibility history base +0xe0
        self.assertEqual([0x93a300e4, 0x988300e8], [self.word(0x8a3c), self.word(0x8a4c)])
        # +0xec is the slap count: incremented by the slap path and passed to the limit check.
        self.assertEqual([0x806400ec, 0x38030001, 0x900400ec], self.words(0x8e28, 0x8e34))
        self.assertEqual(0x80a300ec, self.word(0x9198))

    def test_cyclic_variant_alternates_two_response_advice(self):
        self.assertEqual([0x806300e4, 0x3ba30001], self.words(0x8974, 0x897c))
        self.assertEqual(0xd5ec, self.call(0x89d4))
        self.assertEqual([0x7c1d1800, 0x41800008, 0x3ba00000], self.words(0x89d8, 0x89e4))
        data = self.app.data_section.data
        base = self.toc_target(-30072)
        rows = {}
        for i in range(351):
            record = struct.unpack_from('>12i', data, base + 48 * i)
            rows[record[1]] = record
        self.assertEqual({2}, {struct.unpack_from('>12i', data, base + 48 * i)[10] for i in range(351)})
        # message: (group +8, duplicate limit +28, first +32, count +36)
        got = {m: (rows[m][2], rows[m][7] & 0xff, rows[m][8], rows[m][9]) for m in (0, 106, 128, 129, 323)}
        self.assertEqual({0: (0, 1, 1, 1), 106: (0, 1, 274, 2), 128: (0, 1, 308, 2),
                          129: (0, 1, 310, 2), 323: (1, 1, 587, 1)}, got)

    def test_controller_wrapper_succeeds_whatever_the_player_returns(self):
        self.assertEqual([0x89fc], self.callers(0xba54))
        self.assertEqual([0x2c030000, 0x418200f8], self.words(0x8a00, 0x8a08))   # success -> history
        self.assertEqual([0x80810098, 0x387f0000, 0x388403e8], self.words(0x8a08, 0x8a14))
        self.assertEqual(0x7c992378, self.word(0xba68))   # r25 = span out-parameter
        self.assertEqual([0x88030014, 0x28000000, 0x4182016c], self.words(0xba84, 0xba90))  # invalid -> 0
        self.assertEqual([0x2c1a0266, 0x41820088], self.words(0xbb54, 0xbb5c))  # 614 -> 0
        self.assertEqual(0x6b7c, self.call(0xbb60))
        self.assertEqual(0x90790000, self.word(0xbb64))   # player result is only stored as the span
        self.assertEqual([0x38600001], [self.word(0xbbf0)])
        self.assertEqual([0x38000000, 0x38600000], self.words(0xbbe0, 0xbbe8))
        self.assertEqual(0x38600000, self.word(0xbbf8))
        self.assertEqual(0x7f43d378, self.word(0xbb68))   # mr r3,r26: the result is overwritten, never tested

    def test_player_returns_zero_when_the_advisor_option_byte_is_clear(self):
        self.assertEqual(0x808289c4, self.word(0x6b84))
        self.assertEqual(0x120a14, self.toc_target(-30268))
        self.assertEqual([0x88640034, 0x28030000, 0x4082000c, 0x38600000], self.words(0x6bb4, 0x6bc4))
        self.assertEqual(0x48000490, self.word(0x6bc4))   # b 0x7054 (epilogue)
        self.assertEqual(0x6bc4 + 0x490, 0x7054)
        # The eligibility check reads byte +0x35 of the same object.
        self.assertEqual([0x80c289c4, 0x88060035], [self.word(0x9010), self.word(0x9038)])

    def test_no_reader_of_general_intervals_in_the_balance_accessors(self):
        hits = []
        for start in BALANCE_ACCESSORS:
            at = start
            while self.word(at) != 0x4e800020:
                word = self.word(at)
                if word >> 26 == 32 and word >> 16 & 31 not in (1, 5) and s16(word & 0xffff) in (0x18, 0x1c):
                    hits.append(at)
                at += 4
        self.assertEqual([], hits)
        slots = [d for d in range(-32768, 32768, 4)
                 if (t := self.app.relocs[1].get(TOC + d)) and t.kind == 'section' and t.target == 1 and t.addend == BALANCE]
        self.assertEqual([-30116], slots)


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'OPENTPW_PC_DATA not set')
class AdvisorSam(unittest.TestCase):
    def test_bound_scores_and_controls(self):
        path = Path(os.environ['OPENTPW_PC_DATA']) / 'Advisor' / 'Advisor.sam'
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != ADVISOR_SAM_SHA:
            raise unittest.SkipTest('Advisor.sam is not the identified file')
        values = {}
        for line in raw.decode('latin-1').splitlines():
            parts = line.split()
            if len(parts) >= 2 and not line.startswith('#'):
                values.setdefault(parts[0], parts[1])
        want = {'GeneralAdvisor.MinScoreForConsideration': '25', 'GeneralAdvisor.MinTimeAnyMessage': '5',
                'GeneralAdvisor.MinTimeSameMessage': '120', 'Welcome.Score': '100000',
                'Bankrupted.Score': '100000', 'ParkNowOpen.Score': '20', 'ParkNowClosed.Score': '20',
                'PrebuiltPark.Score': '1000', 'MessageGroups[0].MinTimeSameMessage': '120',
                'MessageGroups[0].SayOnlyOnce': '0', 'MessageGroups[0].DiscardAfterSlaps': '0',
                'MessageGroups[1].MinTimeSameMessage': '0', 'MessageGroups[1].SayOnlyOnce': '1',
                'MessageGroups[1].DiscardAfterSlaps': '3'}
        self.assertEqual(want, {key: values.get(key) for key in want})
        # Selection needs a score strictly above the minimum, so open/close never play.
        self.assertFalse(int(values['ParkNowOpen.Score']) > int(values['GeneralAdvisor.MinScoreForConsideration']))


def normalize(text: str) -> str:
    text = text.replace('OriginalAdvisor', 'Advisor').replace('unscaledClock', 'advisorClock')
    text = re.sub(r'\b(public|internal)\s+', '', text)
    return re.sub(r'\s+', ' ', text).strip()


def members(source: str) -> dict:
    """Queue class members, keyed by declaration head; a member starts at a one-tab declaration line."""
    lines = [re.sub(r'//.*$', '', line).rstrip() for line in source.replace('\r', '').splitlines()]
    lines = lines[next(i for i, line in enumerate(lines) if 'class OriginalAdvisorScoreQueue' in line
                       or 'class AdvisorScoreQueue' in line) + 2:]
    chunks = []
    for line in lines:
        if re.match(r'^\t(public|private|internal)\b', line):
            chunks.append([])
        if chunks and line.strip():
            chunks[-1].append(line)
    return {normalize(re.split(r'[(={;]', chunk[0], maxsplit=1)[0]): normalize('\n'.join(chunk)) for chunk in chunks}


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class AdvisorRuntimeGit(unittest.TestCase):
    def git(self, *args, check=True):
        result = subprocess.run(['git', '-C', os.environ['OPENTPW_REVIEW_REPO'], *args],
                                capture_output=True, check=False)
        if check and result.returncode:
            raise unittest.SkipTest(result.stderr.decode(errors='replace').strip() or 'git object missing')
        return result

    def show(self, rev, path):
        return self.git('show', f'{rev}:{path}').stdout.decode('utf-8')

    def setUp(self):
        for rev in ('f51e874', 'f8375b2', 'be05bb1'):
            self.git('cat-file', '-e', rev + '^{commit}')

    def test_parent_and_clean_merge(self):
        self.assertEqual('be05bb1', self.git('rev-parse', '--short=7', 'f8375b2^').stdout.decode().strip())
        self.assertEqual(0, self.git('merge-tree', '--write-tree', 'f51e874', 'f8375b2', check=False).returncode)

    def test_line_endings_are_kept(self):
        changed = self.git('diff', '--name-only', 'be05bb1', 'f8375b2').stdout.decode().split()
        for path in changed:
            new = self.git('show', f'f8375b2:{path}').stdout
            old = self.git('show', f'be05bb1:{path}', check=False)
            lines = new.count(b'\n')
            crlf = new.count(b'\r\n')
            if old.returncode:
                self.assertEqual(lines, crlf, f'new file {path} is CRLF')
            elif old.stdout.count(b'\r\n') == 0:
                self.assertEqual(0, crlf, f'{path} stays LF')
            else:
                self.assertEqual(lines, crlf, f'{path} stays CRLF')
        for path in ('source/OpenTPW/World/Advisor.cs', 'source/OpenTPW/Client/GameFlow.cs',
                     'source/OpenTPW/Economy/ParkEconomyRuntime.cs'):
            self.assertNotIn(b'\r', self.git('show', f'f8375b2:{path}').stdout)
        plain = self.git('diff', '--stat', 'be05bb1', 'f8375b2').stdout
        ignoring = self.git('diff', '--ignore-cr-at-eol', '--stat', 'be05bb1', 'f8375b2').stdout
        self.assertEqual(plain, ignoring)

    def test_port_members_equal_helper_members(self):
        helper = members(self.show('f51e874', HELPER))
        port = members(self.show('f8375b2', PORT))
        shared = set(helper) & set(port)
        self.assertEqual(set(helper), shared, 'every helper member is ported')
        for key in shared:
            self.assertEqual(helper[key].replace('reviewed cyclic', 'shipped cyclic'), port[key], key)
        extra = sorted(key.split()[-1] for key in set(port) - shared)
        self.assertEqual(['ClearHistory', 'HasDescriptor', 'IsAttemptOutstanding'], extra)

    def test_blocker_witnesses_at_f8375b2(self):
        advisor = self.show('f8375b2', 'source/OpenTPW/World/Advisor.cs')
        speech = self.show('f8375b2', 'source/OpenTPW/Client/SpeechAudioPlayer.cs')
        audio = self.show('f8375b2', 'source/OpenTPW/Audio/GameAudio.cs')
        automatic = self.show('f8375b2', 'source/OpenTPW/Client/AutomaticAdvisor.cs')
        queue = self.show('f8375b2', PORT)
        # V1-1: muted -> no mixer -> the fallback player opens its own SDL device.
        self.assertIn(': new SpeechAudioPlayer( audio );', advisor)
        self.assertIn('openDevice: AudioMixer.Current == null', speech)
        self.assertRegex(audio, r'if \( !Enabled \)\s*return false;')
        self.assertNotIn('--mute', automatic)
        # V1-2: option off returns before the controller runs; failures report no success.
        update = automatic.split('public void Update()', 1)[1].split('private uint? Play', 1)[0]
        self.assertLess(update.index('return;', update.index('GameOptions.Current.Advisor')),
                        update.index('Controller.Update('))
        self.assertIn('span.HasValue', self.show('f8375b2', 'source/OpenTPW/World/AdvisorController.cs'))
        # V1-3: event 10 drops the saved tick with the rest of the history.
        self.assertIn('public void ClearHistory() => history.Clear();', queue)


if __name__ == '__main__':
    unittest.main()
