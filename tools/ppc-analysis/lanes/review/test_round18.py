"""Round 18: batch 2 ba70ea5 line endings, formats 8f0e048 clock-A/normal operands, scenarios ffa87c6 envelopes.

- With OPENTPW_REVIEW_REPO naming a checkout that holds ba70ea5, Game.cs is CRLF again (419/419, as in 6ec5d28),
  and the byte diff against 6ec5d28 is the comment line and the IntroPlaylist.ShouldPlay line.
- With OPENTPW_PPC_BIN_ROOT naming the identified Feral Mac bin directory, the rate constants, the clamp,
  the hold divisor and both n = 32 call sites, the held step, the 0x10000000 writer and placement's 0x211f
  flags are decoded from the instruction words with a separate field split. The clamp is executed by a
  ten-instruction interpreter, so a wrong branch condition, bound or constant fails the test. That
  interpreter shows a NaN rate is stored unchanged. The formats reference model says it becomes 0.25.
- With OPENTPW_SCENARIOS_LANE naming ffa87c6's lanes/scenarios directory, the strict envelope decoder is
  probed with synthetic records only. Two expected failures pin known gaps in the partial-record check.

Nothing is executed from the original. Nothing here says anything about the PC build, PC game speed, or a
real gms.dat.
"""
from __future__ import annotations

import copy
import hashlib
import json
import math
import os
import struct
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import pef  # noqa: E402

APP_SHA256 = '04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5'
TOC = 0x8000  # r2 relative to the data section (toc[0x5710] = -10480(r2))


def fields(word: int) -> tuple[int, int, int, int]:
    """(primary opcode, rD/rS/BO, rA/BI, signed 16-bit immediate)."""
    return word >> 26, (word >> 21) & 31, (word >> 16) & 31, struct.unpack('>h', struct.pack('>H', word & 0xffff))[0]


def xo(word: int) -> int:
    return (word >> 1) & 0x3ff


@unittest.skipUnless(os.environ.get('OPENTPW_REVIEW_REPO'), 'OPENTPW_REVIEW_REPO not set')
class Batch2CrlfRestored(unittest.TestCase):
    def show(self, rev: str) -> bytes:
        repo = os.environ['OPENTPW_REVIEW_REPO']
        try:
            return subprocess.run(['git', '-C', repo, 'show', f'{rev}:source/OpenTPW/Client/Game.cs'],
                                  check=True, capture_output=True).stdout
        except subprocess.CalledProcessError:
            raise unittest.SkipTest(f'{rev} not in {repo}')

    def test_ba70ea5_is_crlf_with_only_the_intro_gate_changed(self):
        parent, commit = self.show('6ec5d28'), self.show('ba70ea5')
        self.assertEqual((419, 419), (commit.count(b'\r\n'), commit.count(b'\n')))
        self.assertTrue(commit.startswith(b'\xef\xbb\xbf'), 'BOM kept')
        old, new = parent.split(b'\r\n'), commit.split(b'\r\n')
        self.assertEqual(len(old), len(new))
        changed = [(a, b) for a, b in zip(old, new) if a != b]
        self.assertEqual(2, len(changed))
        self.assertIn(b'--capture-world', changed[0][1])
        self.assertIn(b'IntroPlaylist.ShouldPlay', changed[1][1])
        self.assertIn(b'var playIntro', changed[1][0])


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'OPENTPW_PPC_BIN_ROOT not set')
class SceneClockAndNormals(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(os.environ['OPENTPW_PPC_BIN_ROOT']) / 'SimThemePark.data'
        if hashlib.sha256(path.read_bytes()).hexdigest() != APP_SHA256:
            raise unittest.SkipTest('not the identified Feral Mac SimThemePark.data')
        c = pef.load(str(path))
        cls.code, cls.data = bytes(c.code.data), bytes(c.data_section.data)

    def w(self, at: int) -> int:
        return struct.unpack_from('>I', self.code, at)[0]

    def toc_double(self, disp: int) -> float:
        return struct.unpack_from('>d', self.data, TOC + disp)[0]

    def bl_target(self, at: int) -> int:
        word = self.w(at)
        self.assertEqual((18, 1), (word >> 26, word & 3), f'bl at {at:#x}')
        li = word & 0x03fffffc
        return at + (li - 0x04000000 if li & 0x02000000 else li)

    def run_clamp(self, entry: int, rate: float) -> float:
        """Execute the step routine's words: lfd/fmul/fdiv/stfd/fcmpo/bc/b/fmr/blr on one double slot."""
        f, mem, cr, pc = [0.0] * 32, {24: rate}, [False] * 4, entry
        for _ in range(32):
            word = self.w(pc)
            op, d, a, imm = fields(word)
            nxt = pc + 4
            if op == 50:                                  # lfd
                f[d] = mem[imm] if a == 3 else self.toc_double(imm)
                self.assertIn(a, (2, 3))
            elif op == 54:                                # stfd
                self.assertEqual((3, 24), (a, imm))
                mem[24] = f[d]
            elif op == 63 and (word >> 1) & 31 == 25:     # fmul (A-form, frC)
                f[d] = f[a] * f[(word >> 6) & 31]
            elif op == 63 and (word >> 1) & 31 == 18:     # fdiv
                f[d] = f[a] / f[(word >> 11) & 31]
            elif op == 63 and xo(word) == 32:             # fcmpo crfD
                self.assertEqual(0, d >> 2)
                x, y = f[a], f[(word >> 11) & 31]
                un = math.isnan(x) or math.isnan(y)
                cr = [not un and x < y, not un and x > y, not un and x == y, un]
            elif op == 63 and xo(word) == 72:             # fmr
                f[d] = f[(word >> 11) & 31]
            elif op == 16:                                # bc BO, BI
                self.assertEqual(0, word & 3)
                bo, bi = d, a
                self.assertIn(bo, (4, 12))
                if cr[bi] == (bo == 12):
                    nxt = pc + (imm & ~3)
            elif op == 18:                                # b
                li = word & 0x03fffffc
                nxt = pc + (li - 0x04000000 if li & 0x02000000 else li)
            elif word == 0x4e800020:
                return mem[24]
            else:
                self.fail(f'unexpected word {word:#010x} at {pc:#x}')
            pc = nxt
        self.fail('no blr')

    def test_rate_constants(self):
        self.assertEqual((2.0, 0.25, 1.25, 1.0), tuple(self.toc_double(d) for d in (-10504, -10496, -10488, -10480)))
        self.assertEqual(1.0, self.toc_double(-10536), 'reset handler 0x1131ac loads 1.0')
        self.assertEqual((54, 0, 31, 24), fields(self.w(0x127c28)), 'init stores the 1.0 at +0x18')
        self.assertEqual(0x127c08, self.bl_target(0x10ea78), 'clock initializer calls the rate init')

    def test_steps_clamp_to_quarter_and_two(self):
        up, down = 0x127c48, 0x127c88
        path, rate = [], 1.0
        for _ in range(4):
            rate = self.run_clamp(up, rate)
            path.append(rate)
        self.assertEqual([1.25, 1.5625, 1.953125, 2.0], path)
        self.assertEqual(1.6, self.run_clamp(down, 2.0))
        self.assertEqual(0.25, self.run_clamp(down, 0.3))
        self.assertEqual(2.0, self.run_clamp(up, 1.9))

    def test_nan_rate_is_kept_not_clamped(self):
        # fcmpo on NaN sets only the unordered bit: bf LT and bf GT both branch, ending in fmr f0,f1.
        # 8f0e048's clamp_rate (and its witness text "NaN -> 0.25") says 0.25. NaN is not reachable from the
        # three handlers (1.0 start, finite x/÷ 1.25 steps, reset to 1.0); this is a model error, not a game path.
        for entry in (0x127c48, 0x127c88):
            self.assertTrue(math.isnan(self.run_clamp(entry, float('nan'))))

    def test_handlers_reach_the_rate_routines(self):
        for handler, routine in ((0x11315c, 0x127c88), (0x113184, 0x127c48), (0x1131ac, 0x127c40)):
            self.assertEqual((32, 3, 2, -0x75d8), fields(self.w(handler + 4)), 'r3 = clock object (TOC -0x75d8)')
            call = handler + (0x10 if handler != 0x1131ac else 0x14)
            self.assertEqual(routine, self.bl_target(call))

    def test_hold_divisor_step_and_call_sites(self):
        self.assertEqual((14, 31, 4, 0), fields(self.w(0x10ec68)), 'r31 = n (addi r31,r4,0)')
        self.assertEqual((14, 0, 0, 1000), fields(self.w(0x10ec90)), 'li r0,1000')
        word = self.w(0x10ec98)
        self.assertEqual((31, 3, 0, 31, 459), (word >> 26, (word >> 21) & 31, (word >> 16) & 31, (word >> 11) & 31,
                                              xo(word) & 0x1ff), 'divwu r3,r0,r31')
        self.assertEqual((36, 3, 30, 0x34), fields(self.w(0x10eca0)), 'step stored at +0x34')
        self.assertEqual((32, 0, 3, 0x2c), fields(self.w(0x10ec7c)), 'already-held test reads +0x2c')
        self.assertEqual((16, 4, 2), fields(self.w(0x10ec84))[:3], 'bne: held -> return')
        self.assertEqual(1000 // 32, 31)
        # Held step: unless the paused getter returns non-zero, +0x30 += +0x34.
        self.assertEqual(0x117d6c, self.bl_target(0x10ed24))
        self.assertEqual((16, 4, 2), fields(self.w(0x10ed2c))[:3], 'bne: paused -> skip')
        self.assertEqual([(32, 3, 31, 0x30), (32, 0, 31, 0x34), (36, 0, 31, 0x30)],
                         [fields(self.w(a)) for a in (0x10ed30, 0x10ed34, 0x10ed3c)])
        self.assertEqual(0x10ed10, self.bl_target(0x1104d8), 'main-loop pass calls the held step')
        for li, call in ((0x1c0b34, 0x1c0b64), (0x1c2298, 0x1c229c)):
            self.assertEqual((14, 4, 0, 32), fields(self.w(li)), f'n = 32 before {call:#x}')
            self.assertEqual(0x10ec60, self.bl_target(call))

    def test_record_flag_0x10000000_writer(self):
        oris = [o for o in range(0, len(self.code), 4)
                if self.w(o) >> 26 == 25 and self.w(o) & 0xffff == 0x1000]
        addis = [o for o in range(0, len(self.code), 4)
                 if self.w(o) >> 26 == 15 and self.w(o) & 0xffff == 0x1000]
        self.assertEqual([0x31d68, 0x55b10, 0x58d38, 0x19aef0], oris)
        self.assertEqual([], addis)
        # 0x19aef0: r8 = selected record +4; flags word 0 |= 0x10000000, gated by entry flags & r9.
        self.assertEqual((32, 8, 3, 4), fields(self.w(0x19aee8)))
        self.assertEqual((32, 0, 8, 0), fields(self.w(0x19aeec)))
        self.assertEqual((36, 0, 8, 0), fields(self.w(0x19aef4)))
        self.assertEqual((15, 3, 0, 4), fields(self.w(0x19ae40)), 'lis r3,4')
        self.assertEqual((14, 9, 3, 0x40), fields(self.w(0x19ae44)), 'r9 = 0x40040')
        self.assertEqual(0x19adf0, self.bl_target(0x59274), 'ride loader call')
        # The other three store elsewhere: r24+8, object +0x50, a stack slot.
        self.assertEqual([(36, 0, 24, 8), (36, 0, 31, 0x50), (36, 0, 1, 0x448)],
                         [fields(self.w(a + 4)) for a in oris[:3]])

    def test_placement_flags_and_recompute(self):
        self.assertEqual((14, 5, 0, 0x211f), fields(self.w(0x59f64)))
        self.assertEqual((24, 5, 5, 0x200), fields(self.w(0x59f6c)), 'placement flag 0x800 adds 0x200')
        word = self.w(0x543f4)  # rlwinm r26,r5,0,21,19: every bit but 0x800
        self.assertEqual((21, 5, 26, 0, 21, 19), (word >> 26, (word >> 21) & 31, (word >> 16) & 31,
                                                  (word >> 11) & 31, (word >> 6) & 31, (word >> 1) & 31))
        self.assertEqual(0x53004, self.bl_target(0x54408))
        self.assertEqual((25, 0, 0, 1), fields(self.w(0x5a48c)), 'records |= 0x00010000')
        self.assertEqual((14, 4, 4, 160), fields(self.w(0x5a494)), '160-byte stride')
        self.assertEqual((25, 0, 0, 4), fields(self.w(0x5a4a0)), 'header +0x30 |= 0x40000')
        self.assertEqual(0xa78ec, self.bl_target(0x5a4b4))


@unittest.skipUnless(os.environ.get('OPENTPW_SCENARIOS_LANE'), 'OPENTPW_SCENARIOS_LANE not set')
class ScenarioEnvelopeV2(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        sys.path.insert(0, os.environ['OPENTPW_SCENARIOS_LANE'])
        import profile_snapshot
        from test_profile_snapshot import gms
        cls.snap, cls.gms = profile_snapshot, staticmethod(gms)
        if getattr(profile_snapshot, 'ENVELOPE_VERSION', None) != 2:
            raise unittest.SkipTest('not envelope v2 (ffa87c6 or later)')

    def env(self, n=None, policy='mac-partial'):
        raw = self.gms() if n is None else self.gms()[:n]
        return self.snap.to_envelope(self.snap.read_profile_snapshot(raw, policy))

    def refused(self, env):
        with self.assertRaises(ValueError):
            self.snap.from_envelope(env)

    def test_types_versions_and_fields(self):
        e = self.env()
        self.assertEqual(self.snap.read_profile_snapshot(self.gms(), 'mac-partial'), self.snap.from_envelope(e))
        for key, value in (('version', True), ('envelope_version', '2'), ('envelope_version', True),
                           ('envelope_version', 1), ('layout_source', 'x'), ('source', 'x'), ('issues', ['forged'])):
            with self.subTest(key=key, value=value):
                self.refused({**copy.deepcopy(e), key: value})
        for name, value in (('mEarnedGlobalTicket', [True, 0, 2, 0]), ('mSpentTickets', 5.0)):
            m = copy.deepcopy(e)
            m['player'][name] = value
            self.refused(m)
        m = copy.deepcopy(e)
        del m['player']['mExtraKeys']
        self.refused(m)

    def test_json_level(self):
        text = json.dumps(self.env())
        for bad in (text[:-1] + ', "policy": "mac-partial"}',
                    text.replace('"mExtraKeys": ', '"mExtraKeys": 1, "mExtraKeys": ', 1),
                    text.replace('"version": 12', '"version": NaN'),
                    text.replace('"version": 12', '"version": 1e400')):
            with self.assertRaises(ValueError):
                self.snap.loads_envelope(bad)

    def test_partial_cannot_become_complete(self):
        p = self.env(12)
        self.assertEqual('mSpentTickets', p['failed_at'])
        self.refused({**copy.deepcopy(p), 'complete': True, 'failed_at': None, 'failed_offset': None})
        self.refused({**copy.deepcopy(p), 'policy': 'strict-host'})
        self.refused({**copy.deepcopy(p), 'trailing_hex': 'aa'})

    def test_large_repeated_ride_set_is_linear(self):
        e = self.env()
        n = 60000
        snap = self.snap.read_profile_snapshot(self.gms(mystery=[i % 7 for i in range(n)]), 'mac-partial')
        self.assertEqual(n - 7, len([i for i in snap.issues if 'repeats' in i]))
        self.assertEqual(snap, self.snap.loads_envelope(json.dumps(self.snap.to_envelope(snap))))
        self.assertTrue(e)

    @unittest.expectedFailure
    def test_failing_u8_member_cannot_hold_short_read_bytes(self):
        # R18-3: a u8 member has no partial delivery, so failing there leaves the reset value.
        e = self.env(18)
        self.assertEqual('mEasyModeUser', e['failed_at'])
        e['player']['mEasyModeUser'] = 7
        self.refused(e)

    @unittest.expectedFailure
    def test_failing_i32_member_keeps_its_reset_low_byte(self):
        # R18-3: fewer than 4 delivered bytes leave the low-order byte at its reset value (0).
        e = self.env(12)
        e['player']['mSpentTickets'] = 0x12345678
        self.refused(e)


if __name__ == '__main__':
    unittest.main()
