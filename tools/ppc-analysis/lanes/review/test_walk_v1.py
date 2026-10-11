"""WALK-V: independent review of WALK-I (b505706) on c6c19ea.

Static tests read files from the git object store. The mutation shim reruns the M3 mutation suites v3 and v4 on
WALK-I: their position-holding hooks wrote ``Guest.X/Y``, which WALK-I made read-only (derived from the 16.16
``GuestNavigation``), so at b505706 those suites do not compile and every v3/v4 mutation goes unchecked. The shim
holds the guest through ``Navigation.X/Y`` (velocity zeroed) instead; nothing else in the hooks changes.

WALK-F ported the hooks in v3/v4 themselves and retired the shim classes; ``_port`` documents what the shim did.

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
        # WALK-F ported the hooks in v3/v4 themselves (as _port did), so the shim classes below are retired.
        for hooks in (v3.HOOKS_V3, v4.HOOKS_V4):
            self.assertFalse(any('(held.X, held.Y) =' in r for _, r in hooks))
            self.assertTrue(any('held.Navigation.VelocityX' in r for _, r in hooks))

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


# MutationsWalkRound3/4 reran v3/v4 with _port's hooks and three updated pins. WALK-F ported the hooks and the pins in
# test_m3_gate_v3.py / test_m3_gate_v4.py themselves, so running v3 and v4 covers them; _port stays as the record of the shim.


if __name__ == '__main__':
    unittest.main()
