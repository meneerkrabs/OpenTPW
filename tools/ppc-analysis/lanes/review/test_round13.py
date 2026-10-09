"""Round 13: runner 0f4ae55 fixture trust and coverage against realistic absences and linked projects.

Synthetic only: no original assets, SDKs or private paths are needed or inferred.
"""
from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import run_evidence_checks as runner  # noqa: E402
import round13_evidence as r13  # noqa: E402

SCIENTIST = 'lanes/economy/OriginalScientistSnapshot.Tests.csproj'
NET8 = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>{}</Project>'


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

    def tearDown(self):
        self.temp.cleanup()

    def project(self, key: str, items: str = '') -> Path:
        path = self.tools / key
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(NET8.format(items))
        return path

    def report(self, *arguments) -> tuple[int, dict]:
        with redirect_stdout(io.StringIO()) as out:
            code = runner.main(['--repo', str(self.repo), '--json', *arguments])
        return code, json.loads(out.getvalue())


class VacuousFixtureConsumer(Scratch):
    def test_present_but_unrun_harness_is_the_only_consumer_without_dotnet(self):
        # 62c0a2e shape: the scientist harness is present and no Python source reads the variable.
        self.project(SCIENTIST)
        _, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures')
        self.assertEqual([SCIENTIST], report['fixture_consumers']['OPENTPW_PC_FIXTURE'])
        self.assertEqual([], report['unconsumed_fixtures'])
        self.assertEqual(['OPENTPW_PC_FIXTURE'], r13.vacuous_fixture_consumers(report, runner.FIXTURE_ARGUMENTS))

    def test_a_python_reader_or_a_passing_fixture_run_is_not_vacuous(self):
        report = {'fixture_consumers': {'OPENTPW_PC_FIXTURE': [SCIENTIST]},
                  'dotnet': [{'project': SCIENTIST, 'status': 'passed', 'arguments': ['--fixture', '/x.TPWI']}]}
        self.assertEqual([], r13.vacuous_fixture_consumers(report, runner.FIXTURE_ARGUMENTS))
        report['dotnet'][0]['status'] = 'fixture-skipped'
        self.assertEqual(['OPENTPW_PC_FIXTURE'], r13.vacuous_fixture_consumers(report, runner.FIXTURE_ARGUMENTS))
        report['fixture_consumers']['OPENTPW_PC_FIXTURE'].append('lanes/rides/test_evidence.py')
        self.assertEqual([], r13.vacuous_fixture_consumers(report, runner.FIXTURE_ARGUMENTS))

    def test_absent_registered_harness_is_neither_consumer_nor_coverage(self):
        _, report = self.report('--pc-fixture', str(self.fixture))
        self.assertEqual(['--pc-fixture'], report['unconsumed_fixtures'])
        absent = {item['project'] for item in report['dotnet'] if item['status'] == 'absent'}
        self.assertIn(SCIENTIST, absent)


class LinkedProjects(Scratch):
    def test_includes_that_climb_out_of_the_project_directory_are_listed(self):
        path = self.project('lanes/rides/controllers/ControllersWitness.csproj',
                            '<ItemGroup><None Include="../controller-contracts.json" Link="c.json" />'
                            '<Compile Include="sub/../Local.cs" /><Compile Include="./x/../../Up.cs" />'
                            '<ProjectReference Include="..\\..\\..\\..\\source\\A.csproj" /></ItemGroup>')
        self.assertEqual(['None ../controller-contracts.json', 'Compile ./x/../../Up.cs',
                          'ProjectReference ..\\..\\..\\..\\source\\A.csproj'], r13.linked_outside_copy(path))

    def test_registered_self_tests_in_this_checkout_stay_inside_their_copy(self):
        tools = Path(runner.__file__).resolve().parent
        for key in runner.SELF_TESTS:
            if (tools / key).is_file():
                with self.subTest(key):
                    self.assertEqual([], r13.linked_outside_copy(tools / key))


if __name__ == '__main__':
    unittest.main()
