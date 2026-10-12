"""Differential oracle tests: identified Mac routines vs the plans and vs OpenTPW's C# on this checkout.

Gated on OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT naming the private Feral `bin` directory (SHA-256
pinned); the C# comparisons additionally need a .NET SDK (OPENTPW_DOTNET or the project default).
Every comparison is pinned in EXPECTED: an agreement must stay an agreement, and a known mismatch
(a finding, docs/reverse/ORACLE.md §5) must keep its count and first counterexample until someone
fixes it and updates the table and ORACLE.md together. Gates only get stricter.
"""
from __future__ import annotations

import os
import random
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
from ppc32 import UnknownCall, s32  # noqa: E402

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
    # F9: 0xcb4e8 uses the factor 0.8 while the 0x1083c8 thing's +48 word is positive (the Traffic Lights' mTopLeft: GUESTS.md
    # and C# take that), cuts to 500 − guests in limit mode 1 (online; C#: OnlineVisit, ARRIVAL-002), and both cuts compare
    # guests + group unsigned without a floor at 0 (GUESTS.md and C# floor at 0, ARRIVAL-004). 49 of the 515 inputs remain:
    # 36 in limit mode 1 and 12 in the other modes where the group is cut below 0 (guests above the limit), plus 1 where a
    # negative group (MinPeople −5) wraps the unsigned compare.
    ('arrival-group', 'plan'): (49, {'world_mode': 0, 'guests': 600, 'points': 0, 'bonus': 20, 'per_visitor': 6,
                                     'min_people': 1, 'obj48': 0, 'limit_mode': 1}),
    ('arrival-group', 'routine-reading'): AGREE,
    ('arrival-group', 'csharp'): (49, {'world_mode': 0, 'guests': 600, 'points': 0, 'bonus': 20, 'per_visitor': 6,
                                       'min_people': 1, 'obj48': 0, 'limit_mode': 1}),
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
    # F10 (fixed, counts-test partly): a type whose +0x9C word (Bumper.WhichTrackType) is 0 is aged from its first purchase and
    # divided by its count, any other type is aged per copy and counts in full, and only things passing 0xdc834 count, in
    # GUESTS.md and C# too. The input's open flag is the chosen result of the counts-test; C# maps it to the object's open
    # state (thing +0x64), the one condition of the test that the inputs vary.
    ('park-points', 'plan'): AGREE,
    ('park-points', 'routine-reading'): AGREE,
    ('park-points', 'csharp'): AGREE,
    # ARRIVAL-003: 0xdc834 executed (the entrance cell, the queue count and the coaster controller stubbed) agrees with the
    # reading on the returned word and the stub call order. The part C# implements (Info.IsChoosable, not broken down or
    # condemned, open) differs wherever one of the other conditions fails.
    ('park-points-counts-test', 'plan'): AGREE,
    ('park-points-counts-test', 'implemented-subset'): (205, {'flags': 4, 'state': 0, 'open': 1, 'cell': 0, 'cells': 2,
                                                              'track': 0, 'thing40': 1, 'coaster': 1, 'count': 7}),
    # F12/F13 (fixed in C#): 0xcb7a8 stores monthly = trunc(P x pow(1 + APR/100, N/24) / N), not an annuity (3651 for 100000
    # at 20 % over 36 months), and 0xcc21c deducts exactly that for N months (no balance, interest or last-month remainder,
    # profit down by (M x N - P) / N). LoanMath.MonthlyRepayment / Pay and the plan now do the same.
    ('loan-offers', 'plan'): AGREE,
    ('loan-offers', 'routine-reading'): AGREE,
    ('loan-offers', 'csharp'): AGREE,
    ('loan-month', 'routine-reading'): AGREE,
    ('loan-term', 'plan'): AGREE,
    ('loan-term', 'routine-reading'): AGREE,
    ('loan-term', 'csharp'): AGREE,
    # F14: 0xcc904 credits the amount to the balance and the money-in word and leaves the year profit alone (ECON-005):
    # OpenTPW's TakeLoan already does the same.
    ('loan-take', 'plan'): AGREE,
    ('loan-take', 'routine-reading'): AGREE,
    ('loan-take', 'csharp'): AGREE,
    # F13 (early repayment, executed): 0xccc78 pays M x (N - months repaid) and charges the year profit the whole-term share
    # q x N; the year-profit word wraps at 32 bits. TryRepayLoan does the same.
    ('loan-payoff', 'plan'): AGREE,
    ('loan-payoff', 'routine-reading'): AGREE,
    ('loan-payoff', 'csharp'): AGREE,
    # F15 (fixed in C#): 0xdfe34 never reads the script. It is "relief object (ride +46 bit 0x1, type UsageInfo.ProvidesRelief) whose
    # gauge +64, truncated to a byte, is below 25". QUEUE-008's old reading, VAR_BROKEN != 0, differs wherever a broken ride is no
    # relief object (and the reverse); the plan now says the routine reading, and RideVisitorBridge.ReliefWornOut does the same.
    ('queue-failure-predicate', 'plan'): AGREE,
    ('queue-failure-predicate', 'plan-var-broken'): (310, {'flags': 0, 'gauge': 100.0, 'broken': 1}),
    ('queue-failure-predicate', 'routine-reading'): AGREE,
    ('queue-failure-predicate', 'csharp'): AGREE,
    # F15 (executed, fixed in C#): the ride update 0xe0a8c in the broken (1), closing (2) and condemned (4) states removes the head guest
    # from the queue and dismisses it, every park turn, unless the script already took it and it waits in state 14 (forced aboard).
    ('queue-ride-update', 'plan'): AGREE,
    # F16 (fixed in C#): 0xe9a84 is 100 while 0xdfe34 holds, else clamp(excitement byte - preferred byte, -50, 50) with the excitement
    # of 0xe02ec(ride, 0): sideshows 20 + trunc(0.8 * win * sqrt(clamp(goods - price, 0, 100)) / 10), coasters trunc(0.5 E + 50),
    # other types ExcitementLevel scaled by the speed and duration ratios (clamped to 0.75..1.25) and, with a thing behind +36,
    # 60 % of it plus up to 40. The old plan's static "ride minus preferred" differs on most inputs ("plan-static").
    # C# agrees except where a ride has a thing behind +36 (QUEUE-004: OpenTPW has none; test_queue_join_csharp_remainder).
    ('queue-join-difference', 'plan'): AGREE,
    ('queue-join-difference', 'plan-static'): (387, {'idx': 0, 'pref': 80, 'kind': 2, 'level': 30, 'track': 0, 'flags': 0, 'gauge': 100.0, 'rlevel': 0,
                                      'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'thing36': 0,
                                      'pieces': [0, 0, 0, 0], 'goods': 60, 'price': 35, 'win': 50, 'ctrl': 0,
                                      'coaster_e': 40.0}),
    ('queue-join-difference', 'routine-reading'): AGREE,
    ('queue-join-difference', 'csharp'): (46, {'idx': 0, 'pref': 80, 'kind': 0, 'level': 90, 'track': 0, 'flags': 0, 'gauge': 100.0, 'rlevel': 0,
                                      'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'thing36': 5,
                                      'pieces': [3, 2, 1, 7], 'goods': 60, 'price': 35, 'win': 50, 'ctrl': 0,
                                      'coaster_e': 40.0}),
    # F16: the gate applies when the low byte of the type's UsageInfo.ExcitementLevel is nonzero (0xe02ec(ride, 1)), not by kind.
    # The old plan and C# gated rides and sideshows on the static level: the outcome (pass, thought 12 or 15 with its message)
    # differed on 155 of 524 inputs ("plan-static"). C# follows the original now, except for the +36 thing as above.
    ('queue-join-gate', 'plan'): AGREE,
    ('queue-join-gate', 'plan-static'): (155, {'idx': 0, 'pref': 80, 'kind': 2, 'level': 30, 'track': 0, 'flags': 0, 'gauge': 100.0, 'rlevel': 0,
                                      'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'thing36': 0,
                                      'pieces': [0, 0, 0, 0], 'goods': 60, 'price': 35, 'win': 50, 'ctrl': 0,
                                      'coaster_e': 40.0}),
    ('queue-join-gate', 'routine-reading'): AGREE,
    ('queue-join-gate', 'csharp'): (61, {'idx': 0, 'pref': 80, 'kind': 0, 'level': 90, 'track': 0, 'flags': 0, 'gauge': 100.0, 'rlevel': 0,
                                      'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'thing36': 5,
                                      'pieces': [3, 2, 1, 7], 'goods': 60, 'price': 35, 'win': 50, 'ctrl': 0,
                                      'coaster_e': 40.0}),
    # F18 (ECON-020, C# unchanged): the sale routine 0xeaaf8 adds the shop type's five UsageInfo effects to the GUEST's need meters
    # (thirst +0x1a4 and hunger +0x1a8 subtracted, vomit +0x1b0, happiness +0x19c and litter +0x1b4 added), each in single precision and
    # clamped to 0..100. Litter is the raw LitterEffect (type +0x154) with no division by 100, but it fills a meter that the guest drops
    # at a byte value of 90 or more (guest-litter-trigger), so the claim "unscaled, up to 100x" of the static trace is a misreading of
    # the unit. The old rule (the park's store grows by LitterEffect, no floor and no ceiling) differs only where the meter clamps.
    ('shop-sale-litter', 'plan'): (213, {'meters': {'thirst': 40.0, 'hunger': 40.0, 'vomit': 10.0, 'happy': 50.0, 'litter': 60.0},
                                         'effects': [20, 30, 5, 10, 50]}),
    ('shop-sale-litter', 'routine-reading'): AGREE,
    ('shop-sale-litter', 'csharp'): (213, {'meters': {'thirst': 40.0, 'hunger': 40.0, 'vomit': 10.0, 'happy': 50.0, 'litter': 60.0},
                                           'effects': [20, 30, 5, 10, 50]}),
    ('guest-litter-trigger', 'routine-reading'): AGREE,
    # F17 (fixed in C#): 0xfc1c4 divides each role's budget by ALL its employees (the base counts behind TOC -19008 are zero in the
    # file), debits the whole share for every employee below the top grade (the top grade's share is not spent), gives
    # min(share / price, 100) points and carries the points above 99 into the next grade. The former rule (only trainees share the
    # budget, only the points bought are spent, cap at the grade boundary, reset to 0) is the pinned plan: 358 of 507 inputs differ.
    ('staff-training-split', 'plan'): (358, {'budgets': [1000, 1000, 0, 0, 0],
                                             'prices': [[5, 8, 12, 15, 1], [5, 8, 12, 15, 1], [5, 8, 12, 15, 1], [5, 8, 12, 15, 1],
                                                        [5, 8, 12, 15, 1]],
                                             'staff': [{'role': 0, 'class': 0, 'grade': 0, 'points': 0},
                                                       {'role': 0, 'class': 0, 'grade': 0, 'points': 0},
                                                       {'role': 0, 'class': 0, 'grade': 4, 'points': 0},
                                                       {'role': 1, 'class': 0, 'grade': 1, 'points': 0},
                                                       {'role': 1, 'class': 0, 'grade': 1, 'points': 0}], 'active': True}),
    ('staff-training-split', 'routine-reading'): AGREE,
    ('staff-training-split', 'csharp'): AGREE,
}
CSHARP_PENDING = {'det-world-lcg', 'det-rse-rand', 'coast-rating-step',
                  'coast-rating-final', 'arrival-update', 'park-points-counts-test', 'loan-month',
                  'queue-ride-update', 'guest-litter-trigger'}


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
        for key in (('park-points', 'plan'), ('park-points', 'routine-reading'), ('park-points', 'csharp')):
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

    def test_counts_test(self):
        for key in (('park-points-counts-test', 'plan'), ('park-points-counts-test', 'implemented-subset')):
            self.check(key)

    def test_counts_test_reading_is_sharp(self):
        # Every condition of the reading matters: dropping any one brings mismatches back.
        import dataclasses

        def without(**fixed):
            return lambda x: targets.counts_plan({**x, **fixed})
        variants = {
            'choosable': lambda x: targets.counts_plan({**x, 'flags': x['flags'] | 4}),
            'broken-down': lambda x: targets.counts_plan({**x, 'state': 0 if x['state'] == 1 else x['state']}),
            'condemned': lambda x: targets.counts_plan({**x, 'state': 0 if x['state'] == 4 else x['state']}),
            'call-order': lambda x: {**targets.counts_plan(x), 'calls': []},
            'open': without(open=1),
            'cell': without(cell=1),
            'queue-room': without(count=0, cells=1),
            'coaster': without(coaster=1),
            'kart-track': lambda x: targets.counts_plan({**x, 'thing40': 1 if x['track'] == 1 else x['thing40']}),
            'water-track': lambda x: targets.counts_plan({**x, 'thing40': 1 if x['track'] == 2 else x['thing40']}),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.COUNTS, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_training(self):
        for key in (('staff-training-split', 'plan'), ('staff-training-split', 'routine-reading')):
            self.check(key)

    def test_training_csharp(self):
        self.check(('staff-training-split', 'csharp'))

    def test_training_reading_is_sharp(self):
        # Every part of the reading matters: the divisor counting all employees (and no base count), the top grade left out, the
        # whole share debited, the cap of 100 points (not the points left to the grade) and the carry into the next grade.
        import dataclasses
        variants = {
            'divide-by-trainees': lambda x: targets.train_reading(x, count_all=False),
            'base-count-one': lambda x: targets.train_reading(x, base=1),
            'spend-what-was-bought': lambda x: targets.train_reading(x, spend_share=False),
            'no-carry': lambda x: targets.train_reading(x, carry=False),
            'cap-at-grade-boundary': lambda x: targets.train_reading(x, cap=False),
            'train-the-top-grade': lambda x: targets.train_reading(x, skip_top=False),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.TRAIN_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_training_example(self):
        # A budget of 1,000 for 3 handymen (grades 0, 0 and 4, 5 pounds per point) and for 2 mechanics (grade 1, 8 pounds per
        # point): the handymen's share is 333 (the top-grade one counts in the divisor, its share is not spent), the mechanics' 500.
        target = targets.TRAIN_TARGET
        sandbox = target.sandbox(self.image)
        [x] = [x for x in target.inputs(random.Random('example'), 0) if x['budgets'] == [1000, 1000, 0, 0, 0]]
        original = targets.run_binary(target, sandbox, x)
        self.assertEqual(original, {'staff': [[0, 66], [0, 66], [4, 0], [1, 62], [1, 62]], 'spent': 1666, 'booked': 1666})
        self.assertEqual(targets.train_current(x)['spent'], 1992)

    def test_training_inactive_bank(self):
        # With the bank's flag +276 clear the debit 0xcbfdc does nothing, but the training is booked (0xf3588) and the points are given.
        target = targets.TRAIN_TARGET
        sandbox = target.sandbox(self.image)
        [x] = [x for x in target.inputs(random.Random('example'), 0) if x['budgets'] == [600, 0, 0, 0, 0]]
        active = targets.run_binary(target, sandbox, x)
        self.assertEqual(active["spent"], 600)
        inactive = targets.run_binary(target, sandbox, {**x, 'active': False})
        self.assertEqual(inactive['staff'], active['staff'])
        self.assertEqual(inactive['spent'], 0)
        self.assertEqual(inactive['booked'], 600)

    def test_loans(self):
        for key in (('loan-offers', 'plan'), ('loan-offers', 'routine-reading'), ('loan-month', 'routine-reading'),
                    ('loan-term', 'plan'), ('loan-term', 'routine-reading'), ('loan-take', 'plan'),
                    ('loan-take', 'routine-reading'), ('loan-payoff', 'plan'), ('loan-payoff', 'routine-reading')):
            self.check(key)

    def test_loans_csharp(self):
        for key in (('loan-offers', 'csharp'), ('loan-term', 'csharp'), ('loan-take', 'csharp'), ('loan-payoff', 'csharp')):
            self.check(key)

    def test_loan_shipped_offers(self):
        """The eight shipped offers as the routine stores them (PPC-economy table); original PC saves (Lost Kingdom,
        Halloween World, Space Zone, Wonder Land, autosave) hold the same eight instalments."""
        target = targets.LOAN_OFFERS
        x = {'cash': 100000, 'offers': targets.SHIPPED_LOANS}
        original = targets.run_binary(target, target.sandbox(self.image), x)
        self.assertEqual(original['monthly'], targets.SHIPPED_MONTHLY)
        self.assertEqual(original['monthly'], [3651, 1825, 912, 365, 922, 1282, 2320, 2749])
        self.assertEqual([n * m for (_, _, n, _), m in zip(targets.SHIPPED_LOANS, original['monthly'])],
                         [131436, 65700, 32832, 13140, 22128, 38460, 111360, 82470])
        annuity = [targets.loan_annuity_plan(a, r, n) for a, r, n, _ in targets.SHIPPED_LOANS]
        self.assertEqual(annuity, [3716, 1858, 929, 371, 942, 1308, 2349, 2803])
        self.assertEqual(original['bank'], (100000, 0, 1, 0, 0, 0, 0))
        self.assertEqual([record[0] for record in original['records']], [0, 0, 0, 1, 0, 0, 0, 0])  # amount <= 10000

    def test_loan_zero_months_saturates(self):
        """No shipped offer has 0 months. The routine divides by 0: P > 0 gives +inf, saturated to 2^32 - 1 by
        0x1c3fbc; P = 0 gives NaN, which the same helper also saturates."""
        target = targets.LOAN_OFFERS
        offers = [(10000, 20, 0, 0), (0, 20, 0, 0)] + [(1, 0, 1, 0)] * 6
        original = targets.run_binary(target, target.sandbox(self.image), {'cash': 0, 'offers': offers})
        self.assertEqual(original['monthly'][:2], [0xFFFFFFFF, 0xFFFFFFFF])
        self.assertEqual(targets.loan_offers_reading({'cash': 0, 'offers': offers})['monthly'], original['monthly'])

    def test_loan_offers_reading_is_sharp(self):
        # Each part of the formula matters: the exponent (N/12 instead of N/24), the APR divisor (monthly rate), the
        # truncation and the 0.5 -> 1 base; the retired annuity differs too.
        import dataclasses
        variants = {
            'exponent-years': lambda x: targets.loan_offers_reading(x, exponent=1.0),
            'apr-per-month': lambda x: targets.loan_offers_reading(x, rate=1200.0),
            'annuity': lambda x: {**targets.loan_offers_reading(x), 'monthly': [
                targets.loan_annuity_plan(a, r, n) for a, r, n, _ in x['offers']]},
            'rounded': lambda x: {**targets.loan_offers_reading(x), 'monthly': [
                int(targets.loan_pow(1.0 + r / 100.0, (n / 12.0) * 0.5) * a / n + 0.5) for a, r, n, _ in x['offers']]},
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.LOAN_OFFERS, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_loan_month_reading_is_sharp(self):
        # Each part of the step matters: the batch flush, the withdrawals flag, the red turn, the year profit terms
        # and the clear when the months repaid reach the term.
        import dataclasses
        for part in ('batch', 'withdraw-flag', 'red', 'share', 'profit-debit', 'clear'):
            mutated = dataclasses.replace(targets.LOAN_MONTH, plan=lambda x, part=part: targets.loan_step_reading(x, drop=part),
                                          alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, part)

    def test_loan_term_100000_at_20_percent(self):
        """100000 at 20 % over 36 months: the original deducts 3651 for 36 months (131436 in all, profit down by the
        interest share 873 a month, 31428 in all) and the loan is gone after month 36. OpenTPW's former annuity
        (M 3716) would have cost 133776 in all; the plan and C# now pay the original's 131436."""
        target = targets.LOAN_TERM
        loan = {'amount': 100000, 'apr': 20, 'months': 36, 'monthly': 3651, 'bought': 1, 'repaid': 0}
        idle = {'amount': 0, 'apr': 0, 'months': 0, 'monthly': 0, 'bought': 0, 'repaid': 0}
        x = {'bal': 1000000, 'batch': 0, 'wd': 1, 'last': 1000000, 'red': 0, 'profit': 0, 'loans': [loan] + [idle] * 7,
             'turns': [800 * k for k in range(38)]}
        original = targets.run_binary(target, target.sandbox(self.image), x)
        self.assertEqual(original['balance'][35], 1000000 - 131436)
        self.assertEqual(original['balance'][37], 1000000 - 131436)
        self.assertEqual(original['profit'][35], -873 * 36)
        self.assertEqual(original['active'][34:36], [1, 0])
        self.assertEqual(original['acc_out'][-1], 131436)
        self.assertEqual(original['loans'][35][0], (0, 0))
        plan = targets.loan_term_plan(x)
        self.assertEqual(1000000 - plan['balance'][-1], 131436)
        self.assertEqual(plan['profit'][35], -873 * 36)
        plan = targets.loan_term_plan({**x, 'loans': [{**loan, 'monthly': 3716}] + [idle] * 7})
        self.assertEqual(1000000 - plan['balance'][-1], 133776)

    def test_loan_zero_apr_pays_less_than_the_principal(self):
        """At 0 % APR M = floor(P / N), so the original deducts M x N, which is less than P (100000 / 36: 99972); the
        plan and C# follow it (they used to settle the remainder in the last month). The year-profit share wraps in 32 bits when M x N < P."""
        target = targets.LOAN_TERM
        loan = {'amount': 100000, 'apr': 0, 'months': 36, 'monthly': 2777, 'bought': 1, 'repaid': 0}
        idle = {'amount': 0, 'apr': 0, 'months': 0, 'monthly': 0, 'bought': 0, 'repaid': 0}
        x = {'bal': 1000000, 'batch': 0, 'wd': 1, 'last': 1000000, 'red': 0, 'profit': 0, 'loans': [loan] + [idle] * 7,
             'turns': [800 * k for k in range(37)]}
        original = targets.run_binary(target, target.sandbox(self.image), x)
        self.assertEqual(1000000 - original['balance'][-1], 99972)
        self.assertEqual(1000000 - targets.loan_term_plan(x)['balance'][-1], 99972)
        self.assertEqual(original['profit'][0], -(((2777 * 36 - 100000) & 0xFFFFFFFF) // 36))

    def test_loan_payoff_100000_at_20_percent_after_12_months(self):
        """0xccc78 executed: paying off 100000 / 36 months / 20 % (M 3651) after 12 months takes 3651 x 24 = 87624 off the
        balance and the whole-term share 873 x 36 = 31428 off the year profit (months repaid are cleared before they are
        read again); with withdrawals off nothing leaves the balance and the profit gains 87624 - 31428."""
        target = targets.LOAN_PAYOFF
        x = {'bal': 300000, 'profit': 0, 'wd': 1, 'last': 300000, 'index': 2, 'amount': 100000, 'apr': 20, 'months': 36,
             'monthly': 3651, 'bought': 1, 'repaid': 12}
        original = targets.run_binary(target, target.sandbox(self.image), x)
        self.assertEqual((original['taken'], original['balance'], original['profit'], original['bought'], original['repaid'],
                          original['money_out']), (1, 300000 - 87624, -31428, 0, 0, 87624))
        self.assertEqual(targets.loan_payoff_plan(x)['profit'], -31428)
        off = {**x, 'wd': 0}
        original = targets.run_binary(target, target.sandbox(self.image), off)
        self.assertEqual((original['balance'], original['profit'], original['money_out']), (300000, 87624 - 31428, 0))
        self.assertEqual(original, {**original, **targets.loan_payoff_reading(off)})

    def test_loan_payoff_reading_is_sharp(self):
        # The sufficiency test, the whole-term share (not the share of the months left, not none) and the flags matter.
        import dataclasses
        variants = {
            'no-share': lambda x: {**targets.loan_payoff_plan(x), 'profit': x['profit']},
            'share-of-months-left': lambda x: {**targets.loan_payoff_plan(x), 'profit': targets.to_s32(
                x['profit'] - ((((x['monthly'] * x['months']) - x['amount']) & targets.MASK32) // max(x['months'], 1)) * (x['months'] - x['repaid']))
                if targets.loan_payoff_plan(x)['taken'] else x['profit']},
            'payoff-in-profit': lambda x: {**targets.loan_payoff_plan(x), 'profit': targets.to_s32(
                targets.loan_payoff_plan(x)['profit'] - targets.loan_payoff_plan(x)['money_out'])},
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.LOAN_PAYOFF, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_loan_take_reading_is_sharp(self):
        # ECON-005: the amount reaches the balance and the money-in word but not the year profit. Dropping either part, or
        # letting the profit rise, turns the agreement into mismatches.
        import dataclasses
        variants = {
            'profit-rises': lambda x: {**targets.loan_take_plan(x), 'profit': x['profit'] + (0 if x['bought'] else x['amount'])},
            'no-money-in': lambda x: {**targets.loan_take_plan(x), 'money_in': 0},
            'no-balance': lambda x: {**targets.loan_take_plan(x), 'balance': x['bal']},
            'repeatable': lambda x: {**targets.loan_take_plan({**x, 'bought': 0})},
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.LOAN_TAKE, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)
    def test_queue_failure(self):
        for key in (('queue-failure-predicate', 'plan'), ('queue-failure-predicate', 'routine-reading'),
                    ('queue-failure-predicate', 'plan-var-broken')):
            self.check(key)

    def test_queue_failure_csharp(self):
        self.check(('queue-failure-predicate', 'csharp'))

    def test_queue_ride_update(self):
        self.check(('queue-ride-update', 'plan'))

    def test_queue_ride_update_reading_is_sharp(self):
        # Each part matters: the states that hand the head on (1, 2 and 4), the state-14 forcing, the script's variable, the release.
        import dataclasses
        read = targets.ride_update_plan
        variants = {
            'state-1-only': lambda x: read({**x, 'state': 0 if x['state'] in (2, 4) else x['state']}),
            'forcing': lambda x: read({**x, 'head_state': 11}),
            'taken': lambda x: read({**x, 'letmeon': 0}),
            'no-release': lambda x: [e for e in read(x) if e[0] != 'release'],
            'always-dismiss': lambda x: read({**x, 'letmeon': x['head']}),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.RIDE_UPDATE_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_queue_failure_reading_is_sharp(self):
        # Every part of the reading matters: the relief flag, the byte wrap of the truncated gauge and the limit of 25.
        import dataclasses
        variants = {
            'relief-flag': lambda x: targets.failure_reading({**x, 'flags': 1}),
            'byte-wrap': lambda x: int(bool(x['flags'] & 1) and targets.fctiwz(x['gauge']) < 25),
            'limit': lambda x: int(bool(x['flags'] & 1) and (targets.fctiwz(x['gauge']) & 0xFF) < 26),
            'not-flag': lambda x: targets.failure_reading({**x, 'flags': x['flags'] ^ 1}),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.FAILURE, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_queue_join(self):
        for key in (('queue-join-difference', 'plan'), ('queue-join-difference', 'routine-reading'),
                    ('queue-join-difference', 'plan-static'), ('queue-join-gate', 'plan'),
                    ('queue-join-gate', 'routine-reading'), ('queue-join-gate', 'plan-static')):
            self.check(key)

    def test_queue_join_csharp(self):
        for key in (('queue-join-difference', 'csharp'), ('queue-join-gate', 'csharp')):
            self.check(key)

    def test_queue_join_csharp_remainder(self):
        # QUEUE-004: C# has no thing behind ride +36. Every input where it differs from the original is an ordinary ride (not a
        # sideshow, coaster or relief object) with a data level and such a thing, and with that thing removed C# agrees on all inputs.
        import dataclasses
        if self.bridge is None:
            self.skipTest('no .NET 10 SDK')
        for target in (targets.JOIN_DIFFERENCE_TARGET, targets.JOIN_GATE):
            [comparison] = [c for c in compare(target, self.image, DEFAULT_COUNT, keep=10**6, bridge=self.bridge)
                            if c.side == 'csharp']
            self.assertGreater(comparison.mismatches, 0, target.key)
            for example in comparison.examples:
                x = example['input']
                self.assertTrue(x['thing36'] != 0 and x['kind'] != 2 and x['track'] != 3 and s32(x['level']) != 0
                                and not targets.failure_reading(x), f'{target.key}: unexplained {example}')
            original_inputs = target.inputs
            without = dataclasses.replace(target, inputs=lambda rng, n: [{**x, 'thing36': 0} for x in original_inputs(rng, n)])
            [comparison] = [c for c in compare(without, self.image, DEFAULT_COUNT, bridge=self.bridge) if c.side == 'csharp']
            self.assertEqual(comparison.mismatches, 0, f'{target.key}: first counterexample {comparison.first}')

    def test_queue_join_reading_is_sharp(self):
        # Every part of the reading matters, for the gate (outcome and difference) and the difference alone.
        import dataclasses
        read = targets.join_gate_reading

        def gate(difference_of, flag_of):
            def reading(x):
                d = difference_of(x)
                far = flag_of(x) and abs(d) >= 45
                return {'outcome': ('bored' if d < 0 else 'scared') if far else 'pass', 'difference': d}
            return reading
        plain = targets.join_difference_reading
        unclamped = lambda x: (targets.join_excitement_reading(x) & 0xFF) - (x['pref'] & 0xFF)
        unmasked_pref = lambda x: max(-50, min(50, (targets.join_excitement_reading(x) & 0xFF) - x['pref']))
        unmasked_exc = lambda x: max(-50, min(50, targets.join_excitement_reading(x) - (x['pref'] & 0xFF)))
        gate_variants = {
            'relief': lambda x: read({**x, 'flags': x['flags'] & ~1}),
            'sideshow': lambda x: read({**x, 'kind': 0 if x['kind'] == 2 else x['kind']}),
            'coaster': lambda x: read({**x, 'track': 0}),
            'pieces': lambda x: read({**x, 'thing36': 0}),
            'speed-ratio': lambda x: read({**x, 'speed': x['init_speed']}),
            'duration-ratio': lambda x: read({**x, 'dur': x['init_dur']}),
            'gate-by-word': gate(plain, lambda x: s32(x['level']) != 0),
            'gate-always': gate(plain, lambda x: True),
            'gate-by-kind': gate(plain, lambda x: x['kind'] in (0, 2)),
            'threshold': lambda x: {**read(x), 'outcome': 'pass' if abs(plain(x)) < 46 else read(x)['outcome']},
            'sign': lambda x: {**read(x), 'outcome': {'bored': 'scared', 'scared': 'bored'}.get(read(x)['outcome'], 'pass')},
            'no-clamp': gate(unclamped, lambda x: targets.join_excitement_reading(x, 1)),
            'pref-unmasked': gate(unmasked_pref, lambda x: targets.join_excitement_reading(x, 1)),
            'excitement-unmasked': gate(unmasked_exc, lambda x: targets.join_excitement_reading(x, 1)),
        }
        for name, reading in gate_variants.items():
            mutated = dataclasses.replace(targets.JOIN_GATE, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)
        difference_variants = {
            'relief': lambda x: plain({**x, 'flags': x['flags'] & ~1}),
            'sideshow': lambda x: plain({**x, 'kind': 0 if x['kind'] == 2 else x['kind']}),
            'coaster': lambda x: plain({**x, 'track': 0}),
            'pieces': lambda x: plain({**x, 'thing36': 0}),
            'speed-ratio': lambda x: plain({**x, 'speed': x['init_speed']}),
            'duration-ratio': lambda x: plain({**x, 'dur': x['init_dur']}),
            'no-clamp': unclamped, 'pref-unmasked': unmasked_pref, 'excitement-unmasked': unmasked_exc,
            'level-zero': lambda x: plain({**x, 'level': x['level'] or 1}),
        }
        for name, reading in difference_variants.items():
            mutated = dataclasses.replace(targets.JOIN_DIFFERENCE_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_queue_join_refusal_trace(self):
        # The refusal path: thought 12 with "ride is not exciting enough!" and reason 5 when the ride is below the preference,
        # thought 15 with "ride is too exciting!" and reason 4 above it (join_gate_binary raises on any other trace).
        target = targets.JOIN_GATE
        sandbox = target.sandbox(self.image)
        base = {'idx': 0, 'pref': 80, 'kind': 0, 'level': 30, 'track': 0, 'flags': 0, 'gauge': 100.0, 'rlevel': 0,
                'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'thing36': 0, 'pieces': [0, 0, 0, 0], 'goods': 0,
                'price': 0, 'win': 0, 'ctrl': 0, 'coaster_e': 0.0}
        self.assertEqual(targets.run_binary(target, sandbox, base), {'outcome': 'bored', 'difference': -50})
        self.assertEqual(targets.run_binary(target, sandbox, {**base, 'pref': 20, 'level': 90}),
                         {'outcome': 'scared', 'difference': 50})
        self.assertEqual(targets.run_binary(target, sandbox, {**base, 'pref': 0, 'level': 90, 'flags': 1, 'gauge': 3.0}),
                         {'outcome': 'scared', 'difference': 100})
        self.assertEqual(targets.run_binary(target, sandbox, {**base, 'level': 0}), {'outcome': 'pass', 'difference': -50})

    def test_shop_sale_litter(self):
        for key in (('shop-sale-litter', 'plan'), ('shop-sale-litter', 'routine-reading'), ('guest-litter-trigger', 'routine-reading')):
            self.check(key)

    def test_shop_sale_litter_csharp(self):
        self.check(('shop-sale-litter', 'csharp'))

    def test_shop_sale_litter_rule_differs_only_where_the_meter_clamps(self):
        # ECON-020: the old rule (store + LitterEffect) and C# agree with the routine wherever the meter stays inside 0..100; every
        # mismatch is a sum outside that range (a NaN meter agrees: the clamp lets it through).
        for side in ('plan', 'csharp'):
            if side == 'csharp' and self.bridge is None:
                continue
            [comparison] = [c for c in compare(targets.ECON_SALE, self.image, DEFAULT_COUNT, keep=10**6, bridge=self.bridge)
                            if c.side == side]
            self.assertEqual(comparison.mismatches, 213, side)
            for example in comparison.examples:
                x = example['input']
                total = targets.f32(float(x['meters']['litter']) + targets.f32(float(x['effects'][4])))  # jsonable turns nan/inf into text
                self.assertTrue(total > 100.0 or total < 0.0, f'{side}: unexplained {example}')

    def test_shop_sale_litter_reading_is_sharp(self):
        # Every part of the reading matters: the sign, the effect slot and the clamp of each of the five meters, and the log test.
        import dataclasses
        names = [name for name, _, _, _ in targets.SALE_METERS]
        variants = {'log-threshold': lambda x: targets.sale_reading(x, log_threshold=10),
                    'log-always': lambda x: targets.sale_reading(x, log_threshold=-2 ** 31),
                    'swap-vomit-happy': lambda x: targets.sale_reading(x, slots={'vomit': 3, 'happy': 2}),
                    'swap-thirst-hunger': lambda x: targets.sale_reading(x, slots={'thirst': 1, 'hunger': 0}),
                    'litter-slot-hunger': lambda x: targets.sale_reading(x, slots={'litter': 1})}
        for name in names:
            variants[f'sign-{name}'] = lambda x, name=name: targets.sale_reading(
                x, signs={name: -dict((n, g) for n, _, _, g in targets.SALE_METERS)[name]})
            variants[f'no-clamp-{name}'] = lambda x, name=name: targets.sale_reading(x, unclamped=(name,))
        variants['no-clamp-floor'] = lambda x: {**targets.sale_reading(x), 'meters': {
            **targets.sale_reading(x)['meters'], 'litter': min(100.0, targets.f32(x['meters']['litter'] + float(x['effects'][4])))}}
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.ECON_SALE, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)
        # the drop threshold: 90 and not 89 or 91, byte-wrapped
        for name, reading in {'89': lambda x: (targets.fctiwz(x['litter']) & 0xFF) >= 89,
                              '91': lambda x: (targets.fctiwz(x['litter']) & 0xFF) >= 91,
                              'no-byte': lambda x: targets.fctiwz(x['litter']) >= 90,
                              'rounded': lambda x: (round(x['litter']) & 0xFF) >= 90 if x['litter'] == x['litter'] and abs(x['litter']) < 1e9 else False,
                              }.items():
            mutated = dataclasses.replace(targets.ECON_TRIGGER, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'trigger {name}')

    def test_shop_sale_litter_items_per_sale(self):
        # What the number means, executed: a guest who keeps buying from one shop gains LitterEffect on the litter meter per sale, and the
        # drop test (byte >= 90) fires after ceil(90 / LitterEffect) sales. The executed routine and test give, for the shipped
        # LitterEffect values (0, 10, 25, 50), 0, 1/9, 1/4 and 1/2 litter items per sale; the old rule gave 0, 1/10, 1/4 and 1/2.
        sale, trigger = targets.ECON_SALE, targets.ECON_TRIGGER
        sale_box, trigger_box = sale.sandbox(self.image), trigger.sandbox(self.image)
        sales_per_drop = {}
        for effect in (0, 5, 10, 25, 50):
            meter, sales = 0.0, 0
            while sales < 200:
                x = {'meters': {'thirst': 0.0, 'hunger': 0.0, 'vomit': 0.0, 'happy': 50.0, 'litter': meter}, 'effects': [0, 0, 0, 0, effect]}
                meter = targets.run_binary(sale, sale_box, x)['meters']['litter']
                sales += 1
                if targets.run_binary(trigger, trigger_box, {'litter': meter}):
                    break
            sales_per_drop[effect] = sales if sales < 200 else None
        self.assertEqual(sales_per_drop, {0: None, 5: 18, 10: 9, 25: 4, 50: 2})

    def test_shop_sale_litter_stubs_are_checked(self):
        # The type lookup must see the registry and the shop's type id; a wrong id aborts the run instead of returning a record.
        target = targets.ECON_SALE
        sandbox = target.sandbox(self.image)
        sandbox.reset()
        targets.SALE_STATE.registry = targets.toc_pointer(sandbox, targets.POINTS_REGISTRY)
        targets.SALE_STATE.record = sandbox.struct(4096)
        targets.SALE_STATE.guest = sandbox.struct(1024)
        shop = sandbox.struct(256, {14: ('H', targets.SALE_TYPE_ID + 1)})
        with self.assertRaises(AssertionError):
            sandbox.call(targets.SALE_FRAGMENT, {3: shop, 25: targets.toc_pointer(sandbox, targets.SALE_GLOBALS),
                                                 28: targets.SALE_STATE.guest, 30: targets.toc_pointer(sandbox, targets.SALE_STRINGS)},
                         stop=targets.SALE_STOP)

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
        ('queue-join-gate', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         'return difference < 0 ? QueueJoinResult.NotExcitingEnough : QueueJoinResult.TooExciting;',
         'return difference > 0 ? QueueJoinResult.NotExcitingEnough : QueueJoinResult.TooExciting;'),
        ('queue-failure-predicate', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         '(Truncate( wearGauge ) & 0xFF) < WornGauge', '(Truncate( wearGauge ) & 0xFF) <= WornGauge'),
        ('queue-join-difference', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         'ratio > 1.25f ? 1.25f : ratio', 'ratio > 1.5f ? 1.5f : ratio'),
        ('queue-join-difference', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         'Math.Clamp( terms.CostOfGoods - terms.PricePerUse, 0, 100 )', 'Math.Clamp( terms.CostOfGoods - terms.PricePerUse, 0, 99 )'),
        ('park-points', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         '!item.ScriptBroken && item.IsOpen;', '!item.ScriptBroken;'),
        ('loan-offers', 'csharp', 'source/OpenTPW/Economy/ParkLedger.cs',
         'return TruncateUnsigned( amount * power / months );', 'return TruncateUnsigned( amount * power / months + 1.0 );'),
        ('loan-term', 'csharp', 'source/OpenTPW/Economy/ParkLedger.cs',
         'account.MonthlyRepayment);\n\t}', 'account.MonthlyRepayment + 1);\n\t}'),
        ('loan-payoff', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'Ledger.ChargeLoanInterest( LoanMath.PayoffInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount ) );',
         'Ledger.ChargeLoanInterest( 0 );'),
        ('staff-training-split', 'csharp', 'source/OpenTPW/Economy/ParkStaff.cs',
         'spent += share;', 'spent += share / 2;'),
        ('loan-take', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'Post( LedgerCategory.LoanReceived, offer.Amount );', 'Post( LedgerCategory.OtherIncome, offer.Amount );'),
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
