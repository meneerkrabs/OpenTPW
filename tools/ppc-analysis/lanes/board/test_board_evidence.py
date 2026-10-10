"""Tests for the bounded boarding witnesses (BOARD-R), with optional identified local assets."""
import os
from pathlib import Path
import struct
from types import SimpleNamespace
import unittest

import board_evidence as b

NAMES = ['VAR_LETMEON', 'VAR_LETMEOFF', 'VAR_CAPACITY', 'VAR_DURATION', 'VAR_ONRIDE', 'VAR_RIDECLOSED',
         'VAR_TEMP']
OPS = {name: number for number, name in b.OPCODES.items()}
OPS['NOP'] = 0

# The Belly Bounce loop shape (docs/reverse/BOARD-plan.md section 3), with a closed-ride branch.
LOOP = [
    ('top', 'BOUNCING', ['VAR_ONRIDE']), (None, 'JSR', ['@sub']), ('wait', 'WAIT', [500]),
    (None, 'TEST', ['VAR_RIDECLOSED']), (None, 'BRANCH_NZ', ['@closed']), (None, 'BOUNCING', ['VAR_TEMP']),
    (None, 'CMP', ['VAR_CAPACITY', 'VAR_TEMP']), (None, 'BRANCH_PV', ['@admit']), (None, 'BRANCH', ['@unlock']),
    ('admit', 'CRIT_LOCK', []), (None, 'TEST', ['VAR_LETMEON']), (None, 'BRANCH_Z', ['@unlock']),
    (None, 'BOUNCE', ['VAR_LETMEON', 'VAR_DURATION']), (None, 'COPY', ['VAR_LETMEON', 0]),
    ('unlock', 'CRIT_UNLOCK', []), (None, 'TEST', ['VAR_LETMEOFF']), (None, 'BRANCH_NZ', ['@top']),
    (None, 'UNBOUNCE', ['VAR_LETMEOFF']), (None, 'BRANCH', ['@top']),
    ('closed', 'ENDSLICE', []), (None, 'BRANCH', ['@closed']), ('sub', 'RETURN', []),
]


def assemble(listing, time_slice=50):
    labels, at = {}, 0
    for label, _, operands in listing:
        if label:
            labels[label] = at
        at += 1 + len(operands)
    words = []
    for _, name, operands in listing:
        words.append(0x80000000 | OPS[name])
        for operand in operands:
            if isinstance(operand, int):
                words.append(operand & 0xffff)
            elif operand.startswith('@'):
                words.append(0x20000000 | labels[operand[1:]])
            else:
                words.append(0x40000000 | NAMES.index(operand))
    raw = b'RSSEQ\x0f\x01\x00' + struct.pack('<I5i', len(NAMES), 3, time_slice, 0, 10, 0) + b'Pad Pad Pad Pad '
    raw += struct.pack(f'<I{len(words)}I', len(words), *words) + struct.pack('<I', 0)
    for name in NAMES:
        raw += struct.pack('<I', len(name) + 1) + name.encode() + b'\0'
    return raw


def d_word(op, rd, ra, imm):
    return (op << 26) | (rd << 21) | (ra << 16) | (imm & 0xffff)


def synthetic_fields_container():
    code = bytearray(0x110000)
    for at, op, (rd, ra, imm), _ in b.FIELDS:
        struct.pack_into('>I', code, at, d_word(op, rd, ra, imm))
    for at, source, target, shift in b.SRAWI:
        struct.pack_into('>I', code, at, (31 << 26) | (source << 21) | (target << 16) | (shift << 11) | (824 << 1))
    for at, source, target, shift, begin, end in b.CLRLWI:
        struct.pack_into('>I', code, at, (21 << 26) | (source << 21) | (target << 16) | (shift << 11)
                         | (begin << 6) | (end << 1))
    return SimpleNamespace(code=SimpleNamespace(data=code))


class ScriptLoopTests(unittest.TestCase):
    def test_bounce_loop_slices(self):
        loop = b.bounce_loop(b.read_rse(assemble(LOOP)))
        self.assertEqual(loop['wait_ms'], 500)
        self.assertEqual(loop['bounce_operands'], ['VAR_LETMEON', 'VAR_DURATION'])
        self.assertEqual((loop['admission_paths'], loop['release_paths']), (3, 2))
        self.assertLessEqual(loop['longest_slice'], 50)

    def test_animation_wait_in_the_loop_is_untraced(self):
        listing = LOOP[:1] + [(None, 'WAITANIM', [0, 0])] + LOOP[1:]
        with self.assertRaises(ValueError):
            b.bounce_loop(b.read_rse(assemble(listing)))

    def test_crit_unlock_must_end_the_admission_slice(self):
        listing = [(label, 'NOP' if name == 'CRIT_UNLOCK' else name, operands) for label, name, operands in LOOP]
        with self.assertRaises((ValueError, b.pef.PEFError)):
            b.bounce_loop(b.read_rse(assemble(listing)))

    def test_budget_is_enforced(self):
        with self.assertRaises(b.pef.PEFError):
            b.bounce_loop(b.read_rse(assemble(LOOP, time_slice=5)))

    def test_closed_ride_branch_is_excluded(self):
        # The ENDSLICE loop behind TEST VAR_RIDECLOSED would otherwise end the release slice.
        loop = b.bounce_loop(b.read_rse(assemble(LOOP)))
        self.assertEqual(loop['release_paths'], 2)


class TimingTests(unittest.TestCase):
    def test_loop_period_from_the_wait_operand(self):
        self.assertEqual(b.wait_resume_slices(500), 3)   # 3 x 248 = 744 >= 500 > 496
        self.assertEqual(b.loop_period(500), 4)
        self.assertEqual(b.loop_period(496), 3)
        self.assertEqual(b.wait_resume_slices(500, speed_bias=100), 2)  # trunc(500 / 1.5) = 333

    def test_nominal_hold(self):
        self.assertEqual(b.nominal_hold_turns(30, 4), 121)        # 30.008 s
        self.assertEqual(b.nominal_hold_turns(10, 4), 41)
        for duration in range(10, 31):
            self.assertEqual(b.nominal_hold_turns(duration, 4), 4 * duration + 1)
        for duration in range(31, 61):                             # (248 - 8j) mod 1000 leaves [0, 200)
            self.assertEqual(b.nominal_hold_turns(duration, 4), 529)

    def test_latency_terms(self):
        self.assertEqual(b.boarding_latency_turns(4, 0, 0), 21)
        self.assertEqual(b.boarding_latency_turns(4, 2, 6), 29)
        self.assertEqual(dict((name, turns) for name, turns, _ in b.LATENCY_TERMS)['interlude'], 11)
        self.assertEqual(b.queue.move_delay(b.MOVE_UP_GAP), 2)

    def test_wait_bound_and_tau(self):
        self.assertEqual(b.wait_bound_turns(0, 5, 121, 29), 151)
        self.assertEqual(b.wait_bound_turns(99, 5, 121, 29), 5321)
        self.assertAlmostEqual(b.tau_max_turns(5, 121, 29, 30), 142.0)
        self.assertAlmostEqual(b.tau_max_turns(5, 121, 21, 30), 102.0)
        with self.assertRaises(ValueError):
            b.wait_bound_turns(1, 0, 121, 29)

    def test_implementation_walk_turns(self):
        self.assertEqual(b.implementation_walk_turns((141 - 114) / 255 / 2, 1.0), 2)
        self.assertEqual(b.implementation_walk_turns(((191 / 255) ** 2 + (27 / 255) ** 2) ** 0.5, 1.0), 6)


class SimulationTests(unittest.TestCase):
    def runs(self):
        for seed in range(8):
            for script_first in (True, False):
                walk_stand, walk_up = 1 + seed % 3, 1 + seed % 2
                waits = b.simulate(seed, walk_stand=walk_stand, walk_up=walk_up, script_first=script_first,
                                   ride_first=bool(seed & 1), phase=seed % 4, turns=1200,
                                   interlude_chance=(1.0, 0.3)[seed % 2], join_gap=((0, 1), (0, 3), (4, 30))[seed % 3])
                yield walk_stand, walk_up, waits

    def test_simulated_waits_within_bound(self):
        boardings, ratio = 0, 0.0
        for walk_stand, walk_up, waits in self.runs():
            latency = b.boarding_latency_turns(4, walk_stand, walk_up)
            for position, wait in waits:
                bound = b.wait_bound_turns(position, 5, 121, latency)
                self.assertLessEqual(wait, bound, (position, wait))
                ratio = max(ratio, wait / bound)
                boardings += 1
        self.assertGreater(boardings, 200)
        self.assertGreater(ratio, 0.4)  # not vacuous

    def test_bound_without_the_hold_is_violated(self):
        violated = any(wait > b.wait_bound_turns(position, 5, 0, b.boarding_latency_turns(4, s, u))
                       for s, u, waits in self.runs() for position, wait in waits)
        self.assertTrue(violated)


class DecoderTests(unittest.TestCase):
    def test_synthetic_fields_pass_and_each_mutation_fails(self):
        c = synthetic_fields_container()
        self.assertEqual(b.check_fields(c), len(b.FIELDS) + len(b.SRAWI) + len(b.CLRLWI))
        sites = [b.FIELDS[0][0], b.FIELDS[len(b.FIELDS) // 2][0], b.FIELDS[-1][0], b.SRAWI[0][0], b.CLRLWI[0][0]]
        for at in sites:
            mutated = synthetic_fields_container()
            word = struct.unpack_from('>I', mutated.code.data, at)[0]
            struct.pack_into('>I', mutated.code.data, at, word ^ (1 << 11))
            with self.assertRaises(b.pef.PEFError):
                b.check_fields(mutated)

    def test_key_operands_are_pinned(self):
        pinned = {(at, op): fields for at, op, fields, _ in b.FIELDS}
        self.assertEqual(pinned[(0xae050, 14)], (25, 4, -0x7ae1))   # /200 magic low half
        self.assertEqual(pinned[(0xb0698, 14)], (3, 3, -2))         # WAIT rewind
        self.assertEqual(pinned[(0xaf5e8, 36)], (0, 31, 152))       # CRIT_UNLOCK clears the budget
        self.assertEqual(pinned[(0xe05ec, 40)], (0, 31, 56))        # state 14 compares with the head
        self.assertEqual(b.STATE_HANDLERS[14], 0xef4f8)


@unittest.skipUnless(os.environ.get('OPENTPW_PPC_BIN_ROOT'), 'requires local identified PEF')
class IdentifiedBinaryTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(os.environ['OPENTPW_PPC_BIN_ROOT'])

    def test_identified_original_witness(self):
        report = b.inspect(self.root)
        self.assertEqual(report['field_witnesses'], len(b.FIELDS) + len(b.SRAWI) + len(b.CLRLWI))
        self.assertEqual(report['call_witnesses'], len(b.CALLS))
        self.assertEqual(report['tables']['states']['13'], '0xef4ec')
        self.assertEqual(report['witness_source'], 'tools/ppc-analysis/lanes/board/board_evidence.py')

    def _mutated(self, section, offset, value):
        c = b.load_identified(self.root / 'SimThemePark.data')
        data = bytearray(getattr(c, section).data)
        struct.pack_into('>I', data, offset, value)
        getattr(c, section).data = data
        return c

    def test_wrong_release_window_divisor_is_rejected(self):
        c = self._mutated('code', 0xae050, d_word(14, 25, 4, -0x7ae0))
        with self.assertRaises(b.pef.PEFError):
            b.check_fields(c)

    def test_wrong_wait_rewind_is_rejected(self):
        c = self._mutated('code', 0xb0698, d_word(14, 3, 3, -1))
        with self.assertRaises(b.pef.PEFError):
            b.check_fields(c)

    def test_wrong_consumption_offset_is_rejected(self):
        c = self._mutated('code', 0xe05ec, d_word(40, 0, 31, 54))
        with self.assertRaises(b.pef.PEFError):
            b.check_fields(c)

    def test_wrong_state_14_handler_is_rejected(self):
        c = self._mutated('data_section', b.STATE_TABLE + 4 * 14, 0xef4ec)
        with self.assertRaises(b.pef.PEFError):
            b.check_tables(c)


@unittest.skipUnless(os.environ.get('OPENTPW_PC_DATA'), 'requires local PC Data directory')
class PcDataTests(unittest.TestCase):
    def test_bounce_rides(self):
        data = Path(os.environ['OPENTPW_PC_DATA'])
        rides = {ride['theme']: ride for ride in (b.bounce_ride(data, *row) for row in b.BOUNCE_RIDES)}
        for ride in rides.values():
            self.assertEqual((ride['loop']['wait_ms'], ride['period_turns'], ride['runs_continuously']), (500, 4, 1))
            self.assertEqual(ride['loop']['bounce_operands'], ['VAR_LETMEON', 'VAR_DURATION'])
            self.assertEqual(ride['time_slice'], 50)
        belly = rides['jungle']
        self.assertEqual(belly['script_sha256'], '7f32699cb6c511d8a9d80dd3250ae89f0b3991b2645fdfccf5cc13a68f677a35')
        self.assertEqual((belly['id'], belly['capacity'], belly['duration'], belly['hold_turns']), (1100, 5, 30, 121))
        self.assertEqual((belly['loop']['wait_index'], belly['loop']['unlock_index']), (46, 99))
        self.assertEqual(b.wait_bound_turns(99, belly['capacity'], belly['hold_turns'],
                                            b.boarding_latency_turns(belly['period_turns'], 2, 6)), 5321)
        self.assertEqual({theme: ride['hold_turns'] for theme, ride in rides.items()},
                         {'jungle': 121, 'space': 121, 'fantasy': 41, 'hallow': 41})


if __name__ == '__main__':
    unittest.main()
