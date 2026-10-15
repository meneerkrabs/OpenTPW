"""Synthetic tests for the round-7 repository hygiene checks; the operand audit runs only when
OPENTPW_PPC_BIN_ROOT names the identified Feral Mac bin directory.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import round7_evidence as r7  # noqa: E402
from review_evidence import Binary, APP_TOC  # noqa: E402


def project(framework: str) -> str:
    return f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{framework}</TargetFramework></PropertyGroup></Project>'


class RepoHygiene(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.repo = Path(self.temp.name)
        (self.repo / 'global.json').write_text(json.dumps({'sdk': {'version': '10.0.401'}}))

    def tearDown(self):
        self.temp.cleanup()

    def write(self, rel: str, text: str = ''):
        path = self.repo / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def test_only_other_majors_are_reported(self):
        self.write('a/A.csproj', project('net10.0'))
        self.write('b/B.csproj', project('net8.0'))
        self.write('b/obj/C.csproj', project('net8.0'))
        self.assertEqual(r7.framework_mismatches(self.repo, 10), ['b/B.csproj: net8'])

    def test_tests_below_non_packages_are_unreachable(self):
        self.write('tools/ppc-analysis/test_top.py')
        self.write('tools/ppc-analysis/lanes/x/test_hidden.py')
        self.write('tools/ppc-analysis/pkg/__init__.py')
        self.write('tools/ppc-analysis/pkg/test_found.py')
        self.write('tools/ppc-analysis/pkg/sub/test_gap.py')
        self.assertEqual(r7.undiscovered_tests(self.repo),
                         ['tools/ppc-analysis/lanes/x/test_hidden.py', 'tools/ppc-analysis/pkg/sub/test_gap.py'])

    def test_home_paths_are_found_but_placeholders_are_not(self):
        self.write('docs/a.md', 'run $OPENTPW_PPC_BIN_ROOT\n/Users/someone/bin\n')
        self.write('docs/b.md', 'see /usr/local/bin and /home/x/data\n')
        self.assertEqual(r7.home_paths(self.repo), ['docs/a.md:2', 'docs/b.md:1'])


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'set OPENTPW_PPC_BIN_ROOT for the operand audit')
class OriginalOperands(unittest.TestCase):
    def test_clock_and_advisor_operands(self):
        app = Binary(Path(os.environ['OPENTPW_PPC_BIN_ROOT']), 'SimThemePark.data', 'SimThemePark.data', APP_TOC)
        self.assertIn('controller_clock', r7.clock_and_advisor_audit(app))


if __name__ == '__main__':
    unittest.main()
