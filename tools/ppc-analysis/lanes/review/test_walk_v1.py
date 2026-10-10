"""WALK-V: independent review of WALK-I (b505706) on c6c19ea.

Static tests read files from the git object store. The mutation shim reruns the M3 mutation suites v3 and v4 on
WALK-I: their position-holding hooks wrote ``Guest.X/Y``, which WALK-I made read-only (derived from the 16.16
``GuestNavigation``), so at b505706 those suites do not compile and every v3/v4 mutation goes unchecked. The shim
holds the guest through ``Navigation.X/Y`` (velocity zeroed) instead; nothing else in the hooks changes.

Mutation runs need ``OPENTPW_M3_MUTATE=1`` and ``OPENTPW_GAME_PATH`` like v1-v4 (``OPENTPW_M3_SUBJECT`` defaults to
``HEAD``). Run: ``python3 -m unittest test_walk_v1``. Nothing here says anything about how the original game behaves.
"""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_m3_gate_v1 as v1  # noqa: E402
import test_m3_gate_v3 as v3  # noqa: E402
import test_m3_gate_v4 as v4  # noqa: E402

SUBJECT = 'b505706'
BASE = 'c6c19ea'
GATE = 'source/OpenTPW/Client/M3Gate.cs'
NAV = 'source/OpenTPW/World/Guests/GuestNavigation.cs'


def _port(hooks: list[tuple[str, str]], prefix: str) -> list[tuple[str, str]]:
    """Hold a guest through its 16.16 navigation state instead of the read-only Guest.X/Y."""
    x, y = f'held{prefix}X', f'held{prefix}Y'
    swaps = [
        (f'private float {x}, {y};', f'private int {x}, {y};'),
        ('walker.X, walker.Y)', 'walker.Navigation.X, walker.Navigation.Y)'),
        (f'(held.X, held.Y) = ({x}, {y});',
         f'(held.Navigation.X, held.Navigation.Y, held.Navigation.VelocityX, held.Navigation.VelocityY) = ({x}, {y}, 0, 0);'),
    ]
    out = []
    for anchor, replacement in hooks:
        for old, new in swaps:
            replacement = replacement.replace(old, new)
        out.append((anchor, replacement))
    return out


def _shimmed(subject: str) -> bool:
    return 'public float X => Navigation.X' in v1.show('source/OpenTPW/World/Guests/Guest.cs', subject)


@unittest.skipUnless(v1.has_commit(SUBJECT), f'{SUBJECT} not in this repository')
class StaticWalk(unittest.TestCase):
    def test_v3_v4_hooks_write_read_only_guest_position(self):
        # WV-1: the unported hooks assign Guest.X/Y, which is a read-only property at b505706: the suites cannot build.
        guest = v1.show('source/OpenTPW/World/Guests/Guest.cs', SUBJECT)
        self.assertIn('public float X => Navigation.X / (float)Fixed16.One;', guest)
        self.assertTrue(any('(held.X, held.Y) =' in r for _, r in v3.HOOKS_V3))
        self.assertTrue(any('(held.X, held.Y) =' in r for _, r in v4.HOOKS_V4))

    def test_young_head_is_never_judged_by_rule_a(self):
        # WV-2: HeadYoung is decided once, when the guest becomes head, and suppresses rule (a) for its whole streak.
        gate = v1.show(GATE, SUBJECT)
        self.assertIn('state.HeadYoung = tracks.GetValueOrDefault( check.HeadGuest ) is not { } headTrack || '
                      'guests.ParkTurn - headTrack.FirstSeenTurn < MinimumWalkAgeTurns;', gate)
        self.assertIn('if ( streak > state.HeadBound && !state.HeadYoung )', gate)
        self.assertNotIn('HeadYoung', v1.show(GATE, BASE))

    def test_bounds_were_loosened(self):
        # 194 / 50 -> 281 / 65 head-not-ready; W(0) head check gains up to N x 9 + 15 = 240 turns at N = 25.
        def old(n):
            import math
            cells = min(2 * max(1, n) + 2, n + 4)
            return math.ceil(cells / 0.7 * 1000 / 248) + 26

        def new(n):
            return max(1, n) * 9 + 30 + 26
        self.assertEqual((194, 50, 281, 65), (old(25), old(1), new(25), new(1)))
        self.assertEqual(240, 25 * 9 + 15)

    def test_gate_derivation_carries_no_approx_marker(self):
        # WV-4: the bound rests on WALK-002..004 (and the zero separation of WALK-003) but its derivation text no longer
        # names an approximation; the GATE register lost GATE-004/005 without a successor.
        gate = v1.show(GATE, SUBJECT)
        body = gate[gate.index('internal static (long Turns, string Derivation) HeadNotReadyBound'):]
        body = body[:body.index('\n\t}\n')]
        self.assertNotIn('[APPROX:', body)
        approx = v1.show('source/OpenTPW/Client/M3GateApproximations.cs', SUBJECT)
        self.assertNotIn('GATE-004', approx)
        self.assertNotIn('GATE-005', approx)

    def test_separation_force_is_constant_zero_in_production(self):
        # WV-5: production never passes opposeSeparation, so the 0.1 weighting multiplies a constant (0, 0).
        sim = v1.show('source/OpenTPW/World/Guests/GuestSimulation.cs', SUBJECT)
        self.assertIn('navigation.Step( guest.MaxSpeed, guest.MaxForce, isOpenForStep )', sim)
        self.assertIn('0xfd8a8', v1.show(NAV, SUBJECT))

    def test_schema_six_is_claimed_by_three_branches(self):
        # WV-7: WALK-I, ANIM-I (b84bf58) and COAST-I (02712e0) all bump SchemaVersion 5 -> 6 with different pins.
        pins = {}
        for rev in (SUBJECT, 'b84bf58', '02712e0'):
            if not v1.has_commit(rev):
                self.skipTest(rev)
            self.assertIn('public const int SchemaVersion = 6;', v1.show('source/OpenTPW/World/WorldStateHash.cs', rev))
            pins[rev] = v1.show('source/OpenTPW.Tests/DeterminismTests.cs', rev)
        self.assertEqual(3, len(set(pins.values())))


class MutationsWalkRound3(v3.MutationsRound3):
    """v3's mutations with ported hooks."""

    @classmethod
    def setUpClass(cls):
        saved = v3.HOOKS_V3
        if v1.has_commit(v1.MUTATION_SUBJECT) and _shimmed(v1.MUTATION_SUBJECT):
            v3.HOOKS_V3 = _port(saved, '')
        try:
            super().setUpClass()
        finally:
            v3.HOOKS_V3 = saved

    # Three v3 expectations pin pre-WALK-I numbers; at b505706 they read as follows (review run, WALK-V).
    def test_gate_upd_baseline(self):
        # v3 pins W_max 8021 = W(99); WALK-I adds J(99) = 24, so 8045. Everything else in the baseline is unchanged.
        rows = self.rows('', determinism=True)
        self.assertEqual([], sorted(key for key, row in rows.items() if row['verdict'] != 'pass'))
        bound = rows['queues.no-stuck-queue']['evidence']['attraction']['waitBound']
        cap, h, r, qmax = bound['capacity'], bound['latencyTurns'], bound['holdTurns'], bound['qmax']
        self.assertEqual((20, 15, 9), (bound['standWalkTurns'], bound['moveUpWalkTurns'], bound['queueCellWalkTurns']))
        self.assertEqual('240 / 24', bound['joinWalkTurnsAtHeadAndQmax'])
        self.assertEqual(24 + qmax * h + ((qmax - 1) // cap + 1) * r + 1, bound['wMaxTurns'])
        self.assertEqual(8045, bound['wMaxTurns'])
        self.assertEqual(-310, bound['closestToBoundTurns'])  # was -69 at c6c19ea: J(p) moved every bound up

    def test_head_bound_is_n_plus_four_cells(self):
        # WV-3: 194 / 50 / 50 -> 281 / 65 / 65. A head held 250 turns is no longer caught by rule (a) (259 <= 281); the
        # row still fails, but only through the BOUNCE head wait bound W(0) (179 > 178), which shops and toilets lack.
        baseline = self.rows('')['queues.no-stuck-queue']['evidence']
        self.assertEqual((281, 65, 65), (baseline['attraction']['headNotReadyBoundTurns'], baseline['shop']['headNotReadyBoundTurns'],
                                         baseline['toilet']['headNotReadyBoundTurns']))
        row = self.rows('head-held-250-turns')['queues.no-stuck-queue']
        attraction = row['evidence']['attraction']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual(0, attraction['headNotReadyViolations'])
        self.assertGreater(attraction['headNotReadyMaxTurns'], 194)
        self.assertLessEqual(attraction['headNotReadyMaxTurns'], 281)
        self.assertIn('after becoming head > bound', row['evidence']['firstViolation'])

    def test_slowdowns_within_the_loose_bound_pass(self):
        # hold-slots-longer passed at c6c19ea; at b505706 it fails the head check by one turn (179 > 178): stricter.
        row = self.rows('hold-slots-longer')['queues.no-stuck-queue']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('179 turns after becoming head > bound 178', row['evidence']['firstViolation'])
        self.assertEqual('pass', self.rows('head-delayed-60-turns')['queues.no-stuck-queue']['verdict'])


class MutationsWalkRound4(v4.MutationsRound4):
    """v4's mutations with ported hooks (v4 instruments on top of v3)."""

    @classmethod
    def setUpClass(cls):
        saved3, saved4 = v3.HOOKS_V3, v4.HOOKS_V4
        if v1.has_commit(v1.MUTATION_SUBJECT) and _shimmed(v1.MUTATION_SUBJECT):
            v3.HOOKS_V3, v4.HOOKS_V4 = _port(saved3, ''), _port(saved4, 'Stand')
        try:
            super().setUpClass()
        finally:
            v3.HOOKS_V3, v4.HOOKS_V4 = saved3, saved4



if __name__ == '__main__':
    unittest.main()
