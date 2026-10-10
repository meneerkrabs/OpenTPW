"""Synthetic vectors for the determinism reference model. No original file is needed."""
import re
import unittest
from pathlib import Path

import det_model as m

PLAN = Path(__file__).resolve().parents[4] / 'docs' / 'reverse' / 'DET-plan.md'


class WorldGeneratorTests(unittest.TestCase):
    def test_first_draws_from_seed_zero(self):
        state, value = m.world_next(0)
        self.assertEqual((state, value), (1013904223, 1013904223))
        state, value = m.world_next(state)
        self.assertEqual(state, (1013904223 * 1664525 + 1013904223) & m.MASK32)

    def test_rse_rand_matches_rides_vectors(self):
        # Rides lane vector: seed 0, bound 10 -> 6 then 8 from one shared context.
        state, first = m.world_next(0)
        state, second = m.world_next(state)
        self.assertEqual((m.rse_rand(first, 10), m.rse_rand(second, 10)), (6, 8))
        # Independently restarting at seed 0 repeats 6: shared-vs-per-script ownership is observable.
        self.assertEqual(m.rse_rand(m.world_next(0)[1], 10), 6)

    def test_rse_rand_uses_raw_signed16_bound(self):
        returned = m.world_next(0)[1]
        self.assertEqual(m.rse_rand(returned, 0x40000003), 3)   # variable-kind word: low 16 bits only
        self.assertEqual(m.rse_rand(returned, 0xfffe), 0)       # bound -2 -> divisor -1
        with self.assertRaises(ValueError):
            m.rse_rand(returned, 0xffff)                         # bound -1 unsupported

    def test_wrapping_absolute_keeps_int_min_bits(self):
        self.assertEqual(m.wrapping_abs32(0x80000000), 0x80000000)
        self.assertEqual(m.wrapping_abs32(0xffffffff), 1)
        self.assertEqual(m.rse_rand(0x80000000, 32767), 1073741824 % 32768)

    def test_reseed_is_the_unsigned_thing_id(self):
        self.assertEqual(m.world_reseed(0x1234), 0x1234)
        with self.assertRaises(ValueError):
            m.world_reseed(0x10000)
        # Two guests with the same ID draw the same post-reseed values regardless of prior history.
        a = m.world_next(m.world_reseed(77))
        b = m.world_next(m.world_reseed(77))
        self.assertEqual(a, b)


class OwnStateGeneratorTests(unittest.TestCase):
    def test_coaster_logical_high_half(self):
        self.assertEqual(m.coaster_next(12345)[1], 40352)
        self.assertEqual(m.coaster_next(12345)[1] % 3, 2)
        self.assertNotEqual(m.coaster_next(12345)[1] & 32767, m.coaster_next(12345)[1])

    def test_object_generators_shift_arithmetically(self):
        state = 0x40000000
        new, value = m.object_ms_next(state)
        self.assertEqual(new, m.coaster_next(state)[0])
        if new & 0x80000000:
            self.assertLess(value, 0)
        self.assertEqual(value & 0xffff, m.coaster_next(state)[1])

    def test_weather_returns_full_state(self):
        self.assertEqual(m.weather_next(0), (1013904223, 1013904223))
        state = 0x7fffffff
        new, value = m.weather_next(state)
        self.assertEqual(new, value)

    def test_clib_rand_ansi_vector(self):
        self.assertEqual(m.clib_rand(1)[1], 16838)

    def test_sound_seed_is_not_advanced(self):
        self.assertEqual(m.sound_candidate(99), m.sound_candidate(99))


class SchedulerTests(unittest.TestCase):
    def kinds(self, state, kind):
        return [step for step, k in state.events if k == kind]

    def test_one_ms_gap_runs_one_substep(self):
        state = m.catch_up(m.SchedulerState(previous=1000), 1001)
        self.assertEqual((state.substep, state.previous), (1, 1031))

    def test_two_second_gap_caps_park_turns(self):
        state = m.catch_up(m.SchedulerState(previous=0), 2000, mode=2)
        self.assertEqual(state.substep, 65)
        self.assertEqual(state.previous, 2015)
        self.assertEqual(self.kinds(state, 'turn'), [8, 16, 24])
        self.assertEqual(self.kinds(state, 'turn_capped'), [32, 40, 48, 56, 64])
        self.assertEqual(self.kinds(state, 'thirty_second'), [32, 64])
        self.assertEqual(len(self.kinds(state, 'even')), 32)
        self.assertEqual(state.park_work, 0)

    def test_excess_backlog_is_dropped(self):
        state = m.catch_up(m.SchedulerState(previous=0), 5000)
        self.assertEqual((state.substep, state.previous), (65, 5015))

    def test_excluded_world_still_advances_time(self):
        state = m.catch_up(m.SchedulerState(), 100, gameplay_flag1=True)
        self.assertEqual(state.substep, 4)
        self.assertEqual({kind for _, kind in state.events}, {'excluded'})
        state = m.catch_up(m.SchedulerState(), 100, gameplay_flag1=True, application_flag8=True)
        self.assertIn('work', {kind for _, kind in state.events})

    def test_mode_three_consumes_cap_without_turn(self):
        state = m.catch_up(m.SchedulerState(), 300, mode=3)
        self.assertEqual(state.park_turn, 0)
        self.assertEqual(self.kinds(state, 'eighth'), [8])

    def test_signed_boundary_does_not_terminate(self):
        with self.assertRaises(RuntimeError):
            m.catch_up(m.SchedulerState(previous=0x7ffffffe), 0x7fffffff, max_steps=1000)

    def test_interpolation_alpha_is_non_positive_after_catch_up(self):
        state = m.catch_up(m.SchedulerState(previous=0), 1000)
        alpha = m.interpolation_alphas(1000, state.previous, 0, 0)[0]
        self.assertTrue(-1.0 < alpha <= 0.0)


class PlanConsistencyTests(unittest.TestCase):
    @unittest.skipUnless(PLAN.is_file(), 'DET plan not present in this checkout')
    def test_plan_lists_every_canonical_replay_field(self):
        text = PLAN.read_text(encoding='utf-8')
        missing = [name for name in m.CANONICAL_REPLAY_FIELDS if not re.search(rf'`{name}`', text)]
        self.assertEqual(missing, [])


if __name__ == '__main__':
    unittest.main()
