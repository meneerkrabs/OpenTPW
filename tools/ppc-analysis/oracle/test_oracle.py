"""Differential oracle tests: identified Mac routines vs the plans and vs OpenTPW's C# on this checkout.

Gated on OPENTPW_MAC_BIN or OPENTPW_PPC_BIN_ROOT naming the private Feral `bin` directory (SHA-256
pinned); the C# comparisons additionally need a .NET SDK (OPENTPW_DOTNET or the project default).
Every comparison is pinned in EXPECTED: an agreement must stay an agreement, and a known mismatch
(a finding, docs/reverse/ORACLE.md §5) must keep its count and first counterexample until someone
fixes it and updates the table and ORACLE.md together. Gates only get stricter.
"""
from __future__ import annotations

import dataclasses
import datetime
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
from differential import DEFAULT_COUNT, compare, target_inputs  # noqa: E402
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
    # F20 (fixed in C#): the only writer of the speed boost +196 is the Sugar branch (type +344 == 4) of the outcome routine 0xeaaf8:
    # boost += trunc(6 x amount byte / 100), u16. Fat, Salt and Ice change the toilet and thirst floats instead (plan only).
    ('walk-boost', 'plan'): AGREE,
    ('walk-boost', 'csharp'): AGREE,
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
    # F9: 0xcb4e8 uses the factor 0.8 while the 0x1083c8 thing's +48 word is positive (the weather thing's mCurrentDrops: GUESTS.md
    # and C# take that), cuts to 500 − guests in limit mode 1 (online; C#: OnlineVisit, tp.exe 0x4c0830 sets GameType 1 for a
    # visit), and both cuts compare guests + group unsigned without a floor at 0 (fixed in GUESTS.md and C#, tp.exe 0x4cf6e5:
    # the group above the limit is negative, a negative group wraps the compare). All 515 inputs agree.
    ('arrival-group', 'plan'): AGREE,
    ('arrival-group', 'routine-reading'): AGREE,
    ('arrival-group', 'csharp'): AGREE,
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
    # reading on the returned word and the stub call order. The C# rule with the level's sync (every condition) differs only
    # where four times the queue cells wraps 32 bits, which C# ints do not reach.
    ('park-points-counts-test', 'plan'): AGREE,
    ('park-points-counts-test', 'implemented-subset'): (47, {'flags': 4, 'state': 0, 'open': 1, 'cell': 4660,
                                                             'cells': 0x40000000, 'track': 0, 'thing40': 1, 'coaster': 1,
                                                             'count': 0xFFFFFFFF}),
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
    # Fixed in C# (ORACLE.md 7.15): the guest's litter meter takes LitterEffect with MeterMath.Add, clamped like the routine.
    ('shop-sale-litter', 'csharp'): AGREE,
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
    # F19 (fixed in C#): the mechanic arrival 0xd9c0c sets the job timer to WorkDuration x (100 - low byte of the truncated gauge) / 100 for a
    # repair (a ride at 40 %: 48 of 80) and to WorkDuration x Upgrades[level + 1].DurationOfUpgrade for an upgrade (ride state 2), without a
    # minimum of one. The old flat WorkDuration differs on 396 of 529 inputs ("flat-repair": the same without the upgrade factor).
    ('ride-repair-duration', 'plan'): (396, {'mech_id': 176, 'arrive': 0, 'grade': 0, 'work': 80, 'gauge': 40.0, 'level': 0, 'upgrade': 2, 'state': 1, 'flag': 0,
                                      'ride_mech': 176}),
    ('ride-repair-duration', 'routine-reading'): AGREE,
    ('ride-repair-duration', 'flat-repair'): (352, {'mech_id': 176, 'arrive': 0, 'grade': 0, 'work': 80, 'gauge': 40.0, 'level': 0, 'upgrade': 2, 'state': 1, 'flag': 0,
                                      'ride_mech': 176}),
    ('ride-repair-duration', 'csharp'): AGREE,
    # F19 (executed flow): a mechanic whose ride names another mechanic, or that is still walking (0xe6454 == 2), releases itself; a job
    # needs ride state 1 or 2 or the maintenance word +96; it writes the sub-state 18, the state 13 and the timer.
    ('ride-repair-arrival', 'plan'): AGREE,
    # F21 (fixed in C#): the sale credits 0xe25e8 = Upgrades[0].CostOfUpgrade (record +440, no upgrade costs) x the percentage 0xe2424
    # / 100; the percentage reads the CURRENT level's row (+392 + 64 x level) by age in exact 365 day years (0, 1, 2, anything else the
    # fourth) and is 100 for a thing nobody used (+412 == 0) in its first 30 days while level 0's first percentage is positive. The old
    # rule (catalogue costs summed through the level, no 100) differs on 280 of 572 inputs.
    ('object-scrap-value', 'plan'): (280, {'now': 1000, 'built': 1000, 'level': 0, 'used': 0, 'cost': [10000, 8000, 12000],
                                           'pct': [[50, 30, 20, 10], [45, 25, 15, 8], [40, 20, 10, 5]]}),
    ('object-scrap-value', 'routine-reading'): AGREE,
    ('object-scrap-value', 'csharp'): AGREE,
    # F22 (ECON-014, fixed in C#): a staff member's energy (+504) and happiness (+500) are singles that start at 100 and move every park turn:
    # a moving member (0xf44c0) loses 0.012 (6 - grade) energy and 0.005 (6 - grade) happiness, a working one (0xf459c) 0.025f (6 - grade)
    # and 0.01 (6 - grade), a resting one (0xf3d8c) gains the grade's RecuperationRate and HappinessRecuperationRate, all clamped to 0..100;
    # a rest ends when the truncated energy byte reaches 100. OpenTPW had happiness fixed at 100 and no energy ("plan").
    ('staff-vitals', 'plan'): (427, {'op': 'walk', 'grade': 0, 'energy': 100.0, 'happiness': 100.0, 'rate_e': 42.81186294555664,
                                     'rate_h': 1.053891658782959, 'type': 8, 'level': -1}),
    ('staff-vitals', 'routine-reading'): AGREE,
    ('staff-vitals', 'csharp'): AGREE,
    # F22: the tail of 0xf4900. A member whose truncated energy byte is at most RestLevel (signed) is tired and seeks a rest area; with none
    # a world draw with (draw & 15) == 0 costs it HappyHitCosNoRestArea happiness (clamped), one turn in sixteen. The plan never does.
    ('staff-rest-seek', 'plan'): (81, {'energy': 1.0, 'happiness': 50.0, 'level': 1, 'hit': 2, 'found': 0, 'rng': 249103477}),
    ('staff-rest-seek', 'routine-reading'): AGREE,
    ('staff-rest-seek', 'csharp'): AGREE,
    # F22: 0xf8410. More than three members of a role whose truncated energy bytes, or happiness bytes, average (unsigned floor) below 15
    # have a grievance; handymen also when the analyser quotient exceeds 0.2f; a nonzero forced word at manager +100 is one. The plan has none.
    ('staff-strike-grievance', 'plan'): 313,
    ('staff-strike-grievance', 'routine-reading'): AGREE,
    ('staff-strike-grievance', 'csharp'): AGREE,
    # F22: 0xf7e18 with 0xf7fe0, replayed over several month ends. A grievance climbs a role's level 0 -> 1 (a warning), then 2, 3, 4 (a strike each
    # time, ended at the next month end); no grievance clears it. The plan has no strikes.
    ('staff-strike-month', 'plan'): 442,
    ('staff-strike-month', 'routine-reading'): AGREE,
    ('staff-strike-month', 'csharp'): AGREE,
    # F23 (ECON-039, fixed in C#): the local ticket check 0xd31d0 awards the yearly profit ticket when the bank's year profit word is signed
    # GREATER than GoldenTicketLocal.ProfitYear, with no factor (the 0.9 multiplier of 0x13fdc is the advisor's near-ticket hint). The
    # former rule (>=) is the pinned plan: it differs exactly where the profit equals the threshold.
    ('profit-ticket', 'plan'): (154, {'profit': 15000, 'threshold': 15000}),
    ('profit-ticket', 'routine-reading'): AGREE,
    ('profit-ticket', 'csharp'): AGREE,
    ('profit-ticket-hint', 'routine-reading'): AGREE,
    # F23 (all tickets): every local and global arm is a signed strictly-greater compare; the happiness ticket compares the single average with
    # single(Happiness) and the guest count with AtLeastThisManyHappyPeople; the recent-visitors arm also needs the park age in months above
    # RecentVisitorMonths. The former rule (>= everywhere, the average truncated) is the pinned plan.
    ('local-tickets', 'plan'): (458, {'visitors_limit': 100, 'people_limit': 200, 'happiness_limit': 75, 'happy_people_limit': 150, 'threshold': 15000,
                                      'recent_limit': 350, 'months_limit': 6, 'visitors': 100, 'people': 0, 'avg': 0.0, 'built': 0, 'profit': 0,
                                      'recent': 0, 'age': 9}),
    ('local-tickets', 'routine-reading'): AGREE,
    ('local-tickets', 'csharp'): AGREE,
    ('global-tickets', 'routine-reading'): AGREE,
    ('global-tickets', 'csharp'): AGREE,
    # F30 (ECON-001, ECON-002, executed): 0xe4394 turns the world turn into 2000-01-01 + floor(turn x rate / 4) seconds (the rate is the
    # calendar's +28 word, 15000: 3750 s a turn) through the OS calendar, and 0xe3f0c posts a day, a month and a year message when the
    # date's day, month (0-based) and year differ from the fields stored at the last update. ParkCalendar.DateAtTurn and the
    # day / month / year events of ParkEconomy.Advance do the same: agree on every turn near the month, leap-day and year boundaries.
    ('calendar-date', 'plan'): AGREE,
    ('calendar-date', 'csharp'): AGREE,
    ('calendar-update', 'plan'): AGREE,
    ('calendar-update', 'routine-reading'): AGREE,
    ('calendar-update', 'csharp'): AGREE,
    # F30: the only difference is the start. The constructor 0xe3c90 stores month 0, day -1 and year 0, so the first update of a new
    # calendar posts a day and a year message (and a month message outside January) whatever the date; C# starts synchronised.
    ('calendar-first-update', 'plan'): (503, {'start': 0, 'count': 40, 'state': 'fresh'}),
    ('calendar-first-update', 'routine-reading'): AGREE,
    # F30: the catch-up loop 0x1c22b0 executed down to the clock: 31 ms substeps, a park turn on every eighth, 3 at most per call, a
    # backlog above 2000 ms dropped. The 57 differences are 1 ms: the scheduler clock adds each gap times the scale with a fused
    # multiply-add in binary64 and truncates (16 x 0.8 summed ten times is 127.99999999999999, the plan's exact 128).
    ('park-turn-cadence', 'plan'): (57, {'scale': 0.8, 'mode': 2, 'flags': [False, False], 'raw0': 2677538059, 'counter0': 0,
                                         'gaps': [16] * 64}),
    ('park-turn-cadence', 'routine-reading'): AGREE,
    # F31 (ECON-033, executed): the world update 0xd67f0 runs the bank, the staff pool and the calendar update, then (turn % 100 == 0, unsigned)
    # the ticket dispatcher 0xd2f1c, which runs its three checks only while the game type word is 0. ParkEconomy.RunsGoldenTicketCheck agrees; the
    # order was the other way round in C# (ticket check, then calendar): 148 scripts differ in the call order, 5 differ with a signed remainder.
    ('golden-ticket-cadence', 'plan'): AGREE,
    ('golden-ticket-cadence', 'plan-signed'): (5, {'mode': 0, 'turns': [4294967200, 4294967196, 4294967295, 2147483600, 2147483700,
                                                                          2147483648]}),
    ('golden-ticket-cadence', 'plan-tickets-before-calendar'): (148, {'mode': 0, 'turns': [0, 1, 99, 100, 101, 199, 200, 201, 300]}),
    ('golden-ticket-cadence', 'csharp'): AGREE,
    # F32 (ECON-004, UI-022, executed): the speed keys' release callbacks 0x11315c / 0x113184 / 0x1131ac divide the scheduler clock's scale by
    # 1.25, multiply it by 1.25 and store 1.0, clamped to 0.25..2.0 (any scale above 2.0 steps down to it), and the scheduler clock 0x10e844 that
    # park turns, scripts and animation read adds each raw gap times the scale. The plan's exact scaled time differs from the executed clock by
    # 1 ms once (ten 16 ms reads at 0.8: 127, not 128). C#: OpenTPW's fixed clock fed with each frame's time times the scale reaches the turn
    # count of the executed loop to within one turn (never ahead of it, at most one behind) at every frame of 506 loops at scales 0.25 to 4.
    ('clock-speed-scale', 'plan'): (1, {'scale': 0.8, 'raw0': 0, 'ops': [16] * 10}),
    ('clock-speed-scale', 'routine-reading'): AGREE,
    ('park-turn-cadence-frames', 'plan'): (53, {'scale': 0.8, 'mode': 0, 'flags': [False, False], 'raw0': 0, 'counter0': 0,
                                                'gaps': [33] * 95}),
    ('park-turn-cadence-frames', 'routine-reading'): AGREE,
    ('park-turn-cadence-frames', 'csharp'): AGREE,
    # F24 (fixed in C#): 0xe9164 scores an attraction as seven matches (distance, queue, excitement, thirst, hunger, toilet, illness) combined as
    # sum(weight * match) / sum(ALL seven weights) in unsigned 32-bit arithmetic, then scales by DecisionVariable2 for a new ride, by
    # DecisionVariable3 for an indoor ride in rain and by the golden-ticket or upgrade-cost factor, and divides by 5, 4, 3, 2 for the
    # used and temporary history. The former additive rule differs on every input but a few that the clamp happens to equal.
    ('attraction-score', 'plan'): (592, {'w': [1, 1, 1, 2, 2, 2, 2], 'dv': [7, 5, 5], 'mod': 0, 'golden': 0, 'cost0': 0, 'indoors': 0, 'drops': 0,
                                         'age': 100, 'used': [0, 0, 0, 0], 'temp': [0, 0, 0, 0], 'hist_thing': 'none', 'teffect': 0, 'heffect': 0,
                                         'thirst': 0.0, 'hunger': 0.0, 'toilet': 0.0, 'ill': 0.0, 'qcells': 3, 'qcount': 0, 'gx': 10, 'gy': 10,
                                         'rx': 10, 'ry': 10, 'level': 60, 'kind': 0, 'flags': 4, 'thing36': 0, 'track': 0, 'pref': 60,
                                         'pieces': [0, 0, 0, 0], 'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'rlevel': 0, 'idx': 0,
                                         'gauge': 100.0, 'goods': 60, 'price': 35, 'win': 50, 'ctrl': 0, 'coaster_e': 40.0, 'rid': 7, 'value': 40}),
    ('attraction-score', 'routine-reading'): AGREE,
    ('attraction-score', 'csharp'): AGREE,
    # F24: 0xea318 does nothing for an attraction whose live excitement byte is 0, else adds PerfectRide (difference below 5), GoodRide
    # (below 15) or OKRide (below 40) to happiness (no penalty beyond) and trunc(excitement / divisor) * trunc((100 - byte(hunger)) / 20) to
    # illness, both clamped to 0..100.
    ('ride-outcome', 'plan'): (498, {'idx': 0, 'pref': 60, 'kind': 0, 'level': 60, 'track': 0, 'flags': 4, 'gauge': 100.0, 'rlevel': 0,
                                     'init_speed': 100, 'speed': 100, 'init_dur': 30, 'dur': 30, 'thing36': 0, 'pieces': [0, 0, 0, 0], 'goods': 60,
                                     'price': 35, 'win': 50, 'ctrl': 0, 'coaster_e': 40.0, 'happy': 50.0, 'hunger': 0.0, 'ill': 0.0,
                                     'perfect': 25, 'good': 15, 'ok': 5, 'divisor': 10}),
    ('ride-outcome', 'routine-reading'): AGREE,
    ('ride-outcome', 'csharp'): AGREE,
    # F24: 0xe8ff4 scores every choosable thing with a back-of-queue cell, ignores a score below 10 (unsigned), keeps the best (signed) and takes
    # an equal one on an odd world turn, if the counts-test and the reachability test pass. No random bonus, no positive-only threshold.
    ('attraction-choice', 'plan'): (99, {'turn': 0, 'things': [{'flags': 4, 'cell': 10, 'score': 9, 'counts': 1, 'reach': 1}]}),
    ('attraction-choice', 'routine-reading'): AGREE,
    ('attraction-choice', 'csharp'): AGREE,
    # F25 (fixed in C#): 0xeece4 is gated on the guest's phase (turn & 3 == thing id & 3) as a whole: the exit counter loses 1, the three words of the
    # guest's cell go to happiness, illness and hunger, on a multiple of 16 toilet gains 1 and hunger and thirst 2, a need at a byte of exactly 100 costs
    # happiness 1, and the toilet speed term follows the toilet byte; every turn that is a multiple of 20 shifts a 0 into the temporary history. The former
    # per-second rates (hunger +0.35, thirst +0.45, toilet +0.3, nausea decay) differ on every input.
    ('guest-needs-turn', 'plan'): (529, {'turn': 0, 'id': 4, 'gx': 10, 'gy': 10, 'happy': 50.0, 'thirst': 10.0, 'hunger': 10.0, 'toilet': 10.0,
                                         'ill': 0.0, 'exit': 120, 'cells': [0, 0, 0], 'bonus': 0, 'state': 6, 'prev': 6, 'predicate': 0,
                                         'countdown': 0, 'rng': 1, 'temp': [0, 0, 0, 0]}),
    ('guest-needs-turn', 'routine-reading'): AGREE,
    ('guest-needs-turn', 'csharp'): AGREE,
    # F25: 0xe7644 starts a guest at happiness 50, thirst and hunger a draw modulo 50, toilet a draw modulo 30, illness and litter 0, the exit counter
    # ExitLevel + draw % (2 ExitLevelVar) - ExitLevelVar and the cash StartingCash * (100 + draw % (2 StartingCashVarPc + 1) - StartingCashVarPc) / 100,
    # from eight world draws in a fixed order. The former rule (happiness 60, all needs 0) differs on every input.
    ('guest-constructor', 'plan'): (517, {'rng': 0, 'turn': 3438955804, 'id': 53913, 'exit': 120, 'exit_var': 42, 'cash_pc': 15, 'prank': 0,
                                          'types': [[80, 300, 40], [65, 500, 30], [50, 600, 40], [35, 700, 35], [65, 750, 30], [80, 600, 40],
                                                    [45, 400, 30], [80, 500, 40]]}),
    ('guest-constructor', 'routine-reading'): AGREE,
    ('guest-constructor', 'csharp'): AGREE,
    # F18 (guest side, fixed in C#): UseEffects applies the four meter updates of the sale routine; the litter meter stays in the park's store.
    ('shop-sale-litter', 'csharp-guest-meters'): AGREE,
    # F26 (fixed in C#): the state-6 handler leaves when the truncated happiness byte is 0 (below 1, not "at most 10"), when the exit counter is exactly
    # 0 (not "at most 0": a guest that passes 0 outside state 6 never leaves by it) or when the park is closed (game +0x1da710, mParkClosed).
    ('guest-exit-test', 'plan'): (181, {'happy': 1.0, 'exit': 120, 'closing': 0}),
    ('guest-exit-test', 'routine-reading'): AGREE,
    ('guest-exit-test', 'csharp'): AGREE,
    # F27 (fixed in C#, ECON-049): 0xde654, the guard of the park door's opening walk and of the open buttons, reads the ride's state word
    # (not 1 broken down, not 4 condemned, not 2 upgrading), the maintenance flag +96, the back of the queue and a coaster's circuit. The
    # old rule (ParkGate.MayOpen: the script's VAR_BROKEN == 0) is the pinned "plan-var-broken": 264 of 517 inputs differ.
    ('ride-may-open', 'plan'): AGREE,
    ('ride-may-open', 'plan-var-broken'): (264, {'state': 1, 'maintenance': 0, 'queue': 1, 'track': 0, 'coaster': 1, 'broken': 0}),
    ('ride-may-open', 'routine-reading'): AGREE,
    ('ride-may-open', 'csharp'): AGREE,
    # F27 (executed): CObject::SetState 0xe0c3c writes only the state word for 0 and 3; for 1, 2 and 4 it also runs the head-guest step once,
    # clears the open word +100 and the pending guest +104, writes VAR_RIDECLOSED = 1 and sends the effect 1. Others assert and store.
    ('ride-set-state', 'plan'): AGREE,
    ('ride-set-state', 'plan-store-only'): (248, {'state': 1, 'previous': 0, 'head': 5, 'letmeon': 5, 'head_state': 14, 'open': 1, 'pending': 5}),
    ('ride-set-state', 'routine-reading'): AGREE,
    # F27 (executed, fixed in C#): a repair (0xdef2c, not an upgrade) writes 100.0 into the wear gauge, clears VAR_WORN, the mechanic
    # reservation, the maintenance flag and VAR_BREAKSTAT, opens the ride whatever the guard says (VAR_RIDECLOSED 0, open word 1, SetState(0)) and
    # closes an incomplete coaster again. The life gauge is not touched. The old rule only restored the gauge: 423 of 512 inputs differ.
    ('ride-repair', 'plan'): AGREE,
    ('ride-repair', 'plan-gauge-only'): (423, {'state': 1, 'gauge': 0.0, 'life': 40.0, 'mechanic': 17185, 'maintenance': 0, 'open': 0,
                                               'pending': 0, 'queue': 1, 'track': 0, 'coaster': 1}),
    ('ride-repair', 'routine-reading'): AGREE,
    ('ride-repair', 'csharp'): AGREE,
    # F27 (executed, fixed in C#): the ride update 0xe077c skips states 3 and 4, and runs the wear step and the breakdown check only for a ride
    # that is not in state 1, is open (+100 != 0) and on a turn that is a multiple of 8; relief objects never break down (VAR_WORN below 25).
    # The old rule (ParkEconomy.UpdateRides: no state, open or relief gate) differs on 280 of 530 inputs.
    ('ride-breakdown-update', 'plan'): AGREE,
    ('ride-breakdown-update', 'plan-before'): (280, {'state': 1, 'open': 1, 'tick': 64, 'flags': 4, 'life': 100.0, 'repair': 0.0, 'breakstat': 0}),
    ('ride-breakdown-update', 'routine-reading'): AGREE,
    ('ride-breakdown-update', 'csharp'): AGREE,
    # F28 (ECON-012, ECON-043): the monthly wage handler 0xf3364 debits PayMultiplier[role] x BaseWage[grade] (a 32-bit product) and raises the staff
    # wages statistic, as ParkStaff / EndMonth already did. The dismissal routine 0xf33ec charges the same wage once more (fixed in C#: Fire was free,
    # 506 of 511 inputs differ). Hiring has no charge (the static tests below).
    ('staff-wage-month', 'plan'): AGREE,
    ('staff-wage-month', 'routine-reading'): AGREE,
    ('staff-wage-month', 'csharp'): AGREE,
    ('staff-dismiss-charge', 'plan'): AGREE,
    ('staff-dismiss-charge', 'plan-free'): (506, {'role': 0, 'grade': 0, 'multipliers': [2, 3, 4, 5, 6], 'base_wages': [500, 600, 700, 800, 900],
                                                  'balance': 1000000, 'active': True}),
    ('staff-dismiss-charge', 'routine-reading'): AGREE,
    ('staff-dismiss-charge', 'csharp'): AGREE,
    # F29 (ECON-027, fixed in C# except the census and the staff byte): 0xc7b24 adds eleven capped counts: guests on a cell of type 0, 1, 3, 9 or 10 (the census), objects
    # by the type's Info.WhichUIType (the shipped add-ons declare 0 and count as rides), rides at level 2 or more, and staff of the five classes whose byte +3 is clear.
    # The rule before (the directory kind, guests in the park by state, every hired staff member) differs on 249 of 519 inputs. C# now differs only where a guest's cell
    # type and in-park state disagree or a staff byte is set (test_park_rating_csharp_remainder): ECON-027.
    ('park-rating', 'plan'): AGREE,
    ('park-rating', 'plan-before'): (249, {'things': [{'cls': 3, 'ui': 0, 'dir': 'upgrade', 'level': 0}, {'cls': 3, 'ui': 0, 'dir': 'upgrade', 'level': 0},
                                                       {'cls': 3, 'ui': 0, 'dir': 'upgrade', 'level': 0}]}),
    ('park-rating', 'csharp'): (134, {'things': [{'cls': 5, 'flag3': 1}, {'cls': 5, 'flag3': 0}]}),
    # F29 (ECON-026, fixed in C# for the object part): 0xc1d4c adds points per map cell (1 / 3 / 5 / 1 / 1 / 2 by type, 50 for a cell with a track record, none for unowned
    # cells) and, per object that is not condemned, the costs of its levels times its state of repair byte / 100. The former rule (the sum of the scrap values) differs on
    # 355 of 526 inputs; C# agrees on the object part (the cell term has no counterpart).
    ('park-value', 'plan'): AGREE,
    ('park-value', 'plan-scrap-sum'): (355, {'kind': 'cell', 'type': 0, 'flags': 0, 'id': 3, 'rec_type': 0, 'link': None, 'link_type': 18}),
    ('park-value', 'csharp'): AGREE,
}
CSHARP_PENDING = {'det-world-lcg', 'det-rse-rand', 'coast-rating-step',
                  'coast-rating-final', 'arrival-update', 'park-points-counts-test', 'loan-month',
                  'queue-ride-update', 'guest-litter-trigger', 'ride-repair-arrival', 'ride-set-state', 'profit-ticket-hint',
                  'calendar-first-update', 'park-turn-cadence', 'clock-speed-scale'}

class Identity(unittest.TestCase):
    def test_unidentified_container_is_rejected(self):
        with self.assertRaises(IdentityError):
            Image(b'Joy!peffpwpc' + bytes(64))


class SaleComparison(unittest.TestCase):
    """The shop-sale C# comparison must not pass vacuously: an answer of no meters, or fewer than the four the hook
    documents, compares nothing and would agree with any plan."""

    def test_a_full_answer_decodes_and_agrees(self):
        original = {'meters': {'thirst': 40.0, 'hunger': 30.0, 'vomit': 10.0, 'happy': 50.0, 'litter': 60.0}}
        answer = targets.sale_meters_result({'meters': {}}, [targets.bits(40.0), targets.bits(30.0), targets.bits(10.0), targets.bits(50.0)])
        self.assertEqual({40.0, 30.0, 10.0, 50.0}, set(answer['meters'].values()))
        self.assertTrue(targets.sale_equal(original, answer))

    def test_an_empty_or_short_answer_is_a_mismatch(self):
        original = {'meters': {'thirst': 40.0, 'hunger': 30.0, 'vomit': 10.0, 'happy': 50.0, 'litter': 60.0}}
        for answer in ([], [targets.bits(40.0)], [targets.bits(40.0)] * 5):
            with self.subTest(count=len(answer)):
                decoded = targets.sale_meters_result({'meters': {}}, answer)
                self.assertIn('error', decoded)
                self.assertFalse(targets.sale_equal(original, decoded), 'a malformed answer is never equal')
        self.assertFalse(targets.sale_equal(original, {'meters': {}}), 'an empty comparison map compares nothing')


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

    _runs: dict = {}

    @classmethod
    def cached_runs(cls, target):
        """The inputs of a target and the original's answers, run once for the sharpness and remainder tests."""
        if target.key not in cls._runs:
            inputs = target_inputs(target, DEFAULT_COUNT, 'oracle-v1')
            sandbox = target.sandbox(cls.image)
            cls._runs[target.key] = (inputs, [targets.run_binary(target, sandbox, x) for x in inputs])
        return cls._runs[target.key]

    def reading_mismatches(self, target, reading) -> int:
        inputs, originals = self.cached_runs(target)
        return sum(1 for x, original in zip(inputs, originals) if not target.equal(original, reading(x)))

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
        for key in (('walk-speed-setter', 'plan'), ('walk-smoothing', 'plan'), ('walk-smoothing', 'plan-fused'),
                    ('walk-boost', 'plan')):
            self.check(key)

    def test_walk_boost_reading_is_sharp(self):
        # Each part of the reading matters: the factor 6, the amount byte, the u16 wrap, the Sugar ingredient and the
        # thirst/toilet effects of the other ingredients.
        import dataclasses
        read = targets.boost_reading
        variants = {
            'factor': lambda x: {**read(x), 'boost': (x['boost'] + (x['amount'] & 0xFF) * 5 // 100) & 0xFFFF if x['mode'] == 4 else read(x)['boost']},
            'amount-byte': lambda x: {**read(x), 'boost': (x['boost'] + (x['amount'] & 0xFFFF) * 6 // 100) & 0xFFFF if x['mode'] == 4 else read(x)['boost']},
            'no-wrap': lambda x: {**read(x), 'boost': min(x['boost'] + (x['amount'] & 0xFF) * 6 // 100, 0xFFFF) if x['mode'] == 4 else read(x)['boost']},
            'ingredient': lambda x: read({**x, 'mode': 3 if x['mode'] == 4 else x['mode']}),
            'fat-is-thirst': lambda x: read({**x, 'mode': 2 if x['mode'] == 1 else x['mode']}),
            'ice-is-salt': lambda x: read({**x, 'mode': 2 if x['mode'] == 3 else x['mode']}),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.BOOST, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_walk_csharp(self):
        # WALK-F wired GuestSpeed.Caps / GuestSpeed.Update into the bridge (WALK-plan section 15 left them for after the merge).
        for key in (('walk-speed-setter', 'csharp'), ('walk-smoothing', 'csharp'), ('walk-boost', 'csharp')):
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
    def test_scrap_value(self):
        for key in (('object-scrap-value', 'plan'), ('object-scrap-value', 'routine-reading'), ('object-scrap-value', 'csharp')):
            self.check(key)

    def test_scrap_reading_is_sharp(self):
        # Every part of the routine reading matters: the 100 for an unused new thing, its use test and 30 day length, the current
        # level's row, the base cost alone and the 365 day year each bring mismatches back when changed.
        import dataclasses
        variants = {
            'no-initial-refund': lambda x: targets.scrap_reading(x, initial=False),
            'use-ignored': lambda x: targets.scrap_reading(x, used_test=False),
            'month-of-31-days': lambda x: targets.scrap_reading(x, start_ticks=31 * targets.DAY_TICKS),
            'month-of-29-days': lambda x: targets.scrap_reading(x, start_ticks=29 * targets.DAY_TICKS),
            'level-0-row': lambda x: targets.scrap_reading(x, level_row=False),
            'summed-costs': lambda x: targets.scrap_reading(x, base_cost=False),
            'year-of-366-days': lambda x: targets.scrap_reading(x, year_ticks=366 * targets.DAY_TICKS),
            'year-of-364-days': lambda x: targets.scrap_reading(x, year_ticks=364 * targets.DAY_TICKS),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.SCRAP, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_scrap_value_of_a_two_year_old_upgraded_ride(self):
        # Jungle Rides.sam rows (50/30/20/10, 60/40/30/15, 65/45/35/20) with the Belly Bounce costs 500 / 400 / 500: a used ride at
        # level 2 after two years. The routine refunds 500 x 35 % = 175, the old rule gave 1400 x 35 % = 490.
        two_years = 2 * 365 * 86400 * 4 // 15000 + 3  # turns of 3750 s, just past 730 days
        x = {'now': 2999 + two_years, 'built': 2999, 'level': 2, 'used': 12, 'cost': [500, 400, 500],
             'pct': [[50, 30, 20, 10], [60, 40, 30, 15], [65, 45, 35, 20]]}
        self.assertEqual(targets.run_binary(targets.SCRAP, targets.SCRAP.sandbox(self.image), x), 175)
        self.assertEqual(targets.scrap_plan(x), 490)
        # Unused and new, whatever the level: the whole base price.
        y = {**x, 'now': x['built'] + 10, 'used': 0}
        self.assertEqual(targets.run_binary(targets.SCRAP, targets.SCRAP.sandbox(self.image), y), 500)
        # The 32-bit product wraps before the unsigned division by 100.
        big = {**y, 'cost': [50_000_000, 1, 1]}
        self.assertEqual(targets.run_binary(targets.SCRAP, targets.SCRAP.sandbox(self.image), big), 7_050_327)

    def test_ride_state(self):
        for key in (('ride-may-open', 'plan'), ('ride-may-open', 'plan-var-broken'), ('ride-may-open', 'routine-reading'),
                    ('ride-set-state', 'plan'), ('ride-set-state', 'plan-store-only'), ('ride-set-state', 'routine-reading'),
                    ('ride-repair', 'plan'), ('ride-repair', 'plan-gauge-only'), ('ride-repair', 'routine-reading'),
                    ('ride-breakdown-update', 'plan'), ('ride-breakdown-update', 'plan-before'),
                    ('ride-breakdown-update', 'routine-reading')):
            self.check(key)

    def test_ride_state_csharp(self):
        for key in (('ride-may-open', 'csharp'), ('ride-repair', 'csharp'), ('ride-breakdown-update', 'csharp')):
            self.check(key)

    def test_ride_readings_are_sharp(self):
        # Every part of the four readings matters: each guard condition of 0xde654, each write of SetState, each repair effect and each gate
        # of the breakdown check turns an agreement into mismatches when dropped.
        import dataclasses
        variants = [(targets.MAY_OPEN_TARGET, f'no-{drop}', lambda x, drop=drop: targets.may_open_reading(x, drop=drop))
                    for drop in ('broken', 'condemned', 'maintenance', 'upgrading', 'queue', 'coaster')]
        variants += [(targets.SET_STATE_TARGET, 'no-writes', lambda x: targets.set_state_reading(x, write=False)),
                     (targets.SET_STATE_TARGET, 'no-head-step', lambda x: targets.set_state_reading(x, head_step=False)),
                     (targets.REPAIR_TARGET, 'no-reopen', lambda x: targets.repair_reading(x, reopen=False)),
                     (targets.REPAIR_TARGET, 'keeps-flags', lambda x: targets.repair_reading(x, clear_flags=False)),
                     (targets.REPAIR_TARGET, 'no-coaster-recheck', lambda x: targets.repair_reading(x, coaster_recheck=False)),
                     (targets.REPAIR_TARGET, 'gauge-99', lambda x: targets.repair_reading(x, full_gauge=99.0)),
                     (targets.BREAKDOWN_TARGET, 'no-state-gate', lambda x: targets.breakdown_reading(x, gate_state=False)),
                     (targets.BREAKDOWN_TARGET, 'no-open-gate', lambda x: targets.breakdown_reading(x, gate_open=False)),
                     (targets.BREAKDOWN_TARGET, 'no-relief', lambda x: targets.breakdown_reading(x, relief=False)),
                     (targets.BREAKDOWN_TARGET, 'no-breakstat', lambda x: targets.breakdown_reading(x, breakstat=False))]
        for target, name, reading in variants:
            mutated = dataclasses.replace(target, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'{target.key}: {name}')

    def test_repair_example(self):
        # A broken-down ride (state 1, wear gauge 0, life 40) after its mechanic has finished: the gauge is 100.0, the reservation and the
        # maintenance flag are gone, the ride is open (VAR_RIDECLOSED 0) and idle (state 0), the life gauge stays 40, and the guard of
        # 0xde654 still sees state 1: it logs its warning five times ("opening non-openable ride") and the repair opens the ride anyway.
        target = targets.REPAIR_TARGET
        sandbox = target.sandbox(self.image)
        base = {'state': 1, 'gauge': 0.0, 'life': 40.0, 'mechanic': 0x4321, 'maintenance': 1, 'open': 0, 'pending': 0, 'queue': 1, 'track': 0,
                'coaster': 1}
        original = targets.run_binary(target, sandbox, base)
        self.assertEqual({key: original[key] for key in ('gauge', 'life', 'mechanic', 'maintenance', 'open', 'state')},
                         {'gauge': 100.0, 'life': 40.0, 'mechanic': 0, 'maintenance': 0, 'open': 1, 'state': 0})
        self.assertEqual(original['calls'].count(('warn',)), 5)
        self.assertIn(('var', 6, 0), original['calls'])        # VAR_RIDECLOSED := 0
        self.assertIn(('var', 4, 0), original['calls'])        # VAR_BREAKSTAT := 0
        self.assertIn(('var', 8, 0), original['calls'])        # VAR_WORN := 0
        # A condemned ride (life gauge 0) is idle again after its repair, and the life gauge is still 0: its next ride update breaks it again.
        condemned = targets.run_binary(target, sandbox, {**base, 'state': 4, 'life': 0.0})
        self.assertEqual((condemned['state'], condemned['life']), (0, 0.0))
        # A coaster whose circuit is not complete is closed again by its own repair.
        coaster = targets.run_binary(target, sandbox, {**base, 'state': 0, 'track': 3, 'coaster': 0, 'open': 1})
        self.assertEqual((coaster['open'], coaster['state']), (0, 0))

    def test_breakdown_example(self):
        # A ride whose wear gauge ran out (life 100) on a multiple-of-8 turn: the wear step runs, the life gauge pays 5 and VAR_BREAKSTAT is
        # set. The same ride closed (open word 0), broken down already (state 1) or condemned (4) is left alone, and a relief object with a
        # wear gauge of 20 is only marked worn.
        target = targets.BREAKDOWN_TARGET
        sandbox = target.sandbox(self.image)
        base = {'state': 0, 'open': 1, 'tick': 64, 'flags': 0x4, 'life': 100.0, 'repair': 0.0, 'breakstat': 0}
        self.assertEqual(targets.run_binary(target, sandbox, base), {'calls': [('wear',), ('var', 4, 1)], 'life': 95.0})
        for change in ({'open': 0}, {'state': 1}, {'state': 4}, {'tick': 12}, {'breakstat': 1}):
            result = targets.run_binary(target, sandbox, {**base, **change})
            self.assertNotIn(('var', 4, 1), result['calls'], str(change))
        relief = targets.run_binary(target, sandbox, {**base, 'flags': 1, 'repair': 20.0})
        self.assertEqual(relief, {'calls': [('wear',), ('var', 8, 1)], 'life': 100.0})

    def test_repair_constant_is_one_hundred(self):
        # ECON-024: the "fixed constant from the data pool" a repair writes into the wear gauge is the float at TOC -11016, 100.0.
        self.assertEqual(self.image.memory.u32(self.image.toc + targets.RIDE_GAUGE_SLOT), targets.bits(100.0))

    # ------------------------------------------------------------------------- F28: what a staff member costs
    def test_staff_charges(self):
        for key in (('staff-wage-month', 'plan'), ('staff-wage-month', 'routine-reading'), ('staff-dismiss-charge', 'plan'),
                    ('staff-dismiss-charge', 'plan-free'), ('staff-dismiss-charge', 'routine-reading')):
            self.check(key)

    def test_staff_charges_csharp(self):
        for key in (('staff-wage-month', 'csharp'), ('staff-dismiss-charge', 'csharp')):
            self.check(key)

    def test_staff_charge_reading_is_sharp(self):
        # The wage is the pay multiplier of the role times the base wage of the grade (not the other way round, not a neighbour), the debit is
        # the whole wage, and the wages statistic is raised even where the bank's flag stops the debit.
        import dataclasses
        variants = [(targets.WAGE_MONTH_TARGET, 'wrong-type', lambda x: targets.wage_reading(x, wrong_type=True)),
                    (targets.WAGE_MONTH_TARGET, 'wrong-grade', lambda x: targets.wage_reading(x, wrong_grade=True)),
                    (targets.WAGE_MONTH_TARGET, 'half-wage', lambda x: {**targets.wage_reading(x), 'balance': targets.wage_reading(x)['balance'] // 2}),
                    (targets.WAGE_MONTH_TARGET, 'inactive-no-statistic', lambda x: {**targets.wage_reading(x), 'wages': 0 if not x['active'] else targets.wage_reading(x)['wages']}),
                    (targets.DISMISS_TARGET, 'free', targets.dismiss_free),
                    (targets.DISMISS_TARGET, 'wrong-grade', lambda x: targets.wage_reading(x, wrong_grade=True))]
        for target, name, reading in variants:
            mutated = dataclasses.replace(target, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'{target.key}: {name}')

    def test_dismissal_example(self):
        # A grade-2 mechanic (pay multiplier 3, base wage 700 for grade 2) dismissed from a park with 1,000,000: 2,100 leave the balance, the year profit
        # and the staff statistics. Hiring the same mechanic cost nothing (test_hiring_has_no_debit_in_the_binary).
        target = targets.DISMISS_TARGET
        sandbox = target.sandbox(self.image)
        x = {'role': 1, 'grade': 2, 'multipliers': [2, 3, 4, 5, 6], 'base_wages': [500, 600, 700, 800, 900], 'balance': 1_000_000, 'active': True}
        self.assertEqual(targets.run_binary(target, sandbox, x), {'balance': -2100, 'profit': 2100, 'month': 2100, 'wages': 2100})
        self.assertEqual(targets.run_binary(target, sandbox, {**x, 'active': False}), {'balance': 0, 'profit': 0, 'month': 0, 'wages': 2100})

    @staticmethod
    def code_words(image):
        import struct
        data = image.container.code.data
        return struct.unpack('>%dI' % (len(data) // 4), data[:len(data) // 4 * 4])

    @staticmethod
    def direct_callers(words, target):
        """Offsets of the bl / b instructions that reach the code offset `target` directly."""
        callers = []
        for index, word in enumerate(words):
            if word >> 26 == 18:
                displacement = word & 0x03FFFFFC
                displacement -= 0x04000000 if displacement & 0x02000000 else 0
                if (displacement if word & 2 else 4 * index + displacement) == target:
                    callers.append(4 * index)
        return callers

    BANK_DEBIT, BANK_CREDIT, BANK_OF_GAME = 0xCBFDC, 0xCBF50, 0x108424
    STAFF_CODE = [(0xD2000, 0xDA800), (0xEFF00, 0xF7800)]    # the five staff roles' update code and the staff manager (the object code starts at 0xda874)

    def test_bank_debit_callers_are_pinned(self):
        # ECON-012: every direct caller of the bank's Spend and credit routines. Three lie in the staff code: the monthly wage (0xf3364), the dismissal
        # (0xf33ec) and the month-end training (0xf3588). None lies in the hire path (the next test).
        words = self.code_words(self.image)
        debits = self.direct_callers(words, self.BANK_DEBIT)
        credits = self.direct_callers(words, self.BANK_CREDIT)
        self.assertEqual(debits, [0x378C0, 0x462C0, 0x4A674, 0x4A6D8, 0x72BC8, 0x749D4, 0x74B28, 0x82CEC, 0x82D2C, 0x88E18, 0xDAD0C, 0xDF1C0, 0xE1EA8,
                                  0xF33A0, 0xF3534, 0xF35D4])
        self.assertEqual(credits, [0x462AC, 0x4A644, 0x4A660, 0x4A6C4, 0x85DF8, 0x85F00, 0x8BF44, 0xD06C8, 0xD0A28, 0xDC070, 0xE1B8C, 0x113294, 0x113CD8])
        in_staff = [pc for pc in debits + credits if any(low <= pc < high for low, high in self.STAFF_CODE)]
        self.assertEqual(in_staff, [0xF33A0, 0xF3534, 0xF35D4])

    def test_hiring_has_no_debit_in_the_binary(self):
        # ECON-012 (APPROX-TRACE round 3, "refuted -> not_found"): the hire path is the UI handler of the hire button (0x151d5c -> 0xf61d0, which
        # queues a command for the candidate), the command's constructor 0x6bfa0, its executor 0x6c034 and completion 0x6c1f4, the pool removal 0xf6b58, the
        # thing factory 0xb882c and the staff constructors 0xf2914 / 0xf2980. None of them calls the bank's Spend, credit or lookup routine: hiring is free in
        # the Mac binary. (The staff things' own update code reaches the bank only through the wage, dismissal and training routines above.)
        words = self.code_words(self.image)
        hire_path = [(0xF61D0, 0xF6548), (0x6BFA0, 0x6BFDC), (0x6C034, 0x6C278), (0xF6B58, 0xF6BC8), (0xB882C, 0xB8974), (0xF2914, 0xF2B3C)]
        for routine in (self.BANK_DEBIT, self.BANK_CREDIT, self.BANK_OF_GAME):
            for pc in self.direct_callers(words, routine):
                self.assertFalse(any(low <= pc < high for low, high in hire_path), f'{pc:#x} calls {routine:#x} inside the hire path')
        # The scan sees what it should: the dismissal routine (0xf33ec..0xf3590) does call the bank.
        self.assertTrue(any(0xF33EC <= pc < 0xF3590 for pc in self.direct_callers(words, self.BANK_DEBIT)))

    def test_hire_fee_keys_are_never_read(self):
        # StaffPoolInfo.BaseCostPerStaff (balance +700) and CostPerQualityLevel (+704) are never loaded from the settings object (the TOC slot -30084
        # holds its address). Positive control: the pool timing words +708 and +712 are (ECON-010).
        words = self.code_words(self.image)
        loads: dict[int, list[int]] = {}
        tracked: set[int] = set()
        load_ops = {32, 34, 40, 42, 48, 50}
        for index, word in enumerate(words):
            op, rd, ra = word >> 26, (word >> 21) & 31, (word >> 16) & 31
            if word == 0x7C0802A6:      # mfspr r0,8: a function prologue
                tracked.clear()
                continue
            displacement = word & 0xFFFF
            displacement -= 0x10000 if displacement & 0x8000 else 0
            if op == 32 and ra == 2 and displacement == -30084:
                tracked.add(rd)
                continue
            if op in load_ops and ra in tracked and ra != 1:
                loads.setdefault(displacement, []).append(4 * index)
            if op in (14, 15, 32, 34, 40, 42, 48, 50, 12, 13, 8, 7) and rd in tracked and op not in (48, 50):
                tracked.discard(rd)
            elif op == 31 and ((word >> 1) & 0x3FF) == 444 and ra in tracked:
                tracked.discard(ra)
            elif op == 21 and ra in tracked:
                tracked.discard(ra)
        self.assertTrue(loads.get(708) and loads.get(712), 'the scan must find the pool timing words')
        self.assertEqual((loads.get(700), loads.get(704)), (None, None))

    # ------------------------------------------------------------------------- F29: the park rating and the park value
    def test_park_rating(self):
        for key in (('park-rating', 'plan'), ('park-rating', 'plan-before')):
            self.check(key)

    def test_park_rating_csharp(self):
        self.check(('park-rating', 'csharp'))

    def test_park_rating_csharp_remainder(self):
        # C# has neither the census (the cell type under each guest) nor the staff byte +3, so it differs exactly where a guest's cell type and in-park state disagree
        # and the crowd is large enough to move the visitor term, or a staff member has the byte set (ECON-027). Every other input, objects included, agrees.
        if self.bridge is None:
            self.skipTest('no .NET SDK')
        target = targets.RATING_TARGET
        inputs, originals = self.cached_runs(target)
        answers = self.bridge.run([target.csharp[1](x) for x in inputs])
        differing = [x for x, original, answer in zip(inputs, originals, answers) if not target.equal(original, target.csharp[2](x, answer))]
        self.assertGreater(len(differing), 0)
        for x in differing:
            things = x['things']
            explained = any(t['cls'] == 1 and t['counted'] != t['in_park'] for t in things) or any(t['cls'] in (4, 5, 6, 7, 8) and t['flag3'] for t in things)
            self.assertTrue(explained, f'unexplained: {x}')
        # ... and every input without such a guest or staff member agrees.
        clean = [x for x in inputs if not any(t['cls'] == 1 and t['counted'] != t['in_park'] for t in x['things'])
                 and not any(t['cls'] in (4, 5, 6, 7, 8) and t['flag3'] for t in x['things'])]
        self.assertGreater(len(clean), 100)
        self.assertFalse([x for x in clean if x in differing])

    def test_park_rating_reading_is_sharp(self):
        # Every part of the reading matters: the census (not the in-park state), the cap of 1,000 guests, the UI type of the object (not its directory), the level 2
        # of the upgraded rides, the staff byte +3 and each cap.
        directory_ui = {'ride': 0, 'shop': 1, 'sideshow': 2, 'feature': 3, 'upgrade': 5, 'fixed': 5}
        variants = {'in-park-guests': lambda x: targets.rating_reading(x, census=False),
                    'cap-999': lambda x: targets.rating_reading(x, cap_visitors=999), 'cap-1100': lambda x: targets.rating_reading(x, cap_visitors=1100),
                    'directory-kind': lambda x: targets.rating_reading(x, ui_of=lambda t: directory_ui[t['dir']]),
                    'level-1': lambda x: targets.rating_reading(x, upgraded_level=1), 'level-3': lambda x: targets.rating_reading(x, upgraded_level=3),
                    'ignore-byte-3': lambda x: targets.rating_reading(x, flag3=False),
                    'staff-cap-3': lambda x: targets.rating_reading(x, staff_cap=3), 'staff-cap-5': lambda x: targets.rating_reading(x, staff_cap=5)}
        for name, reading in variants.items():
            self.assertGreater(self.reading_mismatches(targets.RATING_TARGET, reading), 0, name)

    def test_park_rating_example(self):
        # Three "Jump" add-ons (the upgrades directory: Info.WhichUIType 0) count as rides, 3 x 3 / 2 = 4 points, where the directory rule gave nothing. A handyman
        # whose byte +3 is set (being removed) does not count, and 999 counted guests give 19 points where 1,000 give 20.
        target = targets.RATING_TARGET
        sandbox = target.sandbox(self.image)
        addons = {'things': [{'cls': 3, 'ui': 0, 'dir': 'upgrade', 'level': 0}] * 3}
        self.assertEqual((targets.run_binary(target, sandbox, addons), targets.rating_before(addons)), (4, 0))
        staff = {'things': [{'cls': 5, 'flag3': 1}, {'cls': 5, 'flag3': 0}]}
        self.assertEqual((targets.run_binary(target, sandbox, staff), targets.rating_before(staff)), (1, 2))
        for count, expected in ((999, 19), (1000, 20), (1100, 20)):
            crowd = {'things': [{'cls': 1, 'counted': True, 'in_park': True, 'cell': n} for n in range(count)]}
            self.assertEqual(targets.run_binary(target, sandbox, crowd), expected, count)

    def test_park_value(self):
        for key in (('park-value', 'plan'), ('park-value', 'plan-scrap-sum')):
            self.check(key)

    def test_park_value_csharp(self):
        self.check(('park-value', 'csharp'))

    def test_park_value_reading_is_sharp(self):
        variants = {'owned-mask-0x20': lambda x: targets.value_cell_points(x, mask=0x20) if x['kind'] == 'cell' else targets.value_thing_points(x),
                    'no-link-points': lambda x: targets.value_cell_points(x, weights=(1, 3, 5, 1, 1, 2, 0)) if x['kind'] == 'cell' else targets.value_thing_points(x),
                    'no-follow': lambda x: targets.value_cell_points(x, follow=False) if x['kind'] == 'cell' else targets.value_thing_points(x),
                    'empty-cell-1': lambda x: targets.value_cell_points(x, weights=(1, 3, 5, 1, 1, 1, 50)) if x['kind'] == 'cell' else targets.value_thing_points(x),
                    'path-cell-2': lambda x: targets.value_cell_points(x, weights=(1, 2, 5, 1, 1, 2, 50)) if x['kind'] == 'cell' else targets.value_thing_points(x),
                    'queue-cell-4': lambda x: targets.value_cell_points(x, weights=(1, 3, 4, 1, 1, 2, 50)) if x['kind'] == 'cell' else targets.value_thing_points(x),
                    'no-repair-scale': lambda x: targets.value_cell_points(x) if x['kind'] == 'cell' else targets.value_thing_points(x, scale=False),
                    'count-condemned': lambda x: targets.value_cell_points(x) if x['kind'] == 'cell' else targets.value_thing_points(x, skip_condemned=False),
                    'current-level-only': lambda x: targets.value_cell_points(x) if x['kind'] == 'cell' else targets.value_thing_points(x, sum_levels=False),
                    'no-byte': lambda x: targets.value_cell_points(x) if x['kind'] == 'cell' else targets.value_thing_points(x, byte=False)}
        for name, reading in variants.items():
            self.assertGreater(self.reading_mismatches(targets.VALUE_TARGET, reading), 0, name)

    def test_park_value_whole_function(self):
        # The cell loop (128 x 128) and the thing loop of 0xc1d4c run once on a whole map: the value is the sum of the per-cell and per-thing readings, an empty map is
        # worth 2 points per cell, and the cache (0xc9020) answers within the same four-turn window.
        empty, executed = targets.value_whole(self.image, {}, [])
        self.assertEqual(empty, 2 * 128 * 128)
        self.assertGreater(executed, 2_000_000)
        cells = {0: {14: ('H', 0x40)}, 5: {8: ('I', 1)}, 6: {8: ('I', 3)}, 7: {8: ('I', 9)}, 8: {8: ('I', 10)}, 9: {8: ('I', 2)},
                 10: {0: ('H', 3)}, 11: {0: ('H', 4), 8: ('I', 3)}, 12: {0: ('H', 5)}}
        records = {3: {8: ('I', 12), 16: ('H', 9)}, 9: {8: ('I', 25)}, 4: {8: ('I', 13)}, 5: {8: ('I', 17), 16: ('H', 7)}}
        things = [{'gauge': 80.0, 'level': 1, 'state': 0}, {'gauge': 80.0, 'level': 1, 'state': 4}, {'gauge': 25.5, 'level': 0, 'state': 1}]
        value, _ = targets.value_whole(self.image, cells, things, records=records)
        base = {'type': 0, 'flags': 0, 'rec_type': 0, 'link': None, 'link_type': 0}
        special = [{**base, 'flags': 0x40}, {**base, 'type': 1}, {**base, 'type': 3}, {**base, 'type': 9}, {**base, 'type': 10}, {**base, 'type': 2},
                   {**base, 'rec_type': 12, 'link': 9, 'link_type': 25}, {**base, 'type': 3, 'rec_type': 13}, {**base, 'rec_type': 17, 'link': 7, 'link_type': 0}]
        self.assertEqual([targets.value_cell_points(cell) for cell in special], [0, 3, 5, 6, 1, 1, 52, 55, 2])
        expected = sum(targets.value_cell_points(cell) for cell in special) + 2 * (128 * 128 - len(special))
        expected += targets.value_thing_points({**things[0], 'costs': [1000, 500, 300]}) + targets.value_thing_points({**things[1], 'costs': [1000, 500, 300]}) \
            + targets.value_thing_points({**things[2], 'costs': [1000, 500, 300]})
        self.assertEqual(expected - 2 * (128 * 128 - len(special)) - sum(targets.value_cell_points(cell) for cell in special), 1200 + 0 + 250)
        self.assertEqual(value, expected)
        # The cache: a valid value answers when the turn is in the stamp's four-turn window (the maximum age is 0), else the map is walked again.
        cached, short = targets.value_whole(self.image, {}, [], elapsed=(1, 100, 101))
        self.assertEqual((cached, short < 1000), (77777, True))
        fresh, _ = targets.value_whole(self.image, {}, [], elapsed=(1, 100, 104))
        self.assertEqual(fresh, 2 * 128 * 128)

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

    def test_repair_duration(self):
        for key in (('ride-repair-duration', 'plan'), ('ride-repair-duration', 'routine-reading'), ('ride-repair-duration', 'flat-repair'),
                    ('ride-repair-arrival', 'plan')):
            self.check(key)

    def test_repair_duration_csharp(self):
        self.check(('ride-repair-duration', 'csharp'))

    def test_repair_duration_reading_is_sharp(self):
        # Every part matters: the scaling by the missing repair, the low byte of the truncated gauge, the division by 100, the upgrade
        # branch on ride state 2 and the product with the type record word, and the absence of a minimum.
        import dataclasses
        read = targets.repair_timer_result
        variants = {
            'flat': lambda x: read(x, flat=True),
            'no-byte': lambda x: read(x, byte=False),
            'no-division': lambda x: read(x, divide=False),
            'upgrade-by-state-1': lambda x: read(x, upgrade_state=1),
            'upgrade-flat': lambda x: {'timer': x['work'] & 0xFFFFFFFF if x['state'] == 2 else read(x)['timer']},
            'minimum-one': lambda x: {'timer': max(1, read(x)['timer'])},
            'percent-of-100': lambda x: {'timer': read({**x, 'gauge': 100.0 - x['gauge']})['timer'] if x['state'] != 2 else read(x)['timer']},
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.REPAIR_TIMER, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_repair_arrival_reading_is_sharp(self):
        import dataclasses
        read = targets.repair_arrival_reading
        variants = {
            'other-mechanic': lambda x: read({**x, 'ride_mech': x['mech_id']}),
            'walking': lambda x: read({**x, 'arrive': 0 if x['arrive'] == 2 else x['arrive']}),
            'no-maintenance-word': lambda x: read({**x, 'flag': 0}),
            'state-0-works': lambda x: read({**x, 'state': 1 if x['state'] == 0 else x['state']}),
            'no-sub-state': lambda x: {**read(x), 'sub': 0},
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.REPAIR_ARRIVAL_TARGET, plan=reading, alternatives={})
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
            self.skipTest('no .NET 11 SDK')
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

    def test_profit_ticket(self):
        for key in (('profit-ticket', 'plan'), ('profit-ticket', 'routine-reading'), ('profit-ticket-hint', 'routine-reading')):
            self.check(key)

    def test_profit_ticket_csharp(self):
        self.check(('profit-ticket', 'csharp'))

    def test_profit_ticket_old_rule_differs_only_at_equality(self):
        # F23: the former rule (>=) and the routine disagree only where the profit equals the threshold.
        [comparison] = [c for c in compare(targets.PROFIT_TARGET, self.image, DEFAULT_COUNT, csharp=False, keep=10**6) if c.side == 'plan']
        self.assertGreater(comparison.mismatches, 0)
        for example in comparison.examples:
            self.assertEqual(example['input']['profit'], example['input']['threshold'], example)
            self.assertEqual((example['original'], example['plan']), ([], [4]))

    def test_profit_ticket_reading_is_sharp(self):
        # The strict compare, the signed compare and the absence of a factor each matter, and so do the hint's pieces.
        import dataclasses
        variants = {'at-least': lambda x: targets.profit_reading(x, strict=False),
                    'unsigned': lambda x: targets.profit_reading(x, unsigned=True),
                    'hint-factor': lambda x: targets.profit_reading(x, scale=targets.f32(0.9))}
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.PROFIT_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)
        hint = {'unscaled': lambda x: targets.hint_reading(x, unscaled=True),
                'ignore-earned': lambda x: targets.hint_reading(x, ignore_earned=True),
                'at-least': lambda x: targets.hint_reading(x, strict=False)}
        for name, reading in hint.items():
            mutated = dataclasses.replace(targets.HINT_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'hint {name}')

    def test_profit_ticket_earned_flag_and_hint(self):
        # Executed: an earned profit ticket is not awarded again, and with the profit ticket open and every other ticket earned
        # it is the only award. The hint (0x13fdc) is the advisor's: 0.9 x ProfitYear, the rule's score, none once earned.
        target = targets.PROFIT_TARGET
        box = target.sandbox(self.image)
        self.assertEqual(targets.run_binary(target, box, {'profit': 15001, 'threshold': 15000}), [4])
        self.assertEqual(targets.run_binary(target, box, {'profit': 15001, 'threshold': 15000, 'earned': True}), [])
        self.assertEqual(targets.run_binary(target, box, {'profit': 15000, 'threshold': 15000}), [])
        hint, hint_box = targets.HINT_TARGET, targets.HINT_TARGET.sandbox(self.image)
        base = {'profit': 13501, 'threshold': 15000, 'mult': targets.f32(0.9), 'score': 100}
        self.assertEqual(targets.run_binary(hint, hint_box, base), 100)
        self.assertEqual(targets.run_binary(hint, hint_box, {**base, 'profit': 13500}), 0)
        self.assertEqual(targets.run_binary(hint, hint_box, {**base, 'earned': True}), 0)

    def test_all_tickets(self):
        for key in (('local-tickets', 'plan'), ('local-tickets', 'routine-reading'), ('global-tickets', 'routine-reading')):
            self.check(key)

    def test_all_tickets_csharp(self):
        for key in (('local-tickets', 'csharp'), ('global-tickets', 'csharp')):
            self.check(key)

    def test_all_tickets_reading_is_sharp(self):
        import dataclasses
        local = {'at-least': lambda x: targets.local_reading(x, strict=False),
                 'truncated-average': lambda x: targets.local_reading(x, trunc_average=True)}   # the age gate: test_local_tickets_arms_and_boundaries
        for name, reading in local.items():
            mutated = dataclasses.replace(targets.LOCAL_TICKETS_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)
        mutated = dataclasses.replace(targets.GLOBAL_TICKETS_TARGET, plan=lambda x: targets.global_reading(x, strict=False), alternatives={})
        [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
        self.assertGreater(comparison.mismatches, 0, 'global at-least')

    def test_local_tickets_arms_and_boundaries(self):
        # Executed boundaries of each arm (the shipped jungle limits): equal never wins, one above does.
        target = targets.LOCAL_TICKETS_TARGET
        box = target.sandbox(self.image)
        base = {'visitors_limit': 100, 'people_limit': 200, 'happiness_limit': 75, 'happy_people_limit': 150, 'threshold': 15000,
                'recent_limit': 350, 'months_limit': 6, 'visitors': 0, 'people': 0, 'avg': 0.0, 'built': 0, 'profit': 0, 'recent': 0, 'age': 9}
        run = lambda **kw: targets.run_binary(target, box, {**base, **kw})
        self.assertEqual(run(), [])
        self.assertEqual((run(visitors=100), run(visitors=101)), ([], [0]))
        self.assertEqual(run(people=200), [])   # 200 is not above 200 (the happiness arm needs the average too)
        self.assertEqual(run(people=201), [1])
        # happiness: the single average above single(75) and the guest count above 150 (nobody is filtered by happiness)
        self.assertEqual(run(avg=75.0, people=151), [])
        self.assertEqual(run(avg=targets.f32(75.01), people=150), [])
        self.assertEqual(run(avg=targets.f32(75.01), people=151), [2])
        self.assertEqual(run(avg=targets.f32(75.5), people=151), [2], 'the original compares the fraction: 75.5 > 75')
        # all researched and built: the low byte of the result
        self.assertEqual((run(built=1), run(built=256), run(built=255)), ([3], [], [3]))
        self.assertEqual((run(profit=15000), run(profit=15001)), ([], [4]))
        # recent visitors: the counter above 350 and the park age in months above 6 (equal ages do not count)
        self.assertEqual((run(recent=350), run(recent=351)), ([], [5]))
        self.assertEqual((run(recent=351, age=6), run(recent=351, age=7), run(recent=351, age=5)), ([], [5], []))
        glob, glob_box = targets.GLOBAL_TICKETS_TARGET, targets.GLOBAL_TICKETS_TARGET.sandbox(self.image)
        limits = [105, 90, 50, 2000]
        for i, limit in enumerate(limits):
            for stat, expected in ((limit, []), (limit + 1, [i])):
                stats = [0, 0, 0, 0]
                stats[i] = stat
                self.assertEqual(targets.run_binary(glob, glob_box, {'global_limits': limits, 'global_stats': stats}), expected, (i, stat))

    def test_profit_ticket_stubs_are_checked(self):
        # The bank lookup must see the game pointer; a wrong one aborts the run instead of returning the bank.
        target = targets.PROFIT_TARGET
        box = target.sandbox(self.image)
        box.reset()
        targets.profit_setup(box, {'profit': 1, 'threshold': 0})
        box.write(targets.toc_pointer(box, targets.PROFIT_GAME_VARIABLE), 'I', box.alloc(16))
        with self.assertRaises(AssertionError):
            box.call(targets.LOCAL_TICKETS, {3: targets.PROFIT_STATE.this})

    def test_shop_sale_litter(self):
        for key in (('shop-sale-litter', 'plan'), ('shop-sale-litter', 'routine-reading'), ('guest-litter-trigger', 'routine-reading')):
            self.check(key)

    def test_shop_sale_litter_csharp(self):
        self.check(('shop-sale-litter', 'csharp'))

    def test_shop_sale_litter_rule_differs_only_where_the_meter_clamps(self):
        # ECON-020: the old rule (store + LitterEffect) agrees with the routine wherever the meter stays inside 0..100; every
        # mismatch is a sum outside that range (a NaN meter agrees: the clamp lets it through). C# now clamps (pinned AGREE).
        for side in ('plan',):
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
    def test_staff_vitals(self):
        for key in (('staff-vitals', 'plan'), ('staff-vitals', 'routine-reading')):
            self.check(key)

    def test_staff_vitals_csharp(self):
        self.check(('staff-vitals', 'csharp'))

    def test_staff_rest_seek(self):
        for key in (('staff-rest-seek', 'plan'), ('staff-rest-seek', 'routine-reading')):
            self.check(key)

    def test_staff_rest_seek_csharp(self):
        self.check(('staff-rest-seek', 'csharp'))

    def test_staff_strike_grievance(self):
        for key in (('staff-strike-grievance', 'plan'), ('staff-strike-grievance', 'routine-reading')):
            self.check(key)

    def test_staff_strike_grievance_csharp(self):
        self.check(('staff-strike-grievance', 'csharp'))

    def test_staff_strike_month(self):
        for key in (('staff-strike-month', 'plan'), ('staff-strike-month', 'routine-reading')):
            self.check(key)

    def test_staff_strike_month_csharp(self):
        self.check(('staff-strike-month', 'csharp'))

    def _sharp(self, target, variants, side='plan'):
        import dataclasses
        for name, reading in variants.items():
            mutated = dataclasses.replace(target, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_staff_constants(self):
        # The constants the vitals reading hard-codes are the binary's: the clamp 100.0 and 0.0, 0.012, 0.005, 0.01 (binary64), the single
        # 0.025 and the handymen's single 0.2 (TOC -10696, -10692, -10736, -10744, -10760, -10752, -10680).
        import struct
        memory, toc = self.image.memory, self.image.toc
        single = lambda d: struct.unpack('>f', memory.read(toc + d, 4))[0]
        double = lambda d: struct.unpack('>d', memory.read(toc + d, 8))[0]
        self.assertEqual((single(-10696), single(-10692), single(-10752), single(-10680)), (100.0, 0.0, targets.f32(0.025), targets.f32(0.2)))
        self.assertEqual((double(-10736), double(-10744), double(-10760)), (0.012, 0.005, 0.01))
        self.assertEqual((6034 << 32) | 0xF8648000, targets.STAFF_MONTH)

    def test_staff_vitals_reading_is_sharp(self):
        # Walking and working differ, the grade enters as 6 - grade, the clamp matters, the rest threshold is the byte 100 and the
        # rest-level test is strict (below), each bringing mismatches back when changed.
        read = targets.vitals_reading
        swap = {'walk': 'work', 'work': 'walk'}
        self._sharp(targets.VITALS, {
            'walk-as-work': lambda x: read({**x, 'op': swap.get(x['op'], x['op'])}),
            'grade': lambda x: read({**x, 'grade': x['grade'] + 1}) if x['op'] in swap else read(x),
            'rates-swapped': lambda x: read({**x, 'rate_e': x['rate_h'], 'rate_h': x['rate_e']}),
            'below-or-equal': lambda x: {**read(x), 'below': int(targets.vital_byte(x['energy']) <= x['level'])} if x['op'] == 'below' else read(x),
            'finished-at-99': lambda x: {**read(x), 'finished': int(targets.vital_byte(min(100.0, max(0.0, targets.f32(x['rate_e'] + x['energy'])))) >= 99)}
            if x['op'] == 'rest' else read(x),
        })

    def test_staff_rest_seek_reading_is_sharp(self):
        read = targets.seek_reading
        self._sharp(targets.SEEK, {
            'strict': lambda x: read(x, strict=True),
            'always-hit': lambda x: read(x, always_hit=True),
            'hit-sign': lambda x: read({**x, 'hit': -x['hit']}),
            'found-ignored': lambda x: read({**x, 'found': 0}),
            'level': lambda x: read({**x, 'level': x['level'] + 1}),
        })

    def test_staff_grievance_reading_is_sharp(self):
        read = targets.grievance_reading
        self._sharp(targets.GRIEVANCE, {
            'count-3': lambda x: read(x, count=2),
            'count-5': lambda x: read(x, count=4),
            'limit-14': lambda x: read(x, limit=14),
            'limit-16': lambda x: read(x, limit=16),
            'fatigue-only': lambda x: read(x, happiness=False),
            'happiness-only': lambda x: read(x, fatigue=False),
            'litter': lambda x: read(x, litter=False),
            'byte-wrap': lambda x: read(x, byte_wrap=False),
            'forced': lambda x: read({**x, 'forced': 0}),
        })

    def test_staff_strike_month_reading_is_sharp(self):
        read = targets.month_reading
        self._sharp(targets.STRIKE_MONTH, {
            'age-23': lambda x: read(x, age_gate=23),
            'age-25': lambda x: read(x, age_gate=25),
            'active-not-ended': lambda x: read(x, drop='end'),
            'guests-ignored': lambda x: read({**x, 'months': [{**step, 'guests': 0} for step in x['months']]}),
            'closed-ignored': lambda x: read({**x, 'months': [{**step, 'closed': 0} for step in x['months']]}),
            'turn-guard': lambda x: read({**x, 'ticks': [0] * 5}),
        })

    def test_staff_grievance_rejects_a_role_outside_the_five(self):
        # Roles 5 and -1 assert (message 784) and give 0; OpenTPW's StaffType has no such value, so they are not in the comparison inputs.
        target = targets.GRIEVANCE
        sandbox = target.sandbox(self.image)
        base = {'forced': 0, 'roles': [[(0.0, 0.0)] * 5] * 5, 'litter_den': 10, 'litter_num': 9}
        for role in (5, -1):
            self.assertEqual(targets.run_binary(target, sandbox, {**base, 'role': role}),
                             {'strike': 0, 'ret': 0, 'events': [('assert', 784)]})
            self.assertEqual(targets.grievance_reading({**base, 'role': role}), {'strike': 0, 'ret': 0, 'events': [('assert', 784)]})

    def test_staff_strike_story(self):
        # The executed sequence for two roles with unhappy staff (four to five members at 0): the first month end is a warning, the second a
        # strike that the third month end only ends (no new strike), the fourth strikes again at level 3, and so on up to level 4.
        target = targets.STRIKE_MONTH
        sandbox = target.sandbox(self.image)
        unhappy, content = [(0.0, 0.0)] * 5, [(100.0, 100.0)] * 5
        months = [{'turn': 1000 * (i + 1), 'closed': 0, 'guests': 0, 'stamp': 30 * targets.STAFF_MONTH} for i in range(6)]
        x = {'roles': [unhappy, unhappy, content, content, content], 'forced': 0, 'levels': [0] * 5, 'flags': [0] * 5, 'ticks': [0] * 5,
             'litter_den': 10, 'litter_num': 0, 'months': months}
        out = targets.run_binary(target, sandbox, x)
        self.assertEqual([levels[:2] for levels in out['levels']], [[1, 1], [2, 2], [2, 2], [3, 3], [3, 3], [4, 4]])
        self.assertEqual([flags[:2] for flags in out['flags']], [[0, 0], [1, 1], [0, 0], [1, 1], [0, 0], [1, 1]])
        self.assertEqual(out['messages'], [[(0, 0), (1, 0)], [(0, 2), (1, 2)], [], [(0, 2), (1, 2)], [], [(0, 2), (1, 2)]])
        # Contented staff: nothing happens and the roles are marked as considered.
        calm = targets.run_binary(target, sandbox, {**x, 'roles': [content] * 5})
        self.assertEqual((calm['levels'][-1], calm['flags'][-1], calm['messages'][-1]), ([0] * 5, [0] * 5, []))

    def test_staff_vitals_shipped_turns(self):
        # One turn of a grade 0 member at full vitals: walking 100 - 0.072 and 100 - 0.03, working 100 - 0.15 and 100 - 0.06; a grade 4
        # member walking loses 0.024 and 0.01, resting a grade 0 member at 99.9 energy ends the rest (0.2 per turn).
        target = targets.VITALS
        sandbox = target.sandbox(self.image)
        f32, fbits = targets.f32, targets.fbits
        base = {'type': 6, 'level': 1, 'rate_e': 0.2, 'rate_h': 1.0}
        walk = targets.run_binary(target, sandbox, {**base, 'op': 'walk', 'grade': 0, 'energy': 100.0, 'happiness': 100.0})
        self.assertEqual((walk['energy'], walk['happiness']), (fbits(f32(100.0 - f32(0.012 * 6))), fbits(f32(100.0 - f32(0.005 * 6)))))
        self.assertAlmostEqual(targets.from_bits(walk['energy']), 99.928, places=3)
        work = targets.run_binary(target, sandbox, {**base, 'op': 'work', 'grade': 0, 'energy': 100.0, 'happiness': 100.0})
        self.assertAlmostEqual(targets.from_bits(work['energy']), 99.85, places=3)
        self.assertAlmostEqual(targets.from_bits(work['happiness']), 99.94, places=3)
        rest = targets.run_binary(target, sandbox, {**base, 'op': 'rest', 'grade': 0, 'energy': f32(99.9), 'happiness': 50.0})
        self.assertEqual((rest['finished'], rest['events']), (1, [('release', True), ('next-6', True)]))

    # ---------------------------------------------------------------- F30: the park calendar and the cadence of park turns

    def test_calendar(self):
        for key in (('calendar-date', 'plan'), ('calendar-update', 'plan'), ('calendar-update', 'routine-reading'),
                    ('calendar-first-update', 'plan'), ('calendar-first-update', 'routine-reading')):
            self.check(key)

    def test_calendar_csharp(self):
        self.check(('calendar-date', 'csharp'))
        self.check(('calendar-update', 'csharp'))

    def test_calendar_dates(self):
        # The date is the 2000-01-01 start plus floor(turn x 15000 / 4) seconds: a day change at turn 24 (86,400 s lie in turn 23.04),
        # 2000 is a leap year (29 February exists), 2100 is not, and the leap year has 8,433 turns before the first turn of 2001.
        target = targets.CALENDAR_DATE
        sandbox = target.sandbox(self.image)
        date = lambda turn: targets.run_binary(target, sandbox, {'turn': turn, 'rate': 15000})['date']
        self.assertEqual([date(0), date(23), date(24)], [[2000, 1, 1, 0, 0, 0], [2000, 1, 1, 23, 57, 30], [2000, 1, 2, 1, 0, 0]])
        self.assertEqual([date(8432), date(8433)], [[2000, 12, 31, 23, 20, 0], [2001, 1, 1, 0, 22, 30]])

        def last_before_march(year: int) -> list:
            seconds = (datetime.datetime(year, 3, 1) - targets.CAL_EPOCH).total_seconds()
            turn = -(-int(seconds) // 3750)
            return [date(turn - 1)[:3], date(turn)[:3]]
        self.assertEqual(last_before_march(2000), [[2000, 2, 29], [2000, 3, 1]])
        self.assertEqual(last_before_march(2004), [[2004, 2, 29], [2004, 3, 1]])
        self.assertEqual(last_before_march(2100), [[2100, 2, 28], [2100, 3, 1]])
        self.assertEqual(last_before_march(2400), [[2400, 2, 29], [2400, 3, 1]])

    def test_calendar_rate_floors_the_seconds(self):
        # A saved clock rate other than 15000: the product turn x rate is a 64-bit word and the division by 4 floors (5 turns at
        # rate 3 are 3 seconds, not 4); the largest turn and rate products stay in 64 bits. The reading with rounded seconds differs.
        target = targets.CALENDAR_DATE
        sandbox = target.sandbox(self.image)
        rounded = 0
        for x in targets.CAL_ODD_RATES:
            original = targets.run_binary(target, sandbox, x)
            self.assertEqual(original, targets.calendar_date_plan(x), x)
            if not x['stamp_only'] and original != targets.calendar_date_rounded(x):
                rounded += 1
        self.assertGreater(rounded, 0)
        self.assertEqual(targets.run_binary(target, sandbox, {'turn': 5, 'rate': 3})['date'][5], 3)

    def test_calendar_update_reading_is_sharp(self):
        # The constructed calendar's first update is what the stored fields (0, -1, 0) say: a changed initial month, day or year, or
        # dropping the month hooks, makes the reading disagree with the executed routine.
        variants = {
            'month-minus-one': lambda x: targets.calendar_update_plan(x, init=(-1, -1, 0)),
            'day-one': lambda x: targets.calendar_update_plan(x, init=(0, 1, 0)),
            'year-2000': lambda x: targets.calendar_update_plan(x, init=(0, -1, 2000)),
            'no-hooks': lambda x: targets.calendar_update_plan(x, init=(0, -1, 0), hooks=False),
            'synchronised': lambda x: targets.calendar_update_plan(x),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.CALENDAR_FIRST_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, 80, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_calendar_first_update_examples(self):
        # The first update of a new calendar at the first park turn (3,750 s: 01:02:30 on 1 January 2000) posts a day and a year message
        # and no month message; the date given with the day message is second, minute, hour, day, month - 1 and year - 1900.
        target = targets.CALENDAR_FIRST_TARGET
        sandbox = target.sandbox(self.image)
        first = targets.run_binary(target, sandbox, {'start': 1, 'count': 2, 'state': 'fresh'})
        self.assertEqual(first['steps'], [[['post', 'day'], ['notify', [30, 2, 1, 1, 0, 100]], ['post', 'year']], []])
        self.assertEqual(first['state'], [[0, 1, 2000], [0, 1, 2000]])
        # Started in February (turn 715 is 00:47:30 on 1 February) all three messages come, in the order day, month (with its three
        # hooks, the new month 2), year.
        february = targets.run_binary(target, sandbox, {'start': 715, 'count': 1, 'state': 'fresh'})
        self.assertEqual(february['steps'][0], [['post', 'day'], ['notify', [30, 47, 0, 1, 1, 100]], ['post', 'month'],
                                                ['hook', 0x15BBD8, 2], ['hook', 0x151698, 2], ['hook', 0x155234, 2], ['post', 'year']])
        # Synchronised (the state of a loaded or running calendar), nothing is posted on a turn with the same date.
        synced = targets.run_binary(targets.CALENDAR_UPDATE_TARGET, targets.CALENDAR_UPDATE_TARGET.sandbox(self.image),
                                    {'start': 1, 'count': 2, 'state': 'synced'})
        self.assertEqual(synced['kinds'], [[], []])

    def test_calendar_update_at_the_boundaries(self):
        # One message of each kind per boundary, on the first turn whose date has the new field: 31 January -> 1 February is turn 715,
        # the first turn of 2001 turn 8,433 where all three come; a 28-day February of 2100 does not skip to a 29th.
        target = targets.CALENDAR_UPDATE_TARGET
        sandbox = target.sandbox(self.image)
        run = lambda start, count: targets.run_binary(target, sandbox, {'start': start, 'count': count, 'state': 'synced'})['kinds']
        self.assertEqual(run(714, 2), [[], ['day', 'month']])
        self.assertEqual(run(8432, 3), [[], ['day', 'month', 'year'], []])
        march_2100 = -(-int((datetime.datetime(2100, 3, 1) - targets.CAL_EPOCH).total_seconds()) // 3750)
        self.assertEqual(run(march_2100 - 1, 2), [[], ['day', 'month']])

    def test_park_turn_cadence(self):
        self.check(('park-turn-cadence', 'plan'))
        self.check(('park-turn-cadence', 'routine-reading'))

    def test_park_turn_cadence_reading_is_sharp(self):
        variants = {
            'substep-30': lambda x: targets.cadence_model(x, step=30),
            'substep-32': lambda x: targets.cadence_model(x, step=32),
            'turn-every-7': lambda x: targets.cadence_model(x, per_turn=7),
            'turn-every-16': lambda x: targets.cadence_model(x, per_turn=16),
            'turn-cap-2': lambda x: targets.cadence_model(x, cap=2),
            'turn-cap-4': lambda x: targets.cadence_model(x, cap=4),
            'backlog-1500': lambda x: targets.cadence_model(x, drop=1500),
            'backlog-2500': lambda x: targets.cadence_model(x, drop=2500),
            'even-every-3': lambda x: targets.cadence_model(x, per_even=3),
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.CADENCE_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, 120, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_park_turns_per_real_second_by_scale(self):
        # The clock multiplies the real milliseconds by its scale (1.0 from the constructor; the speed keys step it by 1.25 within 0.25..2):
        # 100 s of real time in frames of any length is 403 turns (4.03 a second, a game day of 5.7 s) at the default scale, and the
        # scale shows in the turn rate; the substeps are 31 ms of scaled time each.
        target = targets.CADENCE_TARGET
        sandbox = target.sandbox(self.image)
        expected = {1.0: 403, 2.0: 806, 0.25: 100, 1.25: 504, 1.5625: 630, 1.953125: 787, 0.8: 322}
        for scale, turns in expected.items():
            for frame in (16, 17, 33):
                for mode in (0, 1, 2):
                    x = {'scale': scale, 'mode': mode, 'flags': (False, False), 'raw0': 0, 'counter0': 0, 'gaps': [frame] * (100000 // frame)}
                    calls = targets.run_binary(target, sandbox, x)['calls']
                    self.assertEqual(sum(call['turns'] for call in calls), turns, (scale, frame, mode))
        self.assertAlmostEqual(23.04 / (403 / 100), 5.717, places=2)

    def test_golden_ticket_cadence(self):
        for side in ('plan', 'plan-signed', 'plan-tickets-before-calendar'):
            self.check(('golden-ticket-cadence', side))

    def test_golden_ticket_cadence_csharp(self):
        self.check(('golden-ticket-cadence', 'csharp'))

    def test_golden_ticket_cadence_reading_is_sharp(self):
        variants = {
            'every-50': targets.golden_every(50),
            'every-99': targets.golden_every(99),
            'every-200': targets.golden_every(200),
            'any-game-type': targets.golden_any_mode,
        }
        for name, reading in variants.items():
            mutated = dataclasses.replace(targets.GOLDEN_TARGET, plan=reading, alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, 120, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_golden_ticket_examples(self):
        # Turn 100 of a Full Simulation world (game type 0) runs the bank, the staff pool, the calendar update and the three ticket checks, in
        # that order; turn 99 and game types 1, 2 and 3 run no check. The remainder is unsigned: 2^32 - 96 is a multiple of 100, -96 is not.
        target = targets.GOLDEN_TARGET
        sandbox = target.sandbox(self.image)
        run = lambda mode, turns: targets.run_binary(target, sandbox, {'mode': mode, 'turns': turns})['calls']
        everything = ['bank', 'pool', 'calendar', 'ticket-1', 'ticket-2', 'ticket-3']
        self.assertEqual(run(0, [99, 100, 101]), [['bank', 'pool', 'calendar'], everything, ['bank', 'pool', 'calendar']])
        for mode in (1, 2, 3, -1):
            self.assertEqual(run(mode, [100]), [['bank', 'pool', 'calendar']], mode)
        self.assertEqual(run(0, [2 ** 32 - 96, 2 ** 32 - 100]), [everything, ['bank', 'pool', 'calendar']])
        # About 4.3 game days apart: 100 turns x 3750 s = 4.34 days, 24.8 s of real time at the default clock scale.
        self.assertAlmostEqual(100 * 3750 / 86400, 4.34, places=2)
        self.assertAlmostEqual(100 * 0.248, 24.8)

    def test_speed_keys_step_the_scale_by_1_25_within_0_25_and_2(self):
        target = targets.SPEED_TARGET
        sandbox = target.sandbox(self.image)
        run = lambda scale, ops: [(op, value, r3) for op, value, r3 in
                                  targets.run_binary(target, sandbox, {'scale': scale, 'raw0': 0, 'ops': ops})['scales']]
        self.assertEqual([value for _, value, _ in run(1.0, ['faster'] * 6)], [1.25, 1.5625, 1.953125, 2.0, 2.0, 2.0])
        self.assertEqual([round(value, 6) for _, value, _ in run(2.0, ['slower'] * 12)],
                         [1.6, 1.28, 1.024, 0.8192, 0.65536, 0.524288, 0.41943, 0.335544, 0.268435, 0.25, 0.25, 0.25])
        self.assertEqual([value for _, value, _ in run(0.25, ['reset', 'faster'])], [1.0, 1.25])
        # Every key callback returns 1 (handled) and the clamp applies to a scale outside the range too: the direct setter 0x127c40 stores
        # any value, and the next step brings it back inside.
        self.assertEqual({r3 for _, _, r3 in run(1.0, ['faster', 'slower', 'reset'])}, {1})
        self.assertEqual(run(4.0, ['faster'])[0][1], 2.0)
        self.assertEqual(run(3.0, ['slower'])[0][1], 2.0)
        self.assertEqual(run(0.1, ['faster'])[0][1], 0.25)

    def test_speed_scale_multiplies_the_scheduler_clock(self):
        # The scheduler adds each raw gap times the scale that is current at the read: a scale change applies to the whole gap since the previous
        # read. At scale 1 a second is 1000 ms; after one faster key 1000 raw ms are 1250; at the highest scale 2000.
        target = targets.SPEED_TARGET
        sandbox = target.sandbox(self.image)
        run = lambda scale, ops: targets.run_binary(target, sandbox, {'scale': scale, 'raw0': 0, 'ops': ops})['reads']
        self.assertEqual(run(1.0, [1000, 'faster', 1000, 'faster', 1000, 'faster', 1000, 'faster', 1000]), [1000, 2250, 3812, 5765, 7765])
        self.assertEqual(run(2.0, [1000, 1000]), [2000, 4000])
        self.assertEqual(run(0.25, [1000, 1000]), [250, 500])
        self.assertEqual(run(4.0, [1000, 'faster', 1000]), [4000, 6000], "a scale above 2.0 (the direct setter) runs until a key steps it down")
        # A raw counter that wraps: the gap is an unsigned 32-bit difference.
        self.assertEqual(targets.run_binary(target, sandbox, {'scale': 1.0, 'raw0': 4294967000, 'ops': [100, 100, 100]})['reads'], [100, 200, 300])

    def test_scaled_clock_callers(self):
        # The consumers of the scaled clock 0x10e844 and of the unscaled 0x10e864, as call sites in the code section (a static scan of the
        # branch-and-link instructions; nothing is executed): the scale reaches the world callback, the RSE interpreter (GETTIME 0xaf6d8, WAIT
        # 0xb0674, WAITABS 0xb0764, SETTIMER 0xb1fec, GETTIMER 0xb200c), the board bounce code and the animation updater 0xa6f70, which
        # samples both clocks and picks one per channel (flag 0x40). 60 call sites read the scaled clock and 11 the unscaled one.
        image = self.image
        words = [image.memory.u32(image.code(pc)) for pc in range(0, 0x1EBEB0, 4)]
        calls = {0x10E844: [], 0x10E864: []}
        for index, word in enumerate(words):
            if word >> 26 == 18 and word & 3 == 1:       # bl, relative
                offset = word & 0x03FFFFFC
                offset -= 0x04000000 if offset & 0x02000000 else 0
                if index * 4 + offset in calls:
                    calls[index * 4 + offset].append(index * 4)
        self.assertEqual((len(calls[0x10E844]), len(calls[0x10E864])), (60, 11))
        for site in (0xA6FA0, 0xAF6D8, 0xB0674, 0xB0764, 0xB1FEC, 0xB200C, 0xAE03C, 0xAD350, 0x1C22B4, 0x1C1274):
            self.assertIn(site, calls[0x10E844], hex(site))
        self.assertIn(0xA6FD8, calls[0x10E864])

    def test_park_turn_cadence_frames(self):
        for side in ('plan', 'routine-reading'):
            self.check(('park-turn-cadence-frames', side))

    def test_park_turn_cadence_frames_csharp(self):
        self.check(('park-turn-cadence-frames', 'csharp'))

    def test_park_turn_cadence_backlog_and_flags(self):
        # A gap of 2000 ms or more (or a stall of 5000 ms: 3000 ms are dropped first) runs 65 substeps and 3 park turns and ends 15 ms
        # ahead; the rest of the backlog is lost. Flag 8 clear and flag 1 set (the world's exclusion) skips the substep bodies.
        target = targets.CADENCE_TARGET
        sandbox = target.sandbox(self.image)
        base = {'scale': 1.0, 'mode': 0, 'flags': (False, False), 'raw0': 0, 'counter0': 0}
        for gap in (2000, 5000):
            [call] = targets.run_binary(target, sandbox, {**base, 'gaps': [gap]})['calls']
            self.assertEqual((call['scripts'], call['turns'], call['previous'] - call['now']), (65, 3, 15), gap)
        [call] = targets.run_binary(target, sandbox, {**base, 'gaps': [248]})['calls']
        self.assertEqual((call['scripts'], call['turns'], call['previous'], call['counter']), (8, 1, 248, 8))
        excluded = targets.run_binary(target, sandbox, {**base, 'flags': (False, True), 'gaps': [248] * 3})['calls']
        self.assertEqual([(call['scripts'], call['turns'], call['counter']) for call in excluded], [(0, 0, 8), (0, 0, 16), (0, 0, 24)])
        overridden = targets.run_binary(target, sandbox, {**base, 'flags': (True, True), 'gaps': [248]})['calls']
        self.assertEqual(overridden[0]['turns'], 1)

    def test_attraction_choice(self):
        for key in (('attraction-score', 'plan'), ('attraction-score', 'routine-reading'), ('ride-outcome', 'plan'),
                    ('ride-outcome', 'routine-reading'), ('attraction-choice', 'plan'), ('attraction-choice', 'routine-reading')):
            self.check(key)

    def test_attraction_choice_csharp(self):
        for key in (('attraction-score', 'csharp'), ('ride-outcome', 'csharp'), ('attraction-choice', 'csharp')):
            self.check(key)

    def test_attraction_score_reading_is_sharp(self):
        # Every part of the score matters: dropping or changing any of these brings mismatches back. The first run is the routine reading itself.
        import dataclasses
        variants = {
            'active-weights-only': dict(active_only=True), 'distance-divisor-400': dict(distance_divisor=400),
            'distance-divisor-500': dict(distance_divisor=500), 'no-cell-modifier': dict(no_modifier=True),
            'queue-always': dict(always_queue=True), 'queue-clamped': dict(clamp_queue=True),
            'excitement-weight-ungated': dict(ungated_excitement=True), 'relief-ungated': dict(always_relief=True),
            'no-new-ride': dict(no_new=True), 'no-indoor-rain': dict(no_rain=True), 'no-golden-ticket': dict(no_golden=True),
            'no-upgrade-cost': dict(no_expensive=True), 'no-used-history': dict(no_used=True), 'used-all-matches': dict(used_all=True),
            'no-temporary-history': dict(no_temp=True), 'temporary-first-match': dict(temp_first=True), 'no-same-type-exit': dict(no_type_exit=True),
        }
        for name, variant in variants.items():
            mutated = dataclasses.replace(targets.targets_guests.SCORE_TARGET, plan=lambda x, v=variant: targets.targets_guests.score_reading(x, **v),
                                          alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, name)

    def test_ride_outcome_and_choice_readings_are_sharp(self):
        import dataclasses
        g = targets.targets_guests
        for name, variant in {'ungated': dict(ungated=True), 'inclusive-bands': dict(inclusive=True), 'penalty': dict(penalty=True),
                              'linear-hunger': dict(linear_hunger=True)}.items():
            mutated = dataclasses.replace(g.OUTCOME_TARGET, plan=lambda x, v=variant: g.outcome_reading(x, **v), alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'outcome {name}')
        for name, variant in {'threshold-0': dict(threshold=0), 'threshold-11': dict(threshold=11), 'ties-never': dict(ties='never'),
                              'ties-always': dict(ties='always')}.items():
            mutated = dataclasses.replace(g.CHOICE_TARGET, plan=lambda x, v=variant: g.choice_reading(x, **v), alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'choice {name}')

    def test_attraction_score_tables(self):
        # The thirst/hunger table is exactly trunc(n^2 * sqrt(e / 10)) for need and effect in tens (what AttractionScore.NeedMatch computes),
        # the toilet/illness curve starts with nine zeros and ends at 100; the table bytes themselves are read here, never stored.
        import math
        blob = targets.targets_guests.score_tables()['blob']
        for effect in range(11):
            for need in range(11):
                self.assertEqual(blob[effect * 11 + need], int(need * need * math.sqrt(effect / 10)), (effect, need))
        curve = blob[0x4129B - 0x41222:0x4129B - 0x41222 + 21]
        self.assertEqual(list(curve), [0] * 9 + [1, 2, 5, 10, 20, 30, 40, 50, 60, 75, 90, 100])

    def test_guest_needs(self):
        for key in (('guest-needs-turn', 'plan'), ('guest-needs-turn', 'routine-reading'), ('guest-constructor', 'plan'),
                    ('guest-constructor', 'routine-reading')):
            self.check(key)

    def test_guest_needs_csharp(self):
        for key in (('guest-needs-turn', 'csharp'), ('guest-constructor', 'csharp'), ('shop-sale-litter', 'csharp-guest-meters')):
            self.check(key)

    def test_guest_needs_readings_are_sharp(self):
        import dataclasses
        g = targets.targets_guests
        for name, variant in {'every-turn': dict(every_turn=True), 'no-exit-count': dict(no_exit=True), 'growth-every-8': dict(growth_mask=7),
                              'growth-every-32': dict(growth_mask=31), 'hunger-step-1': dict(hunger_step=1.0), 'hunger-step-3': dict(hunger_step=3.0),
                              'no-full-penalty': dict(no_penalty=True), 'penalty-from-100': dict(penalty_at_least=True),
                              'no-history-decay': dict(no_decay=True)}.items():
            mutated = dataclasses.replace(g.NEEDS_TARGET, plan=lambda x, v=variant: g.needs_reading(x, **v), alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'needs {name}')
        for name, variant in {'happiness-60': dict(happy60=True), 'thirst-modulus-40': dict(thirst_modulus=40),
                              'toilet-modulus-50': dict(toilet50=True)}.items():
            mutated = dataclasses.replace(g.CTOR_TARGET, plan=lambda x, v=variant: g.ctor_reading(x, **v), alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'constructor {name}')

    def test_guest_exit_counter_counts_one_per_four_park_turns(self):
        # F25/F26 unit of the exit counter, executed over 400 consecutive park turns: it loses exactly one per needs phase, that is one per four turns
        # (0.992 s at 248 ms a turn, the .sam's "seconds"), whatever the id; hunger and thirst rise only for a guest whose id is a multiple of four.
        g = targets.targets_guests
        target = g.NEEDS_TARGET
        sandbox = target.sandbox(self.image)
        for ident in (4, 5, 6, 7, 8, 10001):
            x = {'turn': 1000, 'id': ident, 'gx': 10, 'gy': 10, 'happy': 50.0, 'thirst': 0.0, 'hunger': 0.0, 'toilet': 0.0, 'ill': 0.0, 'exit': 120,
                 'cells': [0, 0, 0], 'bonus': 0, 'state': 6, 'prev': 6, 'predicate': 0, 'countdown': 0, 'rng': 1, 'temp': [0, 0, 0, 0]}
            for turn in range(1000, 1400):
                result = targets.run_binary(target, sandbox, {**x, 'turn': turn})
                x.update({k: result[k] for k in ('happy', 'thirst', 'hunger', 'toilet', 'ill', 'exit', 'bonus', 'temp')})
            self.assertEqual(x['exit'], 120 - 100, ident)
            growth = 50.0 if ident % 4 == 0 else 0.0   # 25 turns that are multiples of 16 in 1000..1399, each +2
            self.assertEqual((x['hunger'], x['thirst'], x['toilet']), (growth, growth, growth / 2), ident)

    def test_guest_constructor_division_by_zero_is_undefined(self):
        # ExitLevelVar 0 would make the first divw divide by zero: the architecture leaves it undefined, the oracle reports UNDEFINED.
        g = targets.targets_guests
        x = {'rng': 5, 'turn': 0, 'id': 4, 'exit': 120, 'exit_var': 0, 'cash_pc': 15, 'prank': 5, 'types': [[80, 300, 40]]}
        self.assertEqual(targets.run_binary(g.CTOR_TARGET, g.CTOR_TARGET.sandbox(self.image), x), targets.UNDEFINED)
        self.assertEqual(g.ctor_reading(x), targets.UNDEFINED)

    def test_guest_exit_test(self):
        for key in (('guest-exit-test', 'plan'), ('guest-exit-test', 'routine-reading')):
            self.check(key)

    def test_guest_exit_test_csharp(self):
        self.check(('guest-exit-test', 'csharp'))

    def test_guest_exit_test_reading_is_sharp(self):
        import dataclasses
        g = targets.targets_guests
        for name, variant in {'happiness-at-most-10': dict(threshold=10), 'counter-at-most-0': dict(at_most=True), 'no-closing': dict(no_closing=True),
                              'no-byte': dict(no_byte=True)}.items():
            mutated = dataclasses.replace(g.EXIT_TARGET, plan=lambda x, v=variant: g.exit_reading(x, **v), alternatives={})
            [comparison] = [c for c in compare(mutated, self.image, DEFAULT_COUNT, csharp=False) if c.side == 'plan']
            self.assertGreater(comparison.mismatches, 0, f'exit {name}')

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


@unittest.skipUnless(ROOT and bridge_module.dotnet(), 'requires the Feral bin directory and a .NET 11 SDK')
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
        ('walk-boost', 'csharp', 'source/OpenTPW/World/Guests/GuestNavigation.cs',
         '(amount & 0xFF) * 6 / Divisor', '(amount & 0xFF) * 5 / Divisor'),
        ('queue-join-gate', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         'return difference < 0 ? QueueJoinResult.NotExcitingEnough : QueueJoinResult.TooExciting;',
         'return difference > 0 ? QueueJoinResult.NotExcitingEnough : QueueJoinResult.TooExciting;'),
        ('ride-repair-duration', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         '(100 - (QueueJoinRules.Truncate( (float)stateOfRepair ) & 0xFF)) / 100', '(100 - (QueueJoinRules.Truncate( (float)stateOfRepair ) & 0xFF)) / 101'),
        ('queue-failure-predicate', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         '(Truncate( wearGauge ) & 0xFF) < WornGauge', '(Truncate( wearGauge ) & 0xFF) <= WornGauge'),
        ('queue-join-difference', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         'ratio > 1.25f ? 1.25f : ratio', 'ratio > 1.5f ? 1.5f : ratio'),
        ('queue-join-difference', 'csharp', 'source/OpenTPW/World/Guests/QueueJoinRules.cs',
         'Math.Clamp( terms.CostOfGoods - terms.PricePerUse, 0, 100 )', 'Math.Clamp( terms.CostOfGoods - terms.PricePerUse, 0, 99 )'),
        ('park-points', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         '!item.ScriptBroken && item.IsOpen && item.QueueHasRoom\n', '!item.ScriptBroken && item.QueueHasRoom\n'),
        ('loan-offers', 'csharp', 'source/OpenTPW/Economy/ParkLedger.cs',
         'return TruncateUnsigned( amount * power / months );', 'return TruncateUnsigned( amount * power / months + 1.0 );'),
        ('loan-term', 'csharp', 'source/OpenTPW/Economy/ParkLedger.cs',
         'account.MonthlyRepayment);\n\t}', 'account.MonthlyRepayment + 1);\n\t}'),
        ('loan-payoff', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'Ledger.ChargeLoanInterest( LoanMath.PayoffInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount ) );',
         'Ledger.ChargeLoanInterest( 0 );'),
        ('staff-training-split', 'csharp', 'source/OpenTPW/Economy/ParkStaff.cs',
         'spent += share;', 'spent += share / 2;'),
        ('object-scrap-value', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'return unchecked( (uint)((int)info.Upgrades[0].CostOfUpgrade * ScrapPercent( item )) ) / 100;',
         'return info.Upgrades.Take( Math.Min( item.Level, info.Upgrades.Count - 1 ) + 1 ).Sum( upgrade => upgrade.CostOfUpgrade ) * ScrapPercent( item ) / 100;'),
        ('object-scrap-value', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'return unchecked( (uint)((int)info.Upgrades[0].CostOfUpgrade * ScrapPercent( item )) ) / 100;',
         'return info.Upgrades[0].CostOfUpgrade * ScrapPercent( item ) / 100;'),
        ('object-scrap-value', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'age / (30 * ParkCalendar.SecondsPerDay) == 0 && item.TimesUsed == 0',
         'age / (31 * ParkCalendar.SecondsPerDay) == 0 && item.TimesUsed == 0'),
        ('object-scrap-value', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'age / (30 * ParkCalendar.SecondsPerDay) == 0 && item.TimesUsed == 0',
         'age / (30 * ParkCalendar.SecondsPerDay) == 0 && item.TimesUsed <= 1'),
        ('object-scrap-value', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'return year is 1 or 2 ? Row( (int)year ) : Row( 3 );', 'return year is 1 or 2 ? Row( (int)year ) : Row( 2 );'),
        ('attraction-score', 'csharp', 'source/OpenTPW/World/Guests/AttractionScore.cs',
         'public const int DistanceDivisor = 450;', 'public const int DistanceDivisor = 449;'),
        ('attraction-score', 'csharp', 'source/OpenTPW/World/Guests/AttractionScore.cs',
         'if ( input.AgeDays <= variables.NewRideDays )', 'if ( input.AgeDays < variables.NewRideDays )'),
        ('ride-outcome', 'csharp', 'source/OpenTPW/World/Guests/RideOutcome.cs',
         'else if ( difference < 15 )', 'else if ( difference <= 15 )'),
        ('attraction-choice', 'csharp', 'source/OpenTPW/World/Guests/AttractionChoice.cs',
         '(worldTurn & 1) == 0', '(worldTurn & 1) != 0'),
        ('guest-needs-turn', 'csharp', 'source/OpenTPW/World/Guests/NeedsUpdate.cs',
         'public const int GrowthMask = 15;', 'public const int GrowthMask = 7;'),
        ('guest-needs-turn', 'csharp', 'source/OpenTPW/World/Guests/NeedsUpdate.cs',
         'if ( MeterMath.Byte( need ) == 100 )', 'if ( MeterMath.Byte( need ) >= 100 )'),
        ('guest-constructor', 'csharp', 'source/OpenTPW/World/Guests/GuestStart.cs',
         'public const uint ThirstHungerModulus = 50;', 'public const uint ThirstHungerModulus = 49;'),
        ('shop-sale-litter', 'csharp-guest-meters', 'source/OpenTPW/World/Guests/UseEffects.cs',
         'Hunger = MeterMath.Add( meters.Hunger, -(float)hungerEffect ),', 'Hunger = MeterMath.Add( meters.Hunger, -(float)thirstEffect ),'),
        ('guest-exit-test', 'csharp', 'source/OpenTPW/World/Guests/GuestLifecycle.cs',
         'MeterMath.Byte( happiness ) == 0 || exitLevel == 0 || parkClosed', 'MeterMath.Byte( happiness ) == 0 || exitLevel <= 0 || parkClosed'),
        ('loan-take', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'Post( LedgerCategory.LoanReceived, offer.Amount );', 'Post( LedgerCategory.OtherIncome, offer.Amount );'),
        ('staff-vitals', 'csharp', 'source/OpenTPW/Economy/StaffVitals.cs',
         'member.Energy - 0.025f * (float)factor', 'member.Energy - 0.026f * (float)factor'),
        ('staff-rest-seek', 'csharp', 'source/OpenTPW/Economy/StaffVitals.cs',
         'if ( Byte( member.Energy ) > restLevel )', 'if ( Byte( member.Energy ) >= restLevel )'),
        ('staff-strike-grievance', 'csharp', 'source/OpenTPW/Economy/StaffStrikes.cs',
         'return sum / (uint)members.Count < GrievanceBelow;', 'return sum / (uint)members.Count <= GrievanceBelow;'),
        ('staff-strike-month', 'csharp', 'source/OpenTPW/Economy/StaffStrikes.cs',
         '< EscalationUnits )', '<= EscalationUnits )'),
        ('calendar-date', 'csharp', 'source/OpenTPW/Economy/ParkCalendar.cs',
         'Epoch.AddSeconds( turn * SecondsPerTurn )', 'Epoch.AddSeconds( turn * (SecondsPerTurn + 1) )'),
        ('calendar-update', 'csharp', 'source/OpenTPW/Economy/ParkCalendar.cs',
         'if ( now.Day != stored.Day )', 'if ( now.Day > stored.Day )'),
        ('park-turn-cadence-frames', 'csharp', 'source/OpenTPW/World/FixedStepClock.cs',
         'public const double TickDuration = 1d / TicksPerSecond;', 'public const double TickDuration = 1d / (TicksPerSecond - 6);'),
        ('golden-ticket-cadence', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'turn % GoldenTicketCheckInterval == 0 && ParkModeFeatures.For( mode ).GoldenTickets',
         'turn % GoldenTicketCheckInterval == 1 && ParkModeFeatures.For( mode ).GoldenTickets'),
        ('ride-may-open', 'csharp', 'source/OpenTPW/Economy/RideStateRules.cs',
         '&& queueConnected && (trackType != CoasterTrackType || coasterComplete);', '&& queueConnected;'),
        ('ride-breakdown-update', 'csharp', 'source/OpenTPW/Economy/RideStateRules.cs',
         '(GaugeByte( life ) == 0 || GaugeByte( repair ) == 0)', '(GaugeByte( life ) == 0 || GaugeByte( repair ) <= 1)'),
        ('ride-breakdown-update', 'csharp', 'source/OpenTPW/Economy/RideStateRules.cs',
         'return after > 100f ? 100f : after < 0f ? 0f : after;', 'return after > 100f ? 100f : after;'),
        ('ride-repair', 'csharp', 'source/OpenTPW/Economy/RideStateRules.cs',
         'public const float FullGauge = 100f;', 'public const float FullGauge = 99f;'),
        ('staff-dismiss-charge', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'Post( LedgerCategory.StaffCosts, wage );', 'Post( LedgerCategory.StaffCosts, wage + 1 );'),
        ('park-value', 'csharp', 'source/OpenTPW/Economy/ParkValueRules.cs',
         'return unchecked( total * RideBreakdownRules.GaugeByte( stateOfRepair ) ) / 100;',
         'return unchecked( total * RideBreakdownRules.GaugeByte( stateOfRepair ) ) / 101;'),
        ('park-value', 'csharp', 'source/OpenTPW/Economy/ParkValueRules.cs',
         'public static bool CountsForValue( int state ) => state != RideObjectStates.Condemned;',
         'public static bool CountsForValue( int state ) => true;'),
        ('park-rating', 'csharp', 'source/OpenTPW/Economy/ParkEconomy.cs',
         'Math.Min( Count( 0, 2 ), 10 );', 'Math.Min( Count( 0, 3 ), 10 );'),
        ('staff-wage-month', 'csharp', 'source/OpenTPW/Economy/BalanceSettings.cs',
         'unchecked( BaseWage[Math.Clamp( grade, 0, GradeCount - 1 )] * this[type].PayMultiplier )',
         '(long)BaseWage[Math.Clamp( grade, 0, GradeCount - 1 )] * this[type].PayMultiplier'),
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
