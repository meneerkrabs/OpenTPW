"""Tests of the reference channel/scene clock model; synthetic inputs only, no original binaries."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from clip_clock_reference import (FREEZE_AT_END, FREEZE_AT_START, RATE_MAX, RATE_MIN, RATE_RESET,  # noqa: E402
                                  SELECT_CLOCK_B, Channel, SceneClocks, clamp_rate, f32, faster, fresh_start,
                                  instantiate, reset_channels, slower, to_unsigned, update)


def started(flags: int, clocks: SceneClocks, duration: int = 300) -> Channel:
    channel = Channel(flags=flags)
    fresh_start(channel, duration, clocks)
    return channel


class SelectorTests(unittest.TestCase):
    def test_bit_0x40_reads_clock_b(self):
        clocks = SceneClocks.started(raw=1000, rate=2.0)
        clocks.refresh(1000)
        on_a, on_b = started(0, clocks), started(SELECT_CLOCK_B, clocks)
        clocks.refresh(2000)
        self.assertEqual((update(on_a, clocks), update(on_b, clocks)), (60.0, 30.0))
        self.assertEqual((on_a.anim_time, on_b.anim_time), (clocks.clock_a, clocks.clock_b))

    def test_rate_one_matches_the_thirty_tick_clock(self):
        clocks = SceneClocks.started(raw=0)
        clocks.refresh(0)
        channel = started(0, clocks)
        clocks.refresh(999)
        self.assertEqual(update(channel, clocks), f32(29.97))
        clocks.refresh(1000)
        self.assertEqual(update(channel, clocks), 30.0)

    def test_rate_change_is_not_a_ticks_per_second_change(self):
        # Negative: the rate scales clock A's accumulation from its last read on, not the whole clip.
        clocks = SceneClocks.started(raw=0)
        clocks.refresh(0)
        channel = started(0, clocks)
        clocks.refresh(1000)
        first = update(channel, clocks)
        clocks.a.rate = 2.0
        clocks.refresh(2000)
        second = update(channel, clocks)
        self.assertEqual((first, second), (30.0, 90.0))
        candidates = {first * 1000 / 1000, second * 1000 / 2000}
        self.assertEqual(len(candidates), 2, 'no single ticks-per-second value gives both frames')

    def test_constant_rate_truncates_scaled_milliseconds(self):
        # Negative: rate 1.5 is not 45 ticks/s; clock A is whole milliseconds after scaling.
        clocks = SceneClocks.started(raw=0, rate=1.5)
        clocks.refresh(0)
        channel = started(0, clocks)
        clocks.refresh(1001)
        self.assertEqual(clocks.clock_a, 1501)
        self.assertEqual(update(channel, clocks), f32(f32(30.0 * 1501) / 1000))
        self.assertNotEqual(update(channel, clocks), f32(f32(45.0 * 1001) / 1000))

    def test_pause_stops_both_and_hold_stops_only_a(self):
        clocks = SceneClocks.started(raw=0)
        clocks.refresh(0)
        a, b = started(0, clocks), started(SELECT_CLOCK_B, clocks)
        clocks.refresh(500)
        clocks.pause(500)
        clocks.refresh(900)
        self.assertEqual((update(a, clocks), update(b, clocks)), (15.0, 15.0))
        clocks.resume(900)
        clocks.refresh(1400)
        self.assertEqual((update(a, clocks), update(b, clocks)), (30.0, 30.0))
        clocks.held, clocks.held_value = True, clocks.clock_a
        clocks.refresh(2400)
        self.assertEqual((update(a, clocks), update(b, clocks)), (30.0, 60.0))

    def test_selector_and_start_must_name_the_same_clock(self):
        # Negative: a start taken on A and read on an earlier B wraps the unsigned difference.
        clocks = SceneClocks.started(raw=0, rate=2.0)
        clocks.refresh(1000)
        channel = started(0, clocks)
        channel.flags |= SELECT_CLOCK_B
        frame = update(channel, clocks)
        self.assertEqual(frame, f32(f32(30.0 * f32(float(2 ** 32 - 1000))) / 1000))
        self.assertGreater(frame, 1e8)


class FrozenChannelTests(unittest.TestCase):
    def test_freeze_bits(self):
        clocks = SceneClocks.started(raw=0)
        clocks.refresh(0)
        at_start, at_end = started(FREEZE_AT_START | FREEZE_AT_END, clocks, 45), started(FREEZE_AT_END, clocks, 45)
        clocks.refresh(5000)
        self.assertEqual(update(at_start, clocks), 0.0)
        self.assertEqual(update(at_end, clocks), 45.0)
        self.assertEqual((at_start.anim_time, at_end.anim_time), (0, 1500))
        self.assertEqual((at_start.no_pause_time, at_end.no_pause_time), (5000, 5000))

    def test_unsigned_conversion(self):
        self.assertEqual([to_unsigned(v) for v in (-0.5, 0.99, 2.0 ** 31 + 0.5, 2.0 ** 32)],
                         [0, 0, 2 ** 31, 0xffffffff])
        with self.assertRaises(ValueError):
            to_unsigned(float('nan'))


class ControlTests(unittest.TestCase):
    def test_rate_keys_step_by_a_quarter_and_clamp(self):
        rates = [RATE_RESET]
        for _ in range(4):
            rates.append(faster(rates[-1]))
        self.assertEqual(rates, [1.0, 1.25, 1.5625, 1.953125, RATE_MAX])
        # Clamping makes the rate path-dependent: back down from 2.0 is not the way up.
        self.assertEqual(slower(RATE_MAX), 1.6)
        self.assertNotIn(slower(RATE_MAX), rates)
        rate = RATE_RESET
        for _ in range(7):
            rate = slower(rate)
        self.assertEqual(rate, RATE_MIN)
        self.assertEqual(clamp_rate(float('nan')), RATE_MIN)

    def test_hold_steps_a_fixed_31_ms_per_pass(self):
        clocks = SceneClocks.started(raw=0)
        clocks.refresh(1000)
        clocks.hold(1000)
        self.assertEqual(clocks.held_step, 31, '1000 // 32 truncates 31.25')
        channel = started(0, clocks)
        for _ in range(10):
            clocks.step()
        clocks.refresh(60000)
        self.assertEqual(clocks.clock_a, 1310, 'real time does not move a held clock A')
        self.assertEqual(update(channel, clocks), f32(f32(30.0 * 310) / 1000))
        self.assertEqual(clocks.clock_b, 60000, 'clock B keeps real time while A is held')

    def test_paused_hold_does_not_step_and_release_continues(self):
        clocks = SceneClocks.started(raw=0)
        clocks.hold(500)
        clocks.pause(500)
        clocks.step()
        clocks.resume(800)
        clocks.step()
        clocks.release(2000)
        clocks.refresh(2000)
        self.assertEqual(clocks.clock_a, 531)
        clocks.refresh(2100)
        self.assertEqual(clocks.clock_a, 631)


class InheritanceTests(unittest.TestCase):
    def test_instances_copy_the_template_selector(self):
        template = [Channel(), Channel()]
        placed = instantiate(template)
        reset_channels(placed, use_clock_b=False)
        self.assertEqual([c.flags for c in placed], [0, 0])
        advisor = instantiate(template)
        reset_channels(advisor, use_clock_b=True)
        self.assertEqual([c.flags for c in advisor], [SELECT_CLOCK_B] * 2)
        self.assertEqual([c.flags for c in template], [0, 0], 'the copy does not write back')

    def test_reset_without_clock_b_keeps_an_inherited_bit(self):
        # Negative: 0xa7bb8 only ORs; an instance of a template with 0x40 keeps it.
        instance = instantiate([Channel(flags=SELECT_CLOCK_B | 0x1)])
        reset_channels(instance, use_clock_b=False)
        self.assertEqual(instance[0].flags, SELECT_CLOCK_B | 0x1)


if __name__ == '__main__':
    unittest.main()
