"""UI-092 clean numeric/source guards; these do not execute C# or original software.

The decimal oracle derives every lookup value from mathematics. This file requires
only the Python standard library and repository source, never private fixtures or assets.
"""
from decimal import Decimal, localcontext
import math
from pathlib import Path
import re
import struct
import unittest


ROOT = Path(__file__).resolve().parents[1]


def signed32(value):
    return (value + (1 << 31)) % (1 << 32) - (1 << 31)


def atan_series(value):
    power = total = value
    for n in range(1, 1000):
        power *= -value * value
        next_total = total + power / (2 * n + 1)
        if next_total == total:
            return total
        total = next_total
    raise AssertionError("The bounded decimal atan series did not converge.")


def mathematical_angles():
    with localcontext() as context:
        context.prec = 100
        pi = 16 * atan_series(Decimal(1) / 5) - 4 * atan_series(Decimal(1) / 239)
        angles = []
        for i in range(257):
            value = Decimal(i) / 256
            radians = (pi / 4 + atan_series((value - 1) / (value + 1))
                       if value > Decimal("0.5") else atan_series(value))
            angles.append(256 if i == 256 else int(radians * 1024 / pi))
        return angles


def movement_angle(dx, dy, lookup):
    a, b = signed32(-dx), signed32(dy)
    if a == 0 and b == 0:
        return 0
    negative_a, negative_b = a < 0, b < 0
    magnitude_a = signed32(-a) if negative_a else a
    magnitude_b = signed32(-b) if negative_b else b
    first = magnitude_a >= magnitude_b
    low, high = (magnitude_b, magnitude_a) if first else (magnitude_a, magnitude_b)
    denominator = high & 0xffffffff
    if denominator == 0:
        return None
    index = ((low << 8) & 0xffffffff) // denominator
    assert 0 <= index <= 256
    q = lookup[index]
    octants = {
        (False, False): (512 + q, 1024 - q),
        (False, True): (512 - q, q),
        (True, False): (1536 - q, 1024 + q),
        (True, True): (1536 + q, 2048 - q),
    }
    return octants[negative_a, negative_b][0 if first else 1] & 2047


def binary32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def interval_for_sum(value, run):
    if value <= 0:
        return 0
    speed = binary32(math.sqrt(value) * binary32(3.829657e-05))
    if not run:
        speed = binary32(speed + speed)
    return min(250, int(62.0 / speed))


def integer_literal(value):
    return {"int.MinValue": -(1 << 31), "int.MaxValue": (1 << 31) - 1,
            "null": None}.get(value, int(value) if value.lstrip("-").isdigit() else None)


class StaffMotionMathContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = (ROOT / "source/OpenTPW/World/Staff/StaffSpriteMotionMath.cs").read_text()
        cls.tests = (ROOT / "source/OpenTPW.Tests/StaffSpriteMotionMathTests.cs").read_text()
        cls.guest = (ROOT / "source/OpenTPW/World/Guests/GuestSimulation.SpriteState.cs").read_text()
        cls.lookup = mathematical_angles()

    def test_n05_all_257_values_match_an_independent_high_precision_mathematical_oracle(self):
        double_angles = [0] + [math.floor(math.atan(i / 256) * 1024 / math.pi)
                               for i in range(1, 256)] + [256]
        self.assertEqual(257, len(self.lookup))
        self.assertEqual(self.lookup, double_angles)
        self.assertIn("Math.Floor( Math.Atan( i / 256d ) * 1024d / Math.PI )", self.source)
        self.assertIn("angles[0] = 0", self.source)
        self.assertIn("angles[256] = 256", self.source)
        self.assertIn("DecimalAtanSeries", self.tests)
        self.assertIn("i <= 256", self.tests)

    def test_n02_all_49_csharp_oracle_rows_match_the_independent_integer_contract(self):
        rows = self.tests.split("movementCases =", 1)[1].split("[TestMethod]", 1)[0]
        rows = re.findall(r"\( ([^()\n]+) \)", rows)
        self.assertEqual(49, len(rows))
        invalid_count = 0
        for row in rows:
            dx, dy, expected, expected_heading = map(integer_literal, row.split(", "))
            with self.subTest(dx=dx, dy=dy):
                result = movement_angle(dx, dy, self.lookup)
                self.assertEqual(expected, result)
                if result is None:
                    invalid_count += 1
                    self.assertIsNone(expected_heading)
                else:
                    self.assertEqual(expected_heading, ((result - 896) & 2047) >> 8)
        self.assertEqual(2, invalid_count)

    def test_n02_all_2048_publications_match_sector_ranges_and_wrapped_periods(self):
        sectors = ([4] * 128 + [5] * 256 + [6] * 256 + [7] * 256 + [0] * 256
                   + [1] * 256 + [2] * 256 + [3] * 256 + [4] * 128)
        self.assertEqual(2048, len(sectors))
        for angle, expected in enumerate(sectors):
            for offset in (0, -2048, 2048, -(1 << 31)):
                self.assertEqual(expected, (signed32(angle + offset - 896) & 2047) >> 8)
        self.assertIn("(unchecked( angle - 0x380 ) & 0x7ff) >> 8", self.source)

    def test_n01_all_12_interval_vectors_keep_wrapped_sums_and_double_quotients(self):
        rows = re.findall(r"\[DataRow\( (.+?) \)\]", self.tests)
        self.assertEqual(12, len(rows))
        for row in rows:
            before_x, before_y, after_x, after_y, expected_sum, run, walk = map(integer_literal, row.split(", "))
            dx, dy = signed32(after_x - before_x), signed32(after_y - before_y)
            value = signed32(dx * dx + dy * dy)
            with self.subTest(row=row):
                self.assertEqual(expected_sum, value)
                self.assertEqual(run, interval_for_sum(value, True))
                self.assertEqual(walk, interval_for_sum(value, False))
                if value > 0:
                    self.assertTrue(34 <= run <= 250)
                    self.assertTrue(17 <= walk <= 250)

    def test_source_preserves_signed_comparisons_wrapped_shifts_and_shared_guest_policy(self):
        for token in ("unchecked( beforeX - afterX )", "unchecked( afterY - beforeY )",
                      "unchecked( -a )", "unchecked( -b )", "magnitudeA >= magnitudeB",
                      "unchecked( (uint)low << 8 )", "unchecked( (uint)high )",
                      "denominator == 0", "InvalidAngleArithmetic", "return false;",
                      "GuestSimulation.BodyIntervalForStep( beforeX, beforeY, afterX, afterY, run )",
                      "nearest p53/p64-compatible", "masked floating exceptions"):
            self.assertIn(token, self.source)
        for forbidden in ("Atan2", "Math.Abs", "MathF.Atan", "Random", "NextDouble", "ulong", "long "):
            self.assertNotIn(forbidden, self.source)
        for token in ("unchecked( afterX - beforeX )", "unchecked( afterY - beforeY )",
                      "Math.Sqrt( unchecked( dx * dx + dy * dy ) ) * SpritePlayer.SpeedPerUnit",
                      "speed += speed", "!float.IsFinite( speed ) || speed <= 0",
                      "(int)((double)SpritePlayer.IntervalNumerator / speed)"):
            self.assertIn(token, self.guest)

    def test_n06_only_two_axes_fail_in_an_extreme_component_cross_product(self):
        components = (-(1 << 31), -(1 << 31) + 1, -16777216, -256, -1,
                      0, 1, 256, 16777216, (1 << 31) - 1)
        for dx in components:
            for dy in components:
                expected_invalid = (dx == -(1 << 31) and dy == 0) or (dx == 0 and dy == -(1 << 31))
                result = movement_angle(dx, dy, self.lookup)
                self.assertEqual(expected_invalid, result is None)
                if result is not None:
                    self.assertTrue(0 <= result <= 2047)

    def test_strike_api_keeps_already_consumed_random_word_and_unsigned_clamp(self):
        self.assertIn("StrikeAngle( int angle, uint random )", self.source)
        self.assertIn("(random & 7) != 0", self.source)
        self.assertIn("unchecked( angle + (int)(random & 0xff) - 128 )", self.source)
        self.assertIn("unchecked( (uint)adjusted ) > 0x7ff ? 0x7ff : adjusted", self.source)
        self.assertIn("random < 256", self.tests)
        self.assertIn("angle < 2048", self.tests)
        self.assertIn("random | 0xabcdef00", self.tests)


if __name__ == "__main__":
    unittest.main()
