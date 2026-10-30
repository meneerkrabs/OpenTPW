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
(`0x4da830`); the wear step writes VAR_WORN below a state of repair of 20 and a relief object below 25, and a finished repair clears it (`0x4df8f0`). ECON-023 (retired in round 10) keeps
the stored states 2 and 3 in the one shared word (`RideStateWord`: the ride update's 0, 1 and 4 and the stored 2 and 3; the bridge's update dispatches on all of it as `0x4e0e00` does); a bought upgrade closes the ride until it is installed (`0xdf928`, static).

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
Patrol areas are simulated in a level: the employee window's patrol button starts the patrol tool, two clicks on the map set the member's rectangle (cell ids `+0x20A`/`+0x20C`, inclusive, whole map by default) and every role's wander keeps to it, walking into it first when outside (tp.exe `0x4B5B50`, `0x524960`, `0x4F9490`, `0x506F30`; [STAFF-PLACEMENT.md](STAFF-PLACEMENT.md), [GUESTS.md](GUESTS.md)). The litter and toilet searches ignore the area.

**Toilets: the door, the call and the clean (tp.exe `0x48CCF0`, `0x4DF300`, `0x4DF390`, `0x4DFE30`, `0x4D7880`, `0x4D73C0`, `0x4DFD80`; GitHub issue 829).** A toilet's window has two controls the player can use. Its door is a toggle (`0x48CCF0`: a non-zero argument closes through `0x4DF300`, otherwise it opens through `0x4DF390`); closing
(`0x4DF300`: the open word `+0x68` clears, the guest being loaded is dropped, script variable `VAR_RIDECLOSED` = 1); it calls nobody and nothing cleans a toilet
because it is closed. The call button (`b_callhandy`, `0x4DFE30`) sets the request `+0x64`, closes the toilet the same way and sends a handyman from anywhere in the park.
A handyman who has no litter within his DetectionRange takes a toilet that is called (any distance, when nearer than the best so far) or whose gauge byte is below 25 and within his DetectionRange (accepted whatever the best so far, as `0x4D79A9` has no best comparison, so the last acceptable one in list order wins)
(`0x4D7880`); it never reads the open word, so a closed dirty toilet is cleaned like an open one, and the patrol area is not read either. He walks to the toilet's access cell
`+0x36`, cleans for `WorkDuration` turns from his arrival (animation 17), and the end of the clean (`0x4DFD80`) swaps the dirty row 6 for the clean row 1, writes script variable
8 (`VAR_WORN`) = 0, sets the gauge to 100, and clears the claim and the request; `0x4DF390` then opens the toilet whatever its door or its call was. The toilet has its own window (`0x497640`, refresh `0x4973a0`): its door (`0x15BC4`) is enabled only while the reopening guard `0x4DF290` allows (not in states 1, 2 or 4, no call pending, entrance linked, and for a track ride type 3 a complete circuit through `0x441970`), so a called toilet cannot be reopened by the player until the clean answers the call. OpenTPW wrote no `VAR_WORN` = 0 on a clean, so a toilet that a handyman cleaned (closed by the player or not) came back open but
its script kept the worn state set below a gauge byte of 25 (`ToiletCleaningFlowTests`).

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
| Advisor `Wealth*`, `TakeLoan.Score`, `InTheRed*`, `WagesHigh.*`, `AwardMoved.Score`, `Bankrupted.Score` | | Advisor message scores and thresholds (`AdvisorScanCallbacks`; ADVISOR-SCAN.md "Money") | used by the advisor scan |
| Advisor `GT*`, `GoldTicketNearTo*` (the near-ticket hints scale the threshold, e.g. profit 0.9 × `ProfitYear`) | | Advisor message scores | unused (advisor slice) |

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
message tube (`0x59b620`, docs/LIPS.md "Captions"), the warnings 102 to 105 are produced by the advisor's scan from the bank (`AdvisorScanCallbacks`: 102 while a loan can be taken, 103 to 105 with none and below 3, 3 or 4, exactly 5 months in the red), and the main loop silences the music and the crowd in state 4
(`0x54f86d`). The five-entry loss pause menu is implemented; the gate-camera scene, its banner and hidden-HUD
root lifecycle are ported (ECON-030; below). With the withdrawals flag (+0x114) clear, debits,
instalments and payoffs charge nothing; it is imported from original saves and set in all shipped ones, and only the online layout replay clears it (`0x404140`, set again at its end), so a single-player park keeps it. Ledger categories come from UITEXT 164–169 and 356–360.

### Lost-park menu and remaining loss scene (ECON-030)

`ParkHud.OpenPauseMenu` follows the branch order of `0x48C150`: the online game type (`0x48C1E3`, represented by a read-only
visit) gets its existing three rows first. Otherwise a bankrupt park (`0x48C27A`) gets exactly Load, Restart Park, Resume Game,
Exit To Lobby and Quit Game, in that order (`0x48C28C..0x48C367`). Save, Publish Park, Options and Go Online are absent. The
ordinary menu is unchanged. The Load row uses text identifier 3 and command 22; 22 is not a second text-table identifier.

These rows reuse the existing callbacks, capabilities and confirmations. In `0x48B6A0`, the loss Load command passes 1 instead
of 0 to `0x49EFB0`, but that helper never reads the argument. Loss Restart, Exit and Quit add only the no-op helper
`0x5DA3C0`; Resume shares the ordinary branch `0x48B984` and does not clear the lost state. The managed pause stack, speed
setting and stale-menu Exit guard stay unchanged. `LostParkPauseMenuTests` constructs the actual menu and drives its rows,
load chooser, confirmations and Back path with synthetic strings and economies.

The loss scene is ported for single-player parks (`Hud/ParkHud.LossWindow.cs`). The bank's bankruptcy message (`0x5168F0`)
selects the park's gates and enters the window root 0x7472 (`0x481B50`, `0x48AC40`): the camera rides the gate's seat (the
winning-view machinery of `Level.EnterWinningView`), the layout `LossScreen` (`0x74FB20`, rect 174,500 - 1874,1000, generated into
`OriginalLayoutTables`) shows the `s_bkrupt` banner model with the 24-step animation table at `0x74FC38` (35 ms per step, a jump to 0
at the last). Handler `0x48A740` accepts modifier-free Escape and a right release after more than 2000 ms when RMBCancel is on;
either leaves the camera and creates the hidden-HUD overlay (`0x4A29D0`): the HUD is hidden, the banner stays and the park camera is
the player's again. Escape in the overlay (`0x4A2925`) opens the loss pause menu above. A loaded lost park restores the overlay
without entering the camera (`0x4815E0`, `0x48ADB0`); the lost flag round-trips in the existing save field, so old saves load as
before. The window pauses nothing: calendar and bank go on. The scene is gated on "no CoopSession" and on a park that is not a
read-only visit, and not on Theme Park Inc, which has no `bankrupt.MD2` banner (its own exe was not read), so shared parks, visits and TPI parks are unchanged until the co-op owner decides. While a deactivation holds the P window over the loss window, the window gets no Escape or right click (the P window holds the focus, `0x47EE67`) and P alone resumes (`0x4A2706`).

Money advice is already produced by `AutomaticAdvisor.ScanPark` through `AdvisorScan` and `EconomyMoneyState`: 102 requires
negative cash and a loan available; 103-105 require no loan and select below 3, 3-4, or exactly 5 months in red. Existing
`AdvisorScanTests` cover the real queue and selection. No second warning producer is needed.

Still open (ECON-030): the overlay's state-4 key branch (`0x4A2925`) and the pause/play early-out (`0x4A2AC9`) are not
refusals, so OpenTPW's direct purchase, hire and open guards under the overlay remain unaudited; the banner's draw is matched to
the table but not to a captured original frame. `ParkLossWindowTests` and `ParkLossCorpusTests` drive the window, the exits, the
overlay, the restore and the layout in all four themes.

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
| Objects | RideSystem records (also those with an unverified footprint) + attraction things | registered uncharged; `Price`, `CostOfGoods`, `Level`, `Repair`, `LifeGauge`, `IngredientAmount`, `QualityOfGoods` (its byte) from the matching attraction (same Info.Id and cell); the state word (1 and 4 broken down, 2 the next level waiting for a mechanic and its cost, `0x4e0e60`), `ChanceOfLosingPercent` (100 − the win byte), the total profit (takings − costs, `0x4e1c60`), the maintenance call; the operating speed, capacity and duration become the ride runtime's words when they differ from its tier's (`Level.ImportOriginalRideSettings`; the loader stores them raw, `0x4dcc6c`..`0x4dccb8`); the customer count and time stamp are the use count and build time of the scrap rule (`0x4e2290`, thing +0x1a0 and +0x18) | Traced; `mNumWalkAways` is imported (the loader reads it back, `0x4dc12b`; `0x4e1670` raises it and nothing else reads the field); a closed open word of a working object, names SAVE-006; every staff claim (the attraction's claimant word and turn, `0x4e01f0`) is imported as saved and lapses through the lazy query `0x4e0220`, including when the owner has no current job, a claimant the import does not hold counting as allocated to nothing (a negative claimant word) and a stamp after the save's turn taken as made on it (the third gauge, `+0x4c`, is only written and saved: nothing in tp.exe reads it) |
| Staff | models 4–8 | `Staff.Members`: type, grade, training percentage, happiness, energy, inline name, hire tick (the clock stamps whole turns, `0x4f8690`), idle stamp (`+0x200`), patrol rectangle (corner cell ids y × 128 + x + 1, `0x506ed0`), the state word `+0x19c` as the member's job with its target (a mechanic's ride and repair timer, a handyman's toilet and cleaning start, an entertainer's or researcher's work start, the rest area while walking to or resting in it; thing ids name the reloaded attractions) and display state (`0x5064f0`), and the walk on to `mAccurateDestX/Y`, which the person loader routes to again (`0x4f9407`); a world takes back the ride or toilet claim when it places the member | Traced (`0x504de0` and the role serializers); `mJobsDone` needs no import (only the constructor and the serializer touch it); a litter job (SAVE-010) and a guard's guest (SAVE-002) are dropped |
| Open state | World `mParkClosed` | an imported park opens when guests attach unless it is set; a park started without a save stays closed until the player opens it (`0x51562d`) | Traced |
| Challenges | challenge system (model 19, tp.exe `0x4d2320`) | won slots finished, declined counts, a running challenge (deadline from `mNextChallengeEventTime`, value as baseline), an unanswered offer, the next offer day; before the system is switched on the first offer is on park day `DaysUntilFirstChallenge` or the day the park is first open after it (Easymode: day 540, not counted from the import) | Traced; slots that differ from the level's challenges are adopted as saved (SAVE-005 retired); the loss counts and the offered words are read and need no import (nothing reads them, `0x4d1ae0`); a save whose slots do not match the level's challenges keeps the new park's state (SAVE-005) |
| Research | research lab (model 14, `0x502dc0`) | projects whose points reach their cost researched, started projects' points, each category's effort and current project, the work load | Traced; the object controls' researched bytes (`0x4d3aa0`) are read and agree with the lab's research on the shipped save and on all 11 corpus saves, whose labs also list OpenTPW's projects in the same order; the highest open groups and known current projects are imported as saved, even when the picker would not offer the current project; unknown object/project identities are dropped and reported (SAVE-007) |
| Recent research | world-referenced TPW park analyser (model 13) | four capacity-three presentation rings in `Info.WhichUIType` order; physical head, wrapped flag, scratch word and all three slots | Paid completion events only, retaining duplicates, upgrades and add-ons; free unlocks do not append. Typed/bounds-checked import and a 68-byte-only export patch. No inferred Theme Park Inc policy |
| Staff pool | staff hiring pool before the calendar (tp.exe `0x507850`) | `Staff.Candidates`: role, name index, grade, leaving on the first turn past 4 × its timeout from its stamp; the next refill past 4 × `TimeBetweenStaffUpdates` turns from the pool's stamp (`0x5084f0`) | Traced; sub-types are the candidates' sprite variants, kept by the members hired from them; the on-pointer flag SAVE-004 (the per-role counts and stop-producing flags are recomputed by the census `0x508000` before every read) |
| Staff HQ | staff HQ (model 9, `0x508bb0`) | training budgets (handyman, mechanic, entertainer, guard, researcher, `0x50c800`), strike levels, active flags and last-considered turns | Traced; the picked-up id is read to the end (`[EXT:staff-pickup-reconcile]`: the original keeps a member saved in state 7 in that state for good, OpenTPW puts him back to idle); the force strike is never set in play, the trained byte never read |

The saved guests are restored by the level's guest simulation (`GuestSimulation.ImportOriginal`, docs/TPWS-PAYLOAD.md): those on their way in or out and those walking, deciding, going to a bin or a ride or leaving in the park keep their thing id, type, cash, meters, exit counter, prankery flag, balloon, costume, histories and walk speed and walk on to their saved destination; guests leaving a ride (15) walk on to their saved destination, and guests in a queue (states 11–14, or its idle interlude, state 8 with the saved state 11) are restored in their attraction's saved queue (the head mFirstInQ, the links mQNext and mQPrev and the called guest mPersonBeingLoaded, read as saved; a queue that does not read as the loader's chain is refused with its reason); a guest held by a guard (17) is imported held with its escorting guard (docs/TPWS-PAYLOAD.md); guests using an attraction (16) or walking to the arrival vehicle (20) are not imported (SAVE-002); a park guest (states 6–10) standing off the paths is imported where it stands and recovers through its own state handlers, as tp.exe's load leaves it (`0x4F9407`, `0x4F9490`).

Imported: `mRandomSeed`, the state of the one world generator (`0x516330`; SAVE-001 retired), so the park continues the saved stream. Not imported and reported in `Evidence`: the guests the guest simulation cannot restore (SAVE-002), the hiring
pool's candidates saved on the pointer (SAVE-004), unknown research object/project identities (SAVE-007; reported; none in the corpus), the staff HQ's picked-up member (`[EXT:staff-pickup-reconcile]`; its force-strike word and trained byte are never set or read in play), the advisor presentation block, world state, unsupported arrival scheduler/vehicle continuation,
calendar session start, the advisor controller's presentation words (its current record is the last tutorial advice when an automatic advisor attaches), its pending
advice and history when no automatic advisor is attached, and the subsystem things without an
OpenTPW counterpart (SAVE-010). The camera, the cell grid's litter (with the handyman jobs on it), the lab's highest open groups and the analyser's recent-research rings, the gate's two sign lines as the park's name (a player's own save), the open word of idle attractions and the staff pool's variants are imported. The Scripts sprite scripts, the sound mixer, the message-centre lists and the track and flying ride chunks are not restored (`[EXT:original-engine-state-dumps]`); the Particles pool is. The RSSE block's script ids, list order, pass counter and run state are applied (`SavedScriptImport`, `RideVM.ImportSavedRunState`); the records' object lists, node, bounce and walk tables and the animations on their objects are not. A script with a saved child or sound child keeps its fresh state; saved child records are left for their parent to recreate. Staff values
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
| Bankruptcy | More than 5 thirty-day months since the debit that went into the red, counted at month ends (`0x4d0370`) | Message type 2 loses the park (world state 4): thing updates stop, calendar and bank go on; music and crowd go silent; advice 106 shows its caption; money advice 102–105 and the five-entry loss pause menu are implemented; the loss camera/root/input lifecycle remains open (ECON-030) |
| Staff | Pool sizes, maxima, wages, training prices; traced and executed: energy and happiness per turn, rest at the Staff Room, the strike test and levels (F22); hiring is free and a dismissal costs one monthly wage (executed, F28) | Candidate grades, the handyman's job duration, the mechanic's walk to the ride; the staff state machine behind the vitals (ECON-014) |
| Maintenance | Wear rates, red lines, maxima, upgrade durations, worn threshold 25 | Traced: wear every 64 turns from the ride script's running state, riders and speed; breakdown check every 8 turns, only for rides that are open and not broken down (F27); a repair takes WorkDuration x (100 - state of repair) / 100 park turns, an upgrade WorkDuration x DurationOfUpgrade (executed, ORACLE F19); repair restores 100.0 (the float at TOC −11016 in the binary) but not the life gauge and opens the ride (F27); the life gauge starts at 100 (`0x100DAB1C`). The ride-state word is the original's (ECON-023, retired); a condemned ride gets no mechanic (the arrival `0xd9c0c` releases one, F19) |
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
newest save there, so each player keeps one running park per theme. The PC build agrees on the timing: its park-save writer is called when a park is left, not on a timer (tp.exe, REA 2026-10-18; PARK-SAVES.md, "Periodic autosave"), which OpenTPW's periodic autosave extends (`[EXT:periodic-autosave]`). A park entry also writes a
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
  (GuestPaid, ObjectBuilt/Sold, FixedItemMade, UpgradeBought/Completed, StaffHired/Fired, WagesPaid, Loan*,
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
  `RegisterExisting` for imported/fixed items; `RegisterFixedItemMade` for the fixed items made in play, an arrival vehicle at its
  first trip and the finale's End (`Level.BindFixedItemMadeInPlay`): the same uncharged record (none for a type the catalogue does not
  know), plus the `FixedItemMade` event, raised either way, that moves the advisor's construction stamp as `ObjectBuilt` does
  (tp.exe `0x4DB7A3`); `SetObjectOpen`, `Sell`, `TryBuyUpgrade`,
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
The running game never writes original TPWS/TPWI files; the library writer (`OriginalParkExport`, TPWS-PAYLOAD.md) is not reachable from it.

## Approximation register

Project rule: no value or rule may be invented except for upscaling, custom resolutions, online and
mods. Until proven, every economy approximation carries a `// [APPROX:ECON-NNN]` comment at its code
site, is listed in `Economy/EconomyApproximations.cs` and is logged once at startup as
`[APPROX:ECON-NNN]` warning when an original level loads. Values from original data are tagged
`// [DATA:<file>:<field>]`. Paths are relative to `source/OpenTPW/` (`Files/` = `source/OpenTPW.Files/`).

| Id | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| ECON-022 | `World/Staff/StaffWorldSimulation.Handymen.cs` | all staff in a level walk and work as the original's state machines (WANDER-plan §4, StaffWorldSimulation) on the guests' walk (the route search, the steering step 0x004FA2A0 → 0x0050F3B0 with avoid_walls, follow_path and separation, the walk mask +0x188), with the shared walks to a rest area and to the strike area; a striker walks out along arrival lane A (OpenTPW's path grid ends at the park entrance) and is not sent after a prankster until he is back on the paths; separation meets the cell's one thing chain, guests and staff interleaved by entry time as the original's (WALK-003, 0x005102F0); the stranded stamp the wander sets (+0x198, 0x004FA670) and SetDest's quick failure while the cells around stay unchanged (0x004FA770) are not kept, as for the guests; the toilet, rest-area and ride searches take the objects newest first by OpenTPW's object id, which is the original's head-inserted list (game +0x1DA746, 0x00519D80) for objects placed in the park, where a park loaded from an original save has the chain the save keeps (each attraction's mNext +0xC and the head word), which the import does not follow; a toilet's, bin's or rest area's access cell is its join or entrance cell or first path neighbour; an entertainer's performance has no animation (the original draws one, 0x004D46D0), where the guests do turn to watch it as the original's state-6 step does (0x004FEFF4 with the state-14 test 0x004FF04A, see GUESTS.md, Deciding and wandering); a picked-up entertainer's or guard's row stays on the cell he was lifted from; a guard carries a caught guest to the park entrance when the ticket booth is no path cell, where the original routes only to the booth gate cell, twice (0x004D6790 → 0x004D8610), and otherwise sets the guest down (0x004FEB50) | the +0x36 access-cell writer, the animation counts (0x00541FA0 and the fix animations) |
| ECON-030 | `Economy/ParkEconomy.cs`, `Hud/ParkHud.cs`, `Hud/ParkHud.LossWindow.cs` | the lost park follows the bank handler, thing-loop stop, zero arrival target and silent music/crowd; AutomaticAdvisor already produces money advice 102-105 through AdvisorScan and captions advice 106 on each bank message. The five-entry pause menu (0x48C150), the loss window 0x7472 with its gate camera (0x5168F0 -> 0x481B50 -> 0x48AC40, handler 0x48A740, layout 0x74FB20, banner table 0x74FC38), its dismissal into the hidden-HUD overlay (0x4A29D0) and the loaded-loss restore without the camera (0x4815E0 -> 0x48ADB0) are ported for single-player parks. The audit of the economic guards is done: tp.exe reads the world state 4 only at the bank handler (0x4D030F), the arrival target (0x4CF5BC, zero), the thing loop (0x516390), the music and crowd sound (0x54F862, 0x54F8B2), the load restore (0x4815F2), the pause menu's entries (0x48C27C) and the HUD's overlay (the key branch 0x4A2925, which lets only an Escape without modifiers through to the pause menu, and the hide toggle's early-out 0x4A2AC9); no purchase, hire, loan, build or open routine tests it, so the original refuses nothing in a lost park itself and its HUD, which holds every such control, is hidden. OpenTPW's overlay does the same for a single-player park (every shortcut and click is off under it, ParkHud.ParkShortcutsAllowed) and its economy-level refusals (ParkEconomy.IsBankrupt) stay as the stand-in where the overlay is not shown (co-op, visits, Theme Park Inc), where they are the only refusal. The banner's animation is read to its clock and cadence (the step runs on the frame message 0x1e, which the per-frame update 0x658F6C sends down the window tree from 0x48A5DF, through 0x65C357 to the stepper 0x65E9A4: strictly more than the interval 35 ms since the last step on the clock timeGetTime, 0x65968E), as UiWindowAnimation steps it once per drawn frame. Still open: the loss scene in co-op and visits (gated off until the co-op owner decides) | the co-op owner's decision for the loss scene in shared parks |
| ECON-049 | `World/ParkGate.cs` | the back-of-queue link test (tp.exe 0x004DE4A0) reads a queue ride whose entry has no cardinal link bit as unconnected (no front: 0x004DE040 writes 0, so the back of the queue 0x004DE130 is 0) and runs for rides with real queue cells (their back cell's link byte must differ from its queue link); a ride whose entry OpenTPW's world cannot reconstruct (an untyped entrance), a queue ride whose front holds no queue cell and an object without Info.HasQueue on untyped geometry are taken as connected, because OpenTPW's ride entrance cells are not all typed map cells with a link byte, so tp.exe's front cell and the entrance-to-path test of objects without a queue cannot run there; nothing that tests the link closes an open ride (the repair 0x004DF8F0, the upgrade cancel 0x004E0050, the toilet clean 0x004DF390 and the track-edit exit 0x00447520 open regardless); the object window's EXIT NOT CONNECTED status (the exit test 0x004DE5A0 of 0x00485F60: the exit cell's link byte, the cell in front of it and the edge test 0x004D8750 in the three directions that do not lead back) is shown for placed exits, and an exit cell without a cardinal link byte, a neighbour off the map and a bridge without a placed exit keep the permissive answer | the entrance cells' types and link bytes in OpenTPW's cell map (WALK-005) |
| ECON-050 | `World/ParkCellMap.cs` | the guest grid takes the world init's types and flags (tp.exe 0x00536490 with the terrain test 0x00450420: 7, 2 or 30 where no terrain lies, 1 with flag 0x20 for an InitialPath cell and 0 inside, flag 0x40 on every cell without 0x20, which the level's land then clears inside the fence) and the footprint types a placement writes (0x00528A70: 4, 9 and 10, the owner word, the '@' and add-on cells, the cells outside the entrance and exit), and a removed object leaves the cells the demolition 0x00527EE0 leaves (DemolishFootprint): pass 1 clears the NoModify flag of the path cell in front of an entrance or exit that fewer than two entrance or exit cells link to and the MAP marks no initial path, pass 2 clears flag 0x20 and runs the cell remover 0x005367A0 on every cell of the footprint mask (types 1 and 10 unlink eight link bits, 3 and 9 the four cardinal ones, and every removed type 1, 3, 4, 9, 10 and 0x15 is left with links, queue link, owner and the whole flag word zero; a type-3 cell refunds while its owner cell is not empty; the other types are left alone), the vectors being the executable's own on synthetic cells (ORACLE.md X36), and the record clear 0x0053B280(0, 0, 1) runs on the second layer's record under each of those cells (TrackEditor.ClearUnderDemolition: the forced clear (0, 0) of the ride's track editor, so a tile goes with its refund and the arms of the tiles beside it, a ride's station tiles go with its footprint, an add-on object takes the tile it stands on, and the add-on a cleared tile takes demolishes the records' flag 0x10 alone, the third argument 1 of 0x527EE0; ORACLE.md X37, 17 cases of both families, replayed by ParkDemolitionTrackTests). Residual: the queue is cleared by the owner scan and the ride's own queue chain (QUEUE-015) where the original replays the corner stack 0x530120 with the forced-clear flag 0x81D7A8; the exit and entry tiles that the record clear spares (the track tool's globals 0x7639E0/E4 and 0x7639E8/EC, whichever ride the tool last worked on when the bulldozer clicks) are the demolished ride's own; a map without a level has no track editor to take the record clear (a record of no ride is erased alone); a read-only visit or a Theme Park Inc layout keeps the cells it was given | the queue demolition's corner stack replay 0x530120 with the flag 0x81D7A8 run in the oracle against the owner scan; which tile the stale tool globals name at the bulldozer's click (the last track tool's exit and entry) |
| ECON-058 | `Economy/ParkObjectives.cs` | all 33 progress and success/failure types use immutable payloads and keyed localized templates; terminal instances retain read rows and use an unscaled 30-second lifetime plus delayed final release, with a named managed suspension mapping; offers retain OpenTPW wording/buttons and the 90 s expiry with decline on removal (UI-025); the repost of a refused or loaded-park offer stays an extension; full native timer queue/wrap semantics and focus/modal ownership parity remain open | runtime visual validation, the repost extension's scope, remaining focus/modal ownership and native timer queue/wrap integration; UI particles remain PTCL-908 (docs/reverse/CHALLENGE-TERMINALS.md) |
| ECON-059 | `Economy/ShopGoodsCost.cs` | the sum, the factor and the product are rounded to a single after each step, as the x87 does at the control word 0x7F that the shipped D3DIM CreateDevice sets (the DDSCL_FPUSETUP flags 0x851 at 0x563914 and 0x808 at 0x563B7A let it) on the one thread that runs the economy (the thread starters, _beginthread 0x67E050 from 0x5C8B7C (the helper 0x5C8B70, called from 0x5D1C04, 0x5D219E and 0x5D22F0), 0x66EFE8, 0x6727DA, 0x6B6102, 0x6B9F0C, 0x6BA362 and 0x6D2FCA and _beginthreadex 0x67F780 from 0x5FBC4E and 0x632639, all lie in library code and none runs the economy; the renderer 0x576EC0 restores the word, and bit 1 of the render-state word 0x8BD508, which would restore extended precision through 0x591250, is set by no writer), but on the software renderer path (0x8BD524 = 4, chosen at 0x44DD50..0x44DDE9 from option bit 0x40 of 0x79D86C or after a failed hardware init) no D3DIM device is created, nothing in tp.exe changes the C runtime's 53 bits there, and a run on that path would round at 53 bits, which OpenTPW does not model | the thread's control word under the software renderer (a captured cost of goods with a quality or ingredient byte other than 50 on that path) |
| ECON-060 | `Economy/ParkEconomy.cs` | Theme Park Inc's month message 0x5DEFFF debits each object's current level's Upgrades[n].RunningCost (0 in ride state 1 or 4) together with the costs its sales and plays booked during the month (the accumulator +0x938, raised by 0x5E60E0, which debits nothing: OpenTPW's Theme Park Inc goods and prizes are booked to the accumulator at the sale and paid at the month end, as one debit per object), but its statistics are monthly rings (costs +0xB88, profit +0x1028, rolled by the same message) where OpenTPW's are the daily 30-day rings of Theme Park World, so the month's running cost enters today's slot of the daily ring, and the debit's order against the bank's month handler 0x5C0B10 is not read, so OpenTPW debits the running costs after the wages' place and before the loan instalments | the listener order of the month message in Theme Park Inc (the bank's handler 0x5C0B10 against the objects' 0x5DEFFF: the things' ids decide it, and a prebuilt park's ids come from its save) and the monthly statistics rings' readers (the messages 0xC and 0xD that 0x5E60E0 posts go to the analyser, 0x5AEF20: none of the debit sites of 0x5C07B0 lies in its code) |
| ECON-062 | `Economy/EconomyObjectCatalog.cs` | Theme Park Inc's rides charge UsageInfo.InitPricePerUse (20, the thing's +0x94, 0x5DB58D) for each use as Game.exe does (0x6126D0 (called at 0x61193B inside 0x6118D0, from 0x619A8E): the fee the guest recorded at the call, 0x619670, is credited to the bank at once, 0x5E5DE0, and leaves the guest's purse) and refuse a guest whose purse is below it (0x61243A), but Game.exe's price test 0x6120C0 also refuses a price above the guest's willingness to pay, which OpenTPW's rides do not: its base is Info.ExpectedPrice (25, type +0x88) in place of Theme Park World's InitCostOfGoods, with no 115 percent, raised by how well the attraction's rating byte (0x5E3DF0 through 0x403062) fits the guest's expected byte (+0x2A8), and the rating byte (a per-kind function of the ride's excitement words) is not ported | the ride rating byte 0x5E3DF0 (its branches by Info type word +0x14C, the words 0x4099F8 and 0x406433 read) and the TPI price test's own statistics (+0x5D8, +0x660, +0x4C8 of the statistics block); Info.WorkingLife (+0x8C) and Upgrades[n].CashPerBrainPoint remain unread |

Closed or split as OpenTPW design choices (user decision, 2026-10-16): `APPROX:ECON-044` is closed as `[EXT:guest-event-ring]` (the guest event ring is
read only by a debug dump, so there is no gameplay effect to match). `APPROX:ECON-014` is closed: the month gate reads the analyser's cached census (age 5), refreshed by the main loop's crowd read after every fourth turn counted from the turn the step counter started at, and the headless economy is
`[EXT:headless-economy]` (no staff world, a test and tool design); `APPROX:ECON-020` is closed: the loaded caches' start stamp is read (the analyser constructor 0x4C58C0 stamps 0 and the serializer keeps none, so a loaded cache is counted again by its first reader once the turn is past its age), and the stamps OpenTPW's own park save keeps are
`[EXT:save-analyser-stamps]`; `ECON-057` keeps the happiness-change ring's writer and the customers row's second number and sheds the calendar-month sums that
old saves seed their rings from (`[EXT:ring-month-sums]`).
`APPROX:ECON-055` is closed: the carried object follows tp.exe for challenge types 14, 15 and 18 (type 15 now counts the live karts' crossings, 0x4CC4B0, and type 21 the live track rides' sections, 0x4CC5D0, instead of reported records) and the accumulator's filter stops the admission events of type 11 (posted as (11, 1, 0, 0) by 0x4C757D) with the other object-less events.
`APPROX:ECON-001` is closed as `[EXT:fixed-catch-up-cap]` (the original's loop catches up a backlog of up to 2000 ms in 31 ms substeps with at most three park turns a frame; OpenTPW's deterministic 60 Hz clock runs at most 16 ticks a frame, at most two park turns, to keep a slow machine from spiralling; the rules themselves are read and the 60 Hz sampling is `[EXT:fixed-60hz-clock]`).
`APPROX:ECON-054` is closed as `[EXT:headless-economy-dispatch]` and the headless part of `APPROX:ECON-023` as `[EXT:headless-economy-breakdown]` (user-approved design choices, 2026-10-16: they exist only in the test and tool economy without a map or ride scripts, a level runs the staff world and the ride scripts; the routines of ECON-023 are read). The split ride state word of `APPROX:ECON-023` was not an EXT (real parks had it too, user decision) and is now one shared word (`RideStateWord`: 1 and 4 written by the ride update, cleared by every opener); the approximation stays open for the stored states 2 and 3. The headless handyman dispatch of `APPROX:ECON-022` is also `[EXT:headless-economy-dispatch]`; ECON-022 stays open for the rest.
`APPROX:SAVE-007` remains open only for unknown object/project identities: a lab project of an object the level archives lack, or a current project absent from its category's reconstructed table, is dropped and reported as "Research not imported". Original continuation with those missing identities, or an explicit supported-save scope decision, is still needed. The 11 corpus labs are pinned (OriginalSaveImportCorpusTests.TheElevenCorpusLabsArePinned: projects, started and researched counts, effort split, work load, current projects, highest groups and the object controls' agreement), but contain none of those cases. The lab's table order is the original's object scan and the saved highest groups are imported as saved (0x504760). A known saved current project is kept as saved, offered by the picker now or not, as the loader reads it back (0x504760; `ParkResearch.SelectAsSaved`); `SavedResearchGroupTests.ASavedCurrentProjectThePickerWouldNotOfferIsKeptAsSaved` covers that closed-group cursor and its park-save round-trip. That resolved known-project case is not part of the residual.
`APPROX:ECON-059` is narrowed to the software renderer path: the control word of the thread that runs the economy is 0x7F on the hardware path (the D3DIM device creation sets it), the rounding of the goods cost is the single-precision one, and no oracle target is needed for that path.

## Open questions

- Which keys step the original's clock scale and whether a speed control is shown (the scale, its 1.25 steps and range and what it scales are traced and executed), and runtime turn timing under load (the turn length, the real-time rate at the default clock scale, the calendar start and civil months are traced and executed).
- Whether building is allowed in the red.
- `Research.StartingWorkLoad`, researcher `WorkDuration` (`BaseCostPerStaff` and `CostPerQualityLevel` are never read by the Mac binary, F28).
- How golden keys are earned.
- The money block before the loan table (87,987 / 87,787 / −12,013), the extra words around the
  challenge table (`09 00 0b 00`, floats 100.0) and the last loan-record word.

### Ride upgrades

The ride window's `b_upgrade` opens the ride's upgrade list (tp.exe `0x4AE560`, rows `0x4AE140`; [UI.md](UI.md), "Object window"). Outside Instant Action it lists:

- every add-on of the ride the park may buy (its `AddOn.UpgradesId` is the ride's type and its research is done), with its name and price `Upgrades[0].CostOfUpgrade`.
  A click arms the build tool with the add-on when the price is at most the balance, and the placement charges it ([TRACKS.md](TRACKS.md#add-ons));
- the ride's next level, while the ride is not waiting for one, its level is below 2 and research has reached the next: UITEXT 29 with the 1-based next level and the
  price `Upgrades[level + 1].CostOfUpgrade`. A click requests the upgrade only when the cost is below the balance and debits nothing: like tp.exe (buy handler `0x4af480`,
  installation `0x4df8f0`), the mechanic who installs the upgrade debits its cost to Other Costs, whatever the balance by then; the request waits for a mechanic, including
  when none is hired. `[EXT:upgrade-tier-runtime]` lists the level only for a tier the ride's catalogue defaults and runtime can install;
- while the ride waits for its upgrade, the cancel row (UITEXT 30, no price): a click drops the request (`0x4E0050(0)`: the job is released, the ride opens again and
  nothing is charged; `ParkEconomy.TryCancelUpgrade`, event `UpgradeCancelled`).

A list without rows shows its message instead (UITEXT 27 in Instant Action, 28 at level 2, 26 otherwise), one line per row, which a click ignores. The ride window's
refresh fills the list again while it is open (`0x4ADE40`), and the list closes when its ride is no longer the selected one or loses its economy record, so a row acts
only on the ride it was opened for (`ParkHud.UpgradeListCurrent`). The button shows for a Theme Park World ride with an economy record outside a read-only visit (not for the developer prototype);
sparse, custom, stopped or faulted runtimes get no level row (coasters list theirs as any ride does: every shipped coaster has two researchable levels, and neither `0x004AE140` nor `0x4df8f0` tests the track type), and neither does a broken-down ride whose upgrade waits (OpenTPW's state keeps both, the
original's one state word holds either). A save written while OpenTPW charged at purchase keeps such a pending upgrade paid. Completion
applies only the existing minimum catalogue-default contract: live CAP/DUR remain authored, with no native reset or full performance claim. Authored CAP9/DUR77, an active
WAIT deadline and occupied rider/head slots survive a purchase from the list and the mechanic's completion; the other instance keeps its prior fallback capacity
(`RideUpgradeIngressTests`, `RideUpgradeReviewTests`: synthetic HUD, ledger, queued-no-mechanic and completion controls, which do not qualify original assets or rendering).

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

### Original arrival scheduler attachment

The TPW level attaches the saved arrival timer, signed group remainder and active flag after importing its objects and scripts. This bounded import requires an exact saved park-turn clock, a timer no later than that turn, all cached/current vehicle handles zero, and an attached vehicle host with no existing vehicles. It makes no dispatch, random draw or history event at attachment. The next ordinary update continues the saved group or waits for the saved timer; existing own-save fields preserve this continuation.

Any failed check leaves the complete scheduler unchanged and reports ARRIVAL-001. Nonzero cached/current vehicle handles still need verified object identity and successful script continuation. The decoded arrival rate, target capacity and gates-open fields have no applied host behavior. TPI and online visits are unchanged. Levels without a vehicle host retain their existing fallback adaptation rather than claiming native vehicle continuation.
