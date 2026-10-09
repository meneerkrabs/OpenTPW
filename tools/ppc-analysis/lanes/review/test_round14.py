"""Round 14: the runner counts a fixture as consumed only by what actually read it, and qualifies
harnesses whose items a project-directory scratch copy cannot reach.

Synthetic only: a fake `dotnet` script stands in for the SDK, and the fixture is a zero-filled file
of the right shape. No original assets, SDKs or private paths are needed or inferred.
"""
from __future__ import annotations

import io
import json
import os
import stat
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import run_evidence_checks as runner  # noqa: E402

SCIENTIST = 'lanes/economy/OriginalScientistSnapshot.Tests.csproj'
CONTROLLERS = 'lanes/rides/controllers/ControllersWitness.csproj'
NET8 = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>{}</Project>'
LINKED = ('<ItemGroup><None Include="../controller-contracts.json" Link="controller-contracts.json"'
          ' CopyToOutputDirectory="PreserveNewest" /></ItemGroup>')
# Fake SDK: `--version` prints a version; `run --project P -- ARGS` logs P, the working directory
# ARGS and the inherited OPENTPW_PC_FIXTURE, then prints $FAKE_OUTPUT and exits $FAKE_EXIT.
FAKE_DOTNET = '''#!/bin/sh
if [ "$1" = "--version" ]; then echo 8.0.425; exit 0; fi
printf '%s\\t%s\\t%s\\t%s\\n' "$3" "$PWD" "$*" "${OPENTPW_PC_FIXTURE:-}" >> "$FAKE_LOG"
printf '%b' "$FAKE_OUTPUT"
exit "${FAKE_EXIT:-0}"
'''


@unittest.skipUnless(os.name == 'posix', 'fake dotnet is a POSIX shell script')
class Scratch(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.repo = self.root / 'repo'
        self.tools = self.repo / 'tools' / 'ppc-analysis'
        (self.tools / 'lanes').mkdir(parents=True)
        (self.tools / 'test_top.py').write_text('import unittest\nclass T(unittest.TestCase):\n    def test_a(self): pass\n')
        self.fixture = self.root / 'assets' / 'Easymode.TPWI'
        self.fixture.parent.mkdir()
        with self.fixture.open('wb') as handle:
            handle.truncate(runner.PC_FIXTURE_HEADER_BYTES + 16)
        self.dotnet = self.root / 'sdk' / 'dotnet'
        self.dotnet.parent.mkdir()
        self.dotnet.write_text(FAKE_DOTNET)
        self.dotnet.chmod(self.dotnet.stat().st_mode | stat.S_IXUSR)
        self.log = self.root / 'dotnet.log'
        self.fake = {'FAKE_LOG': str(self.log), 'FAKE_OUTPUT': 'self-test ok\\n', 'FAKE_EXIT': '0',
                     'OPENTPW_PC_FIXTURE': str(self.root / 'stale.TPWI')}

    def tearDown(self):
        self.temp.cleanup()

    def project(self, key: str, items: str = '') -> Path:
        path = self.tools / key
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(NET8.format(items))
        return path

    def report(self, *arguments, **fake) -> tuple[int, dict]:
        with mock.patch.dict(os.environ, {**self.fake, **fake}), redirect_stdout(io.StringIO()) as out:
            code = runner.main(['--repo', str(self.repo), '--json', *arguments])
        return code, json.loads(out.getvalue())

    def runs(self) -> list[list[str]]:
        return [line.split('\t') for line in self.log.read_text().splitlines()] if self.log.exists() else []

    def item(self, report: dict, key: str) -> dict:
        return next(entry for entry in report['dotnet'] if entry['project'] == key)


class ActualFixtureUse(Scratch):
    def test_present_harness_that_never_ran_does_not_consume_the_fixture(self):
        # Round 13 MEDIUM: 62c0a2e shape, no Python reader, no --dotnet. This exited 0.
        self.project(SCIENTIST)
        code, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures')
        self.assertEqual(1, code)
        self.assertEqual([], report['fixture_consumers']['OPENTPW_PC_FIXTURE'])
        self.assertEqual(['--pc-fixture'], report['unconsumed_fixtures'])
        self.assertEqual('not-run', self.item(report, SCIENTIST)['status'])
        self.assertEqual([], self.runs())

    def test_without_require_fixtures_the_unconsumed_fixture_is_only_a_note(self):
        self.project(SCIENTIST)
        code, report = self.report('--pc-fixture', str(self.fixture))
        self.assertEqual(0, code)
        self.assertEqual(['--pc-fixture'], report['unconsumed_fixtures'])

    def test_harness_that_passed_with_the_fixture_argument_consumes_it(self):
        self.project(SCIENTIST)
        code, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures', '--dotnet', str(self.dotnet))
        self.assertEqual(0, code)
        self.assertEqual([SCIENTIST], report['fixture_consumers']['OPENTPW_PC_FIXTURE'])
        self.assertEqual([], report['unconsumed_fixtures'])
        (run,) = self.runs()
        self.assertTrue(run[2].endswith(f'--fixture {self.fixture.resolve()}'), run)

    def test_harness_that_skipped_or_failed_does_not_consume_it(self):
        self.project(SCIENTIST)
        for fake, status in (({'FAKE_OUTPUT': 'NOT RUN: fixture unreadable\\n'}, 'passed'),
                             ({'FAKE_EXIT': '1', 'FAKE_OUTPUT': 'boom\\n'}, 'failed')):
            with self.subTest(status):
                code, report = self.report('--pc-fixture', str(self.fixture), '--dotnet', str(self.dotnet), **fake)
                self.assertEqual([], report['fixture_consumers']['OPENTPW_PC_FIXTURE'])
                self.assertEqual(['--pc-fixture'], report['unconsumed_fixtures'])
                self.assertEqual(status, self.item(report, SCIENTIST)['status'])
                self.assertEqual(0 if status == 'passed' else 1, code)
        code, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures', '--dotnet', str(self.dotnet),
                                   FAKE_OUTPUT='NOT RUN: fixture unreadable\\n')
        self.assertEqual(1, code)
        self.assertEqual('fixture-skipped', self.item(report, SCIENTIST)['status'])

    def test_python_reader_still_consumes_without_dotnet(self):
        lane = self.tools / 'lanes' / 'economy'
        lane.mkdir(parents=True)
        (lane / 'test_reader.py').write_text(
            'import os, unittest\nclass T(unittest.TestCase):\n'
            "    def test_a(self): self.assertTrue(os.environ.get('OPENTPW_PC_FIXTURE'))\n")
        self.project(SCIENTIST)
        code, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures')
        self.assertEqual(0, code)
        self.assertEqual(['lanes/economy/test_reader.py'], report['fixture_consumers']['OPENTPW_PC_FIXTURE'])

    def test_absent_registered_harness_is_neither_consumer_nor_coverage(self):
        code, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures', '--dotnet', str(self.dotnet))
        self.assertEqual(1, code)
        self.assertEqual('absent', self.item(report, SCIENTIST)['status'])
        self.assertEqual(['--pc-fixture'], report['unconsumed_fixtures'])
        self.assertEqual([], self.runs())


class IsolationPreserved(Scratch):
    def test_each_harness_runs_from_its_own_scratch_copy_outside_the_checkout(self):
        for key in ('lanes/economy/OriginalLoanRules.Tests.csproj', SCIENTIST):
            self.project(key)
        code, _ = self.report('--dotnet', str(self.dotnet))
        self.assertEqual(0, code)
        runs = self.runs()
        self.assertEqual(2, len(runs))
        self.assertEqual(2, len({run[1] for run in runs}))
        for project, cwd, arguments, fixture in runs:
            self.assertFalse(Path(project).resolve().is_relative_to(self.repo.resolve()), project)
            self.assertTrue(Path(project).resolve().is_relative_to(Path(cwd).resolve()), (project, cwd))
            self.assertNotIn('--fixture', arguments)
            self.assertEqual('', fixture)
        self.assertFalse(any(self.tools.rglob('bin')) or any(self.tools.rglob('obj')))


class DependencyCopy(Scratch):
    def test_includes_that_climb_out_of_the_project_directory_are_listed(self):
        path = self.project(CONTROLLERS, '<ItemGroup><None Include="../controller-contracts.json" Link="c.json" />'
                                         '<Compile Include="sub/../Local.cs" /><Compile Include="./x/../../Up.cs" />'
                                         '<EmbeddedResource Include="$(MSBuildThisFileDirectory)../r.json" />'
                                         '<Content\n  Include="..\\..\\multi.json" />'
                                         '<ProjectReference Include="..\\..\\..\\..\\source\\A.csproj" /></ItemGroup>')
        self.assertEqual(['None ../controller-contracts.json', 'Compile ./x/../../Up.cs',
                          'EmbeddedResource $(MSBuildThisFileDirectory)../r.json', 'Content ..\\..\\multi.json',
                          'ProjectReference ..\\..\\..\\..\\source\\A.csproj'], runner.linked_outside_copy(path))

    def test_unregistered_controllers_witness_is_qualified_not_run(self):
        self.project(CONTROLLERS, LINKED)
        for arguments in ((), ('--dotnet', str(self.dotnet))):
            with self.subTest(arguments):
                code, report = self.report(*arguments)
                item = self.item(report, CONTROLLERS)
                self.assertEqual(0, code)
                self.assertEqual('not-run', item['status'])
                self.assertIn('no registered self-test', item['reason'])
                self.assertIn('../controller-contracts.json', item['reason'])
        self.assertEqual([], self.runs())

    def test_registered_linked_harness_fails_closed_before_building(self):
        self.project(CONTROLLERS, LINKED)
        with mock.patch.dict(runner.SELF_TESTS, {CONTROLLERS: []}):
            code, report = self.report('--dotnet', str(self.dotnet))
        self.assertEqual(1, code)
        self.assertEqual('linked-outside-copy', self.item(report, CONTROLLERS)['status'])
        self.assertIn('None ../controller-contracts.json', self.item(report, CONTROLLERS)['reason'])
        self.assertEqual([], self.runs())

    def test_registered_self_tests_in_this_checkout_stay_inside_their_copy(self):
        tools = Path(runner.__file__).resolve().parent
        for key in runner.SELF_TESTS:
            if (tools / key).is_file():
                with self.subTest(key):
                    self.assertEqual([], runner.linked_outside_copy(tools / key))


if __name__ == '__main__':
    unittest.main()
