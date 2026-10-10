"""Tests for the bounded queue witnesses (QUEUE-R), with optional identified local assets."""
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import queue_evidence as q


def d_word(op, rd, ra, imm):
    return (op << 26) | (rd << 21) | (ra << 16) | (imm & 0xffff)


def synthetic_fields_container():
    code = bytearray(0x110000)
    for at, op, (rd, ra, imm), _ in q.FIELDS:
        struct.pack_into('>I', code, at, d_word(op, rd, ra, imm))
    return SimpleNamespace(code=SimpleNamespace(data=code))


class QueueLimitTests(unittest.TestCase):
    def test_has_queue_objects_get_the_fixed_limit_100(self):
        self.assertEqual(q.queue_limit(True, 130.0, 60, 60, 5, 30), 100)
        self.assertEqual(q.queue_limit(True, 0.0, 0, 0, 0, 0), 100)

    def test_wait_time_constant_formula_for_objects_without_queue(self):
        # 130 * (1.0 * 5) / 30 = 21.67 -> 21; SPEED 0 uses the factor 1.0.
        self.assertEqual(q.queue_limit(False, 130.0, 60, 60, 5, 30), 21)
        self.assertEqual(q.queue_limit(False, 130.0, 0, 60, 5, 30), 21)
        # Faster than InitSpeed lengthens the limit: 130 * (90/60 * 5) / 30 = 32.5 -> 32.
        self.assertEqual(q.queue_limit(False, 130.0, 90, 60, 5, 30), 32)

    def test_limit_floor_is_four(self):
        self.assertEqual(q.queue_limit(False, 3.0, 0, 60, 1, 10), 4)
        self.assertEqual(q.queue_limit(False, 0.0, 0, 60, 5, 30), 4)

    def test_zero_duration_quotient(self):
        self.assertEqual(q.queue_limit(False, 0.0, 0, 60, 5, 0), 4)  # NaN fails > 4.0
        self.assertEqual(q.queue_limit(False, 130.0, 0, 60, 5, 0), 0xffffffff)

    def test_join_needs_physical_room_and_limit(self):
        self.assertTrue(q.may_join(19, 5, 100))
        self.assertFalse(q.may_join(20, 5, 100))   # 4 positions per cell
        self.assertFalse(q.may_join(21, 30, 21))   # limit reached first
        self.assertEqual(q.max_queue_length(5, 100), 20)
        self.assertEqual(q.max_queue_length(30, 100), 100)
        self.assertEqual(q.max_queue_length(1, 21), 4)


class StandingQueueTests(unittest.TestCase):
    def test_move_delay_is_single_precision_1_2_times_position(self):
        self.assertEqual([q.move_delay(p) for p in (0, 1, 2, 4, 5, 10, 99)], [0, 1, 2, 4, 6, 12, 118])

    def test_delay_only_for_small_gaps(self):
        self.assertTrue(q.delays_move(5, 3, 6))
        self.assertFalse(q.delays_move(5, 2, 6))   # gap 3 moves at once
        self.assertFalse(q.delays_move(5, 3, 0))   # delay used up
        self.assertFalse(q.delays_move(3, 3, 6))   # already there

    def test_four_positions_per_cell_and_depth_bytes(self):
        self.assertEqual([q.queue_slot(p, 3) for p in range(9)],
                         [(0, 0), (0, 63), (0, 127), (0, 191), (1, 0), (1, 63), (1, 127), (1, 191), (2, 0)])
        # Past the last cell the walk ends on the terminator (index == cells); joins never get there.
        self.assertEqual(q.queue_slot(9, 2), (2, 63))
        self.assertEqual(q.queue_slot(7, 2), (1, 191))

    def test_lateral_offset_range(self):
        self.assertEqual(q.lateral_offset(0), 114)
        self.assertEqual(q.lateral_offset(27), 141)
        self.assertEqual(q.lateral_offset(28), 114)

    def test_boredom_formula_alone_can_fire(self):
        # Entry 190 turns ago, interlude 10 turns ago: both conditions hold.
        self.assertTrue(q.bored(1000, 810, 990))
        self.assertFalse(q.bored(1000, 900, 990))  # timeout not reached
        self.assertFalse(q.bored(1000, 810, 960))  # outside the 30-turn window

    def test_boredom_unreachable_while_entry_follows_interlude(self):
        # In state 11 the entry turn (+508) is never earlier than the interlude turn (+520).
        for counter in range(0, 260, 3):
            for interlude in range(0, counter + 1, 7):
                for entry in range(interlude, counter + 1, 5):
                    self.assertFalse(q.bored(counter, entry, interlude), (counter, entry, interlude))

    def test_simulated_state_11_never_bores(self):
        # Turn-by-turn model of 0xed58c with the state 8 interlude (11 turns) and state 11 re-entry.
        for happiness in (5, 15, 50, 90):
            counter, entry, interlude, state, left, waited = 1000, 1000, 0, 11, None, 0
            while waited < 5000 and left is None:
                counter += 1
                waited += 1
                if state == 8:
                    if counter > interlude + 10:
                        state, entry = 11, counter
                    continue
                if q.interlude_due(counter, interlude):
                    if happiness > 80 or 10 <= happiness < 20:
                        state, interlude = 8, counter
                    elif happiness < 10:
                        left = 'unhappy'
                elif q.bored(counter, entry, interlude):
                    left = 'bored'
            self.assertNotEqual(left, 'bored', happiness)
            self.assertEqual(left, 'unhappy' if happiness < 10 else None)


class BounceTests(unittest.TestCase):
    def test_bounce_holds_duration_seconds(self):
        self.assertEqual(q.bounce_deadline_ms(5000, 30), 35000)

    def test_unbounce_release_window(self):
        start = 5000
        deadline = q.bounce_deadline_ms(start, 30)
        self.assertFalse(q.unbounce_releases(deadline, start, deadline))       # strict deadline < now
        self.assertTrue(q.unbounce_releases(deadline + 1, start, deadline))
        self.assertTrue(q.unbounce_releases(deadline + 199, start, deadline))
        self.assertFalse(q.unbounce_releases(deadline + 200, start, deadline))
        self.assertTrue(q.unbounce_releases(deadline + 1000, start, deadline))

    def test_wait_bound(self):
        self.assertEqual(q.bounce_wait_bound(0, 5, 30), 31)
        self.assertEqual(q.bounce_wait_bound(19, 5, 30), 124)
        self.assertEqual(q.bounce_wait_bound(20, 5, 30), 155)
        self.assertEqual(q.bounce_wait_bound(19, 5, 30, boarding_s=2.5), 134)
        with self.assertRaises(ValueError):
            q.bounce_wait_bound(1, 0, 30)

    def test_parameter_clamp(self):
        self.assertEqual(q.clamp_parameter(30, 10, 60), 30)
        self.assertEqual(q.clamp_parameter(3, 10, 60), 10)
        self.assertEqual(q.clamp_parameter(70, 10, 60), 60)


class DecoderTests(unittest.TestCase):
    def test_synthetic_fields_pass_and_each_mutation_fails(self):
        c = synthetic_fields_container()
        self.assertEqual(q.check_fields(c), len(q.FIELDS))
        for index in (0, len(q.FIELDS) // 2, len(q.FIELDS) - 1):
            at = q.FIELDS[index][0]
            mutated = synthetic_fields_container()
            word = struct.unpack_from('>I', mutated.code.data, at)[0]
            struct.pack_into('>I', mutated.code.data, at, word ^ 1)
            with self.assertRaises(q.pef.PEFError):
                q.check_fields(mutated)

    def test_key_operands_are_pinned(self):
        pinned = {(at, op): fields for at, op, fields, _ in q.FIELDS}
        self.assertEqual(pinned[(0xed670, 14)], (0, 4, 100))
        self.assertEqual(pinned[(0xed5a0, 10)], (0, 0, 30))
        self.assertEqual(pinned[(0xdcbb0, 14)], (28, 0, 100))
        self.assertEqual(pinned[(0xdcc58, 48)], (31, 3, 436))
        self.assertEqual(pinned[(0xddd30, 14)], (21, 21, -4))
        self.assertEqual(pinned[(0xadfb4, 7)], (5, 28, 1000))

    def test_rlwinm_and_compare_decoders(self):
        c = SimpleNamespace(code=SimpleNamespace(data=struct.pack('>3I', 0x54000739, 0x7c050040, 0x408100c8)))
        self.assertEqual(q.rlwinm_fields(c, 0), (0, 0, 0, 28, 28))
        self.assertEqual(q.x_compare(c, 4), ('cmplw', 0, 5, 0))
        self.assertEqual(q.branch_conditional(c, 8), (4, 1, 8 + 200))
        with self.assertRaises(q.pef.PEFError):
            q.rlwinm_fields(c, 4)

    def test_sam_value_parser(self):
        values = q.sam_values('Info.Id\t1100\n# x\nUpgrades[0].InitDuration\t\t30\tcomment\nInfo.Name "A b"\n')
        self.assertEqual(values['Info.Id'], '1100')
        self.assertEqual(values['Upgrades[0].InitDuration'], '30')
        self.assertEqual(values['Info.Name'], '"A b"')


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
class IdentifiedBinaryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ['OPENTPW_PPC_BIN_ROOT'])

    def test_identified_original_witness(self):
        report = q.inspect(self.root)
        self.assertEqual(report['field_witnesses'], len(q.FIELDS))
        self.assertEqual(report['schema_offsets']['Upgrades.QueueWaitTimeConstant'], 436)
        self.assertEqual(report['schema_offsets']['Info.HasQueue'], 64)
        self.assertEqual(report['constants']['0x55a0'], 1.2000000476837158)
        self.assertEqual(report['witness_source'], 'tools/ppc-analysis/lanes/queue/queue_evidence.py')

    def _mutated(self, section, offset, value_fmt, value):
        c = q.load_identified(self.root / 'SimThemePark.data')
        data = bytearray(getattr(c, section).data)
        struct.pack_into(value_fmt, data, offset, value)
        getattr(c, section).data = data
        return c

    def test_wrong_boredom_timeout_operand_is_rejected(self):
        c = self._mutated('code', 0xed670, '>I', d_word(14, 0, 4, 101))
        with self.assertRaises(q.pef.PEFError):
            q.check_fields(c)

    def test_wrong_wait_constant_offset_is_rejected(self):
        c = self._mutated('code', 0xdcc58, '>I', d_word(48, 31, 3, 432))
        with self.assertRaises(q.pef.PEFError):
            q.check_fields(c)

    def test_wrong_limit_floor_constant_is_rejected(self):
        c = self._mutated('data_section', 0x54e8, '>f', 5.0)
        with self.assertRaises(q.pef.PEFError):
            q.check_code(c)


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'requires local PC Data directory')
class PcDataTests(unittest.TestCase):
    def test_belly_bounce_parameters_and_bound(self):
        bouncy = q.ride_parameters(Path(os.environ['OPENTPW_PC_DATA']), 'jungle', 'bouncy.wad', 'Bouncy.sam')
        self.assertEqual((bouncy['id'], bouncy['has_queue'], bouncy['duration_unit']), (1100, True, 1))
        self.assertEqual((bouncy['capacity'], bouncy['duration']), (5, 30))
        self.assertEqual(bouncy['queue_wait_time_constant'], 130.0)
        limit = q.queue_limit(bouncy['has_queue'], bouncy['queue_wait_time_constant'], bouncy['init_speed'],
                              bouncy['init_speed'], bouncy['capacity'], bouncy['duration'])
        self.assertEqual(limit, 100)
        self.assertEqual(q.bounce_wait_bound(q.max_queue_length(5, limit) - 1, bouncy['capacity'],
                                             bouncy['duration']), 124)


if __name__ == '__main__':
    unittest.main()
