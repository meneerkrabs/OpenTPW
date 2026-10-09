# Park management simulation (economy slice)

Evidence date: October 9, 2026. Static reading of the original `.sam` settings, `.str` string
tables and the jungle `Easymode.TPWI` payload; no original executable was run, and a plain string search of `TP.ICD`
finds none of the setting names, so the formulas cannot be read from the binary. This is an OpenTPW simulation
**built on** original data, not a reproduction of the original rules: every place where the original
behaviour is unknown is marked *approximation* below and in the code documentation.

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
| `LoanInfo[0–7].LoanAmount` / `.APRInPercent` / `.RepaymentPeriodInMonths` | 10,000–100,000 / 18–23 % (easy 0) / 24–48 | Loan offers (UITEXT 170–180: lender, term, amount, interest rate, monthly repayment, total payable) | data; repayment at APR > 0 approx |
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
| `UsageInfo.GoldenTicketCost` | 0–5 (10 objects) | Golden tickets needed to buy (UIHELPTEXT 152) | data (tickets are spent: approx) |
| `UsageInfo.ShopType`, `UsageInfo.SpecialIngredient` | 1–6; 0–4 | Shop kind (jungle: 1 gift, 2 burger/fries/ice cream, 3 restaurant, 4 drinks, 5 costume, 6 balloon); ingredient "0 none, 1 Fat, 2 Salt, 3 Ice, 4 Sugar" (`INGREDIENT.str`) | data (challenge sales) |
| `UsageInfo.LitterEffect` | 0–50 | "How much litter to add" per purchase | approx (1/100 item) |
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
| `StaffPoolInfo.BeginningNumberOf*`, `Max*`, `Min*InPool` | 5/5/5/5/2, 6/5/6/4/3, 1…0 | Hiring pool sizes | data |
| `StaffPoolInfo.Max*InPark` | 30/15/30/15/10 | Employee maximum per role (TAG_SYSTEM 135–144 say 10; online files use 10) | data |
| `StaffPoolInfo.ChanceToGetGreat*`, `AvgGradeOf*` | 20/10/35/2/15 %, 1–2 | Candidate quality | approx (great = average + 2, else average ± 1) |
| `StaffPoolInfo.TimeBetweenStaffUpdates`, `MaxNumberOfStaffPerUpdate`, `StaffTimeoutTime` | 90, 10, 120 | Pool refresh/expiry | approx (seconds at normal speed) |
| `StaffPoolInfo.BaseCostPerStaff`, `CostPerQualityLevel` | 2000, 100 | Unknown (hiring fee or pool valuation) | unused — hiring is free |
| `*ConstsPerGrade[g].PoundsPerTrainingPoint` | 5/8/12/15/0 (researcher 8/12/15/18/0) | "cost to raise the training level by 1%"; 0 = cannot improve grade 4 | data; 100 points per grade inferred from the online-file comments (10 × 100 = "1000 to get up to grade 1") |
| `MechanicConstsPerGrade[g].WorkDuration` | 80…20 | "Speed of fixing improves with training" | approx (game hours per repair) |
| `HandymanConstsPerGrade[g].WorkDuration`, `.DetectionRange` | 40…5; 2–5 | Cleaning speed; litter detection range | WorkDuration approx (game minutes per litter item); range exposed only |
| `Entertainer/GuardConstsPerGrade` (`WorkDuration`, `HappinessEffectOnCell`, `ActivationDistance`) | | Entertaining/pursuit | unused (guests slice) |
| `ResearcherConstsPerGrade[g].WorkDuration` | 10–50 | "How long the researcher researches for" | unused |
| `PerGradeStaffConsts[g].IdleDuration`, `RecuperationRate`, `HappinessRecuperationRate`; `AllStaffConstants.*` | | Rest/happiness | unused (no staff behaviour yet) |
| `Upgrades[n].WearRate` | 0–5 "out of 10" (easy lower) | Ride wear | approx (state of repair −WearRate per open day) |
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
| `ResearcherConstsPerGrade[g].ResearchAbility` | 2–6 (easy 6–20) | Research ability | approx (points per researcher per day) |
| `ResearchTech[g].PercentageForThisTech` | 0,0,80,85,85 | Threshold per group | approx (group g opens when this % of group g−1 of the same category is done) |
| `Research.StartingWorkLoad` | 85 | Unknown | unused |
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
`WaitingTimes.*`, `MapInfo.*`, `FixedItemInfo.*`. OpenTPW uses `Info.AttractionValue` only in the
approximated park rating.

## Original save evidence (Easymode.TPWI)

`SaveEconomyRecords` finds two tables in the decoded payload by structure; `OriginalEconomyImport`
then requires loan amount/APR/term/lender metadata and challenge fields to equal settings exactly.
Monthly repayment equality is required only for 0 % APR, using integer `amount / months`, as
proven by this fixture. Positive-APR monthly values are preserved in the decoded records and
reported as **unverified**, without comparing them to the runtime's annuity approximation.

| Table | Offset | Layout | Cross-check |
| --- | --- | --- | --- |
| Bank prefix | 1,411,362 | 7 × 32-bit words: admission fee, balance, batch balance, withdrawals enabled, last balance, entered-red tick, annual profit | Typed values: 25, 87987, 0, true, 87787, 0, −12013 |
| Loan offers | 1,411,390 | 8 × 32 bytes: available flag, i32 amount, i32 APR, months, monthly repayment, bought flag, months repaid, lender name index | Amounts, APR, terms and lenders equal `LoanInfo[0–7]`; repayments 2777, 1388, 694, 277, 750, 1000, 1666, 2166 = floor(amount / months), i.e. 0 % APR: only `Easy_Standard.sam` matches |
| Challenges | 1,410,409 | 8 × 45 bytes: `i32 type, time, value, object, object2, prize, follow-up`, 14 zero bytes, `u8 independent`, 2 zero bytes | Equal `Challenges[1, 15, 6, 18, 12, 7, 8, 9]` = jungle `ChallengesInThisLevel`, including the `Independent` flags (1,1,1,1,1,1,0,0) |

Proven and used: the 0 % APR repayment formula, the easy balance for the jungle original level, and
the challenge list. The placed objects and fixed items (docs/TPWS-PAYLOAD.md) are registered in the
economy without charge.

**Bank fields identified; simulation state not restored.** The Mac serializer's named fields
and 4-byte writes independently identify the bank prefix and eight-word loan records, and the
actual PC fixture matches that order. The old `i64 amount` combined amount with APR; the old
record's final word belonged to the next loan's available flag. Lender name index is independent
of record order. The typed parser handles nonzero APR and reordered lenders, while import still
requires the original settings metadata to match. APR > 0 repayment arithmetic on PC remains
unproven; each such saved repayment receives an `[APPROX:ECON-006]` diagnostic and is not reported
as matched or used to restore active loans. The current runtime calculation remains the ECON-006
approximation. A positive-APR stored value can differ from that calculation without invalidating
otherwise matching typed data; replacing one unproved formula with the Mac formula is not part
of validation.

The decoded balance is 87,987, last balance 87,787 and annual profit −12,013, consistent with
100,000 − 12,013. The spending history behind that profit is still unknown. This correction
reports the typed bank values but continues to start the simulation at `InitialCash`; it does
not restore active loans or other unimplemented original state. The locator still uses
plausibility limits and lacks framing proof for other saves (**ECON-045**).

The verified PC `Easymode.TPWI` SHA-256 is
`6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`.
The layout review is documented in [PPC-review.md](reverse/PPC-review.md), with the field/call
evidence in [PPC-economy.md](reverse/PPC-economy.md). Regression tests cover positive APR,
permuted lenders, field/flag decoding, truncated prefix/records, ambiguous tables and settings
mismatches; original-data tests check the typed PC fixture.

## Simulation model

| Area | Original-data-driven | Approximation |
| --- | --- | --- |
| Clock | Days, months, years, hours exist (challenge days, monthly wages/loans, yearly scrap, `Clock.RSE` HOUR) | 1 day = 240 fixed ticks (4 s at normal speed), 30-day months, 12 months; speeds Fast ×2/Fastest ×4 are OpenTPW additions (only pause is evidenced) |
| Ledger | Categories from UITEXT 164–169/356–360; integer dollars | Challenge prizes and scrap sales as "other income"; build, upgrade, goods, prizes and land as "other costs"; training as staff costs |
| Gate/shops/sideshows | Entrance fee, shop price and cost of goods, sideshow price/prize/chance of losing | Purchase requires money ≥ cost; litter per sale |
| Construction | Purchase, upgrade and cell costs; research gate; golden-ticket cost | Scrap value basis = catalogue cost up to the current level; park value = sum of scrap values |
| Loans | Offers, lenders, terms; 0 % APR repayment = floor(amount/months) | Annuity formula and monthly interest for APR > 0; early repayment pays the remaining balance |
| Bankruptcy | Six month-ends in the red, warnings at 3 and 5 months (strings) | Advance stops when bankrupt |
| Staff | Pool sizes, maxima, wages, training prices | Candidate grades, pool timing, free hiring, 100 points per grade, mechanic/handyman job durations |
| Maintenance | Wear rates, upgrade durations, worn threshold 25 | Wear per open day, repair restores 100 |
| Research | Items, categories, groups, costs, effort, ability, thresholds | Points per day, group opening rule, cheapest-first order, automatic Instant Action rate |
| Challenges | Definitions, level list, timings, prizes, follow-ups | Type semantics from comments; explicit accept/decline; types 14, 22, 23, 26, 32+ unmeasured |
| Golden tickets | All thresholds | Monthly check; tickets spent on purchases |
| Keys/progression | Keys per theme, theme order (THEMENAMES; ascending key cost); +1 per 3 earned golden tickets; spending tickets preserves keys (manual p. 28) | Start with 1 key; keys persist when entering themes |
| Park rating | — | (2 × happiness + attractions + cleanliness) / 4 |

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
  `TryBuyCells(CellPurchase, count)` for paths, queues, tracks and land.
- `IParkClock` (`Tick`, `Date`, `Speed`) for the frontend and for the `YEAR/MONTH/DAY/HOUR` RSE opcodes.
- `ParkEconomyRuntime` (in `Level.Park`, original levels only): created from `OriginalPark`, ticked
  on the level's 60 Hz `FixedStepClock`, logs `Park clock: …; balance $…` per game day.

## Save format

`ParkSaveFile`: JSON, `"Format": "opentpw-park"`, `"Version": 1`. Stores theme, difficulty and mode
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
| ECON-001 | `Economy/ParkCalendar.cs:33` | one game day = 240 fixed ticks (4 s at normal speed) | capture of the original clock against wall time |
| ECON-002 | `Economy/ParkCalendar.cs:38` | every month has 30 days, 12 months per year | original calendar (binary or captured date display) |
| ECON-003 | `Economy/ParkCalendar.cs:35` | 24 hours per day (Clock.RSE only shows HOUR is used mod 12) | original HOUR range (binary or Clock.RSE trace) |
| ECON-004 | `Economy/ParkCalendar.cs:11` | Fast x2 and Fastest x4 speeds (only pause is evidenced) | original speed controls, if any |
| ECON-005 | `Economy/ParkLedger.cs:53` | challenge prizes and scrap sales are other income; build, upgrade, goods, prizes, land are other costs; loans received are not money in | captured financial screen after these transactions |
| ECON-006 | `Economy/ParkLedger.cs:121` | APR > 0 repayment is an annuity at APR/12 per month, rounded down; interest accrues monthly on the balance | standard-mode save or capture with an outstanding loan |
| ECON-007 | `Economy/ParkEconomy.cs:266` | a repaid loan offer becomes available again | capture of the loan screen after repayment |
| ECON-008 | `Economy/ParkStaff.cs:50` | 100 training points per grade (from Online_Standard.sam comments "costs 1000 to get up to grade 1") | capture of a training run |
| ECON-009 | `Economy/ParkStaff.cs:112` | candidate grade = average + 2 when "great", else average +-1 | hiring pool captures (grade distribution) |
| ECON-010 | `Economy/ParkStaff.cs:64` | TimeBetweenStaffUpdates/StaffTimeoutTime are seconds at normal speed | capture of pool refresh timing |
| ECON-011 | `Economy/ParkStaff.cs:100` | each pool slot above the minimum is filled with 50 % chance per update | hiring pool captures |
| ECON-012 | `Economy/ParkStaff.cs:119` | hiring is free; BaseCostPerStaff/CostPerQualityLevel unused | capture of the balance before/after hiring |
| ECON-013 | `Economy/ParkStaff.cs:139` | training budget is spent evenly over a role at month end | capture of training budget effects |
| ECON-014 | `Economy/ParkStaff.cs:24` | staff start at happiness 100 and it never changes (no strikes) | staff happiness rules (binary/captures) |
| ECON-015 | `Economy/ParkResearch.cs:131` | each researcher adds ResearchAbility points per game day, split by effort | capture of research progress over time |
| ECON-016 | `Economy/ParkResearch.cs:100` | group g opens when PercentageForThisTech % of group g-1 of the same category is researched | capture of new research groups appearing |
| ECON-017 | `Economy/ParkResearch.cs:110` | items are researched cheapest first within open groups | capture of research order |
| ECON-018 | `Economy/ParkResearch.cs:41` | ride upgrade levels and add-on objects form the "upgrade" research category | research lab capture |
| ECON-019 | `Economy/ParkResearch.cs:136` | Instant Action research runs at one grade-2 researcher without staff | Instant Action capture |
| ECON-020 | `Economy/ParkEconomy.cs:28` | a sale drops LitterEffect/100 litter items | capture of litter after sales |
| ECON-021 | `Economy/ParkEconomy.cs:185` | a repair takes WorkDuration game hours (x DurationOfUpgrade for upgrades); mechanics are dispatched instantly | capture of repair duration per grade |
| ECON-022 | `Economy/ParkEconomy.cs:203` | a handyman removes one litter item per WorkDuration game minutes, park-wide | capture of cleaning speed |
| ECON-023 | `Economy/ParkEconomy.cs:217` | an open ride loses WearRate state of repair per game day; breakdown at 0 | capture of state of repair over time |
| ECON-024 | `Economy/ParkEconomy.cs:160` | a repair restores state of repair to 100 | capture after a repair |
| ECON-025 | `Economy/ParkEconomy.cs:329` | scrap value basis = catalogue cost of all levels up to the current one | capture of scrap value |
| ECON-026 | `Economy/ParkEconomy.cs:337` | park value = sum of scrap values | capture of the park value screen |
| ECON-027 | `Economy/ParkEconomy.cs:354` | park rating = (2 x happiness + attractions/3 + cleanliness) / 4 | park rating formula (binary/captures) |
| ECON-028 | `Economy/ParkEconomy.cs:434` | purchases need a balance covering the cost | capture of building with too little money |
| ECON-029 | `Economy/ParkEconomy.cs:438` | golden tickets are spent when buying items with GoldenTicketCost | capture of ticket count after such a purchase |
| ECON-030 | `Economy/ParkEconomy.cs:110` | the simulation stops once bankrupt | capture of the bankrupt state |
| ECON-031 | `Economy/ParkEconomyRuntime.cs:33` | imported parks are opened on load (open state not decoded) | park-open flag in the save |
| ECON-032 | `Economy/ParkEconomyRuntime.cs:46` | the prototype ride is registered uncharged | replace with a real catalogue purchase (rides slice) |
| ECON-033 | `Economy/ParkEconomy.cs:304` | golden tickets are checked at each month end | capture of the award timing |
| ECON-034 | `Economy/ParkObjectives.cs:113` | challenge type meanings come from Challenges.sam comments (shop types by ShopType/SpecialIngredient) | challenge captures per type |
| ECON-035 | `Economy/ParkObjectives.cs:153` | offers wait for accept/decline; follow-ups are offered right after completion; failed challenges count as finished | challenge flow captures |
| ECON-036 | `Economy/ParkObjectives.cs:149` | build challenges with TargetVal 0 need one item; type 28 needs level 3 | challenge captures |
| ECON-037 | `Economy/ParkObjectives.cs:107` | staff skill % = grade x 25 + training points / 4 | staff skill display capture |
| ECON-038 | `Economy/ParkObjectives.cs:281` | big park uses MinCellsOwned, cameras use MinCellsCovered | golden ticket award captures |
| ECON-039 | `Economy/ParkObjectives.cs:262` | profit year = profit of the last 12 closed months | golden ticket award capture |
| ECON-040 | `Economy/ParkObjectives.cs:318` | players start with 1 golden key and keys are not consumed by entering themes | initial lobby and repeated theme-entry captures |
| ECON-041 | `Economy/EconomyObjectCatalog.cs:93` | features-directory objects with Research.Category != 3 are fixed (non-buyable) items | buy-menu capture |
| ECON-042 | `Economy/EconomyObjectCatalog.cs:111` | sideshow InitCostOfGoods is the cost of a prize paid per win | sideshow panel capture |
| ECON-043 | `Economy/BalanceSettings.cs:173` | monthly wage = BaseWage[grade] x PayMultiplier[type] | staff list capture with grades |
| ECON-044 | `Economy/GuestEconomyBridge.cs:60` | balloon/costume percentages are 0 (guests carry no items yet) | guests slice item state |
| ECON-045 | `Files/Formats/Save/SaveEconomyRecords.cs:91` | loan/challenge record locators use plausibility bounds (one fixture) | a second TPWS/TPWI fixture |
| ECON-046 | `Economy/ParkEconomy.cs:522` | upgrades need at least one employed mechanic to be bought | capture (TAG_SYSTEM 151 suggests it) |

## Open questions

- Day length, month lengths and the calendar start; original game speeds.
- Repayment formula with interest; whether loans can be retaken; whether building is allowed in the red.
- `BaseCostPerStaff`/`CostPerQualityLevel`, `Research.StartingWorkLoad`, researcher `WorkDuration`.
- Park rating, park value and the scrap basis; how golden keys are earned.
- The money block before the loan table (87,987 / 87,787 / −12,013), the extra words around the
  challenge table (`09 00 0b 00`, floats 100.0) and the last loan-record word.
