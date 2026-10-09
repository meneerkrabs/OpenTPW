"""Synthetic tests for run_evidence_checks.py; no original binaries, SDKs or private data needed."""
from __future__ import annotations

import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import run_evidence_checks as runner  # noqa: E402


def project(framework: str, reference: str | None = None) -> str:
    item = f'<ItemGroup><ProjectReference Include="{reference}" /></ItemGroup>' if reference else ''
    return (f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}'
            f'</TargetFramework></PropertyGroup>{item}</Project>')


class Scratch(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()


class Fixtures(Scratch):
    def test_mac_bin_sets_directory_and_data_fork_and_strips_stale_values(self):
        (self.root / runner.APP_NAME).write_bytes(b'')
        env = runner.fixture_environment({'OPENTPW_PC_DATA': '/stale', 'KEEP': '1'}, self.root, None)
        self.assertEqual(str(self.root), env['OPENTPW_PPC_BIN_ROOT'])
        self.assertEqual(str(self.root / runner.APP_NAME), env['OPENTPW_MAC_APP'])
        self.assertNotIn('OPENTPW_PC_DATA', env)
        self.assertEqual('1', env['KEEP'])

    def test_no_flags_strips_inherited_fixture_variables(self):
        env = runner.fixture_environment({'OPENTPW_MAC_APP': '/x', 'OPENTPW_PPC_BIN_ROOT': '/y'}, None, None)
        self.assertFalse(set(runner.FIXTURE_VARIABLES) & set(env))

    def test_rejects_file_as_bin_and_missing_data_fork(self):
        app = self.root / runner.APP_NAME
        app.write_bytes(b'')
        with self.assertRaises(runner.FixtureError):
            runner.fixture_environment({}, app, None)
        app.unlink()
        with self.assertRaises(runner.FixtureError):
            runner.fixture_environment({}, self.root, None)


class Discovery(Scratch):
    def test_root_and_each_lane_cover_flat_test_files_only(self):
        for path in ('test_root.py', 'lanes/a/test_a.py', 'lanes/b/test_b.py', 'lanes/b/deep/test_hidden.py'):
            (self.root / path).parent.mkdir(parents=True, exist_ok=True)
            (self.root / path).write_text('')
        (self.root / 'lanes/empty').mkdir()
        suites = runner.python_suites(self.root)
        self.assertEqual([self.root, self.root / 'lanes/a', self.root / 'lanes/b'], suites)
        self.assertEqual([os.path.join('lanes', 'b', 'deep', 'test_hidden.py')],
                         runner.uncovered_test_files(self.root, suites))

    def test_parse_unittest_counts_skips_and_failures(self):
        output = ("test_x (m.T.test_x) ... skipped 'set OPENTPW_MAC_APP'\n"
                  '----\nRan 16 tests in 0.2s\n\nFAILED (errors=1, skipped=3)\n')
        result = runner.parse_unittest(output)
        self.assertEqual((16, False, 3, 1), (result['ran'], result['ok'], result['skipped'], result['failures']))
        self.assertEqual(['set OPENTPW_MAC_APP'], result['skip_reasons'])
        self.assertEqual(0, runner.parse_unittest('no tests')['ran'])


class Dotnet(Scratch):
    def test_failure_classes_are_distinct(self):
        cases = {
            "error NU1100: Unable to resolve 'Microsoft.NETCore.App.Ref (= 8.0.31)'": 'restore-targeting-pack-unavailable',
            "Project '../x.csproj' targets 'net10.0'. It cannot be referenced by a project that targets '.NETCoreApp,Version=v8.0'.": 'incompatible-reference',
            'error NU1201: Project Files is not compatible with net8.0': 'incompatible-reference',
            'error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.': 'sdk-too-old',
            "You must install or update .NET to run this application.\nFramework: 'Microsoft.NETCore.App', version '8.0.0'": 'missing-runtime',
            'https://aka.ms/dotnet/sdk-not-found': 'global-json-sdk-not-found',
            'Unhandled exception': 'failed',
        }
        for output, expected in cases.items():
            with self.subTest(expected=expected):
                self.assertEqual(expected, runner.classify_dotnet_failure(output))

    def test_newer_project_reference_is_incompatible_and_not_run(self):
        (self.root / 'source').mkdir()
        (self.root / 'source/Files.csproj').write_text(project('net10.0'))
        tool = self.root / 'tools/Witness.csproj'
        tool.parent.mkdir()
        tool.write_text(project('net8.0', '..\\source\\Files.csproj'))
        self.assertEqual(['..\\source\\Files.csproj targets net10.0'], runner.incompatible_references(tool))
        result = runner.run_dotnet(Path('/nonexistent/dotnet'), 'lanes/x/Witness.csproj', tool)
        self.assertEqual('incompatible-reference', result['status'])
        corpus = runner.run_dotnet(Path('/nonexistent/dotnet'), 'lanes/rides/CorpusWitness.csproj', tool)
        self.assertEqual('incompatible-reference', corpus['status'])
        tool.write_text(project('net10.0', '..\\source\\Files.csproj'))
        self.assertEqual([], runner.incompatible_references(tool))

    def test_corpus_and_unregistered_harnesses_are_reported_not_run(self):
        tool = self.root / 'Tool.csproj'
        tool.write_text(project('net8.0'))
        for key in ('lanes/advisor/layer1/Layer1Corpus.csproj', 'lanes/new/Unknown.csproj'):
            with self.subTest(key=key):
                self.assertEqual('not-run', runner.run_dotnet(Path('/nonexistent/dotnet'), key, tool)['status'])


if __name__ == '__main__':
    unittest.main()
