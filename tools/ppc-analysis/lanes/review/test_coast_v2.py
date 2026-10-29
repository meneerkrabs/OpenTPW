"""COAST-V2: focused re-review of COAST-F (coast-fix: 36a52b0 review cherry-pick + 8f01c3d fixes, on 02712e0).

- Binary (OPENTPW_MAC_BIN = the Feral bin directory, SimThemePark.data identified by SHA-256): the decodes behind
  docs/reverse/REVIEW-COAST-2.md. Uses test_queue_v1's PEF reader and field decoder, like test_coast_v1.
  CV2-1: the partial hop of the tick 0x40650 stores max(v_end, M) and then overwrites it with min(cap, v_end) from
  the unfloored v_end, so the minimum floor does not apply on a partial hop.
  CV2-2: a pylon-less node with node flag 0x40 gets the knot direction (0, -1, 0) when node flag 0x80 is clear and
  (0, +1, 0) when it is set (0x32dd8..0x32e08); the station end node (0x144) is the first case.
  CV2-3: the knot chord 0x32990 and the node tangent 0x31450 use fused and double-precision steps.
  Also: the traced parts COAST-F ported (rating pass, cost-rate fallback, COAST 4 range, tangent Y zeroing).
- Source (this checkout): the C# side. The open findings were expected failures until COAST-F2 fixed them.
- Git (OPENTPW_REVIEW_REPO, defaults to this repository; skipped when a commit is missing): the schema collisions
  of the in-flight branches and the conflict set of a trial merge onto origin/main c09c6ba.

Bounded: static reading only, nothing original is executed. The oracle runs of the review were scratch-only.
"""
from __future__ import annotations

import hashlib
import os
import re
import unittest
from pathlib import Path

from test_queue_v1 import DATA_SHA256, Code, git, load_sections, show

REPO = Path(__file__).resolve().parents[4]
COASTERS = REPO / 'source' / 'OpenTPW' / 'World' / 'Coasters'
FIX, MAIN = '8f01c3d', 'c09c6ba'
# In-flight branches at review time: ANIM-I, anim-fix, CTRL-I, walk-fix (schema version each sets).
SCHEMAS = {'b84bf58': 6, '4b55686': 6, 'aebd85f': 7, 'bdf3dde': 7}
LFS, STFS, FCMPO = 48, 52, 32


def text(path: Path) -> str:
    return path.read_text(encoding='utf-8')


def fcmpo(c: Code, at: int) -> tuple[int, int]:
    w = c.word(at)
    assert w >> 26 == 63 and (w >> 1) & 0x3FF == FCMPO, hex(at)
    return (w >> 16) & 31, (w >> 11) & 31


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set to the Feral bin directory')
class Binary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        raw = (Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data').read_bytes()
        if hashlib.sha256(raw).hexdigest() != DATA_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.c = Code(*load_sections(raw))

    def test_cv2_1_partial_hop_floor_is_overwritten(self):
        c = self.c
        # v = v_end < M ? M : v_end  -> train +120 (0x40d78..0x40d98)
        self.assertEqual((LFS, 1, 1, 276), c.d_form(0x40d78))  # v_end
        self.assertEqual((LFS, 0, 1, 288), c.d_form(0x40d7c))  # M (fMinSpeed x P)
        self.assertEqual((1, 0), fcmpo(c, 0x40d80))
        self.assertEqual((4, 0, 0x40d90), c.cond_branch(0x40d84))
        self.assertEqual((STFS, 0, 23, 120), c.d_form(0x40d98))
        # v = cap < v_end ? cap : v_end  -> train +120 again, compared against the raw v_end at 276(r1)
        self.assertEqual((LFS, 1, 1, 284), c.d_form(0x40d9c))  # cap
        self.assertEqual((LFS, 0, 1, 276), c.d_form(0x40da0))  # v_end, not the floored +120
        self.assertEqual((1, 0), fcmpo(c, 0x40da4))
        self.assertEqual((4, 0, 0x40db4), c.cond_branch(0x40da8))
        self.assertEqual((STFS, 0, 23, 120), c.d_form(0x40dc8))
        for at in range(0x40d9c, 0x40dc8, 4):
            self.assertNotEqual((LFS, 23, 120), (c.d_form(at)[0], c.d_form(at)[2], c.d_form(at)[3]), hex(at))

    def test_cv2_2_end_node_knot_points_down_without_flag_0x80(self):
        c = self.c
        self.assertEqual((LFS, 29, 2, -13628), c.d_form(0x32d38))
        self.assertEqual((LFS, 30, 2, -13648), c.d_form(0x32d3c))
        self.assertEqual((LFS, 31, 2, -13788), c.d_form(0x32d40))
        self.assertEqual((0.0, 1.0, -1.0), (c.toc_f32(-13628), c.toc_f32(-13648), c.toc_f32(-13788)))
        self.assertEqual(0x40, c.rlwinm_mask(0x32ddc))
        self.assertEqual((12, 2, 0x32e0c), c.cond_branch(0x32de0))  # no 0x40: the stored knot direction
        self.assertEqual(0x80, c.rlwinm_mask(0x32de4))
        self.assertEqual((12, 2, 0x32dfc), c.cond_branch(0x32de8))  # 0x80 clear -> 0x32dfc
        self.assertEqual((STFS, 31, 1, 80), c.d_form(0x32e00))  # y = -1.0
        self.assertEqual((STFS, 30, 1, 80), c.d_form(0x32df0))  # 0x80 set: y = +1.0
        self.assertEqual((0x144 & 0x40, 0x144 & 0x80), (0x40, 0))  # the end node's flags (0x37e34)

    def test_cv2_3_chord_and_tangent_norms_are_fused_and_double(self):
        c = self.c
        self.assertEqual((59, 28), c.float_op(0x32a64))  # fmsubs: 0.7*b.y - 0.7*a.y, one rounding
        self.assertEqual((63, 29), (c.float_op(0x32a7c)[0], c.float_op(0x32a7c)[1] & 31))  # fmadd (double): dx*dx + dy*dy
        self.assertEqual((63, 25), (c.float_op(0x3182c)[0], c.float_op(0x3182c)[1] & 31))  # fmul (double) x*x in the tangent's unit vectors
        self.assertEqual((59, 29), c.float_op(0x3196c))  # the tangent's dot product ends in fmadds

    def test_rating_pass_force_maxima_and_end_samples(self):
        c = self.c
        self.assertEqual((LFS, 17, 2, -13460), c.d_form(0x3fc90))
        self.assertEqual(0.0, c.toc_f32(-13460))
        self.assertEqual((STFS, 17, 1, 264), c.d_form(0x3fcb8))  # max speed
        self.assertEqual((STFS, 17, 1, 328), c.d_form(0x3fcbc))  # max height
        self.assertEqual(0x8, c.rlwinm_mask(0x3fdc4) ^ 0xFFFFFFFF)  # trough flag cleared
        self.assertEqual((59, 18), c.float_op(0x3ff60))  # v / 10, then
        self.assertEqual((59, 25), c.float_op(0x3ff64))  # a * (v / 10)
        for at, offset in ((0x40028, 32), (0x4002c, 36), (0x40030, 40), (0x40038, 28)):
            self.assertEqual((STFS, 0, 3, offset), c.d_form(at))  # sample 0: force 0, speed fWinchSpeed

    def test_cost_rate_fallback(self):
        c = self.c
        self.assertEqual(0xC000, c.rlwinm_mask(0x4b298))
        self.assertEqual(0xC000, c.rlwinm_mask(0x4b2ac))
        self.assertEqual(0x4000, c.rlwinm_mask(0x4b2bc))
        self.assertEqual((32, 3, 26, 104), c.d_form(0x4b2c4))  # previous node
        self.assertEqual(0x80, c.rlwinm_mask(0x4b2cc))
        self.assertEqual((LFS, 1, 31, 96), c.d_form(0x4b2d8))  # length rate of the chosen pylon
        self.assertEqual((LFS, 0, 31, 100), c.d_form(0x4b2dc))  # height rate

    def test_coast4_ignores_negative_arguments(self):
        c = self.c
        self.assertEqual((11, 0, 4, 0), c.d_form(0x3de20))
        self.assertEqual((4, 0, 0x3de38), c.cond_branch(0x3de24))  # >= 0 (that is, 0) -> 16
        self.assertEqual(0x48000030, c.word(0x3de28))  # b 0x3de58: negative -> nothing

    def test_tangent_y_zeroing_next_to_a_loop_pylon(self):
        c = self.c
        for at in (0x31bf4, 0x31c1c, 0x31c44):
            self.assertEqual(0x4000, c.rlwinm_mask(at), hex(at))
        self.assertEqual((LFS, 0, 2, -13628), c.d_form(0x31c4c))
        self.assertEqual((STFS, 0, 1, 196), c.d_form(0x31c50))
        self.assertEqual((STFS, 0, 1, 184), c.d_form(0x31c54))


class Source(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ride = text(COASTERS / 'CoasterRide.cs')
        cls.track = text(COASTERS / 'CoasterTrack.cs')
        cls.rating = text(COASTERS / 'CoasterRating.cs')
        cls.curve = text(COASTERS / 'CoasterCurve.cs')
        cls.register = text(COASTERS / 'CoasterApproximations.cs')

    def test_cv2_1_partial_hop_is_not_floored(self):
        move = re.search(r'private bool HopTrain\( CoasterTrain train.*?\n\t}\n', self.ride, re.S).group(0)
        self.assertNotRegex(move, r'end = end < minimumFloor \? minimumFloor : end;')

    def test_cv2_2_end_node_knot_points_down(self):
        knots = re.search(r'public IReadOnlyList<CoasterKnot> BuildKnots\(.*?\n\t}\n', self.track, re.S).group(0)
        self.assertNotRegex(knots, r'CoasterNodeFlags\.Loop\) != 0 \? SVector3\.UnitY\b')

    def test_cv2_3_remaining_precision_is_registered(self):
        self.assertRegex(self.register, r'\("COAST-0\d\d", "[^"]*\b(precision|fused)\b')

    def test_rating_pass_force_sign_and_order(self):
        self.assertIn('(behind.X + ahead.X) / spacing * -Gravity', self.rating)
        self.assertIn('var scale = speed / ForceDivisor;', self.rating)
        self.assertIn('var maxHeight = 0f;', self.rating)

    def test_cost_rate_fallback_is_ported(self):
        self.assertIn('private CoasterPylonControl RatePylon(', self.track)
        self.assertNotIn('"COAST-020"', self.register)
        self.assertNotIn('"COAST-021"', self.register)

    def test_coast4_range(self):
        self.assertIn('if ( parameter is >= 0 and <= 2 )', self.ride)
        self.assertIn('1 => 64, 2 => 32', self.ride)

    def test_derivative_is_the_power_form(self):
        # The PC executable's shape (round 10, X40): unfused singles, c1 - (c2 * -2 - c3 * t * 3) * t.
        self.assertIn('return c1 - (c2 * -2f - c3 * t * 3f) * t;', self.curve)


class Git(unittest.TestCase):
    def setUp(self):
        git('cat-file', '-e', FIX + '^{commit}')

    def test_schema_versions_in_flight(self):
        self.assertIn('public const int SchemaVersion = 6;', show(FIX, 'source/OpenTPW/World/WorldStateHash.cs'))
        for rev, schema in SCHEMAS.items():
            if git('cat-file', '-e', rev + '^{commit}', check=False).returncode:
                continue
            self.assertIn(f'public const int SchemaVersion = {schema};', show(rev, 'source/OpenTPW/World/WorldStateHash.cs'), rev)

    def test_trial_merge_onto_main_conflicts(self):
        git('cat-file', '-e', MAIN + '^{commit}')
        out = git('merge-tree', '--write-tree', '--name-only', FIX, MAIN, check=False).stdout.decode()
        conflicted = [line for line in out.split('\n\n', 1)[0].splitlines()[1:] if line]
        self.assertEqual(['docs/DETERMINISM.md', 'docs/FIDELITY-REGISTER.md', 'source/OpenTPW/Hud/ParkHud.cs'], conflicted)


if __name__ == '__main__':
    unittest.main()
