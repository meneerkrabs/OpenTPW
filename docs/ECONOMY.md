# Park management simulation (economy slice)

Evidence date: October 9, 2026. Static reading of the original `.sam` settings, `.str` string
tables and the jungle `Easymode.TPWI` payload; no original executable was run, and a plain string search of `TP.ICD`
finds none of the setting names, so the formulas cannot be read from the binary. This is an OpenTPW simulation
**built on** original data, not a reproduction of the original rules: every place where the original
behaviour is unknown is marked *approximation* below and in the code documentation.

The Mac PowerPC build does contain the setting names and its code is readable. The ride wear, breakdown and
repair rules are traced there and implemented, see [reverse/RIDE-WEAR.md](reverse/RIDE-WEAR.md); only the
ride-state gate (ECON-023) remains an approximation; the life gauge starts at 100 (`0x100DAB1C`).

Code: `source/OpenTPW/Economy/` (simulation), `source/OpenTPW.Files/Formats/Save/SaveEconomyRecords.cs`
(original save records). Tests: `ParkEconomyTests` (synthetic, no assets) and
`ParkEconomyOriginalDataTests` (original data, inconclusive without `OPENTPW_GAME_PATH`).

## Settings sources and layering

| Layer | Files | Evidence |
| --- | --- | --- |
| Global balance | `data/levels/Standard.sam` | Header "Theme Park 2 Standard Balance file"; all money, staff, research, costs, challenge-timing and golden-ticket-global keys |
| Theme balance | `data/levels/<theme>/Standard.sam` | Repeats a subset (map, fixed items, weather, `StaffPoolInfo.AvgGradeOf*` = 2 in jungle/hallow, `PeepInfo.ExcitementToCostDivisor` = 5 in fantasy/space), plus `GoldenTicketLocal.*`, `ChallengesInThisLevel[n]` and `LoanInfo[n].Lendername` renames — so it is an override layer |
| Easy balance | `data/levels/jungle/Easy_Standard.sam` (only theme with one) | No header, only changed keys (cash 100,000, 0 % APR, lower wages, faster research). Proven to be the balance of `Easymode.TPWI`: the save's loan table only matches 0 % APR (below) |
| Challenge definitions | `data/Challenges.sam` | 35 `Challenges[n]` with comments describing each |
| Theme lobby data | `data/levels/<theme>/global.sam` | "has to be known about each theme before we have loaded the theme (ie, in the lobby)": tickets/keys |
| Objects | `<theme>/<category>/<Category>.sam` → object `.sam` (the one declaring `Info.Id`) → `Easy_<object>.sam` | Category files say "TOP-LEVEL DESCRIPTION" and hold defaults ("Always zero for shops"); object files repeat keys with specific values; `Easy_*` files hold only changed keys (e.g. `Easy_Bouncy.sam` wear rates, zero research costs). `Online_*` files are ignored offline |
| Online | `Online_Standard.sam` ("loaded INSTEAD of the normal STANDARD.SAM"), `Online_Rides.sam`, `Online_*.sam` | Online mode only; not used |

`SamDocument`/`SamSettings` keep every numeric/quoted value of a line (multi-value keys such as
`PeepTypes[0].PreferredExcitement.StartingCash.BoredomThreshold 80 300 40`), stop at trailing
free-text comments (which the files write without `#`), skip `Info.Shape`/`Info.Hoarding` blocks
and report the file that supplied each value.

## Evidence inventory

"Use" says how OpenTPW uses the key: **data** (value used as the rule the name/comment describes),
**approx** (value used inside an approximated rule), **exposed** (read and offered to another slice),
**unused** (meaning unknown or belongs to another system).

### Money, loans, costs

| Key(s) | Values | Meaning (from names/comments/strings) | Use |
| --- | --- | --- | --- |
| `BankAccountInfo.InitialCash` | 50,000; easy 100,000 | Starting bank balance | data |
| `BankAccountInfo.InitialAdmissionFee` | 20 | Starting ticket price (UITEXT 160 "Ticket Price") | data |
| `LoanInfo[0–7].LoanAmount` / `.APRInPercent` / `.RepaymentPeriodInMonths` | 10,000–100,000 / 18–23 % (easy 0) / 24–48 | Loan offers (UITEXT 170–180: lender, term, amount, interest rate, monthly repayment, total payable) | data; instalment per ORACLE F12 |
| `LoanInfo[n].Lendername` | 0–17, renamed per theme | Index into `LOANNAMES.str` (18 names: "Cash Unlimited" … "Jurassic Loans", "Inca Finance", "Pumpkin Bank" …) | data |
| `Costs.PathCell` / `QueueCell` / `KartTrackCell` / `WaterTrackCell` / `MapCell` | 20 / 75 / 400 (easy 200) / 500 (easy 250) / 100 (easy 10) | "How much incidental building costs" per cell; `MapCell` = land (UITEXT 134 "Buy Land") | data |
| object `Upgrades[0].CostOfUpgrade` | 0–10,000 | "cash cost when buying this item" | data |
| object `Upgrades[1,2].CostOfUpgrade` | ride upgrades | Price of upgrade levels (UITEXT 29 "Upgrade to level") | data |
| object `Upgrades[n].ScrapValueYear1–4` | 50/30/20/10, 60/40/30/15, 65/45/35/20 % | Scrap value (UITEXT 23) by age in years | data (percent); basis approx |
| `UsageInfo.InitPricePerUse` | shops 30–75, sideshows 10–20 | Shop sale price / price of game (UITEXT 40, 51) | data |
| `UsageInfo.InitCostOfGoods` | shops 20–50, sideshows 25–50 | Shop cost of goods (UITEXT 33); for sideshows the cost of prize (UITEXT 50) — inferred from the UI labels and `RipOffOK` "above average win" | data |
| `UsageInfo.InitChanceOfLoosing` | 70–75 | Sideshow chance of losing (UITEXT 49 "Chance of winning") | data |
| `UsageInfo.InitPrizeValue` | 25 (one file) | Prize value | unused |
| `UsageInfo.RipOffOK` | 100 / 250 | "%premium peeps willing to pay above 'average win'" | unused (guests slice) |
| `UsageInfo.GoldenTicketCost` | 0–5 (10 objects) | Golden tickets needed to buy (UIHELPTEXT 152) | data; traced: the first copy costs tickets and no cash, later copies cost cash (0x100DA874) |
| `UsageInfo.ShopType`, `UsageInfo.SpecialIngredient` | 1–6; 0–4 | Shop kind (jungle: 1 gift, 2 burger/fries/ice cream, 3 restaurant, 4 drinks, 5 costume, 6 balloon); ingredient "0 none, 1 Fat, 2 Salt, 3 Ice, 4 Sugar" (`INGREDIENT.str`) | data (challenge sales) |
| `UsageInfo.LitterEffect` | 0, 10, 25, 50 (burger, fries, drinks 50; ice cream 25; restaurants 10; balloon, costume, gift 0) | "How much litter to add" per purchase: added to the buying guest's litter meter (0..100), dropped at 90 (ORACLE F18) | approx (park-wide store, 1/100 item per point; ECON-020) |
| `PeepInfo.ExcitementToCostDivisor`, `MinimumEntryFee`, `Cheap/Average/ExpensivePriceMultiplier` | 4 (fantasy/space 5); 20; 0.75/1.25/2.0 (easy 1.5/2.5) | Visitors' entry-fee judgement | exposed (`BalanceSettings.EntryFee`) for guests |

Rides have no price key in any `.sam` and the ride info panel (UITEXT 17–31) has no price field:
rides are free; money comes from the gate, shops and sideshows (UITEXT 164–167).

### Staff

Role order everywhere: handyman/cleaner, mechanic, entertainer, guard, researcher/scientist
(`PerTypeStaffConsts[0–4]` comments, `STAFF_TYPES.str`, `HANDYMAN/MECHANIC/ENTERTAINER/GUARD/RESEARCHER_NAMES.str`, 35 names each).

| Key(s) | Values | Meaning | Use |
| --- | --- | --- | --- |
| `PerGradeStaffConsts[g].BaseWage` | 4,5,6,8,12 (easy 3,4,5,7,9) | Wage per grade 0–4 | data: monthly wage = BaseWage × PayMultiplier (product inferred from names) |
| `PerTypeStaffConsts[t].PayMultiplier` | 10,30,15,20,35 (easy 9,23,12,15,25) | Wage factor per role | data |
| `StaffPoolInfo.BeginningNumberOf*`, `Max*`, `Min*InPool` | 5/5/5/5/2, 6/5/6/4/3, 1…0 | Hiring pool sizes; refill drawn in proportion to each role's shortfall below `Max*`, then topped up to `Min*InPool` counting hired staff (traced: 0x100F6E24) | data |
| `StaffPoolInfo.Max*InPark` | 30/15/30/15/10 | Employee maximum per role (TAG_SYSTEM 135–144 say 10; online files use 10) | data |
| `StaffPoolInfo.ChanceToGetGreat*`, `AvgGradeOf*` | 20/10/35/2/15 %, 1–2 | Candidate quality: grade = average + (random mod 3) − 1, a −1 wraps to grade 4; `ChanceToGetGreat*` is not read (traced: 0x100F5B64) | data (`ChanceToGetGreat*` unused) |
| `StaffPoolInfo.TimeBetweenStaffUpdates`, `MaxNumberOfStaffPerUpdate`, `StaffTimeoutTime` | 90, 10, 120 | Pool refresh/expiry | approx (seconds at normal speed) |
| `StaffPoolInfo.BaseCostPerStaff`, `CostPerQualityLevel` | 2000, 100 | Unknown (hiring fee or pool valuation) | unused — hiring is free |
| `*ConstsPerGrade[g].PoundsPerTrainingPoint` | 5/8/12/15/0 (researcher 8/12/15/18/0) | "cost to raise the training level by 1%"; 0 = cannot improve grade 4 | data; 100 points per grade inferred from the online-file comments (10 × 100 = "1000 to get up to grade 1"), and the promotion at 100 points is read in `0x100F3588`. Month-end training follows `0x100FC1C4` (executed, ORACLE.md F17): the role budget is divided by the number of **all** the role's staff, and each member below the top grade costs the whole share, gets min(share / price, 100) points, and carries the points above 99 into the next grade; the top grade's share is not spent. Example: a budget of 1,000 for 3 handymen (grades 0, 0, 4) at 5 pounds per point gives each trainee 333 → 66 points and spends 666 (OpenTPW trained both for 500 → 100 points and a grade, spending 1,000) |
| `MechanicConstsPerGrade[g].WorkDuration` | 80…20 | "Speed of fixing improves with training" | approx (game hours per repair) |
| `HandymanConstsPerGrade[g].WorkDuration`, `.DetectionRange` | 40…5; 2–5 | Cleaning speed; litter detection range | WorkDuration approx (game minutes per litter item); range exposed only |
| `Entertainer/GuardConstsPerGrade` (`WorkDuration`, `HappinessEffectOnCell`, `ActivationDistance`) | | Entertaining/pursuit | unused (guests slice) |
| `ResearcherConstsPerGrade[g].WorkDuration` | 10–50 | "How long the researcher researches for" | unused |
| `PerGradeStaffConsts[g].IdleDuration`, `RecuperationRate`, `HappinessRecuperationRate`; `AllStaffConstants.*` | | Rest/happiness | unused (no staff behaviour yet) |
| `Upgrades[n].WearRate` | 0–5 "out of 10" (easy lower) | Ride wear | traced: scales the wear step every 64 turns (RIDE-WEAR.md) |
| `Upgrades[n].RedLineSpeed`, `Upgrades[n].RedLineCapacity`, `UsageInfo.MaxSpeed`, `UsageInfo.MaxCapacity` | Speed and capacity limits | Ride wear | traced: the speed and rider terms of the wear step |
| `Upgrades[n].DurationOfUpgrade` | 0/2/4 | "time taken for mechanic to carry out the upgrade — in conjunction with mechanic's WorkDuration" | approx (multiplies WorkDuration) |
| Advisor `StaffHireMechanics1.PoorerStateThan` | 25 | Worn-ride threshold | data (mechanic dispatch below 25) |

Staff states use `STAFFSTATES.str` order (Idle, Patrolling, Working, Resting, On strike, Picked up).
Strikes, happiness and patrol areas are not simulated.

### Research

| Key(s) | Values | Meaning | Use |
| --- | --- | --- | --- |
| object `Research.Category` | 0 ride, 1 shop, 2 sideshow, 3 feature, 4 upgrade | From the category files' comments | data |
| object `Research.Group` | 0–4 | "0 = Available initially" | data; group 0 of ride/shop/sideshow/feature starts researched |
| object `Upgrades[n].CostOfResearch` | 0–1,250 | "research points taken for item to be researched"; levels 1–2 = ride upgrades | data |
| `ResearchCategories[c].Effort` | 100/15/30/15/10 (easy 100/15/30/25/0) | Starting slider effort (UIHELPTEXT 244–248) | data |
| `ResearcherConstsPerGrade[g].ResearchAbility` | 2–6 (easy 6–20) | Research ability | traced: added every 20 park turns per researcher, × effort share × work load / 100 (0x100F0DF0) |
| `ResearchTech[g].PercentageForThisTech` | 0,0,80,85,85 | Threshold per group | approx (group g opens when this % of group g−1 of the same category is done) |
| `Research.StartingWorkLoad` | 85 | The lab's work load percentage that scales research points (balance +0x540, 0x100F0854) | data |
| `AddOn.UpgradesId`, `AddOn.UpgradeType` | ride Info.Id; 1 track, 2 other | Add-on object's ride | data (researchable once the ride is available; buildable once the ride is built) |

With the standard balance and three hired researchers every theme researches all items (92–102 per
theme) in 7–9 game years (test output); nothing deadlocks.

### Objectives, golden tickets, keys

The supplied Windows European English manual, `theme-park-world_win_manual_europe_en_ii5.pdf`,
PDF page 15 / printed page 28, confirms one golden key for every third earned ticket and explicitly
preserves earned keys when buying mystery items. `PlayerProgress` uses cumulative earned tickets,
not the spendable balance; tests cover thresholds 0/2/3/5/6, a mystery-item purchase and restoration
of earned/spent ticket state from an OpenTPW save. This proves the earning ratio and spending
invariant, but not the initial key count or whether entering a theme consumes keys: ECON-040 stays
registered for those assumptions. PDF identity: SHA-256
`c96eb25f3dc13f7f0824bbf03f9bbeb3bb94e9f4756d4d8dfa09ac71732b0668`.

| Key(s) | Values | Meaning | Use |
| --- | --- | --- | --- |
| `Challenges[n].Type/FollowupType/TargetTime/TargetVal/TargetObj/TargetObj2/TargetStaffType/Prize/CheckAtEndOnly/Independent` | 35 definitions | Each commented ("Sell 30 Drinks in 60 days" …) | data; type meanings from comments (approx) |
| `ChallengesInThisLevel[n].ChallengeType` | jungle 1 15 6 18 12 7 8 9 | Indices into `Challenges[]` | data — confirmed by the save |
| `Challenges.DaysUntilFirstChallenge/DaysAfterCompletedChallenge/DaysAfterDeclinedChallenge/DeclinesToForfeit/ShortTimeLeftWarningAt` | 540/270/270/2/20 | Offer timing | data (warning not raised) |
| `GoldenTicketLocal.Visitors/PeopleInPark/Happiness/AtLeastThisManyHappyPeople/ProfitYear/RecentVisitors/RecentVisitorMonths` | jungle 100/200/75/150/15,000/350/6; space 3000/350/85/150/30,000/500/6 | Per-theme ticket thresholds; award texts TAG_SYSTEM 180–185 | data |
| `GoldenTicketGlobal.CoasterHeight/GokartExcitement/WaterLength/MinCellsOwned/MinCellsCovered` | 105/90/50/3000/2000 | TAG_SYSTEM 186–191 (coaster, go-kart, water ride, big park, cameras, all land) | data; big park ↔ MinCellsOwned and cameras ↔ MinCellsCovered inferred |
| `Tickets.CanEarnTicketsInTheme`, `Tickets.CanSpendTicketsInTheme`, `Keys.CostToEnter` | 1, 1, jungle 1 / hallow 1 / fantasy 3 / space 5 | Lobby: keys needed to enter (UIHELPTEXT 338) | data; key earning manual-confirmed, initial key/entry persistence approx |
| Advisor `GT*`, `GoldTicketNearTo*`, `Wealth*`, `InTheRed*`, `WagesHigh.*`, `Bankrupted.Score` | | Advisor message scores | unused (advisor slice) |

The bankruptcy rule comes from text, not settings: TAG_SYSTEM 123–127 — "If you stay in the red for
six months you will be made bankrupt", warnings after three and five months, then "the banks have
closed your park". Ledger categories come from UITEXT 164–169 and 356–360.

### Settings read by other slices (inventory only)

`PeepInfo.*` (exit level, happiness changes, ride vomit, decision weights, opinions, pranks, ride
effects), `PeepTypes[0–7]` (excitement, starting cash, boredom), `Arrival.*` (min people, time
between arrivals, fixed rate, new-park bonus, points per visitor), `RegionFX[0–7]` (entertainer,
clean/dirty toilet, vomit, guard, camera, stink bomb, fireworks), `Seasons[]`/`Weather*`,
`Info.AttractionValue`/`NewAttractionDecayTime`/`Attraction[n].NewBonus`, `UsageInfo.*` effects,
`WaitingTimes.*`, `MapInfo.*`, `FixedItemInfo.*`. The park rating does not use `Info.AttractionValue`.

## Original save evidence (Easymode.TPWI)

`SaveEconomyRecords` reads two things of the save's World thing list (docs/TPWS-PAYLOAD.md,
docs/reverse/SAVE-plan.md): the bank account named by `mBankAccount` (model 16) and the
challenge system (model 19). `OriginalEconomyImport.Verify` then requires loan
amount/APR/term/lender metadata and challenge fields to equal settings exactly. Monthly
repayment must equal the original's instalment `trunc(P x pow(1 + APR/100, N/24) / N)` for every APR
(`LoanMath.MonthlyRepayment`, ORACLE F12: at 0 % APR that is `amount / months`, as this fixture shows;
five original PC saves store the same eight positive-APR values, 3651, 1825, 912, 365, 922, 1282,
2320, 2749).

| Thing data | Layout | Cross-check |
| --- | --- | --- |
| Bank words (after the 8-byte base) | 7 × 32-bit words: admission fee, balance, batch balance, withdrawals enabled, last balance, turn entered red, annual profit | Typed values: 25, 87987, 0, true, 87787, 0, −12013 |
| Loan offers | 8 × 32 bytes: available flag, i32 amount, i32 APR, months, monthly repayment, bought flag, months repaid, lender name index | Amounts, APR, terms and lenders equal `LoanInfo[0–7]`; repayments 2777, 1388, 694, 277, 750, 1000, 1666, 2166 = floor(amount / months), i.e. 0 % APR: only `Easy_Standard.sam` matches |
| Challenge slots | 20 × 45 bytes: `i32 type, time, value, u16 object, u16 object2, i32 staff type, prize, follow-up, u8 check at end only`, 13 raw bytes, `u8 independent`, 2 raw bytes; the leading slots with a nonzero type are used | Equal `Challenges[1, 15, 6, 18, 12, 7, 8, 9]` = jungle `ChallengesInThisLevel`, including the `Independent` flags (1,1,1,1,1,1,0,0) |

Proven and used: the loan instalment formula, the easy balance for the jungle original level, and
the challenge list.

The Mac serializer's named fields and 4-byte writes independently identify the bank words and
eight-word loan records, and the actual PC fixture matches that order. Lender name index is
independent of record order. The instalment is the same expression the Mac binary executes
(`0xcb7a8`, run in the PowerPC oracle, ORACLE F12), and OpenTPW uses it. The decoded balance 87,987 and annual profit
−12,013 are consistent with 100,000 − 12,013; the spending history behind that profit is not in the
save.

### Original save import

A park started from a shipped save (Instant Action, or the reference start) runs
`OriginalEconomyImport.Apply` before its first tick, in dependency order:

| Step | Source | OpenTPW target | Status |
| --- | --- | --- | --- |
| Turn | World `mGameTick` (755) | `Tick` = first tick of that turn; ledger month, hiring-pool timers and the first challenge offer day move with it | Traced (the turn the calendar converts); the save's calendar caches are checked against the resulting date (2 February 2000) |
| Bank | bank words | `Balance` (ledger opening balance), `EntranceFee`, `ProfitThisYear` | Traced field identities |
| Loans | bought loan offers | `Loans` (months repaid and instalment; outstanding = monthly × months left, as in the original) and taken offers | Field identities traced; outstanding balance SAVE-008 |
| Objects | RideSystem records (also those with an unverified footprint) + attraction things | registered uncharged; `Price`, `CostOfGoods`, `Level`, `Repair`, `LifeGauge` from the matching attraction (same Info.Id and cell) | Traced fields; state enum, chance of winning, totals, histories, names, operating, quality, queue and count fields, time stamp and mesh instance not imported (SAVE-006) |
| Staff | models 4–8 | `Staff.Members`: type, grade, training percentage, happiness, state, hire tick (rounded down to its turn) | Grade/percentage/happiness traced; state order, names, jobs, idle tick, role suffix SAVE-003 |
| Open state | World `mParkClosed` | the park opens when guests attach unless it is set | Traced |

Not imported and reported in `Evidence`: `mRandomSeed` (SAVE-001), guests (SAVE-002), the hiring
pool (SAVE-004), challenge timing, switch bytes, Value and ActualThing (SAVE-005), research
(SAVE-007), batch/last balance, withdrawals, turn entered red and the loan offers' Available flags
(SAVE-008), the staff HQ's training budgets, strike words, strike levels and trained byte (SAVE-009;
all-zero budgets equal OpenTPW's starting budgets), the opaque blocks, world state, arrivals,
calendar session start, the advisor controller's presentation words and current record, its pending
advice and history when no automatic advisor is attached, and the subsystem things without an
OpenTPW counterpart (SAVE-010). Staff values
outside OpenTPW's ranges are adjusted and each adjustment is reported (SAVE-003); an attraction
with out-of-range values keeps the defaults and is listed. The ledger's category history starts
empty: the save has none, and the month opens at the current balance: `mLastBalance` (87,787 in
Easymode against 87,987) is the balance after the last debit, not a month opening (SAVE-008).

The eleven saves of the second fixture set (docs/TPWS-PAYLOAD.md) corrected the challenge layout.
Easymode's challenges have no second object, staff type or end-only check, so they could not show
where those go. Every one of the 97 challenge records in those saves equals its `Challenges.sam`
definition under one layout: both object ids share the word at +12 (first in the low 16 bits),
`TargetStaffType` is the word at +16, and `CheckAtEndOnly` is the byte at +28. Halloween
`Challenges[24]` (Crypt Kart plus Firepit, 2150 and 2501) is stored as `0x09C50866`; space
`Challenges[25]` has staff type 1; fantasy `Challenges[10]` has the end-only flag. The old reading
(a whole word per object, 14 zero bytes) rejected those records, so it found only part of the
fantasy list and two lists in Halloween.

The verified PC `Easymode.TPWI` SHA-256 is
`6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`.
The layout review is documented in [PPC-review.md](reverse/PPC-review.md), with the field/call
evidence in [PPC-economy.md](reverse/PPC-economy.md). Regression tests cover positive APR,
field/flag decoding, truncated records, the challenge slot prefix, settings mismatches and the
runtime import (`SaveWorldTests`, `OriginalSaveImportTests`); original-data tests check the typed
PC fixture and the imported Instant Action start.

## Simulation model

| Area | Original-data-driven | Approximation |
| --- | --- | --- |
| Clock | Days, months, years, hours exist (challenge days, monthly wages/loans, yearly scrap, `Clock.RSE` HOUR) | Traced (Mac binary): one park turn per 248 ms, date = 2000-01-01 + turn × 3750 s through the civil calendar (a day is 23.04 turns, about 5.7 s), day/month/year events on date changes; approximate: sampling turns from the 60 Hz clock, the Gregorian script system; speeds Fast ×2/Fastest ×4 are OpenTPW additions (only pause is evidenced) |
| Ledger | Categories from UITEXT 164–169/356–360; integer dollars | Challenge prizes and scrap sales as "other income"; build, upgrade, goods, prizes and land as "other costs"; training as staff costs |
| Gate/shops/sideshows | Entrance fee, shop price and cost of goods, sideshow price/prize/chance of losing | Purchase requires money ≥ cost; litter per sale |
| Construction | Purchase, upgrade and cell costs; research gate; golden-ticket cost | Scrap value basis = catalogue cost up to the current level; park value = sum of scrap values |
| Loans | Offers, lenders, terms; instalment M = trunc(P x pow(1 + APR/100, N/24) / N) (F12); M paid flat for N months with no interest on a balance and no final remainder (F13); total payable M x N; early repayment M x months left, charged to the year profit as the whole-term share q x N (`0xccc78`, executed in the oracle); the year-profit word is 32-bit and wraps. **0 % APR (jungle Easy mode):** M = floor(P/N), so M x N < P and the unsigned share (M x N - P) / N is about 119 million a month; the original does this, and OpenTPW follows it on purpose (the year profit swings by that much and wraps) | The bank's withdrawals flag is assumed on (ECON-051) |
| Bankruptcy | Six month-ends in the red, warnings at 3 and 5 months (strings) | Advance stops when bankrupt |
| Staff | Pool sizes, maxima, wages, training prices | Candidate grades, pool timing, free hiring, 100 points per grade, mechanic/handyman job durations |
| Maintenance | Wear rates, red lines, maxima, upgrade durations, worn threshold 25 | Traced: wear every 64 turns from the ride script's running state, riders and speed; breakdown check every 8 turns; repair restores 100 but not the life gauge. the life gauge starts at 100 (`0x100DAB1C`). Approximated: the ride-state gate (ECON-023) |
| Research | Items, categories, groups, costs, effort, ability, thresholds, work load; traced: points every 20 turns, cumulative group opener, first open item in table order; points from researcher staff only, in both modes (Mac) | Info-id table order, the excluded researcher states |
| Challenges | Definitions, level list, timings, prizes, follow-ups | Type semantics from comments, except the build types 28 and 30–33, traced to the handler table at `0x100CE690`; explicit accept/decline; types 14, 22, 23, 26 unmeasured (23 and 26 never pass in the binary) |
| Golden tickets | All thresholds | Checked every 100 park turns in Full Simulation only; the profit ticket reads the year's running profit (`mProfitThisYear`, reset each calendar year); the first copy of a ticket object costs tickets, not cash (all traced) |
| Keys/progression | Keys per theme, theme order (THEMENAMES; ascending key cost); +1 per 3 earned golden tickets; spending tickets preserves keys (manual p. 28) | Start with 1 key; keys persist when entering themes |
| Park rating | — (traced: Mac binary 0x100C7B24) | Capped counts: guests in park × 20 / 1000 (max 20), rides × 1.5 (max 20), shops and sideshows × 2 (max 10 each), features (max 10), rides at upgrade level 2 (max 10), each staff type (max 4); the sub-kind to object-kind mapping is approximate |

### Game modes

A park start (`ParkStart`, `Economy/ParkStart.cs`) keeps three decisions apart: the economy rules
(`ParkGameMode`), the `Easy_` balance layer and importing the level's shipped `Easymode.TPWI`. The
front-end Game Mode button (`FrontEnd.GameMode`) is mapped to a start kind explicitly; neither enum is
the original GameType, front-end exit code or main-loop state (docs/reverse/PPC-scenarios.md).

| Start | Rules | `Easy_` layer | Shipped save | Evidence |
| --- | --- | --- | --- | --- |
| Full Simulation (front end) | Full Simulation | no | not read | Mac (static): layer added for GameType 2 only (`0x10474c`). An offline park entry reads the level, its balance and then only the newest `*.TPW*` in the player's theme directory (`0x198e50`); `easymode.TPWI` is named only by the copy made for Instant Action players (`0x137600`). A Full Simulation player's first park in a theme therefore has no seed |
| Instant Action (front end) | Instant Action | where the theme has one (jungle); object files too | imported where shipped (jungle) | Mac: same sites; the copy is the newest file on the first entry; a missing layer is not an error (balance `0x10474c`, object files `0x119878`) |
| Reference (`--load-original-level`, Load Park) | Full Simulation | when the shipped save is imported | imported where shipped | OpenTPW inspection path, unchanged; not an original GameType. Load Park ignores the last Game Mode choice (UI-041): the Mac saves no GameType with a park and takes it from the loading player's profile, which OpenTPW does not have; an own world save restores its saved start kind instead (docs/PARK-SAVES.md) |

Instant Action rules (`ParkModeFeatures`, Mac direct GameType tests; PC equivalence not established):
no golden-ticket checks and no challenges, no loan offers, research lab efforts cannot be changed,
no ride upgrades. Research points come only from researchers: the Mac code has no staffless research
path. The Instant Action seed ships with its staff, including a grade-2 researcher; the import takes
them (see "Original save import"), so research runs on the seed's own researcher. A start without a
seed (Full Simulation, or an Instant Action theme that ships none) has no staff. The native front-end
smoke run starts jungle in Instant Action from the menu and checks the seed, its imported bank, turn
and staff, research advancing over 60 days and each refused gate.

OpenTPW's economy-only park save stores `Mode` by name (`"FullSimulation"`/`"InstantAction"`, integers
rejected) and `Easy` as required members; neither has a default, so older version-1 saves already
carry both. A running park refuses a save of the other mode. That legacy file does not save the start
kind: the reference start is not an original state, and an imported seed is already in the saved objects and
staff. The park-resume world save (docs/PARK-SAVES.md) does save the start kind, together with the
mode and the closed-park state, and pins the three together — an Instant Action park restores under
the easy layer, a closed park stays closed. The legacy `SeedResearcherStandIn` member of earlier
version-2 saves is still accepted, ignored and written as `false` (the stand-in it described is gone).

Original flow that OpenTPW does not reproduce (Mac, static; docs/reverse/PPC-scenarios.md, "Park
entry"): leaving a park saves `<player>:<theme>:autosave.TPWS`, and the next entry resumes the
newest save there, so each player keeps one running park per theme. A park entry also writes a
`restart.INTS` snapshot. OpenTPW starts a new park on every front-end entry (UI-015).

Not implemented: player profiles and the per-player mode choice (UI-015), theme keys and the
Instant Action "no key check" rule (the front end has no key gate), the research and finance
panels themselves (the HUD buttons have no panel, so the UITEXT 467 refusal is not shown), the
Instant Action completion message (UITEXT 470), and the advisor rule filter by mode.

Determinism: one SplitMix64 state drives candidates and sideshow draws; time advances in whole ticks;
tests compare saves before/after load and after identical continuations byte for byte.

## Interfaces for other slices

- `IParkEconomy` (implemented by `ParkEconomy`): guests call `TryAdmitVisitor`, `TryBuy` (shop),
  `PlaySideshow`, `RecordRideUse`; rides report `ReportRecord(ParkRecordKind, instance, value)`
  (coaster height/loops, go-kart excitement/crossroads/sections, water length, cells owned/covered,
  all land, toilet cleanliness); everything publishes `ParkEvent`s through `EventRaised`
  (GuestPaid, ObjectBuilt/Sold, UpgradeBought/Completed, StaffHired/Fired, WagesPaid, Loan*,
  ItemResearched, Day/Month/YearEnded, InTheRed, BankruptcyWarning, Bankrupt, RideWorn/BrokeDown/
  Repaired, Challenge*, GoldenTicketWon).
- Guests are wired: `GuestSimulation.Payments` (`IGuestPayments`) is a `GuestEconomyBridge`, so
  admission, shop and sideshow money goes through `TryAdmitVisitor`/`TryBuy`/`PlaySideshow` (the
  economy's entrance fee and open state are the single source; closed park or too little money turns
  the guest back at the booth), rides call `RecordRideUse`, and the bridge implements
  `IParkGuestStatistics` (people in park, average happiness, happy-visitor count; balloons/costumes 0).
  The guest slice keeps no money totals, only purses and the `MoneySpent` event. Attractions are
  linked with `ParkEconomyRuntime.LinkAttraction` (the prototype Totem is registered uncharged as
  Info.Id 1110 in jungle; other themes do not have it, so its visits are not booked there).
  OpenTPW opens imported parks on load (the original open state is not decoded).
- `IEconomyObjectCatalog` / `EconomyObjectInfo` (rides/objects slice may supply its own catalogue);
  `ParkEconomy.TryBuild(infoId)` returns the instance id the placed object should keep;
  `RegisterExisting` for imported/fixed items; `SetObjectOpen`, `Sell`, `TryBuyUpgrade`,
  `TryBuyCells(CellPurchase, count)` for batches of paths, queues, tracks and land;
  `TrySpendCell(CellPurchase)` charges one path/queue cell when it is written, refused only
  when balance − cost < 0 (`SetCellType` `0x82ac4`, PATH-plan §3.2); `RefundQueueCell(ride)`
  credits `Costs.QueueCell` × the ride's `ScrapPercent` / 100 for a removed queue cell
  (`ClearCell` `0x85dcc`).
- `IParkClock` (`Tick`, `Date`, `Speed`) for the frontend and for the `YEAR/MONTH/DAY/HOUR` RSE opcodes.
- `ParkEconomyRuntime` (in `Level.Park`, original levels only): created from `OriginalPark`, ticked
  on the level's 60 Hz `FixedStepClock`, logs `Park clock: …; balance $…` per game day.

## Save format

`ParkSaveFile`: JSON, `"Format": "opentpw-park"`, `"Version": 2` (version 1 used the earlier 30-day calendar and is rejected). This is the economy-only save; the park-resume world save of docs/PARK-SAVES.md embeds this record by value in its own `opentpw-world` envelope together with the layout, guests, scripts and clock. It stores theme, difficulty and mode
(not the balance values, which are reloaded from the original files), tick, speed, RNG state, park
open/fee/bankruptcy/litter/tickets, ledger with current totals and up to 144 closed months, objects
with prices, levels, repair and statistics, loans, staff and candidates, training budgets, research
completion/progress/effort, challenge state, golden tickets and counters. Writes are atomic; loads
are capped at 16 MiB, reject unknown or missing members, other versions/formats, `.tpws`/`.tpwi`
paths, a theme/difficulty mismatch and inconsistent values (ids, ranges, ledger month vs clock).
Original TPWS/TPWI files are never written.

## Approximation register

Project rule: no value or rule may be invented except for upscaling, custom resolutions, online and
mods. Until proven, every economy approximation carries a `// [APPROX:ECON-NNN]` comment at its code
site, is listed in `Economy/EconomyApproximations.cs` and is logged once at startup as
`[APPROX:ECON-NNN]` warning when an original level loads. Values from original data are tagged
`// [DATA:<file>:<field>]`. Paths are relative to `source/OpenTPW/` (`Files/` = `source/OpenTPW.Files/`).

| Id | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| ECON-001 | `Economy/ParkCalendar.cs:38` | OpenTPW's fixed 60 Hz clock is sampled into 248 ms turns (14.88 ticks per turn), without the original's catch-up cap and scheduler phases | runtime turn timing under load and speed changes |
| ECON-002 | `Economy/ParkCalendar.cs:59` | the Mac OS date conversion (LongSecondsToDate, reached through 0x101C5C4C) uses the default Gregorian calendar | the script system of an original run |
| ECON-004 | `Economy/ParkCalendar.cs:11` | Fast x2 and Fastest x4 speeds (only pause is evidenced) | original speed controls, if any |
| ECON-005 | `Economy/ParkLedger.cs:54` | challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; profit leaves out loans received | the per-category ledger routines of other income and other costs (a loan received is money in and not profit: executed, ORACLE F14) |
| ECON-007 | `Economy/ParkEconomy.cs:320` | repaid loan offers reopen without the original credit-eligibility gate | implement the traced credit predicate and qualify its cross-edition behavior |
| ECON-008 | `Economy/ParkStaff.cs:50` | 100 training points per grade (from Online_Standard.sam comments "costs 1000 to get up to grade 1") | capture of a training run |
| ECON-010 | `Economy/ParkStaff.cs:64` | TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed | capture of pool refresh timing |
| ECON-012 | `Economy/ParkStaff.cs:152` | hiring is free; BaseCostPerStaff/CostPerQualityLevel unused | capture of the balance before/after hiring |
| ECON-014 | `Economy/ParkStaff.cs:24` | staff start at happiness 100 and it never changes (no strikes) | staff happiness rules (binary/captures) |
| ECON-015 | `Economy/ParkResearch.cs:169` | researchers on strike or picked up are the original's excluded states 3, 4 and 5 | the staff state values behind 0x100F4170 |
| ECON-017 | `Economy/ParkResearch.cs:148` | the research table is in info-id order and the player cannot step the cursor to another item | the table fill order (FUN_100c9064) and the next/previous control |
| ECON-018 | `Economy/ParkResearch.cs:48` | ride upgrade levels and add-on objects form the "upgrade" research category | research lab capture |
| ECON-020 | `Economy/ParkEconomy.cs:28` | a sale adds LitterEffect/100 litter items to a park-wide store; the original adds LitterEffect to the buying guest's 0..100 litter meter, which drops one litter event at 90 (ORACLE F18) | a guest litter meter with the drop-at-90 and bin rules, and the park's litter count |
| ECON-021 | `Economy/ParkEconomy.cs:243` | a repair takes WorkDuration game hours (x DurationOfUpgrade for upgrades); mechanics are dispatched instantly | capture of repair duration per grade |
| ECON-022 | `Economy/ParkEconomy.cs:261` | a handyman removes one litter item per WorkDuration game minutes, park-wide | capture of cleaning speed |
| ECON-023 | `Economy/ParkEconomy.cs:228` | the ride update's state gates (ride states 1, 3 and 4 at +0x198, +0x64 == 0, +0x2e bit 0) map only to "a mechanic is at work" | the names of those ride states and flags |
| ECON-025 | `Economy/ParkEconomy.cs:380` | scrap value basis = catalogue cost of all levels up to the current one; a scrap year is 365 park-clock days | capture of scrap value |
| ECON-026 | `Economy/ParkEconomy.cs:388` | park value = sum of scrap values | capture of the park value screen |
| ECON-027 | `Economy/ParkEconomy.cs:403` | the record sub-kinds 0–3 are rides, shops, sideshows and features, and every hired staff member counts | the record field at +0x4C behind sub-kind +0x7A8 and the staff byte +3 tested by FUN_100C4064 |
| ECON-028 | `Economy/ParkEconomy.cs:491` | purchases need a balance covering the cost | capture of building with too little money |
| ECON-030 | `Economy/ParkEconomy.cs:114` | the simulation stops once bankrupt | capture of the bankrupt state |
| ECON-031 | `Economy/ParkEconomyRuntime.cs:43` | a park started without an original save is opened when guests attach (an imported park follows its saved mParkClosed) | the open state of a new original park |
| ECON-034 | `Economy/ParkObjectives.cs:113` | challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient) | challenge captures per type |
| ECON-035 | `Economy/ParkObjectives.cs:153` | offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished | challenge flow captures |
| ECON-036 | `Economy/ParkObjectives.cs:154` | type 18 needs TargetVal items built since acceptance; the binary compares TargetVal with a per-ride value from 0x100C7264 that is not identified | the fifth output of 0x10041A34 |
| ECON-038 | `Economy/ParkObjectives.cs:280` | big park uses MinCellsOwned, cameras use MinCellsCovered | golden ticket award captures |
| ECON-039 | `Economy/ParkObjectives.cs:262` | the profit ticket compares the running yearly profit (mProfitThisYear) with ProfitYear directly; the original (0x10013FDC) scales the threshold by a per-objective factor not yet tied to that key | the caller of 0x10013FDC and its factor |
| ECON-040 | `Economy/ParkObjectives.cs:318` | players start with 1 golden key and keys are not consumed by entering themes | initial lobby and repeated theme-entry captures |
| ECON-041 | `Economy/EconomyObjectCatalog.cs:93` | features-directory objects with Research.Category != 3 are fixed (non-buyable) items | buy-menu capture |
| ECON-044 | `Economy/GuestEconomyBridge.cs:61` | guests never hold a balloon or wear a costume, so both percentages are 0; the binary gives them when a guest uses a balloon or costume shop (0x100EAAF8) | balloon lifetime (+0x214) and costume state rules |
| ECON-048 | `Economy/ParkEconomy.cs:719` | a negative path or queue cell cost (modified `Costs.*`) is booked as other income; balance and profit match the original's Spend of a negative amount | the ledger category of a negative Spend |
| ECON-049 | `World/ParkGate.cs:181` | the door's reopen guard (FUN_004df290) checks only VAR_BROKEN; the upgrade and condemned states and the back-of-queue path test (FUN_004de4a0) are not modelled | the ride states that block reopening and the queue back-link test |
| ECON-050 | `World/ParkGate.cs` | the gate census reads the reduced imported guest grid; unpreserved outside, approach and footprint cells can remain counted type 0 | original cell-type provenance in the guest-grid import |
| ECON-051 | `Economy/ParkEconomy.cs:362` | the bank's withdrawals flag (+276) has no counterpart: loan instalments are always deducted from the balance (the original, with the flag off, skips the deduction and keeps the profit bookkeeping) | the writer of the flag and when the original clears it |

## Open questions

- Original game speeds and runtime turn timing (the turn length, calendar start and civil months are traced).
- Whether loans can be retaken; whether building is allowed in the red.
- `BaseCostPerStaff`/`CostPerQualityLevel`, `Research.StartingWorkLoad`, researcher `WorkDuration`.
- Park value and the scrap basis; how golden keys are earned.
- The money block before the loan table (87,987 / 87,787 / −12,013), the extra words around the
  challenge table (`09 00 0b 00`, floats 100.0) and the last loan-record word.
