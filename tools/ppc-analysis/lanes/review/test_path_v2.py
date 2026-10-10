"""PATH-V2: second review round of the path stack, fixes 3d78a8a (ESC-FIX) and 267c420 on d21fb4a.

Reuses the round-1 witness's own PEF reader and field decoder (test_path_v1.py, which imports nothing from the
PATH-R lane). New here:

- Binary (OPENTPW_MAC_BIN): the operands 267c420 cites for B2 (the start/end compare 0x7105c..0x71078, the
  count test and the skip to 0x71174), the order and modes of all eight LayLine calls in the commit block,
  and the end test 0x7121c..0x71238, which ends the tool on end == start without reading the flag.
- Git (OPENTPW_REVIEW_REPO, default this repository): each round-1 finding's fix as it stands in 267c420,
  the register, line endings, and the GATE-FIX2 merge (39ccda4) conflicting in exactly two files.

Bounded: static reading only, nothing original is executed. The behaviour checks (mutation runs, smokes,
the merged-tree build) are scratch runs described in docs/reverse/REVIEW-PATH.md, Round 2.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_path_v1 as v1  # noqa: E402

REPO = v1.REPO
IMPL, ESC, FIX, GATE = 'd21fb4a', '3d78a8a', '267c420', '39ccda4'
LAYLINE = 0x84EA4


class Binary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if 'OPENTPW_MAC_BIN' not in os.environ:
            raise unittest.SkipTest('OPENTPW_MAC_BIN is not set')
        raw = (Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data').read_bytes()
        if v1.hashlib.sha256(raw).hexdigest() != v1.DATA_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.c = v1.Code(*v1.load_sections(raw))

    def test_b2_compare_is_start_against_end(self):
        c = self.c
        # r30/r31 point at the start (passed as LayLine x0/y0 at 0x710b0/0x710b8); sp+2536/2540 hold the end (&x1/&y1).
        self.assertEqual(c.fields(0x710b0), (32, 4, 30, 0))
        self.assertEqual(c.fields(0x710b4), (14, 6, 1, 2536))
        self.assertEqual(c.fields(0x710b8), (32, 5, 31, 0))
        self.assertEqual(c.fields(0x710bc), (14, 7, 1, 2540))
        for load_start, load_end, compare, branch, start_reg, offset in ((0x7105c, 0x71060, 0x71064, 0x71068, 30, 2536),
                                                                         (0x7106c, 0x71070, 0x71074, 0x71078, 31, 2540)):
            self.assertEqual(c.fields(load_start), (32, 3, start_reg, 0))
            self.assertEqual(c.fields(load_end), (32, 0, 1, offset))
            self.assertEqual(c.cmpw(compare), (3, 0))
            bo, bi, target = c.cond(branch)
            self.assertEqual((bo, bi & 3, target), (v1.BRANCH_IF_FALSE, v1.EQ, 0x71088), 'a different coordinate goes to the push')
        self.assertEqual(c.call(0x7107c), 0x7BCE8)
        self.assertEqual(c.cmpwi(0x71080), (3, 1))
        bo, bi, target = c.cond(0x71084)
        self.assertEqual((bo, bi & 3, target), (v1.BRANCH_IF_FALSE, v1.EQ, 0x71174), 'equal and count != 1: skip everything')
        # The push loads the end: lwz r3, 2536(sp); lwz r4, 2540(sp); bl 0x7bcf4.
        self.assertEqual(c.fields(0x71088), (32, 3, 1, 2536))
        self.assertEqual(c.fields(0x7108c), (32, 4, 1, 2540))
        self.assertEqual(c.call(0x71090), 0x7BCF4)

    def test_commit_block_lays_eight_lines_and_the_flag_is_the_last(self):
        c = self.c
        calls = [at for at in range(0x71088, 0x71174, 4) if c.word(at) >> 26 == 18 and c.word(at) & 1 and c.call(at) == LAYLINE]
        self.assertEqual(calls, [0x710a8, 0x710c0, 0x710d8, 0x710f0, 0x7111c, 0x71134, 0x7114c, 0x71164])
        modes = [c.li(at - 4)[1] for at in calls if at != 0x710c0]
        self.assertEqual(modes, [0x87, 0x80, 0x82, 0x85, 0x86, 0x83, 0x81])
        self.assertEqual(c.call(0x710ac), 0x7B878, 'the commit takes r3 from the mode getter, not an immediate')
        # flag = (LayLine(0x81) == 0): cntlzw then rlwinm r0, r0, 27, 24, 31 (>> 5), stored at sp+2380.
        self.assertEqual(c.word(0x71168), 0x7C600034)
        self.assertEqual(c.word(0x7116c), 0x5400DE3E)
        self.assertEqual(c.fields(0x71170), (36, 0, 1, 2380))
        self.assertEqual([at for at in range(0x70F84, 0x71304, 4) if c.fields(at)[2:] == (1, 2380)], [0x71170, 0x7123c])

    def test_end_test_ends_on_start_without_reading_the_flag(self):
        c = self.c
        self.assertEqual(c.fields(0x7121c), (32, 0, 30, 0))
        self.assertEqual(c.fields(0x71220), (32, 4, 1, 2536))
        bo, bi, target = c.cond(0x71228)
        self.assertEqual((bo, bi & 3, target), (v1.BRANCH_IF_FALSE, v1.EQ, 0x7123c), 'x differs: read the flag')
        self.assertEqual(c.fields(0x7122c), (32, 3, 31, 0))
        self.assertEqual(c.fields(0x71230), (32, 0, 1, 2540))
        bo, bi, target = c.cond(0x71238)
        self.assertEqual((bo, bi & 3, target), (v1.BRANCH_IF_TRUE, v1.EQ, 0x71248), 'end == start: end the tool, flag unread')
        self.assertEqual(c.fields(0x7123c), (32, 0, 1, 2380))
        self.assertEqual(c.cmpwi(0x71240), (0, 0))
        self.assertEqual(c.li(0x71250), (4, -1), 'the end path stores start -1')


def git(*args: str) -> str:
    return subprocess.run(['git', '-C', str(REPO), *args], check=True, capture_output=True).stdout.decode('utf-8', 'replace')


def show(rev: str, path: str) -> str:
    return git('show', f'{rev}:{path}')


def has(*revs: str) -> bool:
    try:
        for rev in revs:
            git('cat-file', '-e', f'{rev}^{{commit}}')
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False


def between(text: str, start: str, end: str) -> str:
    return text[text.index(start):text.index(end, text.index(start))]


@unittest.skipUnless(has(IMPL, ESC, FIX), 'the review revisions are not in this checkout')
class Fixes(unittest.TestCase):
    def test_stack_shape(self):
        self.assertEqual(git('rev-parse', f'{ESC}^').strip(), git('rev-parse', IMPL).strip())
        self.assertEqual(git('rev-parse', f'{FIX}^^').strip(), git('rev-parse', ESC).strip())

    def test_b2_ending_click_returns_before_the_push_and_commit(self):
        click = between(show(FIX, 'source/OpenTPW/World/CellBuildTool.cs'), 'public SegmentResult? Click(', 'public void Undo(')
        early = click.index('if ( end == start && vertices.Count != 1 )')
        self.assertLess(early, click.index('vertices.Add( end );'))
        self.assertLess(early, click.index('Writer.Commit( start, end )'))
        self.assertIn('End();\n\t\t\treturn null;', click[early:click.index('vertices.Add( end );')])

    def test_s1_remove_tool_comes_first(self):
        hud = between(show(FIX, 'source/OpenTPW/Hud/ParkHud.cs'), 'private bool UpdateCellTool', 'private void DrawCellToolGhost')
        self.assertIn('level.IsPlacing || level.IsRemovingObjects )', hud)
        level = between(show(FIX, 'source/OpenTPW/World/Level.Objects.cs'), 'private void HandleObjectClick', 'public bool RemoveAt(')
        self.assertLess(level.index('if ( IsRemovingObjects )'), level.index('CellTool.Click( (x, y) )'))

    def test_s4_preview_checks_every_cell(self):
        tool = show(FIX, 'source/OpenTPW/World/CellBuildTool.cs')
        self.assertIn('var result = write ? build( x, y ) : check( x, y, built );', tool)
        self.assertNotIn('built.Count == 0 ? check', tool)
        level = show(FIX, 'source/OpenTPW/World/Level.Objects.cs')
        self.assertIn('( x, y, pending ) => CheckQueueCell( ride, x, y, pending )', level)
        build = between(level, 'public QueueBuildResult BuildQueueCell(', 'public QueueBuildResult CheckQueueCell(')
        self.assertLess(build.index('RecomputeQueue'), build.index('CheckQueueCell('))
        self.assertLess(build.index('CheckQueueCell('), build.index('TrySpendCell( CellPurchase.Queue )'))

    def test_s5_pause_menu_was_already_captured(self):
        """The round-1 S5 premise was wrong: Update's overUi includes an open pause menu since d21fb4a."""
        for rev in (IMPL, FIX):
            self.assertIn('var overUi = Stack.Covers( context.Canvas, input.Mouse ) || Stack.Screens.Count > 1;',
                          show(rev, 'source/OpenTPW/Hud/ParkHud.cs'))
        self.assertIn('return overUi || consumed || toolClick;', show(FIX, 'source/OpenTPW/Hud/ParkHud.cs'))
        self.assertIn('Level.UiCapturesMouse = Hud.Update( Context, input );', show(FIX, 'source/OpenTPW/Client/GameFlow.cs'))

    def test_register_and_free_byte_citations(self):
        register = show(FIX, 'source/OpenTPW/World/PathApproximations.cs')
        self.assertEqual(re.findall(r'\("(PATH-\d{3})"', register), [f'PATH-{n:03}' for n in range(1, 14)])
        self.assertIn('**189 unresolved unique APPROX IDs**', show(FIX, 'docs/FIDELITY-REGISTER.md'))
        builder = show(FIX, 'source/OpenTPW/World/ParkPathBuilder.cs')
        for site in ('0x6f5f4', '0x71330', '0x71cb8', '0x70ca4', '0x71c94', '0x722ec'):
            self.assertIn(site, builder)

    def test_line_endings_kept(self):
        self.assertEqual(git('diff', '--numstat', IMPL, FIX), git('diff', '--numstat', '--ignore-cr-at-eol', IMPL, FIX))

    @unittest.skipUnless(has(GATE), 'GATE-FIX2 (39ccda4) is not in this checkout')
    def test_gate_merge_conflicts_in_two_files(self):
        run = subprocess.run(['git', '-C', str(REPO), 'merge-tree', '--write-tree', '--name-only', FIX, GATE], capture_output=True, text=True)
        self.assertEqual(run.returncode, 1)
        files = run.stdout.split('\n\n')[0].splitlines()[1:]
        self.assertEqual(files, ['docs/FIDELITY-REGISTER.md', 'source/OpenTPW/World/Level.Objects.cs'])


if __name__ == '__main__':
    unittest.main()
