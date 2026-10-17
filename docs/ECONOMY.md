# Park management simulation (economy slice)

Evidence date: October 9, 2026. Static reading of the original `.sam` settings, `.str` string
tables and the jungle `Easymode.TPWI` payload; no original executable was run, and a plain string search of `TP.ICD`
finds none of the setting names, so the formulas cannot be read from the binary. This is an OpenTPW simulation
**built on** original data, not a reproduction of the original rules: every place where the original
behaviour is unknown is marked *approximation* below and in the code documentation.

The Mac PowerPC build does contain the setting names and its code is readable. The ride wear, breakdown and
repair rules are traced there and implemented, see [reverse/RIDE-WEAR.md](reverse/RIDE-WEAR.md); only the
ride update's state gates and the reopening guard are executed in the oracle (ORACLE.md F27) and implemented; the life gauge starts at 100 (`0x100DAB1C`).
Every choosable object is updated each turn (tp.exe `0x4e0b90`): a breakdown writes the script's VAR_BREAKSTAT = 1, the script reports VAR_BROKEN, and the
ride update's state 1 (`0x4e14e0`) breaks the object down and calls a mechanic, whose repair writes VAR_BREAKSTAT = 0 and waits for the script to clear VAR_BROKEN
(`0x4da830`); the wear step writes VAR_WORN below a state of repair of 20 and a relief object below 25, and a finished repair clears it (`0x4df8f0`). ECON-023 keeps
the split state word; a bought upgrade closes the ride until it is installed (`0xdf928`, static).

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
| `Costs.PathCell` / `QueueCell` / `KartTrackCell` / `WaterTrackCell` / `MapCell` | 20 / 75 / 400 (easy 200) / 500 (easy 250) / 100 (easy 10) | "How much incidental building costs" per cell; `MapCell` = land, charged per unowned cell by Buy Land (UITEXT 134, `ParkLand.Price`) | data |
| object `Upgrades[0].CostOfUpgrade` | 0–10,000 | "cash cost when buying this item" | data |
| object `Upgrades[1,2].CostOfUpgrade` | ride upgrades | Price of upgrade levels (UITEXT 29 "Upgrade to level") | data |
| object `Upgrades[n].ScrapValueYear1–4` | 50/30/20/10, 60/40/30/15, 65/45/35/20 % | Scrap value (UITEXT 23) by age in years: the sale refund is `Upgrades[0].CostOfUpgrade` × the current level's row, 100 % for an unused object in its first 30 days (Mac `0xe25e8`, ORACLE F21) | data (percent); rule traced on the Mac |
| `UsageInfo.InitPricePerUse` | shops 30–75, sideshows 10–20 | Shop sale price / price of game (UITEXT 40, 51) | data |
| `UsageInfo.InitCostOfGoods` | shops 20–50, sideshows 25–50 | Shop cost of goods (UITEXT 33); for sideshows the cost of prize (UITEXT 50) — inferred from the UI labels and `RipOffOK` "above average win" | data |
| `UsageInfo.InitChanceOfLoosing` | 70–75 | Sideshow chance of losing (UITEXT 49 "Chance of winning"); a play is won when the draw mod 100 is at most 100 − it (tp.exe `0x4e2670`), so the chance of winning is 101 − it % | data |
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
| object `Research.Group` | 0–4 | "0 = Available initially" | data; what starts researched is an object without research cost, whatever its group (tp.exe `0x5031a0`; Jungle's Wooden Log, group 0 at cost 300, is researched in the lab) |
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
invariant. tp.exe settles the rest: a new Full Simulation player gets one key when created (`0x4a6a50` →
`0x5afc30`; an Instant Action player none), and entering a theme only compares its cost with the keys and
subtracts nothing (`0x5e1cc0`). PDF identity: SHA-256
`c96eb25f3dc13f7f0824bbf03f9bbeb3bb94e9f4756d4d8dfa09ac71732b0668`.

| Key(s) | Values | Meaning | Use |
| --- | --- | --- | --- |
| `Challenges[n].Type/FollowupType/TargetTime/TargetVal/TargetObj/TargetObj2/TargetStaffType/Prize/CheckAtEndOnly/Independent` | 35 definitions | Each commented ("Sell 30 Drinks in 60 days" …) | data; type meanings from tp.exe's writers and checks (ECON-034) |
| `ChallengesInThisLevel[n].ChallengeType` | jungle 1 15 6 18 12 7 8 9 | Indices into `Challenges[]` | data — confirmed by the save |
| `Challenges.DaysUntilFirstChallenge/DaysAfterCompletedChallenge/DaysAfterDeclinedChallenge/DeclinesToForfeit/ShortTimeLeftWarningAt` | 540/270/270/2/20 | Offer timing | data (warning not raised) |
| `GoldenTicketLocal.Visitors/PeopleInPark/Happiness/AtLeastThisManyHappyPeople/ProfitYear/RecentVisitors/RecentVisitorMonths` | jungle 100/200/75/150/15,000/350/6; space 3000/350/85/150/30,000/500/6 | Per-theme ticket thresholds; award texts TAG_SYSTEM 180–185 | data |
| `GoldenTicketGlobal.CoasterHeight/GokartExcitement/WaterLength/MinCellsOwned/MinCellsCovered` | 105/90/50/3000/2000 | TAG_SYSTEM 186–191 (coaster, go-kart, water ride, big park, cameras, all land) | data; big park ↔ MinCellsOwned and cameras ↔ MinCellsCovered inferred |
| `Tickets.CanEarnTicketsInTheme`, `Tickets.CanSpendTicketsInTheme`, `Keys.CostToEnter` | 1, 1, jungle 1 / hallow 1 / fantasy 3 / space 5 | Lobby: keys needed to enter (UIHELPTEXT 338) | data; key earning manual-confirmed, initial key/entry persistence approx |
| Advisor `GT*`, `GoldTicketNearTo*` (the near-ticket hints scale the threshold, e.g. profit 0.9 × `ProfitYear`), `Wealth*`, `InTheRed*`, `WagesHigh.*`, `Bankrupted.Score` | | Advisor message scores | unused (advisor slice) |

The bankruptcy rule is the bank's month routine (tp.exe `0x4d0370`), not a setting. A debit (`0x4d01f0`) that takes the
balance from 0 or more below 0 stamps the park turn (+0x120); every debit stores the new balance as the last balance (+0x11C),
and credits touch neither. At each month end, when the balance and the last balance are both below 0, the park time since the
stamp (turns × 15000 / 4 seconds) is divided into 30-day months; above 5 the bank posts message type 2 at every such month end (no warnings come
before it; `ParkEventKind.BankruptcyMessage`), and the advisor answers each with its advice 106 (game event 2). The bank's own handler (`0x4d02d0`) acts on it once, unless a winning sequence shows (`0x516c00`): the park is lost
(`0x5168f0`, world state 4). Every attraction is asked to break down (`VAR_BREAKSTAT` = 1) and the gate gets its end-of-park
command 2; the park-closed word keeps its value. From then on the per-turn thing updates stop (staff, rides, research, the
challenge system, the guests and staff walks), while the turn counter, the calendar, the bank's month, the staff pool and the
golden-ticket checks go on. TAG_SYSTEM entries 123–127 ("If you stay in the red for six months you will be made bankrupt",
..., "the banks have closed your park") are the captions of the advisor's money messages 102 to 106: the advice 106 of this message shows entry 127 in the
message tube (`0x59b620`, docs/LIPS.md "Captions"), the warnings 102 to 105 have no producer yet (ADVISOR-009), and the main loop silences the music and the crowd in state 4
(`0x54f86d`); the panel with the tick button (`0x48ac40`) is open (ECON-030). With the withdrawals flag (+0x114) clear, debits,
instalments and payoffs charge nothing; it is imported from original saves and set in all shipped ones, and only the online layout replay clears it (`0x404140`, set again at its end), so a single-player park keeps it. Ledger categories come from UITEXT 164–169 and 356–360.

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
| Loans | bought loan offers, each offer's stored credit test | `Loans` (months repaid and instalment; outstanding = monthly × months left, as in the original), taken offers and the offers whose stored test passed | Field identities traced |
| Objects | RideSystem records (also those with an unverified footprint) + attraction things | registered uncharged; `Price`, `CostOfGoods`, `Level`, `Repair`, `LifeGauge`, `IngredientAmount`, `QualityOfGoods` (its byte) from the matching attraction (same Info.Id and cell); the state word (1 and 4 broken down, 2 the next level waiting for a mechanic and its cost, `0x4e0e60`), `ChanceOfLosingPercent` (100 − the win byte), the total profit (takings − costs, `0x4e1c60`), the maintenance call; the operating speed, capacity and duration become the ride runtime's words when they differ from its tier's (`Level.ImportOriginalRideSettings`; the loader stores them raw, `0x4dcc6c`..`0x4dccb8`); the customer count and time stamp are the use count and build time of the scrap rule (`0x4e2290`, thing +0x1a0 and +0x18) | Traced; `mNumWalkAways` is imported (the loader reads it back, `0x4dc12b`; `0x4e1670` raises it and nothing else reads the field); a closed open word of a working object, names and lapsing claims SAVE-006 (the third gauge, `+0x4c`, is only written and saved: nothing in tp.exe reads it) |
| Staff | models 4–8 | `Staff.Members`: type, grade, training percentage, happiness, energy, inline name, hire tick (the clock stamps whole turns, `0x4f8690`), idle stamp (`+0x200`), patrol rectangle (corner cell ids y × 128 + x + 1, `0x506ed0`), the state word `+0x19c` as the member's job with its target (a mechanic's ride and repair timer, a handyman's toilet and cleaning start, an entertainer's or researcher's work start, the rest area while walking to or resting in it; thing ids name the reloaded attractions) and display state (`0x5064f0`), and the walk on to `mAccurateDestX/Y`, which the person loader routes to again (`0x4f9407`); a world takes back the ride or toilet claim when it places the member | Traced (`0x504de0` and the role serializers); `mJobsDone` needs no import (only the constructor and the serializer touch it); a litter job (SAVE-010) and a guard's guest (SAVE-002) are dropped |
| Open state | World `mParkClosed` | an imported park opens when guests attach unless it is set; a park started without a save stays closed until the player opens it (`0x51562d`) | Traced |
| Challenges | challenge system (model 19, tp.exe `0x4d2320`) | won slots finished, declined counts, a running challenge (deadline from `mNextChallengeEventTime`, value as baseline), an unanswered offer, the next offer day; before the system is switched on the first offer is on park day `DaysUntilFirstChallenge` or the day the park is first open after it (Easymode: day 540, not counted from the import) | Traced; the loss counts and the offered words are read and need no import (nothing reads them, `0x4d1ae0`); a save whose slots do not match the level's challenges keeps the new park's state (SAVE-005) |
| Research | research lab (model 14, `0x502dc0`) | projects whose points reach their cost researched, started projects' points, each category's effort and current project, the work load | Traced; the object controls' researched bytes (`0x4d3aa0`) are read and agree with the lab's research on the shipped save; the table order and highest open group SAVE-007 |
| Staff pool | staff hiring pool before the calendar (tp.exe `0x507850`) | `Staff.Candidates`: role, name index, grade, leaving on the first turn past 4 × its timeout from its stamp; the next refill past 4 × `TimeBetweenStaffUpdates` turns from the pool's stamp (`0x5084f0`) | Traced; sub-types (the staff variant, GUEST-014) and on-pointer flag SAVE-004 (the per-role counts and stop-producing flags are recomputed by the census `0x508000` before every read) |
| Staff HQ | staff HQ (model 9, `0x508bb0`) | training budgets (handyman, mechanic, entertainer, guard, researcher, `0x50c800`), strike levels, active flags and last-considered turns | Traced; picked-up id SAVE-009 (the force strike is never set in play, the trained byte never read) |

The saved guests are restored by the level's guest simulation (`GuestSimulation.ImportOriginal`, docs/TPWS-PAYLOAD.md): those on their way in or out and those walking, deciding, going to a bin or a ride or leaving in the park keep their thing id, type, cash, meters, exit counter, prankery flag, balloon, costume, histories and walk speed and walk on to their saved destination; queued, boarding, riding and held guests are not imported (SAVE-002).

Imported: `mRandomSeed`, the state of the one world generator (`0x516330`; SAVE-001 retired), so the park continues the saved stream. Not imported and reported in `Evidence`: the guests the guest simulation cannot restore (SAVE-002), the hiring
pool's sub-types and on-pointer flags (SAVE-004), a challenge system whose slots do not match the level's (SAVE-005), the research table's order and the dropped projects
(SAVE-007), the staff HQ's picked-up id (SAVE-009; its force-strike word and trained byte are never set or read in play), the advisor presentation block, world state, arrivals,
calendar session start, the advisor controller's presentation words and current record, its pending
advice and history when no automatic advisor is attached, and the subsystem things without an
OpenTPW counterpart (SAVE-010). The raw dumps of the original's engine structures (scripts, RSSE, particles, sound mixer, message-centre lists, track and flying ride chunks) are not restored (`[EXT:original-engine-state-dumps]`). Staff values
outside OpenTPW's ranges (and states or targets a save of the original cannot hold) are adjusted and each adjustment is reported; an attraction
with out-of-range values keeps the defaults and is listed. The ledger's category history starts
empty: the save has none, and the month opens at the current balance: `mLastBalance` (87,787 in
Easymode against 87,987) is the balance after the last debit, not a month opening. The bank's batch word (a pending credit the month message pays
in, 0x4D0370; 0 in every shipped save), the loan offers' stored credit tests, the withdrawals flag, last balance and turn entered red are imported.

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
| Construction | Purchase, upgrade and cell costs; research gate; golden-ticket cost | Scrap value follows the Mac routine (base price × the current level's age percentage, 100 % for an unused object in its first 30 days, F21); the park value is the original's net worth (`0xc1d4c`, tp.exe `0x4c7db0`): 2 points per owned empty cell (the park's land, PATH-007), 3 per path, 5 per queue, 6 per entrance, 1 per exit or water cell, 50 per track tile (F29), plus per object the costs of the levels up to the current one times the state of repair / 100, condemned objects excluded; the month sample and the loans' credit test read it through the analyser's cache (ages 0 and 20) |
| Loans | Offers, lenders, terms; instalment M = trunc(P x pow(1 + APR/100, N/24) / N) (F12); M paid flat for N months with no interest on a balance and no final remainder (F13); total payable M x N; early repayment M x months left, charged to the year profit as the whole-term share q x N (`0xccc78`, executed in the oracle); the Available tab lists an offer that is not bought and passed the credit test of the last list build (tp.exe `0x49fa00`, run when the dialog opens or a tab is selected): amount ≤ net worth − the bought loans' instalments still owed (`0x4d06b0`; the net worth counts only the park's land, Buy Land's `ParkLand`, as the original does; a map without the level's land counts every cell over the terrain, ECON-026); taking a loan needs only the offer not bought (`0x4d0750`); the year-profit word is 32-bit and wraps. **0 % APR (jungle Easy mode):** M = floor(P/N), so M x N < P and the unsigned share (M x N - P) / N is about 119 million a month; the original does this, and OpenTPW follows it on purpose (the year profit swings by that much and wraps) | The bank's withdrawals flag comes from the save; only the original's online layout replay clears it (`0x404140`), which a single-player park never runs |
| Bankruptcy | More than 5 thirty-day months since the debit that went into the red, counted at month ends (`0x4d0370`) | Message type 2 loses the park (world state 4): thing updates stop, calendar and bank go on; music and crowd go silent; the advice 106 shows its caption (the panel with the tick button and the money warnings 102–105 are open, ECON-030) |
| Staff | Pool sizes, maxima, wages, training prices; traced and executed: energy and happiness per turn, rest at the Staff Room, the strike test and levels (F22); hiring is free and a dismissal costs one monthly wage (executed, F28) | Candidate grades, the handyman's job duration, the mechanic's walk to the ride; the staff state machine behind the vitals (ECON-014) |
| Maintenance | Wear rates, red lines, maxima, upgrade durations, worn threshold 25 | Traced: wear every 64 turns from the ride script's running state, riders and speed; breakdown check every 8 turns, only for rides that are open and not broken down (F27); a repair takes WorkDuration x (100 - state of repair) / 100 park turns, an upgrade WorkDuration x DurationOfUpgrade (executed, ORACLE F19); repair restores 100.0 (the float at TOC −11016 in the binary) but not the life gauge and opens the ride (F27); the life gauge starts at 100 (`0x100DAB1C`). Approximated: the ride-state gate (ECON-023); a condemned ride gets no mechanic (the arrival `0xd9c0c` releases one, F19) |
| Research | Items, categories, groups, costs, effort, ability, thresholds, work load; traced: points every 20 turns, cumulative group opener, first open item in table order; points from researcher staff only, in both modes (Mac) | Info-id table order, the excluded researcher states |
| Challenges | Definitions, level list, timings, prizes, follow-ups; a chain pays only the prize of its last part (a won part with a follow-up type pays nothing, tp.exe `0x4d2fb0`); a lost challenge stays open for another offer (`0x4d3140`), and so does a declined one: the decline never raises the slot's declined count, so `Challenges.DeclinesToForfeit` only skips a slot whose saved count is already at it (`0x4d2ec0`, `0x4d2a70`); a won part with a follow-up marks the follow-up won instead of itself, so it can be offered again (`0x4d2fb0`); declining a follow-up (not `Independent`) pays half its prize (`0x4d2ec0`) | Type semantics follow tp.exe's accumulator writers (`0x4d27a0`; sales, profits, admissions, research and ride uses), the coaster types 14, 18 and 19 (`0x4cc810`), the build types 28 and 30–33 (`0x100CE690`) and the census percentages 9 and 10 (`0x4d1660`); types 24, 25 and 27 compare the current value where tp.exe averages over the challenge, and a few shop-sale details differ (ECON-034); a declined challenge counts towards a forfeit (ECON-035); the object a won part carries into its follow-up filters the accumulator (the kart type counts the live karts); types 23 and 26 never pass (no case in `0x4d1660`) |
| Golden tickets | All thresholds | Checked every 100 park turns (4.3 game days, 25 s at normal speed) in Full Simulation only, after the turn's calendar update (executed, ORACLE.md F31); the profit ticket is won when the year's running profit (`mProfitThisYear`, reset each calendar year) is signed greater than `ProfitYear` (every local and global ticket is a strict signed greater-than, oracle F23; no factor: the 0.9 `GoldTicketNearToXProfit.ProfitMultiplier` is the advisor's near-ticket hint, oracle F23); the first copy of a ticket object costs tickets, not cash (all traced) |
| Keys/progression | Keys per theme, theme order (THEMENAMES; ascending key cost); +1 per 3 earned golden tickets; spending tickets preserves keys (manual p. 28); a new player starts with 1 key (tp.exe `0x4a6a50` → `0x5afc30`, skipped for GameType 2) and entering a theme only compares its cost with the keys (`0x5e1cc0`, nothing subtracted) | — |
| Park rating | — (traced and executed: Mac binary 0x100C7B24, F29) | Capped counts: guests on a cell of type 0, 1, 3, 9 or 10 (tp.exe `0x4cceb0`, at most 1000) × 20 / 1000 (max 20), rides × 1.5 (max 20), shops and sideshows × 2 (max 10 each), features (max 10), rides at upgrade level 2 (max 10), each staff type (max 4); objects count by `Info.WhichUIType` (the add-ons are rides); staff whose byte +3 is set (only while being deleted) are skipped, as a dismissal removes them here |

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
`restart.INTS` snapshot; OpenTPW writes its own restart snapshot there too (`SAVE-012`). OpenTPW starts a new park on every front-end entry (`[EXT:no-player-profiles]`).

Not implemented: player profiles and the per-player mode choice (`[EXT:no-player-profiles]`), theme keys and the
Instant Action "no key check" rule (the front end has no key gate), the research and finance
panels themselves (the HUD buttons have no panel, so the UITEXT 467 refusal is not shown), the
Instant Action completion message (UITEXT 470), and the advisor rule filter by mode.

Determinism: the park's one world generator (`ParkEconomy.World`, docs/DETERMINISM.md) drives candidates and sideshow draws; time advances in whole ticks;
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
- `EconomyRules` (`Economy/EconomyRules.cs`) say which systems a `ParkEconomy` runs and whose money rules apply: `Full` is Theme Park World, `ThemeParkIncMoney` the
  Theme Park Inc slice (no staff pool, wages, mechanics, ride wear, research gate, challenges or golden tickets; monthly running costs, the condemned-object scrap branch,
  no Buy Land, no upgrade purchase), over `BalanceSettings.ForThemeParkInc` (docs/THEME-PARK-INC.md, "Theme Park Inc economy"). Every Theme Park World park runs `Full`, unchanged.
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
| ECON-017 | `Economy/ParkResearch.cs` | a new park's research table follows the original's object scan (Rides, Features, Upgrades, Shops, Sideshow, each directory in archive-name order: 0x413140, then the fill 0x5031A0 over 0x412E60 / 0x412E70 with no sort; the shipped Jungle Easymode lab's order matches), a level project joins the category-4 table at its end when the level below completes (0x503D40 → 0x504580 at 0x503D8C, or at the base's turn in the fill when the base is free), the cursor steps and moves on as the original's picker does (0x4AA0C0 → 0x5034F0 / 0x5036A0, 0x4AAEC0) and its add-on test holds a target with a golden-ticket cost to the ticket set (0x4D4B70 over 0x5AF780), but OpenTPW lists a project for every buyable object with research levels, where the fill skips a type whose word +0x508 is set (1 when the type's model load 0x4629D0 gave 0, 0x414305): the shipped Easymode lab holds 24 projects and none for the coasters 1185 and 1190, Sun God 1106 and seven of the features that OpenTPW lists, though the archives of all ten hold their main model (OpenTPW's catalogue needs it), so the loader's failure (0x004629D0 → the archive lookup 0x00461A60 and the parse 0x00461F10, stored at +0x490, then +0x508 = 1 at 0x00414305) is not a missing file | why those types have no model result in that game (the file names the object loader 0x00413C10 builds from the settings stack), and so which projects a normal game's lab holds |
| ECON-022 | `World/Staff/StaffWorldSimulation.Handymen.cs` | all staff in a level walk and work as the original's state machines (WANDER-plan §4, StaffWorldSimulation) on the guests' walk (the route search, the steering step 0x004FA2A0 → 0x0050F3B0 with avoid_walls, follow_path and separation, the walk mask +0x188), with the shared walks to a rest area and to the strike area; a striker walks out along arrival lane A (OpenTPW's path grid ends at the park entrance) and is not sent after a prankster until he is back on the paths; separation meets a cell's guests in OpenTPW's cell lists and then the staff on it in member order, where the original walks the cell's head-inserted thing list (0x0050B6A0); the stranded stamp the wander sets (+0x198, 0x004FA670) and SetDest's quick failure while the cells around stay unchanged (0x004FA770) are not kept, as for the guests; the toilet, rest-area and ride searches take the objects newest first by OpenTPW's object id, which is the original's head-inserted list (game +0x1DA746, 0x00519D80) for objects placed in the park, where a park loaded from an original save has the chain the save keeps (each attraction's mNext +0xC and the head word), which the import does not follow; a toilet's, bin's or rest area's access cell is its join or entrance cell or first path neighbour; an entertainer's performance has no animation (the original draws one, 0x004D46D0) and the guests do not turn to watch; a picked-up entertainer's or guard's row stays on the cell he was lifted from; a guard carries a caught guest to the park entrance when the ticket booth is no path cell, where the original routes only to the booth gate cell, twice (0x004D6790 → 0x004D8610), and otherwise sets the guest down (0x004FEB50) | the +0x36 access-cell writer, the guests' reaction to a performance and the animation counts (0x00541FA0 and the fix animations) |
| ECON-023 | `Economy/ParkEconomy.cs` | an object a script runs breaks down as tp.exe's does (0x004E0B90 writes VAR_BREAKSTAT = 1, the script reports VAR_BROKEN, the ride update's state 1 from 0x004E14E0 calls a mechanic, whose repair writes VAR_BREAKSTAT = 0 and waits for VAR_BROKEN to clear, 0x004DA830, and 0x004DF8F0 clears VAR_WORN), but the one state word is split between the visitor bridge (states 1 and 4, told apart by the life gauge the level mirrors into it, QUEUE-019 retired) and the economy's broken flag, which the level joins on the rising edge of the bridge's state (the headless economy without ride scripts breaks an object down at once and repairs it without the wait, [EXT:headless-economy-breakdown]) | none for the routines (read); the one state word needs the bridge's and the economy's records merged |
| ECON-026 | `Economy/ParkEconomy.cs` | the cell points follow tp.exe 0x004C7DB0 on OpenTPW's cell map and its land (the cells without the unowned flag 0x40: the level fence's enclosure, the strip at the gate and Buy Land's purchases, or an imported save's saved flags, PATH-007), but a track tile's map type is taken as 0 (OpenTPW keeps the track's type in the map's type byte as well as in the track layer, where the original's cell keeps its own type under the structure record, which the track tool is not found to write) and the fence's hoardings (structure type 25) count 50 each on the owned post cells of the land's ring, one cell per post, where the original lays them with the track tool (0x005323F0 enters mode 0x19 and clicks the positions of the level's Hoardings.sam through 0x00524960: its mode 0x19 branch takes the first click as the line start and lays each later click as a segment of the line walker 0x0053AAC0, two cells a step, 0x005370E0 per step, with the passes 0x80, 0x82, 0x85, 0x86 and 0x81, and 0x005323F0 then runs 0x00531040, 0x005311C0 and 0x0052BA50) and the piece's footprint is read only from the counting tests; a map without the level's land (a level without a fence setup, a Theme Park Inc map, a read-only visit) counts every cell over the terrain as owned | what the step 0x005370E0 writes for the structure type 25 (a cell's type and the footprint of a hoarding piece, the cells that carry its structure record) and what 0x00531040, 0x005311C0 and 0x0052BA50 add after the clicks, or a net worth cached in an original save (the analyser's +0x1F0EC) to compare with |
| ECON-030 | `Economy/ParkEconomy.cs` | the lost park follows the bank's handler and the world update (the thing loop stops, the arrival target 0x004CF5B0 is 0 and the music and crowd are silent in state 4), and the advisor plays its advice 106 for every bank message (game event 2, AutomaticAdvisor) with its caption, but OpenTPW has not the panel tp.exe builds in state 4 (0x005168F0 → 0x00481B50 → 0x0048AC40 closes the current tool, hides the HUD windows and builds the panel window 0x7472, with 0x004815E0 → 0x0048ADB0 and the state-4 branch of 0x0048C150 with UITEXT group 3, id 0x16), the money warnings (advice messages 102 to 105, whose scores compute the months in the red) have no producer (ADVISOR-009), and tp.exe's interface tests world state 4 only at 0x004815F0, 0x0048C27A, 0x004A2925 (a function not read) and 0x004A2AC9 (the pause and play buttons are left alone), none of them a purchase, hiring or opening test found, so OpenTPW's refusal of those in state 4 is not shown to be the original's | the panel window 0x7472 (its layout and the handler 0x0048A740 of its tick button) and the function around 0x004A2925 |
| ECON-049 | `World/ParkGate.cs` | the back-of-queue link test (tp.exe 0x004DE4A0) runs for rides with real queue cells (the back cell's link byte must differ from its queue link); a ride on OpenTPW's one-cell queue and an object without Info.HasQueue are taken as connected, because OpenTPW's ride entrance cells keep no link byte (tp.exe reads the queue's front from it, 0x004DE040, and tests a shop's entrance against the path in front) | the entrance cells' types and link bytes in OpenTPW's cell map |
| ECON-050 | `World/ParkCellMap.cs` | the guest grid takes the world init's types and flags (tp.exe 0x00536490 with the terrain test 0x00450420: 7, 2 or 30 where no terrain lies, 1 with flag 0x20 for an InitialPath cell and 0 inside, flag 0x40 on every cell without 0x20, which the level's land then clears inside the fence) and the footprint types a placement writes (0x00528A70: 4, 9 and 10), but a removed object empties the cells it typed, where the original's clear of them is not found (its destructor writes no cell word), the placement's other cell writes (the clear before the write beyond a path cell under the footprint, which ParkObjects clears, the link to the path in front of an entrance, the '@' path links) are not modelled, and a read-only visit or a Theme Park Inc layout keeps the cells it was given | the sale path's clear of the footprint cells: tp.exe's destructor 0x004DD0A0 writes no cell word (it credits the scrap value, takes the RegionFX row off, kills the mesh 0x00559060 and posts message type 10, read only by the staff and guest handlers 0x004DA0CC, 0x004D6CFC and 0x004FB36D), so the clear is in the code that marks the object for it, whose caller of 0x0050B780 is not identified |
| ECON-057 | `Economy/ParkEconomy.cs` | an attraction's six daily 30-day rings follow tp.exe (costs +0xF8, takings +0x70, customers +0x1A8, walk-aways +0x230, winners +0x2B8 and the visits' averaged happiness change +0x340: the constructor 0x004DB090 starts each at head -1, size 30; the day message 0x004DD320 rolls them; the windows and lists sum them over their counts, 0x004E1C70, 0x00495C20, 0x004B01D0, 0x004B16C0, 0x004955E0, 0x00493CD0), the happiness-change ring is written as the use routine 0x004FD970 does (3 x the guest's happiness byte change since joining the queue, averaged in by 0x004E1E00; a won sideshow's tail is GUEST-005), the walk-aways stay a list beside the ring type and the customers row's second number (the text variables 0x19 and 0x1A of its UITEXT) is not formatted | the UITEXT 32 and 41 templates |
| ECON-058 | `Economy/ParkObjectives.cs` | the challenge system runs tp.exe's update on every park turn (an idle system offers on the first turn at or after its next event time, a running challenge is checked on the turns whose low two bits are not 0 and ends at its exact second, 0x4d1e90, 0x4d1f99), but the progress message that the other turns post (tag 0x4d3390 by type, advice 0x160, which the advisor skips) has no OpenTPW message: the tag system shows the tag's TAG_SYSTEM text with the slot's numbers in the message tube, replacing the offer's message in place, while OpenTPW's tube shows its own description of the offer (Describe) | the placeholder grammar of the TAG_SYSTEM entries (the substitution 0x509e10 from the message's fields) and the tube's in-place update (0x48eb20) |
| ECON-059 | `Economy/ShopGoodsCost.cs` | the sum, the factor and the product are rounded to a single after each step, as the x87 does at the single precision DDSCL_FPUSETUP gives (the flags 0x851 and 0x808 of the SetCooperativeLevel calls 0x563914 and 0x563B7A), but the precision the simulation thread runs at is not captured: tp.exe's only writes of the precision field are the renderer's (0x576EC0, called from the main loop on the render thread: 0x591220 clears the field at entry and returns the old word; the normal exit puts the old word back, 0x591240, or the old word with extended precision, 0x591250, when bit 1 of the render-state word 0x8BD508 is set, and one failure exit at 0x577433 leaves it cleared), __ftol (0x67A830) and the inline conversions only set the rounding bits, so the simulation runs at the precision the thread started with, which the C runtime and DirectDraw set (the system's), unless that bit ends a render or the failure exit leaves single precision | the control word of the original's simulation thread, or a captured cost of goods with a quality or ingredient byte other than 50; what sets bit 1 of 0x8BD508 |
| ECON-060 | `Economy/ParkEconomy.cs` | Theme Park Inc's monthly running costs charge only each object's current level's `Upgrades[n].RunningCost` (0 in ride state 1 or 4), as TPI Game.exe's month message 0x5DEFFF does, but the message also adds an amount its statistics block accumulated (+0x938) that is not traced, and its statistics are monthly rings (costs +0xB88, profit +0x1028, rolled by the same message) where OpenTPW's are the daily 30-day rings of Theme Park World, so the cost enters today's slot of the daily ring, and the debit's order against the bank's month handler is not read, so OpenTPW debits the running costs after the wages' place and before the loan instalments, and keeps booking the cost of goods and prizes at once as Theme Park World does | the writers of the statistics word +0x938 (whether the cost of goods and prizes are debited there at the month's end) and the listener order of the month message |
| ECON-061 | `Economy/ParkEconomy.cs` | Theme Park Inc's scrap value follows tp.exe's year rule with TPI Game.exe's state-4 branch (a condemned object refunds nothing, 0x5E765B) but leaves out the routine's first branch, a thing flag (+0x43 bit 0x10) that gives 100, 67, 34 or 0 percent from a count read through a script-side lookup, so an object with that flag takes the year rule | what writes the thing flag +0x43 bit 0x10 and the lookup behind 0x40AE89 and 0x4078BA |
| ECON-062 | `Economy/EconomyObjectCatalog.cs` | Theme Park Inc's rides carry UsageInfo.InitPricePerUse (20) and Info.ExpectedPrice (25) in their settings, but OpenTPW's guests pay nothing for a ride, as in Theme Park World, so a Theme Park Inc ride earns no takings and the keys are read without being used | how Theme Park Inc's guests decide and pay for a ride (the use routine's price step and the meaning of ExpectedPrice and Upgrades[n].CashPerBrainPoint) |

Closed or split as OpenTPW design choices (user decision, 2026-10-16): `APPROX:ECON-044` is closed as `[EXT:guest-event-ring]` (the guest event ring is
read only by a debug dump, so there is no gameplay effect to match). `APPROX:ECON-014` is closed: the month gate reads the analyser's cached census (age 5), refreshed by the main loop's crowd read after every fourth turn counted from the turn the step counter started at, and the headless economy is
`[EXT:headless-economy]` (no staff world, a test and tool design); `APPROX:ECON-020` is closed: the loaded caches' start stamp is read (the analyser constructor 0x4C58C0 stamps 0 and the serializer keeps none, so a loaded cache is counted again by its first reader once the turn is past its age), and the stamps OpenTPW's own park save keeps are
`[EXT:save-analyser-stamps]`; `ECON-057` keeps the happiness-change ring's writer and the customers row's second number and sheds the calendar-month sums that
old saves seed their rings from (`[EXT:ring-month-sums]`).
`APPROX:ECON-055` is closed: the carried object follows tp.exe for challenge types 14, 15 and 18 (type 15 now counts the live karts' crossings, 0x4CC4B0, and type 21 the live track rides' sections, 0x4CC5D0, instead of reported records) and the accumulator's filter stops the admission events of type 11 (posted as (11, 1, 0, 0) by 0x4C757D) with the other object-less events.
`APPROX:ECON-001` is closed as `[EXT:fixed-catch-up-cap]` (the original's loop catches up a backlog of up to 2000 ms in 31 ms substeps with at most three park turns a frame; OpenTPW's deterministic 60 Hz clock runs at most 16 ticks a frame, at most two park turns, to keep a slow machine from spiralling; the rules themselves are read and the 60 Hz sampling is `[EXT:fixed-60hz-clock]`).
`APPROX:ECON-054` is closed as `[EXT:headless-economy-dispatch]` and the headless part of `APPROX:ECON-023` as `[EXT:headless-economy-breakdown]` (user-approved design choices, 2026-10-16: they exist only in the test and tool economy without a map or ride scripts, a level runs the staff world and the ride scripts; the routines of ECON-023 are read). The split ride state word of `APPROX:ECON-023` is not an EXT (real parks have it too, user decision) and stays an open approximation. The headless handyman dispatch of `APPROX:ECON-022` is also `[EXT:headless-economy-dispatch]`; ECON-022 stays open for the rest.

## Open questions

- Which keys step the original's clock scale and whether a speed control is shown (the scale, its 1.25 steps and range and what it scales are traced and executed), and runtime turn timing under load (the turn length, the real-time rate at the default clock scale, the calendar start and civil months are traced and executed).
- Whether building is allowed in the red.
- `Research.StartingWorkLoad`, researcher `WorkDuration` (`BaseCostPerStaff` and `CostPerQualityLevel` are never read by the Mac binary, F28).
- The track tool's writes of a cell's type and the footprint of a hoarding piece, for the park value (ECON-026); how golden keys are earned.
- The money block before the loan table (87,987 / 87,787 / −12,013), the extra words around the
  challenge table (`09 00 0b 00`, floats 100.0) and the last loan-record word.

### Selected ride upgrade confirmation

The selected ordinary TPW ride detail offers **Buy upgrades** when its factory-owned catalogue defaults support the exact next SAM tier. The confirmation previews the existing economy catalogue's next-tier price; accepting calls the unchanged `TryBuyUpgrade` API for that live instance. Research, Instant Action, pending work, bankruptcy and available-money refusals remain model decisions. A successful request needs a balance greater than the cost and debits nothing: like tp.exe (buy handler `0x4af480`, installation `0x4df8f0`), the mechanic who installs the upgrade debits its cost to Other Costs, whatever the balance by then; the request waits for a mechanic, including when none is hired. A save written while OpenTPW charged at purchase keeps such a pending upgrade paid. Cancel never buys, and there is no cancellation/refund API for already queued work.

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
