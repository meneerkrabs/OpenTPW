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
    # F1: 0xdcbac takes the constant 100 when ride +46 & 0x8 is CLEAR; QUEUE-plan and C# say when set.
    ('queue-limit', 'plan'): (515, {'flag8': 0, 'qwtc_bits': 1097358578, 'speed': 0, 'init_speed': 305, 'capacity': 249,
                                    'duration': 252, 'level': 3, 'count': 99}),
    ('queue-limit', 'plan-flag-clear'): AGREE,
    ('queue-limit', 'csharp'): (515, {'flag8': 0, 'qwtc_bits': 1097358578, 'speed': 0, 'init_speed': 305, 'capacity': 249,
                                      'duration': 252, 'level': 3, 'count': 99}),
    # F2: with the polarity swapped the C# formula agrees except where the quotient reaches 2^31 (C# int.MaxValue,
    # original up to 2^32 - 1: DUR 0, InitSpeed 0, huge QWTC) and SPEED >= 2^31 (signed in C#).
    ('queue-limit', 'csharp-flag-clear'): (6, {'flag8': 1, 'qwtc_bits': 1315859240, 'speed': 0, 'init_speed': 161,
                                               'capacity': 255, 'duration': 1, 'level': 1, 'count': 0}),
    # F3: 0x8408c allows queue over path (old 1, new 3) when data:0x84b2c is ZERO and refuses when nonzero.
    ('path-can-change', 'plan'): (2, [1, 3, 0]),
    ('path-can-change', 'csharp'): (2, [1, 3, 0]),
    # F4 (boundary): balance − cost wraps in 32 bits in the original; C# rejects a negative cost with an exception.
    ('path-set-cell-type', 'plan'): (2, {'old': 0, 'type': 1, 'last_flag': 0, 'balance': -2147483648, 'path_cost': 1,
                                         'queue_cost': 75}),
    ('path-set-cell-type', 'csharp'): (2, {'old': 0, 'type': 1, 'last_flag': 0, 'balance': -2147483648, 'path_cost': 1,
                                           'queue_cost': 75}),
    ('board-unbounce', 'plan'): AGREE,
    # F5: C# releases at deadline <= now without the 200 ms window of 0xae020 (BOARD-plan §5 not implemented on main).
    ('board-unbounce', 'csharp'): (326, {'now': 31000, 'slots': [[7, 31000, 1000]]}),
    ('walk-speed-setter', 'plan'): AGREE,
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
}
CSHARP_PENDING = {'det-world-lcg', 'det-rse-rand', 'walk-speed-setter', 'walk-smoothing', 'coast-rating-step',
                  'coast-rating-final'}


class Identity(unittest.TestCase):
    def test_unidentified_container_is_rejected(self):
        with self.assertRaises(IdentityError):
            Image(b'Joy!peffpwpc' + bytes(64))


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
        for side in ('plan', 'plan-flag-clear'):
            self.check(('queue-limit', side))

    def test_queue_limit_csharp(self):
        for side in ('csharp', 'csharp-flag-clear'):
            self.check(('queue-limit', side))

    def test_path(self):
        for key in (('path-can-change', 'plan'), ('path-set-cell-type', 'plan')):
            self.check(key)

    def test_path_csharp(self):
        for key in (('path-can-change', 'csharp'), ('path-set-cell-type', 'csharp')):
            self.check(key)

    def test_board(self):
        self.check(('board-unbounce', 'plan'))

    def test_board_csharp(self):
        self.check(('board-unbounce', 'csharp'))

    def test_walk(self):
        for key in (('walk-speed-setter', 'plan'), ('walk-smoothing', 'plan'), ('walk-smoothing', 'plan-fused')):
            self.check(key)

    def test_coast(self):
        for key in (('coast-rating-step', 'plan'), ('coast-rating-step', 'plan-stale-fz'),
                    ('coast-rating-final', 'plan'), ('coast-rating-final', 'plan-both-flags')):
            self.check(key)

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


@unittest.skipUnless(ROOT and bridge_module.dotnet(), 'requires the Feral bin directory and a .NET SDK')
class CSharpMutation(unittest.TestCase):
    """Flipping one operand in a C# implementation on a scratch copy makes its oracle comparison fail."""

    MUTATIONS = [
        ('queue-limit', 'csharp-flag-clear', 'source/OpenTPW/World/Guests/RideVisitorBridge.cs',
         'quotient = MathF.Max( quotient, 4f );', 'quotient = MathF.Max( quotient, 5f );'),
        ('path-can-change', 'csharp', 'source/OpenTPW/World/ParkCellMap.cs',
         'if ( type == 21 && old == 21 )', 'if ( type == 21 && old == 20 )'),
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
            for key, side, path, before, after in self.MUTATIONS:
                with self.subTest(target=key):
                    file = scratch / path
                    original = file.read_text(encoding='utf-8')
                    self.assertEqual(original.count(before), 1, f'mutation anchor in {path}')
                    file.write_text(original.replace(before, after), encoding='utf-8')
                    try:
                        mutated = bridge_module.Bridge(bridge_module.dotnet(),
                                                       scratch / 'tools/ppc-analysis/oracle/dotnet/OracleBridge.csproj',
                                                       scratch / f'out-{key}')
                        results = {c.side: c for c in compare(by_key[key], image, DEFAULT_COUNT, bridge=mutated)}
                        baseline = EXPECTED[(key, side)]
                        baseline_count = 0 if baseline == AGREE else baseline[0]
                        self.assertGreater(results[side].mismatches, baseline_count,
                                           f'{key} {side}: mutation "{after}" went unnoticed')
                    finally:
                        file.write_text(original, encoding='utf-8')


if __name__ == '__main__':
    unittest.main()
