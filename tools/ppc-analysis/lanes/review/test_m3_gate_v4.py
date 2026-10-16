"""GATE-V4 (round 4, M3 acceptance candidate 33f8584): mutation checks for the BOARD/WALK rules of the queue row.

Builds on the v1/v2/v3 harness (same fault hooks, plus the ones below). Each new rule of the queue row gets a fault
that only that rule can catch, and the boundary of the head bound W(0) = H + R + 1 is bracketed:

- ``hold-stand-walk``: the first guest of the ride in OpenTPW's Boarding (state 13) from tick 18,000 is pinned for 30
  turns at the centre of the queue's front cell, one cell back from the stand point: walk-stall on w = 20 (v3
  ``hold-move-up`` covers w2 only). WALK-F2 (REVIEW-ORACLE-2 OV2-5): in the 6-cell layout a guest enters state 13
  about 0.01 cells from its stand point, so pinning it where it stands no longer delays its arrival.
- ``freeze-slots-160`` / ``freeze-slots-190``: on the first turn from tick 18,000 that a boarding fills the last of the
  CAP slots, every slot is set to free exactly 160 / 190 turns later. The new head cannot be called before then, so it
  waits about that long as head: 160 stays under W(0) = 177 (PASS), 190 exceeds it (FAIL through the head check alone).
- ``never-release-early+no-head-check``: BOUNCE slots are never released from tick 1 and the head check is disabled:
  the per-guest age check (each queued guest against W(p) for its join position) fails the row on its own.
- ``release-at-1000+no-head-check+no-age-check``: slots are held until park turn 1,000, then released; the head and age
  checks are disabled: the completed-wait check fails the row on its own.
- ``corner-front``: the queue route turns at the front (the cell behind the front is beside it, not in line with the
  entrance): WALK-plan W3 fails, no wait is judged, and the queue row and the exit code are UNRESOLVED / 2, not 0.
- determinism coverage: ``diverge-second-run-cellmap`` (the second in-process run bumps a placement counter of the
  cell map at tick 2,000) and ``diverge-second-run-energy`` (the oldest guest's energy - 1 at tick 2,000 of the second
  run) are not in the minute hash, which pins the coverage gap; ``strict-hash`` adds the raw guest hash, the cell
  map and every bridge's queue, called guest and BOUNCE slots to every minute hash, still passes on the baseline,
  and catches ``strict-hash+diverge-second-run-cellmap``.

Static tests read ``OPENTPW_M3_SUBJECT`` (default ``HEAD``); mutation tests need ``OPENTPW_M3_MUTATE=1`` and
``OPENTPW_GAME_PATH``. Nothing here says anything about how the original game behaves.
"""
from __future__ import annotations

import math
import re
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_m3_gate_v1 as v1  # noqa: E402
import test_m3_gate_v3 as v3  # noqa: E402

SUBJECT = v1.MUTATION_SUBJECT

HOOKS_V4 = [
    ('\t\t\tMutateV3( tick );\n',
     '\t\t\tMutateV3( tick );\n\t\t\tMutateV4( tick );\n'),
    # Sub-rule isolation: disable the head check (3) or the per-guest age check (2) of CheckBoardingAges.
    ('\t\t\tif ( asHead > bound && !Excluded( state.BoardingHeadSince ) && state.ReportedAges.Add( -head ) )\n',
     '\t\t\tif ( !Mutation.Contains( "no-head-check" ) && asHead > bound && !Excluded( state.BoardingHeadSince ) && state.ReportedAges.Add( -head ) )\n'),
    ('\t\t\tif ( age > bound && !Excluded( now - age ) && state.ReportedAges.Add( id ) )\n',
     '\t\t\tif ( !Mutation.Contains( "no-age-check" ) && age > bound && !Excluded( now - age ) && state.ReportedAges.Add( id ) )\n'),
    # corner-front: the second queue cell must turn at the front instead of continuing the line.
    ('(cell == front && (dx, dy) != back)',
     '(cell == front && (Mutation == "corner-front" ? (dx, dy) == back || (dx, dy) == (-back.DX, -back.DY) : (dx, dy) != back))'),
    # strict-hash: every minute hash and the final gate hash also cover the raw guest hash, the cell map and the
    # bridges' queue state.
    ('\t\treturn hash;\n\t}\n\n\tprivate void AddRow(',
     '\t\tif ( Mutation.StartsWith( "strict-hash" ) )\n\t\t{\n'
     '\t\t\tAdd( (long)guests.ComputeStateHash() );\n'
     '\t\t\tvar cells = grid.Cells;\n'
     '\t\t\tfor ( var y = 0; y < cells.Height; y++ )\n'
     '\t\t\t\tfor ( var x = 0; x < cells.Width; x++ )\n'
     '\t\t\t\t\tAdd( cells.RawTypeAt( x, y ) | (long)cells.FlagsAt( x, y ) << 8 | (long)cells.LinksAt( x, y ) << 24 | (long)cells.QueueLinkAt( x, y ) << 32 | (long)(ushort)cells.PlacementCountAt( x, y ) << 40 );\n'
     '\t\t\tforeach ( var item in placed )\n\t\t\t{\n'
     '\t\t\t\tvar bridge = item.Runtime.Visitors;\n'
     '\t\t\t\tAdd( bridge.Queue.Count );\n'
     '\t\t\t\tforeach ( var id in bridge.Queue )\n\t\t\t\t\tAdd( id );\n'
     '\t\t\t\tAdd( bridge.CalledGuest );\n'
     '\t\t\t\tvar slots = (List<(int Guest, double Until, double Start)>)typeof( RideVisitorBridge ).GetField( "bouncing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!.GetValue( bridge )!;\n'
     '\t\t\t\tforeach ( var (slotGuest, until, _) in slots )\n\t\t\t\t{\n\t\t\t\t\tAdd( slotGuest );\n\t\t\t\t\tAdd( BitConverter.DoubleToInt64Bits( until ) );\n\t\t\t\t}\n'
     '\t\t\t}\n'
     '\t\t}\n'
     '\t\treturn hash;\n\t}\n\n\tprivate void AddRow('),
    ('\tprivate string AttractionName( int id )',
     '\tprivate int heldStandWalker;\n\tprivate long heldStandTurn;\n\tprivate int heldStandX, heldStandY;\n'
     '\tprivate bool slotsFrozen;\n\tprivate int previousSlotCount = -1;\n'
     '\tprivate void MutateV4( long tick )\n\t{\n'
     '\t\tvar ride = placed.FirstOrDefault( item => item.Role == "attraction" );\n'
     '\t\tif ( ride == null )\n\t\t\treturn;\n'
     '\t\tvar visitors = ride.Runtime.Visitors;\n'
     '\t\tvar slots = (List<(int Guest, double Until, double Start)>)typeof( RideVisitorBridge ).GetField( "bouncing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!.GetValue( visitors )!;\n'
     '\t\tvar now = ride.Runtime.Script!.TimeMilliseconds;\n'
     '\t\tif ( Mutation == "hold-stand-walk" && tick >= 18000 )\n\t\t{\n'
     '\t\t\tif ( heldStandWalker == 0 && guests.Guests.FirstOrDefault( guest => guest.AttractionId == visitors.AttractionId && guest.State == GuestState.Boarding ) is { } walker )\n'
     '\t\t\t\t(heldStandWalker, heldStandTurn, heldStandX, heldStandY) = (walker.Id, guests.ParkTurn, visitors.QueueCells[0].X * Fixed16.One + Fixed16.One / 2, visitors.QueueCells[0].Y * Fixed16.One + Fixed16.One / 2);\n'
     '\t\t\tif ( heldStandWalker != 0 && guests.ParkTurn < heldStandTurn + 30 && guests.Find( heldStandWalker ) is { State: GuestState.Boarding } held )\n'
     '\t\t\t\t(held.Navigation.X, held.Navigation.Y, held.Navigation.VelocityX, held.Navigation.VelocityY) = (heldStandX, heldStandY, 0, 0);\n'
     '\t\t}\n'
     '\t\tif ( Mutation is "freeze-slots-160" or "freeze-slots-190" && tick >= 18000 && !slotsFrozen )\n\t\t{\n'
     '\t\t\tvar capacity = ride.Runtime.GetVariable( RideVariables.VAR_CAPACITY );\n'
     '\t\t\tvar occupied = slots.Count( slot => slot.Guest != 0 );\n'
     '\t\t\tif ( occupied == capacity && previousSlotCount == capacity - 1 )\n\t\t\t{\n'
     '\t\t\t\tvar turns = Mutation == "freeze-slots-160" ? 160 : 190;\n'
     '\t\t\t\tfor ( var index = 0; index < slots.Count; index++ )\n'
     '\t\t\t\t\tslots[index] = slots[index] with { Until = now + turns * (double)ParkCalendar.TurnMilliseconds };\n'
     '\t\t\t\tslotsFrozen = true;\n'
     '\t\t\t}\n'
     '\t\t\tpreviousSlotCount = occupied;\n'
     '\t\t}\n'
     '\t\tif ( Mutation.StartsWith( "never-release-early" ) )\n'
     '\t\t\tfor ( var index = 0; index < slots.Count; index++ )\n'
     '\t\t\t\tslots[index] = slots[index] with { Until = 1e12 };\n'
     '\t\tif ( Mutation.StartsWith( "release-at-1000" ) )\n'
     '\t\t\tfor ( var index = 0; index < slots.Count; index++ )\n'
     '\t\t\t\tslots[index] = slots[index] with { Until = guests.ParkTurn < 1000 ? 1e12 : slots[index].Until >= 1e11 ? now : slots[index].Until };\n'
     '\t\tif ( Mutation.EndsWith( "diverge-second-run-cellmap" ) && SecondRun && tick == 2000 )\n'
     '\t\t\tgrid.Cells.SetPlacementCount( 0, 0, (short)(grid.Cells.PlacementCountAt( 0, 0 ) + 1) );\n'
     '\t\tif ( Mutation == "diverge-second-run-energy" && SecondRun && tick == 2000 && guests.Guests.Count > 0 )\n'
     '\t\t\tguests.Guests[0].Energy = Math.Max( 0, guests.Guests[0].Energy - 1 );\n'
     '\t}\n\n'
     '\tprivate string AttractionName( int id )'),
]


def instrument(text: str) -> str:
    text = v3.instrument(text)
    for anchor, replacement in HOOKS_V4:
        if text.count(anchor) != 1:
            raise AssertionError(f'v4 hook anchor not unique/absent: {anchor[:60]!r}')
        text = text.replace(anchor, replacement)
    return text


def bounce_hold_turns(duration: int, period: int = 4, turn_ms: int = 248) -> int:
    """BOARD-plan section 5 / UNBOUNCE 0xae020: first release poll P*j + 1 turns after the BOUNCE with
    deadline < now and (now - start) mod 1000 < 200 (decoded again in this round)."""
    for poll in range(100_000):
        turns = period * poll + 1
        elapsed = turns * turn_ms
        if elapsed > 1000 * duration and elapsed % 1000 // 200 == 0:
            return turns
    raise AssertionError(duration)


class Arithmetic(unittest.TestCase):
    """Round 4 recomputation of every bound the queue row uses, from the cited rules (no assets)."""

    def test_terms(self):
        # loop WAIT 500 ms divided by jungle Bouncy's script speed 0.5 + 60/100 (WAIT 0xb0658): trunc(454.5) = 454 ms
        period = 1 + math.ceil(int(500 / 1.1) / 248)
        host = 1 + (int(1.2 * 2) + 1) + (10 + 1) + 1 + 1 + period  # removal, move-up, interlude, call, notice, P
        self.assertEqual((3, 20), (period, host))
        h = host + 20 + 15
        r = bounce_hold_turns(30)
        self.assertEqual((55, 121), (h, r))
        self.assertEqual(177, h + r + 1)                        # W(0), the head check
        self.assertEqual(272, 5 * h + r + 1 - 31 * 1000 // 248)  # tau_max (31 000 / 248 = 125 exactly)
        self.assertAlmostEqual(67.456, 272 * 0.248, places=3)
        w_max = 100 * h + (99 // 5 + 1) * r + 1
        self.assertEqual((7921, 1964.408), (w_max, round(w_max * 0.248, 3)))

    def test_hold_rule_versus_closed_form(self):
        # 4 DUR + 1 holds for 7 <= DUR <= 30 (DUR 7 gives 29 = 4 * 7 + 1 by coincidence); DUR <= 6 gives 29; 31 jumps.
        for duration in range(1, 31):
            expected = 29 if duration <= 7 else 4 * duration + 1
            self.assertEqual(expected, bounce_hold_turns(duration), duration)
        self.assertEqual(529, bounce_hold_turns(31))

    def test_head_not_ready_bound_uses_the_opentpw_walk_speed(self):
        # At review: min(2 max(1, N) + 2, N + 4) cells at WalkSpeedCellsPerSecond 1.0 x 0.7, plus 3 + 22 + 1.
        def bound(cells_n, speed=1.0):
            cells = min(2 * max(1, cells_n) + 2, cells_n + 4)
            return math.ceil(cells / (speed * 0.7) * 1000 / 248) + 3 + 22 + 1
        self.assertEqual((194, 50), (bound(25), bound(1)))
        # At the traced floor (WALK-plan section 4: 0.12 cell per turn at s = 0.6, i.e. 0.484 cell/s) it would be larger:
        # the 194 turns were OpenTPW timing, not an original-derived bound (GATE-004).
        self.assertGreater(bound(25, speed=0.12 / 0.248 / 0.7), 194)

    def test_head_not_ready_bound_after_walk_i(self):
        # WALK-I (WALK-plan section 15) replaced the walk term: two queue walks over N cells, N x C + 2 x w2 with the
        # enumerated per-cell term C and w2 = 15, plus the same 3 + 22 + 1. C was 9 at WALK-I and is 27 since the original walk
        # (GATE-006 re-enumerated over 0x510100); the traced floor stays inside C: one cell at 0.118 cell per update takes 8.47 updates.
        def bound(cells_n):
            return max(1, cells_n) * 27 + 2 * 15 + 3 + 22 + 1
        self.assertEqual((731, 83), (bound(25), bound(1)))
        self.assertEqual(9, math.ceil(65536 / int(0.59 * 0.2 * 65536)))


@unittest.skipUnless(v1.has_commit(SUBJECT), f'{SUBJECT} not in this repository')
class StaticRound4(unittest.TestCase):
    def test_head_not_ready_bound_reads_the_opentpw_walk_speed(self):
        # Round 4 pinned the walk speed read (GATE-004); WALK-I retired it for the traced per-cell term, so this now pins
        # that the bound no longer reads an OpenTPW walk speed and uses QueueCellWalkTurns and w2.
        gate = v1.show(v1.GATE, SUBJECT)
        self.assertIn('HeadNotReadyBound( item.Runtime.Visitors.QueueSizeInCells )', gate)
        self.assertNotIn('WalkSpeedCellsPerSecond', gate)
        self.assertNotIn('TiredWalkFactor', gate)
        self.assertIn('var walkTurns = cells * QueueCellWalkTurns + 2 * MoveUpWalkTurns;', gate)
        self.assertIn('internal const long QueueCellWalkTurns = 27;', gate)

    def test_walk_scope_failure_leaves_waits_unjudged(self):
        gate = v1.show(v1.GATE, SUBJECT)
        judge = gate[gate.index('private JsonObject JudgeWaits('):]
        self.assertIn('if ( model.WalkScope.Length > 0 )\n\t\t\t{\n\t\t\t\toutsideWalkScope++;\n\t\t\t\treturn;', judge)
        self.assertIn('violations.Count > 0 ? M3GateVerdict.Fail : judged > 0 ? M3GateVerdict.Pass : M3GateVerdict.Unresolved', gate)

    def test_minute_hash_is_the_gate_hash_only(self):
        # Pins Q4's gap: the minute hashes come from ComputeGateHash, which covers neither the cell map nor the bridges'
        # queue state nor the guests' RNG, energy, nausea, queue position or move delay.
        gate = v1.show(v1.GATE, SUBJECT)
        self.assertIn('MinuteHashes.Add( ComputeGateHash() );', gate)
        body = gate[gate.index('private ulong ComputeGateHash()'):]
        body = body[:body.index('return hash;')]
        for absent in ('ComputeStateHash', 'grid.Cells', '.Queue', 'CalledGuest', 'Energy', 'random'):
            self.assertNotIn(absent, body, absent)


@unittest.skipUnless(v1.MUTATE and v1.GAME and Path(v1.GAME).is_dir() and Path(v1.DOTNET).exists() and v1.has_commit(SUBJECT),
                     'set OPENTPW_M3_MUTATE=1, OPENTPW_GAME_PATH and a .NET 10 SDK (OPENTPW_DOTNET)')
class MutationsRound4(unittest.TestCase):
    """Only the round-4 faults (the inherited v1 tests already run in v1/v2/v3)."""

    @classmethod
    def setUpClass(cls):
        saved = v1.instrument
        v1.instrument = instrument
        try:
            v1.Mutations.results = {}
            v1.Mutations.setUpClass()
        finally:
            v1.instrument = saved
        cls.gate = v1.Mutations

    @classmethod
    def tearDownClass(cls):
        v1.Mutations.tearDownClass()
        v1.Mutations.results = {}

    def rows(self, mutation: str, determinism: bool = False) -> dict:
        result = self.gate.run_gate(mutation, determinism)
        self.assertTrue(result['rows'], result['stderr'] or result['stdout'])
        return result['rows']

    def fails(self, mutation: str, determinism: bool = False) -> list[str]:
        return sorted(key for key, row in self.rows(mutation, determinism).items() if row['verdict'] == 'fail')

    def queue(self, mutation: str) -> dict:
        return self.rows(mutation)['queues.no-stuck-queue']

    def test_baseline_still_passes_with_the_round4_hooks(self):
        self.assertEqual([], self.fails('', True))
        self.assertEqual(0, self.gate.run_gate('', True)['exit'])

    def test_stand_walk_held_beyond_w_fails_walk_stall(self):
        row = self.queue('hold-stand-walk')
        self.assertEqual('fail', row['verdict'], row['evidence'].get('firstViolation'))
        self.assertIn('walk-stall', row['evidence']['firstViolation'])
        self.assertIn('state 13 walk to the stand point for 21 turns > w 20', row['evidence']['firstViolation'])
        self.assertEqual(['queues.no-stuck-queue'], self.fails('hold-stand-walk'))

    def test_slot_freeze_brackets_the_head_bound(self):
        under = self.queue('freeze-slots-160')
        over = self.queue('freeze-slots-190')
        bound = under['evidence']['attraction']['waitBound']
        self.assertEqual(177, bound['headToBoardingBoundTurns'])
        self.assertEqual('pass', under['verdict'], under['evidence'].get('firstViolation'))
        self.assertGreaterEqual(bound['headToBoardingMaxTurns'], 160)
        self.assertLessEqual(bound['headToBoardingMaxTurns'], 177)
        self.assertEqual('fail', over['verdict'])
        self.assertIn('after becoming head > bound 177', over['evidence']['firstViolation'])
        # Rules (a), (b) and walk-stall stay silent: only the head check catches it.
        attraction = over['evidence']['attraction']
        self.assertEqual((0, 0, 0), (attraction['headNotReadyViolations'], attraction['blockedViolations'],
                                     attraction['waitBound']['walkStalls']))

    def test_age_check_alone_fails_a_queue_that_never_releases(self):
        row = self.queue('never-release-early+no-head-check')
        self.assertEqual('fail', row['verdict'], row['evidence'])
        self.assertRegex(row['evidence']['firstViolation'], r'queued \d+ turns from position \d+ > bound')

    def test_completed_wait_check_alone_fails_a_late_release(self):
        row = self.queue('release-at-1000+no-head-check+no-age-check')
        self.assertEqual('fail', row['verdict'], row['evidence'])
        self.assertRegex(row['evidence']['firstViolation'], r' wait \d+ turns from position \d+ > bound')

    def test_corner_front_is_not_judged_and_cannot_exit_zero(self):
        result = self.gate.run_gate('corner-front', True)
        rows = result['rows']
        bound = rows['queues.no-stuck-queue']['evidence']['attraction']['waitBound']
        self.assertIn('front segment not straight', bound['walkScope'])
        self.assertEqual(0, bound['judgedWaits'] + bound['judgedStillQueued'])
        self.assertGreater(bound['notJudgedWalkScope'], 0)
        self.assertEqual('unresolved', rows['queues.no-stuck-queue']['verdict'])
        self.assertEqual('pass', rows['build.queue']['verdict'])
        self.assertEqual(2, result['exit'])

    def test_minute_hash_misses_cell_map_and_energy_divergence(self):
        # Pins Q4's gap: a second run whose cell map or a guest's energy diverges still passes the row.
        for mutation in ('diverge-second-run-cellmap', 'diverge-second-run-energy'):
            row = self.rows(mutation, True)['determinism.same-seed']
            self.assertEqual('pass', row['verdict'], (mutation, row['evidence']))

    def test_strict_minute_hash_passes_the_baseline_and_catches_the_cell_map(self):
        self.assertEqual('pass', self.rows('strict-hash', True)['determinism.same-seed']['verdict'])
        row = self.rows('strict-hash+diverge-second-run-cellmap', True)['determinism.same-seed']
        self.assertEqual('fail', row['verdict'])
        self.assertEqual(1, row['evidence']['firstDivergentMinute'])


if __name__ == '__main__':
    unittest.main()
