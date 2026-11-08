"""Regenerate X91's numeric fixture only when the identified private executable is supplied."""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from ui_page_alloc_golden import render  # noqa: E402
from x86_harness import executable_path, have_unicorn  # noqa: E402


class OriginalPageAllocatorOracleTests(unittest.TestCase):
    @unittest.skipUnless(have_unicorn() and executable_path('tpw'), 'requires Unicorn and identified OPENTPW_TPW_EXE')
    def test_checked_in_fixture_matches_isolated_native_execution(self):
        golden = HERE.parents[2] / 'source' / 'OpenTPW.Tests' / 'UiPageAllocGolden.cs'
        self.assertEqual(golden.read_text(), render())


if __name__ == '__main__':
    unittest.main()
