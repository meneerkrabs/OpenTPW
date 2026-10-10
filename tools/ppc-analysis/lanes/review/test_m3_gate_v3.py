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
- S3: the end-of-run reachability check includes the queue's join cell, so ``cut-path`` lists Belly Bounce;
- S4 (in test_m3_gate_v1.py): a second run that diverges early and converges by the end fails determinism.

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
        self.assertIn('BuildQueueCell( Guests.Grid, Park?.Economy, ride, x, y, IsQueueBlocked, out var message );', instance)
        helper = v1.method_body(new, 'internal static QueueBuildResult BuildQueueCell(')
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
                     'set OPENTPW_M3_MUTATE=1, OPENTPW_GAME_PATH and a .NET 10 SDK (OPENTPW_DOTNET)')
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
        self.assertEqual(['queues.no-stuck-queue'], sorted(key for key, value in verdicts.items() if value == 'unresolved'))
        self.assertEqual(2, self.run_gate('', True)['exit'])
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
        self.assertEqual(0, stuck['attraction']['waitBound']['waitsAboveBoundAtTauParameter'])
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
        self.assertEqual('unresolved', row['verdict'], row)
        self.assertEqual(0, row['evidence']['violations'])

    def test_never_called_head_fails_the_queue_row(self):
        row = self.rows('never-called-head')['queues.no-stuck-queue']
        self.assertEqual('fail', row['verdict'], row)
        self.assertGreater(row['firstViolationTick'], 18000)
        self.assertIn('not ready', row['evidence']['firstViolation'])
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
        self.assertIn('litter not reduced', row['evidence']['problems'])
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
                self.assertIn('not ready', row['evidence']['firstViolation'])
                self.assertGreater(attraction['headNotReadyMaxTurns'], attraction['headNotReadyBoundTurns'])
                self.assertEqual(1, attraction['blockedMaxEvaluations'])
                baseline = {key for key, value in self.rows('').items() if value['verdict'] == 'fail'}
                self.assertEqual({'queues.no-stuck-queue'}, {key for key, value in rows.items() if value['verdict'] == 'fail'} - baseline)

    def test_head_bound_is_n_plus_four_cells(self):
        # GATE-FIX2 (S1): the head only walks forward, so the walk term is min(2N + 2, N + 4) cells: 194 turns for 25
        # cells (was 326), 50 for one-cell queues. A head held for 250 turns was UNRESOLVED under 326 and now fails.
        baseline = self.rows('')['queues.no-stuck-queue']['evidence']
        self.assertEqual((194, 50, 50), (baseline['attraction']['headNotReadyBoundTurns'], baseline['shop']['headNotReadyBoundTurns'],
                                         baseline['toilet']['headNotReadyBoundTurns']))
        self.assertIn('walk 29 cells', baseline['attraction']['headNotReadyBound'])
        row = self.rows('head-held-250-turns')['queues.no-stuck-queue']
        attraction = row['evidence']['attraction']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('not ready', row['evidence']['firstViolation'])
        self.assertGreater(attraction['headNotReadyMaxTurns'], 194)
        self.assertLessEqual(attraction['headNotReadyMaxTurns'], 326)

    def test_end_of_run_reachability_includes_the_join_cell(self):
        # GATE-FIX2 (S3): cutting spine[1] also cuts Belly Bounce's join cell; its entrance and exit cells stay reachable,
        # so before the fix the end-of-run list was only "Drinks Shop, Small Toilet".
        row = self.rows('cut-path')['paths.no-unreachable-goal']
        self.assertEqual('fail', row['verdict'], row)
        self.assertIn('Belly Bounce', row['evidence']['unreachableAttractions'].split(', '))

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
        # Not measured (round 3, N2): the ride's exit cell is no guest's walking target, so cutting it for 600 ticks fails
        # no row; exit reachability is checked only at the end of the run (after the repair). Pinned as observed.
        baseline = {key for key, value in self.rows('').items() if value['verdict'] == 'fail'}
        self.assertEqual(baseline, {key for key, value in rows.items() if value['verdict'] == 'fail'})
        self.assertEqual('none', rows['paths.no-unreachable-goal']['evidence']['unreachableAttractions'])


# The inherited v1 tests run in test_m3_gate_v1.py; None is not collected.
for _name in [name for name in vars(v1.Mutations) if name.startswith('test_')]:
    setattr(MutationsRound3, _name, None)

if __name__ == '__main__':
    unittest.main()
