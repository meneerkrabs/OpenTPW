"""Tests for the bounded coaster geometry/motion/money witnesses (COAST-R2), optional identified assets."""
import math
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import coast_evidence as ce
import coast_geometry_evidence as ge

D = ce.DEFAULTS
TOC = ge.TOC_CONSTANTS


def d_word(op, rd, ra, imm):
    return (op << 26) | (rd << 21) | (ra << 16) | (imm & 0xffff)


def m_word(rs, ra, sh, mb, me):
    return (21 << 26) | (rs << 21) | (ra << 16) | (sh << 11) | (mb << 6) | (me << 1)


def field(at):
    return next(expected for offset, _, expected, _ in ge.FIELDS if offset == at)


def chain(points, directions, curviness=D['fCurviness']):
    """Knots (P, control_back, control_forward) with 0x32298 plain tangents and 0x32990 chords."""
    knots = []
    for i, (point, direction) in enumerate(zip(points, directions)):
        back = ge.chord(points[i - 1], point) if i > 0 else 1.0
        forward = ge.chord(point, points[i + 1]) if i + 1 < len(points) else 1.0
        knots.append((point, *ge.knot_controls(point, direction, back, forward, curviness)))
    return knots


class ConstantLinkTests(unittest.TestCase):
    """The restatement constants are the decoded TOC values and immediates, not free literals."""

    def test_geometry_constants(self):
        self.assertEqual(ge.SPACING, TOC[field(0x38ab8)[2]])
        self.assertEqual(ge.PARAMETER_STEP, TOC[field(0x45144)[2]])
        self.assertEqual(ge.CHORD_HEIGHT_WEIGHT, TOC[field(0x32a34)[2]])
        self.assertEqual(ge.LOOP_TANGENT, TOC[field(0x324e8)[2]])
        self.assertEqual(ge.LOOP_EXIT_TANGENT, TOC[field(0x32650)[2]])
        self.assertEqual(ge.HALF_CELL, TOC[field(0x333ec)[2]])
        self.assertEqual(ge.BANK_DIVISOR, TOC[field(0x2d2d0)[2]])
        self.assertEqual(ge.SINE_SIZE, field(0x4b720)[2])
        self.assertEqual(ge.TWO_PI, TOC[field(0xa3d18)[2]])
        self.assertEqual((TOC[-13336], TOC[-13324], TOC[-13328], TOC[-13332]), (-6.0, 3.0, -3.0, -1.0))

    def test_motion_constants(self):
        self.assertEqual(ge.HOP, TOC[field(0x40868)[2]])
        self.assertEqual(ge.DECAY_BELOW, TOC[field(0x40888)[2]])
        self.assertEqual(ge.STOP_BELOW, TOC[field(0x4088c)[2]])
        self.assertEqual(ge.DECAY, TOC[field(0x40f30)[2]])
        self.assertEqual(ge.DEFAULT_SPEED_SETTING, field(0x38b2c)[2])
        self.assertEqual(TOC[field(0x3fbb8)[2]], 100.0)


class SineTableTests(unittest.TestCase):
    def test_identity_and_quadrants(self):
        for k in range(-40, 41):
            angle = k * 0.157
            self.assertAlmostEqual(ge.tsin(angle) ** 2 + ge.tcos(angle) ** 2, 1.0, delta=4e-3)
        self.assertAlmostEqual(ge.tsin(math.pi / 2), 1.0, delta=2e-6)
        self.assertEqual(ge.tsin(-math.pi / 2), -ge.tsin(math.pi / 2))
        self.assertEqual(ge.tcos(0.0), 1.0)

    def test_table_steps_are_one_entry(self):
        table, mask, scale = ge.sine_table()
        self.assertEqual(mask + 1, ge.SINE_SIZE)
        self.assertAlmostEqual(scale * ge.TWO_PI, ge.SINE_SIZE, delta=1e-3)
        self.assertEqual(table[0], 0.0)


class CurveTests(unittest.TestCase):
    def test_bezier_endpoints_and_bernstein(self):
        p0, p1, p2, p3 = (1.0, 2.0, 3.0), (5.0, 9.0, -1.0), (8.0, 0.5, 4.0), (12.0, 3.0, 7.0)
        self.assertEqual(ge.bezier(p0, p1, p2, p3, 0.0), p0)
        for t in (0.25, 0.5, 0.75, 1.0):
            expected = tuple((1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t * t * c + t ** 3 * d
                             for a, b, c, d in zip(p0, p1, p2, p3))
            for got, want in zip(ge.bezier(p0, p1, p2, p3, t), expected):
                self.assertAlmostEqual(got, want, delta=1e-5)

    def test_knot_controls_are_collinear_and_chord_scaled(self):
        point, direction = (10.0, 20.0, 30.0), (0.0, 0.6, 0.8)
        back, forward = ge.knot_controls(point, direction, 12.0, 30.0)
        arm_back, arm_forward = math.dist(point, back), math.dist(point, forward)
        self.assertAlmostEqual(arm_back, D['fCurviness'] * 12.0 / TOC[-13684], delta=1e-4)
        self.assertAlmostEqual(arm_forward / arm_back, 30.0 / 12.0, delta=1e-5)
        mid = tuple((a + b) / 2 for a, b in zip(back, forward))
        cross = ge.cross(ge.sub(forward, point), ge.sub(point, back))
        self.assertLess(max(abs(x) for x in cross), 1e-3)
        self.assertGreater(ge.sub(forward, point)[2], 0.0)
        self.assertNotEqual(mid, point)

    def test_chord_weights_height(self):
        self.assertAlmostEqual(ge.chord((0.0, 0.0, 0.0), (0.0, 10.0, 0.0)), 10.0 * ge.CHORD_HEIGHT_WEIGHT,
                               delta=1e-5)
        self.assertEqual(ge.chord((0.0, 0.0, 0.0), (3.0, 0.0, 4.0)), 5.0)

    def test_node_tangent_directions(self):
        prev, here = (0.0, 0.0, 0.0), (10.0, 0.0, 0.0)
        back, forward = ge.node_tangents(prev, here, (10.0, 0.0, 10.0))
        # Perpendicular to the bisector of the corner, length chord x fCurviness on each side.
        self.assertAlmostEqual(forward[0], forward[2], delta=1e-6)
        self.assertAlmostEqual(math.hypot(*forward), 10.0 * D['fCurviness'], delta=1e-4)
        self.assertAlmostEqual(math.hypot(*back), 10.0 * D['fCurviness'], delta=1e-4)
        self.assertLess(back[0], 0.0)
        # Straight on: tangent along the line with length |b| / fStraightDivisor.
        _, straight = ge.node_tangents(prev, here, (30.0, 0.0, 0.0))
        self.assertAlmostEqual(straight[0], 20.0 / D['fStraightDivisor'], delta=1e-4)
        self.assertEqual(straight[1:], (0.0, 0.0))
        # Track ends follow the only neighbour.
        self.assertEqual(ge.node_tangents(None, here, (13.0, 0.0, 0.0))[1][0], ge.f32(3.0 / D['fStraightDivisor']))
        self.assertIsNone(ge.node_tangents(None, here, None))

    def test_loop_pylon_tangent_points_away_from_both_neighbours(self):
        back, forward = ge.node_tangents((0.0, 0.0, 0.0), (10.0, 5.0, 0.0), (0.0, 10.0, 0.0), loop_pylon=True)
        self.assertGreater(back[0] * -1, 0.0)
        self.assertGreater(forward[0], 0.0)

    def test_loop_factor_blends_to_2_1(self):
        self.assertAlmostEqual(ge.loop_knot_factor(D['fCurviness'], 0.0, 0.0), ge.LOOP_TANGENT, delta=1e-5)
        self.assertAlmostEqual(ge.loop_knot_factor(D['fCurviness'], 0.0, 0.25), D['fCurviness'], delta=1e-3)
        self.assertAlmostEqual(ge.loop_knot_factor(D['fCurviness'], 1.0, 0.25), ge.LOOP_TANGENT, delta=1e-3)

    def test_turn_angle_sign_and_bank(self):
        prev, here = (0.0, 0.0, 0.0), (10.0, 0.0, 0.0)
        left = ge.turn_angle_xz(prev, here, (10.0, 0.0, 10.0))
        right = ge.turn_angle_xz(prev, here, (10.0, 0.0, -10.0))
        self.assertEqual(left, -right)
        self.assertAlmostEqual(abs(left), math.pi / 2, delta=1e-6)
        bank_left, bank_right = ge.auto_bank(20.0, left, 10.0, 10.0), ge.auto_bank(20.0, right, 10.0, 10.0)
        self.assertEqual(bank_left, -bank_right)
        straight = ge.turn_angle_xz(prev, here, (20.0, 0.0, 0.0))
        self.assertAlmostEqual(ge.auto_bank(20.0, straight, 10.0, 10.0), 0.0, delta=1e-6)
        self.assertAlmostEqual(ge.auto_bank(40.0, left, 10.0, 10.0), 2 * bank_left, delta=1e-6)
        self.assertAlmostEqual(ge.auto_bank(20.0, left, 20.0, 20.0), bank_left / 2, delta=1e-6)
        self.assertAlmostEqual(bank_left * ge.BANK_DIVISOR * 20.0 / 20.0, math.pi - abs(left), delta=1e-5)

    def test_speed_estimate_floors(self):
        self.assertEqual(ge.speed_estimate(D, 0.0, D['fMinSpeed'], 10.0, 30.0), D['fMinSpeed'])
        down = ge.speed_estimate(D, 0.0, D['fMinSpeed'], 30.0, 10.0)
        self.assertEqual(down, D['fAccelerationPerHeight'] * 20.0 * D['fDownhillAccelModifier'])


class SamplerTests(unittest.TestCase):
    def test_straight_track_is_uniform(self):
        points = [(0.0, 10.0, 0.0), (30.0, 10.0, 0.0), (60.0, 10.0, 0.0)]
        samples = ge.sample_chain(chain(points, [(1.0, 0.0, 0.0)] * 3))
        self.assertEqual(len(samples), int(60.0 / ge.SPACING) + 1)
        for (a, _, _), (b, _, _) in zip(samples, samples[1:]):
            self.assertAlmostEqual(math.dist(a, b), ge.SPACING, delta=1e-4)
        self.assertEqual(samples[0][0], points[0])

    def test_curved_track_spacing_is_chord_length(self):
        points = [(0.0, 10.0, 0.0), (40.0, 20.0, 0.0), (40.0, 30.0, 40.0), (0.0, 10.0, 60.0)]
        directions = [(1.0, 0.0, 0.0), (0.7, 0.1, 0.7), (-0.7, 0.0, 0.7), (-1.0, 0.0, 0.0)]
        samples = ge.sample_chain(chain(points, directions))
        steps = [math.dist(a, b) for (a, _, _), (b, _, _) in zip(samples, samples[1:])]
        self.assertTrue(all(step <= ge.SPACING + 1e-4 for step in steps))
        self.assertGreater(min(steps), 0.95 * ge.SPACING)
        parameters = [(pair, t) for _, pair, t in samples]
        self.assertEqual(parameters, sorted(parameters))
        self.assertEqual({pair for pair, _ in parameters}, {0, 1, 2})


class MotionTests(unittest.TestCase):
    cap = ge.speed_cap(ge.DEFAULT_SPEED_SETTING, D)

    def test_speed_cap_setting(self):
        self.assertEqual(ge.speed_cap(0, D), D['fMaxSpeedAtMinSetting'])
        self.assertEqual(ge.speed_cap(100, D), D['fMaxSpeedAtMaxSetting'])
        self.assertAlmostEqual(self.cap, 0.4 * D['fMaxSpeedAtMinSetting'] + 0.6 * D['fMaxSpeedAtMaxSetting'],
                               delta=1e-4)

    def test_flat_track_moves_v_dt(self):
        flat = lambda s: 10.0  # noqa: E731
        for dt in (0.031, 0.25, 1.0):
            position, speed = ge.advance(D, flat, 0.0, D['fMinSpeed'], dt, D['fMinSpeed'], self.cap)
            self.assertEqual(speed, D['fMinSpeed'])
            self.assertAlmostEqual(position * ge.SPACING, D['fMinSpeed'] * dt, delta=1e-4)

    def test_downhill_hop_gains_height_energy(self):
        slope = lambda s: 100.0 - 2.0 * s  # noqa: E731
        speed = ge.hop_speed(D, 20.0, slope(0.0), slope(ge.HOP), D['fMinSpeed'], self.cap)
        self.assertEqual(speed, 20.0 + D['fAccelerationPerHeight'] * 1.0 * D['fDownhillAccelModifier'])
        uphill = ge.hop_speed(D, 20.0, slope(ge.HOP), slope(0.0), D['fMinSpeed'], self.cap)
        self.assertLess(uphill, 20.0)
        self.assertEqual(ge.hop_speed(D, 200.0, 10.0, 10.0, 5.0, self.cap), self.cap)
        self.assertEqual(ge.hop_speed(D, 30.0, 10.0, 10.0, 5.0, self.cap, friction=True),
                         ce.f32(30.0 * D['fFrictionMultiplier']))

    def test_unpowered_train_decays_to_stop(self):
        flat = lambda s: 10.0  # noqa: E731
        position, speed = 0.0, 1.9
        for _ in range(400):
            position, speed = ge.advance(D, flat, position, speed, 0.031, 0.0, self.cap)
        self.assertEqual(speed, 0.0)
        self.assertGreater(position, 0.0)

    def test_station_braking(self):
        start = ge.braking_start(100, 30.0)
        self.assertEqual(start, 99 - 30.0 / ge.SPACING)
        self.assertEqual(ge.braking_start(5, 30.0), 0.0)
        self.assertEqual(ge.brake_blend(40.0, 5.0, start, start, 100.0), 40.0)
        self.assertEqual(ge.brake_blend(40.0, 5.0, 100.0, start, 100.0), 5.0)
        self.assertEqual(ge.brake_blend(40.0, 5.0, start - 1, start, 100.0), 5.0)
        speeds = [ge.brake_blend(40.0, 5.0, start + k, start, 100.0) for k in range(11)]
        self.assertEqual(speeds, sorted(speeds, reverse=True))


class MoneyAndLayoutTests(unittest.TestCase):
    def test_place_truncates_and_preview_rounds_up(self):
        cost = ce.pylon_cost(40.0, 10.0, 10.0, 31.0, 47.0, True)
        self.assertEqual(ge.place_charge(cost), math.floor(cost))
        self.assertEqual(ge.preview_delta(0.0, cost), math.ceil(cost))

    def test_move_settlement(self):
        old, new = [64.5, 51.25], [70.1, 49.0]
        refund, charge = ge.move_settlement(old, new, moved=True)
        self.assertEqual((refund, charge), (sum(math.floor(c) for c in old), sum(math.ceil(c) for c in new)))
        self.assertGreaterEqual(charge - refund, sum(new) - sum(old))
        self.assertEqual(ge.move_settlement(old, new, moved=False), (0, 0))
        there, back = ge.move_settlement(old, new, True), ge.move_settlement(new, old, True)
        self.assertGreater(there[1] - there[0] + back[1] - back[0], 0)

    def test_node_world_position(self):
        self.assertEqual(ge.node_world((3, 4), 2.0, 10.0), (35.0, 12.0, 45.0))
        stacked = ge.node_world((3, 4), 2.0, 10.0, below=12.0, stack_offset=1.5)
        self.assertEqual(stacked[1], 23.5)


class WitnessShapeTests(unittest.TestCase):
    def synthetic(self):
        code = bytearray(0x200000)
        for at, op, (rd, ra, imm), _ in ge.FIELDS:
            struct.pack_into('>I', code, at, d_word(op, rd, ra, imm))
        for at, (rs, ra, sh, mb, me, _) in ge.MASKS.items():
            struct.pack_into('>I', code, at, m_word(rs, ra, sh, mb, me))
        return SimpleNamespace(code=SimpleNamespace(data=code))

    def test_synthetic_fields_decode(self):
        c = self.synthetic()
        for at, op, expected, meaning in ge.FIELDS:
            self.assertEqual(ge.d_fields(c, at, op), expected, meaning)
        for at, (*fields, meaning) in ge.MASKS.items():
            self.assertEqual(ge.ce.rlwinm_fields(c, at), tuple(fields), meaning)

    def test_synthetic_wrong_operand_is_rejected(self):
        c = self.synthetic()
        struct.pack_into('>I', c.code.data, 0x45144, d_word(48, 19, 2, -13420))
        with self.assertRaises(ge.pef.PEFError):
            ge.require(ge.d_fields(c, 0x45144, 48), field(0x45144), 'parameter step')

    def test_scan_finds_only_matching_immediates(self):
        c = self.synthetic()
        hits = ge.scan(c, 52, 172, *ge.MODULE)
        self.assertEqual(hits, {0x38abc} | {at for at, op, (_, ra, imm), _ in ge.FIELDS
                                           if op == 52 and imm == 172 and ra != 1})

    def test_tables_are_consistent(self):
        self.assertTrue(set(ge.NO_MONEY_BLOCKS) <= set(ge.BLOCKS))
        self.assertEqual(len({at for at, *_ in ge.FIELDS}), len(ge.FIELDS))
        for start, end, digest in ge.BLOCKS.values():
            self.assertLess(start, end)
            self.assertEqual(len(digest), 64)


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
class IdentifiedBinaryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ['OPENTPW_PPC_BIN_ROOT'])

    def test_identified_original_witness(self):
        report = ge.inspect(self.root)
        self.assertEqual(report['field_witnesses'], len(ge.FIELDS))
        self.assertEqual(report['toc_pointers']['-29688'], 0x66f78)
        self.assertEqual(report['scans']['module_credit_calls'], ['0x462ac', '0x4a644', '0x4a660', '0x4a6c4'])
        self.assertEqual(report['witness_source'], 'tools/ppc-analysis/lanes/coast/coast_geometry_evidence.py')

    def _mutated(self, section, offset, value_fmt, value):
        c = ce.load_identified(self.root / 'SimThemePark.data')
        data = bytearray(getattr(c, section).data)
        struct.pack_into(value_fmt, data, offset, value)
        getattr(c, section).data = data
        return c

    def test_wrong_spacing_constant_is_rejected(self):
        c = self._mutated('data_section', ce.TOC - 13684, '>f', 2.5)
        with self.assertRaises(ge.pef.PEFError):
            ge.check_code(c)

    def test_wrong_parameter_step_operand_is_rejected(self):
        c = self._mutated('code', 0x45144, '>I', d_word(48, 19, 2, -13420))
        with self.assertRaises(ge.pef.PEFError):
            ge.check_code(c)

    def test_live_map_instead_of_snapshot_is_rejected(self):
        c = self._mutated('code', 0x36cd0, '>I', d_word(32, 4, 2, -29908))
        with self.assertRaises(ge.pef.PEFError):
            ge.check_code(c)

    def test_refund_on_delete_would_be_rejected(self):
        start = ge.BLOCKS['delete_nodes'][0] + 0x100
        c = self._mutated('code', start, '>I', (18 << 26) | ((0xcbf50 - start) & 0x03fffffc) | 1)
        with self.assertRaises(ge.pef.PEFError):
            ge.check_scans(c)

    def test_wrong_decay_constant_is_rejected(self):
        c = self._mutated('data_section', ce.TOC - 13560, '>f', 0.9)
        with self.assertRaises(ge.pef.PEFError):
            ge.check_code(c)


if __name__ == '__main__':
    unittest.main()
