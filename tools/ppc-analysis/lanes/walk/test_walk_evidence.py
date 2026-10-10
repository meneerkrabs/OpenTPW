"""Tests for the bounded guest-walk witnesses (WALK-R), with an optional identified local binary."""
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import walk_evidence as w


def d_word(op, rd, ra, imm):
    return (op << 26) | (rd << 21) | (ra << 16) | (imm & 0xffff)


def rlwinm_word(source, target, shift, begin, end):
    return (21 << 26) | (source << 21) | (target << 16) | (shift << 11) | (begin << 6) | (end << 1)


def synthetic_fields_container():
    code = bytearray(0x110000)
    for at, op, (rd, ra, imm), _ in w.FIELDS:
        struct.pack_into('>I', code, at, d_word(op, rd, ra, imm))
    for at, *fields in w.RLWINM:
        struct.pack_into('>I', code, at, rlwinm_word(*fields))
    return SimpleNamespace(code=SimpleNamespace(data=code))


class SpeedTests(unittest.TestCase):
    def test_caps_follow_the_setter(self):
        self.assertEqual(w.speed_caps(1.0), (13107, 26214))
        self.assertEqual(w.speed_caps(0.0), (655, 655))           # the floor on both
        self.assertEqual(w.speed_caps(0.04), (655, 1048))
        self.assertEqual(w.speed_caps(5.0), w.speed_caps(2.0))    # input capped at 2.0
        self.assertEqual(w.speed_caps(2.0), (26214, 52428))

    def test_smoothing_and_its_lower_bound(self):
        self.assertAlmostEqual(w.smooth_speed(0.0, 60), 0.15)
        self.assertAlmostEqual(w.smooth_speed(1.0, 100, 25, 0), 1.0625)
        self.assertEqual(w.BASE_SPEEDS, (60, 80, 100, 120, 140))
        speed = 0.0
        for _ in range(15):
            speed = w.smooth_speed(speed, min(w.BASE_SPEEDS))
        self.assertAlmostEqual(speed, w.speed_lower_bound(15))
        self.assertGreater(w.speed_lower_bound(15), 0.59)          # age >= 15 updates
        self.assertLess(w.speed_lower_bound(14), 0.59)
        self.assertGreater(w.speed_lower_bound(5), 0.44)

    def test_top_speed_in_cells_per_second(self):
        self.assertAlmostEqual(w.cells_per_second(13107), 0.8064, places=4)
        self.assertAlmostEqual(w.cells_per_second(w.speed_caps(0.6)[0]), 0.4839, places=4)


class ArrivalTests(unittest.TestCase):
    def test_radii(self):
        self.assertEqual(w.arrival_radius(), 20971)                # FixMul(1.6, 0.2) = 0.32 cell
        self.assertEqual(w.arrival_radius('floor'), 20971)
        self.assertEqual(w.waypoint_radius(), 26214)               # FixMul(2.0, 0.2) = 0.4 cell
        self.assertEqual(w.ARRIVE_FACTOR, 104857)

    def test_desired_speed_floor(self):
        self.assertEqual(w.desired_speed_floor(7733), 7733)
        self.assertEqual(w.desired_speed_floor(26214), 9379)       # d / 2 with d >= 0.32 / 1.118

    def test_fixed_point_helpers(self):
        self.assertEqual(w.octile(3 * w.ONE, 4 * w.ONE), 7 * w.ONE - 3 * w.ONE // 2)
        self.assertEqual(w.length(3000, 4000), 5000)
        self.assertEqual(w.length(3 * w.ONE, 4 * w.ONE), 5 * w.ONE)
        self.assertEqual(w.truncate((3 * w.ONE, 4 * w.ONE), 5 * w.ONE), (3 * w.ONE, 4 * w.ONE))
        self.assertEqual(w.truncate((6 * w.ONE, 8 * w.ONE), 5 * w.ONE), (3 * w.ONE, 4 * w.ONE))
        self.assertEqual(w.fix_div(1, 0), 0x7fffffff)


class GeometryTests(unittest.TestCase):
    def test_points(self):
        self.assertEqual(w.stand_point(), (127 << 8, -w.ONE + (127 << 8)))
        self.assertEqual(w.slot_point(0, 114), (114 << 8, 0))
        self.assertEqual(w.slot_point(3, 141), (141 << 8, 191 << 8))
        self.assertEqual(w.slot_point(3, 141, reversed_axis=True), (141 << 8, 64 << 8))
        self.assertEqual(w.centre((0, -1)), (32768, -32768))

    def test_routes_include_the_start_cell(self):
        self.assertEqual(w.route_cells(w.FRONT, w.ENTRANCE), [w.FRONT, w.ENTRANCE])
        self.assertEqual(w.route_cells(w.BEHIND, w.ENTRANCE), [w.BEHIND, w.FRONT, w.ENTRANCE])
        self.assertEqual(w.route_cells(w.FRONT, w.FRONT), [w.FRONT])


class WalkTests(unittest.TestCase):
    def test_straight_walk_arrives(self):
        updates, info = w.walk(w.slot_point(0, 127), (0, 0), w.stand_point(), [w.FRONT, w.ENTRANCE], 1.0,
                               strip=w.STRIP)
        self.assertIsNotNone(updates)
        self.assertLessEqual(updates, 8)
        self.assertEqual(info['walls'], 0)

    def test_bounds_at_the_speed_floor_of_old_guests(self):
        # The pinned worst cases of the reduced grid (the full enumeration is in --derive).
        self.assertEqual(w.worst_walk('w', 0.59, grid=7)[0], 10)
        self.assertEqual(w.worst_walk('w2', 0.59, grid=7)[0], 14)
        self.assertEqual(w.worst_walk('w', 0.59, grid=7, reversed_axis=True)[0], 17)
        for kind in ('w', 'w2'):
            for separation, fences, rounding in w.VARIANTS:
                worst, _, failures = w.worst_walk(kind, 0.59, rounding=rounding, separation=separation,
                                                  fences=fences, grid=5)
                self.assertEqual(failures, [], (kind, separation, fences, rounding))
                self.assertLessEqual(worst, {'w': 12, 'w2': 15}[kind])

    def test_slow_guests_can_trigger_the_reroute(self):
        # Progress is measured against the cell centre, so a slow move-up to the front edge looks stuck.
        failures = w.worst_walk('w2', 0.2, grid=7)[2]
        self.assertTrue(failures)
        self.assertEqual({reason for _, _, reason in failures}, {'reroute'})
        worst, _, failures = w.worst_walk('w', 0.0, grid=7)
        self.assertEqual((worst, failures), (72, []))           # the stand walk completes even at the floor

    def test_arrival_factor_matters(self):
        original = w.ARRIVE_FACTOR
        try:
            w.ARRIVE_FACTOR = 0x10000                             # 1.0 instead of 1.6: longer walks
            self.assertGreater(w.worst_walk('w', 0.59, grid=7)[0], 10)
        finally:
            w.ARRIVE_FACTOR = original

    def test_varying_speed(self):
        self.assertEqual(w.varying_speed_check(6, 12, 15, w.speed_lower_bound(15)), 12)


class GateArithmeticTests(unittest.TestCase):
    def test_opentpw_walk_terms(self):
        self.assertEqual(w.implementation_walk_turns((141 - 114) / 255 / 2), 2)
        self.assertEqual(w.implementation_walk_turns(((191 / 255) ** 2 + (27 / 255) ** 2) ** 0.5), 6)

    def test_latency_and_bound(self):
        self.assertEqual(w.boarding_latency(20, 15), 56)
        self.assertEqual(w.wait_bound(99, 56), 8021)
        self.assertEqual(w.wait_bound(50, 56), 4188)
        self.assertEqual(w.wait_bound(99, w.boarding_latency(2, 6)), 5321)   # BOARD-plan's OpenTPW figure


class DecoderTests(unittest.TestCase):
    def test_synthetic_fields_pass_and_each_mutation_fails(self):
        c = synthetic_fields_container()
        self.assertEqual(w.check_fields(c), len(w.FIELDS) + len(w.RLWINM))
        sites = [w.FIELDS[0][0], w.FIELDS[len(w.FIELDS) // 2][0], w.FIELDS[-1][0], w.RLWINM[0][0]]
        for at in sites:
            mutated = synthetic_fields_container()
            word = struct.unpack_from('>I', mutated.code.data, at)[0]
            struct.pack_into('>I', mutated.code.data, at, word ^ (1 << 11))
            with self.assertRaises(w.pef.PEFError):
                w.check_fields(mutated)

    def test_key_operands_are_pinned(self):
        pinned = {(at, op): fields for at, op, fields, _ in w.FIELDS}
        self.assertEqual(pinned[(0xffea4, 14)], (0, 0, 655))          # max speed floor
        self.assertEqual(pinned[(0xfe7b4, 14)], (3, 3, -26215))       # arrival factor 1.6 (low half)
        self.assertEqual(pinned[(0xffaa8, 14)], (25, 27, -32768))     # follow_path weight 0.5
        self.assertEqual(pinned[(0xffc68, 14)], (0, 0, 13107))        # radius 0.2
        self.assertEqual(pinned[(0xe6bbc, 7)], (0, 0, 99))            # boost decay


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
class IdentifiedBinaryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ['OPENTPW_PPC_BIN_ROOT'])

    def test_identified_original_witness(self):
        report = w.inspect(self.root)
        self.assertEqual(report['field_witnesses'], len(w.FIELDS) + len(w.RLWINM))
        self.assertEqual(report['call_witnesses'], len(w.CALLS) + len(w.GLUE_CALLS))
        self.assertEqual(report['behaviour_methods']['follow_path'], '0xfe628')
        self.assertEqual(report['witness_source'], 'tools/ppc-analysis/lanes/walk/walk_evidence.py')

    def _mutated(self, section, offset, packed):
        c = w.load_identified(self.root / 'SimThemePark.data')
        data = bytearray(getattr(c, section).data)
        data[offset:offset + len(packed)] = packed
        getattr(c, section).data = data
        return c

    def test_wrong_speed_floor_is_rejected(self):
        c = self._mutated('code', 0xffea4, struct.pack('>I', d_word(14, 0, 0, 654)))
        with self.assertRaises(w.pef.PEFError):
            w.check_fields(c)

    def test_wrong_arrival_factor_is_rejected(self):
        c = self._mutated('code', 0xfe7b4, struct.pack('>I', d_word(14, 3, 3, -26214)))
        with self.assertRaises(w.pef.PEFError):
            w.check_fields(c)

    def test_wrong_follow_path_weight_is_rejected(self):
        c = self._mutated('code', 0xffaa8, struct.pack('>I', d_word(14, 25, 27, -32767)))
        with self.assertRaises(w.pef.PEFError):
            w.check_fields(c)

    def test_wrong_base_speed_is_rejected(self):
        c = self._mutated('data_section', w.SPEED_TABLE_AT + 6, struct.pack('>H', 59))
        with self.assertRaises(w.pef.PEFError):
            w.check_data(c)

    def test_wrong_speed_factor_is_rejected(self):
        c = self._mutated('data_section', 0x5650, struct.pack('>d', 0.25))
        with self.assertRaises(w.pef.PEFError):
            w.check_data(c)


if __name__ == '__main__':
    unittest.main()
