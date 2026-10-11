"""Reference-model tests for the traced animation paths (synthetic inputs only)."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import anim_model as m  # noqa: E402


def channel(**overrides):
    state = {'anim': m.NO_ANIMATION, 'sub': 0, 'flags': 0, 'frame': 0.0, 'total': 0.0,
             'deferred': m.NO_ANIMATION, 'deferred_sub': 0}
    state.update(overrides)
    return state


class UvTests(unittest.TestCase):
    def test_key_search_uses_first_later_tick(self):
        ticks = [0, 10, 30]
        self.assertEqual(m.uv_key(ticks, 0.0), (0, 0.0))
        self.assertEqual(m.uv_key(ticks, 9.5), (0, 0.95))
        # trunc(10.0) is not < 10, so the search moves to the next segment (unlike the vertex cursor).
        self.assertEqual(m.uv_key(ticks, 10.0), (1, 0.0))
        self.assertEqual(m.uv_key(ticks, 20.0), (1, 0.5))
        self.assertEqual(m.uv_key(ticks, 30.0), (1, 1.0))
        self.assertEqual(m.uv_key(ticks, 99.0), (1, 1.0))
        with self.assertRaises(ValueError):
            m.uv_key([5, 10], 1.0)

    def test_sample_and_destination(self):
        self.assertEqual(m.uv_sample([0, 10], [(0.0, 1.0), (1.0, 0.0)], 2.5), (0.25, 0.75))
        self.assertEqual(m.uv_destination(0), (0, 16))
        self.assertEqual(m.uv_destination(5), (36, 52))

    def test_gate(self):
        self.assertTrue(m.uv_pass_runs(0x10000, 0, 0, 0))
        self.assertFalse(m.uv_pass_runs(0x1000, 0, 0, 0))
        self.assertFalse(m.uv_pass_runs(0x10000, 0x10, 0, 0))
        self.assertFalse(m.uv_pass_runs(0x10000, 0, 0x4000, 0))
        self.assertTrue(m.uv_pass_runs(0x10000, 0x10, 0x4000, 0x4))


class Block12Tests(unittest.TestCase):
    def test_node_bits(self):
        node = m.reset_node_bits(0)
        self.assertEqual(node, 0x100)
        self.assertEqual(m.bind_node_bits(node, 0x5000, category=0), 0x900)
        bound = m.bind_node_bits(node, 0x5000, category=5)
        self.assertEqual(bound, 0x800)
        self.assertEqual(m.clear_pass(bound), 0x800)  # 0x100 cleared: the 0x800 mark stays
        self.assertEqual(m.clear_pass(0x900), 0x100)  # category 0 keeps 0x100: the mark is dropped
        self.assertEqual(m.bind_node_bits(node, 0x1000, category=5), 0x100)


class TriggerTests(unittest.TestCase):
    def test_start_length_ignores_speed(self):
        self.assertEqual(m.start_length_ms(430), 14333)
        self.assertEqual(m.start_length_ms(10), 333)
        state = channel()
        self.assertEqual(m.trigger(state, 5, 0, 0, 4.0, 430), 14333)
        self.assertEqual((state['anim'], state['total']), (5, 430.0))

    def test_missing_clip_reports_1000(self):
        self.assertEqual(m.trigger(channel(), 5, 3, 0, 1.0, None), 1000)

    def test_deferral_only_with_flag_2(self):
        running = channel(anim=5, frame=100.0, total=300.0)
        self.assertEqual(m.trigger(running, 6, 0, 0x2, 1.0, 30, current_ticks=300),
                         m.fctiwz(m.f32(33.333332 * 200.0)) + 1000)
        self.assertEqual((running['anim'], running['deferred']), (5, 6))
        self.assertTrue(m.flush(running))
        self.assertEqual((running['anim'], running['deferred']), (5, m.NO_ANIMATION))
        self.assertFalse(m.flush(running))
        # Script opcodes pass flags 0 or 1: the new clip replaces the running one at once.
        replaced = channel(anim=5, frame=100.0, total=300.0)
        m.trigger(replaced, 6, 0, 0, 1.0, 30, current_ticks=300)
        self.assertEqual((replaced['anim'], replaced['deferred']), (6, m.NO_ANIMATION))
        # AnimID 13/14 never defer.
        special = channel(anim=5, frame=1.0, total=300.0)
        m.trigger(special, 13, 0, 0x2, 1.0, 30, current_ticks=300)
        self.assertEqual(special['anim'], 13)

    def test_loop_and_flag_4(self):
        state = channel()
        m.trigger(state, 5, 0, 0x1 | 0x4, 1.0, 30)
        self.assertEqual(state['flags'] & 0x21, 0x21)
        m.trigger(state, 5, 0, 0, 1.0, 30)
        self.assertEqual(state['flags'] & 0x21, 0)


class ScriptWaitTests(unittest.TestCase):
    def test_duration_out_and_deadline(self):
        self.assertEqual(m.duration_out(14333), 14033)
        self.assertEqual(m.duration_out(100), 300)
        self.assertEqual(m.wait4anim_deadline(5000, 14333, 1.0), 19033)
        self.assertEqual(m.wait4anim_deadline(5000, 14333, 2.0), 5000 + 7016)

    def test_waitanim_unsigned_wrap(self):
        self.assertEqual(m.waitanim_wait(14333, 1.0), 14033)
        self.assertEqual(m.waitanim_wait(400, 1.0), 300)
        self.assertEqual(m.waitanim_wait(299, 1.0), m.INT32_MAX)
        self.assertEqual(m.waitanim_wait(166, 1.0), m.INT32_MAX, 'WAITANIM saturates')

    def test_waitanim_ch_wraps_into_the_past(self):
        self.assertEqual(m.waitanim_ch_deadline(10000, 1000), 10700)
        self.assertEqual(m.waitanim_ch_deadline(10000, 166), 10000 - 134, 'a 166 ms clip: 134 ms in the past')
        self.assertFalse(m.wait4anim_blocks(m.waitanim_ch_deadline(10000, 166), 10016))
        self.assertEqual(m.waitanim_ch_deadline(16, 166), 16 + 2**32 - 134, 'no wrap below 134 ms')

    def test_triganimspeed(self):
        self.assertEqual(m.triganimspeed_speed(1.0, 4000), 4.0)
        self.assertEqual(m.triganimspeed_deadline(0, 14333, 4000), 3508)
        self.assertEqual(m.triganimspeed_deadline(0, 1300, -1000), -1000)

    def test_wait4anim_and_tokens(self):
        self.assertFalse(m.wait4anim_blocks(0, 10))
        self.assertTrue(m.wait4anim_blocks(100, 99))
        self.assertFalse(m.wait4anim_blocks(100, 100))
        self.assertEqual(m.loopanim_token(9, 1), 0x10009)
        self.assertNotEqual(m.loopanim_token(9, 1), m.loopanim_token(1, 9))
        self.assertEqual(m.getanim(0x14, 14), -1)
        self.assertEqual(m.getanim(0x1, 9), 9)
        self.assertEqual(m.getanim(0, m.NO_ANIMATION), 12)
        self.assertTrue(m.trigwaitanim_resumes(5, 5))
        self.assertFalse(m.trigwaitanim_resumes(6, 5))


class ChannelTests(unittest.TestCase):
    def test_highest_writing_channel_wins(self):
        self.assertEqual(m.node_winner([(1, True), (0, True)]), 1)
        self.assertEqual(m.node_winner([(2, False), (0, True)]), 0)
        self.assertIsNone(m.node_winner([(0, False)]))

    def test_idle_restart(self):
        self.assertEqual(m.idle_restart(0, True), (5, 0))
        self.assertIsNone(m.idle_restart(0x8000, True))
        self.assertIsNone(m.idle_restart(0x4, True))
        self.assertIsNone(m.idle_restart(0, False))


class AdvisorAndLoaderTests(unittest.TestCase):
    def test_advisor_clip_names(self):
        self.assertEqual(m.advisor_clip(13), 'Advisorm13.MD2')
        self.assertEqual(m.advisor_clip(1, '_FAN'), 'Advisor_FANm1.MD2')
        with self.assertRaises(ValueError):
            m.advisor_clip(0)

    def test_version_24_refused(self):
        self.assertFalse(any(m.loads(24, flags) for flags in (0, 2, 6)))
        self.assertTrue(m.loads(24, 1))
        self.assertTrue(m.loads(221, 0))
        self.assertFalse(m.loads(222, 1))


if __name__ == '__main__':
    unittest.main()
