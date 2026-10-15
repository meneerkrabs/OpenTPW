# Park management simulation (economy slice)

Evidence date: October 9, 2026. Static reading of the original `.sam` settings, `.str` string
tables and the jungle `Easymode.TPWI` payload; no original executable was run, and a plain string search of `TP.ICD`
finds none of the setting names, so the formulas cannot be read from the binary. This is an OpenTPW simulation
**built on** original data, not a reproduction of the original rules: every place where the original
behaviour is unknown is marked *approximation* below and in the code documentation.

The Mac PowerPC build does contain the setting names and its code is readable. The ride wear, breakdown and
repair rules are traced there and implemented, see [reverse/RIDE-WEAR.md](reverse/RIDE-WEAR.md); only the
ride update's state gates and the reopening guard are executed in the oracle (ORACLE.md F27) and implemented; ECON-023 keeps the relief objects, the derived state word and a breakdown only the script reports; the life gauge starts at 100 (`0x100DAB1C`).
Not yet connected: an economy breakdown does not reach the ride script (OpenTPW does not write VAR_BREAKSTAT), so guests keep boarding a ride that broke down in the economy until
its script reports VAR_BROKEN; a bought upgrade closes the ride until it is installed (`0xdf928`, static).

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
| object `Upgrades[n].ScrapValueYear1–4` | 50/30/20/10, 60/40/30/15, 65/45/35/20 % | Scrap value (UITEXT 23) by age in years: the sale refund is `Upgrades[0].CostOfUpgrade` × the current level's row, 100 % for an unused object in its first 30 days (Mac `0xe25e8`, ORACLE F21) | data (percent); rule traced on the Mac |
| `UsageInfo.InitPricePerUse` | shops 30–75, sideshows 10–20 | Shop sale price / price of game (UITEXT 40, 51) | data |
| `UsageInfo.InitCostOfGoods` | shops 20–50, sideshows 25–50 | Shop cost of goods (UITEXT 33); for sideshows the cost of prize (UITEXT 50) — inferred from the UI labels and `RipOffOK` "above average win" | data |
| `UsageInfo.InitChanceOfLoosing` | 70–75 | Sideshow chance of losing (UITEXT 49 "Chance of winning") | data |
| `UsageInfo.InitPrizeValue` | 25 (one file) | Prize value | unused |
| `UsageInfo.RipOffOK` | 100 / 250 | "%premium peeps willing to pay above 'average win'" | unused (guests slice) |
| `UsageInfo.GoldenTicketCost` | 0–5 (10 objects) | Golden tickets needed to buy (UIHELPTEXT 152) | data; traced: the first copy costs tickets and no cash, later copies cost cash (0x100DA874) |
| `UsageInfo.ShopType`, `UsageInfo.SpecialIngredient` | 1–6; 0–4 | Shop kind (jungle: 1 gift, 2 burger/fries/ice cream, 3 restaurant, 4 drinks, 5 costume, 6 balloon); ingredient "0 none, 1 Fat, 2 Salt, 3 Ice, 4 Sugar" (`INGREDIENT.str`) | data (challenge sales) |
| `UsageInfo.LitterEffect` | 0, 10, 25, 50 (burger, fries, drinks 50; ice cream 25; restaurants 10; balloon, costume, gift 0) | "How much litter to add" per purchase: added to the buying guest's litter meter (0..100), dropped at 90 (ORACLE F18) | traced: every use adds it to the guest's litter meter (tp.exe `0x4fe1e0`); the guest bins or drops its litter in state 6 (docs/GUESTS.md) |
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
| `StaffPoolInfo.TimeBetweenStaffUpdates`, `MaxNumberOfStaffPerUpdate`, `StaffTimeoutTime` | 90, 10, 120 | Pool refresh/expiry | traced (tp.exe `0x5084f0`): the pool refills on the first turn past 4 × 90 turns since its last refill, and a candidate leaves on the first turn past 4 × its timeout, StaffTimeoutTime + R % (StaffTimeoutTime / 2) (`0x507600`), turns since it was drawn; expiry is checked at every pool update call, refills only on the interval |
| `StaffPoolInfo.BaseCostPerStaff`, `CostPerQualityLevel` | 2000, 100 | Never read in the Mac binary (balance +700 and +704: no instruction loads them); hiring has no debit (ORACLE.md F28) | unused — hiring is free; dismissing costs one monthly wage |
| `*ConstsPerGrade[g].PoundsPerTrainingPoint` | 5/8/12/15/0 (researcher 8/12/15/18/0) | "cost to raise the training level by 1%"; 0 = cannot improve grade 4 | data; 100 points per grade is read in tp.exe `0x505a10` (CStaff::Train) and the PPC `0x100F3588` (the online-file comment "1000 to get up to grade 1" does not match: 100 points at 5 pounds cost 500). Month-end training follows `0x100FC1C4` (executed, ORACLE.md F17): the role budget is divided by the number of **all** the role's staff, and each member below the top grade costs the whole share, gets min(share / price, 100) points, and carries the points above 99 into the next grade; the top grade's share is not spent. Example: a budget of 1,000 for 3 handymen (grades 0, 0, 4) at 5 pounds per point gives each trainee 333 → 66 points and spends 666 (OpenTPW trained both for 500 → 100 points and a grade, spending 1,000) |
| `MechanicConstsPerGrade[g].WorkDuration` | 80…20 | "Speed of fixing improves with training" | park turns of a repair at 0 % state of repair, scaled by the missing repair (executed, ORACLE F19) |
| `HandymanConstsPerGrade[g].WorkDuration`, `.DetectionRange` | 40…5; 2–5 | Cleaning speed; litter detection range | WorkDuration: park turns per sweep or toilet clean from the handyman's arrival (traced, tp.exe `0x4d76a0`); DetectionRange: the square his litter and toilet searches cover (tp.exe `0x4c8ed0`, `0x4d7880`; ORACLE.md X23) |
| `Entertainer/GuardConstsPerGrade` (`WorkDuration`, `HappinessEffectOnCell`, `ActivationDistance`) | | Entertaining/pursuit | unused (guests slice) |
| `ResearcherConstsPerGrade[g].WorkDuration` | 10–50 | "How long the researcher researches for" | unused |
| `PerGradeStaffConsts[g].RecuperationRate`, `.HappinessRecuperationRate` | 0.2/0.3/0.4/0.5/0.75; 1/2/2/3/3 | Energy and happiness a resting member regains per park turn | traced and executed (ORACLE.md F22): `StaffVitals.Rest` |
| `AllStaffConstants.RestLevel`, `.HappyHitCosNoRestArea` | 1; 2 | A member whose truncated energy byte is at most `RestLevel` seeks a rest area (a Staff Room); with none it loses `HappyHitCosNoRestArea` happiness on one turn in sixteen | traced and executed (F22): `StaffVitals.SeekRest` |
| `PerGradeStaffConsts[g].IdleDuration` | 40/30/20/10/5 | Idle turns before a member acts | handymen in a level: an idle one scans for litter every IdleDuration turns and searches again that long after a patrol (tp.exe `0x4d7060`, `0x4d73c0`); the other roles do not use it yet |
| `Upgrades[n].WearRate` | 0–5 "out of 10" (easy lower) | Ride wear | traced: scales the wear step every 64 turns (RIDE-WEAR.md) |
| `Upgrades[n].RedLineSpeed`, `Upgrades[n].RedLineCapacity`, `UsageInfo.MaxSpeed`, `UsageInfo.MaxCapacity` | Speed and capacity limits | Ride wear | traced: the speed and rider terms of the wear step |
| `Upgrades[n].DurationOfUpgrade` | 0/2/4 | "time taken for mechanic to carry out the upgrade — in conjunction with mechanic's WorkDuration" | multiplies WorkDuration for the upgrade to level n, no minimum (executed, ORACLE F19) |
| Advisor `StaffHireMechanics1.PoorerStateThan` | 25 | Worn-ride threshold | data (the headless mechanic dispatch below 25; in a level the mechanics' finder serves only broken and upgrading rides, tp.exe `0x4daa90`) |

Staff states use `STAFFSTATES.str` order (Idle, Patrolling, Working, Resting, On strike, Picked up). In a level a hire starts the Place tool and an employee is picked up and moved with the Carry tool, see [STAFF-PLACEMENT.md](STAFF-PLACEMENT.md).
Patrol areas are not simulated.

**Vitals and strikes (ECON-014, traced and executed in the oracle, ORACLE.md F22).** Every member has an energy and a happiness (singles, 0..100,
both starting at 100). Each park turn a walking member (Patrolling) loses `0.012 x (6 - grade)` energy and `0.005 x (6 - grade)` happiness, a
working one (a mechanic on a job) `0.025 x (6 - grade)` and `0.01 x (6 - grade)`, a resting one regains the grade's recuperation rates; idle and struck
members do not change. At the rest level a member goes to rest when a Staff Room is built (the one object with `UsageInfo.ChillsYouOut`; it is not dispatched
while resting, nor does a researcher research) and until its energy byte is back at 100; with no Staff Room it loses 2 happiness about once in 16 turns.
At each month end, when the park is open and no guest stands in it, each role with staff is considered: an active strike ends; otherwise, once the park
clock is 24 thirty-day months old (the original's stamp epoch is not traced), a role of more than three members whose truncated energies, or happinesses, average
below 15 has a grievance and its level climbs: 1 is a warning (`TAG_SYSTEM` 38-42), 2, 3 and 4 are strikes (48-52) that last until the next month end (free members go
on strike: no repairs, no cleaning, no research), and no grievance calls it off (53-57). `ParkEvent` kind `StaffStrikeMessage` carries role, kind and the
text index, and the HUD message area shows the `TAG_SYSTEM` text. A working mechanic finishes its job first; when it ends, the strike or rest decision runs before the next repair is dispatched, and dispatch skips members on strike, resting or tired with a rest area. Selling the last Staff Room ends a rest at once (what the original does with the vanished rest thing is not traced). The handymen's litter grievance counts the littered cells against the path and queue cells (`ParkLitter.Statistic`, ECON-020); the forced-strike
debug word is not modelled. In a level the handymen, entertainers, guards and researchers walk to the nearest Staff Room to rest (its
`VAR_STAFFIN` counts them in and out) and, while their role strikes and the gates are open, out past the ticket booth to the strike area
(`FixedItemInfo.StrikeArea*`) and back when the strike ends (WANDER-plan §4); the mechanics, and every role in a headless economy, rest
and strike where they stand.

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
| Advisor `GT*`, `GoldTicketNearTo*` (the near-ticket hints scale the threshold, e.g. profit 0.9 × `ProfitYear`), `Wealth*`, `InTheRed*`, `WagesHigh.*`, `Bankrupted.Score` | | Advisor message scores | unused (advisor slice) |

The bankruptcy rule is the bank's month routine (tp.exe `0x4d0370`), not a setting. A debit (`0x4d01f0`) that takes the
balance from 0 or more below 0 stamps the park turn (+0x120); every debit stores the new balance as the last balance (+0x11C),
and credits touch neither. At each month end, when the balance and the last balance are both below 0, the park time since the
stamp (turns × 15000 / 4 seconds) is divided into 30-day months; above 5 the bank posts message type 2. OpenTPW takes that as
the bankruptcy of TAG_SYSTEM 123–127 ("If you stay in the red for six months you will be made bankrupt", "the banks have closed
your park") and gives the warnings when the count reaches 3 and 5 (ECON-030). With the withdrawals flag (+0x114) clear, debits,
instalments and payoffs charge nothing; it is imported from original saves and set in all shipped ones (ECON-051). Ledger categories come from UITEXT 164–169 and 356–360.

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
| Turn | World `mGameTick` (755) | `Tick` = first tick of that turn; the ledger month moves with it (a save without a readable pool also moves the drawn pool's timers) | Traced (the turn the calendar converts); the save's calendar caches are checked against the resulting date (2 February 2000) |
| Bank | bank words | `Balance` (ledger opening balance), `EntranceFee`, `ProfitThisYear` | Traced field identities |
| Loans | bought loan offers | `Loans` (months repaid and instalment; outstanding = monthly × months left, as in the original) and taken offers | Field identities traced; outstanding balance SAVE-008 |
| Objects | RideSystem records (also those with an unverified footprint) + attraction things | registered uncharged; `Price`, `CostOfGoods`, `Level`, `Repair`, `LifeGauge` from the matching attraction (same Info.Id and cell) | Traced fields; state enum, chance of winning, totals, histories, names, operating, quality, queue and walk-away fields and mesh instance not imported; customer count and time stamp are the use count and build time of the scrap rule (SAVE-006) |
| Staff | models 4–8 | `Staff.Members`: type, grade, training percentage, happiness, energy, the display state of the saved state word (`0x5064f0`), hire tick (rounded down to its turn) | Grade/percentage/happiness/energy traced; state order, names, jobs, idle tick, role suffix SAVE-003 |
| Open state | World `mParkClosed` | the park opens when guests attach unless it is set | Traced |
| Challenges | challenge system (model 19, tp.exe `0x4d2320`) | won slots finished, declined counts, a running challenge (deadline from `mNextChallengeEventTime`, value as baseline), an unanswered offer, the next offer day; before the system is switched on the first offer is on park day `DaysUntilFirstChallenge` (Easymode: day 540, not counted from the import) | Traced; loss counts and the follow-up's carried object SAVE-005 |
| Research | research lab (model 14, `0x502dc0`) | projects whose points reach their cost researched, started projects' points, each category's effort and current project, the work load | Traced; the object table's researched flags SAVE-007 |
| Staff pool | staff hiring pool before the calendar (tp.exe `0x507850`) | `Staff.Candidates`: role, name index, grade, leaving on the first turn past 4 × its timeout from its stamp; the next refill past 4 × `TimeBetweenStaffUpdates` turns from the pool's stamp (`0x5084f0`) | Traced; sub-types and on-pointer flag SAVE-004 (the per-role counts and stop-producing flags are recomputed by the census `0x508000` before every read) |
| Staff HQ | staff HQ (model 9, `0x508bb0`) | training budgets (handyman, mechanic, entertainer, guard, researcher, `0x50c800`), strike levels, active flags and last-considered turns | Traced; picked-up id SAVE-009 (the force strike is never set in play, the trained byte never read) |

Not imported and reported in `Evidence`: `mRandomSeed` (SAVE-001), guests (SAVE-002), the hiring
pool's sub-types and on-pointer flags (SAVE-004), the challenge slots' loss counts and carried object (SAVE-005), the object table's researched
flags (SAVE-007), the batch balance and the loan offers' Available flags (SAVE-008; the withdrawals flag,
last balance and turn entered red are imported for the bankruptcy count), the staff HQ's picked-up id (SAVE-009; its force-strike word and trained byte are never set or read in play), the opaque blocks, world state, arrivals,
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
| Clock | Days, months, years, hours exist (challenge days, monthly wages/loans, yearly scrap, `Clock.RSE` HOUR) | Traced and executed in the PowerPC oracle (ORACLE.md F30): one park turn per 248 ms of the scheduler's millisecond clock at its default scale 1.0 (4.03 turns a second), date = 2000-01-01 + floor(turn × 15000 / 4) s through the civil calendar (a day is 23.04 turns, 5.7 s of real time at normal speed: January takes 177 s, the leap year 2000 34.9 minutes), a day, month and year event on the turn whose date has a different day, month or year than the last update's (the original's first update after construction also posts a day and a year event; OpenTPW starts synchronised); approximate: sampling turns from the 60 Hz clock, the Gregorian script system; speeds: the original has one clock scale (1.0, stepped by 1.25 within 0.25..2 by three key actions) that multiplies the whole scheduler clock, so guests, rides, scripts and the economy run at it together (ORACLE.md F32); OpenTPW's pause / ×1 / ×2 / ×4 buttons set that scale (×4 is beyond the original's maximum) |
| Ledger | Categories from UITEXT 164–169/356–360; integer dollars | Challenge prizes and scrap sales as "other income"; build, upgrade, goods, prizes, land and training as "other costs"; staff costs are the wages only, as the original's row (`0x505a10` books training to money out only) |
| Gate/shops/sideshows | Entrance fee, shop price and cost of goods, sideshow price/prize/chance of losing | Purchase requires money ≥ cost; a use fills the buyer's litter meter |
| Construction | Purchase, upgrade and cell costs; research gate; golden-ticket cost | Scrap value follows the Mac routine (base price × the current level's age percentage, 100 % for an unused object in its first 30 days, F21); the park value is the object part of the original's `0xc1d4c` (costs of the levels up to the current one times the state of repair / 100, condemned objects excluded); the map's cell points are not added (F29) |
| Loans | Offers, lenders, terms; instalment M = trunc(P x pow(1 + APR/100, N/24) / N) (F12); M paid flat for N months with no interest on a balance and no final remainder (F13); total payable M x N; early repayment M x months left, charged to the year profit as the whole-term share q x N (`0xccc78`, executed in the oracle); the year-profit word is 32-bit and wraps. **0 % APR (jungle Easy mode):** M = floor(P/N), so M x N < P and the unsigned share (M x N - P) / N is about 119 million a month; the original does this, and OpenTPW follows it on purpose (the year profit swings by that much and wraps) | The bank's withdrawals flag comes from the save; nothing in OpenTPW clears it (ECON-051) |
| Bankruptcy | More than 5 thirty-day months since the debit that went into the red, counted at month ends (`0x4d0370`) | Message type 2 is taken as bankruptcy and Advance stops; warnings at 3 and 5 (ECON-030) |
| Staff | Pool sizes, maxima, wages, training prices; traced and executed: energy and happiness per turn, rest at the Staff Room, the strike test and levels (F22); hiring is free and a dismissal costs one monthly wage (executed, F28) | Candidate grades, the handyman's job duration, the mechanic's walk to the ride; the staff state machine behind the vitals (ECON-014) |
| Maintenance | Wear rates, red lines, maxima, upgrade durations, worn threshold 25 | Traced: wear every 64 turns from the ride script's running state, riders and speed; breakdown check every 8 turns, only for rides that are open and not broken down (F27); a repair takes WorkDuration x (100 - state of repair) / 100 park turns, an upgrade WorkDuration x DurationOfUpgrade (executed, ORACLE F19); repair restores 100.0 (the float at TOC −11016 in the binary) but not the life gauge and opens the ride (F27); the life gauge starts at 100 (`0x100DAB1C`). Approximated: the ride-state gate (ECON-023); a condemned ride gets no mechanic (the arrival `0xd9c0c` releases one, F19) |
| Research | Items, categories, groups, costs, effort, ability, thresholds, work load; traced: points every 20 turns, cumulative group opener, first open item in table order; points from researcher staff only, in both modes (Mac) | Info-id table order, the excluded researcher states |
| Challenges | Definitions, level list, timings, prizes, follow-ups; a chain pays only the prize of its last part (a won part with a follow-up type pays nothing, tp.exe `0x4d2fb0`); a lost challenge stays open for another offer (`0x4d3140`) | Type semantics from comments, except the build types 28 and 30–33, traced to the handler table at `0x100CE690`, and the census percentages 9 and 10 (`0x4d1660`); explicit accept/decline; a follow-up of the same object is preferred (ECON-055); types 14, 22, 23, 26 unmeasured (23 and 26 never pass in the binary) |
| Golden tickets | All thresholds | Checked every 100 park turns (4.3 game days, 25 s at normal speed) in Full Simulation only, after the turn's calendar update (executed, ORACLE.md F31); the profit ticket is won when the year's running profit (`mProfitThisYear`, reset each calendar year) is signed greater than `ProfitYear` (every local and global ticket is a strict signed greater-than, oracle F23; no factor: the 0.9 `GoldTicketNearToXProfit.ProfitMultiplier` is the advisor's near-ticket hint, oracle F23); the first copy of a ticket object costs tickets, not cash (all traced) |
| Keys/progression | Keys per theme, theme order (THEMENAMES; ascending key cost); +1 per 3 earned golden tickets; spending tickets preserves keys (manual p. 28) | Start with 1 key; keys persist when entering themes |
| Park rating | — (traced and executed: Mac binary 0x100C7B24, F29) | Capped counts: guests in park × 20 / 1000 (max 20), rides × 1.5 (max 20), shops and sideshows × 2 (max 10 each), features (max 10), rides at upgrade level 2 (max 10), each staff type (max 4); objects count by `Info.WhichUIType` (the add-ons are rides); the original counts guests on cells of type 0, 1, 3, 9 and 10 and skips staff whose byte +3 is set (ECON-027) |

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
`restart.INTS` snapshot. OpenTPW starts a new park on every front-end entry (`[EXT:no-player-profiles]`).

Not implemented: player profiles and the per-player mode choice (`[EXT:no-player-profiles]`), theme keys and the
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
  `IParkGuestStatistics` (people in park, average happiness, happy-visitor count, the balloon and costume percentages).
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
  (`ClearCell` `0x85dcc`); `ScrapValue` is the sale refund `Upgrades[0].CostOfUpgrade` × `ScrapPercent` / 100 and
  `ScrapPercent` the whole of `0xe2424` (the current level's row by exact 365-day years, 100 for an object nobody
  used, `ParkObjectState.TimesUsed` = 0, in its first 30 days; ORACLE.md F21, the same in tp.exe `0x4e2400`, X13).
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
Litter is saved by cell (`Litter`: cell, type, the claiming handyman and the claim's turn). A save from before litter by cell carries
a park-wide `LitterScaled` count instead: it has no cells to land on, so it is accepted, logged and dropped (the park loads clean),
and it is written as 0.
Original TPWS/TPWI files are never written.

## Approximation register

Project rule: no value or rule may be invented except for upscaling, custom resolutions, online and
mods. Until proven, every economy approximation carries a `// [APPROX:ECON-NNN]` comment at its code
site, is listed in `Economy/EconomyApproximations.cs` and is logged once at startup as
`[APPROX:ECON-NNN]` warning when an original level loads. Values from original data are tagged
`// [DATA:<file>:<field>]`. Paths are relative to `source/OpenTPW/` (`Files/` = `source/OpenTPW.Files/`).

| Id | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| ECON-001 | `Economy/ParkCalendar.cs` | OpenTPW's fixed 60 Hz clock is sampled into 248 ms park turns (4.03 a second at normal speed) without the original's 31 ms substeps; under load the two differ: OpenTPW runs at most 16 ticks (266.7 ms) a frame for the whole simulation and drops the rest, where the original's world substeps catch up a backlog of up to 2000 ms but at most three park turns run a frame (further due turns in that frame are skipped, not deferred) | none for the rules (read: the clock route 0x5F5E80 / 0x5F5F10 and the frame loop 0x54E360 case 10); a timed run of the original only for the size of the drift under load |
| ECON-007 | `Economy/ParkEconomy.cs` | a repaid loan is offered again at once and every offer not taken is listed, without a credit test: the original clears the bought flag at the last instalment (0x4D0370) and lists an offer on the Available tab (0x49FA00) only when it is not bought and its stored test passed, amount ≤ net worth (0x4C7DB0, the park value with its cell points, ECON-026) − the remaining instalments of the bought loans (0x4D06B0), stored whenever the list is built (the dialog opens, a tab is selected); taking a loan needs only the bought flag clear (0x4D0750) | the net worth's cell points (ECON-026) |
| ECON-014 | `Economy/ParkEconomy.cs` | staff vitals and strikes follow the traced routines (tp.exe month gate 0x508E70, escalation 0x508F70, grievance 0x509360; the month message comes from the calendar update 0x4F8260 with no game-type test, and the age stamp 0x4F8800 counts floor(turn × 3750) seconds from world turn 0, both as here); without a staff world (a headless economy) they skip the state machine: walking members drain every turn, working means a job in progress, resting and striking need no walk, idle members are not told apart and the rest search runs every turn; the month gate skips a month while any guest is in the park by state, where the original counts the guests standing on a cell of type 0, 1, 3, 9 or 10 in a census reused for up to 23 turns (0x4C7FA0); the handymen's litter share counts the live map (ECON-020); the forced-strike word (zero at creation 0x5089FE, written only by the save 0x508BB0) is not imported (SAVE-009) | none for the routines (read); the census needs the original cell types (ECON-050) |
| ECON-017 | `Economy/ParkResearch.cs` | a new park's research table is in info-id order, where the original fills it in its object loader's order (0x5031A0 over 0x412E60 / 0x412E70, no sort on that path); the cursor steps with the arrows (0x4AA0C0 → 0x5034F0 / 0x5036A0) and moves on after a completed project (0x4AAEC0) to the next eligible project in table order with wrap, as the original's picker does, but the original appends an object's next level project to the end of the table only when the level below completes (0x503D40 → 0x504580 at 0x503D8C), where OpenTPW lists every level right after its base project, so after a level completes the two cursors can land on different projects; the picker's second test for an add-on whose target has a golden-ticket cost (+0xC4 > 0 → 0x4D4B70 over the set at 0xF7C58C) is not modelled; and the original marks an object researched at the start when its first research cost (+0x180) is 0, in any group, where OpenTPW does so for every group-0 item; the shipped Jungle lab lists its projects in another order (rides 1140, 1150, 1108, …; features 1414 before 1400), which an imported park follows for its current projects only | the enumeration order of the object loader 0x413930 / 0x413C10, and what the set at 0xF7C58C holds |
| ECON-020 | `World/ParkLitter.cs` | litter lies on map cells (one item per cell, dropped by guests' full litter meters, vomit and bins they cannot reach) with the original's sprite offset (0x004D93B0), but the drop's reseed of the original's world generator with the cell id (0x004D94E2) is not followed by OpenTPW's separate streams (DET-014), and the handymen's strike share (0x00509360) and the guests' thought pick (0x004FC8A0) count the live map, where the analyser recounts a 4 × 4 block's littered cells (0x004C8BA0) and path cells (0x004C8AE0) only when (turn >> 2) − (stamp >> 2) exceeds the reader's age (1 for the strike, 4 for the thought), so the counts can be 7 or 19 turns old | none (read): the reseed is DET-014's stream decision; a shared block cache would also need the original's order of the reads |
| ECON-022 | `World/Staff/StaffWorldSimulation.Handymen.cs` | all staff in a level walk and work as the original's state machines (WANDER-plan §4, StaffWorldSimulation), with the shared walks to a rest area and to the strike area, but they step without the steering the original's shared walk step applies to staff as to guests (0x004FA2A0 → 0x0050F3B0 over the global list 0x007CF078); a striker walks out along arrival lane A (OpenTPW's path grid ends at the park entrance) and is not sent after a prankster until he is back on the paths; a resting member stays on the map, where the original frees his model on arrival (0x005054D0 state 3 → 0x004D4170) and recreates it when the rest ends (0x00506D10); a sold rest area ends the walk to it or the rest in it, where the original (removal message 10 from 0x004DD0A0, handled by 0x00504C70) also sends a resting member to the nearest other rest area; a toilet claim ends when the walk to it fails, and a mechanic's claim when his walk fails or the ride no longer needs him, where the original's claim (0x004E0220) lapses only once 100 turns have passed and the claimant no longer targets the object; a toilet's, bin's or rest area's access cell is its join or entrance cell or first path neighbour; a mechanic's finder scans the objects from the first, where the original keeps one cursor on the mechanic HQ (game +0x1DA71A, 0x004DAA90) that starts each search one object after the previous start, so the search after one that started at the last object finds no ride; a repair does not wait for a still-broken ride's fix animation (0x004DA830, 0x004E0410); an entertainer's performance has no animation (the original draws one, 0x004D46D0) and the guests do not turn to watch; the guard list runs in hire order, where the original's (game +0x1DA744, next +0x210) is head-inserted at hire (0x004D5DE0 → 0x00519AA0), so the newest guard wins a tie; a prank reaches the guards at once; a picked-up entertainer's or guard's row stays on the cell he was lifted from; a guard carries a caught guest to the park entrance when the ticket booth is no path cell, where the original routes only to the booth gate cell, twice (0x004D6790 → 0x004D8610), and otherwise sets the guest down (0x004FEB50); a headless economy without a world keeps its own handyman dispatch: the first unclaimed littered cell in the finder's order, park-wide, else the dirtiest toilet, timed from the dispatch | the +0x36 access-cell writer, the bus delivery time of a prank (0x0050B550), the guests' reaction to a performance, the animation counts (0x00541FA0 and the fix animations) and the contents of the steering list (filled at run time) |
| ECON-023 | `Economy/ParkEconomy.cs` | the ride's state is derived from the economy (breakdown flag, pending upgrade) and the script's VAR_BROKEN instead of a state word: an economy breakdown (a life or repair byte of 0, relief objects exempt) marks the ride broken down at once, where the original only writes VAR_BREAKSTAT = 1 (0x004E0CA5 / 0x004E0CFA) and leaves it in state 0 until the script sets VAR_BROKEN (state 1, or 4 with a life byte of 0: 0x004E14E0); a breakdown that only the script reports gets no mechanic, where the original's state 1 gets one (0x004DAA90) and the repair waits for VAR_BROKEN to clear (0x004DA830); VAR_WORN is not written (the wear step 0x004DF670 sets it below a repair byte of 20, the update for relief objects below 25 at 0x004E0D16); only rides are updated, where the original updates every choosable object (state 0 at 0x004DB4FB): the shipped toilets are choosable with WearRate 0, so only their use (0x004E2440, modelled) wears them; state 2 comes only from the upgrade request 0x004E0050 (buy handler 0x004AF480) | the ride scripts' handling of VAR_BREAKSTAT and VAR_WORN (.rse data) |
| ECON-026 | `Economy/ParkEconomy.cs` | the map's cell points are not added: the original (0x004C7DB0) adds, for each cell without flag 0x40, 2 for type 0, 3 for type 1, 1 for types 2 and 10, 5 for type 3, 6 for type 9 and 50 when the cell's 40-byte record (for a record of type 12 or 17, the record its link names) has type 11, 13, 16, 18 or 25; at map start (0x00536490, 0x00450420) flag 0x40 is set on every cell but the InitialPath cells the terrain's ownership bits mark owned; OpenTPW's cell map has no such flag (PATH-007) and its types are incomplete (ECON-050), so the park value is the object part only, which matches, without the scrap values, as in the original | the terrain's ownership bit (Terrain base.md2 vertex flags +0x2C, bit 0, read by 0x00450420) and the writer that later clears 0x40 (Buy Land) |
| ECON-027 | `Economy/ParkEconomy.cs` | the visitor term counts the guests in the park by state, where the original counts the guests standing on a cell of type 0, 1, 3, 9 or 10 (0x004CCEB0 with the cell test 0x004FA990, recounted at every rating, capped at 1000); ParkGate.GuestCensus has that test, but the imported cell types are incomplete (ECON-050), so the rating keeps the state count; the staff terms match (the byte +3 is set only by the deleter at 0x0050B8E9, which then marks the thing deleted at 0x0050B906, and a dismissal removes the member at once here too) | the original cell types (ECON-050) |
| ECON-030 | `Economy/ParkEconomy.cs` | message type 2 of the bank's month (posted at each month end while more than 5 thirty-day months have passed since the turn-entered-red stamp, tp.exe 0x4D0370) is taken as bankruptcy: the park closes and the simulation stops, and the TAG_SYSTEM warnings come when the count reaches 3 and 5; the original's bank handler (0x4D02D0) acts on it once, unless the word 0x7CF4F4 is set (0x516C00), by setting the world state to 4 (0x5168F0): the per-turn thing loop stops (0x516380) and the park script's variable 0 becomes 2 (0x519EF0); the park-closed word is not written, and the turn counter, the calendar and the monthly bank go on; the bank routine compares the count only with 5, so it gives no warnings | the producer of the TAG_SYSTEM warnings and the meaning of the word 0x7CF4F4 |
| ECON-031 | `Economy/ParkEconomyRuntime.cs:49` | a park started without an original save is opened when guests attach (an imported park follows its saved mParkClosed) | the open state of a new original park |
| ECON-034 | `Economy/ParkObjectives.cs` | challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient); the check 0x4d1660 compares an accumulated value with TargetVal for types 1-8, 11-13, 16, 17, 20 and 22, and types 9 and 10 are census percentages of guests with a balloon or a costume (0x4c9530 / 0x4c95d0 over 0x4c9130); the events that feed each accumulated type are not traced | the writers of the challenge value (+0x3dc) per type |
| ECON-035 | `Economy/ParkObjectives.cs` | offers wait for accept/decline (the offer dialog 0x4d2c50 keeps +0x3da set until it is answered); a won challenge is marked finished, where 0x4d2fb0 marks the slot at the current index, which the follow-up offer has already moved on | challenge flow captures |
| ECON-036 | `Economy/ParkObjectives.cs:156` | type 18 needs TargetVal items built since acceptance; the binary compares TargetVal with a per-ride value from 0x100C7264 that is not identified | the fifth output of 0x10041A34 |
| ECON-038 | `Economy/ParkObjectives.cs:290` | big park uses MinCellsOwned, cameras use MinCellsCovered | golden ticket award captures |
| ECON-040 | `Economy/ParkObjectives.cs:331` | players start with 1 golden key and keys are not consumed by entering themes | initial lobby and repeated theme-entry captures |
| ECON-041 | `Economy/EconomyObjectCatalog.cs:118` | features-directory objects with Research.Category != 3 are fixed (non-buyable) items | buy-menu capture |
| ECON-044 | `World/Guests/GuestSimulation.cs` | the shop's quality of goods stays at the build's 50 (a balloon lives 127 needs phases), the balloon model's world reseed and the guest events 11 and 12 are not followed; a balloon's life counts down, and the challenge percentages count, while the guest is in the park and on the grid rather than on a park-type cell | none (read, tp.exe 0x004FE1E0, 0x00501650, 0x00501DB0) |
| ECON-048 | `Economy/ParkEconomy.cs:944` | a negative path or queue cell cost (modified `Costs.*`) is booked as other income; balance and profit match the original's Spend of a negative amount | the ledger category of a negative Spend |
| ECON-049 | `World/ParkGate.cs:218` | the back-of-queue link test (tp.exe 0x4DE4A0: connection bits beyond the link) is not modelled: OpenTPW takes the queue as connected; the maintenance flag (mRequestedService, +0x64) is the ride panel's call for a mechanic | the queue tool's connection writes |
| ECON-050 | `World/ParkGate.cs` | the gate census reads the reduced imported guest grid; unpreserved outside, approach and footprint cells can remain counted type 0 | original cell-type provenance in the guest-grid import |
| ECON-051 | `Economy/ParkEconomy.cs` | the bank's withdrawals flag is imported from an original save and kept (debits, instalments and payoffs honour it, tp.exe 0x4d01f0 / 0x4d0370 / 0x4d0850), and no OpenTPW action clears it | the writer that clears +0x114 (every shipped save has it set) |
| ECON-052 | `World/Staff/StaffPlacement.cs` | the original refuses a pick-up in the staff state 13 besides 4, 5, 7 and 18, and plays script 0x14 (slot 3, one frame) for state 6; OpenTPW's StaffJob has neither state, so the pick-up rule refuses only the jobs it has (strike walks, strike, in hand, escorting) and no clip plays state 6 | what the staff states 6 and 13 are (the state setter 0x5054D0 and the handlers 0x4D4810, 0x4D6410) |
| ECON-053 | `World/Staff/StaffPlacement.cs` | a member carried at save time is saved dropped on its old cell and the tool ends (the original saves it in state 7 and nothing re-creates the carry tool after a load, so it probably stays stuck) | a load of an original save made while carrying |
| ECON-054 | `Economy/IMechanicJobWorld.cs` | a mechanic put down off the path network (a drop on grass or a queue) is not dispatched: the original's walk from such a cell to the network is untraced, and a reserved ride would wait for an arrival that cannot happen | what the mechanic state machine (0x4DA490) does from a cell off the paths |
| ECON-055 | `Economy/ParkObjectives.cs` | a follow-up of the same object as the challenge it follows is preferred, else the first open one of the follow-up type (0x4d2b60); the original carries the satisfying object (+0x2e, set by types 14, 15 and 18) into the follow-up slot instead | how the follow-up types read the carried object |

## Open questions

- Which keys step the original's clock scale and whether a speed control is shown (the scale, its 1.25 steps and range and what it scales are traced and executed), and runtime turn timing under load (the turn length, the real-time rate at the default clock scale, the calendar start and civil months are traced and executed).
- Whether loans can be retaken; whether building is allowed in the red.
- `Research.StartingWorkLoad`, researcher `WorkDuration` (`BaseCostPerStaff` and `CostPerQualityLevel` are never read by the Mac binary, F28).
- The park value's cell points (ECON-026); how golden keys are earned.
- The money block before the loan table (87,987 / 87,787 / −12,013), the extra words around the
  challenge table (`09 00 0b 00`, floats 100.0) and the last loan-record word.

### Selected ride upgrade confirmation

The selected ordinary TPW ride detail offers **Buy upgrades** when its factory-owned catalogue defaults support the exact next SAM tier. The confirmation previews the existing economy catalogue's next-tier price; accepting calls the unchanged `TryBuyUpgrade` API for that live instance. Research, Instant Action, pending work, bankruptcy and available-money refusals remain model decisions. A successful purchase charges the existing Other Costs ledger and waits for a mechanic, including when none is hired. Cancel never buys, and there is no cancellation/refund API for already queued work.

Open and confirm recheck unique object/economy ownership, the current tier, factory parameter identity and current cost. Sparse/custom/stopped/faulted runtimes, coasters, prototypes, TPI, read-only visits and missing economies refuse before charging. Captured mouse gestures cannot open or confirm on a later held release. UI-935 qualifies this host confirmation and placement; native upgrade-window geometry is not established. Completion applies only the existing minimum catalogue-default contract: live CAP/DUR remain authored, with no native reset or full performance claim. Synthetic HUD, ledger, queued-no-mechanic and completion controls do not qualify original assets or rendering.

Independent synthetic actual-HUD controls verify the exact localized next-tier cost preview, refuse a cost or economy change during confirmation without charging, and require a fresh confirmation at the new cost. Authored CAP9/DUR77, an active WAIT deadline and occupied rider/head slots survive HUD purchase and mechanic completion; the other instance remains at its prior fallback capacity. These are host integration controls, not native upgrade performance or seat-count qualification.


### Mechanic arrival and own-save work phase

New TPW mechanic jobs in a live staff world reserve the existing dispatch-computed
`RepairTimer+1` amount and exact object/employee identity, then await a reachable
arrival at the existing job-access path cell. The observation update clears
`AwaitingJobArrival`; subsequent staff updates consume the unchanged timer and
raise the existing repair/upgrade event. Level still advances the economy before
staff movement. This dispatch-time amount, one observation update and cell-based
arrival are host policies, not native movement/timer-sampling qualification.

The optional `IMechanicJobWorld` binding applies only to new mechanic jobs;
headless dispatch keeps immediate work and handymen retain their existing policy.
The separate phase leaves staff display/vitals/strike/rest state rules intact.
Sale/removal/fire clear the matching phase; no new refund or cancellation API is
introduced. A pending job cannot start through a foreign same-ID actor/world or a
nonreciprocal object owner. Existing job priority and upgrade costs are unchanged.

Own saves omit the false phase. Missing phase therefore preserves old
Working/BusyTurns as already-started work; true preserves pending job, actor
position and travel fraction. Capture and load validate role, positive reserved
timer, reciprocal owner and eligible ride. Restored true without a world binding
stays frozen until the matching actor/path/object mapping is rebound. The provider
itself is neither serialized nor hashed; replay needs that same host binding.
The phase is gameplay state and joins canonical schema14; save-format versions
stay unchanged. Actual synthetic economy/path/clock/save controls cover travel,
disconnection/reconnect, exact completion, legacy/headless behavior and owner
isolation. Original assets, native positions/order and platform gameplay remain
unqualified.
