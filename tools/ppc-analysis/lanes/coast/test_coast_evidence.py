"""Tests for the bounded coaster witnesses (COAST-R), with optional identified local assets."""
import math
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import coast_evidence as ce


def d_word(op, rd, ra, imm):
    return (op << 26) | (rd << 21) | (ra << 16) | (imm & 0xffff)


def a_word(frd, fra, frb, frc, xo=29):
    return (59 << 26) | (frd << 21) | (fra << 16) | (frb << 11) | (frc << 6) | (xo << 1)


def m_word(rs, ra, sh, mb, me):
    return (21 << 26) | (rs << 21) | (ra << 16) | (sh << 11) | (mb << 6) | (me << 1)


def synthetic_container():
    code = bytearray(0x100000)
    for at, op, (rd, ra, imm), _ in ce.FIELDS:
        struct.pack_into('>I', code, at, d_word(op, rd, ra, imm))
    for at, (frd, fra, frb, frc, _) in ce.FMADDS.items():
        struct.pack_into('>I', code, at, a_word(frd, fra, frb, frc))
    for at, (rs, ra, sh, mb, me, _) in ce.MASKS.items():
        struct.pack_into('>I', code, at, m_word(rs, ra, sh, mb, me))
    return SimpleNamespace(code=SimpleNamespace(data=code))


def check_shapes(c):
    ce.check_fields(c)
    for at, (*fields, meaning) in ce.FMADDS.items():
        ce.require(ce.a_fields(c, at), (*fields, 29), meaning)
    for at, (*fields, meaning) in ce.MASKS.items():
        ce.require(ce.rlwinm_fields(c, at), tuple(fields), meaning)


STD, LOOP = ce.STD_PYLON, ce.LOOP_PYLON


class PlacementTests(unittest.TestCase):
    def test_straight_continuation_is_pi(self):
        self.assertAlmostEqual(ce.turn_angle((10, 0), (5, 0), (0, 0)), math.pi, places=6)
        self.assertAlmostEqual(ce.turn_angle((5, 5), (5, 0), (0, 0)), math.pi / 2, places=6)

    def test_distance_window_is_squared_cells(self):
        self.assertTrue(ce.placement_ok(STD, (8, 0), (5, 0)))          # d = 3, minimum
        self.assertFalse(ce.placement_ok(STD, (7, 0), (5, 0)))         # d = 2
        self.assertTrue(ce.placement_ok(STD, (13, 0), (5, 0)))         # d = 8, maximum
        self.assertFalse(ce.placement_ok(STD, (14, 0), (5, 0)))        # d = 9 > 8
        self.assertTrue(ce.placement_ok(LOOP, (14, 0), (5, 0)))        # loop pylons reach 9
        self.assertFalse(ce.placement_ok(STD, (5, 0), (5, 0)))         # same cell
        self.assertFalse(ce.placement_ok(STD, (128, 0), (125, 0)))     # outside the map

    def test_turn_limits_relax_with_distance(self):
        # 60 degree angle (a 120 degree turn): rejected at d=3 (min 80), accepted at d=8 (min 50).
        prev, before = (20, 20), (20, 10)
        near = (20 + round(3 * math.sin(math.radians(60))), 20 - round(3 * math.cos(math.radians(60))))
        self.assertLess(ce.turn_angle(near, prev, before), math.radians(80))
        self.assertFalse(ce.placement_ok(STD, near, prev, before))
        far = (27, 17)
        angle = ce.turn_angle(far, prev, before)
        low, high = ce.angle_window(STD, math.sqrt(58), False)
        self.assertTrue(low <= angle <= high)
        self.assertTrue(ce.placement_ok(STD, far, prev, before))

    def test_entrance_limits_are_stricter(self):
        prev, before = (20, 20), (20, 10)
        new = (22, 25)  # d^2 = 29, angle about 158 degrees
        self.assertTrue(ce.placement_ok(STD, new, prev, before, entrance=False))
        bent = (24, 22)  # d^2 = 20, angle about 117 degrees
        self.assertTrue(ce.placement_ok(STD, bent, prev, before, entrance=False))
        self.assertFalse(ce.placement_ok(STD, bent, prev, before, entrance=True))

    def test_window_endpoints(self):
        self.assertEqual(ce.angle_window(STD, 3.0, True), (ce.f32(140 * ce.DEG), ce.f32(180 * ce.DEG)))
        self.assertEqual(ce.angle_window(STD, 8.0, False), (ce.f32(50 * ce.DEG), ce.f32(180 * ce.DEG)))
        self.assertEqual(ce.angle_window(STD, 20.0, False), ce.angle_window(STD, 8.0, False))  # t clamps


class CostAndHeightTests(unittest.TestCase):
    def test_pylon_cost(self):
        self.assertEqual(ce.pylon_cost(40.0, 10.0, 10.0, 50.0, 30.0, True), 120.0)
        self.assertEqual(ce.pylon_cost(40.0, 10.0, 10.0, 50.0, 30.0, False), 80.0)
        self.assertEqual(ce.pylon_cost(60.0, 15.0, 15.0, 80.0, 0.0, True), 180.0)

    def test_round_up(self):
        self.assertEqual(ce.round_up(120.0), 120)
        self.assertEqual(ce.round_up(120.2), 121)
        self.assertEqual(ce.round_up(0.0), 0)

    def test_height_parameter_clamps_twice(self):
        self.assertEqual(ce.height_parameter(55.0, 10.0, 100.0, 10.0, 100.0), 0.5)
        self.assertEqual(ce.height_parameter(500.0, 10.0, 100.0, 10.0, 100.0), 1.0)
        self.assertEqual(ce.height_parameter(0.0, 10.0, 100.0, 10.0, 100.0), 0.0)
        self.assertEqual(ce.height_parameter(80.0, 0.0, 100.0, 10.0, 40.0), ce.f32(0.4))


class RunningTests(unittest.TestCase):
    def test_train_count(self):
        self.assertEqual(ce.train_count(60.0, 15000, 1), 1)      # defaults
        self.assertEqual(ce.train_count(130.0, 30000, 6), 4)
        self.assertEqual(ce.train_count(10.0, 30000, 6), 1)      # floor of one train
        self.assertEqual(ce.train_count(400.0, 8000, 30), 30)    # uiMaxTrains cap

    def test_speed_step_and_winch(self):
        d = ce.DEFAULTS
        speed, winch = ce.next_speed(5.0, 50.0, 40.0, d, 5.0, 130.0)
        self.assertEqual((speed, winch), (20.0, False))          # 1.5 * 10 downhill
        speed, winch = ce.next_speed(20.0, 40.0, 50.0, d, 5.0, 130.0)
        self.assertEqual((speed, winch), (5.0, True))            # climb hits the winch floor
        speed, _ = ce.next_speed(120.0, 100.0, 0.0, d, 5.0, 130.0)
        self.assertEqual(speed, 130.0)                           # cap
        speed, _ = ce.next_speed(20.0, 50.0, 50.0, d, 5.0, 130.0, friction_sample=True)
        self.assertEqual(speed, ce.f32(20.0 * ce.f32(0.8)))


class RatingTests(unittest.TestCase):
    def test_flat_run_converges(self):
        r = ce.Rating()
        for i in range(40):
            self.assertEqual(r.step(i, 10.0, 0.0, (0.0, 0.0, 0.0)), 0)
        self.assertAlmostEqual(r.excitement, 2.0, places=5)      # fixed point of 0.5 E + 1
        sickness, excitement = r.final()
        self.assertEqual(sickness, 0.0)
        self.assertAlmostEqual(excitement, 0.5 * 2.0 + 1.7 * 2.0, places=4)

    def test_inverted_section_makes_sick(self):
        r = ce.Rating()
        r.step(0, 0.0, math.pi, (0.0, 1.0, 0.0))
        upside = ce.f32(ce.f32(math.pi - ce.f32(math.pi / 2)) + 1.0)
        self.assertAlmostEqual(r.sickness, 0.5 * 1.0 + 3.0 * upside, places=4)

    def test_inverted_discomfort_needs_low_vertical_force(self):
        hanging, pressed = ce.Rating(), ce.Rating()
        hanging.step(0, 0.0, math.pi, (0.0, -1.0, 0.0))
        pressed.step(0, 0.0, math.pi, (0.0, 1.0, 0.0))
        self.assertLess(hanging.excitement, pressed.excitement)

    def test_scream_flags(self):
        r = ce.Rating()
        for i in range(4):
            r.step(i, 0.0, 0.0, (0.0, 0.0, 0.0))
        self.assertEqual(r.step(4, 100.0, 0.0, (0.0, 0.0, 0.0)), 6)   # both start deltas exceeded
        self.assertEqual(r.step(5, 0.0, 0.0, (0.0, 0.0, 0.0)), 0)     # 5.5 below both sustains
        r2 = ce.Rating()
        self.assertEqual(r2.step(2, 100.0, 0.0, (0.0, 0.0, 0.0)), 0)  # index <= 2 never screams

    def test_final_clamps(self):
        r = ce.Rating()
        for i in range(10):
            r.step(i, 1000.0, 0.0, (100.0, 100.0, 100.0))
        self.assertEqual(r.final(), (100.0, 100.0))

    def test_ride_excitement(self):
        self.assertEqual(ce.ride_excitement(0.0), 50)
        self.assertEqual(ce.ride_excitement(37.0), 68)
        self.assertEqual(ce.ride_excitement(100.0), 100)


class DecoderTests(unittest.TestCase):
    def test_synthetic_shapes_pass_and_mutations_fail(self):
        check_shapes(synthetic_container())
        for index in (0, len(ce.FIELDS) // 3, 2 * len(ce.FIELDS) // 3, len(ce.FIELDS) - 1):
            at = ce.FIELDS[index][0]
            mutated = synthetic_container()
            word = struct.unpack_from('>I', mutated.code.data, at)[0]
            struct.pack_into('>I', mutated.code.data, at, word ^ 4)
            with self.assertRaises(ce.pef.PEFError):
                ce.check_fields(mutated)
        for table in (ce.FMADDS, ce.MASKS):
            at = next(iter(table))
            mutated = synthetic_container()
            word = struct.unpack_from('>I', mutated.code.data, at)[0]
            struct.pack_into('>I', mutated.code.data, at, word ^ (1 << 11))
            with self.assertRaises(ce.pef.PEFError):
                check_shapes(mutated)

    def test_key_operands_are_pinned(self):
        pinned = {(at, op): fields for at, op, fields, _ in ce.FIELDS}
        self.assertEqual(pinned[(0x2d280, 14)], (5, 0, 15000))
        self.assertEqual(pinned[(0x2d3d4, 14)], (29, 0, 9))
        self.assertEqual(pinned[(0x2d3dc, 14)], (0, 0, 64))
        self.assertEqual(pinned[(0x36e98, 32)], (3, 19, 156))
        self.assertEqual(pinned[(0x36eac, 32)], (3, 19, 160))
        self.assertEqual(pinned[(0x4192c, 48)], (3, 2, -13456))
        self.assertEqual(pinned[(0xe3008, 8)], (0, 0, 3))
        self.assertEqual(pinned[(0x4b220, 48)], (30, 2, -13300))
        self.assertEqual(ce.TOC_CONSTANTS[-13456], 1.7000000476837158)
        self.assertEqual(ce.TOC_CONSTANTS[-11128], 50.0)
        self.assertEqual(ce.FMADDS[0x41944][:4], (0, 4, 0, 3))

    def test_defaults_match_pinned_constants(self):
        self.assertEqual(ce.DEFAULTS['fMaxSpeedAtMaxSetting'], ce.TOC_CONSTANTS[-13664])
        self.assertEqual(ce.DEFAULTS['fGravityY'], ce.TOC_CONSTANTS[-13672])
        self.assertEqual(ce.f32(ce.STD_PYLON['entrance_min_at_min'] * ce.DEG), ce.TOC_CONSTANTS[-13704])
        self.assertEqual(ce.f32(ce.LOOP_PYLON['max_at_min'] * ce.DEG), ce.TOC_CONSTANTS[-13736])

    def test_sam_value_parser(self):
        values = ce.sam_values('sCoasterType.uiMaxTrains\t\t20\n# x\nasPylonControls[0].pcMeshFilename "StdPylon"\n')
        self.assertEqual(values['sCoasterType.uiMaxTrains'], '20')
        self.assertEqual(values['asPylonControls[0].pcMeshFilename'], '"StdPylon"')


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
class IdentifiedBinaryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ['OPENTPW_PPC_BIN_ROOT'])

    def test_identified_original_witness(self):
        report = ce.inspect(self.root)
        self.assertEqual(report['field_witnesses'], len(ce.FIELDS))
        self.assertEqual(report['settings_offsets']['uiMinPlaceDist'], 14588)
        self.assertEqual(report['toc_pointers']['-21744'], 0x3d160)
        self.assertEqual(report['witness_source'], 'tools/ppc-analysis/lanes/coast/coast_evidence.py')

    def _mutated(self, section, offset, value_fmt, value):
        c = ce.load_identified(self.root / 'SimThemePark.data')
        data = bytearray(getattr(c, section).data)
        struct.pack_into(value_fmt, data, offset, value)
        getattr(c, section).data = data
        return c

    def test_wrong_release_rate_default_is_rejected(self):
        c = self._mutated('code', 0x2d280, '>I', d_word(14, 5, 0, 15001))
        with self.assertRaises(ce.pef.PEFError):
            ce.check_fields(c)

    def test_wrong_min_distance_offset_is_rejected(self):
        c = self._mutated('code', 0x36e98, '>I', d_word(32, 3, 19, 152))
        with self.assertRaises(ce.pef.PEFError):
            ce.check_fields(c)

    def test_wrong_peak_weight_constant_is_rejected(self):
        c = self._mutated('data_section', 0x8000 - 13456, '>f', 1.8)
        with self.assertRaises(ce.pef.PEFError):
            ce.check_code(c)

    def test_wrong_rating_clamp_is_rejected(self):
        c = self._mutated('data_section', 0x3d160, '>f', 99.0)
        with self.assertRaises(ce.pef.PEFError):
            ce.check_code(c)


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'requires local PC Data directory')
class PcDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalog = ce.track_catalog(Path(os.environ['OPENTPW_PC_DATA']))

    def test_track_types(self):
        counts = {}
        for entry in self.catalog.values():
            counts[entry['track_type']] = counts.get(entry['track_type'], 0) + 1
        self.assertEqual(counts, {1: 4, 2: 4, 3: 12})

    def test_chac_atak(self):
        entry = self.catalog[1185]
        self.assertEqual((entry['theme'], entry['price'], entry['max_trains'], entry['train_release_rate']),
                         ('jungle', 10000, 20, 16000))
        self.assertEqual([p['mesh'] for p in entry['pylons']], ['StdPylon'])

    def test_pylon_rates(self):
        for entry in self.catalog.values():
            if entry['track_type'] != 3:
                continue
            rates = [(p['cost_per_unit'], p['cost_per_2d_length'], p['cost_per_height']) for p in entry['pylons']]
            self.assertEqual(rates[0], (40.0, 10.0, 10.0), entry['name'])
            if len(rates) > 1:
                self.assertEqual(rates[1], (60.0, 15.0, 15.0), entry['name'])
        loops = sorted(e['id'] for e in self.catalog.values() if len(e.get('pylons', [])) == 2)
        self.assertEqual(loops, [1180, 1190, 2180, 2190, 3180, 3185, 3190, 4185, 4190])


if __name__ == '__main__':
    unittest.main()
