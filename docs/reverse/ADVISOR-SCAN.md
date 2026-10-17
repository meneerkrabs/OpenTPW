# Advisor scan and producers (tp.exe v2.0)

Trace of every producer of the 351 advisor messages of the Windows executable (`tp.exe`, SHA-256
`5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`), written for the advisor campaign of OpenTPW. The machine-readable
form is `content/data/advisor-producers.toml` (one row per descriptor of `content/data/advisor-descriptors.toml`, covered by
`AdvisorProducerInventoryTests`). Addresses are virtual addresses of `tp.exe`. Nothing here is gameplay code: it is the map from which
the producers are built, batch by batch.

Method: static reading only (the original was never run). Every score callback of the descriptor table `0x76E300` (351 records of
`0x38` bytes, filled by `0x5A0A50`) was disassembled and its calls, its reads of the `Advisor.sam` object and its comparisons read out;
the posting code was found by listing every caller of the record builder `0x59B590`, the enqueue `0x59A940`, the help post `0x486B00`
and of the bus receiver `0x59B060`. The 85 park-statistics helpers the callbacks call (`0x4C7BB0`-`0x4D0A00`, `0x519450`-`0x5195D0`) are
described below with the confidence of the reading; a "formula" in the inventory is a reading of the compare-and-multiply sequence,
not a decompilation, and a few helpers are only identified by role (marked low). The English caption texts and the `Advisor.sam`
values are game data: the inventory stores the `TAG_SYSTEM.str` entry numbers and the key names, not the texts or numbers.

## Result

| Status | Messages | Meaning |
| --- | ---: | --- |
| wired | 87 | OpenTPW produces the message today |
| ready | 146 | every input exists in OpenTPW (the inventory names the class or field); only the producer is missing |
| blocked | 13 | an input is missing or approximated |
| none | 105 | nothing to produce: 21 are scored by the scan but their callback is the constant-zero stub, 84 have no producer in `tp.exe` at all |

| Kind | Messages | Producer |
| --- | ---: | --- |
| scan | 134 | the background scan `0x59C680` scores it from park state (the 134 computed callbacks) |
| scan-zero | 21 | the scan reaches it, the callback is `0x5D60C0` (returns 0): it can never pass `MinScoreForConsideration` |
| event | 67 | game code posts it (bus message or direct call) |
| help | 45 | a window or tool posts it through `0x486B00` (score callback `0x59E8C0`) |
| unused | 84 | no producer found (37 of these are help descriptors that nothing posts) |

| Batch | wired | ready | blocked | none | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Money | 1 | 7 | 0 | 0 | 8 |
| Cleanliness | 0 | 8 | 0 | 0 | 8 |
| Rides, shops, sideshows and features | 1 | 21 | 0 | 0 | 22 |
| Queues and prices | 0 | 22 | 1 | 0 | 23 |
| Guest mood and thoughts | 3 | 6 | 2 | 0 | 11 |
| Staff | 25 | 38 | 6 | 0 | 69 |
| Research | 10 | 9 | 0 | 0 | 19 |
| Weather | 0 | 0 | 0 | 0 | 0 |
| Challenges and golden tickets | 1 | 28 | 0 | 0 | 29 |
| Tutorial and help | 43 | 2 | 3 | 37 | 85 |
| Other | 3 | 5 | 1 | 68 | 77 |
| Total | 87 | 146 | 13 | 105 | 351 |

Weather has no message: no score callback reads weather or season state, no poster is a weather event, and no `TAG_SYSTEM.str` caption
of the advisor mentions rain, storm or snow. The "about 91" messages with a producer are **87** that produce advice today: 45 posted by game code (0, 323, 106, 128, 129, 33-57, 70, 93-97,
125-127, 133-136, 167, 271) and 42 help messages of windows and tools OpenTPW has. Four more look wired but are not: `AutomaticAdvisor.GlobalTicketMovedMessage`
posts 234-237 when a global golden ticket moves here, but `AdvisorTables` has no definition of them, so `AdvisorController.Advise` returns null and nothing
is said (inventory status ready; the fix is the four rows `(234-237, "AwardMoved.Score")` in `EventScoreKeys`). 66, 68 and 69 are defined in `AdvisorTables` without
a poster (status ready).

## The scan

`0x59A550` is the advisor controller's update (thing type 11; called by the thing update `0x50B437`). Each call:

1. `push 1; call 0x59C680` on the controller's `Advisor.sam` object `0xFB3540`: the scan advances **one descriptor** (the cursor is the
   object's first word, wrapped to 0 when it reaches `0x15F` = 351). A full pass is therefore 351 updates.
2. The scan skips a descriptor whose first word is nonzero (the "posted by game code" flag: help messages, strikes, research and so on,
   196 rows here), applies the game-mode selector in word `+0xC` (2 always, 0 only in the full simulation, 1 only in Instant
   Action, `0xFB3B7C`), and calls the score callback `[desc+0x10]` with `this = 0xFB3540 + [desc+0x14]`. The callback returns a score (<= 0
   means no advice).
3. A positive score is kept only if the eligibility check `0x59ABC0` passes (tutorial option, repeat intervals, once-only, slaps and the
   pending-duplicate limit: `AdvisorController.Advise` already ports them), and the highest one wins (a strictly higher score).
4. Back in `0x59A550`: the score is read again (`0x59BF20`), and only a score **above** `MinScoreForConsideration` (`0xFB3560`, the object's
   `+0x20`; 25 shipped) builds a record (`0x59B590`) and queues it (`0x59A940`).
5. The queue is then served as documented in docs/LIPS.md: the strongest pending record above the minimum is said when the previous
   one has finished.

So the scan costs one callback per update and spreads the 134 computed scores over 351 updates; every score is evaluated from the
state of that moment (no history is kept except what the helpers read). `AutomaticAdvisor.Update` is the place for the cursor.

Callback conventions: the world is `[0x80239C]` (also `[[0x7CF6EC]]`). `0x519510` returns the **park thing** (handle at world `+0x1DA720`,
pool `0x7CFB90`, stride `0x14`); the park thing holds the statistics the helpers read (visitor counter `+0x21C08`, visitor ring
`+0x216D8`, the 20 price rings of 50 values from `+0x216FC`, the first-object and first-hire stamps `+0x21C30` / `+0x21C38`, the
prank counters `+0x21BFC` / `+0x21C00`). `0x5195D0` is the **bank** (handle `+0x1DA726`: balance `+0xC`, 8 loan slots of `0x20` bytes
from `+0x20`, year profit `+0x124`, the turn entered red `+0x120`), `0x519550` the **research manager** (`+0x1DA722`),
`0x5194D0` the **advisor thing** itself (`+0x1DA71C`, the `this` of `0x59A940`), `0x519450` the **training** thing (`+0x1DA718`).
`0x51A280` returns the park's *closed* flag (world `+0x1DA710`, nonzero while closed), so `0x4C9050` is "the park is open". Months are
30 days of 100 ns units (`0x2A69C000`:`0xC9` = one day; `0xF8648000`:`0x1792` = 2.592e13 = one month) divided with `0x67C320` from the
park clock `0x4F8800` (`world + 0x2A0`); the money helpers use the bank's turn-entered-red stamp the same way.

## Advisor.sam object layout

The schema `0x72E124` (records of 60 bytes: a type word, the key name at `+4`) lays the object out as: `MinTimeAnyMessage`,
`MinTimeSameMessage` and `MinScoreForConsideration` at `+0x18`/`+0x1C`/`+0x20`, ten message groups of three words from `+0x24`, then
**every further key takes four bytes in schema order from `+0xA0`** (bytes are padded to four). Reading the 358 leaf keys this way puts
`HelpMessage.Score` at `+0x328`, `HelpMessage.ContextHelpScore` `+0x32C`, `WealthHigh.MoreMoneyThan` `+0x330`, `RidesBreakdownImminent.Score`
`+0x248`, `PrebuiltPark.Score` `+0x5F8`, `PrankSbomb.Score` `+0x3D4` and `Challenge.Score` `+0x62C`, each as the anchors already traced from
the getters. The `score` column of the inventory lists the keys a callback reads this way. Shipped values live in the game's
`Data/Advisor/Advisor.sam` (read at runtime by `AdvisorBalance`); the inventory does not copy them.

## Park-statistics helpers

Confidence: high = whole body read, medium = shape read and role inferred from the callers and captions, low = role only.

| Helper | Role | Conf. |
| --- | --- | --- |
| `0x4C7BB0` / `0x4C7CC0` | mean happiness of the guests in the park / of the staff (0 while closed or empty) | high |
| `0x4C7DB0` | park value (cell and ride values, cached for a second) | medium |
| `0x4C7FA0`, `0x4C9130` | guests in the park (the predicate `0x4FA990`: cell types 0, 1, 3, 9, 10) | high |
| `0x4C81E0` | "busy points": guests in the park / 2, capped at 100 | high |
| `0x4C81B0` | developed cells (the 1024 map blocks via `0x4C8C40`) | medium |
| `0x4C9050` | the park is open (`0x51A280` clear) | high |
| `0x4C9070` / `0x4C90D0` | days since the first-object stamp `+0x21C30` (written by `0x4DB090`) / first-hire stamp `+0x21C38` (`0x504B90`) | high |
| `0x4C91C0` | guests in state 3 (paid, waiting at the gate) at the two ticket-booth cells | medium |
| `0x4C9290` / `0x4C9340` / `0x4C93F0` | guests in the park whose toilet (`+0x1AC`) / hunger (`+0x1A8`) / thirst (`+0x1A4`) byte is **strictly above** the argument | high |
| `0x4C94A0(flag)` | the park counters `+0x21BFC` (flag 0) / `+0x21C00` (flag 1): pranks and escaped pranksters | medium |
| `0x4C94C0`, `0x4C94D0` | visitors ever (`+0x21C08`) and visitors since n months | high |
| `0x4C9530` / `0x4C95D0` | guests holding a balloon (`+0x210`) / in a costume (`+0x24` == 2) | high |
| `0x4C9750(n)` | percent of the guests in the park with happiness below n | high |
| `0x4C9830` / `0x4C9920` | mean of the last 50 values of a price ring / the ring holds 50 values | high |
| `0x4C9960(role)`, `0x4C9A10` | staff of a role (thing model 4 mechanic, 5 handyman, 6 entertainer, 7 guard, 8 researcher) / all staff | high |
| `0x4C9AA0`, `0x4C9B40(role)` | tired staff (`0x506680`) / staff with a patrol area set (`0x506EB0`) | high |
| `0x4C9C00(role)` | mean training percent of a role | high |
| `0x4C9E40`, `0x4C9E60`, `0x4C9ED0` | research counters: count, cost researched, total | medium |
| `0x4C9EE0`, `0x4C9F90` | toilets (relief features, bit 1 of `+0x32`), closed toilets | high |
| `0x4CA030`, `0x4CA0C0` | staff rooms (bit 2 of `+0x32`), closed staff rooms | high |
| `0x4CA160`, `0x4CA220` | mean toilet filth (100 - condition `+0x44`), the worst toilet and its handle | high |
| `0x4CA310`, `0x4CA3A0(pct)` | rides broken down (state 1) / rides broken or below a condition percent | high |
| `0x4CA450`, `0x4CA4C0`, `0x4CA510`, `0x4CA570`, `0x4CA5D0` | object types available to build (rides, upgrades, shops, sideshows, features) | medium |
| `0x4CA630`-`0x4CA690`, `0x503B30`, `0x503980` | research outstanding per category, research progress percent | medium |
| `0x4CA740` | percent of buildable object types researched | medium |
| `0x4CA7B0(kind)`, `0x4CA8E0(kind, level)`, `0x4CA9B0(kind)` | built types / built objects (level at least n) / closed objects of a kind (0 ride, 1 shop, 2 sideshow, 3 feature) | high |
| `0x4CAAE0`-`0x4CC090` | the worst/best **value** and **satisfaction** of shops of a stock type (2 food, 3 restaurant, 4 drinks), of retail shops and of sideshows: a scan of attractions with more than 30 customers over their customer, walk-away and satisfaction rings, returning a 0-100 figure and the attraction's handle | medium |
| `0x4CCA40`, `0x4CCB00`, `0x4CCBF0`, `0x4CCD10`, `0x4CCDF0` | a per-toilet mean (unresolved), unconnected queue test, mean and maximum ride fear (`0x4E0860`), litter points | medium/low |
| `0x4CD500(role, mode)` | percent of path cells not covered by a staff role's patrol | medium |
| `0x4D0260`, `0x4D0690`, `0x4D0810`, `0x4D0970`, `0x4D09A0`, `0x4D0A00`, `0x4D09F0` | months in the red, loans taken, a loan can be taken, repayment of a loan, the cheapest loan, total debt, year profit | high |

## Lines the brief asked for

All ids are the descriptor index (the advice id); the caption is the `TAG_SYSTEM.str` entry (0-based) the response shows, none when the
response is speech only. Texts are paraphrased.

| Line | Message | Producer | Status |
| --- | --- | --- | --- |
| Prices are too high (food, shops, restaurant, drinks, sideshows) | 238-242 (responses 478-482, speech only, samples 506-510; the unreferenced `TAG_SYSTEM` texts 96 food, 100 shops, 102 restaurant, 99 drinks and 111 sideshows read as these lines) | scan, callbacks `0x59FB60`-`0x59FD20`: the worst value among the shops of a stock type with more than 30 customers falls below `<Type>PoorValue.ValueWorseThan`; score = (threshold - value) x `ScorePerPoint` | ready |
| Satisfaction is poor (same five shops) | 243-247 (responses 483-487, speech only) | scan, `0x59FD90`-`0x59FF50`, same scan over the satisfaction ring | ready |
| Ticket price too high / too cheap | 91 / 92 (caption 94 / 95) | scan, `0x59E730` / `0x59E790`: the category-0 price ring holds 50 values and its mean satisfaction is below `TicketSatisfactionUnder` / above `TicketSatisfactionOver` | ready (`ParkPriceStatistics` exists; check that the booth records category 0) |
| Queues are too long | 58 (caption 63: bored in long queues) and 59 (caption 64: reduce ride duration) | the scan reaches them but both callbacks are `0x5D60C0` (constant 0) and nothing posts them: **dead in v2.0**. The only queue line that can play is 113 (a queue not connected to a path, caption 134, `0x59EF60`, blocked: `queueLinks`) | none / blocked |
| The park is dirty | 8, 9, 10 (captions 14, 15, 16) for litter without enough handymen; 86-89 (captions 89-92) for dirty toilets | scan, `0x59CB40`, `0x59CBB0`, `0x59CC20` (litter points, developed cells per handyman) and `0x59E450`-`0x59E680` (mean toilet filth in four bands, with and without cleaners) | ready |
| Guests' thoughts | 3 thirsty (caption 9), 4 hungry (10), 82 no toilets (86), 141 too scared (159), 14 entertainers needed (20), 145 visitors happy (no caption) | scan; the advisor reads the guests' **need meters** (thirst, hunger, toilet, happiness bytes), not the thought ids of `THOUGHTS.str` (those feed the park window's Top 3 Thoughts only) | ready (141 blocked: `rideFear`) |
| Money warnings 102-105 | 102 take a loan (caption 123), 103 six-months warning (124), 104 three months (125), 105 five months (126); 106 bankruptcy (127) | scan `0x59E9B0`, `0x59E9F0`, `0x59EA70`, `0x59EAF0`: balance < 0; 102 needs a loan available; 103-105 need **no** loan available and read the months in the red (`0x4D0260` / 30 days): 103 below 3, 104 3 or 4, 105 exactly 5. 106 is game event 2 (`0x4D0370`) | ready (106 wired) |
| Broken rides | 5 worn rides and no mechanic (caption 11), 6 broken rides and no mechanic (12), 7 too few mechanics (13); 66 breakdown imminent (71), 68 no mechanic (72), 69 mechanics busy (73), 70 mechanic on the way (74); 258 ride condemned; 259 too many rides closed | scan `0x59CA30`/`0x59CA80`/`0x59CAC0`/`0x5A02F0`; posts `0x4DF8C0` (condition crosses below 10 percent in the wear step `0x4DF670`) and `0x4DADC0` (the breakdown handler: 68 when no mechanic is hired, 70 when one takes it, 69 when all are busy); condemn message type 8 (`0x59B448`) | ready (70 wired) |

## Money

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 100 | ready | cb 0x59E8D0 | balance (bank +0xC via 0x6AD810) > WealthHigh.MoreMoneyThan and no loan taken (0x4D0690 = 0): flat WealthHigh.Score | speech only |
| 101 | ready | cb 0x59E910 | balance minus total debt (0x4D0A00) greater than the repayment of the cheapest loan (0x4D09A0, 0x4D0970) x Margin: flat WealthHighRepayLoan.Score | speech only |
| 102 | ready | cb 0x59E9B0 | balance below 0 and a loan can still be taken (0x4D0810): flat TakeLoan.Score | 123 |
| 103 | ready | cb 0x59E9F0 | balance below 0, no loan available, months in the red (0x4D0260 / 30-day months) < 3: flat InTheRedSixMonths.Score | 124 |
| 104 | ready | cb 0x59EA70 | balance below 0, no loan available, months in the red 3 or 4: flat InTheRedThreeMonths.Score | 125 |
| 105 | ready | cb 0x59EAF0 | balance below 0, no loan available, months in the red exactly 5: flat InTheRedMonthLeft.Score | 126 |
| 106 | wired | CMsgEvent 2 from the bank month 0x4D0370 (0x4D0557); handler 0x59AED6 posts 106 | balance < 0, previous balance < 0 and 6 or more 30-day months in the red: posted at every month end while it holds | 127 |
| 107 | ready | cb 0x59EB80 | after 3 months: the average staff wages over WagesHigh.AverageOverMonths months (ring +0x1F7F0) x WagesTimesHigherThanIncome above the average income (ring +0x1FC90): flat WagesHigh.Score | 128 |

The state exists: `ParkEconomy.Balance`, `Loans`, `IsLoanAvailable`, `MonthsInRed` (ECON-030) and `Ledger.History`. 103-105 need the months in the red the way the bank counts them (`TurnEnteredRed`), not whole calendar months.

## Cleanliness

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 8 | ready | cb 0x59CB40 | park open and no handyman: litter points (0x4CCDF0) at most LitterPointCutoffHigh; score = litter x ScorePerLitterPoint - LitterPointCutoffLow | 14 |
| 9 | ready | cb 0x59CBB0 | park open and no handyman: litter points >= StaffHireHandymen2.LitterPointCutoffLow; score = litter x ScorePerLitterPoint - cutoff | 15 |
| 10 | ready | cb 0x59CC20 | park open with at least one handyman: developed cells (0x4C81B0) >= MinCellsForConsideration; (cells - handymen x IdealDevelopedCellsPerHandyman) x ScorePerExtra10Cells | 16 |
| 86 | ready | cb 0x59E450 | park open, no handyman, a toilet exists: mean toilet filth (0x4CA160 = 100 - condition) between FeaturesDirtyToilets1 and 3 FilthierThan: filth x PointsPerFilthPct | 89 |
| 87 | ready | cb 0x59E510 | park open, a handyman exists: mean filth between DirtyToilets2 and 4 FilthierThan: filth x PointsPerFilthPct | 90 |
| 88 | ready | cb 0x59E5D0 | park open, no handyman: mean filth >= FeaturesDirtyToilets3.FilthierThan: filth x its PointsPerFilthPct | 91 |
| 89 | ready | cb 0x59E680 | park open, a handyman exists: mean filth >= FeaturesDirtyToilets4.FilthierThan: filth x its PointsPerFilthPct | 92 |
| 262 | ready | cb 0x5A0430 | toilets closed (0x4C9F90) x 100 / toilets > MoreThanThisPercentClosed: flat ToiletsClosed.Score | speech only |

## Rides, shops, sideshows and features

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 5 | ready | cb 0x59CA30 | no mechanic hired (0x4C9960 type 4); else rides that are broken or below StaffHireMechanics1.PoorerStateThan percent condition (0x4CA3A0) x ScorePerWornRide | 11 |
| 6 | ready | cb 0x59CA80 | no mechanic hired; broken-down rides (state 1, 0x4CA310) x StaffHireMechanics2.ScorePerBrokenRide | 12 |
| 7 | ready | cb 0x59CAC0 | days since the first hire (0x4C90D0, park +0x21C38) >= WaitingTimes.DaysForStaff; max(0, rides (0x4CA8E0 kind 0) - mechanics x IdealNumberOfRidesPerMechanic) x ScorePerExtraRide | 13 |
| 60 | ready | cb 0x59D990 | ride types available, days since first object >= DaysForObjects, rides >= MoreRidesThan, built types x 100 / available types < PoorerThan: flat RidesPoorSelection.Score | 65 |
| 62 | ready | cb 0x59DAD0 | no ride built: RidesBuildRides.Score + days since the start x AdditionalPerDay | 67 |
| 66 | ready | ride wear step 0x4DF670 (0x4DF8C0): condition (+0x44) crosses below 10 percent (float 0x7005A0) in a step | condition before the step >= 10 and after < 10 percent, not in game mode 1; flat RidesBreakdownImminent.Score | 71 |
| 68 | ready | ride-breakdown message handler 0x4DADC0 (type 7), jump to 0x4DAFF6 from 0x4DAE68 | a ride breaks down and no mechanic is hired | 72 |
| 69 | ready | 0x4DADC0 (0x4DAFF6): every mechanic is busy with another ride | a ride breaks down and no mechanic is free | 73 |
| 70 | wired | mechanic dispatch 0x4DA5B0 (0x4DA6C5) and 0x4DADC0 (0x4DAF7E): a mechanic takes the broken ride | a mechanic is on the way | 74 |
| 71 | ready | cb 0x59DCA0 | shops available, days since first object >= DaysForObjects, shops built >= MoreShopsThan, built types x 100 / available < PoorerThan: flat Score | 75 |
| 73 | ready | cb 0x59DDE0 | rides exist, none of the shops built (days since first object >= DaysForObjects): Score + days x AdditionalPerDay | 77 |
| 74 | ready | cb 0x59DE80 | rides exist, built shops < rides x PerRideRatio, balance > WealthMoreThan: flat Score | 78 |
| 75 | ready | cb 0x59DF30 | sideshows available, days since first object >= DaysForObjects, sideshows built >= MoreSideshowsThan, built types x 100 / available < PoorerThan: flat Score | 79 |
| 77 | ready | cb 0x59E070 | rides exist, none of the sideshows built (days since first object >= DaysForObjects): Score + days x AdditionalPerDay | 81 |
| 78 | ready | cb 0x59E110 | rides exist, built sideshows < rides x PerRideRatio, balance > WealthMoreThan: flat Score | 82 |
| 79 | ready | cb 0x59E1C0 | features available, days since first object >= DaysForObjects, features built >= MoreFeaturesThan, built types x 100 / available < PoorerThan: flat Score | 83 |
| 81 | ready | cb 0x59E2E0 | rides exist, none of the features built (days since first object >= DaysForObjects): Score + days x AdditionalPerDay | 85 |
| 85 | ready | cb 0x59E420 | park open: mean of a per-toilet figure (0x4CCA40 over 0x4DDF40, unresolved) is returned as the score without an Advisor.sam key | 88 |
| 258 | ready | CMsgRideCondemned (type 8); 0x59B060 posts 258 (0x59B448) | a ride is condemned (state 4) | speech only |
| 259 | ready | cb 0x5A02F0 | rides >= AtLeastThisManyRides and closed rides (0x4CA9B0) x 100 / rides > MoreThanThisPercentClosed: flat RidesClosed.Score | speech only |
| 260 | ready | cb 0x5A0370 | shops closed x 100 / shops > MoreThanThisPercentClosed: flat ShopsClosed.Score | speech only |
| 261 | ready | cb 0x5A03D0 | sideshows closed x 100 / sideshows > MoreThanThisPercentClosed: flat SideshowsClosed.Score | speech only |

## Queues and prices

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 91 | ready | cb 0x59E730 | ticket price ring (category 0, 0x4C9830) holds 50 values (0x4C9920) and its mean satisfaction is below TicketSatisfactionUnder: flat PricingTicketTooExpensive.Score | 94 |
| 92 | ready | cb 0x59E790 | same ring, mean satisfaction above TicketSatisfactionOver: flat PricingTicketTooCheap.Score | 95 |
| 113 | blocked | cb 0x59EF60 | a queue that no path reaches (0x4CCB00): flat QueueUnconnected.Score | 134 |
| 238 | ready | cb 0x59FB60 | Food: the worst value (customers/walk-away rings, 0x4CADF0 style scan of shops with more than 30 customers) below ValueWorseThan: (ValueWorseThan - value) x ScorePerPoint (the price is too high for the quality) | speech only |
| 239 | ready | cb 0x59FBD0 | Shops: the worst value (customers/walk-away rings, 0x4CADF0 style scan of shops with more than 30 customers) below ValueWorseThan: (ValueWorseThan - value) x ScorePerPoint (the price is too high for the quality) | speech only |
| 240 | ready | cb 0x59FC40 | Restaurant: the worst value (customers/walk-away rings, 0x4CADF0 style scan of shops with more than 30 customers) below ValueWorseThan: (ValueWorseThan - value) x ScorePerPoint (the price is too high for the quality) | speech only |
| 241 | ready | cb 0x59FCB0 | Drinks: the worst value (customers/walk-away rings, 0x4CADF0 style scan of shops with more than 30 customers) below ValueWorseThan: (ValueWorseThan - value) x ScorePerPoint (the price is too high for the quality) | speech only |
| 242 | ready | cb 0x59FD20 | Sideshows: the worst value (customers/walk-away rings, 0x4CADF0 style scan of shops with more than 30 customers) below ValueWorseThan: (ValueWorseThan - value) x ScorePerPoint (the price is too high for the quality) | speech only |
| 243 | ready | cb 0x59FD90 | Food: the worst satisfaction (0x4CB200 style scan, SatisfactionRing) below SatisfactionWorseThan: (threshold - value) x ScorePerPoint | speech only |
| 244 | ready | cb 0x59FE00 | Shops: the worst satisfaction (0x4CB200 style scan, SatisfactionRing) below SatisfactionWorseThan: (threshold - value) x ScorePerPoint | speech only |
| 245 | ready | cb 0x59FE70 | Restaurant: the worst satisfaction (0x4CB200 style scan, SatisfactionRing) below SatisfactionWorseThan: (threshold - value) x ScorePerPoint | speech only |
| 246 | ready | cb 0x59FEE0 | Drinks: the worst satisfaction (0x4CB200 style scan, SatisfactionRing) below SatisfactionWorseThan: (threshold - value) x ScorePerPoint | speech only |
| 247 | ready | cb 0x59FF50 | Sideshows: the worst satisfaction (0x4CB200 style scan, SatisfactionRing) below SatisfactionWorseThan: (threshold - value) x ScorePerPoint | speech only |
| 248 | ready | cb 0x59FFC0 | Food: the best value above ValueBetterThan: (value - threshold) x ScorePerPoint (praise for a fair price) | speech only |
| 249 | ready | cb 0x5A0010 | Shops: the best value above ValueBetterThan: (value - threshold) x ScorePerPoint (praise for a fair price) | speech only |
| 250 | ready | cb 0x5A0060 | Restaurant: the best value above ValueBetterThan: (value - threshold) x ScorePerPoint (praise for a fair price) | speech only |
| 251 | ready | cb 0x5A00B0 | Drinks: the best value above ValueBetterThan: (value - threshold) x ScorePerPoint (praise for a fair price) | speech only |
| 252 | ready | cb 0x5A0100 | Sideshows: the best value above ValueBetterThan: (value - threshold) x ScorePerPoint (praise for a fair price) | speech only |
| 253 | ready | cb 0x5A0150 | Food: the best satisfaction above SatisfactionBetterThan: (value - threshold) x ScorePerPoint | speech only |
| 254 | ready | cb 0x5A01A0 | Shops: the best satisfaction above SatisfactionBetterThan: (value - threshold) x ScorePerPoint | speech only |
| 255 | ready | cb 0x5A01F0 | Restaurant: the best satisfaction above SatisfactionBetterThan: (value - threshold) x ScorePerPoint | speech only |
| 256 | ready | cb 0x5A0240 | Drinks: the best satisfaction above SatisfactionBetterThan: (value - threshold) x ScorePerPoint | speech only |
| 257 | ready | cb 0x5A0290 | Sideshows: the best satisfaction above SatisfactionBetterThan: (value - threshold) x ScorePerPoint | speech only |

## Guest mood and thoughts

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 3 | ready | cb 0x59C9D0 | guests whose thirst is above VisitorsThirsty.ThirstierThan (0x4C93F0, strictly greater, truncated byte; the shipped threshold 99 makes only a thirst of 100 count) x ScorePerThirstyPerson | 9 |
| 4 | ready | cb 0x59CA00 | guests whose hunger is above VisitorsHungry.HungrierThan (0x4C9340, same rule as 3) x ScorePerHungryPerson | 10 |
| 82 | ready | cb 0x59E380 | rides exist, no toilet built: guests needing the toilet above NeedingMoreThan (0x4C9290) x 100 / guests in park > MorePeopleThanThisNeeding: flat Score | 86 |
| 125 | wired | prank message (type 0xE, sender 0x4FF2EA); 0x59B060 case at 0x59B3C6 posts 125 | kind word 2 -> 127, 1 -> 126, 0 -> 125 (prankster id modulo 3) | 146 |
| 126 | wired | prank message type 0xE; 0x59B060 case at 0x59B348 | kind word 1 | 147 |
| 127 | wired | prank message type 0xE; 0x59B060 case at 0x59B2CA | kind word 2 | 148 |
| 141 | blocked | cb 0x59F4F0 | rides >= MinimumRides and the average ride fear (0x4CCBF0, cached for a second) > AverageScarierThan: flat VisitorsTooScared.Score | 159 |
| 145 | ready | cb 0x59F5E0 | guests >= MinimumVisitors and average happiness (0x4C7BB0) > HappierThan: flat CongratVisitorsHappy.Score | speech only |
| 158 | ready | cb 0x59F940 | guests holding a balloon (0x4C9530, +0x210 nonzero) > CongratBalloons.MoreThan: flat Score | 174 |
| 159 | ready | cb 0x59F970 | guests in costume (0x4C95D0, +0x24 == 2) > CongratCostumes.MoreThan: flat Score | 175 |
| 264 | blocked | cb 0x5A04F0 | pranksters (0x4C94A0 arg 0) > MorePrankstersThan: flat LotsOfPranks.Score | speech only |

## Staff

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 11 | ready | cb 0x59CCB0 | park open, days since first hire >= DaysForStaff, no guard: busy points (0x4C81E0 = min(100, guests/2)) minus AtLeastThisBusy, x ScorePerExtraBusyPoint | 17 |
| 12 | blocked | cb 0x59CD30 | park open, no guard: StaffHireGuards2.Score + misbehaving kids (0x4C94A0 arg 0) x AdditionalPerMisbehavingKid | 18 |
| 13 | ready | cb 0x59CD90 | park open, days since first hire >= DaysForStaff, developed cells >= MinCellsForConsideration: (cells - guards x IdealDevelopedCellsPerGuard) x ScorePerExtra10Cells | 19 |
| 14 | ready | cb 0x59CE30 | park open, no entertainer, more than 30 percent of guests have happiness below 30 (0x4C9750): (busy points - AtLeastThisBusy) x ScorePerExtraBusyPoint | 20 |
| 18 | ready | cb 0x59D0C0 | months in game >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40) <= DontBotherIfPatrolledUp: (DontBotherIfPatrolledUp - percent) x ScorePerUnpatrolledPct | speech only |
| 19 | blocked | cb 0x59D180 | months in game >= threshold, role count >= AtLeastThisMany, some patrol area set: uncovered-cell percent (0x4CD500) x ScorePerNonCoveredPercent, at least the coverage Score | speech only |
| 20 | ready | cb 0x59D250 | months in game >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40) <= DontBotherIfPatrolledUp: (DontBotherIfPatrolledUp - percent) x ScorePerUnpatrolledPct | speech only |
| 21 | blocked | cb 0x59D310 | months in game >= threshold, role count >= AtLeastThisMany, some patrol area set: uncovered-cell percent (0x4CD500) x ScorePerNonCoveredPercent, at least the coverage Score | speech only |
| 22 | blocked | cb 0x59D3B0 | a guard exists and the escape counter (0x4C94A0 arg 1) > StaffGuardBuildCamera.AcceptableTrouble: flat Score | 28 |
| 23 | ready | cb 0x59D400 | months in game >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40) <= DontBotherIfPatrolledUp: (DontBotherIfPatrolledUp - percent) x ScorePerUnpatrolledPct | speech only |
| 26 | ready | cb 0x59D4C0 | months in game >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40) <= DontBotherIfPatrolledUp: (DontBotherIfPatrolledUp - percent) x ScorePerUnpatrolledPct | speech only |
| 27 | blocked | cb 0x59D580 | months in game >= threshold, role count >= AtLeastThisMany, some patrol area set: uncovered-cell percent (0x4CD500) x ScorePerNonCoveredPercent, at least the coverage Score | speech only |
| 28 | ready | cb 0x59D620 | role exists, no training in progress (0x519450 +0xC), average training percent (0x4C9C00) < WorseThan: flat Score | 33 |
| 29 | ready | cb 0x59D680 | role exists, no training in progress (0x519450 +0xC), average training percent (0x4C9C00) < WorseThan: flat Score | 34 |
| 30 | ready | cb 0x59D6E0 | role exists, no training in progress (0x519450 +0xC), average training percent (0x4C9C00) < WorseThan: flat Score | 35 |
| 31 | ready | cb 0x59D740 | role exists, no training in progress (0x519450 +0xC), average training percent (0x4C9C00) < WorseThan: flat Score | 36 |
| 32 | ready | cb 0x59D7A0 | role exists, no training in progress (0x519450 +0xC), average training percent (0x4C9C00) < WorseThan: flat Score | 37 |
| 33 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 38 |
| 34 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 39 |
| 35 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 40 |
| 36 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 41 |
| 37 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 42 |
| 38 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 43 |
| 39 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 44 |
| 40 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 45 |
| 41 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 46 |
| 42 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 47 |
| 43 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 48 |
| 44 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 49 |
| 45 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 50 |
| 46 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 51 |
| 47 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 52 |
| 48 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 53 |
| 49 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 54 |
| 50 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 55 |
| 51 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 56 |
| 52 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 57 |
| 53 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 58 |
| 54 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 59 |
| 55 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 60 |
| 56 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 61 |
| 57 | wired | CMsgStaffInfo (type 0x17) from the staff manager month routine 0x509031-0x5092AA; 0x59AFC0 posts 33 + 5 x kind + role (0x59B034) | kind 0 unhappy, 1 very unhappy, 2 striking, 3 happier, 4 strike over; role order handyman, mechanic, guard, entertainer, researcher | 62 |
| 98 | blocked | cb 0x59E840 | escaped pranksters (0x4C94A0 arg 1) > MoreThanThisGotAway: count x ScorePerGetAway | 119 |
| 108 | ready | cb 0x59EDD0 | handymen hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 129 |
| 109 | ready | cb 0x59EE20 | entertainers hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 130 |
| 110 | ready | cb 0x59EE70 | mechanics hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 131 |
| 111 | ready | cb 0x59EEC0 | guards hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 132 |
| 112 | ready | cb 0x59EF10 | researchers hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 133 |
| 114 | ready | cb 0x59EFA0 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 135 |
| 115 | ready | cb 0x59EFE0 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 136 |
| 116 | ready | cb 0x59F020 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 137 |
| 117 | ready | cb 0x59F060 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 138 |
| 118 | ready | cb 0x59F0A0 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 139 |
| 119 | ready | cb 0x59F0E0 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 140 |
| 120 | ready | cb 0x59F120 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 141 |
| 121 | ready | cb 0x59F160 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 142 |
| 122 | ready | cb 0x59F1A0 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 143 |
| 123 | ready | cb 0x59F1E0 | staff of the role has reached MoreThan, the limit of ten: flat Score (the V2 message is the repeat variant) | 144 |
| 144 | ready | cb 0x59F590 | staff >= MinimumStaff and average staff happiness (0x4C7CC0) > HappierThan: flat CongratStaffHappy.Score | speech only |
| 146 | ready | cb 0x59F630 | training has run (0x519450 +0xC > 0), guards > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 147 | ready | cb 0x59F6A0 | training has run (0x519450 +0xC > 0), researchers > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 148 | ready | cb 0x59F710 | training has run (0x519450 +0xC > 0), handymen > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 149 | ready | cb 0x59F780 | training has run (0x519450 +0xC > 0), entertainers > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 150 | ready | cb 0x59F7F0 | training has run (0x519450 +0xC > 0), mechanics > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 169 | ready | cb 0x59F9C0 | after 3 months: tired staff percent (0x4C9AA0 / total) > NeedStaffRoom.PctOfStaffTired: flat Score | speech only |
| 170 | ready | cb 0x59FA60 | park open, visitors ever >= MinVisitorsEver, no staff at all: flat HireStaffGeneral.Score | speech only |
| 171 | ready | cb 0x59FAC0 | staff > MinStaffForMessage and nobody has a patrol area (0x4C9B40): flat SetPatrolsGeneral.Score | speech only |
| 172 | ready | cb 0x59FB10 | staff > MinStaffForMessage and no training ever started (0x519450 +0xC == 0): flat TrainStaffGeneral.Score | speech only |
| 263 | ready | cb 0x5A0490 | staff rooms closed (0x4CA0C0) x 100 / staff rooms > MoreThanThisPercentClosed: flat StaffroomClosed.Score | speech only |

## Research

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 15 | ready | cb 0x59CEA0 | park open, no researcher, ride research outstanding, built ride types x 100 / available ride types above StaffHireResearchers1.Most: excess x PointsPerExtraPct | 21 |
| 16 | ready | cb 0x59CF50 | park open, no researcher, ride research outstanding, every available ride type already built: flat StaffHireResearchers2.Score | 22 |
| 17 | ready | cb 0x59CFE0 | park open, a researcher exists, months into the game >= MonthsIntoGameForConsideration, research cost done (0x4C9E60) x 100 / total below SlowerThan: flat StaffHireResearchers3.Score | 23 |
| 61 | ready | cb 0x59DA30 | a researcher, a ride built, ride research outstanding, research progress (0x503980) low, every available ride type built: flat RidesResearchMore.Score | 66 |
| 63 | ready | cb 0x59DB40 | months >= threshold, a researcher, no ride upgrade available (0x4CA4C0), upgrade research progress low: flat RidesNoUpgrades.Score | 68 |
| 64 | ready | cb 0x59DBF0 | a researcher, upgrades available > 0 but <= LessAvailableThan, upgrade research progress low: flat RidesMoreUpgrades.Score | 69 |
| 72 | ready | cb 0x59DD40 | a researcher, a ride built, shop research outstanding and low, every available shop type built: flat Score | 76 |
| 76 | ready | cb 0x59DFD0 | a researcher, a ride built, sideshow research outstanding and low, every available sideshow type built: flat Score | 80 |
| 80 | ready | cb 0x59E260 | a researcher, a ride built, feature research outstanding and low, every available feature type built: flat Score | 84 |
| 93 | wired | CMsgResearchCompleted (type 0x11) sent by 0x504630; 0x59B060 posts 93-97 by category (0x59B0E6-0x59B1CE) | ride 93, add-on 94, shop 95, sideshow 96, feature 97 | 114 |
| 94 | wired | CMsgResearchCompleted (type 0x11) sent by 0x504630; 0x59B060 posts 93-97 by category (0x59B0E6-0x59B1CE) | ride 93, add-on 94, shop 95, sideshow 96, feature 97 | 115 |
| 95 | wired | CMsgResearchCompleted (type 0x11) sent by 0x504630; 0x59B060 posts 93-97 by category (0x59B0E6-0x59B1CE) | ride 93, add-on 94, shop 95, sideshow 96, feature 97 | 116 |
| 96 | wired | CMsgResearchCompleted (type 0x11) sent by 0x504630; 0x59B060 posts 93-97 by category (0x59B0E6-0x59B1CE) | ride 93, add-on 94, shop 95, sideshow 96, feature 97 | 117 |
| 97 | wired | CMsgResearchCompleted (type 0x11) sent by 0x504630; 0x59B060 posts 93-97 by category (0x59B0E6-0x59B1CE) | ride 93, add-on 94, shop 95, sideshow 96, feature 97 | 118 |
| 133 | wired | 0x503C6E (0x503CA7-0x503D07) when a research group opens: 133 ride, 134 shop, 135 sideshow, 136 feature | a new research group opens | 154 |
| 134 | wired | 0x503C6E (0x503CA7-0x503D07) when a research group opens: 133 ride, 134 shop, 135 sideshow, 136 feature | a new research group opens | 154 |
| 135 | wired | 0x503C6E (0x503CA7-0x503D07) when a research group opens: 133 ride, 134 shop, 135 sideshow, 136 feature | a new research group opens | 154 |
| 136 | wired | 0x503C6E (0x503CA7-0x503D07) when a research group opens: 133 ride, 134 shop, 135 sideshow, 136 feature | a new research group opens | 154 |
| 167 | wired | 0x503F96: all research done (full simulation only when the player ledger flag +0xB9 of every park is set) | all items researched | speech only |

## Challenges and golden tickets

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 215 | ready | golden ticket won, 0x4D4FA0 group 1..3 (0x4D51AD, 0x4D50F2, 0x4D5037), called from 0x4D4BF0 | a local ticket is won (visitors, people, happiness, profit ...) | 180, 181, 182, 183, 184, 185 |
| 216 | ready | golden ticket won, 0x4D4FA0 group 1..3 (0x4D51AD, 0x4D50F2, 0x4D5037), called from 0x4D4BF0 | a local ticket is won (visitors, people, happiness, profit ...) | 180, 181, 182, 183, 184, 185 |
| 217 | ready | golden ticket won, 0x4D4FA0 group 1..3 (0x4D51AD, 0x4D50F2, 0x4D5037), called from 0x4D4BF0 | a local ticket is won (visitors, people, happiness, profit ...) | 180, 181, 182, 183, 184, 185 |
| 220 | ready | golden ticket won, 0x4D5270 group 1..3 (0x4D52F3, 0x4D53B4, 0x4D546F) | a global ticket is won | 186, 187, 188, 189 |
| 221 | ready | golden ticket won, 0x4D5270 group 1..3 (0x4D52F3, 0x4D53B4, 0x4D546F) | a global ticket is won | 186, 187, 188, 189 |
| 222 | ready | golden ticket won, 0x4D5270 group 1..3 (0x4D52F3, 0x4D53B4, 0x4D546F) | a global ticket is won | 186, 187, 188, 189 |
| 225 | ready | golden ticket won, 0x4D58F0 group 1..3 (0x4D5A7D, 0x4D59C2, 0x4D5907): the secret tickets | a secret ticket is won | 190, 191 |
| 226 | ready | golden ticket won, 0x4D58F0 group 1..3 (0x4D5A7D, 0x4D59C2, 0x4D5907): the secret tickets | a secret ticket is won | 190, 191 |
| 227 | ready | golden ticket won, 0x4D58F0 group 1..3 (0x4D5A7D, 0x4D59C2, 0x4D5907): the secret tickets | a secret ticket is won | 190, 191 |
| 234 | ready | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved here; OpenTPW posts it (AutomaticAdvisor.GlobalTicketMovedMessage) but AdvisorTables has no definition of 234-237, so Controller.Advise returns null: add (234-237, AwardMoved.Score) to EventScoreKeys | speech only |
| 235 | ready | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved here; OpenTPW posts it (AutomaticAdvisor.GlobalTicketMovedMessage) but AdvisorTables has no definition of 234-237, so Controller.Advise returns null: add (234-237, AwardMoved.Score) to EventScoreKeys | speech only |
| 236 | ready | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved here; OpenTPW posts it (AutomaticAdvisor.GlobalTicketMovedMessage) but AdvisorTables has no definition of 234-237, so Controller.Advise returns null: add (234-237, AwardMoved.Score) to EventScoreKeys | speech only |
| 237 | ready | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved here; OpenTPW posts it (AutomaticAdvisor.GlobalTicketMovedMessage) but AdvisorTables has no definition of 234-237, so Controller.Advise returns null: add (234-237, AwardMoved.Score) to EventScoreKeys | speech only |
| 270 | ready | CMsgChallenge (type 25) from the challenge offer 0x4D2C50 (0x4D2D73); 0x59B060 forwards the id (0x59B489, id < 0x160) | a challenge is offered | speech only |
| 271 | wired | CMsgChallenge from 0x4D1FF0 (0x4D223C) when the time left reaches ShortTimeLeftWarningAt | time-short warning of the running challenge | speech only |
| 272 | ready | CMsgChallenge from 0x4D1A30 (0x4D1A5B) | challenge outcome (probably accepted or completed; the writer was not traced further) | speech only |
| 273 | ready | CMsgChallenge from 0x4D1AE0 (0x4D1B42), the challenge-lost routine | a challenge is lost (loss count +0x24 raised) | speech only |
| 274 | ready | cb 0x5A08B0 | park open and the ticket gate open (0x51A280 clear): guests x 100 / the level target (runtime setting 0x785794, capped at 100 percent) > HigherPctThanThis: flat GTAmountVisitorsA.Score | speech only |
| 275 | ready | cb 0x5A0930 | park open and the ticket gate open (0x51A280 clear): guests x 100 / the level target (runtime setting 0x785794, capped at 100 percent) > HigherPctThanThis: flat GTAmountVisitorsB.Score | speech only |
| 276 | ready | cb 0x5A09B0 | park open and the ticket gate open (0x51A280 clear): guests x 100 / the level target (runtime setting 0x785794, capped at 100 percent) > HigherPctThanThis: flat GTAmountVisitorsC.Score | speech only |
| 278 | ready | cb 0x5A07D0 | park open: percent of object types researched (0x4CA740) above this threshold and not above the next GTAllResearched threshold: flat Score | speech only |
| 279 | ready | cb 0x5A0820 | park open: percent of object types researched (0x4CA740) above this threshold and not above the next GTAllResearched threshold: flat Score | speech only |
| 280 | ready | cb 0x5A0870 | park open: percent of object types researched (0x4CA740) above this threshold and not above the next GTAllResearched threshold: flat Score | speech only |
| 281 | ready | cb 0x5A0780 | the visitor counter (+0x216D8) equals 1: flat GoldTicketExplanation.Score (the first guest) | speech only |
| 311 | ready | 0x4D5B30 (0x4D5BB6) after the first golden ticket was won | first golden ticket of the player | speech only |
| 314 | ready | cb 0x5A0530 | visitors ever (0x4C94C0) above GoldTicketNearToFirstOne.PeopleMultiplier x the first ticket target: flat Score | speech only |
| 315 | ready | 0x4D5B30 (0x4D5C47) clues for the remaining tickets | after a ticket is won | speech only |
| 316 | ready | cb 0x5A05A0 | visitors ever above PeopleMultiplier x target and the visitors ticket (0x5AF7F0 flag 0) not won: flat GoldTicketNearToXPeeps.Score | speech only |
| 317 | ready | cb 0x5A05F0 | average happiness above HappinessMultiplier x target, guests above the target and the happiness ticket (flag 2) not won: flat Score | speech only |

## Tutorial and help

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 185 | ready | cb 0x5A06C0 | game days between TutorialUIPanel.AfterTime and CutoffTime: flat Score | speech only |
| 282 | ready | cb 0x5A0720 | game days between TutorialExplainTooltips.AfterTime and CutoffTime: flat Score | speech only |
| 323 | wired | same handler 0x59AE43 posts advice 323 (0x59AEC1) when the game mode word 0xFB3B7C is 2 (Instant Action) | flat PrebuiltPark.Score | speech only |

Help messages (score callback `0x59E8C0`, `HelpMessage.Score`) are posted by window and tool code through `0x486B00` and withdrawn through
`0x486B40`; the descriptor table has **82** of them. 81 are in `content/data/advisor-help-messages.toml`: message 291 names response 551,
which the response table does not have, so the table generator (and `AdvisorHelpTable`) leave it out, but its descriptor exists in
`advisor-descriptors.toml` and in the inventory (kind unused). The "81 versus 82" counts in the comments and docs are the same table
counted with and without 291.

- wired (42): 184, 188-189, 191-193, 195, 197-201, 203, 205-209, 212-214, 284, 286-287, 289, 292-295, 297-305, 309, 337, 345-346
- posted in the original, no window here (3): 285, 307-308 (285 All Staff window, 308 address book, 307 postcard)
- posted by nothing in `tp.exe` (37): 186-187, 190, 194, 196, 202, 204, 210-211, 266, 283, 288, 290-291, 296, 306, 322, 324-336, 338-344

## Other

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 0 | wired | CMsgEvent 0 sent by the main loop 0x54ED70 (0x54F2D7) at every level start; handler 0x59AE43 posts advice 0 | flat Welcome.Score; no state read | speech only |
| 1 | ready | cb 0x59C8D0 | 0 while the park is open or while no ride and no sideshow is built (0x4CA8E0 kinds 0 and 2); else guests in state 3 at the gate (0x4C91C0) x OpenPark.ScorePerWaitingPerson | 7 |
| 2 | ready | cb 0x59C960 | park open and neither a ride nor a sideshow built: flat ClosePark.Score; else 0 | 8 |
| 124 | ready | cb 0x59F220 | park older than 1 month and younger than HypeNewpark.MaxMonths: visitor drop (0x216D8 ring) above DroppedPercent: flat Score | speech only |
| 128 | wired | CMsgEvent 3 from the open/close button routine 0x519EF0 (0x51A037); handler 0x59AF02 posts 128 | posted at every press of the open button, also when already open | 149 |
| 129 | wired | CMsgEvent 4 from 0x519EF0 (0x51A1C6); handler 0x59AF31 posts 129 | posted at every press of the close button, also when already closed | 150 |
| 140 | blocked | cb 0x59F440 | the last arrival wave (0x4D8650) larger than HowBigIsBig: flat BigBusloadArrived.Score | 158 |
| 156 | ready | cb 0x59F860 | park value (0x4C7DB0) > ValueBetterThan, guests > VisitorsMoreThan, average happiness > HappinessGreaterThan: flat Score | speech only |
| 157 | ready | cb 0x59F8D0 | park value (0x4C7DB0) > ValueBetterThan, guests > VisitorsMoreThan, average happiness > HappinessGreaterThan: flat Score | speech only |

## Messages nothing produces

Scored by the scan but constant zero (`0x5D60C0`), 21: 24-25, 58-59, 65, 90, 151-155, 160, 173, 265, 267-269, 347-350. Messages 58 and 59 (queues) and 90 (guests leaving when tired, build a rest area) are the visible ones: their captions exist, their speech exists, the callbacks were stubbed out.

No producer found, 47 (callback and poster search; a producer that builds its message id from a table or a computed value would be missed, see "Confidence"): 67, 83-84, 99, 130-132, 137-139, 142-143, 161-166, 168, 174-183, 218-219, 223-224, 228-233, 277, 310, 312-313, 318-321. Among them are the new-visitor arrival lines 137-139 (bus, plane, boat), the upgrade lines 130 and 132, the golden-ticket lines 99, 218, 219, 223, 224, 228, 229 and the award lines 230-233 (the posts found cover 215-217, 220-222, 225-227, 234-237, 311 and 315), the online lines 142 and 143, the four `WELCOME` lines 180-183, and the challenge lines 319 and 320 (their callback `0x5A0A30` is `Challenge.Score`, but the challenge system posts only 270-273).

## Captions nothing references

`TAG_SYSTEM.str` has 332 entries; the response rows reach 143 of them through the tag table (`0x75ECA8`). Many of the rest are the
texts of messages whose responses carry the no-text tag `0x17F`: they are not shown by v2.0 (the `orphans` column of the inventory
lists them, matched by wording and by the constant offset to the captioned neighbours, so inferred). They also give the best written
form of lines that are otherwise speech only, e.g. the price lines: 96 (food shops too expensive, message 238), 97 (food very cheap, 248),
98 / 99 (drinks bargain / too expensive, 251 / 241), 100 / 101 (shops, 239 / 249), 102 / 103 (restaurant, 240 / 250), 111-113 (sideshows,
242 / 252 / 247) and the quality lines 104-110 (243, 244 and the ingredient lines of 245, 246, 253-256); the wealth lines 121 / 122
(100 / 101); the patrol lines 24-32 (18-21, 23, 24, 26, 27); the congratulations 160-166, 172-173 and 177-179 (144-150, 156, 157, 161-163).
Whether the spoken clip says the same words is not verifiable without the audio.

## Confidence and open points

- Every address and callback was read from the bytes of `tp.exe`; the polarity of the gates (a jump over a `ret 0` or into it) was checked against the
  captions. Formulas are readings of the instruction sequence; the exact per-shop value of `0x4CAAE0`-`0x4CC090`, the per-toilet mean of `0x4CCA40`,
  the litter points `0x4CCDF0` (a scaled density) and the coverage `0x4CD500` still need a line-by-line port from the bytes before they are implemented.
- The poster search lists every caller of `0x59B590`, `0x59A940`, `0x486B00` and the seven bus message types the controller registers
  (5, 8, 14, 17, 19, 23, 25 - `0x59B060` dispatch table). The constant-id scan inside the posting functions agrees; an id computed at run time
  from a table would not show up, so "no producer" means "no producer found".
- `0x4D4FA0` / `0x4D5270` / `0x4D58F0` (golden tickets) take a group 1-3; whether groups 4 and 5 (218-219, 223-224, 228-229) are reachable was not traced.
- 272 (`0x4D1A30`) is a challenge outcome; whether it is "accepted" or "completed" was not traced (273 is the lost-challenge routine, `0x4D1AE0`).
- Status "ready" says the input state exists, not that the formula is trivial; it also needs the scan driver (cursor, one descriptor per update, `MinScoreForConsideration`).

## Implementation order

1. **The scan driver** (no message by itself): the cursor, the mode selector, one callback per advisor update, the eligibility that `AdvisorController.Advise` already has.
2. **Money** (102, 103, 104, 105, then 100, 101, 107): the most visible warnings, all inputs exist and the numbers are small.
3. **Rides and breakdowns** (5, 6, 7, 66, 68, 69, 258-261): `ParkObjectState` carries the condition, closed and broken flags; 70 is already wired.
4. **Cleanliness** (8, 9, 10, 86-89, 262): litter and toilet gauges exist; port the litter points first.
5. **Guest mood** (3, 4, 82, 145, 158, 159): needs the census members on `IParkGuestStatistics`.
6. **Staff** (11, 13, 14, 18, 20, 23, 26, 28-32, 108-123, 144, 146-150, 169-172) and **research** (15-17, 61, 63, 64, 72, 76, 80).
7. **Prices** (91, 92, then 238-257): the price ring exists; the shop value and satisfaction scans need the line-by-line port.
8. **Challenges and golden tickets** (270, 272, 273, 215-229, 311, 315, 274-281, 314-318).
9. **Blocked**: coverage (19, 21, 27), pranks (12, 22, 98, 264), queue links (113), ride fear (141), arrivals (140), and the three help windows.
