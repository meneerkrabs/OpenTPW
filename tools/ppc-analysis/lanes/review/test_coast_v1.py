"""COAST-V: independent review of COAST-I (515925b COAST-R2, 02712e0 implementation, on de96a43).

- Binary (OPENTPW_MAC_BIN = the Feral bin directory, SimThemePark.data identified by SHA-256): the review's
  decodes behind the blockers in docs/reverse/REVIEW-COAST.md. Uses test_queue_v1's PEF reader and field decoder,
  not the COAST lane witnesses, so a wrong assumption there shows up here as a disagreement.
  CV-1: only the place path asks 0x4b168 for the unit cost (r4 = 1); every move and slider call passes r4 = 0.
  CV-2: COAST 4 maps its argument 0/1/2 to the ride modes 16/64/32 (not 16/32/64).
  CV-3: completion 0x37cc8 appends one end node with flags 0x144 when definition bit 0x20 is set, and the
  loader always ORs 0xA0 into the definition flags.
  CV-16: the rating force is (neighbour - current) summed, over the spacing, times -g.
  CV-6: the straight case of 0x31450 takes the half-difference tangent only on loop pylons (0x4000).
  Also: the module's money calls, the guest excitement trunc(fmadds(E, 0.5, 50)), the LoopPylon +172 default.
- Source (this checkout): the C# side of each finding. The findings were open at 02712e0 as expected failures;
  COAST-F fixed all eight and flipped them to plain tests.
- Git (OPENTPW_REVIEW_REPO, defaults to this repository; skipped when a commit is missing): the CRLF state of
  SandboxSmokeTest.cs and the schema-6 pin collision with ANIM-I b84bf58.

Bounded: static reading only, nothing original is executed.
"""
from __future__ import annotations

import hashlib
import os
import re
import struct
import unittest
from pathlib import Path

from test_queue_v1 import DATA_SHA256, Code, git, load_sections, show

REPO = Path(__file__).resolve().parents[4]
COASTERS = REPO / 'source' / 'OpenTPW' / 'World' / 'Coasters'
BASE, IMPL, ANIM = 'de96a43', '02712e0', 'b84bf58'
COST = 0x4b168
CREDIT, DEBIT, BALANCE = 0xcbf50, 0xcbfdc, 0xcbf48
MODULE = (0x2d000, 0x4bd64)
# The r4 each caller of 0x4b168 loads (1 = charge CostPerUnit): place, pick-up node/next, drop node/next/next, slider.
UNIT_LOADS = {0x37864: 1, 0x4a3dc: 0, 0x4a3f8: 0, 0x4a5dc: 0, 0x4a610: 0, 0x4a690: 0, 0x46268: 0}


def text(path: Path) -> str:
    return path.read_text(encoding='utf-8')


@unittest.skipUnless(os.environ.get('OPENTPW_MAC_BIN'), 'OPENTPW_MAC_BIN not set to the Feral bin directory')
class Binary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        raw = (Path(os.environ['OPENTPW_MAC_BIN']) / 'SimThemePark.data').read_bytes()
        if hashlib.sha256(raw).hexdigest() != DATA_SHA256:
            raise unittest.SkipTest('SimThemePark.data is not the identified Feral build')
        cls.c = Code(*load_sections(raw))

    def calls_in_module(self, target: int) -> list[int]:
        c, out = self.c, []
        for at in range(MODULE[0], MODULE[1], 4):
            w = c.word(at)
            if w >> 26 == 18 and w & 3 == 1 and c.call(at) == target:
                out.append(at)
        return out

    def test_cv1_only_the_place_path_charges_the_unit(self):
        c = self.c
        self.assertEqual((14, 25, 4, 0), c.d_form(0x4b188))  # addi r25, r4, 0
        self.assertEqual((11, 0, 25, 0), c.d_form(0x4b284))  # cmpwi r25, 0
        self.assertEqual((12, 2, 0x4b294), c.cond_branch(0x4b288))  # r4 == 0 skips ...
        self.assertEqual((48, 0, 31, 92), c.d_form(0x4b28c))  # ... lfs CostPerUnit (pylon +92)
        for at, unit in UNIT_LOADS.items():
            self.assertEqual((14, 4, 0, unit), c.d_form(at), hex(at))  # li r4, unit
        # The loads above feed these calls (place, pick-up node/next, drop node/next/complete-next, height slider).
        for at in (0x37888, 0x4a3f0, 0x4a418, 0x4a5ec, 0x4a61c, 0x4a69c, 0x46280):
            if at != 0x37888:
                self.assertEqual(COST, c.call(at), hex(at))
        self.assertEqual(sorted({0x4a3f0, 0x4a418, 0x4a5ec, 0x4a61c, 0x4a69c, 0x46280} - set(self.calls_in_module(COST))), [])

    def test_cv1_money_calls_of_the_module(self):
        self.assertEqual([0x462ac, 0x4a644, 0x4a660, 0x4a6c4], self.calls_in_module(CREDIT))
        self.assertEqual([0x378c0, 0x462c0, 0x4a674, 0x4a6d8], self.calls_in_module(DEBIT))

    def test_cv2_coast4_maps_1_to_mode_64_and_2_to_mode_32(self):
        c = self.c
        self.assertEqual((11, 0, 4, 1), c.d_form(0x3de0c))  # cmpwi r4, 1
        self.assertEqual((12, 2, 0x3de50), c.cond_branch(0x3de18))  # == 1 -> li 64
        self.assertEqual((14, 4, 0, 64), c.d_form(0x3de50))
        self.assertEqual((4, 0, 0x3de2c), c.cond_branch(0x3de1c))  # > 1 -> compare with 3
        self.assertEqual((11, 0, 4, 3), c.d_form(0x3de2c))
        self.assertEqual((4, 0, 0x3de58), c.cond_branch(0x3de30))  # >= 3: nothing
        self.assertEqual((14, 4, 0, 32), c.d_form(0x3de44))  # 2 -> 32
        self.assertEqual((11, 0, 4, 0), c.d_form(0x3de20))
        self.assertEqual((4, 0, 0x3de38), c.cond_branch(0x3de24))  # 0 -> li 16
        self.assertEqual((14, 4, 0, 16), c.d_form(0x3de38))
        for at in (0x3de3c, 0x3de48, 0x3de54):
            self.assertEqual(0x38250, c.call(at))

    def test_cv3_one_end_node_with_flags_0x144(self):
        c = self.c
        self.assertEqual((24, 0, 0, 0xA0), c.d_form(0x301f4))  # ori r0, r0, 0xA0 (loader, always)
        self.assertEqual((36, 0, 30, 0), c.d_form(0x301f8))  # stw r0, 0(r30)
        for at in (0x37cec, 0x37e2c):
            self.assertEqual(0x20, c.rlwinm_mask(at), hex(at))  # rlwinm. definition bit 0x20
        self.assertEqual((32, 30, 3, 4), c.d_form(0x37ce4))  # r30 = controller +4, the definition
        self.assertEqual((14, 28, 0, 1), c.d_form(0x37cf4))  # set: one end node
        self.assertEqual((14, 28, 0, 2), c.d_form(0x37cfc))  # clear: two
        self.assertEqual((14, 7, 0, 0x144), c.d_form(0x37e34))  # set: flags 0x144
        self.assertEqual((14, 7, 0, 4), c.d_form(0x37e3c))  # clear: flags 4

    def test_cv6_straight_half_difference_only_on_loop_pylons(self):
        c = self.c
        self.assertEqual((32, 0, 26, 0), c.d_form(0x31a74))  # lwz pylon flags
        self.assertEqual(0x4000, c.rlwinm_mask(0x31a78))
        self.assertEqual((12, 2, 0x31ba4), c.cond_branch(0x31a7c))  # not a loop pylon -> T = b / SD
        self.assertEqual((48, 2, 25, 12), c.d_form(0x31ba4))  # lfs fStraightDivisor (+12)
        self.assertEqual(0x80, c.rlwinm_mask(0x31a84))  # node flag 0x80 picks the order of the difference

    def test_cv16_force_differences_are_neighbour_minus_current(self):
        c = self.c
        # r23 = current sample, r21 = next, r22 = previous (x at +4): f0 = next.x, f5 = cur.x, f4 = prev.x.
        self.assertEqual((48, 5, 23, 4), c.d_form(0x3fce0))
        self.assertEqual((48, 0, 21, 4), c.d_form(0x3fce4))
        self.assertEqual((48, 4, 22, 4), c.d_form(0x3fce8))
        self.assertEqual(0xec202828, c.word(0x3fcf0))  # fsubs f1, f0, f5: next - cur
        self.assertEqual(0xef042828, c.word(0x3fcf8))  # fsubs f24, f4, f5: prev - cur
        self.assertEqual(0xfc400050, c.word(0x3fe04))  # fneg f2, f0 (g)
        self.assertEqual(1.0, struct.unpack_from('>f', c.data, 0x3d140)[0])

    def test_guest_excitement_is_trunc_of_fused_half_plus_fifty(self):
        c = self.c
        self.assertEqual(0x41a14, c.call(0xe0068))
        self.assertEqual((48, 2, 1, 156), c.d_form(0xe006c))  # excitement (+260, the getter's r5 out)
        self.assertEqual(0.5, c.toc_f32(-11088))
        self.assertEqual(50.0, c.toc_f32(-11128))
        self.assertEqual((59, 29), (c.word(0xe0078) >> 26, (c.word(0xe0078) >> 1) & 31))  # fmadds
        self.assertEqual((48, 0, 3, 260), c.d_form(0x41a28))

    def test_loop_pylon_max_loop_height_default_is_raw_25(self):
        c = self.c
        self.assertEqual((48, 0, 2, -13744), c.d_form(0x2d5d4))
        self.assertEqual(25.0, c.toc_f32(-13744))
        self.assertEqual((52, 0, 31, 172), c.d_form(0x2d5d8))


class Source(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.builder = text(COASTERS / 'CoasterBuilder.cs')
        cls.track = text(COASTERS / 'CoasterTrack.cs')
        cls.ride = text(COASTERS / 'CoasterRide.cs')
        cls.definition = text(COASTERS / 'CoasterDefinition.cs')
        cls.determinism = text(REPO / 'docs' / 'DETERMINISM.md')

    def test_guest_excitement_formula(self):
        self.assertIn('(int)MathF.FusedMultiplyAdd( excitement, 0.5f, 50f )', text(COASTERS / 'CoasterRating.cs'))

    def test_cv1_moves_and_the_slider_do_not_charge_the_unit(self):
        settle = re.search(r'private void Settle\(.*?\n\t}\n', self.builder, re.S).group(0)
        self.assertRegex(settle, r'chargeUnit:\s*false|NodeCost\( \w+, false \)')

    def test_cv2_coast4_mapping(self):
        self.assertRegex(self.ride, r'1 => 64, 2 => 32')

    def test_cv3_completion_appends_one_end_node(self):
        complete = re.search(r'internal CoasterPlaceResult Complete\(\).*?\n\t}\n', self.track, re.S).group(0)
        self.assertNotIn('VisibleEndPylon', complete)

    def test_cv4_placing_is_not_refused_for_money_without_a_register_id(self):
        place = re.search(r'public CoasterPlaceResult Place\(.*?\n\t}\n', self.builder, re.S).group(0)
        self.assertTrue('force: true' in place or 'APPROX:COAST-' in place)

    def test_cv6_straight_case_on_loop_pylons(self):
        self.assertIn('if ( i.HasPylonDefinition && i.LoopPylon', text(COASTERS / 'CoasterNodeTangent.cs'))

    def test_cv13_determinism_doc_cites_the_registered_ids(self):
        # The generator is COAST-027 and the save gap COAST-025; DETERMINISM.md cites COAST-024 and COAST-022.
        self.assertNotRegex(self.determinism, r'folded to 32 bits \(COAST-024\)|generator are not saved either \(COAST-022\)')

    def test_cv14_loop_pylon_max_loop_height_default(self):
        # The default at 0x2d5d4 is the raw 25.0; only the loader scales a SAM value by 10 x pi/180.
        self.assertNotIn('MaxLoopHeightDifference = 25 * 10 * DegreesToRadians', self.definition)

    def test_cv16_force_sign(self):
        self.assertNotIn('(samples[index - 1].Position + next - 2 * sample.Position) / spacing', text(COASTERS / 'CoasterRating.cs'))


class Git(unittest.TestCase):
    def setUp(self):
        for rev in (BASE, IMPL):
            git('cat-file', '-e', rev + '^{commit}')

    def test_smoke_test_file_line_endings(self):
        # git diff --check flags the five added lines only because the file is CRLF; one LF line predates COAST-I.
        path = 'source/OpenTPW/Client/SandboxSmokeTest.cs'
        for rev, lines in ((BASE, 546), (IMPL, 549)):
            body = git('show', f'{rev}:{path}').stdout
            self.assertEqual(lines, body.count(b'\n'))
            self.assertEqual(lines - 1, body.count(b'\r\n'))
        added = [l for l in git('diff', BASE, IMPL, '--', path).stdout.split(b'\n') if l.startswith(b'+') and not l.startswith(b'+++')]
        self.assertEqual(5, len(added))
        self.assertTrue(all(l.endswith(b'\r') for l in added))

    def test_schema_6_pin(self):
        # COAST-I's own pin; COAST-M later merged the coasters into schema 9 (docs/DETERMINISM.md).
        self.assertIn('public const int SchemaVersion = 6;', show(IMPL, 'source/OpenTPW/World/WorldStateHash.cs'))
        self.assertIn('0x5E1198F70F3F2B55UL', show(IMPL, 'source/OpenTPW.Tests/DeterminismTests.cs'))

    def test_anim_i_reuses_schema_6_with_another_pin(self):
        git('cat-file', '-e', ANIM + '^{commit}')
        anim = show(ANIM, 'source/OpenTPW.Tests/DeterminismTests.cs')
        self.assertIn('public const int SchemaVersion = 6;', show(ANIM, 'source/OpenTPW/World/WorldStateHash.cs'))
        self.assertIn('0x0BC982AB91BE9A28UL', anim)
        self.assertNotIn('0x5E1198F70F3F2B55UL', anim)


if __name__ == '__main__':
    unittest.main()
