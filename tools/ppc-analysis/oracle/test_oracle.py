"""Differential oracle tests: identified Mac routines vs the plans and vs OpenTPW's C# on this checkout.

Gated on OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT naming the private Feral `bin` directory (SHA-256
pinned); the C# comparisons additionally need a .NET SDK (OPENTPW_DOTNET or the project default).
Every comparison is pinned in EXPECTED: an agreement must stay an agreement, and a known mismatch
(a finding, docs/reverse/ORACLE.md §5) must keep its count and first counterexample until someone
fixes it and updates the table and ORACLE.md together. Gates only get stricter.
"""
from __future__ import annotations

import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import bridge as bridge_module  # noqa: E402
import targets  # noqa: E402
from differential import DEFAULT_COUNT, compare  # noqa: E402
from harness import Image, IdentityError, Sandbox, bin_root  # noqa: E402
from ppc32 import UnknownCall  # noqa: E402

ROOT = bin_root()
REPO = HERE.parents[2]
AGREE = 'agree'

# (target, side) -> AGREE, or (mismatches, first counterexample input). Seed 'oracle-v1', 500 random + boundaries.
EXPECTED = {
    ('det-world-lcg', 'plan'): AGREE,
    ('det-rse-rand', 'plan'): AGREE,
    # F1 (fixed): 0xdcb74 gives 100 when ride +46 & 0x8 (Info.HasQueue) is CLEAR and the QWTC formula when set.
    # F2 (fixed): the limit is a u32 (saturating at 2^32 - 1) and SPEED an unsigned word, in C# too.
    ('queue-limit', 'plan'): AGREE,
    ('queue-limit', 'csharp'): AGREE,
    # F3 (fixed): 0x8408c allows queue over path (old 1, new 3) while data:0x84b2c, the last-cell flag, is ZERO.
    ('path-can-change', 'plan'): AGREE,
    ('path-can-change', 'csharp'): AGREE,
    # F4 (fixed): balance − cost is a 32-bit difference; a negative cost is spent, not rejected with an exception.
    ('path-set-cell-type', 'plan'): AGREE,
    ('path-set-cell-type', 'csharp'): AGREE,
    ('board-unbounce', 'plan'): AGREE,
    # F5 (fixed): C# releases at deadline < now (strict) inside the 200 ms window of 0xae020, as BOARD-plan §5 states.
    ('board-unbounce', 'csharp'): AGREE,
    # OV2-6 (WALK-F2, REVIEW-ORACLE-2): BOUNCE fills the first free slot of the header's table; a full table stores nothing.
    ('board-bounce', 'plan'): AGREE,
    ('board-bounce', 'csharp'): AGREE,
    ('walk-speed-setter', 'plan'): AGREE,
    ('walk-speed-setter', 'csharp'): AGREE,
    ('walk-smoothing', 'csharp'): AGREE,
    # F6: 0xe6b28 uses fmadds (one rounding; 98 inputs differ in s) and stores boost·99 as u16 before dividing (wraps above 661).
    ('walk-smoothing', 'plan'): (99, {'s': 1.2833095788955688, 'base': 120, 'bonus': 0, 'boost': 662}),
    ('walk-smoothing', 'plan-fused'): (2, {'s': 1.2833095788955688, 'base': 120, 'bonus': 0, 'boost': 662}),
    # F7: the "stale f0" of 0x415b4 is Fz (sample+40) at the only call site 0x3ffa8, not 0.
    ('coast-rating-step', 'plan'): 440,
    ('coast-rating-step', 'plan-stale-fz'): AGREE,
    # F8: 0x41900 counts sections with BOTH flags 0x40 and 0x80 set, not sections without them.
    ('coast-rating-final', 'plan'): (280, {'S': -1.0, 'E': 23.463951110839844, 'peak_s': 0.5, 'peak_e': 23.37443733215332,
                                           'length_a': 47.13532638549805, 'length_b': 29.439252853393555,
                                           'max_y': 0.7295945286750793, 'max_speed': 20.78131103515625,
                                           'sections': [5678, 64, 10262, 128, 64]}),
    ('coast-rating-final', 'plan-both-flags'): AGREE,
    # F9: 0xcb4e8 uses the factor 0.8 while the 0x1083c8 thing's +48 word is positive, cuts to 500 − guests in limit
    # mode 1, and both cuts compare guests + group unsigned without a floor at 0 (GUESTS.md and C# have 1.2 / 1500 / ≥ 0).
    ('arrival-group', 'plan'): (125, {'world_mode': 0, 'guests': 0, 'points': 0, 'bonus': 20, 'per_visitor': 6,
                                      'min_people': 1, 'obj48': 1, 'limit_mode': 0}),
    ('arrival-group', 'routine-reading'): AGREE,
    ('arrival-group', 'csharp'): (125, {'world_mode': 0, 'guests': 0, 'points': 0, 'bonus': 20, 'per_visitor': 6,
                                        'min_people': 1, 'obj48': 1, 'limit_mode': 0}),
    # F11: executed over scripts of 0xcb2ac calls, a negative group (1700 counted guests: 1500 − 1700 = −200) ends at the
    # next stop exactly like 0: guests created, active byte and timer restart agree with the floored plan. The group word
    # keeps the negative value and the "group == 0" vehicle event (slot 74) is missing. The static reading (only exactly 0
    # ends a group, a negative group holds the state) is refuted.
    ('arrival-update', 'plan'): AGREE,
    ('arrival-update', 'routine-reading'): AGREE,
    ('arrival-update', 'static-reading'): (66, {'world_mode': 0, 'guests': 0, 'points': 0, 'bonus': 20, 'per_visitor': 6,
                                                'min_people': 1, 'obj48': 0, 'limit_mode': 0, 'preset': [-5, 1],
                                                'interval': 0, 'timer0': 0, 'steps': [[8, 2, 1], [12, 2, 1], [16, 2, 0]]}),
    ('arrival-update', 'reading-held'): (210, {'world_mode': 0, 'guests': 1700, 'points': 552, 'bonus': 20, 'per_visitor': 6,
                                               'min_people': 2, 'obj48': 1, 'limit_mode': 0, 'preset': None, 'interval': 2,
                                               'timer0': 0, 'steps': [[40, 2, 0], [44, 2, 0], [48, 2, 1], [400, 2, 1],
                                                                      [404, 2, 0], [900, 2, 0]]}),
    # F10: 0xc24c0 counts only things passing 0xdc834; a type with +0x9C == 0 is aged from its first purchase and divided
    # by its count, any other type is aged per thing and every copy counts in full (GUESTS.md and C# age every thing
    # from its own build and always divide).
    ('park-points', 'plan'): (214, {'now': 73, 'scale': 15000,
                                    'types': [{'id': 257, 'kind': 0, 'f9c': 0, 'value': 25, 'decay': 2,
                                               'bonus': [30, 20, 10], 'first': 1}],
                                    'things': [{'type': 257, 'open': True, 'built': 1},
                                               {'type': 257, 'open': True, 'built': 73}]}),
    ('park-points', 'routine-reading'): AGREE,
}
CSHARP_PENDING = {'det-world-lcg', 'det-rse-rand', 'coast-rating-step',
                  'coast-rating-final', 'park-points', 'arrival-update'}


class Identity(unittest.TestCase):
    def test_unidentified_container_is_rejected(self):
        with self.assertRaises(IdentityError):
            Image(b'Joy!peffpwpc' + bytes(64))


class UndefinedResults(unittest.TestCase):
    def test_only_architecturally_undefined_results_become_a_value(self):
        """Undefined (divw by zero, INT_MIN / -1) compares as UNDEFINED; any other Unsupported aborts the run."""
        import dataclasses
        from ppc32 import Undefined, Unsupported

        class Reset:
            def reset(self):
                pass

        def raising(error):
            def binary(sandbox, x, log):
                raise error
            return dataclasses.replace(targets.LCG, binary=binary)
        self.assertEqual(targets.run_binary(raising(Undefined('divw by zero')), Reset(), 0), targets.UNDEFINED)
        with self.assertRaises(Unsupported):
            targets.run_binary(raising(Unsupported('opcode 31 xo 86')), Reset(), 0)


@unittest.skipUnless(ROOT, 'requires the identified Feral bin directory (OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT)')
class Oracle(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = Image.load(ROOT)
        cls.bridge = bridge_module.get()
        cls.results = {}
        for target in targets.TARGETS:
            for comparison in compare(target, cls.image, DEFAULT_COUNT, csharp=cls.bridge is not None):
                cls.results[(target.key, comparison.side)] = comparison

    def check(self, key):
        comparison = self.results[key]
        expected = EXPECTED[key]
        if comparison.skipped:
            self.skipTest(f'{key}: {comparison.skipped}')
        self.assertGreaterEqual(comparison.inputs, DEFAULT_COUNT, key)
        if expected == AGREE:
            self.assertEqual(comparison.mismatches, 0, f'{key}: first counterexample {comparison.first}')
        elif isinstance(expected, int):
            self.assertEqual(comparison.mismatches, expected, key)
        else:
            count, first = expected
            self.assertEqual((comparison.mismatches, comparison.first['input']), (count, first), key)

    def test_every_comparison_is_pinned(self):
        csharp_sides = {key for key in self.results if key[1].startswith('csharp')}
        pending = {key for key in csharp_sides if key[0] in CSHARP_PENDING}
        self.assertEqual(set(self.results) - pending, set(EXPECTED))
        for key in pending:
            self.assertIsNotNone(self.results[key].skipped, f'{key}: C# landed; pin it in EXPECTED and ORACLE.md')

    def test_det(self):
        self.check(('det-world-lcg', 'plan'))
        self.check(('det-rse-rand', 'plan'))

    def test_queue_limit(self):
        self.check(('queue-limit', 'plan'))

    def test_queue_limit_csharp(self):
        self.check(('queue-limit', 'csharp'))

    def test_path(self):
        for key in (('path-can-change', 'plan'), ('path-set-cell-type', 'plan')):
            self.check(key)

    def test_path_csharp(self):
        for key in (('path-can-change', 'csharp'), ('path-set-cell-type', 'csharp')):
            self.check(key)

    def test_board(self):
        for key in (('board-unbounce', 'plan'), ('board-bounce', 'plan')):
            self.check(key)

    def test_board_csharp(self):
        for key in (('board-unbounce', 'csharp'), ('board-bounce', 'csharp')):
            self.check(key)

    def test_walk(self):
        for key in (('walk-speed-setter', 'plan'), ('walk-smoothing', 'plan'), ('walk-smoothing', 'plan-fused')):
            self.check(key)

    def test_walk_csharp(self):
        # WALK-F wired GuestSpeed.Caps / GuestSpeed.Update into the bridge (WALK-plan section 15 left them for after the merge).
        for key in (('walk-speed-setter', 'csharp'), ('walk-smoothing', 'csharp')):
            self.check(key)

    def test_coast(self):
        for key in (('coast-rating-step', 'plan'), ('coast-rating-step', 'plan-stale-fz'),
                    ('coast-rating-final', 'plan'), ('coast-rating-final', 'plan-both-flags')):
            self.check(key)

    def test_arrival(self):
        for key in (('arrival-group', 'plan'), ('arrival-group', 'routine-reading')):
            self.check(key)

    def test_arrival_csharp(self):
        self.check(('arrival-group', 'csharp'))

    def test_arrival_reading_is_sharp(self):
        # Each part of the routine reading matters: dropping the 0.8 factor, the 500 cut or the unsigned/no-floor cut
        # turns the agreement into mismatches.
        import dataclasses
        variants = {
            'factor': lambda x: targets.arrival_full({**x, 'obj48': 0}),
            'mode-500': lambda x: targets.arrival_full({**x, 'limit_mode': 0}),
            'floor': lambda x: max(0, targets.arrival_full(x)),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.ARRIVAL, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_arrival_update(self):
        for key in (('arrival-update', 'plan'), ('arrival-update', 'routine-reading'),
                    ('arrival-update', 'static-reading'), ('arrival-update', 'reading-held')):
            self.check(key)

    def test_arrival_update_reading_is_sharp(self):
        # Each part of the reading matters: the group word kept negative, the missing slot-74 event, the stop gating
        # (phase 2) and the interval test each bring mismatches back when dropped. The plan keeps only the
        # behaviour, so a floored group changes nothing there (the finding), but it does change the full reading.
        import dataclasses
        strip74 = lambda r: {**r, 'events': [[e for e in ev if e[0] != 74] for ev in r['events']]}
        variants = {
            'floor': targets.arrival_update_floored,
            'hold': targets.arrival_update_reading_held,
            'slot-74': lambda x: strip74(targets.arrival_update_reading(x)),
            'phase-gate': lambda x: targets.arrival_update_reading(
                {**x, 'steps': [(now, 2, flag) for now, _, flag in x['steps']]}),
            'interval-off-by-one': lambda x: targets.arrival_update_reading({**x, 'interval': x['interval'] + 1}),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.ARRIVAL_UPDATE, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)
        # The negative group itself: the floored plan's behaviour agrees even where the group started below 0.
        [original] = [targets.run_binary(targets.ARRIVAL_UPDATE, targets.ARRIVAL_UPDATE.sandbox(self.image), x)
                      for x in [{'world_mode': 0, 'guests': 1700, 'points': 0, 'bonus': 20, 'per_visitor': 6,
                                 'min_people': 1, 'obj48': 0, 'limit_mode': 0, 'preset': None, 'interval': 2, 'timer0': 0,
                                 'steps': [(40, -1, 0), (44, -1, 0), (48, 2, 0)]}]]
        self.assertEqual(original['groups'], [-200, -200, -200])
        self.assertEqual(original['calls'], [(1, 0, 0), (1, 0, 0), (0, 48, 0)])
        self.assertEqual(original['events'], [[(2000, -200), (82, 1)], [(82, 1)], [(80, 0)]])

    def test_points(self):
        for key in (('park-points', 'plan'), ('park-points', 'routine-reading')):
            self.check(key)

    def test_points_reading_is_sharp(self):
        # Each part of the routine reading matters: the 0xdc834 test, the +0x9C branch either way and the park record
        # age (+28, not the earliest copy) each bring mismatches back when dropped.
        import dataclasses

        def earliest(x, kind):
            return min([t['built'] for t in x['things'] if t['type'] == kind['id']] or [x['now']])
        variants = {
            'open-test': lambda x: targets.points_full({**x, 'things': [{**t, 'open': True} for t in x['things']]}),
            'f9c-zero': lambda x: targets.points_full({**x, 'types': [{**k, 'f9c': 0} for k in x['types']]}),
            'f9c-set': lambda x: targets.points_full({**x, 'types': [{**k, 'f9c': 1} for k in x['types']]}),
            'record-age': lambda x: targets.points_full(
                {**x, 'types': [{**k, 'first': earliest(x, k)} for k in x['types']]}),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.POINTS, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_unstubbed_call_aborts_the_run(self):
        sandbox = Sandbox(self.image, targets.QUEUE.allowed, {})
        sandbox.reset()
        ride = sandbox.struct(128, {46: ('H', 8)})
        with self.assertRaises(UnknownCall):
            sandbox.call(0xDCB74, {3: ride})

    def test_unstubbed_import_aborts_the_run(self):
        sandbox = Sandbox(self.image, targets.RAND.allowed, {})
        sandbox.reset()
        world = sandbox.alloc(targets.WORLD_RNG + 8)
        holder = sandbox.struct(4, {0: ('I', world)})
        with self.assertRaises(UnknownCall):
            sandbox.call(0xB07F8, {3: 10, 27: holder}, stop=0xB0828)

    def test_mutated_reference_is_caught(self):
        """A one-operand change in a reference (the LCG increment) must turn an agreement into a mismatch."""
        import det_model

        def mutated(state):
            new = (state * 1664525 + 1013904224) & 0xFFFFFFFF
            return det_model.wrapping_abs32(new), new
        comparison = compare(targets.LCG, self.image, 50, csharp=False, plan=mutated)[0]
        self.assertGreater(comparison.mismatches, 0)


@unittest.skipUnless(ROOT and bridge_module.dotnet(), 'requires the Feral bin directory and a .NET 10 SDK')
class CSharpMutation(unittest.TestCase):
    """Flipping one operand in a C# implementation on a scratch copy makes its oracle comparison fail."""

    MUTATIONS = [
        ('queue-limit', 'csharp', 'source/OpenTPW/World/Guests/RideVisitorBridge.cs',
         'quotient = MathF.Max( quotient, 4f );', 'quotient = MathF.Max( quotient, 5f );'),
        ('path-can-change', 'csharp', 'source/OpenTPW/World/ParkCellMap.cs',
         'if ( type == 21 && old == 21 )', 'if ( type == 21 && old == 20 )'),
        ('path-can-change', 'csharp', 'source/OpenTPW/World/ParkCellMap.cs',
         'return type == 3 && old == 1 && !lastCell;', 'return type == 3 && old == 1 && lastCell;'),
        ('path-set-cell-type', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'unchecked( (int)Balance - (int)CellCost( kind ) ) >= 0', 'Balance - CellCost( kind ) >= 0'),
        ('board-unbounce', 'csharp', 'source/OpenTPW/World/Guests/RideVisitorBridge.cs',
         'elapsed % 1000 / 200 == 0', 'elapsed % 1000 / 250 == 0'),
        ('board-bounce', 'csharp', 'source/OpenTPW/World/Guests/RideVisitorBridge.cs',
         'if ( slots[index].Guest == 0 )', 'if ( false )'),
        ('walk-smoothing', 'csharp', 'source/OpenTPW/World/Guests/GuestNavigation.cs',
         'MathF.FusedMultiplyAdd( 3f, previous, (baseSpeed + bonus + boost) / (float)Divisor ) * 0.25f',
         '(3f * previous + (baseSpeed + bonus + boost) / (float)Divisor) * 0.25f'),
        ('walk-speed-setter', 'csharp', 'source/OpenTPW/World/Guests/GuestNavigation.cs',
         'public const int Floor = 655;', 'public const int Floor = 654;'),
    ]

    def test_mutations_are_detected(self):
        image = Image.load(ROOT)
        by_key = {target.key: target for target in targets.TARGETS}
        with tempfile.TemporaryDirectory(prefix='oracle-mutation-') as scratch:
            scratch = Path(scratch)
            ignore = shutil.ignore_patterns('bin', 'obj')
            shutil.copytree(REPO / 'source', scratch / 'source', ignore=ignore)
            shutil.copytree(HERE / 'dotnet', scratch / 'tools' / 'ppc-analysis' / 'oracle' / 'dotnet', ignore=ignore)
            shutil.copy2(REPO / 'global.json', scratch / 'global.json')
            for index, (key, side, path, before, after) in enumerate(self.MUTATIONS):
                with self.subTest(target=key, mutation=after):
                    file = scratch / path
                    original = file.read_text(encoding='utf-8')
                    self.assertEqual(original.count(before), 1, f'mutation anchor in {path}')
                    file.write_text(original.replace(before, after), encoding='utf-8')
                    try:
                        mutated = bridge_module.Bridge(bridge_module.dotnet(), scratch, scratch / f'out-{index}')
                        results = {c.side: c for c in compare(by_key[key], image, DEFAULT_COUNT, bridge=mutated)}
                        baseline = EXPECTED[(key, side)]
                        baseline_count = 0 if baseline == AGREE else baseline[0]
                        self.assertGreater(results[side].mismatches, baseline_count,
                                           f'{key} {side}: mutation "{after}" went unnoticed')
                    finally:
                        file.write_text(original, encoding='utf-8')


if __name__ == '__main__':
    unittest.main()
