"""M3-GATE-V: independent check of the headless ``--m3-gate`` evaluator of commit f6ad99f.

Static tests read the subject commit's files straight from the git object store (``git show f6ad99f:<path>``),
so they need no checkout of the subject. They pin the review findings: the tick order matches ``Level.Update``,
the queue row can never pass or fail, the reference bound is never compared with a measured value, the time
mapping numbers follow from ParkCalendar's constants, the attraction-id counter is process-wide, and only the gate
writes ``Environment.ExitCode``.

Mutation tests are slow and run only with ``OPENTPW_M3_MUTATE=1`` and ``OPENTPW_GAME_PATH``. They extract the
tree of the commit under test (``OPENTPW_M3_SUBJECT``, default ``HEAD``; the review ran them on f6ad99f) with
``git archive`` into a temporary folder (not a worktree), add fault-injection hooks to
``M3Gate.cs`` (selected by the ``M3GATE_MUTATION`` environment variable at run time), build it once with the .NET 10
SDK (``OPENTPW_DOTNET``, default ``~/.local/share/opentpw-dotnet10/dotnet``) and run ``--m3-gate`` once per fault.
Each test states which row must flip. Some tests pin rows that do *not* flip, because that is the finding.
GATE-FIX made the gate stricter; the expectations it changed are marked "GATE-FIX" below (build.paths fails,
staff.work is per staff type, rides.scripts-run fails on a halted script, unresolved rows exit 2).
Nothing here says anything about how the original game behaves.
"""
from __future__ import annotations

import datetime
import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
SUBJECT = 'f6ad99f'
BASE = 'efc090d'
GATE = 'source/OpenTPW/Client/M3Gate.cs'
# The mutation tests build the gate under test, not the reviewed commit.
MUTATION_SUBJECT = os.environ.get('OPENTPW_M3_SUBJECT', 'HEAD')


def git(*args: str) -> str:
    return subprocess.run(['git', '-C', str(REPO), *args], check=True, capture_output=True, text=True).stdout


def has_commit(rev: str) -> bool:
    return subprocess.run(['git', '-C', str(REPO), 'cat-file', '-e', f'{rev}^{{commit}}'], capture_output=True).returncode == 0


def show(path: str, rev: str = SUBJECT) -> str:
    return git('show', f'{rev}:{path}').lstrip('﻿')


def method_body(text: str, signature: str) -> str:
    """Text of the brace block following ``signature`` (no string/comment awareness; good enough for these files)."""
    start = text.index(signature)
    open_at = text.index('{', start)
    depth = 0
    for index in range(open_at, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0:
                return text[open_at:index + 1]
    raise ValueError(f'unbalanced block after {signature!r}')


@unittest.skipUnless(has_commit(SUBJECT), f'subject commit {SUBJECT} not in this repository')
class StaticFindings(unittest.TestCase):
    def test_tick_order_matches_level_update(self):
        level = method_body(show('source/OpenTPW/World/Level.cs'), 'public void Update()')
        level_calls = re.findall(r'(SyncObjectEconomy|Guests\?\.Tick|PlacedRide\?\.Simulate|Objects\.Simulate|Park\?\.FixedTick)', level)
        self.assertEqual(['SyncObjectEconomy', 'Guests?.Tick', 'PlacedRide?.Simulate', 'Objects.Simulate', 'Park?.FixedTick'], level_calls)
        loop = method_body(show(GATE), 'private void Simulate( long ticks )')
        loop = loop[loop.index('for ( long tick = 1'):loop.index('// Time: strictly increasing')]
        loop = re.sub(r'\s+', ' ', loop)
        steps = ['state.IsOpen = item.Runtime.IsOpen;', 'guests.Tick( Tick );', 'foreach ( var item in fixedItems ) item.Simulate( Tick );',
                 'foreach ( var item in placed ) item.Runtime.Simulate( Tick );', 'runtime.FixedTick();']
        positions = [loop.find(step) for step in steps]
        self.assertTrue(all(position >= 0 for position in positions), dict(zip(steps, positions)))
        self.assertEqual(sorted(positions), positions)

    @unittest.skipUnless(has_commit(MUTATION_SUBJECT), f'{MUTATION_SUBJECT} not in this repository')
    def test_tick_order_matches_level_update_in_gate_under_test(self):
        # GATE-FIX: the same drift check on the gate under test (the open-state sync now covers every economy link).
        level = method_body(show('source/OpenTPW/World/Level.cs', MUTATION_SUBJECT), 'public void Update()')
        level_calls = re.findall(r'(SyncObjectEconomy|Guests\?\.Tick|PlacedRide\?\.Simulate|Objects\.Simulate|Park\?\.FixedTick)', level)
        self.assertEqual(['SyncObjectEconomy', 'Guests?.Tick', 'PlacedRide?.Simulate', 'Objects.Simulate', 'Park?.FixedTick'], level_calls)
        loop = method_body(show(GATE, MUTATION_SUBJECT), 'private void Simulate( long ticks )')
        loop = re.sub(r'\s+', ' ', loop[loop.index('for ( long tick = 1'):loop.index('// Time: strictly increasing')])
        steps = ['foreach ( var (item, instance) in economyLinks )', 'state.IsOpen = item.IsOpen;', 'guests.Tick( Tick );',
                 'foreach ( var item in fixedItems ) item.Simulate( Tick );', 'foreach ( var item in placed ) item.Runtime.Simulate( Tick );',
                 'runtime.FixedTick();']
        positions = [loop.find(step) for step in steps]
        self.assertTrue(all(position >= 0 for position in positions), dict(zip(steps, positions)))
        self.assertEqual(sorted(positions), positions)

    def test_level_object_order_is_fixed_items_then_built_objects(self):
        # ParkObjects.Simulate walks objects in insertion order: AddDefaultFixedItems first (no save in Full
        # Simulation), then each built object. The gate simulates fixedItems, then placed: same order and the
        # same seeds 1, 2, 3, ... (both count from 1 per park).
        park_objects = show('source/OpenTPW/World/Objects/ParkObjects.cs')
        self.assertIn('private int nextSeed = 1;', park_objects)
        self.assertIn('foreach ( var item in objects )\n\t\t\titem.Simulate( deltaSeconds );', park_objects)
        gate = show(GATE)
        self.assertIn('private int nextSeed = 1;', gate)
        self.assertEqual(2, gate.count('world, nextSeed++, open: true )'))

    def test_divergence_fixed_items_not_linked_or_synced(self):
        # Level links every object with an economy record (fixed items included) and mirrors IsOpen for all of
        # them; the gate registers the fixed items but neither links nor syncs them. Harmless today because the
        # economy only reads IsOpen for Ride/Shop/Sideshow kinds; this test notices if either side changes.
        level = show('source/OpenTPW/World/Level.Objects.cs')
        connect = method_body(level, 'private void ConnectObjectsToEconomy()')
        self.assertIn('foreach ( var item in Objects.Objects )', connect)
        self.assertIn('LinkEconomy( item, state.Id );', connect)
        entrance = method_body(show(GATE), 'private void BuildEntrance()')
        self.assertIn('economy.RegisterExisting( entry.InfoId );', entrance)
        self.assertNotIn('Link(', entrance)
        economy = show('source/OpenTPW/Economy/ParkEconomy.cs')
        readers = [line.strip() for line in economy.splitlines() if re.search(r'item\.IsOpen\b', line)]
        self.assertTrue(readers and all(re.search(r'ParkObjectKind\.(Ride|Shop|Sideshow)', line) for line in readers), readers)

    def test_divergence_walkable_near_not_used(self):
        # Level replaces a non-walkable outside cell by the nearest walkable cell (RIDES-028); the gate only accepts
        # sites whose entrance opens onto a spine cell and builds a connector for the exit, so both paths agree for
        # the sites it picks, but the gate cannot reproduce a RIDES-028 snap.
        self.assertIn('WalkableNear( entrance.OutsideX, entrance.OutsideY )', show('source/OpenTPW/World/Level.Objects.cs'))
        self.assertNotIn('WalkableNear', show(GATE))
        self.assertNotIn('FindRideEntrance', show(GATE))

    def test_queue_row_is_always_unresolved_and_reference_is_evidence_only(self):
        body = method_body(show(GATE), 'private void AddQueueRow(')
        verdicts = re.findall(r'M3GateVerdict\.(\w+)', body)
        self.assertEqual(['Unresolved'], verdicts)
        uses = [line.strip() for line in body.splitlines() if 'cycles * duration' in line or 'referenceBound' in line]
        self.assertTrue(all(line.startswith('evidence[') or line.startswith('//') for line in uses), uses)
        self.assertNotRegex(body, r'maxWait\s*[<>]=?|[<>]=?\s*maxWait')

    def test_no_minute_bound_anywhere(self):
        # The brief mentions a "15 minutes" queue bound; neither the gate nor its docs contain one.
        for path in (GATE, 'docs/M3-GATE.md', 'docs/COMPLETION-PLAN.md', 'source/OpenTPW.Tests/M3GateTests.cs'):
            self.assertNotRegex(show(path), r'(?i)\b(15|fifteen)[ -]?min|900 ?s\b', path)

    def test_time_mapping_follows_park_calendar_constants(self):
        calendar = show('source/OpenTPW/Economy/ParkCalendar.cs')
        self.assertIn('[BIN:STP-PPC:0x101C22E0 scheduler]', calendar)
        self.assertIn('[BIN:STP-PPC:0x100E4394 calendar conversion]', calendar)
        self.assertIn('[APPROX:ECON-001]', calendar)
        turn_ms = int(re.search(r'TurnMilliseconds = (\d+);', calendar).group(1))
        per_turn = eval(re.search(r'SecondsPerTurn = ([\d /]+);', calendar).group(1))  # "15000 / 4"
        ticks = 30 * 60 * 60
        turns = ticks * 1000 // (60 * turn_ms)
        self.assertEqual((248, 3750, 108_000, 7258), (turn_ms, per_turn, ticks, turns))
        seconds = turns * per_turn
        self.assertEqual(315, seconds // 86_400)
        end = datetime.datetime(2000, 1, 1) + datetime.timedelta(seconds=seconds)
        self.assertEqual((2000, 11, 11), (end.year, end.month, end.day))
        # Thirty park-clock minutes are less than one turn.
        self.assertLess(30 * 60, per_turn)

    def test_completion_plan_does_not_say_in_game_minutes(self):
        plan = show('docs/COMPLETION-PLAN.md')
        self.assertIn('30 minutes of accelerated headless running', plan)
        self.assertNotRegex(plan, r'(?i)in-game minute')

    def test_attraction_id_counter_is_process_wide(self):
        runtime = show('source/OpenTPW/World/Objects/OriginalObjectRuntime.cs')
        self.assertIn('private static int nextAttractionId = 1;', runtime)
        self.assertIn('Interlocked.Increment( ref nextAttractionId )', runtime)
        guests = show('source/OpenTPW/World/Guests/GuestSimulation.cs')
        self.assertIn('Add( guest.AttractionId );', method_body(guests, 'ComputeStateHash'))
        # Attractions are ordered by id; a monotone counter keeps the relative order, so only the hash input changes.
        self.assertIn('attractions.Sort( ( a, b ) => a.AttractionId.CompareTo( b.AttractionId ) );', guests)

    def test_only_the_gate_writes_exit_code(self):
        files = git('ls-tree', '-r', '--name-only', SUBJECT, 'source/').split()
        writers = []
        for path in (p for p in files if p.endswith('.cs')):
            text = show(path)
            if re.search(r'Environment\.ExitCode\s*=', text):
                writers.append(path)
        self.assertEqual(['source/OpenTPW/Client/Game.cs'], writers)
        self.assertIn('return Environment.ExitCode;', show('source/OpenTPW/Program.cs'))

    def test_report_only_row_still_sets_exit_code(self):
        # determinism.same-seed is labelled "report only" but HasFailures counts every Fail row.
        gate = show(GATE)
        self.assertIn('public bool HasFailures => Rows.Any( row => row.Verdict == M3GateVerdict.Fail );', gate)
        self.assertIn('matches ? M3GateVerdict.Pass : M3GateVerdict.Fail', gate)


# ---------------------------------------------------------------- mutation harness

HOOKS = [
    # (anchor, replacement)
    ('\t\t\tguests.Tick( Tick );\n',
     '\t\t\tif ( !(Mutation == "zero-step" && tick == 1000) )\n\t\t\t\tguests.Tick( Tick );\n'),
    ('\t\t\tforeach ( var item in placed )\n\t\t\t\titem.Runtime.Simulate( Tick );\n\t\t\truntime.FixedTick();\n',
     '\t\t\tforeach ( var item in placed )\n\t\t\t\titem.Runtime.Simulate( Mutation == "negative-script-step" && tick == 1000 ? -Tick : Tick );\n'
     '\t\t\truntime.FixedTick();\n\t\t\tMutateAfterTick( tick );\n'),
    ('\t\tforeach ( var type in new[] { StaffType.Mechanic, StaffType.Handyman } )\n',
     '\t\tforeach ( var type in new[] { StaffType.Mechanic, StaffType.Handyman }.Where( type => !(Mutation == "no-handyman" && type == StaffType.Handyman) && !(Mutation == "no-mechanic" && type == StaffType.Mechanic) ) )\n'),
    ('\tprivate string AttractionName( int id )',
     '\tinternal static readonly string Mutation = Environment.GetEnvironmentVariable( "M3GATE_MUTATION" ) ?? "";\n'
     '\tprivate void MutateAfterTick( long tick )\n\t{\n'
     '\t\tvar ride = placed.FirstOrDefault( item => item.Role == "attraction" );\n'
     '\t\tvar toilet = placed.FirstOrDefault( item => item.Role == "toilet" );\n'
     '\t\tswitch ( Mutation )\n\t\t{\n'
     '\t\t\tcase "ledger-skew" when tick == 2000:\n'
     '\t\t\t\ttypeof( ParkLedger ).GetProperty( "Balance" )!.SetValue( economy.Ledger, economy.Ledger.Balance + 1 );\n\t\t\t\tbreak;\n'
     '\t\t\tcase "cut-path" when tick == 1000:\n'
     '\t\t\t\tgrid.SetPath( spine[1].X, spine[1].Y, false );\n\t\t\t\tbreak;\n'
     '\t\t\tcase "cut-entrance" when tick == 36000:\n\t\t\tcase "cut-entrance-transient" when tick == 36000:\n'
     '\t\t\t\tgrid.SetPath( ride!.Runtime.Visitors.EntranceCell.X, ride.Runtime.Visitors.EntranceCell.Y, false );\n\t\t\t\tbreak;\n'
     '\t\t\tcase "cut-entrance-transient" when tick == 36600:\n'
     '\t\t\t\tgrid.SetPath( ride!.Runtime.Visitors.EntranceCell.X, ride.Runtime.Visitors.EntranceCell.Y, true );\n\t\t\t\tbreak;\n'
     '\t\t\tcase "block-boarding" when tick >= 18000:\n'
     '\t\t\t\tride!.Runtime.SetVariable( "VAR_LETMEON", -1 );\n\t\t\t\tbreak;\n'
     '\t\t\tcase "stall-ride" when tick == 18000:\n'
     '\t\t\t\tride!.Runtime.Stop();\n\t\t\t\tbreak;\n'
     '\t\t\tcase "stall-ride-from-start" when tick == 1:\n'
     '\t\t\t\tride!.Runtime.Stop();\n\t\t\t\tbreak;\n'
     '\t\t\tcase "close-toilet" when tick == 1:\n'
     '\t\t\t\ttoilet!.Runtime.Close();\n\t\t\t\tbreak;\n'
     '\t\t\tcase "idle-mechanic":\n\t\t\tcase "idle-handyman":\n'
     '\t\t\t\tforeach ( var member in economy.Staff.Members.Where( member => member.Type == (Mutation == "idle-mechanic" ? StaffType.Mechanic : StaffType.Handyman) ) )\n'
     '\t\t\t\t\tmember.State = StaffState.PickedUp;\n\t\t\t\tbreak;\n'
     '\t\t}\n\t}\n\n'
     '\tprivate string AttractionName( int id )'),
    ('\t\tvar second = M3GateRun.Execute( options, ticks );\n',
     '\t\tif ( M3GateRun.Mutation == "reset-ids" )\n'
     '\t\t\ttypeof( OriginalObjectRuntime ).GetField( "nextAttractionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static )!.SetValue( null, 1 );\n'
     '\t\tvar second = M3GateRun.Execute( options, ticks );\n'),
]


def instrument(text: str) -> str:
    for anchor, replacement in HOOKS:
        if text.count(anchor) != 1:
            raise AssertionError(f'hook anchor not unique/absent: {anchor[:60]!r}')
        text = text.replace(anchor, replacement)
    return text


DOTNET = os.environ.get('OPENTPW_DOTNET', str(Path.home() / '.local/share/opentpw-dotnet10/dotnet'))
GAME = os.environ.get('OPENTPW_GAME_PATH', '')
MUTATE = os.environ.get('OPENTPW_M3_MUTATE') == '1'


@unittest.skipUnless(MUTATE and GAME and Path(GAME).is_dir() and Path(DOTNET).exists() and has_commit(MUTATION_SUBJECT),
                     'set OPENTPW_M3_MUTATE=1, OPENTPW_GAME_PATH and a .NET 10 SDK (OPENTPW_DOTNET)')
class Mutations(unittest.TestCase):
    results: dict[str, dict] = {}

    @classmethod
    def setUpClass(cls):
        # Resolve the macOS /var -> /private/var symlink: with an unresolved solution path NuGet restores every project
        # under two identities and the build writes an OpenTPW.deps.json without the project references.
        cls.tmp = Path(tempfile.mkdtemp(prefix='m3-gate-v-')).resolve()
        archive = subprocess.run(['git', '-C', str(REPO), 'archive', MUTATION_SUBJECT], check=True, capture_output=True).stdout
        subprocess.run(['tar', '-x', '-C', str(cls.tmp)], input=archive, check=True)
        gate = cls.tmp / GATE
        gate.write_text(instrument(gate.read_text(encoding='utf-8-sig')), encoding='utf-8')
        solution = str(cls.tmp / 'source/OpenTPW.sln')
        # OpenTPW.csproj alone does not build (ImageSharp comes through the solution).
        build = subprocess.run([DOTNET, 'restore', solution, '--disable-parallel', '-v', 'q'], capture_output=True, text=True)
        if build.returncode == 0:
            build = subprocess.run([DOTNET, 'build', solution, '-c', 'Release', '-nologo', '-v', 'q', '--no-restore'],
                                   capture_output=True, text=True)
        if build.returncode != 0:
            raise AssertionError(build.stdout[-4000:])
        cls.dll = cls.tmp / 'source/OpenTPW/bin/Release/net10.0/OpenTPW.dll'

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    @classmethod
    def run_gate(cls, mutation: str, determinism: bool = False) -> dict:
        key = f'{mutation}/{determinism}'
        if key not in cls.results:
            report = cls.tmp / f'{mutation or "none"}-{determinism}.json'
            args = [DOTNET, str(cls.dll), '--game-path', GAME, '--m3-gate', '--report', str(report)]
            if not determinism:
                args.append('--no-determinism')
            proc = subprocess.run(args, capture_output=True, text=True, env={**os.environ, 'M3GATE_MUTATION': mutation}, timeout=600)
            data = json.loads(report.read_text()) if report.exists() else {}
            cls.results[key] = {'exit': proc.returncode, 'stdout': proc.stdout, 'stderr': proc.stderr[-3000:],
                                'rows': {row['id']: row for row in data.get('rows', [])}}
        return cls.results[key]

    def verdicts(self, mutation: str, determinism: bool = False) -> dict[str, str]:
        result = self.run_gate(mutation, determinism)
        self.assertTrue(result['rows'], result['stderr'] or result['stdout'])
        return {key: row['verdict'] for key, row in result['rows'].items()}

    def test_baseline(self):
        verdicts = self.verdicts('', determinism=True)
        fails = sorted(key for key, value in verdicts.items() if value == 'fail')
        # GATE-FIX: build.paths fails (no in-game path builder).
        self.assertEqual(['build.paths', 'build.queue', 'determinism.same-seed'], fails)
        self.assertTrue(self.run_gate('', True)['rows']['build.paths']['evidence']['inGameBuilder'].startswith('no in-game path builder'))
        self.assertEqual(['queues.no-stuck-queue'], [key for key, value in verdicts.items() if value == 'unresolved'])
        self.assertEqual(1, self.run_gate('', True)['exit'])

    def test_zero_guest_step_flips_time(self):
        row = self.run_gate('zero-step')['rows']['time.monotonic']
        self.assertEqual(('fail', 1000), (row['verdict'], row['firstViolationTick']), row)

    def test_negative_script_step_is_rejected_before_the_sampler(self):
        # GuestSimulation.Tick and the ride VM throw on a negative step, so the negative branch of time.monotonic
        # is defence in depth: the process exits 1 through Program.Main's catch, with no report.
        result = self.run_gate('negative-script-step')
        self.assertEqual(1, result['exit'])
        self.assertEqual({}, result['rows'])
        self.assertIn("ArgumentOutOfRangeException", result['stderr'])
        self.assertIn("deltaSeconds", result['stderr'])

    def test_unbalanced_ledger_flips_consistency(self):
        verdicts = self.verdicts('ledger-skew')
        row = self.run_gate('ledger-skew')['rows']['economy.ledger-consistent']
        self.assertEqual(('fail', 2000), (row['verdict'], row['firstViolationTick']), row)
        self.assertEqual('pass', verdicts['economy.income-and-expenses'])

    def test_cut_path_flips_reachability_and_flow(self):
        verdicts = self.verdicts('cut-path')
        row = self.run_gate('cut-path')['rows']['paths.no-unreachable-goal']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual('Belly Bounce, Drinks Shop, Small Toilet', row['evidence']['unreachableAttractions'])
        self.assertEqual('fail', verdicts['guests.flow'])

    def test_transient_entrance_cut_flips_reachability_through_guest_sampler(self):
        # The end-of-run target check passes again after the repair; only the per-tick guest sampler fails it.
        row = self.run_gate('cut-entrance-transient')['rows']['paths.no-unreachable-goal']
        self.assertEqual('none', row['evidence']['unreachableAttractions'])
        self.assertEqual(('fail', 36001), (row['verdict'], row['firstViolationTick']), row)
        self.assertGreater(row['evidence']['guestsHeadingToUnreachableTarget'], 0)

    def test_stopped_ride_fails_scripts_run_and_its_frozen_script_clock(self):
        verdicts = self.verdicts('stall-ride')
        rows = self.run_gate('stall-ride')['rows']
        self.assertEqual(('fail', 18001), (verdicts['time.monotonic'], rows['time.monotonic']['firstViolationTick']))
        self.assertEqual('unresolved', verdicts['queues.no-stuck-queue'])
        # GATE-FIX: a Halted script on an open ride fails the row, from the tick it was stopped (review: it passed).
        self.assertEqual(('fail', 18000), (verdicts['rides.scripts-run'], rows['rides.scripts-run']['firstViolationTick']))
        self.assertEqual('Belly Bounce', rows['rides.scripts-run']['evidence']['notRunningAtEnd'])
        self.assertEqual(['build.paths', 'build.queue', 'rides.scripts-run', 'time.monotonic'],
                         sorted(key for key, value in verdicts.items() if value == 'fail'))
        self.assertEqual(1, self.run_gate('stall-ride')['exit'])

    def test_ride_stopped_from_start(self):
        verdicts = self.verdicts('stall-ride-from-start')
        self.assertEqual('fail', verdicts['guests.flow'])
        self.assertEqual('fail', verdicts['time.monotonic'])
        self.assertEqual('fail', verdicts['rides.scripts-run'])  # GATE-FIX
        self.assertEqual('unresolved', verdicts['queues.no-stuck-queue'])

    def test_queue_that_never_boards_again_fails_no_row_but_cannot_exit_zero(self):
        # Main finding: from minute 5 the ride's script runs and stays open, but never takes the offered guest.
        # The queue stays full and nobody boards for the last 25 minutes; the gate adds no failing row (only the
        # baseline build rows fail) because the queue row is unresolved by construction.
        # GATE-FIX regression: the exit code is never 0 here. Today it is 1 (build.paths, build.queue); with those
        # fixed the unresolved queue row alone gives 2 (M3GateTests.ReportListsEveryRowAndFailsOnAnyFailure).
        verdicts = self.verdicts('block-boarding')
        evidence = self.run_gate('block-boarding')['rows']['queues.no-stuck-queue']['evidence']
        self.assertNotEqual(0, self.run_gate('block-boarding')['exit'])
        self.assertEqual(['build.paths', 'build.queue'], sorted(key for key, value in verdicts.items() if value == 'fail'))
        self.assertEqual('unresolved', verdicts['queues.no-stuck-queue'])
        self.assertGreater(evidence['attractionLongestStallSeconds'], 1400)
        self.assertGreater(evidence['stillQueuedAtEndMaxSeconds'], 1500)
        self.assertEqual('20/20', evidence['attractionMaxQueue'])

    def test_closed_toilet(self):
        row = self.run_gate('close-toilet')['rows']['guests.flow']
        self.assertEqual('fail', row['verdict'], row)
        self.assertEqual('toilet', row['evidence']['missingStages'])

    def test_staff_work_requires_each_staff_type(self):
        no_handyman = self.run_gate('no-handyman')['rows']
        no_mechanic = self.run_gate('no-mechanic')['rows']
        self.assertEqual('fail', no_handyman['build.staff']['verdict'])
        self.assertEqual('fail', no_mechanic['build.staff']['verdict'])
        # Without a handyman litter is never cleaned. GATE-FIX: staff.work now fails (review: it passed on repairs).
        self.assertEqual(0, no_handyman['staff.work']['evidence']['litterCleanedItems'])
        self.assertGreater(no_handyman['staff.work']['evidence']['litterPeakItems'], 0)
        self.assertEqual('fail', no_handyman['staff.work']['verdict'])
        self.assertIn('no handyman hired', no_handyman['staff.work']['evidence']['problems'])
        # Without a mechanic nothing is repaired and the ride ends at state of repair 0; guests board exactly as often
        # as in the baseline. GATE-FIX: staff.work now fails (review: it passed on litter).
        self.assertEqual(0, no_mechanic['staff.work']['evidence']['repairs'])
        self.assertEqual(0, no_mechanic['staff.work']['evidence']['attractionStateOfRepair'])
        self.assertEqual('fail', no_mechanic['staff.work']['verdict'])
        self.assertIn('no mechanic hired', no_mechanic['staff.work']['evidence']['problems'])
        baseline = self.run_gate('')['rows']['guests.flow']['evidence']['attractionBoarded']
        self.assertEqual(baseline, no_mechanic['guests.flow']['evidence']['attractionBoarded'])
        self.assertEqual('pass', self.run_gate('')['rows']['staff.work']['verdict'])

    def test_hired_but_idle_staff_fail_staff_work(self):
        # GATE-FIX: hired staff that never work (held PickedUp, so never available) fail the row per type, while
        # build.staff still passes.
        idle_mechanic = self.run_gate('idle-mechanic')['rows']
        self.assertEqual('pass', idle_mechanic['build.staff']['verdict'])
        self.assertEqual(0, idle_mechanic['staff.work']['evidence']['repairs'])
        self.assertGreater(idle_mechanic['staff.work']['evidence']['repairsNeeded'], 0)
        self.assertEqual('fail', idle_mechanic['staff.work']['verdict'])
        self.assertIn('mechanic made no repair', idle_mechanic['staff.work']['evidence']['problems'])
        idle_handyman = self.run_gate('idle-handyman')['rows']
        self.assertEqual('pass', idle_handyman['build.staff']['verdict'])
        self.assertEqual(0, idle_handyman['staff.work']['evidence']['litterCleanedItems'])
        self.assertEqual('fail', idle_handyman['staff.work']['verdict'])
        self.assertIn('handyman cleaned no litter', idle_handyman['staff.work']['evidence']['problems'])

    def test_resetting_the_id_counter_makes_raw_hashes_match(self):
        row = self.run_gate('reset-ids', determinism=True)['rows']['determinism.same-seed']
        self.assertTrue(row['evidence']['guestHashMatches'], row)
        self.assertEqual('pass', row['verdict'])
        self.assertEqual(row['evidence']['attractionIdsRun1'], row['evidence']['attractionIdsRun2'])


if __name__ == '__main__':
    unittest.main()
