"""Round 15: --pc-fixture also reaches clock's saved-epoch witnesses, which read the identified save
as OPENTPW_PPC_SAVE_PATH, and a stale shell value of that variable no longer decides whether they run.

Before this round the runner neither set nor stripped OPENTPW_PPC_SAVE_PATH, so on committed batch2
ef6ff14 the four clock real-save tests were skipped under --pc-fixture (and would have failed
--require-fixtures), or ran against whatever an inherited shell variable named.

Synthetic only: a probe suite records the variables it sees; the fixture is a zero-filled file of the
right shape. No original assets or private paths are needed.
"""
from __future__ import annotations

import io
import json
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import run_evidence_checks as runner  # noqa: E402

PROBE = '''import os, unittest
class T(unittest.TestCase):
    @unittest.skipUnless(os.environ.get('OPENTPW_PPC_SAVE_PATH'), 'requires identified PC save')
    def test_save(self):
        with open(os.environ['PROBE_LOG'], 'a') as log:
            log.write(os.environ['OPENTPW_PPC_SAVE_PATH'] + '\\n')
'''


class SavePathFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.repo = self.root / 'repo'
        clock = self.repo / 'tools' / 'ppc-analysis' / 'lanes' / 'clock'
        clock.mkdir(parents=True)
        (clock / 'test_probe.py').write_text(PROBE)
        (clock.parents[1] / 'test_top.py').write_text('import unittest\nclass T(unittest.TestCase):\n    def test_a(self): pass\n')
        self.fixture = self.root / 'assets' / 'Easymode.TPWI'
        self.fixture.parent.mkdir()
        with self.fixture.open('wb') as handle:
            handle.truncate(runner.PC_FIXTURE_HEADER_BYTES + 16)
        self.log = self.root / 'probe.log'
        self.stale = str(self.root / 'stale.TPWI')

    def tearDown(self):
        self.temp.cleanup()

    def report(self, *arguments) -> tuple[int, dict]:
        env = {'PROBE_LOG': str(self.log), 'OPENTPW_PPC_SAVE_PATH': self.stale}
        with mock.patch.dict(os.environ, env), redirect_stdout(io.StringIO()) as out:
            code = runner.main(['--repo', str(self.repo), '--json', *arguments])
        return code, json.loads(out.getvalue())

    def seen(self) -> list[str]:
        return self.log.read_text().splitlines() if self.log.exists() else []

    def clock(self, report: dict) -> dict:
        return next(suite for suite in report['python'] if suite['suite'] == 'lanes/clock')

    def test_pc_fixture_sets_the_clock_save_variable(self):
        code, report = self.report('--pc-fixture', str(self.fixture), '--require-fixtures')
        self.assertEqual(0, code)
        self.assertEqual([str(self.fixture.resolve())], self.seen())
        self.assertEqual(0, self.clock(report)['skipped'])
        self.assertEqual(str(self.fixture.resolve()), report['fixtures']['OPENTPW_PPC_SAVE_PATH'])
        self.assertEqual(['lanes/clock/test_probe.py'], report['fixture_consumers']['OPENTPW_PPC_SAVE_PATH'])
        self.assertEqual([], report['unconsumed_fixtures'])

    def test_stale_save_variable_is_stripped_without_the_flag(self):
        code, report = self.report()
        self.assertEqual(0, code)
        self.assertEqual([], self.seen())
        self.assertEqual(1, self.clock(report)['skipped'])
        self.assertIsNone(report['fixtures']['OPENTPW_PPC_SAVE_PATH'])

    def test_require_fixtures_still_fails_a_skip_when_no_save_is_supplied(self):
        code, _ = self.report('--require-fixtures')
        self.assertEqual(1, code)

    def test_flag_maps_both_names_from_one_checked_path(self):
        self.assertEqual(('OPENTPW_PC_FIXTURE', 'OPENTPW_PPC_SAVE_PATH'), runner.FIXTURE_FLAGS['--pc-fixture'])
        self.assertIn('OPENTPW_PPC_SAVE_PATH', runner.FIXTURE_VARIABLES)
        with self.assertRaises(runner.FixtureError):
            runner.fixture_environment({}, None, None, self.root / 'Easymode.sav', self.repo)


if __name__ == '__main__':
    unittest.main()
