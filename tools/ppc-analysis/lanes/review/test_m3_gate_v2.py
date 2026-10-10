"""M3-GATE-V2: round-2 check of the stricter ``--m3-gate`` (GATE-FIX, commit ea09cda).

Round 1 (test_m3_gate_v1.py) pinned the review findings and re-runs its own mutations against the gate under test.
This file adds the round-2 mutations for the new rules:

- staff.work asks for at least one repair when a ride became worn or broke down, and at least some cleaning when
  litter existed. A mechanic or handyman that works once and then idles for the rest of the run still passes
  (``idle-mechanic-after-first-repair``, ``idle-handyman-after-first-clean``); this pins that weakness.
- The "when needed" conditions are real: a run without wear events and without litter passes staff.work with both
  staff types idle (``no-wear-no-litter``), so a legitimate zero-breakdown run is not failed for the mechanic.
- Litter that first appears in the very last tick (one item added then) fails the row although no handyman could have cleaned it
  (``late-litter``): strict at the edge, harmless in practice because the baseline drops litter all run.
- A placed script that halts in the last tick fails rides.scripts-run (``halt-last-tick``); a fixed item (the
  entrance gate) that halts is not judged by any row (``halt-gate``).
- Static: Level.RegisterObjectWithGuests and ParkObjects.Check refactors keep their old behaviour (the old bodies are
  textually the new helpers with the instance members passed in).

Mutation tests run only with ``OPENTPW_M3_MUTATE=1`` and ``OPENTPW_GAME_PATH``; they build ``OPENTPW_M3_SUBJECT``
(default ``HEAD``) with the v1 hooks plus the hooks below. Nothing here says anything about how the original game
behaves.
"""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_m3_gate_v1 as v1  # noqa: E402

FIX = 'ea09cda'
BASE = v1.BASE

HOOKS_V2 = [
    ('\t\t\tMutateAfterTick( tick );\n',
     '\t\t\tMutateAfterTick( tick );\n\t\t\tMutateV2( tick, ticks );\n'),
    ('\t\t\t\trepairs++;\n',
     '\t\t\t{\n\t\t\t\trepairs++;\n\t\t\t\tif ( Mutation == "idle-mechanic-after-first-repair" )\n\t\t\t\t\tidleMechanic = true;\n\t\t\t}\n'),
    ('\t\t\tlitterPeak = Math.Max( litterPeak, economy.LitterScaled );\n',
     '\t\t\tif ( Mutation == "no-wear-no-litter" || (Mutation == "late-litter" && tick < ticks) )\n'
     '\t\t\t{\n\t\t\t\ttypeof( ParkEconomy ).GetProperty( "LitterScaled" )!.SetValue( economy, 0L );\n\t\t\t\tpreviousLitter = 0;\n\t\t\t}\n'
     '\t\t\telse if ( Mutation == "late-litter" )\n\t\t\t\teconomy.AddLitter( (int)ParkEconomy.LitterScale );\n'
     '\t\t\tlitterPeak = Math.Max( litterPeak, economy.LitterScaled );\n'),
    ('\t\t\tpreviousLitter = economy.LitterScaled;\n',
     '\t\t\tpreviousLitter = economy.LitterScaled;\n'
     '\t\t\tif ( Mutation == "idle-handyman-after-first-clean" && litterCleaned > 0 )\n'
     '\t\t\t\tforeach ( var member in economy.Staff.Members.Where( member => member.Type == StaffType.Handyman ) )\n'
     '\t\t\t\t\tmember.State = StaffState.PickedUp;\n'),
    ('\tprivate string AttractionName( int id )',
     '\tprivate bool idleMechanic;\n'
     '\tprivate void MutateV2( long tick, long ticks )\n\t{\n'
     '\t\tvar ride = placed.FirstOrDefault( item => item.Role == "attraction" );\n'
     '\t\tif ( idleMechanic || Mutation == "no-wear-no-litter" )\n'
     '\t\t\tforeach ( var member in economy.Staff.Members.Where( member => member.Type == StaffType.Mechanic || Mutation == "no-wear-no-litter" ) )\n'
     '\t\t\t\tmember.State = StaffState.PickedUp;\n'
     '\t\tif ( Mutation == "no-wear-no-litter" )\n'
     '\t\t\tforeach ( var (_, instance) in economyLinks )\n'
     '\t\t\t\tif ( economy.TryGetObject( instance, out var state ) )\n'
     '\t\t\t\t\tstate.StateOfRepair = 100;\n'
     '\t\tif ( Mutation == "halt-last-tick" && tick == ticks )\n\t\t\tride!.Runtime.Stop();\n'
     '\t\tif ( Mutation == "halt-gate" && tick == 1000 )\n'
     '\t\t\tfixedItems.First( item => item.Entry.SettingsName == "Gates" ).Stop();\n'
     '\t}\n\n'
     '\tprivate string AttractionName( int id )'),
]


_v1_instrument = v1.instrument


def instrument(text: str) -> str:
    text = _v1_instrument(text)
    for anchor, replacement in HOOKS_V2:
        if text.count(anchor) != 1:
            raise AssertionError(f'v2 hook anchor not unique/absent: {anchor[:60]!r}')
        text = text.replace(anchor, replacement)
    return text


@unittest.skipUnless(v1.has_commit(FIX), f'fix commit {FIX} not in this repository')
class StaticRound2(unittest.TestCase):
    def test_register_with_guests_refactor_is_textually_the_old_body(self):
        old = v1.method_body(v1.show('source/OpenTPW/World/Level.Objects.cs', BASE), 'private void RegisterObjectWithGuests( OriginalObject item )')
        new = v1.show('source/OpenTPW/World/Level.Objects.cs', FIX)
        # Old: guard Guests/attraction, entrance required, exit defaults to entrance, both snapped by WalkableNear,
        # then cells set and Register. New helpers keep each step; the caller passes item.Runtime/AccessPoints/Guests.
        for step in ['point.Kind == ObjectCellKind.Entrance', 'if ( exit == default )\n\t\t\texit = entrance;',
                     'WalkableNear( entrance.OutsideX, entrance.OutsideY )', 'WalkableNear( exit.OutsideX, exit.OutsideY )']:
            self.assertIn(step, old)
        self.assertIn('if ( Guests != null )\n\t\t\tRegisterWithGuests( item.Runtime, item.AccessPoints, Guests );', new)
        self.assertIn('if ( !runtime.IsAttraction || ResolveVisitorCells( accessPoints, guests.Grid ) is not { } cells )', new)
        self.assertIn('WalkableNear( grid, entrance.OutsideX, entrance.OutsideY )', new)
        self.assertIn('WalkableNear( grid, exit.OutsideX, exit.OutsideY )', new)
        self.assertIn('grid.IsWalkable( x, y ) ? (x, y) : FindRideEntrance( grid, x, y, x, y )', new)
        # item.Visitors is the runtime's bridge, so writing runtime.Visitors is the same object.
        original_object = v1.show('source/OpenTPW/World/Objects/OriginalObject.cs', FIX)
        self.assertRegex(original_object, r'Visitors\s*=>\s*Runtime\.Visitors')

    def test_parkobjects_check_instance_overload_forwards_its_own_grid_and_occupancy(self):
        new = v1.show('source/OpenTPW/World/Objects/ParkObjects.cs', FIX)
        self.assertIn('Check( Grid, IsOccupied, entry, anchorX, anchorY, rotation );', new)
        body = v1.method_body(new, 'internal static OriginalPlacementResult Check(')
        self.assertNotRegex(body, r'\bGrid\.|\bIsOccupied\(')

    def test_gate_builds_through_level_flow_helpers(self):
        gate = v1.show(v1.GATE, FIX)
        place = v1.method_body(gate, 'private void PlaceObject( string role')
        order = ['Level.GetCentredAnchor(', 'ParkObjects.Check(', 'economy.TryBuild(', 'Level.RegisterWithGuests(', '.Link(']
        positions = [place.find(step) for step in order]
        self.assertTrue(all(p >= 0 for p in positions), dict(zip(order, positions)))
        self.assertEqual(sorted(positions), positions)

    def test_new_csharp_files_are_lf_like_their_neighbours(self):
        for path in ['source/OpenTPW/Client/M3Gate.cs', 'source/OpenTPW/Client/M3GateApproximations.cs', 'source/OpenTPW.Tests/M3GateTests.cs']:
            self.assertNotIn('\r\n', v1.git('show', f'{FIX}:{path}'), path)


@unittest.skipUnless(v1.MUTATE and v1.GAME and Path(v1.GAME).is_dir() and Path(v1.DOTNET).exists() and v1.has_commit(v1.MUTATION_SUBJECT),
                     'set OPENTPW_M3_MUTATE=1, OPENTPW_GAME_PATH and a .NET 10 SDK (OPENTPW_DOTNET)')
class MutationsRound2(v1.Mutations):
    results: dict[str, dict] = {}

    @classmethod
    def setUpClass(cls):
        v1.instrument = instrument
        try:
            super().setUpClass()
        finally:
            v1.instrument = _v1_instrument


# The inherited v1 tests run in test_m3_gate_v1.py; None is not collected.
for _name in [name for name in vars(v1.Mutations) if name.startswith('test_')]:
    setattr(MutationsRound2, _name, None)


def _staff(case, mutation):
    return case.run_gate(mutation)['rows']['staff.work']


def test_mechanic_idle_after_first_repair_still_passes(self):
    row = _staff(self, 'idle-mechanic-after-first-repair')
    self.assertEqual(1, row['evidence']['repairs'])
    self.assertGreater(row['evidence']['repairsNeeded'], 1)
    self.assertEqual('pass', row['verdict'], row)  # weakness: one repair satisfies the row


def test_handyman_idle_after_first_clean_still_passes(self):
    row = _staff(self, 'idle-handyman-after-first-clean')
    baseline = _staff(self, '')
    self.assertLess(row['evidence']['litterCleanedItems'], 2)
    self.assertGreater(row['evidence']['litterDroppedItems'], 100)
    self.assertLess(row['evidence']['litterCleanedItems'], baseline['evidence']['litterCleanedItems'])
    self.assertEqual('pass', row['verdict'], row)  # weakness: one clean satisfies the row


def test_zero_wear_and_zero_litter_pass_with_idle_staff(self):
    row = _staff(self, 'no-wear-no-litter')
    self.assertEqual((0, 0, 0.0, 0.0), (row['evidence']['repairsNeeded'], row['evidence']['repairs'],
                                          row['evidence']['litterDroppedItems'], row['evidence']['litterCleanedItems']), row)
    self.assertEqual('pass', row['verdict'], row)


def test_litter_in_last_tick_only_fails(self):
    row = _staff(self, 'late-litter')
    self.assertEqual(0, row['evidence']['litterCleanedItems'])
    self.assertEqual('fail', row['verdict'], row)
    self.assertIn('handyman cleaned no litter', row['evidence']['problems'])


def test_halt_in_last_tick_fails_scripts_run(self):
    rows = self.run_gate('halt-last-tick')['rows']
    self.assertEqual('fail', rows['rides.scripts-run']['verdict'])
    self.assertEqual(108000, rows['rides.scripts-run']['firstViolationTick'])
    self.assertEqual('Belly Bounce', rows['rides.scripts-run']['evidence']['notRunningAtEnd'])


def test_halted_gate_fixed_item_is_judged_by_no_row(self):
    result = self.run_gate('halt-gate')
    verdicts = {key: row['verdict'] for key, row in result['rows'].items()}
    self.assertIn('Gates Halted', result['rows']['rides.scripts-run']['evidence']['scripts'])
    self.assertEqual('pass', verdicts['rides.scripts-run'])
    self.assertEqual(['build.paths', 'build.queue'], sorted(key for key, value in verdicts.items() if value == 'fail'))


for _test in [test_mechanic_idle_after_first_repair_still_passes, test_handyman_idle_after_first_clean_still_passes,
              test_zero_wear_and_zero_litter_pass_with_idle_staff, test_litter_in_last_tick_only_fails,
              test_halt_in_last_tick_fails_scripts_run, test_halted_gate_fixed_item_is_judged_by_no_row]:
    setattr(MutationsRound2, _test.__name__, _test)

if __name__ == '__main__':
    unittest.main()
