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

Message 344 is posted by the first challenge offer, despite retaining the help-score descriptor callback and response 606. It is an event producer, not an unused or window-help producer; the existing first-offer runtime and restored-advice catalog are unchanged.

Message 204 is the kart and water line tool's help: the click that gives the tool the line start it lacks posts it (`0x526D41`, a jump into the queue tool's post `0x527629`; see "Tutorial and help"). It is a `help` row, `wired`, and the restored-advice extension no longer posts it as the roller coaster tool starts.

| Status | Messages | Meaning |
| --- | ---: | --- |
| wired | 236 | OpenTPW produces the message today |
| ready | 0 | every input exists in OpenTPW (the inventory names the class or field); only the producer is missing |
| blocked | 12 | an input is missing or approximated |
| none | 103 | nothing to produce: 21 are scored by the scan but their callback is the constant-zero stub, 82 have no producer in `tp.exe` at all |

| Kind | Messages | Producer |
| --- | ---: | --- |
| scan | 134 | the background scan `0x59C680` scores it from park state (the 134 computed callbacks) |
| scan-zero | 21 | the scan reaches it, the callback is `0x5D60C0` (returns 0): it can never pass `MinScoreForConsideration` |
| event | 68 | game code posts it (bus message or direct call) |
| help | 46 | a window or tool posts it through `0x486B00` (score callback `0x59E8C0`) |
| unused | 82 | no producer found (35 of these are help descriptors that nothing posts) |

| Batch | wired | ready | blocked | none | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Money | 8 | 0 | 0 | 0 | 8 |
| Cleanliness | 8 | 0 | 0 | 0 | 8 |
| Rides, shops, sideshows and features | 22 | 0 | 0 | 0 | 22 |
| Queues and prices | 22 | 0 | 1 | 0 | 23 |
| Guest mood and thoughts | 9 | 0 | 2 | 0 | 11 |
| Staff | 63 | 0 | 6 | 0 | 69 |
| Research | 19 | 0 | 0 | 0 | 19 |
| Weather | 0 | 0 | 0 | 0 | 0 |
| Challenges and golden tickets | 30 | 0 | 0 | 0 | 30 |
| Tutorial and help | 46 | 0 | 3 | 35 | 84 |
| Other | 9 | 0 | 0 | 68 | 77 |
| Total | 236 | 0 | 12 | 103 | 351 |

Weather has no message: no score callback reads weather or season state, no poster is a weather event, and no `TAG_SYSTEM.str` caption
of the advisor mentions rain, storm or snow. The "about 91" messages with a producer: **236** have their producer here: 68 posted by game code (every `event` row: 0, 323, 106, 128, 129, 33-57, 66,
68-70, 93-97, 125-127, 133-136, 167, 215-217, 220-222, 225-227, 234-237, 258, 270-273, 311, 315 and 344), 43 help messages of windows and tools OpenTPW has, and 125 scored by the ported scan (the money
batch: 100-105 and 107; the 31 park callbacks of the rides, cleanliness and guest mood batches: 3-10, 60, 62, 71, 73-75, 77-79, 81, 82, 85-89, 145, 158, 159 and 259-262; the 38 staff callbacks: 11, 13, 14, 18, 20, 23, 26, 28-32,
108-112, 114-123, 144, 146-150, 169-172 and 263; the 31 callbacks of the queues, prices and research batches: 15-17, 61, 63, 64, 72, 76, 80, 91, 92 and 238-257; and the 18 callbacks
of the park-opening, congratulation, tutorial and golden ticket group: 1, 2, 124, 140, 156, 157, 185, 274-276, 278-282, 314, 316 and 317, see "Challenges and golden tickets").
`AutomaticAdvisor.GlobalTicketMovedMessage` posts 234-237 when a global golden ticket moves here, and `AdvisorTables` now defines them (the four rows
`(234-237, "AwardMoved.Score")` in `EventScoreKeys`; their group 2 controls are read when the settings file has them). 66, 68, 69 and 258 are posted by the ride events
(`AdvisorRideEvents`, see "Park callbacks").

## The scan

`0x59A550` is the advisor controller's update (thing type 11; called by the thing update `0x50B437`). Each call:

1. `push 1; call 0x59C680` on the controller's `Advisor.sam` object `0xFB3540`: the scan scores **one descriptor** (the cursor is the
   object's first word, wrapped to 0 when it reaches `0x15F` = 351). The loop's counter (`[esp+0x10]`) is incremented only at `0x59C822`, which a skipped
   row (first word nonzero) bypasses, so one call walks past any number of game-code rows to the next scanned one and leaves the cursor after it:
   a full pass is **155** scored updates (134 computed callbacks and 21 stubs), not 351.
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

So the scan costs one callback per update and spreads the 134 computed scores over 155 updates; every score is evaluated from the
state of that moment (no history is kept except what the helpers read).

**OpenTPW.** `AdvisorScan` (`source/OpenTPW/World/AdvisorScan.cs`) is `0x59C680` with count 1: the cursor, the skip rule, the wrap at 351, the mode selector
(`AdvisorTables.GameModeAccepts`), the callback dispatch by the descriptor's callback address, strictly-higher-than-best and the eligibility check. A callback that is
not ported scores 0, like the stub `0x5D60C0`. `AdvisorController.ScanOnce` adds step 4: the score must be above `MinScoreForConsideration`, then the record
(message, the callback's score) goes through `AdvisorScoreQueue.Enqueue`, which repeats the eligibility check as `0x59A940` does. The cursor is part of
`AdvisorControllerSnapshot` (a snapshot without one restores it at 0; an original save import starts at 0: no save word for it was found). The advisor is presentation state
outside the simulation hash, so the scan moves no determinism pin. `AutomaticAdvisor.ScanPark` calls the scan once per park turn: `0x516380` increments the turn word and walks the thing list only while the world state `+0x1DA738` is not 4, and the advisor's case (`0x50B3F1`-`0x50B437`) calls `0x59A550` unless the game type is 1, so turns that pass while paused or lost are never scanned, and the same call serves the queue at its end (`AutomaticAdvisor.Update`: one service per scan call; ADVISOR-017, retired). Ported callbacks: the money batch below; the other 9 computed callbacks (the `blocked` scan rows, which need an input OpenTPW does not have) and the 21 constant-zero stubs score 0.

Callback conventions: the world is `[0x80239C]` (also `[[0x7CF6EC]]`). `0x519510` returns the **park thing** (handle at world `+0x1DA720`,
pool `0x7CFB90`, stride `0x14`); the park thing holds the statistics the helpers read (visitor counter `+0x21C08`, visitor ring
`+0x216D8`, the 20 price rings of 50 values from `+0x216FC`, the stamps of the newest object construction and the newest hire `+0x21C30` / `+0x21C38`, the
prank counters `+0x21BFC` / `+0x21C00`). `0x5195D0` is the **bank** (handle `+0x1DA726`: balance `+0xC`, 8 loan slots of `0x20` bytes
from `+0x20`, year profit `+0x124`, the turn entered red `+0x120`), `0x519550` the **research manager** (`+0x1DA722`),
`0x5194D0` the **advisor thing** itself (`+0x1DA71C`, the `this` of `0x59A940`), `0x519450` the **training** thing (`+0x1DA718`).
`0x51A280` returns the park's *closed* flag (world `+0x1DA710`, nonzero while closed), so `0x4C9050` is "the park is open". Months are
30 days of 100 ns units (`0x2A69C000`:`0xC9` = one day; `0xF8648000`:`0x1792` = 2.592e13 = one month) divided with `0x67C320` from the
park clock `0x4F8800` (`world + 0x2A0`); the money helpers use the bank's turn-entered-red stamp the same way.

## Advisor.sam object layout

The schema `0x72E124` (records of 60 bytes: a type word, the key name at `+4`; the record that names a group of keys comes **after** its keys, as the trailer of the run) lays the object out as: `MinTimeAnyMessage`,
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
| `0x4C7DB0(age)` | park value (cell points and ride values; the count is cached at `+0x1F0EC` with a stamp and counted again only when the cache is older than the argument: 10 for the congratulations 156/157, 20 for the loan test, 0 for the month sample) | high |
| `0x4C7FA0`, `0x4C9130` | guests in the park (the predicate `0x4FA990`: cell types 0, 1, 3, 9, 10) | high |
| `0x4C81E0` | "busy points": guests in the park / 2, capped at 100 | high |
| `0x4C81B0` | developed cells (the 1024 map blocks via `0x4C8C40`) | medium |
| `0x4C9050` | the park is open (`0x51A280` clear) | high |
| `0x4C9070` / `0x4C90D0` | days since the object stamp `+0x21C30` (rewritten by every object construction: the single exit of the common constructor `0x4DB090` calls `0x4C7680` at `0x4DB7A3`, the newest, not the first; the constructor's five callers are the build commit `0x529E10` at `0x529FD5`, which a build and the Move tool's re-placement both reach, the park setup's gates and lights at `0x515BE1` and `0x515C94` after the park init at `0x5159C8`, the finale's End at `0x516B8B` and an arrival vehicle's first trip at `0x51A57E`; the loader `0x5179C0` constructs nothing through it and reads the saved `mLastObjectBuilt` back at `0x4C7289`) / the hire stamp `+0x21C38` (rewritten by every staff thing init, `0x504B90` calling `0x4C76D0`): (park time now - stamp) / one day (`0x5FC550`, then `__alldiv` by 864,000,000,000), the park time being the calendar's `FunnyTimeStart` + floor(turn x rate / 4) x 10^7 (`0x4F8690`). The analyser serializer `0x4C5D70` saves both raw as the fifth and sixth of its six 8-byte stamps (`0x4C6B9A`, `0x4C6BC4`, after `mLastStrike`, `mLastPuke`, `mLastPrank` and `mParkLastOpened`) and its load reads them back (`0x4C7289`, `0x4C72B3`); the loader zeroes the six before that read (`0x518A43`-`0x518A85`) and restamps neither, as it builds the staff through `0x518E20`, not through the staff init | high |
| `0x4C91C0` | guests in state 3 (waiting at the booth) on the two ticket-booth cells (`0x4D8610` of sides 0 and 1, counted once per side) | high |
| `0x4C9290` / `0x4C9340` / `0x4C93F0` | guests in the park whose toilet (`+0x1AC`) / hunger (`+0x1A8`) / thirst (`+0x1A4`) byte is **strictly above** the argument | high |
| `0x4C94A0(flag)` | the park counters `+0x21BFC` (flag 0) / `+0x21C00` (flag 1): pranks and escaped pranksters | medium |
| `0x4C94C0`, `0x4C94D0` | visitors ever (`+0x21C08`) and visitors since n months | high |
| `0x4C9530` / `0x4C95D0` | guests holding a balloon (`+0x210`) / in a costume (`+0x24` == 2) | high |
| `0x4C9750(n)` | percent of the guests in the park with happiness below n | high |
| `0x4C9830` / `0x4C9920` | mean of the last 50 values of a price ring / the ring holds 50 values | high |
| `0x4C9960(role)`, `0x4C9A10` | staff of a role (thing model 4 mechanic, 5 handyman, 6 entertainer, 7 guard, 8 researcher) / all staff | high |
| `0x4C9AA0`, `0x4C9B40(role)` | tired staff (`0x506680`) / staff with a patrol area set (`0x506EB0`) | high |
| `0x4C9C00(role)` | mean training percent of a role | high |
| `0x4C9E40`, `0x4C9E60`, `0x4C9ED0` | researchers waiting in the hiring pool of a type (`0x508380`), the researchers' ability summed by grade, the research capacity (`MaxResearchersInPark` x the best ability) | high |
| `0x4C9EE0`, `0x4C9F90` | toilets (relief features, bit 1 of `+0x32`), closed toilets | high |
| `0x4CA030`, `0x4CA0C0` | staff rooms (bit 2 of `+0x32`), closed staff rooms | high |
| `0x4CA160`, `0x4CA220` | mean toilet filth (100 - condition `+0x44`), the worst toilet and its handle | high |
| `0x4CA310`, `0x4CA3A0(pct)` | rides broken down (state 1) / rides broken or below a condition percent | high |
| `0x4CA450`, `0x4CA4C0`, `0x4CA510`, `0x4CA570`, `0x4CA5D0` | object types available to build (rides, shops, sideshows, features); `0x4CA4C0` counts the ride types with a researched upgrade level | high |
| `0x4CA630`-`0x4CA690`, `0x503B30`, `0x503980` | unfinished research projects of a category (ride, shop, sideshow, feature), a category's research rate (share x staffing percent) | high |
| `0x4CA740` | percent of the object types **with a golden ticket cost** (`+0xC4` above 0) whose control's built count (`+0x18`, never lowered) is above 0, `built x 100 / types` (signed), 0 without such a type: the share of the ticket items ever built, not a research share | high |
| `0x4CA7B0(kind)`, `0x4CA8E0(kind, level)`, `0x4CA9B0(kind)` | built types / built objects (level at least n) / closed objects of a kind (0 ride, 1 shop, 2 sideshow, 3 feature) | high |
| `0x4CAAE0`-`0x4CC090` | the worst/best **value** and **satisfaction** of shops of a stock type (2 food, 3 restaurant, 4 drinks), of retail shops and of sideshows: a scan of attractions older than 30 days over their customer, walk-away and satisfaction rings, returning a figure and the attraction's handle (see "Queues, prices and research callbacks") | high |
| `0x4CCA40`, `0x4CCB00`, `0x4CCBF0`, `0x4CCD10`, `0x4CCDF0` | a per-toilet mean (unresolved), unconnected queue test, mean and maximum ride fear (`0x4E0860`), litter points | medium/low |
| `0x4CD500(role, mode)` | percent of path cells not covered by a staff role's patrol | medium |
| `0x4D0260`, `0x4D0690`, `0x4D0810`, `0x4D0970`, `0x4D09A0`, `0x4D0A00`, `0x4D09F0` | months in the red, loans taken, a loan can be taken, repayment of a loan, the cheapest loan, total debt, year profit | high |

## Lines the brief asked for

All ids are the descriptor index (the advice id); the caption is the `TAG_SYSTEM.str` entry (0-based) the response shows, none when the
response is speech only. Texts are paraphrased.

| Line | Message | Producer | Status |
| --- | --- | --- | --- |
| Prices are too high (food, shops, restaurant, drinks, sideshows) | 238-242 (responses 478-482, speech only, samples 506-510; the unreferenced `TAG_SYSTEM` texts 96 food, 100 shops, 102 restaurant, 99 drinks and 111 sideshows read as these lines) | scan, callbacks `0x59FB60`-`0x59FD20`: the lowest value among the attractions of the group older than 30 days (walk-aways x 100 / customers) falls below `<Type>PoorValue.ValueWorseThan`; score = (value - threshold) x `ScorePerPoint`, negative for a positive multiplier, so the line never passes | wired (never passes) |
| Satisfaction is poor (same five shops) | 243-247 (responses 483-487, speech only) | scan, `0x59FD90`-`0x59FF50`, the same scan over the satisfaction ring; 247 calls the sideshow value helper; the score is negative as above | wired (never passes) |
| Ticket price too high / too cheap | 91 / 92 (caption 94 / 95) | scan, `0x59E730` / `0x59E790`: perception ring 0 (the booth verdict's opinion of the entrance fee, `0x4FF5B0`) holds 50 values and its mean is below `TicketSatisfactionUnder` / above `TicketSatisfactionOver` | wired |
| Queues are too long | 58 (caption 63: bored in long queues) and 59 (caption 64: reduce ride duration) | the scan reaches them but both callbacks are `0x5D60C0` (constant 0) and nothing posts them: **dead in v2.0**. The only queue line that can play is 113 (a queue not connected to a path, caption 134, `0x59EF60`, blocked: `queueLinks`) | none / blocked |
| The park is dirty | 8, 9, 10 (captions 14, 15, 16) for litter without enough handymen; 86-89 (captions 89-92) for dirty toilets | scan, `0x59CB40`, `0x59CBB0`, `0x59CC20` (litter points, developed cells per handyman) and `0x59E450`-`0x59E680` (mean toilet filth in four bands, with and without cleaners) | wired |
| Guests' thoughts | 3 thirsty (caption 9), 4 hungry (10), 82 no toilets (86), 141 too scared (159), 14 entertainers needed (20), 145 visitors happy (no caption) | scan; the advisor reads the guests' **need meters** (thirst, hunger, toilet, happiness bytes), not the thought ids of `THOUGHTS.str` (those feed the park window's Top 3 Thoughts only) | wired (141 blocked: `rideFear`) |
| Money warnings 102-105 | 102 take a loan (caption 123), 103 six-months warning (124), 104 three months (125), 105 five months (126); 106 bankruptcy (127) | scan `0x59E9B0`, `0x59E9F0`, `0x59EA70`, `0x59EAF0`: balance < 0; 102 needs a loan available; 103-105 need **no** loan available and read the months in the red (`0x4D0260` / 30 days): 103 below 3, 104 3 or 4, 105 exactly 5. 106 is game event 2 (`0x4D0370`) | wired |
| Broken rides | 5 worn rides and no mechanic (caption 11), 6 broken rides and no mechanic (12), 7 too few mechanics (13); 66 breakdown imminent (71), 68 no mechanic (72), 69 mechanics busy (73), 70 mechanic on the way (74); 258 ride condemned; 259 too many rides closed | scan `0x59CA30`/`0x59CA80`/`0x59CAC0`/`0x5A02F0`; posts `0x4DF8C0` (condition crosses below 10 percent in the wear step `0x4DF670`) and `0x4DADC0` (the breakdown handler: 68 when no mechanic is hired, 70 when one takes it, 69 when all are busy); condemn message type 8 (`0x59B448`) | wired |

## Money

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 100 | wired | cb 0x59E8D0 | balance (bank +0xC via 0x6AD810) > WealthHigh.MoreMoneyThan and no loan taken (0x4D0690 = 0): flat WealthHigh.Score | speech only |
| 101 | wired | cb 0x59E910 | balance minus total debt (0x4D0A00) greater than the repayment of the cheapest loan (0x4D09A0, 0x4D0970) x Margin: flat WealthHighRepayLoan.Score | speech only |
| 102 | wired | cb 0x59E9B0 | balance below 0 and a loan can still be taken (0x4D0810): flat TakeLoan.Score | 123 |
| 103 | wired | cb 0x59E9F0 | balance below 0, no loan available, months in the red (0x4D0260 / 30-day months) < 3: flat InTheRedSixMonths.Score | 124 |
| 104 | wired | cb 0x59EA70 | balance below 0, no loan available, months in the red 3 or 4: flat InTheRedThreeMonths.Score | 125 |
| 105 | wired | cb 0x59EAF0 | balance below 0, no loan available, months in the red exactly 5: flat InTheRedMonthLeft.Score | 126 |
| 106 | wired | CMsgEvent 2 from the bank month 0x4D0370 (0x4D0557); handler 0x59AED6 posts 106 | balance < 0, previous balance < 0 and 6 or more 30-day months in the red: posted at every month end while it holds | 127 |
| 107 | wired | cb 0x59EB80 | after 3 months: the average staff wages over WagesHigh.AverageOverMonths months (ring +0x1F7F0) above the average income (ring +0x1FC90) x WagesTimesHigherThanIncome (0 when a ring is empty): flat WagesHigh.Score | 128 |

Wired (`AdvisorScanCallbacks`, `AdvisorScanTests`): the callbacks are ported from the disassembly, over `IAdvisorMoneyState` (`EconomyMoneyState` reads the economy without changing it).
All seven have the `this` offset 0 and return the flat score of their key, so the offsets above are the schema offsets of the keys; the rows with mode 0 (101, 102) are scored in
the full simulation only, the others in both game types.

- **100 `0x59E8D0`:** `balance > WealthHigh.MoreMoneyThan` (signed) and loans taken (`0x4D0690`, the bought records of the eight at bank `+0x28 + 0x20 i`) = 0.
- **101 `0x59E910`:** `diff = balance - total debt` (`0x4D0A00`: the sum over bought records of `(term - repaid) x instalment`, words `+0x0`, `+0xC`, `+0x4` of the record at bank `+0x20 + 0x20 i`); the cheapest loan (`0x4D09A0`: the smallest non-zero owed amount as an unsigned word, the first of equals, -1 for none -> 0); `repayment (0x4D0970, unsigned) x WealthHighRepayLoan.Margin < (float)diff`. `Margin` is a single, and `diff` is rounded to a single by the `fild`/`fstp` pair before the compare.
- **102 `0x59E9B0`:** `balance < 0` and a loan can be taken (`0x4D0810`: walking the eight offers, the first not bought that passes the credit test `0x4D06B0`).
- **103/104/105 `0x59E9F0`/`0x59EA70`/`0x59EAF0`:** `balance < 0`, no loan available, then `months = __alldiv(0x4D0260, 0x1792F8648000)` (0 unless the last balance `+0x11C` is below 0; else the park time since the turn entered red `+0x120`): below 3, `3 <= months < 5`, exactly 5. OpenTPW's `ParkEconomy.MonthsSinceEnteringRed` is that count.
- **107 `0x59EB80`:** the park clock must have run 3 months (`0x4F8800` / `0x1792F8648000`); `n = AverageOverMonths` cut to the months the ring holds (`head + 1`, or 144 when full); the wage average and the income average are the truncated (`__ftol`) means of the newest `n` months of the rings at park `+0x1F7F0` and `+0x1FC90` (a ring is: current accumulator, 144 words, head, capacity, full flag; the month close `0x4C7740` pushes the accumulators and zeroes them, so the ring is the ledger's closed months: `StaffCosts` and `MoneyIn`); `income x WagesTimesHigherThanIncome < wages` (single factor; an empty income ring scores 0 too, `0x59ED8D`). The x87 runs at 24 bits (DirectDraw's FPU setup, ADVISOR-026), so OpenTPW rounds to a single after every step of 101 and 107.

Two reading choices keep the advisor from changing simulation state: the loan availability peeks the net worth as `0x4D06B0` reads it (`0x4C7DB0(20)`: the cached value while its age is at most 20, else a fresh count that is not stamped) and does not store the offers' credit tests (the original's `0x4D06B0` rewrites word `+0x14` of each offer it tests, which the loan list rebuilds before it reads), so the scan never writes to the economy. In a park in the red with staff and no income the wages line 107 (90) outranks the early warnings 103/104 (75, 50) once three months have passed, so those are served after it; 105 (110) outranks it.

## Park callbacks (rides, cleanliness, guest mood)

Ported by `AdvisorParkCallbacks` (`source/OpenTPW/World/AdvisorScanPark.cs`) over `IAdvisorParkState`; `EconomyParkState` computes the statistics from the economy without writing
to it (the advisor stays outside the world hash). 31 callbacks: rides 5, 6, 7, 60, 62, 71, 73, 74, 75, 77, 78, 79, 81, 85, 259-261; cleanliness 8, 9, 10, 86-89, 262; guest mood 3, 4, 82, 145,
158, 159. The thresholds are words of the `Advisor.sam` object (`AdvisorScan.ParameterKeys`); a key the file lacks leaves its callback at 0, as without a park.

| Helper | OpenTPW | Reading |
| --- | --- | --- |
| `0x4C9960` | `ParkStaff.OfType(role).Count()` | things of the models 4 to 8 equal to the argument with flag byte `+3` clear |
| `0x4C9070`, `0x4C90D0` | the advisor's own record of the turn of the newest object construction and newest hire (`mLastObjectBuilt`, `mLastStaffHired`), kept from the construction events (`ObjectBuilt` for a purchase or the Move tool's re-placement, `FixedItemMade` for an arrival vehicle at its first trip and the finale's End) and the `StaffHired` event, stamped at park init like `0x4C58C0` (`0x4C5CAF`, `0x4C5CDE`; a new park's gates and lights, constructed after it, stamp the same turn 0) and saved in the advisor snapshot | days = (park time - stamp, both from the calendar base) / one day; outside the economy and its hashes. Days are signed: a stamp still to come, which only a modified save holds, counts negative days as `__alldiv` does. A park from an original save takes its analyser's saved words (`SaveAnalyserStamps`) as the turns of the save's calendar (its `FunnyTimeStart` and rate) whose park time they are (`OriginalEconomyImport.AnalyserStampTurns`; a stamp between two turns' times, which only a modified save holds, is taken at the earlier turn and reported). Only an OpenTPW save from before the snapshot kept them, and a save that names no analyser, derive them at attach from the newest standing object and employed member (ADVISOR-009) |
| `0x4CA450`, `0x4CA510`, `0x4CA570`, `0x4CA5D0` | catalog types by `RatingType` (`+0x4AC`) that are available (`+0x10` of the object control: outside the research tables, or researched) | rides skip the types with a golden ticket cost (`+0xC4`) at flag 0, the others do not test it |
| `0x4CA7B0(kind, flag)` | catalog types with `Counters[Built]` above 0 | the control's built count (`+0x18`) is never lowered: a sold ride still counts as built |
| `0x4CA8E0`, `0x4CA9B0` | standing objects of the kind (a ride also by upgrade level), the closed ones | closed is the open word `+0x68` clear: the player's close, and the broken, upgrading and condemned states clear it (`0x4E0FD8`, `0x4E10DC`, case 4 of `0x4E0E60`) |
| `0x4CA310`, `0x4CA3A0` | rides in state 1; rides whose condition byte (`+0x44`, `Repair`) is below the percent or in state 1 | a condemned ride (state 4) is neither |
| `0x4CCDF0` | littered cells x 100 / (path, queue and entrance cells, types 1, 3, 9, plus littered ones off them) over the 128 x 128 map | a per-cell scan, not the 4 x 4 block caches |
| `0x4C81B0` | cells of the types 1, 3, 4, 9, 10 | the block caches (`0x4C8C40`, age 0) are recounted |
| `0x4C9EE0`, `0x4C9F90`, `0x4CA160`, `0x4CA220` | toilets (`ProvidesRelief`), closed ones, the integer mean of 100 - condition byte; the worst toilet's handle is not 0 exactly when a toilet exists | the callbacks 86-89 also store that handle in the object (`+4`), presentation only |
| `0x4C9130`, `0x4C93F0`, `0x4C9340`, `0x4C9290`, `0x4C9530`, `0x4C95D0`, `0x4C7BB0` | `IParkGuestStatistics.GuestCensus`, `CountGuestsNeeding`, `GuestsWithBalloons`, `GuestsInCostume`, `CensusHappiness` | guests on a cell the predicate `0x4FA990` accepts; the meter byte is compared unsigned with the argument's low byte; the mean is the single of the integer sum over the count |

What reading the bytes corrected from the first mapping above (the tables carry the corrected formulas):

- The two stamps are the **newest** construction and hire, not the first (`0x4DB090` and `0x504B90` rewrite them every time), and the park init stamps them with the current time; the days-since tests use them as they are.
- 73, 77 and 81 ask for the **previous** kind's built types (rides for the shops, shops for the sideshows, sideshows for the features), and 62 counts the ride types with the ticket items.
- 74 pushes kind 1 for both counts where 78 pushes sideshows and rides, so shops x ratio exceeds shops only for a ratio above 1: with the shipped ratio 0.15 the line never passes.
- 82 has no ride test: park open, no toilet, the needy share. 145 lets a mean equal to the threshold pass (`fcompp`, mask 0x41). 259 tests the park open, 260-262 do not.
- 85 computes `0x4CCA40` and discards it (`fstp st(0)`, then `xor eax, eax`): the line never passes, so it is "wired" as a port of a callback that returns 0.
- 8 passes only up to `LitterPointCutoffHigh`, 9 from `LitterPointCutoffLow` of its own group; 86-89 use the points of bands 1, 1, 3, 4 and the thresholds 1-3, 2-4, 3, 4.

Event posts: **66** is raised by the wear step (`ParkEconomy.Wear`, `ParkEventKind.RideBreakdownImminent`) when the new condition byte is below 10 and the byte plus the step's wear is not
(`0x4DF670` posts message 0x42 at `0x4DF8BE`). **68, 69, 258** are decided on `ParkEventKind.RideEnteredBrokenState` (raised when the ride actually enters its broken state: the type 7 message of case 1 of `0x4E0E60`, `0x4E0FA0`, sent after the script reports VAR_BROKEN; type 8 for a condemned ride, `0x40F3D0`; not at the earlier request `0x4E0B90` that raises `RideBrokeDown`) by `AdvisorRideEvents.ForBreakdown`: a ride whose life gauge byte is 0 is condemned
(258, the message of type 8); otherwise the handler `0x4DADC0` posts 68 without a mechanic and 69 when every mechanic is busy with another ride, resting or on strike (display state 3 or 4 of
`0x5064F0`, which is `StaffState`); a mechanic already on this ride or a free one posts nothing there (70 is the dispatch's, `0x4DA6C5`). Their scores are the words `+0x248`, `+0x24C`,
`+0x250` and `+0x578` (`RideCondemned.Score`, added to `EventScoreKeys`).

Tests: `AdvisorScanParkTests` (each callback at its thresholds over hand-built states, the real queue through a whole scan pass, the statistics over a real `ParkEconomy`, the guest bridge, the ride
events) and `AdvisorProducerInventoryTests` (every ported callback is "wired"). 117 scanned callbacks remained unported then (21 constant-zero stubs, 96 computed); the queues, prices and research batch ported 31 of the 96, so, with the 38 staff callbacks and the 18 of the challenges batch, 9 computed ones remain (and the 21 constant-zero stubs: 30 scanned messages in all, 9 `blocked` and 21 `scan-zero` rows).

## Queues, prices and research callbacks

Ported by `AdvisorQueuesCallbacks` (`source/OpenTPW/World/AdvisorScanQueues.cs`) over `IAdvisorParkState` and the new `IAdvisorQueuesState`, which `EconomyParkState` also implements (a partial class; the scan reaches it
through the park state it already gets). 31 callbacks: the researchers 15, 16, 17; research more 61, 72, 76, 80; upgrades 63, 64; the ticket price 91, 92; the shop value and satisfaction lines 238-257. Their thresholds
are words of the `Advisor.sam` object (`AdvisorQueuesCallbacks.IntegerKeys`, added to `AdvisorScan.IntegerParameterKeys`); a key the file lacks, or a park state without the queues view, leaves a callback at 0.

| Helper | OpenTPW | Reading |
| --- | --- | --- |
| `0x4C9830`, `0x4C9920` | `ParkPriceStatistics` (`PriceRingMean`, `PriceRingHoldsFifty`) | the integer mean (truncated, a byte) of a ring's held values; 50 held values |
| `0x4FF5B0` | `BoothVerdict.Opinion`, recorded by `GuestSimulation.PayAdmission` | the booth verdict records the byte of `PeepInfo.OpinionForMuchTooHigh`, `OpinionForTooHigh`, `OpinionForTooLow` or `OpinionForAboutRight` (`0x7850CC`-`0x7850D8`, key records `0x740430`-`0x7404E4`) in category 0 of the park's perception rings for the verdicts far too expensive, expensive, cheap and fair; the early fair exit (a park value and a fee both at or below the minimum entry fee) records nothing |
| `0x4CADF0`, `0x4CB640`, `0x4CC090` | `WorstValue` | the attractions of the group (`UsageInfo.ShopType` 2, 3, 4; 1, 5 or 6 for the retail shops; type kind 2 for the sideshows) older than 30 days (`0x4DD670`: the clock less the build time, in days) whose customers ring (`+0x1A8`) sums to more than 0: the sum of the walk-away ring (`+0x230`) x 100 / that sum, unsigned; the lowest, the first of equals, and its handle |
| `0x4CAAE0`, `0x4CB300`, `0x4CBBA0` | `BestValue` | the same value, the highest |
| `0x4CB200`, `0x4CBA90`, `0x4CB100`, `0x4CB980`, `0x4CBEC0` | `WorstSatisfaction`, `BestSatisfaction` | 50 plus the mean of the satisfaction ring (`0x4E1E30`) of the group's attractions older than 30 days, the lowest or the highest; the sideshows' best (`0x4CBEC0`) also needs a customers sum above 0 |
| `0x503B30`, `0x4CA630`-`0x4CA690` | `ResearchOutstanding` | the lab's projects of a category (ride, shop, sideshow, feature) that are not finished |
| `0x503980` | `ResearchRate` | a category's share of the research (`ResearchFluid.Rate` without the lab's current-project test) |
| `0x4C9E40(4)`, `0x508380` | `ResearcherCandidates` | the valid slots of the 32-slot hiring pool whose type is 4 (`StaffType.Researcher`) |
| `0x4C9E60`, `0x4C9ED0` | `ResearchDone`, `ResearchCapacity` | the researchers' ability summed by grade; `MaxResearchersInPark` x the best grade's ability |
| `0x4CA4C0` | `ResearchedUpgradeTypes` | the ride types whose control's researched level (`+0x14`) is above 0, a ride type without upgrade rows counting once its base is researched (`ParkResearch.ResearchedLevel`) |

What reading the bytes corrected from the first mapping (the tables carry the corrected formulas):

- The shop figures count only attractions **older than 30 days**, not shops with more than 30 customers: `0x4DD670` returns the age in days. The value helpers also skip an attraction whose customers sum is 0.
- The "value" is the **walk-aways x 100 / customers** over the 30-day rings, so a higher figure means more refusals; the callbacks compare it as if higher were better.
- Every callback scores **(figure - threshold) x ScorePerPoint** with the figure first. For the great lines (figure above the threshold) that is positive; for the poor lines (figure below it) it is **negative for a positive `ScorePerPoint`**, which the shipped file has, so
  the ten poor lines can never pass `MinScoreForConsideration` and are "wired" as ports of callbacks that never pass. With a negative multiplier they would speak.
- 247 (sideshows, poor satisfaction) calls the sideshow **value** helper `0x4CC090`, as 242 does; the other four poor satisfaction lines call a satisfaction helper.
- Each shop callback also keeps the picked handle (word `+4` of the object) and figure (`+8`); `0x59B590` copies them into the queued advice through `0x59C0A0`. OpenTPW has no reader of the handle (the lines are speech only).
- 91 and 92 read ring **0**, which neither the price test `0x4FDE50` (rings 1-4, 6, 7, 9) nor the guest happiness recorder `0x4FD970` writes: only the booth verdict `0x4FF5B0` does. OpenTPW's booth now records it
  (`PeepInfo.OpinionFor*`, read into `GuestSettings` only when the file has the keys), so the ring fills as the original's does.
- 15 needs **no** researcher and every other research line needs one; 16 has no test for 0 available ride types where 15 has; 61, 72, 76 and 80 test **no park-open state**; 80 (features) is the only one of the four that does not test the research rate.
- 17 needs a researcher hired **and** one waiting in the hiring pool, the clock's months at least `MonthsIntoGameForConsideration`, and the research done x 100 / the research capacity (unsigned) below `SlowerThan`.
- 63 and 64 test the upgrade research rate (`0x503980` of 4) for 0 and the upgraded ride types (`0x4CA4C0`): none for 63, above 0 and at most `LessAvailableThan` for 64.
- 61, 63, 64, 72, 76 and 80 are game-mode selector 0 (the full simulation only); 15, 16, 17, 91, 92 and 238-257 always.

Tests: `AdvisorScanQueuesTests` (each callback below, at and above its thresholds over hand-built states, the real queue through a whole scan pass, the statistics over a real `ParkEconomy`, the booth's opinion bytes) and
`AdvisorProducerInventoryTests` (every ported callback is "wired").

## Cleanliness

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 8 | wired | cb 0x59CB40 | park open and no handyman: litter points (0x4CCDF0) at most LitterPointCutoffHigh; score = litter x ScorePerLitterPoint - LitterPointCutoffLow | 14 |
| 9 | wired | cb 0x59CBB0 | park open and no handyman: litter points >= StaffHireHandymen2.LitterPointCutoffLow; score = litter x ScorePerLitterPoint - cutoff | 15 |
| 10 | wired | cb 0x59CC20 | park open with at least one handyman: developed cells (0x4C81B0) >= MinCellsForConsideration; (cells - handymen x IdealDevelopedCellsPerHandyman) x ScorePerExtra10Cells | 16 |
| 86 | wired | cb 0x59E450 | park open, no handyman, a toilet exists: mean toilet filth (0x4CA160 = 100 - condition) between FeaturesDirtyToilets1 and 3 FilthierThan: filth x PointsPerFilthPct | 89 |
| 87 | wired | cb 0x59E510 | park open, a handyman exists: mean filth between DirtyToilets2 and 4 FilthierThan: filth x PointsPerFilthPct | 90 |
| 88 | wired | cb 0x59E5D0 | park open, no handyman: mean filth >= FeaturesDirtyToilets3.FilthierThan: filth x its PointsPerFilthPct | 91 |
| 89 | wired | cb 0x59E680 | park open, a handyman exists: mean filth >= FeaturesDirtyToilets4.FilthierThan: filth x its PointsPerFilthPct | 92 |
| 262 | wired | cb 0x5A0430 | toilets closed (0x4C9F90) x 100 / toilets > MoreThanThisPercentClosed: flat ToiletsClosed.Score | speech only |

## Rides, shops, sideshows and features

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 5 | wired | cb 0x59CA30 | no mechanic hired (0x4C9960 type 4); else rides that are broken or below StaffHireMechanics1.PoorerStateThan percent condition (0x4CA3A0) x ScorePerWornRide | 11 |
| 6 | wired | cb 0x59CA80 | no mechanic hired; broken-down rides (state 1, 0x4CA310) x StaffHireMechanics2.ScorePerBrokenRide | 12 |
| 7 | wired | cb 0x59CAC0 | days since the hire stamp (0x4C90D0; the stamp is rewritten at every staff thing init, so it is the newest hire) >= WaitingTimes.DaysForStaff; max(0, rides (0x4CA8E0 kind 0) - mechanics x IdealNumberOfRidesPerMechanic) x ScorePerExtraRide | 13 |
| 60 | wired | cb 0x59D990 | ride types available (0x4CA450 without ticket items), days since the object stamp (0x4C9070; the newest construction) >= DaysForObjects, rides >= MoreRidesThan, built types x 100 / available types (unsigned) < PoorerThan: flat RidesPoorSelection.Score | 65 |
| 62 | wired | cb 0x59DAD0 | no ride type ever built (0x4CA7B0 kind 0, ticket items counted): RidesBuildRides.Score + days since the start x AdditionalPerDay | 67 |
| 66 | wired | ride wear step 0x4DF670 (0x4DF8C0): condition (+0x44) crosses below 10 percent (float 0x7005A0) in a step | the wear step takes the condition byte below 10 and the byte plus the step's wear is not below 10: flat RidesBreakdownImminent.Score (game types 0 and 2) | 71 |
| 68 | wired | ride-breakdown message handler 0x4DADC0 (type 7), jump to 0x4DAFF6 from 0x4DAE68 | a ride breaks down and no mechanic is hired | 72 |
| 69 | wired | 0x4DADC0 (0x4DAFF6): every mechanic is busy with another ride | a ride breaks down and no mechanic is free | 73 |
| 70 | wired | mechanic dispatch 0x4DA5B0 (0x4DA6C5) and 0x4DADC0 (0x4DAF7E): a mechanic takes the broken ride | a mechanic is on the way | 74 |
| 71 | wired | cb 0x59DCA0 | shops available, days since first object >= DaysForObjects, shops built >= MoreShopsThan, built types x 100 / available < PoorerThan: flat Score | 75 |
| 73 | wired | cb 0x59DDE0 | ride types built, days since the object stamp >= DaysForObjects, no shop type built: Score + days since the start x AdditionalPerDay | 77 |
| 74 | wired | cb 0x59DE80 | shop types built, days since the object stamp >= DaysForObjects, the shop types counted a second time (the original pushes kind 1 again where 0x59E110 pushes rides) x PerRideRatio above the first count, balance > WealthMoreThan: flat Score; passes only for a ratio above 1 | 78 |
| 75 | wired | cb 0x59DF30 | sideshows available, days since first object >= DaysForObjects, sideshows built >= MoreSideshowsThan, built types x 100 / available < PoorerThan: flat Score | 79 |
| 77 | wired | cb 0x59E070 | shop types built (the previous kind, not rides), days since the object stamp >= DaysForObjects, no sideshow type built: Score + days since the start x AdditionalPerDay | 81 |
| 78 | wired | cb 0x59E110 | sideshow types built, days since the object stamp >= DaysForObjects, ride types built x PerRideRatio above the sideshows, balance > WealthMoreThan: flat Score | 82 |
| 79 | wired | cb 0x59E1C0 | features available, days since first object >= DaysForObjects, features built >= MoreFeaturesThan, built types x 100 / available < PoorerThan: flat Score | 83 |
| 81 | wired | cb 0x59E2E0 | sideshow types built (the previous kind), days since the object stamp >= DaysForObjects, no feature type built: Score + days since the start x AdditionalPerDay | 85 |
| 85 | wired | cb 0x59E420 | park open: the per-toilet statistic 0x4CCA40 is computed and discarded (fstp st(0)); every path returns 0, so the line never passes | 88 |
| 258 | wired | CMsgRideCondemned (type 8); 0x59B060 posts 258 (0x59B448) | a ride is condemned (state 4) | speech only |
| 259 | wired | cb 0x5A02F0 | park open; rides >= AtLeastThisManyRides and closed rides (0x4CA9B0) x 100 / rides > MoreThanThisPercentClosed: flat RidesClosed.Score | speech only |
| 260 | wired | cb 0x5A0370 | shops closed x 100 / shops > MoreThanThisPercentClosed: flat ShopsClosed.Score | speech only |
| 261 | wired | cb 0x5A03D0 | sideshows closed x 100 / sideshows > MoreThanThisPercentClosed: flat SideshowsClosed.Score | speech only |

## Queues and prices

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 91 | wired | cb 0x59E730 | perception ring 0 (the booth's opinion of the entrance fee: 0x4FF5B0 records the PeepInfo.OpinionFor* byte of its verdict) holds 50 values (0x4C9920) and its mean (0x4C9830, a byte) is below TicketSatisfactionUnder: flat PricingTicketTooExpensive.Score | 94 |
| 92 | wired | cb 0x59E790 | same ring, mean above TicketSatisfactionOver: flat PricingTicketTooCheap.Score | 95 |
| 113 | blocked | cb 0x59EF60 | a queue that no path reaches (0x4CCB00): flat QueueUnconnected.Score | 134 |
| 238 | wired | cb 0x59FB60 | Food: the lowest value (walk-aways of the 30-day ring x 100 / customers of the customers ring, 0x4CADF0) among attractions older than 30 days with customers, below ValueWorseThan: (value - ValueWorseThan) x ScorePerPoint, value first, so negative for a positive ScorePerPoint and never passing | speech only |
| 239 | wired | cb 0x59FBD0 | Shops: the lowest value (walk-aways of the 30-day ring x 100 / customers of the customers ring, 0x4CB640) among attractions older than 30 days with customers, below ValueWorseThan: (value - ValueWorseThan) x ScorePerPoint, value first, so negative for a positive ScorePerPoint and never passing | speech only |
| 240 | wired | cb 0x59FC40 | Restaurant: the lowest value (walk-aways of the 30-day ring x 100 / customers of the customers ring, 0x4CADF0) among attractions older than 30 days with customers, below ValueWorseThan: (value - ValueWorseThan) x ScorePerPoint, value first, so negative for a positive ScorePerPoint and never passing | speech only |
| 241 | wired | cb 0x59FCB0 | Drinks: the lowest value (walk-aways of the 30-day ring x 100 / customers of the customers ring, 0x4CADF0) among attractions older than 30 days with customers, below ValueWorseThan: (value - ValueWorseThan) x ScorePerPoint, value first, so negative for a positive ScorePerPoint and never passing | speech only |
| 242 | wired | cb 0x59FD20 | Sideshows: the lowest value (walk-aways of the 30-day ring x 100 / customers of the customers ring, 0x4CC090) among attractions older than 30 days with customers, below ValueWorseThan: (value - ValueWorseThan) x ScorePerPoint, value first, so negative for a positive ScorePerPoint and never passing | speech only |
| 243 | wired | cb 0x59FD90 | Food: the lowest satisfaction (0x4CB200, 50 + mean of the satisfaction ring) among attractions older than 30 days, below SatisfactionWorseThan: (satisfaction - threshold) x ScorePerPoint, negative for a positive ScorePerPoint | speech only |
| 244 | wired | cb 0x59FE00 | Shops: the lowest satisfaction (0x4CBA90, 50 + mean of the satisfaction ring) among attractions older than 30 days, below SatisfactionWorseThan: (satisfaction - threshold) x ScorePerPoint, negative for a positive ScorePerPoint | speech only |
| 245 | wired | cb 0x59FE70 | Restaurant: the lowest satisfaction (0x4CB200, 50 + mean of the satisfaction ring) among attractions older than 30 days, below SatisfactionWorseThan: (satisfaction - threshold) x ScorePerPoint, negative for a positive ScorePerPoint | speech only |
| 246 | wired | cb 0x59FEE0 | Drinks: the lowest satisfaction (0x4CB200, 50 + mean of the satisfaction ring) among attractions older than 30 days, below SatisfactionWorseThan: (satisfaction - threshold) x ScorePerPoint, negative for a positive ScorePerPoint | speech only |
| 247 | wired | cb 0x59FF50 | Sideshows: calls the sideshow value helper 0x4CC090 as 242 does, not a satisfaction helper; the lowest value below SatisfactionWorseThan: (value - threshold) x ScorePerPoint, negative for a positive ScorePerPoint | speech only |
| 248 | wired | cb 0x59FFC0 | Food: the highest value (0x4CAAE0) among attractions older than 30 days with customers, above ValueBetterThan: (value - ValueBetterThan) x ScorePerPoint | speech only |
| 249 | wired | cb 0x5A0010 | Shops: the highest value (0x4CB300) among attractions older than 30 days with customers, above ValueBetterThan: (value - ValueBetterThan) x ScorePerPoint | speech only |
| 250 | wired | cb 0x5A0060 | Restaurant: the highest value (0x4CAAE0) among attractions older than 30 days with customers, above ValueBetterThan: (value - ValueBetterThan) x ScorePerPoint | speech only |
| 251 | wired | cb 0x5A00B0 | Drinks: the highest value (0x4CAAE0) among attractions older than 30 days with customers, above ValueBetterThan: (value - ValueBetterThan) x ScorePerPoint | speech only |
| 252 | wired | cb 0x5A0100 | Sideshows: the highest value (0x4CBBA0) among attractions older than 30 days with customers, above ValueBetterThan: (value - ValueBetterThan) x ScorePerPoint | speech only |
| 253 | wired | cb 0x5A0150 | Food: the highest satisfaction (0x4CB100, 50 + mean of the satisfaction ring) among attractions older than 30 days, above SatisfactionBetterThan: (satisfaction - threshold) x ScorePerPoint | speech only |
| 254 | wired | cb 0x5A01A0 | Shops: the highest satisfaction (0x4CB980, 50 + mean of the satisfaction ring) among attractions older than 30 days, above SatisfactionBetterThan: (satisfaction - threshold) x ScorePerPoint | speech only |
| 255 | wired | cb 0x5A01F0 | Restaurant: the highest satisfaction (0x4CB100, 50 + mean of the satisfaction ring) among attractions older than 30 days, above SatisfactionBetterThan: (satisfaction - threshold) x ScorePerPoint | speech only |
| 256 | wired | cb 0x5A0240 | Drinks: the highest satisfaction (0x4CB100, 50 + mean of the satisfaction ring) among attractions older than 30 days, above SatisfactionBetterThan: (satisfaction - threshold) x ScorePerPoint | speech only |
| 257 | wired | cb 0x5A0290 | Sideshows: the highest satisfaction (0x4CBEC0) among sideshows older than 30 days with customers, above SatisfactionBetterThan: (satisfaction - threshold) x ScorePerPoint | speech only |

## Guest mood and thoughts

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 3 | wired | cb 0x59C9D0 | guests whose thirst is above VisitorsThirsty.ThirstierThan (0x4C93F0, strictly greater, truncated byte; the shipped threshold 99 makes only a thirst of 100 count) x ScorePerThirstyPerson | 9 |
| 4 | wired | cb 0x59CA00 | guests whose hunger is above VisitorsHungry.HungrierThan (0x4C9340, same rule as 3) x ScorePerHungryPerson | 10 |
| 82 | wired | cb 0x59E380 | park open and no toilet built: guests needing the toilet above NeedingMoreThan (0x4C9290) x 100 / guests in the park (0x4C9130, signed) > MorePeopleThanThisNeeding: flat Score (no ride test) | 86 |
| 125 | wired | prank message (type 0xE, sender 0x4FF2EA); 0x59B060 case at 0x59B3C6 posts 125 | kind word 2 -> 127, 1 -> 126, 0 -> 125 (prankster id modulo 3) | 146 |
| 126 | wired | prank message type 0xE; 0x59B060 case at 0x59B348 | kind word 1 | 147 |
| 127 | wired | prank message type 0xE; 0x59B060 case at 0x59B2CA | kind word 2 | 148 |
| 141 | blocked | cb 0x59F4F0 | rides >= MinimumRides and the average ride fear (0x4CCBF0, cached for a second) > AverageScarierThan: flat VisitorsTooScared.Score | 159 |
| 145 | wired | cb 0x59F5E0 | guests in the park >= MinimumVisitors and the mean happiness (0x4C7BB0) >= HappierThan (equality passes): flat CongratVisitorsHappy.Score | speech only |
| 158 | wired | cb 0x59F940 | guests holding a balloon (0x4C9530, +0x210 nonzero) > CongratBalloons.MoreThan: flat Score | 174 |
| 159 | wired | cb 0x59F970 | guests in costume (0x4C95D0, +0x24 == 2) > CongratCostumes.MoreThan: flat Score | 175 |
| 264 | blocked | cb 0x5A04F0 | pranksters (0x4C94A0 arg 0) > MorePrankstersThan: flat LotsOfPranks.Score | speech only |

## Staff callbacks

The 38 staff rows below that were `ready` are ported (`AdvisorStaffCallbacks` in `source/OpenTPW/World/AdvisorScanStaff.cs`, over `IAdvisorStaffState`, which `EconomyParkState` implements next
to `IAdvisorParkState`; a park state without it, and a settings file without a threshold, score 0). Re-read from the code of the callbacks, this corrects the first inventory text in five places:

- **Set a patrol area** (18, 20, 23, 26: `0x59D0C0`, `0x59D250`, `0x59D400`, `0x59D4C0`): the score is `(100 - percent) x ScorePerUnpatrolledPct`, not `(DontBotherIfPatrolledUp - percent)`; `DontBotherIfPatrolledUp` is only the upper bound of the
  percent (`percent <= DontBother` passes). The months are the park clock divided by 30 days (`0x4F8800` / `0x1792F8648000`, whole days / 30). The original divides by the role's staff count even when it is 0, which only a `AtLeastThisMany` of 0 or less can reach: a fault there, 0 here.
- **Training** (28-32: `0x59D620`-`0x59D7A0`; congratulations 146-150): the "training in progress" word is the role's **training budget** in the staff HQ (`0x519450` returns the HQ at `+0x1DA718`; its budget words start at `+0xC` in the order handyman, mechanic, entertainer, guard,
  researcher, the training window stores them at `0x4B2534`-`0x4B25CB`). The advice to train needs the budget not above 0, the congratulations need it above 0. The "average training" `0x4C9C00(role)` is `grades x 100 / (members x 5)` over the members of the role (`+0x1E4`, the pay grade), not the training points.
- **Staff happy** (144: `0x59F590`): the staff's mean happiness (`0x4C7CC0`, the truncated happiness bytes added in an integer, then divided as a single; 0 without staff) must be at least `HappierThan` (equality passes), not strictly above it.
- **Train your staff** (172: `0x59FB10`) reads the byte `+0x20` of the same record, the HQ's `mHaveEverTrained`: the training window sets it when a budget above 0 is stored (`0x4B25D0`) and nothing clears it. It is saved (`0x50C640`), so an earlier note that no code reads it missed this line.
  OpenTPW keeps it as `ParkStaff.HaveEverTrained` (set by `SetTrainingBudget`, imported from the original save's HQ, written to the park save when set).
- **Too many staff of a role** (114-123): the words `0x7852E8` (handyman), `0x7852EC` (mechanic), `0x7852F0` (entertainer), `0x7852F4` (guard) and `0x7852F8` (researcher) are the schema fields `MaxHandymenInPark` to `MaxResearchersInPark` of `StaffPoolInfo` (five consecutive words in the schema's order; counting the schema's leaves on from `0x7852E8` through the rest of `StaffPoolInfo` and `Arrival` lands on
  `RestLevel`, the first key of `AllStaffConstants`, at `0x785324`, the word the tired test `0x506680` reads), read with `BalanceSettings` as `MaximumInPark`. The first line of a role needs the limit to equal `<Role>.MoreThan` and the role's staff to reach it; the V2 line needs the limit to differ from it and the staff to equal the limit.
  122 (`0x59F1A0`) compares the **guards'** limit with `MaxGuards.MoreThan` and counts the researchers against `MaxResearchers.MoreThan`: the original's copy slip, ported as it is.

The remaining inputs: busy points `0x4C81E0` = half the guests on census cells, at most 100 (the same census as `0x4C9130`); unhappy percent `0x4C9750(30)` = the census guests whose happiness byte is below 30 x 100 / the census
(`IParkGuestStatistics.CensusPercentBelowHappiness`); tired staff `0x4C9AA0` = energy byte below `RestLevel` (`0x506680`); staff with a patrol area `0x4C9B40(role)` = area corner words other than 1 and `0x4000` (`StaffMember.HasPatrolArea`; argument 0 counts every role);
all staff `0x4C9A10` (every thing of the models 4 to 8, where `0x4C9960` also tests the flag byte `+3`); visitors so far `0x4C94C0` = the lifetime counter `+0x21C08` (`ParkCounters.Admissions`); staff rooms `0x4CA030` / closed `0x4CA0C0` = the attractions with the rest-area flag (`UsageInfo.ChillsYouOut`,
bit 2 of `+0x32`) whose open word is 0. The coverage rows (19, 21, 27) and the prank rows (12, 22, 98) stay blocked.

## Staff

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 11 | wired | cb 0x59CCB0 | park open, days since the newest hire (the hire stamp) >= DaysForStaff, no guard: (busy points (0x4C81E0 = min(100, guests in the park / 2)) - AtLeastThisBusy) x ScorePerExtraBusyPoint, not clamped | 17 |
| 12 | blocked | cb 0x59CD30 | park open, no guard: StaffHireGuards2.Score + misbehaving kids (0x4C94A0 arg 0) x AdditionalPerMisbehavingKid | 18 |
| 13 | wired | cb 0x59CD90 | park open, days since the newest hire >= DaysForStaff, developed cells >= MinCellsForConsideration: (cells - guards x IdealDevelopedCellsPerGuard) x ScorePerExtra10Cells while above 0 | 19 |
| 14 | wired | cb 0x59CE30 | park open, no entertainer, more than 30 percent of the guests have a happiness byte below 30 (0x4C9750 of 30): (busy points - AtLeastThisBusy) x ScorePerExtraBusyPoint | 20 |
| 18 | wired | cb 0x59D0C0 | park months (clock / 30 days) >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40 x 100 / staff) <= DontBotherIfPatrolledUp: (100 - percent) x ScorePerUnpatrolledPct | speech only |
| 19 | blocked | cb 0x59D180 | months in game >= threshold, role count >= AtLeastThisMany, some patrol area set: uncovered-cell percent (0x4CD500) x ScorePerNonCoveredPercent, at least the coverage Score | speech only |
| 20 | wired | cb 0x59D250 | park months (clock / 30 days) >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40 x 100 / staff) <= DontBotherIfPatrolledUp: (100 - percent) x ScorePerUnpatrolledPct | speech only |
| 21 | blocked | cb 0x59D310 | months in game >= threshold, role count >= AtLeastThisMany, some patrol area set: uncovered-cell percent (0x4CD500) x ScorePerNonCoveredPercent, at least the coverage Score | speech only |
| 22 | blocked | cb 0x59D3B0 | a guard exists and the escape counter (0x4C94A0 arg 1) > StaffGuardBuildCamera.AcceptableTrouble: flat Score | 28 |
| 23 | wired | cb 0x59D400 | park months (clock / 30 days) >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40 x 100 / staff) <= DontBotherIfPatrolledUp: (100 - percent) x ScorePerUnpatrolledPct | speech only |
| 26 | wired | cb 0x59D4C0 | park months (clock / 30 days) >= MonthsIntoGameForConsideration, staff of the role >= AtLeastThisMany, percent with a patrol area set (0x4C9B40 x 100 / staff) <= DontBotherIfPatrolledUp: (100 - percent) x ScorePerUnpatrolledPct | speech only |
| 27 | blocked | cb 0x59D580 | months in game >= threshold, role count >= AtLeastThisMany, some patrol area set: uncovered-cell percent (0x4CD500) x ScorePerNonCoveredPercent, at least the coverage Score | speech only |
| 28 | wired | cb 0x59D620 | role exists, its training budget in the staff HQ (0x519450 +0xC to +0x1C) is not above 0, average training (0x4C9C00 = 20 x grades / members) < WorseThan: flat Score | 33 |
| 29 | wired | cb 0x59D680 | role exists, its training budget in the staff HQ (0x519450 +0xC to +0x1C) is not above 0, average training (0x4C9C00 = 20 x grades / members) < WorseThan: flat Score | 34 |
| 30 | wired | cb 0x59D6E0 | role exists, its training budget in the staff HQ (0x519450 +0xC to +0x1C) is not above 0, average training (0x4C9C00 = 20 x grades / members) < WorseThan: flat Score | 35 |
| 31 | wired | cb 0x59D740 | role exists, its training budget in the staff HQ (0x519450 +0xC to +0x1C) is not above 0, average training (0x4C9C00 = 20 x grades / members) < WorseThan: flat Score | 36 |
| 32 | wired | cb 0x59D7A0 | role exists, its training budget in the staff HQ (0x519450 +0xC to +0x1C) is not above 0, average training (0x4C9C00 = 20 x grades / members) < WorseThan: flat Score | 37 |
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
| 108 | wired | cb 0x59EDD0 | handymen hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 129 |
| 109 | wired | cb 0x59EE20 | entertainers hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 130 |
| 110 | wired | cb 0x59EE70 | mechanics hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 131 |
| 111 | wired | cb 0x59EEC0 | guards hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 132 |
| 112 | wired | cb 0x59EF10 | researchers hired > attractions (0x4CA8E0 kind 0) + FudgeFactor: flat Score | 133 |
| 114 | wired | cb 0x59EFA0 | the role's park limit (MaxHandymenInPark, words 0x7852E8-0x7852F8) equals MoreThan and the staff of the role >= MoreThan: flat Score; the V2 message is the line for a limit that is not MoreThan | 135 |
| 115 | wired | cb 0x59EFE0 | the role's park limit is not the first message's MoreThan and the staff of the role equal the limit exactly: flat Score (the repeat variant of the limit line) | 136 |
| 116 | wired | cb 0x59F020 | the role's park limit (MaxEntertainersInPark, words 0x7852E8-0x7852F8) equals MoreThan and the staff of the role >= MoreThan: flat Score; the V2 message is the line for a limit that is not MoreThan | 137 |
| 117 | wired | cb 0x59F060 | the role's park limit is not the first message's MoreThan and the staff of the role equal the limit exactly: flat Score (the repeat variant of the limit line) | 138 |
| 118 | wired | cb 0x59F0A0 | the role's park limit (MaxMechanicsInPark, words 0x7852E8-0x7852F8) equals MoreThan and the staff of the role >= MoreThan: flat Score; the V2 message is the line for a limit that is not MoreThan | 139 |
| 119 | wired | cb 0x59F0E0 | the role's park limit is not the first message's MoreThan and the staff of the role equal the limit exactly: flat Score (the repeat variant of the limit line) | 140 |
| 120 | wired | cb 0x59F120 | the role's park limit (MaxGuardsInPark, words 0x7852E8-0x7852F8) equals MoreThan and the staff of the role >= MoreThan: flat Score; the V2 message is the line for a limit that is not MoreThan | 141 |
| 121 | wired | cb 0x59F160 | the role's park limit is not the first message's MoreThan and the staff of the role equal the limit exactly: flat Score (the repeat variant of the limit line) | 142 |
| 122 | wired | cb 0x59F1A0 | the guards' park limit (0x7852F4, the original's copy slip) equals MaxGuards.MoreThan and the researchers >= MaxResearchers.MoreThan: flat Score; the V2 message is the line for a limit that is not MoreThan | 143 |
| 123 | wired | cb 0x59F1E0 | the role's park limit is not the first message's MoreThan and the staff of the role equal the limit exactly: flat Score (the repeat variant of the limit line) | 144 |
| 144 | wired | cb 0x59F590 | staff >= MinimumStaff and average staff happiness (0x4C7CC0, a single) >= HappierThan: flat CongratStaffHappy.Score | speech only |
| 146 | wired | cb 0x59F630 | the role's training budget in the staff HQ is above 0, staff of the role > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 147 | wired | cb 0x59F6A0 | the role's training budget in the staff HQ is above 0, staff of the role > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 148 | wired | cb 0x59F710 | the role's training budget in the staff HQ is above 0, staff of the role > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 149 | wired | cb 0x59F780 | the role's training budget in the staff HQ is above 0, staff of the role > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 150 | wired | cb 0x59F7F0 | the role's training budget in the staff HQ is above 0, staff of the role > Minimum and average training (0x4C9C00) > BetterThan: flat Score | speech only |
| 169 | wired | cb 0x59F9C0 | park months >= 3: tired staff percent (0x4C9AA0 x 100 / all staff) > PctOfStaffTired: flat Score | speech only |
| 170 | wired | cb 0x59FA60 | park open, visitors ever >= MinVisitorsEver, no staff at all: flat HireStaffGeneral.Score | speech only |
| 171 | wired | cb 0x59FAC0 | staff > MinStaffForMessage and nobody has a patrol area (0x4C9B40): flat SetPatrolsGeneral.Score | speech only |
| 172 | wired | cb 0x59FB10 | all staff > MinStaffForMessage and the staff HQ's mHaveEverTrained byte (+0x20 of 0x519450, set once a training budget above 0 is stored) is clear: flat TrainStaffGeneral.Score | speech only |
| 263 | wired | cb 0x5A0490 | staff rooms closed (0x4CA0C0) x 100 / staff rooms > MoreThanThisPercentClosed: flat StaffroomClosed.Score | speech only |

## Research

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 15 | wired | cb 0x59CEA0 | park open, no researcher (0x4C9960 of 8), ride research outstanding (0x4CA630), ride types available (0x4CA450) above 0 and not all built (0x4CA7B0), built x 100 / available (signed) above StaffHireResearchers1.Most: excess x PointsPerExtraPct | 21 |
| 16 | wired | cb 0x59CF50 | park open, no researcher, ride research outstanding, built ride types equal to the available ones (no zero test): flat StaffHireResearchers2.Score | 22 |
| 17 | wired | cb 0x59CFE0 | park open, research total (0x4C9ED0) above 0, months into the game >= MonthsIntoGameForConsideration, a researcher hired and one waiting in the hiring pool (0x4C9E40 of 4), research done (0x4C9E60) x 100 / total (unsigned) below SlowerThan: flat StaffHireResearchers3.Score | 23 |
| 61 | wired | cb 0x59DA30 | a researcher hired, a ride type built (0x4CA7B0), ride research outstanding, ride research rate (0x503980 of 0) 0, built ride types equal to the available ones: flat RidesResearchMore.Score; no park-open test | 66 |
| 63 | wired | cb 0x59DB40 | months into the game >= MonthsIntoGameForConsideration, a researcher hired, no ride type with a researched upgrade level (0x4CA4C0), upgrade research rate (0x503980 of 4) 0: flat RidesNoUpgrades.Score | 68 |
| 64 | wired | cb 0x59DBF0 | a researcher hired, ride types with a researched upgrade level (0x4CA4C0) above 0 and not above LessAvailableThan, upgrade research rate 0: flat RidesMoreUpgrades.Score | 69 |
| 72 | wired | cb 0x59DD40 | a researcher hired, a shop type built, shop research outstanding (0x4CA650), shop research rate 0, built shop types equal to the available ones (0x4CA510): flat ShopsResearchMore.Score; no park-open test | 76 |
| 76 | wired | cb 0x59DFD0 | a researcher hired, a sideshow type built, sideshow research outstanding (0x4CA670), sideshow research rate 0, built sideshow types equal to the available ones (0x4CA570): flat SideshowsResearchMore.Score | 80 |
| 80 | wired | cb 0x59E260 | a researcher hired, a feature type built, feature research outstanding (0x4CA690), built feature types equal to the available ones (0x4CA5D0): flat FeaturesResearchMore.Score; the only one of the four with no research rate test | 84 |
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

Ported by `AdvisorChallengeCallbacks` and `AdvisorChallengeMessages` (`source/OpenTPW/World/AdvisorScanChallenges.cs`) for the scan, and `AutomaticAdvisor.Challenges.cs` for the posts. 32 messages are wired here (the rows below
with 1, 2, 124, 140, 156, 157 and 185 and 282 of the other batches), by what reading the bytes showed:

- **The ring `+0x216D8` is the five-entry ring of the last arrival group sizes**, not the monthly visitors the first mapping took it for: the arrival update `0x4CF3E0` pushes the size of a group
  (`0x4CF5B0`, the log "Bus about to arrive with %d people") into it as the group starts (`0x4CF42A`; capacity 5 at `+0x216F4`, index `+0x216F0`, filled flag `+0x216F8`). `0x59F220` (124, hype fades:
  the park is younger than `HypeNewpark.MaxMonths`, at least two groups, the newest smaller than the one before by more than `DroppedPercent`), `0x59F440` (140, a big busload) and `0x5A0780` (281,
  exactly one group has come) read it. OpenTPW keeps it as the advisor's own record (`AdvisorArrivalRing`, fed by `GuestSimulation.ArrivalGroupStarted`, saved in the advisor snapshot), like the
  object and hire stamps; an original TPW save's ring is read from the analyser thing (`SaveArrivalRing`: after the recent-stays ring, the five-word ring serialized by `0x4CE660` as current, count, wrapped byte, scratch word and entries; the oldest first is slots current+1 to 4 then 0 to current once wrapped, else 0 to current). A legacy OpenTPW save without the field takes the ring as full once the park has had admissions (ADVISOR-028). Those five zero-size stand-ins cannot recover either the original count or the sizes from aggregate admissions: 281 can differ when the missing history held exactly one group, and the hype (124) and big-busload (140) scores can differ until real arrivals replace the relevant unknown values. Two to four held groups would not meet 281's exactly-one condition. Own-format saves preserve the signed entries: above the guest limit,
  `0x4CF6A6` (online 500) / `0x4CF704` (offline 1500) subtracts the counted guests without a floor, and `0x4CF451` stores that signed remainder unchanged. The hype callback compares signed
  (`0x59F31C`) and divides signed (`0x59F32A`); a negative newest group after a positive group can give a drop above 100 percent. It must not be clamped to zero. Own-format advisor validation
  accepts these signed values while keeping the five-entry bound, controller and speech checks. `RestoreSnapshot` still applies the ADVISOR-028 stand-in to both null and explicitly empty `ArrivalWaves` when admissions exist. This is a separate legacy host-snapshot policy, unchanged here; a known empty original ring is authoritative at `AttachLevel`, but that does not establish empty-ring fidelity through a later snapshot restore. These are static traces and
  bounded import/save validation changes; no original-runtime parity or approximation closure is claimed.
- **Original TPI arrival-history data is imported separately.** `SaveThemeParkIncArrivalRing.Read` follows the World-referenced model-13 analyser and its dynamic `ring5` fields inside that thing's exact body. It never reuses TPW offsets or substitutes monthly arrivals/admissions for group sizes. The native wire order is current entry, capacity, one raw wrapped byte, scratch word and signed 32-bit entries. The reader validates the raw byte as 0 or 1 rather than using the typed boolean, ignores scratch/inactive words, preserves negative values and returns active groups oldest first. Saved preceding short, monthly and recent-stay counts determine the position; there is no fixed body offset. Smaller native capacities are structurally supported, but this adapter requires five because the host retains five groups; that is a host-continuation policy, not a native-format restriction. A zero analyser reference yields unavailable history (`null`); a validated empty ring yields an empty list. Malformed metadata rejects the import before either the arrival history or statistics is exposed.
  `OriginalParkImport.ImportThemeParkInc` assigns that independent history alongside statistics. The real `ForThemeParkIncLevel` factory still uses fresh TPI money rules and `Import = null`, retaining its existing statistics adaptation without native economy, calendar, controller or advisor-clock restoration. `AutomaticAdvisor.AttachLevel` keeps the TPW import guard and additionally permits arrival data only for a real TPI money runtime with a TPI source world. A manually attached advisor can therefore consume the stored history, including authoritative native emptiness at attachment; this is **data readiness only**. The actual `GameFlow.EnterLevel` TPI branch returns after its experimental HUD, before advisor creation or attachment; the edition's advisor/save capability gates remain false and full TPI saved-game resume remains unsupported. Native TPI consumers have corresponding hype, large-group and exactly-one tests, but this import does not establish a TPI descriptor-ID mapping or enable TPW advice as a TPI advisor port.
  Asset-free C# tests and the focused Python source/arithmetic guard cover this bounded contract. Synthetic tests are not a populated/wrapped native-save witness, original-runtime validation or a C# test execution claim. ADVISOR-028 remains open for the unrecoverable pre-field host history and the separate empty-snapshot policy; the public TPI advisor and full-resume boundaries remain unchanged.
- **`0x4CA740` counts ticket items built**, not research: the types with a golden ticket cost that were built at least once, as a percent. 278-280 are three bands of it (above the group's threshold and not
  above the next group's; the last has no upper bound) while the park is open. 274-276 compare the guests in the park x 100 / `GoldenTicketLocal.PeopleInPark` (the runtime word `0x785794`; 0 gives no
  line), cut to 100, with the thresholds of A, B and C (all three can hold at once).
- **The ticket hints** (314, 316, 317) read the level's ticket thresholds (`0x785790` visitors, `0x785794` people, `0x785798` happiness, `0x78579C` guests; the `GoldenTicketLocal` words) times the single
  multiplier, rounded to a single and truncated; 314 needs the player to hold no ticket at all (`0x4D4AC0` = `0x5AF4A0`: the four global tickets and every park's local ones, the secret ones left out), 316 and
  317 need the park's own visitors (index 0) or happiness (index 2) ticket not won; each scores its flat `Score`.
- **A ticket win posts one of three messages by the key rule**: after the ticket's flag is set, the sum of all tickets (`0x5AF530`: global, local of every park and the two secret ones) / 3 plus the starting
  keys must be above 0 and the sum a multiple of 3 for a key outcome; the outcome is 3 when those keys equal what one of the player's parks costs to enter (`Keys.CostToEnter`), 2 when none does and 1 otherwise.
  Local tickets (`0x4D4FA0`) post 215, 216 or 217, global ones (`0x4D5270`, first win) 220-222, the secret camera ticket (`0x4D5870`) 225-227, each with the **ticket's type as the response variant** (the fourth
  argument of `0x59B590`: 6 local, 4 global and 2 secret responses). Every ticket event then runs `0x4D5B30`, which with exactly one ticket in all posts 311 (first golden ticket) and 315 (the clues, one point
  below it in `Advisor.sam`); the moved outcome of a global ticket posts 234 + type first. OpenTPW reads the player's ledger through `AutomaticAdvisor.PlayerTickets` (`PlayerTicketLedger.AdvisorView`: the
  ledger and the park's own tickets counted once, so the order of the ledger's own update does not matter) and, without a ledger, the park's own tickets with the one starting key.
- **The challenge system posts three lines and the first-offer line.** The offer message builder `0x4D2C50` posts 270 for every offer (a follow-up's too, `0x4D2B60`), but **344** (a help descriptor of the
  tutorial group, `HelpMessage.Score`) in place of 270 when the offer is the first, the one that switches the system on (`0x4D2A70` called with 1 at `0x4D1F4B`); `ParkObjectives.LastOfferSwitchedOn` says which.
  The win routine `0x4D2FB0` pays or offers the follow-up, then `0x4D1A00` marks the slot now current as won and posts **272** when that slot has no follow-up type (the won challenge, or the follow-up offered in
  its place); the loss routine `0x4D3140` counts the loss and posts **273** through `0x4D1AE0`. 271 is the existing time-short warning.

Tests: `AdvisorScanChallengesTests` (every callback at, below and above its thresholds, the queue through a whole scan pass, the statistics over a real economy and guest simulation, the key rule, the posts
through the economy's events, the arrival ring and the snapshot).

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 215 | wired | golden ticket won, 0x4D4FA0 group 1..3 (0x4D51AD, 0x4D50F2, 0x4D5037), called from 0x4D4BF0 | a local ticket (visitors, people, happiness, all items, profit, recent visitors) is won and the key rule gives outcome 1, no new key: the ticket's type 0-5 picks the response; 216 is outcome 2 (a key is earned), 217 outcome 3 (the keys equal what a park costs to enter) | 180, 181, 182, 183, 184, 185 |
| 216 | wired | golden ticket won, 0x4D4FA0 group 1..3 (0x4D51AD, 0x4D50F2, 0x4D5037), called from 0x4D4BF0 | a local ticket is won and the key rule gives outcome 2: the tickets are a multiple of 3 and the player has a key, but no park costs that many keys to enter; the ticket's type 0-5 picks the response | 180, 181, 182, 183, 184, 185 |
| 217 | wired | golden ticket won, 0x4D4FA0 group 1..3 (0x4D51AD, 0x4D50F2, 0x4D5037), called from 0x4D4BF0 | a local ticket is won and the key rule gives outcome 3: the keys now equal what one of the player's parks costs to enter; the ticket's type 0-5 picks the response | 180, 181, 182, 183, 184, 185 |
| 220 | wired | golden ticket won, 0x4D5270 group 1..3 (0x4D52F3, 0x4D53B4, 0x4D546F) | a global ticket (coaster height, go-kart excitement, water length, big park) is won for the first time and the key rule gives outcome 1; the global type 0-3 picks the response (221 outcome 2, 222 outcome 3) | 186, 187, 188, 189 |
| 221 | wired | golden ticket won, 0x4D5270 group 1..3 (0x4D52F3, 0x4D53B4, 0x4D546F) | a global ticket is won for the first time and the key rule gives outcome 2; the global type picks the response | 186, 187, 188, 189 |
| 222 | wired | golden ticket won, 0x4D5270 group 1..3 (0x4D52F3, 0x4D53B4, 0x4D546F) | a global ticket is won for the first time and the key rule gives outcome 3; the global type picks the response | 186, 187, 188, 189 |
| 225 | wired | golden ticket won, 0x4D58F0 group 1..3 (0x4D5A7D, 0x4D59C2, 0x4D5907): the secret tickets | the secret camera ticket is won and the key rule gives outcome 1 (226 outcome 2, 227 outcome 3); the secret type is the response | 190, 191 |
| 226 | wired | golden ticket won, 0x4D58F0 group 1..3 (0x4D5A7D, 0x4D59C2, 0x4D5907): the secret tickets | the secret camera ticket is won and the key rule gives outcome 2 | 190, 191 |
| 227 | wired | golden ticket won, 0x4D58F0 group 1..3 (0x4D5A7D, 0x4D59C2, 0x4D5907): the secret tickets | the secret camera ticket is won and the key rule gives outcome 3 | 190, 191 |
| 234 | wired | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved to this park (the outcome 4 of 0x5AF940): AutomaticAdvisor.GlobalTicketMovedMessage posts 234 + type and AdvisorTables defines the four rows (AwardMoved.Score); 0x4D5B30 follows, like after every ticket outcome | speech only |
| 235 | wired | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved to this park (the outcome 4 of 0x5AF940): AutomaticAdvisor.GlobalTicketMovedMessage posts 234 + type and AdvisorTables defines the four rows (AwardMoved.Score); 0x4D5B30 follows, like after every ticket outcome | speech only |
| 236 | wired | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved to this park (the outcome 4 of 0x5AF940): AutomaticAdvisor.GlobalTicketMovedMessage posts 234 + type and AdvisorTables defines the four rows (AwardMoved.Score); 0x4D5B30 follows, like after every ticket outcome | speech only |
| 237 | wired | 0x4D5270 case 4 (0x4D554A-0x4D578A): a global ticket moved to this park, 0xEA + type | a global ticket moved to this park (the outcome 4 of 0x5AF940): AutomaticAdvisor.GlobalTicketMovedMessage posts 234 + type and AdvisorTables defines the four rows (AwardMoved.Score); 0x4D5B30 follows, like after every ticket outcome | speech only |
| 270 | wired | CMsgChallenge (type 25) from the challenge offer 0x4D2C50 (0x4D2D73); 0x59B060 forwards the id (0x59B489, id < 0x160) | a challenge is offered, every offer but the park's first (that one posts 344 instead): the offer of a follow-up challenge too | speech only |
| 271 | wired | CMsgChallenge from 0x4D1FF0 (0x4D223C) when the time left reaches ShortTimeLeftWarningAt | time-short warning of the running challenge | speech only |
| 272 | wired | CMsgChallenge from 0x4D1A30 (0x4D1A5B) | a challenge is won and the slot current afterwards (the won one, or the follow-up that was offered) has no follow-up type: the final win of a chain, after the follow-up's offer line | speech only |
| 273 | wired | CMsgChallenge from 0x4D1AE0 (0x4D1B42), the challenge-lost routine | a challenge is lost: its time ran out unmet (0x4D3140 raises the slot's loss count +0x24 through 0x4D1AE0) | speech only |
| 274 | wired | cb 0x5A08B0 | park open (0x51A280 clear) and GoldenTicketLocal.PeopleInPark (0x785794) nonzero: guests in the park x 100 / that threshold, cut to 100, > GTAmountVisitorsA.HigherPctThanThis: flat GTAmountVisitorsA.Score | speech only |
| 275 | wired | cb 0x5A0930 | park open and GoldenTicketLocal.PeopleInPark nonzero: guests in the park x 100 / that threshold, cut to 100, > GTAmountVisitorsB.HigherPctThanThis: flat GTAmountVisitorsB.Score | speech only |
| 276 | wired | cb 0x5A09B0 | park open and GoldenTicketLocal.PeopleInPark nonzero: guests in the park x 100 / that threshold, cut to 100, > GTAmountVisitorsC.HigherPctThanThis: flat GTAmountVisitorsC.Score | speech only |
| 278 | wired | cb 0x5A07D0 | park open: the share of the object types with a golden ticket cost that were built at least once (0x4CA740) above GTAllResearchedA.HigherPctThanThis and not above the B threshold: flat GTAllResearchedA.Score | speech only |
| 279 | wired | cb 0x5A0820 | park open: that share above GTAllResearchedB.HigherPctThanThis and not above the C threshold: flat GTAllResearchedB.Score | speech only |
| 280 | wired | cb 0x5A0870 | park open: that share above GTAllResearchedC.HigherPctThanThis: flat GTAllResearchedC.Score | speech only |
| 281 | wired | cb 0x5A0780 | the ring of arrival group sizes (+0x216D8) holds exactly one: flat GoldTicketExplanation.Score (the first arrival group has started) | speech only |
| 311 | wired | 0x4D5B30 (0x4D5BB6) after the first golden ticket was won | a golden ticket is won and the player's tickets in all, the secret ones too (0x5AF530), are exactly one: the first-ticket line; the clues line 315 follows at once | speech only |
| 314 | wired | cb 0x5A0530 | the player holds no golden ticket (0x4D4AC0) and the visitors ever (0x4C94C0) are above GoldenTicketLocal.Visitors (0x785790) x GoldTicketNearToFirstOne.PeopleMultiplier, truncated: flat Score | speech only |
| 315 | wired | 0x4D5B30 (0x4D5C47) clues for the remaining tickets | a golden ticket is won and the player's tickets in all are exactly one: posted right after 311 | speech only |
| 316 | wired | cb 0x5A05A0 | visitors ever above PeopleMultiplier x target and the visitors ticket (0x5AF7F0 flag 0) not won: flat GoldTicketNearToXPeeps.Score | speech only |
| 317 | wired | cb 0x5A05F0 | average happiness above HappinessMultiplier x target, guests above the target and the happiness ticket (flag 2) not won: flat Score | speech only |

## Tutorial and help

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 185 | wired | cb 0x5A06C0 | game days between TutorialUIPanel.AfterTime and CutoffTime: flat Score | speech only |
| 282 | wired | cb 0x5A0720 | game days between TutorialExplainTooltips.AfterTime and CutoffTime: flat Score | speech only |
| 323 | wired | same handler 0x59AE43 posts advice 323 (0x59AEC1) when the game mode word 0xFB3B7C is 2 (Instant Action) | flat PrebuiltPark.Score | speech only |

Help messages (score callback `0x59E8C0`, `HelpMessage.Score`) are posted by window and tool code through `0x486B00` and withdrawn through
`0x486B40`; the descriptor table has **82** of them. 81 are in `content/data/advisor-help-messages.toml`: message 291 names response 551,
which the response table does not have, so the table generator (and `AdvisorHelpTable`) leave it out, but its descriptor exists in
`advisor-descriptors.toml` and in the inventory (kind unused). The "81 versus 82" counts in the comments and docs are the same table
counted with and without 291.

- wired (43): 184, 188-189, 191-193, 195, 197-201, 203-209, 212-214, 284, 286-287, 289, 292-295, 297-305, 309, 337, 345-346
- posted in the original, no window here (3): 285, 307-308 (285 All Staff window, 308 address book, 307 postcard)
- posted by no window or tool in `tp.exe` (36 descriptors; 344 is the exception, see below): 186-187, 190, 194, 196, 202, 210-211, 266, 283, 288, 290-291, 296, 306, 322, 324-336, 338-344

Of these, **344** is posted after all: the challenge system's first offer (`0x4D2C50`, the nonzero argument of `0x4D2A70`) posts it in place of 270, as an event (`AutomaticAdvisor.Challenges`), so its inventory row is an `event` row, `wired` (no call of `0x486B00` or `0x59B590` names it); the other 35 are the inventory's `unused` help rows, posted by nothing.

**204** was in that list until its producer was found: the kart and water line tool (modes 0xB and 0x10, `0x525A1C` → `0x526CB3`) stores a click as the line start when it has
none (`0x526D0F`), pushes 0xCC at `0x526D41` and jumps into the queue tool's post at `0x527629` (`0x59B590`, `0x5194D0` on `[0x7CF83C]`, `0x59A940`: the body of `0x486B00`),
the call the poster search had read as 0x151's only. The placement presets the start at the station's exit (`0x52A1A0`) and the track button's switch from the edit tool at the
open end (`0x530830`), and `0x52F580` keeps both, so the line tool reaches the click only after a line closed its circuit with exactly Ctrl held and no placement's queue
start pending (`0x526F95`, `0x527146`). Neither of `SetMode`'s withdrawal lists names 0xCC. Its row is `help` and `wired` now (`AdvisorToolHelp.TrackLineStart`, posted by `ParkHud.ReportCellToolClick`).

## Other

| Msg | Status | Producer | Condition and score | TAG_SYSTEM caption |
| ---: | --- | --- | --- | --- |
| 0 | wired | CMsgEvent 0 sent by the main loop 0x54ED70 (0x54F2D7) at every level start; handler 0x59AE43 posts advice 0 | flat Welcome.Score; no state read | speech only |
| 1 | wired | cb 0x59C8D0 | 0 while the park is open or while no ride and no sideshow is built (0x4CA8E0 kinds 0 and 2); else the guests in state 3 on the two ticket booth cells (0x4C91C0) x OpenPark.ScorePerWaitingPerson | 7 |
| 2 | wired | cb 0x59C960 | park open and neither a ride nor a sideshow built: flat ClosePark.Score; else 0 | 8 |
| 124 | wired | cb 0x59F220 | park younger than HypeNewpark.MaxMonths months and at least two arrival groups in the ring of the last five group sizes (+0x216D8): the newest group smaller than the one before by more than DroppedPercent percent (100 - newest x 100 / previous, the previous nonzero): flat Score | speech only |
| 128 | wired | CMsgEvent 3 from the open/close button routine 0x519EF0 (0x51A037); handler 0x59AF02 posts 128 | posted at every press of the open button, also when already open | 149 |
| 129 | wired | CMsgEvent 4 from 0x519EF0 (0x51A1C6); handler 0x59AF31 posts 129 | posted at every press of the close button, also when already closed | 150 |
| 140 | wired | cb 0x59F440 | the newest arrival group size in the ring (+0x216D8) larger than HowBigIsBig: flat BigBusloadArrived.Score | 158 |
| 156 | wired | cb 0x59F860 | park value (0x4C7DB0, read with age 10) > ValueBetterThan, guests in the park > VisitorsMoreThan, average happiness (0x4C7BB0) > HappinessGreaterThan: flat Score | speech only |
| 157 | wired | cb 0x59F8D0 | park value (0x4C7DB0, read with age 10) > ValueBetterThan, guests in the park > VisitorsMoreThan, average happiness (0x4C7BB0) > HappinessGreaterThan: flat Score | speech only |

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

## Restored advice (the `opentpw.restored-advice` extension)

Of the 103 messages tp.exe v2.0 never says (the status "none" rows), the official extension `opentpw.restored-advice` (version 1.0.0, **on by default**, listed on the Extensions and mods screen) restores **42**. Thirteen of the first 55 stood down (the ticket-win lines 99, 164, 165, 218, 219, 223, 224, 228, 229, the first-challenge hint 344, the double-or-nothing lines 319 and 320 and the track ride hint 204) because the original's own ticket advice (215-227), first-offer line (344), challenge offer and win lines (270, 272) and kart and water line tool help (204) are now ported; a restored line never tells an event the original already tells. The extension gives them a trigger and posts
them through the advisor's own queue. The producer map keeps the original's status for them (the rows stay `none`, `scan-zero` or `unused`); each restored row carries an extra `restored = "<topic>"` field in `content/data/advisor-producers.toml`, and
`RestoredAdvice.Entries` (`source/OpenTPW/World/RestoredAdvice.cs`) is the one list of them, kept equal to the inventory by `AdvisorProducerInventoryTests`. Every trigger is labelled `[EXT:restored-advice-<topic>]` at its site. Messages are
referred to by id only: the captions, the `Advisor.sam` values and the speech transcripts are game content and are not in the repository.

**Why it is safe to be on by default.** `AffectsSimulation` is false: the extension only reads park state (`ParkRestoredAdviceState`) and posts advice. The advisor is outside the world state hash (`WorldStateHash` has no advisor member), nothing the
extension does writes the economy, the guests or the cell map, and its few bookkeeping values (the poll's place, the vehicle last seen, the follow-up count) live in memory and are never saved. A park save records no mod for it, so `ParkResumeMods` (active = recorded)
accepts every save made with or without it. The advisor state a save carries (`AdvisorControllerSnapshot`: pending records, history, the action reservation, the scan cursor) has the same shape either way, and all 351 descriptors exist whether the extension is on or off, so a
save with a restored message pending or said loads with the extension off, and a save from before it loads with it on. The scan cursor walks the same rows with or without it (a row's restored callback only changes what that one row scores).
`AdvisorRestoredAdviceTests` checks both directions, the hash with the extension on and off, and the mod records.

**How it plugs in** (a few touch points, so the scan driver stays the original's):
- *Scan-zero rows.* `AdvisorScan.ScoreOf` asks `RestoredAdviceScan.CallbackFor(message)` for a row whose callback is `0x5D60C0`; the hook answers only while the extension is on, and `AdvisorScanContext.Restored` is the read-only park view (null while off, so every restored callback scores 0).
- *Rows nothing posts.* `RestoredAdviceDriver.Poll` evaluates one state-driven rule (`RestoredAdviceScan.PollRules`) with every scan call, in message order, and posts it above `MinScoreForConsideration` through `AdvisorController.AdviseRestored`.
- *Events.* `RestoredAdviceDriver.OnParkEvent`, `ObserveVehicle` and `OnlineScreen` post the award lines of tickets, upgrades, challenges, arriving vehicles and the online screens; the HUD's windows, tools and the message tube post the tutorial lines through `HudHost.RestoredHelp` and the window map `RestoredAdvice.WindowHelp`.
- Every post is admitted by the queue like any other advice: tutorial option (group 1), once-only, slap limit, pending-duplicate limit, repeat interval, the descriptor's game mode, and `MinScoreForConsideration` on selection. With the advisor option off the response still plays silently and its caption still goes to the tube (parameterised captions are not posted, as for every other advice).

**Repeat rules (no nagging).** The descriptor group's repeat interval (120 groups of four turns for group 0, about two minutes) is not enough for a condition that stays true, so the extension adds its own, `RestoredAdviceEntry.RepeatGroups`, read from the advisor's own history (which a save already carries, so nothing new is saved and a save loads the same with the extension on or off): the congratulations (151-154, 160) and the arrivals (137-139, one per vehicle kind, told when the kind changes from the one last seen, not at every group) are said **once per park**; the persistent lines (24, 25, 58, 59, 65, 83, 84, 173, 265, 277, 318, 347, 348) wait **5400 groups** (about 1.5 real hours at normal speed); the stranded riders (349, 350) and the upgrade lines (130-132) wait 900 groups. The
restored own scores are low (30 to 50, the two security lines capped at 60) so the originals (50 to 110, help 1000) keep priority; keys the original callbacks name keep their values (65, 265 and 349 still repeat only after their interval). `AdvisorRestoredRatesTests` plays an hour (14516 turns at 248 ms) of a park whose conditions all hold: **before 144 restored lines against 24 original ones, after 4 against 24**; a park with no guard or camera heard 25 **24 times an hour before and once after**, the original's 102 unchanged at 24.

**Scores.** The message's own key where its descriptor callback is a getter of one (the unused rows 84, 130-132, 137-139, 143, 230-233, 318 and the stub rows whose siblings name the key: 24/25 `StaffGuardCoverage`, 65 `RidesNoUpgrades`, 265 `RideExitBlocked`, 349/350 the
stranded-people and condemned keys); else the extension's own constant, documented where it is declared (`RestoredAdvice.QueueScore`, `FameScore`, ... `[EXT:restored-advice-<topic>]`). The restored keys are optional: a settings file without one leaves its message silent. The shipped file has them all (checked by `EveryRestoredMessageHasItsKeysAndItsGroupInTheShippedAdvisorSettings`).

### What is restored

| Group | Messages | Trigger |
| --- | --- | --- |
| Restorable with existing triggers (25) | 58, 59 | an open ride's queue holds 30 guests or more (59: three such rides, or half of the open rides, at least two; 59's speech sample is a silent stub, its caption plays); only the stronger line of the pair is posted |
| | 65 | after `RidesNoUpgrades` months: a researched upgrade is on offer and none has ever been bought |
| | 83, 84 | after 82 has been said, still no toilet and over twice `MorePeopleThanThisNeeding` guests with a toilet need above `NeedingMoreThan`; toilets exist but more than `MorePeopleThanThisNeeding` needy guests are 24 or more cells from the nearest |
| | 230-233 | the park earns a global ticket nobody held (coaster height, go-kart excitement, water length, big park): the award line |
| | 130, 131, 132 | an upgrade is bought with no mechanic hired, or while the mechanics strike; an upgrade is researched with no mechanic hired |
| | 137, 138, 139 | the bus, the seaplane or the Ferry starts an arrival group (`GuestSimulation.ArrivalVehicle` changing) |
| | 151, 152, 153, 154 | the park's best coaster height, go-kart excitement or water length record is 75 percent of its golden ticket's threshold (152: the park holds the coaster height ticket) |
| | 160 | retail shops sold 30 items or more in this and the last month, as many as half the visitors of those months |
| | 173 | free owned cells are under 8 percent of the owned terrain cells (the Buy Land hint) |
| | 277, 318 | the year's profit is 50 percent of the profit ticket's threshold (277), or `GoldTicketNearToXProfit.ProfitMultiplier` times it (318, the key the original's own getter reads), and the ticket is not won |
| | 347, 348 | the balance is still in the black but under twice (347), or under (348, with a loan available) the monthly staff wages; only the stronger of the pair |
| New tracking (5) | 24, 25 | security cover below, after `StaffSetGuardArea` months: the cover row's own gates (at least `AtLeastThisMany` guards, one with a patrol area) and 9 percent or more of the path cells uncovered (24); no guard and no camera in a park of 30 path cells (25); both scores capped at 60 |
| | 265 | a ride's exit has no path (below) |
| | 349, 350 | guests aboard a broken-down ride (349), or a condemned one (350) |
| Tutorial leftovers (6) | 196, 211 | the training budget window and the ride upgrade window open (screens `staffCosts`, `rideUpgrade`) |
| | 202 | a blueprint is chosen to place (204, which the extension posted as the roller coaster tool started, is the kart and water line tool's own help now) |
| | 283, 306, 322 | a park starts with the pop-up help off; the camcorder is armed; the first message reaches the tube (344, the first challenge, is the original's own now) |
| Online and postcards (6) | 328, 329, 331, 332, 333, 334 | the Publish Park, park details, other parks, Find Parks, unsent postcards (outbox) and online world screens open (the online extension's own screens, also over a park through Go Online) |

### The new tracking

- **Security cover (24, 25).** What the original's coverage counter is: `0x004CD340` first clears word `+0x3C` of every 0x44-byte cell record, then for each staff thing of the requested role (thing model 4 mechanic, 5 handyman, 6 entertainer, 7 guard, 8 researcher) whose patrol area is set (`0x506EB0`, rectangle `0x506E70`) writes the
  constant 1 to `+0x3C` of every cell in its rectangle; `0x004CD500(role, mode)` then returns the covered share (`+0x3C` above 0, times 100 over the count) of the cells that the mode's cell-type tests select (four lists, `0x4CD708`). It is a **flag per role, not a count**, and no camera enters it: the camera ticket reads another measure (`0x004CD720`,
  the environment security word, which OpenTPW has as `ParkEconomy.SecurityCoveragePercent`). Only the coverage callbacks of the mechanics, handymen and entertainers (19, 21, 27) call `0x004CD500`; the guard's (24) is the stub, so the original never measured guard cover. The restored cover joins both measures over the **path and queue cells** (where guests walk): covered when the cell has a
  security level (a camera's or a standing guard's `RegionFX` row) or lies inside the patrol rectangle of a guard who has one. Read-only, computed per call (`ParkRestoredAdviceState.SecurityCoverage`).
- **Blocked exit (265).** The placement lays the exit's outside cell as a path (`ObjectAccessCells.Lay`); a remove tool may take it (EXT:removable-access-cells). A ride with registered cells whose exit (its first exit, else its entrance) does not resolve for the guests is blocked: the outside cell is no path or queue cell and the level's nearby-path fallback finds none (`Level.WalkableNear`, the rule of `Level.ResolveVisitorCells`; `ParkRestoredAdviceState.ExitBlocked`).
- **Riders stuck (349, 350).** The ride's state (`ParkEconomy.ObjectState`: 1 broken down, 4 condemned) with the guests aboard (`RideVisitorBridge.RidersOnBoard` or its rider list, the larger).

### Skipped, and why

- **Silent stubs and placeholders**: 162, 163, 166, 186, 187, 190, 194 (stub samples, nothing to say or show) and 210, 267-269, 290, 312, 313, 324-327, 335 (the `z_error` placeholder sample 638), 291 (names response 551, which has no row).
- **Speech-only duplicates**: 161 and 321 repeat the key line of the ticket messages (every ticket kind has a criterion line); 180-183 are the welcome speech of the level bank that message 0 already says at level start.
- **No input to trigger from**: 90 (leaving guests with low energy, no such leaving rule), 155 (kart speed record, caption only and no speed measure), 67 (the ride-name variant of 66, which batch 2 posts), 168 (a surprise animation OpenTPW does not model), 266 and 296 (the Home key hint needs a lost-camera detector), 288 and 338-343 (the track editor's pylon, pivot and winch buttons are not in OpenTPW's coaster panel), 330 and 336 (chat-park details and back in the own lobby), 142 (new downloads on the original web site), 143 (a text message from the web site: saying it as the news screen opens is redundant and the news has no other event), 174-179 and 310 (the lobby and new-player tutorial: the front end has no advisor).
- Overlaps with batch 2 (82 and 66 are ported): 83 is the **escalation** of 82, said only after 82 has been said (`RestoredAdviceEntry.AfterMessage`) while there is still no toilet and over twice `MorePeopleThanThisNeeding` guests need one, so it never repeats 82; 67 stays skipped, 66 says the breakdown warning.

## Confidence and open points

- Every address and callback was read from the bytes of `tp.exe`; the polarity of the gates (a jump over a `ret 0` or into it) was checked against the
  captions. Formulas are readings of the instruction sequence; the coverage `0x4CD500` still needs a line-by-line port from the bytes before it is implemented (`0x4CCA40` is computed and discarded by 85; the litter points `0x4CCDF0` and the other helpers below are ported).
- The poster search lists every caller of `0x59B590`, `0x59A940`, `0x486B00` and the seven bus message types the controller registers
  (5, 8, 14, 17, 19, 23, 25 - `0x59B060` dispatch table). The constant-id scan inside the posting functions agrees; an id computed at run time
  from a table would not show up, so "no producer" means "no producer found". The search read the id pushed in each call's own block, so it missed 204, whose push
  jumps into another post's tail (`0x526D41` → `0x527629`, above). A byte scan for `push <id>` of each of the other 35 help ids no post names finds none that leads to
  `0x59B590`: nine are never pushed as a constant, and the other pushes feed text lookups, sounds and window, renderer or allocation calls.
- `0x4D4FA0` / `0x4D5270` / `0x4D5870` (golden tickets) post the outcomes 1-3 only (215-217, 220-222, 225-227); 218-219, 223-224 and 228-229 have no caller. 272 is the win of the last part of a challenge chain (`0x4D1A00`), 273 the loss (`0x4D1AE0`); 344 is posted by the first challenge offer (`0x4D2C50` with a nonzero argument), so its help descriptor's inventory row is an `event` row.
- Status "ready" says the input state exists, not that the formula is trivial; it also needs the scan driver (cursor, one descriptor per update, `MinScoreForConsideration`).

## Implementation order

1. **The scan driver** (no message by itself): the cursor, the mode selector, one callback per advisor update, the eligibility that `AdvisorController.Advise` already has.
2. **Money** (102, 103, 104, 105, then 100, 101, 107): the most visible warnings, all inputs exist and the numbers are small.
3. **Rides and breakdowns** (5, 6, 7, 60, 62, 66, 68, 69, 71, 73-75, 77-79, 81, 85, 258-261): done (see "Park callbacks"); 70 was already wired.
4. **Cleanliness** (8, 9, 10, 86-89, 262): done.
5. **Guest mood** (3, 4, 82, 145, 158, 159): done (the census members are on `IParkGuestStatistics`); 141 and 264 stay blocked.
6. **Staff** (11, 13, 14, 18, 20, 23, 26, 28-32, 108-112, 114-123, 144, 146-150, 169-172, 263): done (see "Staff callbacks"); **research** (15-17, 61, 63, 64, 72, 76, 80): done (see "Queues, prices and research callbacks").
7. **Prices** (91, 92, then 238-257): done (see "Queues, prices and research callbacks"); the booth fills ring 0.
8. **Challenges and golden tickets** (270, 272, 273, 215-227, 311, 315, 274-281, 314, 316, 317) and the park-opening, congratulation and tutorial callbacks (1, 2, 124, 140, 156, 157, 185, 282): done (see "Challenges and golden tickets"); 277 and 318 (the profit hints) stay with the restored advice.
9. **Blocked**: coverage (19, 21, 27), pranks (12, 22, 98, 264), queue links (113), ride fear (141), and the three help windows.
