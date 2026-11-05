"""ThemeEngine loader evidence; skips without the identified private executable."""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from tpi_themeengine_oracle import verify  # noqa: E402
from x86_harness import Image, executable_path, have_unicorn  # noqa: E402

TPI = executable_path('tpi')


class ThemeEngineLoaderTests(unittest.TestCase):
    @unittest.skipUnless(have_unicorn() and TPI, 'requires Unicorn and the identified OPENTPW_TPI_EXE')
    def test_real_loader_resolves_and_writes_the_six_themeengine_words(self):
        self.assertEqual(6, verify(Image.load(TPI)))


if __name__ == '__main__':
    unittest.main()
