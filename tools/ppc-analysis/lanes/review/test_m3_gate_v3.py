"""GATE-UPD: mutation checks for the queue rows and the stricter staff.work of the M3 gate.

Round 3 builds on the v1/v2 harness (test_m3_gate_v1.py, test_m3_gate_v2.py): the same fault-injection hooks, plus:

- build.paths is built through PATH-I's ParkPathBuilder; cells written around the builder (``paths-direct-write``)
  or a builder without the economy (``paths-no-charge``) fail the row.
- build.queue is built through the queue tool's code (Level.BuildQueueCell) and charged; a queue laid with no
  economy (``queue-bypass-economy``) fails the row.
- queues.no-stuck-queue fails on derived progress violations only (docs/M3-GATE.md, "Queue progress"): a ride that
  stops taking guests (v1 ``block-boarding``) and a head that is never ready to be called (``never-called-head``)
  fail it; one blocked admission evaluation that recovers (``stall-one-update``) does not.
- staff.work: a mechanic or handyman that works once and then idles (v2 ``idle-mechanic-after-first-repair``,
  ``idle-handyman-after-first-clean``) now fails, through the repair and litter windows.

Round 3 review (GATE-V3) replaced the layout literals (14 path cells, 25 queue cells, $280) by the rules they come
from, and added:

- ``queue-one-short`` (the last queue cell is not laid) fails build.queue;
- ``cut-target-transient`` (v1 hook, the join cell cut for 600 ticks) fails paths.no-unreachable-goal: the per-tick
  check follows the guests' real target; ``cut-exit-transient`` pins what a transient exit cut does;
- ``never-ready-head-blips`` pinned a residual gap: a head held unready forever, with one blocked evaluation every
  300 turns, restarted the per-head streak and kept the queue row UNRESOLVED instead of FAIL.

GATE-FIX2 (review round 3, S1 to S4) flips the pins and adds:

- S1: the head bound walks min(2N + 2, N + 4) cells (194 turns for 25 cells, was 326); ``head-held-250-turns``
  (one head held unready for 250 turns, then released) fails the queue row (it stayed UNRESOLVED under 326);
- S2: the per-head streak counts every evaluation the head is not at position 0, gates held or not, so
  ``never-ready-head-blips`` fails, and so does ``never-ready-head-blips-150`` (a blocked evaluation every 150
  turns, below the 194-turn bound, so only the S2 counting can catch it);
- S3: the end-of-run reachability check includes the queue's join cell, so ``cut-spine-start`` (entrance and exit
  reachable, join cell not) lists Belly Bounce;
- S4 (in test_m3_gate_v1.py): a second run that diverges early and converges by the end fails determinism.

The BOARD gate (docs/reverse/BOARD-plan.md section 8) judges Belly Bounce's waits against
(p + 1) H + (floor(p / CAP) + 1) R + 1 turns, every completed wait, every queued guest's age at each evaluation, and
the head from the turn it became head (p = 0). The baseline passes (exit 0). Slots held about 5R
(``hold-slots-much-longer``) or never released (``never-release``), and heads held 170 turns
(``head-delayed-170-turns``), fail the row through the wait bound alone. Slots held about 2R and heads held 60 turns
stay within the loose bound and pass (pinned).

WALK-F (review WALK-V, WV-1) ported the position hooks to the 16.16 ``Navigation.X/Y`` (velocity zeroed) and the slot
hooks to ORACLE-F's (guest, deadline, start) BOUNCE slots, and moved the pins that moved: the merged head bound is
110 / 65 / 65 turns ([APPROX:GATE-006]); rule (a) holds each head to its own remaining walk + 26, so ``head-held-250-turns``,
``head-delayed-170-turns`` and ``head-delayed-60-turns`` fail it; ``hold-slots-longer`` fails the head's wait bound by one
turn since WALK-I; ``cut-exit-transient`` fails paths.no-unreachable-goal on ORACLE-F's layout.

Static tests read ``OPENTPW_M3_SUBJECT`` (default ``HEAD``). Mutation tests run only with ``OPENTPW_M3_MUTATE=1``
and ``OPENTPW_GAME_PATH``. Nothing here says anything about how the original game behaves.
"""
from __future__ import annotations

import re
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_m3_gate_v1 as v1  # noqa: E402
import test_m3_gate_v2 as v2  # noqa: E402

SUBJECT = v1.MUTATION_SUBJECT
PATH_CELL = 20  # Costs.PathCell (Standard.sam), as charged by ParkPathBuilder
BASE = '025d410'

HOOKS_V3 = [
    ('\t\t\tMutateV2( tick, ticks );\n',
     '\t\t\tMutateV2( tick, ticks );\n\t\t\tMutateV3( tick );\n'),
    ('paths = new ParkPathBuilder( grid.Cells, grid, economy, buildGrid, ',
     'paths = new ParkPathBuilder( grid.Cells, grid, Mutation == "paths-no-charge" ? null : economy, buildGrid, '),
    ('\t\t\tvar segment = paths.BuildSegment( start, end );\n',
     '\t\t\tvar segment = Mutation == "paths-direct-write" ? DirectWrite( start, end ) : paths.BuildSegment( start, end );\n'),
    ('\t\t\tspine.AddRange( segment.Built );\n',
     '\t\t\tspine.AddRange( Mutation == "paths-direct-write" ? directCells : segment.Built );\n'),
    ('Level.BuildQueueCell( grid, economy, bridge, ',
     'Level.BuildQueueCell( grid, Mutation == "queue-bypass-economy" ? null : economy, bridge, '),
    # GATE-V3: faults for the rows no earlier mutation could fail.
    ('entry.SettingsName is "Gates" or "Lights" or "Bus" ) )\n',
     'entry.SettingsName is "Gates" or "Lights" or "Bus" && !(Mutation == "no-gates" && entry.SettingsName == "Gates") ) )\n'),
    ('\t\t\tvar purchase = economy.TryBuild( entry.InfoId, out var bought );\n',
     # The purchase goes through (money spent) but is reported refused, so the object is not placed.
     '\t\t\tvar purchase = economy.TryBuild( entry.InfoId, out var bought );\n'
     '\t\t\tif ( Mutation == $"unbuyable-{role}" )\n\t\t\t\tpurchase = ParkEconomy.PurchaseResult.UnknownObject;\n'),
    ('\t\tforeach ( var (x, y) in queueRoute )\n',
     '\t\tforeach ( var (x, y) in Mutation == "queue-one-short" ? queueRoute.Take( queueRoute.Count - 1 ) : queueRoute )\n'),
    ('\tprivate string AttractionName( int id )',
     # The pre-PATH-I stand-in: cells charged and written around the builder, which therefore reports nothing built.
     '\tprivate readonly List<(int X, int Y)> directCells = new();\n'
     '\tprivate SegmentResult DirectWrite( (int X, int Y) start, (int X, int Y) end )\n\t{\n'
     '\t\tforeach ( var (x, y) in ParkPathBuilder.LayLine( start, end ).Skip( 1 ) )\n\t\t{\n'
     '\t\t\teconomy.TryBuyCells( CellPurchase.Path, 1 );\n\t\t\tgrid.SetPath( x, y, true );\n\t\t\tdirectCells.Add( (x, y) );\n\t\t}\n'
     '\t\treturn new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = start };\n\t}\n'
     '\tprivate long stallTurn = -1;\n'
     '\tprivate long heldFromTurn = -1;\n'
     '\tprivate int heldWalker;\n\tprivate long heldWalkerTurn;\n\tprivate int heldX, heldY;\n'
     '\tprivate readonly HashSet<int> extendedSlots = new();\n\tprivate int delayedHead;\n\tprivate long delayedSince;\n'
     '\tprivate void MutateV3( long tick )\n\t{\n'
     '\t\tvar ride = placed.FirstOrDefault( item => item.Role == "attraction" );\n'
     '\t\tvar shop = placed.FirstOrDefault( item => item.Role == "shop" );\n'
     '\t\tif ( Mutation == "never-called-head" && tick >= 18000 && ride!.Runtime.Visitors.Queue.Count > 0 && guests.Find( ride.Runtime.Visitors.Queue[0] ) is { } head )\n'
     '\t\t{\n\t\t\thead.InQueueInterlude = true;\n\t\t\thead.InterludeTurn = guests.ParkTurn;\n\t\t}\n'
     # GATE-V3: the never-called head, plus VAR_LETMEON = -1 for one evaluation every 300 turns (below the old 326-turn
     # bound). GATE-FIX2: never-ready-head-blips-150 blips every 150 turns, below the 194-turn bound (S1 + S2).
     '\t\tif ( Mutation is "never-ready-head-blips" or "never-ready-head-blips-150" && ride != null && tick >= 18000 )\n\t\t{\n'
     '\t\t\tif ( ride.Runtime.Visitors.Queue.Count > 0 && guests.Find( ride.Runtime.Visitors.Queue[0] ) is { } held )\n'
     '\t\t\t{\n\t\t\t\theld.InQueueInterlude = true;\n\t\t\t\theld.InterludeTurn = guests.ParkTurn;\n\t\t\t}\n'
     '\t\t\tif ( ParkCalendar.Turn( guests.TickCount + 1 ) != guests.ParkTurn && (guests.ParkTurn + 1) % (Mutation == "never-ready-head-blips" ? 300 : 150) == 0 && ride.Runtime.GetVariable( RideVariables.VAR_LETMEON ) == 0 )\n'
     '\t\t\t\tride.Runtime.SetVariable( "VAR_LETMEON", -1 );\n'
     '\t\t\telse if ( ride.Runtime.GetVariable( RideVariables.VAR_LETMEON ) == -1 )\n'
     '\t\t\t\tride.Runtime.SetVariable( "VAR_LETMEON", 0 );\n'
     '\t\t}\n'
     # GATE-FIX2 (S1): from tick 18,000 the head is held in an interlude for 250 turns (between the 194-turn bound
     # and the old 326), then released.
     '\t\tif ( Mutation == "head-held-250-turns" && ride != null && tick >= 18000 && ride.Runtime.Visitors.Queue.Count > 0 )\n\t\t{\n'
     '\t\t\tif ( heldFromTurn < 0 )\n\t\t\t\theldFromTurn = guests.ParkTurn;\n'
     '\t\t\tif ( guests.ParkTurn < heldFromTurn + 250 && guests.Find( ride.Runtime.Visitors.Queue[0] ) is { } held )\n'
     '\t\t\t{\n\t\t\t\theld.InQueueInterlude = true;\n\t\t\t\theld.InterludeTurn = guests.ParkTurn;\n\t\t\t}\n'
     '\t\t}\n'
     # BOARD gate: hold-slots-longer keeps every new BOUNCE slot 30 s past its deadline (a hold of about 2R),
     # hold-slots-much-longer 120 s (about 5R), never-release forever; head-delayed-60-turns / -170-turns hold each new
     # head in an interlude for its first 60 / 170 turns as head (more than H = 29 per boarding, below the 194-turn head
     # bound of rule (a), so only the wait bound can catch it).
     '\t\tif ( Mutation is "hold-slots-longer" or "hold-slots-much-longer" or "never-release" && ride != null && tick >= 18000 )\n\t\t{\n'
     '\t\t\tvar slots = (List<(int Guest, double Until, double Start)>)typeof( RideVisitorBridge ).GetField( "bouncing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!.GetValue( ride.Runtime.Visitors )!;\n'
     '\t\t\tfor ( var index = 0; index < slots.Count; index++ )\n'
     '\t\t\t\tif ( slots[index].Guest != 0 && extendedSlots.Add( slots[index].Guest ) )\n'
     '\t\t\t\t\tslots[index] = slots[index] with { Until = slots[index].Until + (Mutation == "hold-slots-longer" ? 30_000 : Mutation == "hold-slots-much-longer" ? 120_000 : 1e12) };\n'
     '\t\t}\n'
     '\t\tif ( Mutation is "head-delayed-60-turns" or "head-delayed-170-turns" && ride != null && tick >= 18000 && ride.Runtime.Visitors.Queue.Count > 0 )\n\t\t{\n'
     '\t\t\tif ( ride.Runtime.Visitors.Queue[0] != delayedHead )\n\t\t\t\t(delayedHead, delayedSince) = (ride.Runtime.Visitors.Queue[0], guests.ParkTurn);\n'
     '\t\t\tif ( guests.ParkTurn < delayedSince + (Mutation == "head-delayed-60-turns" ? 60 : 170) && guests.Find( delayedHead ) is { } delayed )\n'
     '\t\t\t{\n\t\t\t\tdelayed.InQueueInterlude = true;\n\t\t\t\tdelayed.InterludeTurn = guests.ParkTurn;\n\t\t\t}\n'
     '\t\t}\n'
     # WALK gate: hold-move-up freezes the first guest that moves up to slot 0 inside the front cell (from tick 18,000)
     # at its position for 30 turns: over w2 = 15, under the 177-turn head and 194-turn head-not-ready bounds. ANIM-M: a
     # held guest that the ride calls before the 30 turns end (it leaves MovingUpQueue) is let go and the next one is held;
     # with ANIM's opcode timing the first candidate after tick 18,000 is called in its first turn, before any stall.
     '\t\tif ( Mutation == "hold-move-up" && ride != null && tick >= 18000 )\n\t\t{\n'
     '\t\t\tif ( heldWalker != 0 && guests.ParkTurn < heldWalkerTurn + 30 && guests.Find( heldWalker ) is not { State: GuestState.MovingUpQueue } )\n'
     '\t\t\t\theldWalker = 0;\n'
     '\t\t\tif ( heldWalker == 0 && guests.Guests.FirstOrDefault( guest => guest.AttractionId == ride.Runtime.Visitors.AttractionId && guest.State == GuestState.MovingUpQueue && guest.QueuePosition == 0 && guest.QueueCellIndex == 0 ) is { } walker )\n'
     '\t\t\t\t(heldWalker, heldWalkerTurn, heldX, heldY) = (walker.Id, guests.ParkTurn, walker.Navigation.X, walker.Navigation.Y);\n'
     '\t\t\tif ( heldWalker != 0 && guests.ParkTurn < heldWalkerTurn + 30 && guests.Find( heldWalker ) is { State: GuestState.MovingUpQueue } held )\n'
     '\t\t\t\t(held.Navigation.X, held.Navigation.Y, held.Navigation.VelocityX, held.Navigation.VelocityY) = (heldX, heldY, 0, 0);\n'
     '\t\t}\n'
     # GATE-FIX2 (S3): cut the first path cell of the segment (between the park entrance and the queue's join cell) at
     # tick 1,000: the ride's entrance and exit cells stay reachable, its join cell does not.
     '\t\tif ( Mutation == "cut-spine-start" && tick == 1000 )\n\t\t\tgrid.SetPath( spine[0].X, spine[0].Y, false );\n'
     '\t\tif ( Mutation == "close-park" && tick == 1 )\n\t\t\teconomy.ClosePark();\n'
     '\t\tif ( Mutation == "stall-one-update" && shop != null )\n\t\t{\n'
     '\t\t\tvar visitors = shop.Runtime.Visitors;\n'
     # Set on the last tick of a turn, so the next host step is an admission evaluation and sees it before the script runs.
     '\t\t\tif ( stallTurn < 0 && tick >= 18000 && ParkCalendar.Turn( guests.TickCount + 1 ) != guests.ParkTurn && visitors.QueueLength > 0 && visitors.CalledGuest == 0 && shop.Runtime.GetVariable( RideVariables.VAR_LETMEON ) == 0 )\n'
     '\t\t\t{\n\t\t\t\tshop.Runtime.SetVariable( "VAR_LETMEON", -1 );\n\t\t\t\tstallTurn = guests.ParkTurn;\n\t\t\t}\n'
     '\t\t\telse if ( stallTurn >= 0 && guests.ParkTurn > stallTurn && shop.Runtime.GetVariable( RideVariables.VAR_LETMEON ) == -1 )\n'
     '\t\t\t{\n\t\t\t\tshop.Runtime.SetVariable( "VAR_LETMEON", 0 );\n\t\t\t\tstallTurn = long.MaxValue;\n\t\t\t}\n'
     '\t\t}\n\t}\n\n'
     '\tprivate string AttractionName( int id )'),
]


def instrument(text: str) -> str:
    text = v2.instrument(text)
    for anchor, replacement in HOOKS_V3:
        if text.count(anchor) != 1:
            raise AssertionError(f'v3 hook anchor not unique/absent: {anchor[:60]!r}')
        text = text.replace(anchor, replacement)
    return text


@unittest.skipUnless(v1.has_commit(SUBJECT) and v1.has_commit(BASE), f'{SUBJECT} or {BASE} not in this repository')
class StaticRound3(unittest.TestCase):
    def test_build_queue_cell_refactor_keeps_the_old_steps(self):
        old = v1.method_body(v1.show('source/OpenTPW/World/Level.Objects.cs', BASE), 'public QueueBuildResult BuildQueueCell( RideVisitorBridge ride, int x, int y )')
        new = v1.show('source/OpenTPW/World/Level.Objects.cs', SUBJECT)
        steps = ['QueuePaths.CheckExtend(', 'TrySpendCell( CellPurchase.Queue )', 'QueuePaths.TryExtend(']
        self.assertEqual(sorted(old.find(step) for step in steps), [old.find(step) for step in steps])
        instance = v1.method_body(new, 'public QueueBuildResult BuildQueueCell( RideVisitorBridge ride, int x, int y )')
        # the queue tool's overwrite priority (tp.exe 0x818C10 = 3) passes the scenery it may cover and its demolition along
        self.assertIn('BuildQueueCell( Guests.Grid, Park?.Economy, ride, x, y, IsQueueBlocked, out var message, QueueGivesWay, DemolishObjectAt );', instance)
        helper = v1.method_body(new, 'internal static QueueBuildResult BuildQueueCell(')
        if 'QueuePaths.CheckExtend(' not in helper:
            # GATE-V4: after the merge with PATH-FIX (PATH-V2's recipe) the check goes through the static CheckQueueCell,
            # which runs QueuePaths.CheckExtend and then the preview's money check; a Refused check is reported as "not
            # enough money" before anything is charged. WALK-F2 (REVIEW-ORACLE-2 OV2-3): the preview's money check is the
            # validator's (CanPreviewCells) and the commit passes no economy to it, so only TrySpendCell's test decides.
            check = v1.method_body(new, 'internal static QueueBuildResult CheckQueueCell(')
            self.assertLess(check.find('QueuePaths.CheckExtend('), check.find('CanPreviewCells( CellPurchase.Queue'))
            self.assertGreaterEqual(check.find('QueuePaths.CheckExtend('), 0)
            self.assertIn('check == QueueBuildResult.Refused || (economy != null && economy.TrySpendCell( CellPurchase.Queue )', helper)
            # the sync before the check takes in the host's geometry only: the cached back and size stay until a re-evaluation (QUEUE-003)
            steps = ['ride.RecomputeQueueKeepingBack( grid );', 'CheckQueueCell( grid, null, ride, x, y, ', 'TrySpendCell( CellPurchase.Queue )', 'QueuePaths.TryExtend(']
        positions = [helper.find(step) for step in steps]
        self.assertTrue(all(p >= 0 for p in positions), dict(zip(steps, positions)))
        self.assertEqual(sorted(positions), positions)
        self.assertNotRegex(helper, r'\bGuests\b|\bPark\b|IsQueueBlocked')

    def test_gate_builds_the_queue_and_paths_through_the_tool_code(self):
        gate = v1.show(v1.GATE, SUBJECT)
        build = v1.method_body(gate, 'private void BuildQueue()')
        self.assertIn('Level.BuildQueueCell( grid, economy, bridge, x, y, IsQueueBlocked, out message )', build)
        self.assertNotIn('QueuePaths.TryExtend(', build)
        self.assertNotIn('SetQueue(', gate)
        paths = v1.method_body(gate, 'private void BuildPaths()')
        self.assertIn('paths.BuildSegment( start, end )', paths)
        self.assertIn('new ParkPathBuilder( grid.Cells, grid, economy, buildGrid, ', paths)
        self.assertNotIn('SetPath(', gate)
        self.assertNotIn('TryBuyCells(', gate)

    def test_queue_row_reads_the_bridge_progress_counters(self):
        # QUEUE-V S1: the old Stalled counter could never become true; QUEUE-FIX replaced it with HeadNotReadyStreak and
        # CalledAgeTurns, which the gate reads.
        gate = v1.show(v1.GATE, SUBJECT)
        self.assertNotIn('Stalled', gate)
        self.assertIn('visitors.MaximumHeadNotReadyStreak', gate)
        self.assertIn('visitors.MaximumCalledAgeTurns', gate)

    def test_head_streak_counts_regardless_of_the_gates(self):
        # GATE-FIX2 (S2): rule (a) counts per head on !HeadAtFront, not on HeadNotReady (which requires ConditionsHold),
        # and restarts only on a new head or the head at the front.
        body = v1.method_body(v1.show(v1.GATE, SUBJECT), 'void OnAdmission( RideVisitorBridge bridge, AdmissionCheck check )')
        self.assertIn('if ( check.HeadGuest != 0 && !check.HeadAtFront )', body)
        self.assertNotIn('check.HeadNotReady', body)
        self.assertNotIn('HeadNotReadyStreak', body)
        rule_a = body[:body.index('var letMeOn')]
        self.assertNotIn('ConditionsHold', rule_a)


@unittest.skipUnless(v1.MUTATE and v1.GAME and Path(v1.GAME).is_dir() and Path(v1.DOTNET).exists() and v1.has_commit(SUBJECT),
                     'set OPENTPW_M3_MUTATE=1, OPENTPW_GAME_PATH and a .NET 11 SDK (OPENTPW_DOTNET)')
class MutationsRound3(v1.Mutations):
    results: dict[str, dict] = {}

    @classmethod
    def setUpClass(cls):
        saved = v1.instrument
        v1.instrument = instrument
        try:
            super().setUpClass()
        finally:
            v1.instrument = saved

    def rows(self, mutation: str, determinism: bool = False) -> dict:
        result = self.run_gate(mutation, determinism)
        self.assertTrue(result['rows'], result['stderr'] or result['stdout'])
        return result['rows']

    def test_gate_upd_baseline(self):
        rows = self.rows('', determinism=True)
        verdicts = {key: row['verdict'] for key, row in rows.items()}
        self.assertEqual([], sorted(key for key, value in verdicts.items() if value == 'fail'))
        # BOARD gate: the queue row passes on the traced boarding bound, so every row passes and M3 exits 0.
        self.assertEqual([], sorted(key for key, value in verdicts.items() if value == 'unresolved'))
        self.assertEqual(0, self.run_gate('', True)['exit'])
        # GATE-V3: the rules, not the layout's numbers: charge = built cells x Costs.PathCell; queue cells =
        # clamp(ceil(limit / 4), 1, 25) (QUEUE-013), charged x Costs.QueueCell, maximum queue min(limit, 4N).
        paths = rows['build.paths']['evidence']
        self.assertEqual('pass', rows['build.paths']['verdict'])
        self.assertGreater(paths['cellsBuilt'], 0)
        self.assertEqual((paths['cellsBuilt'] * PATH_CELL, paths['cellsBuilt'] * PATH_CELL, 0, 'none'),
                         (paths['charged'], paths['balanceCharged'], paths['strayPathCells'], paths['problems']))
        queue = rows['build.queue']['evidence']
        limit = int(re.search(r'queue limit (\d+)', queue['cellsRequestedRule']).group(1))
        cells = min(max(-(-limit // 4), 1), 25)
        self.assertEqual(('pass', cells, cells, cells * queue['queueCellCost'], cells, min(limit, 4 * cells)),
                         (rows['build.queue']['verdict'], queue['cellsRequested'], queue['cellsLaid'], queue['charged'],
                          queue['queueSizeInCells'], queue['maximumQueueLength']))
        self.assertNotEqual('none', queue['joinCell'])
        self.assertTrue(queue['joinReachableFromEntrance'])
        self.assertGreater(queue['guestsStoodOnQueueCells'], 0)
        self.assertGreater(queue['boardedFromQueueCells'], 0)
        stuck = rows['queues.no-stuck-queue']['evidence']
        self.assertEqual(0, stuck['violations'])
        self.assertLessEqual(stuck['attraction']['headNotReadyMaxTurns'], stuck['attraction']['headNotReadyBoundTurns'])
        bound = stuck['attraction']['waitBound']
        self.assertEqual('pass', rows['queues.no-stuck-queue']['verdict'])
        self.assertEqual((True, 0), (bound['judged'], bound['waitsAboveBound']))
        self.assertGreater(bound['judgedWaits'], 0)
        # BOARD-plan section 8, rebuilt from the reported terms: H = 17 + P + w + w2; R for DUR <= 30 is 4 DUR + 1 (DUR >= 8).
        self.assertEqual(17 + bound['loopPeriodTurns'], bound['hostLatencyTurns'])
        self.assertEqual(bound['hostLatencyTurns'] + bound['standWalkTurns'] + bound['moveUpWalkTurns'], bound['latencyTurns'])
        self.assertEqual(4 * bound['durationSeconds'] + 1, bound['holdTurns'])
        cap, h, r = bound['capacity'], bound['latencyTurns'], bound['holdTurns']
        # WALK-I added the join walk J(Qmax - 1) = (N - cell of p) x C + w2 to W_max (stale here until WALK-F ported this
        # suite, review WV-1): 24 at p = 20 of the 6-cell queue.
        join = int(bound['joinWalkTurnsAtHeadAndQmax'].split(' / ')[1])
        self.assertEqual(24, join)
        self.assertEqual(join + bound['qmax'] * h + ((bound['qmax'] - 1) // cap + 1) * r + 1, bound['wMaxTurns'])
        self.assertAlmostEqual(cap * h + r + 1 - (bound['durationSeconds'] + 1) * 1000 / 248, bound['tauMaxTurns'], places=3)
        for role in ('shop', 'toilet'):
            self.assertFalse(stuck[role]['waitBound']['judged'])
        # WALK-plan section 12: w = 20, w2 = 15 from the traced steering, the scope conditions hold, every wait judged.
        self.assertEqual((20, 15), (bound['standWalkTurns'], bound['moveUpWalkTurns']))
        self.assertTrue(bound['walkScope'].startswith('W2-W4 hold'), bound['walkScope'])
        self.assertEqual((0, 0, 0), (bound['notJudgedWalkScope'], bound['notJudgedYoungHead'], bound['walkStalls']))
        self.assertEqual('pass', verdicts['staff.work'])
        self.assertEqual('none', rows['staff.work']['evidence']['problems'])

    def test_block_boarding_now_fails_the_queue_row(self):
        rows = self.rows('block-boarding')
        row = rows['queues.no-stuck-queue']
        self.assertEqual('fail', row['verdict'], row)
        self.assertGreater(row['firstViolationTick'], 18000)
        self.assertIn('VAR_LETMEON', row['evidence']['firstViolation'])
        self.assertEqual(['queues.no-stuck-queue'], sorted(key for key, value in rows.items() if value['verdict'] == 'fail'))

    def test_stall_that_recovers_after_one_update_does_not_fail(self):
        row = self.rows('stall-one-update')['queues.no-stuck-queue']
        self.assertEqual(1, row['evidence']['shop']['blockedMaxEvaluations'], row)
        self.assertEqual('pass', row['verdict'], row)  # BOARD gate: was UNRESOLVED, now judged and within the bound
        self.assertEqual(0, row['evidence']['violations'])

    def test_never_called_head_fails_the_queue_row(self):
        row = self.rows('never-called-head')['queues.no-stuck-queue']
        self.assertEqual('fail', row['verdict'], row)
        self.assertGreater(row['firstViolationTick'], 18000)
        # BOARD gate: the head-to-boarding bound (151 turns) fires before rule (a) (194); rule (a) fires as well.
        self.assertGreater(row['evidence']['attraction']['headNotReadyViolations'], 0)
        self.assertGreater(row['evidence']['attraction']['headNotReadyMaxTurns'], row['evidence']['attraction']['headNotReadyBoundTurns'])

    def test_mechanic_idle_after_first_repair_fails(self):
        row = self.rows('idle-mechanic-after-first-repair')['staff.work']
        self.assertEqual(1, row['evidence']['repairs'])
        self.assertGreater(row['evidence']['repairsNeeded'], 1)
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('not repaired within', row['evidence']['problems'])
        self.assertIsNotNone(row['firstViolationTick'])

    def test_handyman_idle_after_first_clean_fails(self):
        row = self.rows('idle-handyman-after-first-clean')['staff.work']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('unclaimed beside a free handyman', row['evidence']['problems'])  # litter by cell: claims, not a shrinking store
        self.assertIsNotNone(row['firstViolationTick'])

    def test_queue_built_without_the_economy_fails_build_queue(self):
        rows = self.rows('queue-bypass-economy')
        row = rows['build.queue']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual(0, row['evidence']['charged'])
        self.assertEqual(self.rows('')['build.queue']['evidence']['cellsLaid'], row['evidence']['cellsLaid'])
        self.assertIn('charged 0', row['evidence']['problems'])


    def test_paths_written_around_the_builder_fail_build_paths(self):
        rows = self.rows('paths-direct-write')
        row = rows['build.paths']
        self.assertEqual('fail', row['verdict'], row)
        built = self.rows('')['build.paths']['evidence']['cellsBuilt']
        self.assertEqual((0, built), (row['evidence']['cellsBuilt'], row['evidence']['strayPathCells']))
        self.assertIn('not laid by the builder', row['evidence']['problems'])
        self.assertEqual(['build.paths'], sorted(key for key, value in rows.items() if value['verdict'] == 'fail'))

    def test_paths_built_without_charging_fail_build_paths(self):
        rows = self.rows('paths-no-charge')
        row = rows['build.paths']
        self.assertEqual('fail', row['verdict'], row)
        built = self.rows('')['build.paths']['evidence']['cellsBuilt']
        self.assertEqual((built, 0, 0), (row['evidence']['cellsBuilt'], row['evidence']['charged'], row['evidence']['balanceCharged']))
        self.assertIn(f'expected {built} x {PATH_CELL}', row['evidence']['problems'])

    def test_queue_one_cell_short_fails_build_queue(self):
        rows = self.rows('queue-one-short')
        row = rows['build.queue']
        requested = self.rows('')['build.queue']['evidence']['cellsRequested']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual(requested - 1, row['evidence']['cellsLaid'])
        self.assertIn(f'{requested - 1} of {requested} cells laid', row['evidence']['problems'])

    def test_never_ready_head_with_periodic_blocked_evaluation_fails(self):
        # REVIEW-M3-GATE round 3, S2 (flipped by GATE-FIX2): a conditions-false evaluation used to restart the per-head
        # streak, so a head held unready for the whole run escaped rule (a) when admission was blocked once every 300
        # turns (UNRESOLVED, streak <= 299). The streak now counts every evaluation the head is not at position 0.
        # One blocked evaluation stays below rule (b)'s two, so rule (a) alone fails the row.
        for mutation in ('never-ready-head-blips', 'never-ready-head-blips-150'):
            with self.subTest(mutation=mutation):
                rows = self.rows(mutation)
                row = rows['queues.no-stuck-queue']
                attraction = row['evidence']['attraction']
                self.assertEqual('fail', row['verdict'], row)
                self.assertGreater(row['firstViolationTick'], 18000)
                self.assertGreater(attraction['headNotReadyViolations'], 0)
                self.assertGreater(attraction['headNotReadyMaxTurns'], attraction['headNotReadyBoundTurns'])
                self.assertEqual(1, attraction['blockedMaxEvaluations'])
                baseline = {key for key, value in self.rows('').items() if value['verdict'] == 'fail'}
                self.assertEqual({'queues.no-stuck-queue'}, {key for key, value in rows.items() if value['verdict'] == 'fail'} - baseline)

    def test_head_bound_is_n_plus_four_cells(self):
        # GATE-FIX2 (S1) made the walk term min(2N + 2, N + 4) cells at OpenTPW's speed (194 turns for 25 cells, 50 for one
        # cell). WALK-I derived it from the traced steering, N x 9 + 2 x 15 (281 / 65), ORACLE-F cut Belly Bounce's queue
        # to 6 cells, and the merge gives 6 x 9 + 30 + 26 = 110 / 65 / 65 ([APPROX:GATE-006], looser than 84 / 50 / 50 by
        # owner decision). WALK-F (review WV-3) holds each head to its own remaining walk + 26 where that is smaller, so
        # the head held 250 turns at position 0 fails rule (a) after 26 turns instead of passing it (259 <= 281 at WALK-I).
        baseline = self.rows('')['queues.no-stuck-queue']['evidence']
        self.assertEqual((110, 65, 65), (baseline['attraction']['headNotReadyBoundTurns'], baseline['shop']['headNotReadyBoundTurns'],
                                         baseline['toilet']['headNotReadyBoundTurns']))
        self.assertIn('6 x C 9 + 2 x w2 15 = 84 turns', baseline['attraction']['headNotReadyBound'])
        self.assertIn('[APPROX:GATE-006]', baseline['attraction']['headNotReadyBound'])
        row = self.rows('head-held-250-turns')['queues.no-stuck-queue']
        attraction = row['evidence']['attraction']
        self.assertEqual('fail', row['verdict'], row)
        self.assertGreater(attraction['headNotReadyViolations'], 0)
        self.assertIn('from its own walk', row['evidence']['firstViolation'])
        self.assertGreater(attraction['headNotReadyMaxTurns'], 194)

    def test_slots_held_far_longer_than_r_fail_the_wait_bound(self):
        # BOARD gate: BOUNCE slots held 120 s past their deadline (about 5R), or never released. Heads stand ready at the
        # front, so rules (a) and (b) stay silent; the head is not boarded within W(0) = H + R + 1 turns of becoming head.
        for mutation in ('hold-slots-much-longer', 'never-release'):
            with self.subTest(mutation=mutation):
                rows = self.rows(mutation)
                row = rows['queues.no-stuck-queue']
                attraction = row['evidence']['attraction']
                self.assertEqual('fail', row['verdict'], row)
                self.assertGreater(row['firstViolationTick'], 18000)
                self.assertIn('after becoming head > bound', row['evidence']['firstViolation'])
                self.assertGreater(attraction['waitBound']['waitsAboveBound'], 0)
                self.assertEqual((0, 0), (attraction['headNotReadyViolations'], attraction['blockedViolations']))
                baseline = {key for key, value in self.rows('').items() if value['verdict'] == 'fail'}
                self.assertEqual({'queues.no-stuck-queue'}, {key for key, value in rows.items() if value['verdict'] == 'fail'} - baseline)

    def test_head_delayed_beyond_h_fails_the_wait_bound(self):
        # BOARD gate: each new head is held 170 turns before it can be called: beyond W(0) = H + R + 1 = 177 turns once its
        # boarding follows. Before WALK-F only the wait bound caught it (below the 194-turn head bound of rule (a)); now
        # rule (a)'s own-walk limit (26 turns for a head at position 0, review WV-3) catches it first.
        row = self.rows('head-delayed-170-turns')['queues.no-stuck-queue']
        attraction = row['evidence']['attraction']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('from its own walk', row['evidence']['firstViolation'])
        self.assertGreater(attraction['headNotReadyViolations'], 0)
        self.assertGreater(attraction['waitBound']['waitsAboveBound'], 0)

    def test_move_up_held_beyond_w2_fails_walk_stall(self):
        # WALK gate (WALK-plan section 12.4): the traced steering finishes the move-up to slot 0 within w2 = 15 updates.
        # A guest held 30 turns in that walk fails the queue row as walk-stall, before any other queue rule.
        row = self.rows('hold-move-up')['queues.no-stuck-queue']
        bound = row['evidence']['attraction']['waitBound']
        self.assertEqual('fail', row['verdict'], row)
        self.assertTrue(row['evidence']['firstViolation'].startswith('walk-stall:'), row['evidence']['firstViolation'])
        self.assertIn('move-up to slot 0', row['evidence']['firstViolation'])
        self.assertEqual(1, bound['walkStalls'])
        self.assertGreater(bound['moveUpWalkMaxTurns'], bound['moveUpWalkTurns'])
        self.assertEqual((0, 0), (row['evidence']['attraction']['headNotReadyViolations'], row['evidence']['attraction']['blockedViolations']))
        baseline = self.rows('')['queues.no-stuck-queue']['evidence']['attraction']['waitBound']
        self.assertEqual(0, baseline['walkStalls'])
        self.assertLessEqual(baseline['moveUpWalkMaxTurns'], baseline['moveUpWalkTurns'])
        self.assertLessEqual(baseline['standWalkMaxTurns'], baseline['standWalkTurns'])

    def test_slowdowns_within_the_loose_bound_pass(self):
        # Pinned as observed. The BOARD-plan bound is loose by design (it assumes every boarding pays a full H and every
        # CAP-th a full R), and slots held about 2R (+30 s) or heads held 60 turns passed until WALK-I. Since WALK-I the
        # called guest walks into the entrance cell, and slots held 2R fail the head's wait bound by one turn (178 > 177 since the loop WAIT follows the script speed,
        # review WALK-V WV-1). Since WALK-F rule (a) holds a head standing at position 0 to 26 turns (review WV-3), so heads
        # held 60 turns fail too. Both are stricter, not looser.
        row = self.rows('hold-slots-longer')['queues.no-stuck-queue']
        # Slots held 2R sit on the head's wait bound to the turn, and the turns move with every change to the guests' walks (litter by
        # cell gave 203 > 202, pranks 179 > 178, the scaled loop WAIT 178 > 177, both together a pass). So either verdict is allowed,
        # and a failure must be the head's wait bound.
        self.assertIn(row['verdict'], ('pass', 'fail'), row)
        if row['verdict'] == 'fail':
            self.assertRegex(row['evidence']['firstViolation'], r'\d+ turns after becoming head > bound \d+')
        self.assertEqual(0, row['evidence']['attraction']['headNotReadyViolations'])
        row = self.rows('head-delayed-60-turns')['queues.no-stuck-queue']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('from its own walk', row['evidence']['firstViolation'])
        self.assertEqual(0, row['evidence']['attraction']['waitBound']['waitsAboveBound'])

    def test_end_of_run_reachability_includes_the_join_cell(self):
        # GATE-FIX2 (S3): cutting the segment's first cell leaves Belly Bounce's entrance and exit cells reachable but not
        # its join cell; only the join-cell check lists it (against 64616cd the list lacks it).
        rows = self.rows('cut-spine-start')
        row = rows['paths.no-unreachable-goal']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('Belly Bounce', row['evidence']['unreachableAttractions'].split(', '))
        self.assertTrue(rows['build.attraction']['evidence']['reachableFromEntrance'])

    def test_missing_gate_fails_build_entrance(self):
        rows = self.rows('no-gates')
        self.assertEqual('fail', rows['build.entrance']['verdict'], rows['build.entrance'])
        self.assertNotIn('Gates', rows['build.entrance']['evidence']['fixedItems'])

    def test_unbuyable_objects_fail_their_build_rows(self):
        for role, row_id in (('attraction', 'build.attraction'), ('shop', 'build.shop'), ('toilet', 'build.toilet')):
            with self.subTest(role=role):
                rows = self.rows(f'unbuyable-{role}')
                self.assertEqual('fail', rows[row_id]['verdict'], rows[row_id])
                self.assertEqual('UnknownObject', rows[row_id]['evidence']['purchase'])
                self.assertEqual('fail', rows['guests.flow']['verdict'])

    def test_closed_park_fails_income_and_expenses(self):
        rows = self.rows('close-park')
        row = rows['economy.income-and-expenses']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual('pass', rows['economy.ledger-consistent']['verdict'])

    def test_transient_exit_cut(self):
        rows = self.rows('cut-exit-transient')
        # Round 3, N2: the exit cell was no guest's walking target, so cutting it for 600 ticks failed no row. Since
        # ORACLE-F's 6-cell queue the layout puts the exit at (48,21), a path cell guests walk over: guests standing there
        # when it is cut head for a target they cannot reach and the per-tick check fails (also on the merge b10710e,
        # before WALK-F). Exit reachability at the end of the run (after the repair) is still clean.
        baseline = {key for key, value in self.rows('').items() if value['verdict'] == 'fail'}
        self.assertEqual(baseline | {'paths.no-unreachable-goal'}, {key for key, value in rows.items() if value['verdict'] == 'fail'})
        self.assertEqual(36001, rows['paths.no-unreachable-goal']['firstViolationTick'])
        self.assertIn('at (48,21)', rows['paths.no-unreachable-goal']['evidence']['firstViolation'])
        self.assertEqual('none', rows['paths.no-unreachable-goal']['evidence']['unreachableAttractions'])


# The inherited v1 tests run in test_m3_gate_v1.py; None is not collected.
for _name in [name for name in vars(v1.Mutations) if name.startswith('test_')]:
    setattr(MutationsRound3, _name, None)

if __name__ == '__main__':
    unittest.main()
