import unittest

import controller_math as math


class ControllerMathTests(unittest.TestCase):
    def test_signed_division_and_remainder(self):
        for dividend, divisor, expected in ((7, 3, (2, 1)), (-7, 3, (-2, -1)),
                                             (7, -3, (-2, 1)), (-7, -3, (2, -1))):
            self.assertEqual(math.rse_divmod(dividend, divisor), expected)
        self.assertEqual(math.rse_divmod(123, 0), (0, 0))
        with self.assertRaises(ValueError):
            math.rse_divmod(-(1 << 31), -1)

    def test_ring_wraps_and_rejects_empty(self):
        self.assertEqual([math.ring_next(i, 4) for i in range(4)], [1, 2, 3, 0])
        self.assertEqual(math.ring_next(0, 1), 0)
        with self.assertRaises(ValueError):
            math.ring_next(0, 0)

    def test_distribution_conserves_and_puts_remainder_later(self):
        self.assertEqual(math.distribute_capacity(10, 3), [3, 3, 4])
        self.assertEqual(math.distribute_capacity(2, 4), [0, 0, 1, 1])
        for total in range(100):
            for count in range(1, 12):
                assignments = math.distribute_capacity(total, count)
                self.assertEqual(sum(assignments), total)
                self.assertLessEqual(max(assignments) - min(assignments), 1)
                self.assertEqual(assignments, sorted(assignments))
        with self.assertRaises(ValueError):
            math.distribute_capacity(1, 0)

    def test_centered_offsets_and_normalization(self):
        offsets, normalized = math.centered_offsets([2, 3, 4, 5, 6], 2, 10)
        self.assertEqual(offsets, [5.0, 3.0, 0.0, -5.0, -11.0])
        self.assertEqual(normalized, [math.f32(v) for v in (.5, .3, 0, -.5, -1.1)])
        self.assertEqual(math.centered_offsets([7], 0, 10), ([0.0], [0.0]))
        with self.assertRaises(ValueError):
            math.centered_offsets([1, 2], 1, 0)

    def test_group_ordinal_and_disabled_grouping(self):
        self.assertEqual([math.tour_group_ordinal(i, 3) for i in range(6)], [1, 2, 3, 1, 2, 3])
        self.assertIsNone(math.tour_group_ordinal(4, 0))

    def test_tour_departure_uses_reverse_boarding_order(self):
        count = -3
        slots = []
        while count < 0:
            count, slot = math.tour_departure_slot(count)
            slots.append(slot)
        self.assertEqual(slots, [2, 1, 0])
        with self.assertRaises(ValueError):
            math.tour_departure_slot(0)

    def test_heading_magic_division_matches_qualified_domain(self):
        self.assertEqual([math.tour_heading_input(v) for v in (0, 45, 90, 180, 270)], [1024, 512, 0, -1024, -2048])
        for source_angle in range(360):
            numerator = source_angle << 12
            reciprocal = -1240768329  # Signed constant recovered from create callsite.
            quotient = ((numerator * reciprocal >> 32) + numerator) >> 8
            quotient += int(quotient < 0)
            self.assertEqual(math.tour_heading_input(source_angle), 1024 - quotient)
        with self.assertRaises(ValueError):
            math.tour_heading_input(360)


if __name__ == "__main__":
    unittest.main()
