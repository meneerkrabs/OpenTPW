"""Round 12: --pc-fixture, stale fixture variables and harness registration in run_evidence_checks.py.

Synthetic only: no original assets, SDKs or private paths are needed or inferred.
"""
from __future__ import annotations

import io
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import run_evidence_checks as runner  # noqa: E402

SCIENTIST = 'lanes/economy/OriginalScientistSnapshot.Tests.csproj'
NET8 = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>'


class Scratch(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.assets = self.root / 'assets'
        self.repo = self.root / 'repo'
        self.tools = self.repo / 'tools' / 'ppc-analysis'
        (self.tools / 'lanes').mkdir(parents=True)
        self.assets.mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def fixture(self, name='Easymode.TPWI', size=runner.PC_FIXTURE_HEADER_BYTES + 16, where=None) -> Path:
        path = (where or self.assets) / name
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open('wb') as handle:
            handle.truncate(size)
        return path


class PcFixture(Scratch):
    def test_stale_inherited_value_is_stripped_without_the_flag(self):
        stale = {'OPENTPW_PC_FIXTURE': str(self.fixture()), 'KEEP': '1'}
        env = runner.fixture_environment(stale, None, None)
        self.assertNotIn('OPENTPW_PC_FIXTURE', env)
        self.assertEqual('1', env['KEEP'])

    def test_flag_replaces_a_stale_value_with_the_resolved_path(self):
        path = self.fixture()
        env = runner.fixture_environment({'OPENTPW_PC_FIXTURE': '/stale/other.TPWI'}, None, None, path, self.repo)
        self.assertEqual(str(path.resolve()), env['OPENTPW_PC_FIXTURE'])

    def test_pc_data_never_implies_the_fixture(self):
        self.fixture(where=self.assets / 'levels' / 'jungle')
        env = runner.fixture_environment({}, None, self.assets)
        self.assertEqual(str(self.assets), env['OPENTPW_PC_DATA'])
        self.assertNotIn('OPENTPW_PC_FIXTURE', env)

    def test_shape_and_location_are_rejected_before_any_lane_runs(self):
        cases = {
            'missing': self.assets / 'absent' / 'Easymode.TPWI',
            'directory': self.assets,
            'suffix': self.fixture('Easymode.TPWS'),
            'header-only': self.fixture('Short.TPWI', runner.PC_FIXTURE_HEADER_BYTES),
            'oversized': self.fixture('Big.TPWI', runner.PC_FIXTURE_MAX_BYTES + 1),
            'inside-checkout': self.fixture(where=self.repo / 'private'),
        }
        for name, path in cases.items():
            with self.subTest(name), self.assertRaises(runner.FixtureError):
                runner.fixture_environment({}, None, None, path, self.repo)
        self.assertTrue(runner.check_pc_fixture(self.fixture('easymode.tpwi'), self.repo).is_file())

    def test_main_reports_a_missing_fixture_path_as_a_fixture_error(self):
        stderr = io.StringIO()
        with redirect_stderr(stderr):
            code = runner.main(['--repo', str(self.repo), '--pc-fixture', str(self.assets / 'nope.TPWI')])
        self.assertEqual(2, code)
        self.assertIn('--pc-fixture is not a regular file', stderr.getvalue())


class Registration(Scratch):
    def test_scientist_harness_is_a_fixture_aware_self_test(self):
        self.assertEqual([], runner.SELF_TESTS[SCIENTIST])
        self.assertEqual('--fixture', runner.FIXTURE_ARGUMENTS[SCIENTIST])
        self.assertTrue(set(runner.FIXTURE_ARGUMENTS) <= set(runner.SELF_TESTS))
        self.assertFalse(set(runner.SELF_TESTS) & set(runner.NEEDS_CORPUS))

    def test_fixture_argument_only_when_the_flag_supplies_it(self):
        self.assertEqual([], runner.harness_arguments(SCIENTIST, {}))
        self.assertEqual(['--fixture', '/x/Easymode.TPWI'],
                         runner.harness_arguments(SCIENTIST, {'OPENTPW_PC_FIXTURE': '/x/Easymode.TPWI'}))
        self.assertEqual(['--self-test'], runner.harness_arguments(
            'lanes/ui/csharp/OriginalLayoutReader.csproj', {'OPENTPW_PC_FIXTURE': '/x/Easymode.TPWI'}))

    def test_registered_projects_missing_from_the_checkout_are_absent_not_covered(self):
        absent = {item['project']: item['status'] for item in runner.absent_harnesses(self.tools)}
        self.assertEqual(set(runner.SELF_TESTS) | set(runner.NEEDS_CORPUS), set(absent))
        self.assertEqual({'absent'}, set(absent.values()))
        project = self.tools / SCIENTIST
        project.parent.mkdir(parents=True)
        project.write_text(NET8)
        self.assertNotIn(SCIENTIST, {item['project'] for item in runner.absent_harnesses(self.tools)})
        self.assertIn(SCIENTIST, dict(runner.dotnet_harnesses(self.tools)))

    def test_consumers_read_the_variable_or_are_present_fixture_harnesses(self):
        self.assertEqual([], runner.fixture_consumers(self.tools, 'OPENTPW_PC_FIXTURE'))
        lane = self.tools / 'lanes' / 'rides'
        lane.mkdir()
        # Split so this file is not itself counted as a reader in the review lane.
        environ = 'os.' + 'environ'
        (lane / 'test_save.py').write_text(f'{environ}.get("OPENTPW_PC_FIXTURE")\n')
        (lane / 'helper.py').write_text(f"Path({environ}['OPENTPW_PC_FIXTURE'])\n")
        (lane / 'test_other.py').write_text('os.get' + 'env("OPENTPW_MAC_APP")\n')
        # Naming the variable in synthetic data is not a read.
        (lane / 'test_synthetic.py').write_text("env = {'OPENTPW_PC_FIXTURE': '/x'}\n")
        (self.tools / SCIENTIST).parent.mkdir(parents=True)
        (self.tools / SCIENTIST).write_text(NET8)
        rides = os.path.join('lanes', 'rides')
        self.assertEqual([os.path.join(rides, 'helper.py'), os.path.join(rides, 'test_save.py'), SCIENTIST],
                         runner.fixture_consumers(self.tools, 'OPENTPW_PC_FIXTURE'))
        self.assertEqual([os.path.join(rides, 'test_other.py')], runner.fixture_consumers(self.tools, 'OPENTPW_MAC_APP'))


@unittest.skipIf(os.name == 'nt', 'fake dotnet is a POSIX shell script')
class FakeDotnet(Scratch):
    def setUp(self):
        super().setUp()
        self.dotnet = self.root / 'sdk' / 'dotnet'
        self.dotnet.parent.mkdir()
        self.dotnet.write_text('#!/bin/sh\n'
                               'if [ "$1" = "--version" ]; then echo 8.0.0; exit 0; fi\n'
                               'echo "ENV=${OPENTPW_PC_FIXTURE:-unset}"\n'
                               'case "$*" in *--fixture*) ;; *) echo "NOT RUN: actual PC fixture";; esac\n'
                               'echo "1/1 groups passed"\n')
        self.dotnet.chmod(0o755)
        self.project = self.tools / SCIENTIST
        self.project.parent.mkdir(parents=True)
        self.project.write_text(NET8)

    def run_harness(self, env, require):
        return runner.run_dotnet(self.dotnet, SCIENTIST, self.project, env, require)

    def test_stale_process_value_does_not_reach_the_harness(self):
        with mock.patch.dict(os.environ, {'OPENTPW_PC_FIXTURE': '/stale/Easymode.TPWI'}):
            result = runner.run_dotnet(self.dotnet, SCIENTIST, self.project)
            stripped = self.run_harness(runner.fixture_environment(dict(os.environ), None, None), False)
        for item in (result, stripped):
            self.assertEqual('passed', item['status'])
            self.assertEqual([], item['arguments'])
            self.assertEqual(['NOT RUN: actual PC fixture'], item['fixture_skips'])

    def test_require_fixtures_fails_on_a_harness_fixture_skip_only(self):
        self.assertEqual('fixture-skipped', self.run_harness({}, True)['status'])
        path = self.fixture()
        env = runner.fixture_environment({}, None, None, path, self.repo)
        supplied = self.run_harness(env, True)
        self.assertEqual('passed', supplied['status'])
        self.assertEqual(['--fixture', str(path.resolve())], supplied['arguments'])
        self.assertEqual([], supplied['fixture_skips'])


class MainExitCodes(Scratch):
    def setUp(self):
        super().setUp()
        (self.tools / 'test_trivial.py').write_text(
            'import unittest\nclass T(unittest.TestCase):\n    def test_ok(self):\n        pass\n')

    def main(self, *arguments):
        with redirect_stdout(io.StringIO()) as out:
            code = runner.main(['--repo', str(self.repo), *arguments])
        return code, out.getvalue()

    def test_absent_private_fixture_does_not_fail_ci(self):
        with mock.patch.dict(os.environ, {'OPENTPW_PC_FIXTURE': '/stale/Easymode.TPWI'}):
            code, output = self.main('--json')
        self.assertEqual(0, code)
        self.assertIn('"OPENTPW_PC_FIXTURE": null', output)
        self.assertIn('"status": "absent"', output)

    def test_unconsumed_fixture_is_a_note_unless_fixtures_are_required(self):
        path = str(self.fixture())
        code, output = self.main('--pc-fixture', path)
        self.assertEqual(0, code)
        self.assertIn('note --pc-fixture (OPENTPW_PC_FIXTURE) is set but nothing in this checkout reads it', output)
        code, output = self.main('--pc-fixture', path, '--require-fixtures')
        self.assertEqual(1, code)
        self.assertIn('FAIL --pc-fixture (OPENTPW_PC_FIXTURE) is set but nothing', output)

    def test_mac_bin_is_consumed_when_either_of_its_variables_is_read(self):
        bin_root = self.root / 'bin'
        bin_root.mkdir()
        (bin_root / runner.APP_NAME).write_bytes(b'')
        (self.tools / 'reader.py').write_text('os.' + 'environ.get("OPENTPW_MAC_APP")\n')
        code, output = self.main('--mac-bin', str(bin_root), '--require-fixtures')
        self.assertEqual(0, code, output)
        self.assertNotIn('nothing in this checkout reads it', output)


if __name__ == '__main__':
    unittest.main()
