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

Static tests read ``OPENTPW_M3_SUBJECT`` (default ``HEAD``). Mutation tests run only with ``OPENTPW_M3_MUTATE=1``
and ``OPENTPW_GAME_PATH``. Nothing here says anything about how the original game behaves.
"""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_m3_gate_v1 as v1  # noqa: E402
import test_m3_gate_v2 as v2  # noqa: E402

SUBJECT = v1.MUTATION_SUBJECT
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
    ('\tprivate string AttractionName( int id )',
     # The pre-PATH-I stand-in: cells charged and written around the builder, which therefore reports nothing built.
     '\tprivate readonly List<(int X, int Y)> directCells = new();\n'
     '\tprivate SegmentResult DirectWrite( (int X, int Y) start, (int X, int Y) end )\n\t{\n'
     '\t\tforeach ( var (x, y) in ParkPathBuilder.LayLine( start, end ).Skip( 1 ) )\n\t\t{\n'
     '\t\t\teconomy.TryBuyCells( CellPurchase.Path, 1 );\n\t\t\tgrid.SetPath( x, y, true );\n\t\t\tdirectCells.Add( (x, y) );\n\t\t}\n'
     '\t\treturn new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = start };\n\t}\n'
     '\tprivate long stallTurn = -1;\n'
     '\tprivate void MutateV3( long tick )\n\t{\n'
     '\t\tvar ride = placed.FirstOrDefault( item => item.Role == "attraction" );\n'
     '\t\tvar shop = placed.FirstOrDefault( item => item.Role == "shop" );\n'
     '\t\tif ( Mutation == "never-called-head" && tick >= 18000 && ride!.Runtime.Visitors.Queue.Count > 0 && guests.Find( ride.Runtime.Visitors.Queue[0] ) is { } head )\n'
     '\t\t{\n\t\t\thead.InQueueInterlude = true;\n\t\t\thead.InterludeTurn = guests.ParkTurn;\n\t\t}\n'
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
        self.assertIn('bridge.HeadNotReadyStreak', gate)
        self.assertIn('check.HeadNotReady', gate)
        self.assertIn('visitors.MaximumCalledAgeTurns', gate)


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
        paths = rows['build.paths']['evidence']
        self.assertEqual(('pass', 14, 280, 280, 0, 'none'), (rows['build.paths']['verdict'], paths['cellsBuilt'], paths['charged'],
                                                             paths['balanceCharged'], paths['strayPathCells'], paths['problems']))
        queue = rows['build.queue']['evidence']
        self.assertEqual(('pass', 25, 25, 25 * 75, 25, 100), (rows['build.queue']['verdict'], queue['cellsRequested'], queue['cellsLaid'],
                                                              queue['charged'], queue['queueSizeInCells'], queue['maximumQueueLength']))
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
        self.assertIn('Belly Bounce', row['evidence']['firstViolation'])
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
        self.assertEqual(25, row['evidence']['cellsLaid'])
        self.assertIn('charged 0', row['evidence']['problems'])


    def test_paths_written_around_the_builder_fail_build_paths(self):
        rows = self.rows('paths-direct-write')
        row = rows['build.paths']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual((0, 14), (row['evidence']['cellsBuilt'], row['evidence']['strayPathCells']))
        self.assertIn('not laid by the builder', row['evidence']['problems'])
        self.assertEqual(['build.paths'], sorted(key for key, value in rows.items() if value['verdict'] == 'fail'))

    def test_paths_built_without_charging_fail_build_paths(self):
        rows = self.rows('paths-no-charge')
        row = rows['build.paths']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual((14, 0, 0), (row['evidence']['cellsBuilt'], row['evidence']['charged'], row['evidence']['balanceCharged']))
        self.assertIn('expected 14 x 20', row['evidence']['problems'])


# The inherited v1 tests run in test_m3_gate_v1.py; None is not collected.
for _name in [name for name in vars(v1.Mutations) if name.startswith('test_')]:
    setattr(MutationsRound3, _name, None)

if __name__ == '__main__':
    unittest.main()
