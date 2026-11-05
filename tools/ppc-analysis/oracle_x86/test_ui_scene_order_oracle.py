"""Original scene-list and texture-width-class evidence; private-executable gated."""
from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from ui_scene_order_oracle import verify  # noqa: E402
from x86_harness import Image, executable_path, have_unicorn  # noqa: E402

TPW = executable_path('tpw')


class UiSceneOrderTests(unittest.TestCase):
    @unittest.skipUnless(have_unicorn() and TPW, 'requires Unicorn and the identified OPENTPW_TPW_EXE')
    def test_scene_attachment_order_and_width_classes(self):
        self.assertEqual(33, verify(Image.load(TPW)))


if __name__ == '__main__':
    unittest.main()
