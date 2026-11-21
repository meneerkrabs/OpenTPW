# Theme Park Inc staff vs Theme Park World staff: static trace

Executables: the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`, file offset = VA − 0x400000) and
Theme Park World's `tp.exe` (whose `.data` is not at VA − 0x400000, e.g. file 0x35CE78 = VA 0x75EA78). Static reading only; neither game was run, and no listing
or byte dump is included here. Balance key addresses follow the settings layout in [TPI-CHALLENGE-SYSTEM.md](TPI-CHALLENGE-SYSTEM.md#2-where-the-settings-land).

Conventions. "member" = a staff thing; TPI member offsets are relative to the thing. `X ≙ Y` = TPI `X` is the counterpart of TPW `Y`
(same debug strings / same structure). "lerp(a→b)" = `0x62C900`: for `x` in `[lo,hi]`, `t=(x−lo)<<14 / (hi−lo)` (signed idiv), result
`((0x4000−t)·a + t·b)` divided by 16384 rounding toward zero (`cltd; and 0x3fff; add; sar 14`), `x>hi → b`, `x<lo → a`.
"MaxExp" = `AllStaffConstants.MaxExperience` (`0xA02260`). "unread" = not read in this pass; no guesses.

---

## 1. Identity: roles, kinds, lists, states

| | TPW | TPI | Evidence |
|---|---|---|---|
| role enum (pool, wages, strike) | 0 handyman, 1 mechanic, 2 entertainer, 3 guard, 4 researcher | same 0–4 plus **5 gardener** | pool switch `0x631C38..0x631D86` compares role 0–5; candidate jump table `0x6306B4` has 6 cases; error string "Trying to generate a non-existent staff type" `0x630635` for > 5; `STAFF_TYPES.str` (TPI) = Cleaner, Engineer, Entertainer, Guard, Scientist, Gardener |
| thing kind | 4 mechanic, 5 handyman, 6 entertainer, 7 guard, 8 researcher | same, plus **0x14 gardener** | load dispatch cases (ThemeParkIncBodyLayout.Data.cs comments: 0x5d9e40/0x5d4ba0/0x5d1600/0x5d3500/0x61b3c0/0x5d2510); kind→role byte table `0x62D070` = `[0,1,2,3,4,…,5]` for kinds 4..0x14 (used by wage check `0x62CDC0`); HQ message 0x21 filter `0x63253F..0x632553` tests kinds 4,5,6,8,7,0x14 |
| **kind 0x14** | — | **gardener** (`GARD`), waters plants (`mPlantToWater`, `mTimeStartedWatering`, load `0x5D2510`); "Gardener found dry plant" `0x5D291E`, "Too tired to water plants" `0x5D28DC` | |
| per-role list head (world word) | `+0x1DA73C` handy, `+0x1DA73E` mech, `+0x1DA740` ent, `+0x1DA742` res, `+0x1DA744` guard (`0x508ED8..0x508F04`) | `+0x1EAB7C` handy, `+0x1EAB7E` gardener, `+0x1EAB80` mech, `+0x1EAB82` ent, `+0x1EAB84` researcher, `+0x1EAB86` guard | role jump tables `0x632CD0` (consideration) and `0x633FC0` (strike test); member link `+0x5D0` (`0x6337AA`) vs TPW `+0x210` (`0x50942F`) |
| member state word | `+0x19C` | `+0x1B4` (saved state `+0x1B8`) | `0x62B010`, `0x5DA283` |
| display states | 6: Idle, Patrolling, Working, Resting, On strike, Picked up (TPW `STAFFSTATES.str`); map `0x5064F0`: 0→0, 1→1, 2/3→3, 4/5→4, 7→5, else 2 | 9: Idle, **Looking for work**, Working, Resting, On strike, Picked up, (blank), **Researching**, **Training** (TPI `STAFFSTATES.str`); map `0x62B010` (table `0x62B09C`): 0→0, 1→1, 2/3→3, 4/5→4, 6→2, 7/8→8, 9→2, 10→5, 11/12→6, 24/25/33/34→7 for a researcher with `+0x274≠0` else 0, 37/38→8, others→2; state 11 shows the saved state | |
| role state numbers | mechanic walk 12 / repair 13 (OpenTPW ParkStaff) set by `0x4DA370(0xC)` `0x4DA6D9` | mechanic walk **21**, repair **22**: dispatcher `0x5DA280` table `0x5DA358` (0 idle `0x5DA309`, 1 `0x5DA2A1`, 2 `0x629EF0`, 3 rest `0x62A1C0`, 21 `0x5DA650`, 22 `0x5DA7C0`, rest → common `0x6287A0`); search sets 21 at `0x5DA519` | other roles' numbers: unread |

## 2. Settings keys and where they land

TPI `Standard.sam` drops every grade-indexed TPW staff key (`PerGradeStaffConsts[g].*`, `<Role>ConstsPerGrade[g].WorkDuration/
PoundsPerTrainingPoint/DetectionRange/ActivationDistance/ResearchAbility`; see `tpw_keys.txt` vs `tpi_keys.txt`) and adds
experience/wage/agency/strike keys. Grades are gone: no grade member exists in the TPI staff save fragment (staff load `0x6267C0`)
and no absolute reference to any `[g≥1]` element of the remaining per-grade arrays was found (`xrs.py tpi a02340 a0249c`).

| Key (TPI address) | Read at | Use |
|---|---|---|
| `StaffPoolInfo.TimeBetweenStaffUpdates` 0xA0221C, `MaxNumberOfStaffPerUpdate` 0xA02220, `StaffTimeoutTime` 0xA02224 | `0x631B97`, `0x631BD5`, `0x63069B` | as TPW (`0x508539`, `0x508575`, `0x5077B9`) |
| `Max<Role>` 0xA021CC..DC | `0x631C12..0x631D92` | pool shortfall; **gardener uses `MaxHandymen`** (`0x631C12` loads 0xA021CC into esi, role 5 block `0x631C4A` and role 0 block `0x631C89` both subtract from it); `MaxGardeners` 0xA021E0 has no reference |
| `Min<Role>InPool` 0xA021E4..F4 | `0x631690` | **gardener uses `MinHandymenInPool`** (`0x63176A` then role 5 at `0x631792`); `MinGardenersInPool` 0xA021F8 no reference; researcher minimum goes through `0x631970` (`0x631848`, unread) |
| `Max<Role>InPark` 0xA021FC..0xA02210 | `0x631490` (all six, gardener 0xA02210 at `0x631551`), UI `0x720890..0x724E40`, researcher also `0x5B3795` | park cap |
| `BeginningNumberOf<Role>` 0xA021B8..C8 | opening pool `0x631F30` | **gardener uses `BeginningNumberOfHandymen`** (`0x631FCE`, role 5 at `0x631FFE`) |
| `AvgExperienceOf<Role>` 0xA021A4..B4, `AvgWageDifference` 0xA0222C | candidate `0x6302C0` | gardener uses `AvgExperienceOfHandymen` (case role 5 `0x630390` reads 0xA021A4) |
| `ChanceToGetGreat*`, `AvgGradeOf*`, `BaseCostPerStaff`, `CostPerQualityLevel`, `MinExperience` (0xA0217C..A0, 0xA02214, 0xA02218, 0xA02228) | — | no absolute reference found |
| `AllStaffConstants.*` 0xA02254..0xA022B8 | RestLevel `0x62B3BB`; HappyHitCosNoRestArea `0x62BD52`,`0x62CBDE`; HappyHitCosRoomInaccessible `0x62A057`,`0x62A8E1`,`0x62DD12`; MaxExperience (many); UnHappyWageVal/HappyWageVal/LowestWageHappiness `0x62CDA0`; Min/Max(Happiness)RecuperationRate `0x62A1C0`; MinWage `0x62D160`, Min/MaxWage/WageStep UI `0x58A650`,`0x590650`,`0x59ACA0`; PercentageForBadWageDecision `0x559FA9`; MotivationFactor `0x62E4E4`; StaffSlackingEnergy `0x62D662`; MinDistanceForSlacking `0x62D71E`; StaffRoomCapacity `0x58D15B`,`0x629F42`,`0x62CAD3`,`0x724445`; MaxStaffPerStaffRoom `0x7248D1`; MaxSpeedDifference `0x62C974` and 3 more; TrainingExtraExperience `0x62AAEA`; MinExperienceForTraining `0x629BBB`; SkillIncrement `0x62AA50`; ProgessUpperLimit `0x62AA6A`,`0x593680`,`0x62B854` | see §§4–8; `MinStaffPerStaffRoom` 0xA022A4 no reference |
| `Strike.*` 0xA022BC..CC | MinMonthsForStrike `0x633117`,`0x628E3F`; MinStaffOfTypeForStrike / Energy / Happy / Motivation `0x633740`; Motivation also `0x62D873` | §7 |
| `<Role>.Min/MaxWorkDuration`, `Min/MaxDetectionRange`, `Min/MaxActiveRange`, `Min/MaxResearchAbility`, `MaxWage` 0xA022D0..0xA0233C | §5, §6 | `Researcher.Min/MaxWorkDuration` 0xA02320/24 no reference |
| `PerTypeStaffConsts[t].PayMultiplier` 0xA02340 | — | no reference (TPW reads it at `0x504C9A`) |
| `<Role>ConstsPerGrade[0].ExperiencePerMonth` (0xA0235C ent, 0xA02384 handy, 0xA0239C mech, 0xA023B8 res, 0xA023F4 guard) | `0x629650` only the [0] elements | §5 |
| `EntertainerConstsPerGrade[g].HappinessEffectOnCell` 0xA02358, `ResearcherConstsPerGrade[g].NoRRoomHappyHit` 0xA023BC, `.WorkDuration` 0xA023B4 | — | no reference |
| `StaffAgencies[a].*` 0xA0240C..0xA0249C (36-byte rows) | RequiredPromotionLevel `0x630260`,`0x6302C0`; Motivation Gain/Loss `0x62CF78`/`0x62CE96`; (Happiness)Recuperation `0x62A1EC`/`0x62A241`; InitialWage `0x62CE88`,`0x630648`; InitialExperience `0x6301C4`,`0x630343`..; ExperienceGain `0x62970D`; TrainingExperienceGain `0x62AAFD` | new mechanism |
| `FixedItemInfo.StrikeArea*` 0xA02600..0C | `0x62BBA0` | as TPW `0x506A38` (0x7855EC..F8) |
| `PeepInfo.Prank*`, `WhingeToStaffArea` 0xA01AFC..0xA01B14 | guest code `0x60C3F0`, `0x614E4E`, `0x616100`, `0x6161C0` | guest side, unread |
| `ParkAward.Training/TrainingMinStaff` 0xA028D4/D8 | `0x606D0F`, `0x606D1F` | award, uses staff count `0x5B2F40` |
| `LitterBin.Capacity/Dirty/FireRisk` 0xA02990..98 | `0x5279E0`, `0x527BD6`, `0x5DB1B0` | new bins, unread |

## 3. Hiring pool and candidates

**Pool update `0x631B40` ≙ `0x5084F0`** (called each world turn: TPI `0x5D65F0` in `0x5D65E0` from world update `0x649868`; TPW
`0x4D7B30` in `0x4D7B20`). Same skeleton: expiry loop over 32 candidates, refill when turns since stamp > 4 × TimeBetweenStaffUpdates
(stamp `pool+0x418` vs TPW `+0x294`, elapsed `0x448E10` ≙ `0x41A970`), per-update cap min(MaxNumberOfStaffPerUpdate, free slots),
per-role shortfall `Max<Role> − pool count` unless the role flag is set (flags `pool+0x41D..+0x422` vs `+0x299..+0x29D`), draw
`world RNG % total shortfall` picks the role (`0x631DC6`..`0x631DEA` ≙ `0x508722`..`0x50874C`), new candidate, then MaxInPark
check before (`0x631490` ≙ `0x508000`) and the minimum top-up after (`0x631690` ≙ `0x508170`). Differences:
- candidate record **0x20 bytes** (TPW 0x14): `+0` role, `+4` name index, `+8` wage, `+0xC` agency, `+0x10` sprite byte, `+0x11` valid,
  `+0x12` being placed, `+0x14` stamp, `+0x18` timeout, `+0x1C` experience (`0x6302C0`, `0x631B4B..0x631B89`).
- **6 roles** (loop bound 6 at `0x631DA3`, `0x631DE1`), gardener shares the handyman maxima (§2).
- expiry has an **extra condition**: the candidate is kept when its index equals the value `0x540E70` returns for the object at
  `0xA5EA48` (`0x631B71`..`0x631B7D`; `0x540E70` reads `+0x14 → +0x158` and calls `0x85685E`; meaning of 0xA5EA48 unread).

**Candidate `0x6302C0(role, slot)` ≙ `0x507600`.** TPW draws a grade `AvgGradeOf + R%3 − 1` after a discarded world draw
(`0x50762D..0x50764E`). TPI instead:
1. agency `+0xC = rand() % n` where `n` = number of leading `StaffAgencies` rows with `RequiredPromotionLevel ≤ [0x42A3B0()+0x1060]`
   (`0x6302CB..0x6302F8`). `rand` is the **C runtime LCG `0x870D38`** (×214013+2531011), not the world generator `0x6493E0`.
2. sprite byte `+0x10 = 0x6902F0(cat)` with category 9 handyman, 0xA mechanic, 8 entertainer, 0xB guard, 0xC researcher, 0x12 gardener
   (`0x630308`, `0x630418`, `0x6304A0`, `0x630528`, `0x6305B0`, `0x630390`).
3. experience `+0x1C = AvgExperienceOf<Role> + (R % AvgWageDifference − AvgWageDifference/2)` (world RNG; `/2` = `cltd; sub; sar`),
   then `+= exp × InitialExperienceModifier[agency] / 100` (magic `0x51EB851F`, `sar 5`) when both nonzero, capped at MaxExp
   (`0x630318..0x630375`, duplicated in `0x630190`).
4. wage `+8 = ExpectedWage(exp, <Role>.MaxWage)` (`0x62CD30`), then `+= wage × InitialWageModifier[agency] / 100` (`0x630642..0x63066B`).
   `ExpectedWage(e, M)`: `e > MaxExp → M`; `e < 0 → 0`; else `w = ((e<<14)/MaxExp × M) / 16384` (toward zero), `w −= w % 20`,
   result `max(10, w)` (`0x62CD30..0x62CD7F`).
5. name `0x6300F0` ≙ `0x507580`: world `R % 35`, redrawn while `0x631A30` finds it taken, at most 15 draws (identical shape).
6. valid `+0x11=1`, stamp, timeout `StaffTimeoutTime + R % (StaffTimeoutTime>>1)` (`0x630690..0x6306AC`) as TPW.

Opening pool `0x631F30` ≙ `0x5087C9` ("Tried to generate the opening staff pool more than once" `0x6320F2` / `0x508960`).
Hire: member constructors `0x626190` ("specific") copy salary → `+0x214`, experience → `+0x210`, agency → `+0x244` (`0x6261DA..0x626205`);
both constructors end in `0x626390`, which sets the three meters to 100.0 (`0x6263D6..0x6263E7`) as TPW. No hire fee: the cost keys are
unreferenced (§2), as in TPW. Hire UI / placement path: unread.

## 4. Wages and payroll

| | TPW | TPI |
|---|---|---|
| amount | `PayMultiplier[role] × BaseWage[grade]` (`0x504C9A`, `0x504CA4`) | the member's own **`mSalary` `+0x214`** (`0x6265DE`) |
| when | staff message type 0x0C in `0x504C70` | message type 0x0C in `0x6265C0`; 0x0C is sent by the clock when the month changes (`0x60852C..0x608540` → `0x435900`, `0x435920` returns 0xC); 0x0B on a day change (`0x608484..0x60849F` → `0x435890`) |
| bank | `0x5195D0` → `0x4D01F0`, accumulated at analyser `+0x1F7F0` | `0x64EBE0(salary)` → `0x5C07B0`, accumulated at park `+0x1FA58` (`0x6265EB..0x626617`) |
| after paying | — | **wage review `0x62CDA0`** |

**Wage review `0x62CDA0`** (also after a salary change `0x62D160`, which clamps the new salary to ≥ `MinWage`): expected =
`ExpectedWage(exp, MaxWage[role])` inline (`0x62CE35..0x62CE7A`) **plus the raw `InitialWageModifier[agency]`** (added, not a percentage,
`0x62CE88`). If salary < expected: `x = UnHappyWageVal × (MotivationLossModifier+1)` (or `UnHappyWageVal` when either is 0);
motivation `+0x234 += −(x/2)` (toward zero), happiness `+0x230 += x × −0.25f`, both clamped 0..100; shortfall `+0x270 = expected − salary`.
Else `+0x270 = +0x26C = 0`; `y = HappyWageVal × (MotivationGainModifier+1)`; `+0x234 += y`, `+0x230 += y`. Then if `+0x270 ≠ 0` and
`trunc(+0x234)&0xFF ≤ LowestWageHappiness`: wage-demand day `+0x26C = rand() % 27 + 1` (CRT `0x870D38`, `0x62D016..0x62D046`).

**Daily routine `0x628CA0`** (message 0x0B, `0x626626`): experience gain `0x629650` (§5); then when `+0x26C > 0`, at least 30 days since
the last demand (`0xAA07F4`, `0x628CFE..0x628D0E`) and today's day-of-month equals `+0x26C`: an advisor/UI notice (`0x628D61..0x628DAF`) and
a wage-demand object `0x5CEFB0(t, member, shortfall +0x270)` with `t = 0xA` when the role has a grievance (`0x633740`) and the park is younger
than `MinMonthsForStrike` months, else `t = 0xB` (`0x628E0A..0x628E9A`). Negotiation `0x559E00`: offer ≥ demand → result 2; else
offer/demand × 100 ≥ 100 − PercentageForBadWageDecision → result 1; else 0 (`0x559F74..0x559FF2`). Rest of the negotiation and
`0x62D1A0`: unread.

## 5. Experience, work durations, ranges, speed, idle

TPW keys every one of these by grade; TPI by experience:
- **Experience gain `0x629650`** (daily, from `0x628CBD`): `f = (working +0x280 + researching +0x294) / (sum of the seven tick counters
  +0x280..+0x298)` (x87), gain `= trunc(ExperiencePerMonth[role][0] × f)` (kind table `0x629774`: mech 0xA0239C, handy 0xA02384, ent 0xA0235C,
  guard 0xA023F4, researcher 0xA023B8, **gardener → handyman's 0xA02384**); if `ExperienceGainModifier[agency]` and gain are nonzero,
  `gain = trunc(mod × f + gain)`; `exp += gain`, capped at `MaxExp − 1` (`0x629731..0x62974C`). Tick counters are incremented per
  display state in `0x628050` (`0x6280A2..`); counter names from the save order (staff load `0x6267C0`).
- **Work duration `0x62E4C0`**: `e = exp − (100 − trunc(motivation +0x234)) × MotivationFactor`, at least 1; pair by kind (table `0x62E64C`):
  mechanic `Mechanic.Min/MaxWorkDuration` (inverse form), handyman `Handyman.*` (inverse), entertainer `Entertainer.*`, guard `Guard.*`,
  **researcher `Researcher.Min/MaxResearchAbility`**, gardener `Gardener.*`. Normal form = lerp(Min→Max) over `[0,MaxExp]`; inverse form =
  `Min − trunc(t·(Min−Max)/16384)`. Salary < 10: normal returns 1, inverse returns 200 (`0x62E574..0x62E5DC`). Called by every role (17 sites).
- **Search ranges**: handyman `lerp(Handyman.Min→MaxDetectionRange)` (`0x5D507C`); guard `Guard.*DetectionRange` (`0x5D3230`,`0x5D3390`,
  `0x5D3B40`,`0x5D4790`); gardener `Gardener.*DetectionRange` (`0x5D2EC7`); entertainer placement `lerp(Min→MaxActiveRange)` (`0x5D1286`),
  but **the entertainer's guest search passes `MaxActiveRange` as both ends** (`0x5D1A9D..0x5D1AB4`), so its range is constant.
- **Search period**: handyman searches only when idle and `turn % trunc((1 − exp/MaxExp) × 50) == 0` (or the period is 0)
  (`0x5D500D..0x5D5063`). Entertainer gate: world `R % 3 == 0` (`0x5D1AC4..0x5D1AD4`). Idle end `0x62CA10`: `turn > +0x23C +
  trunc((1 − exp/MaxExp) × −50)` (constant −50.0 at `0x93E878`), then `0x62D810`. TPW: `IdleDuration[grade]` (`0x4DA540` etc.).
- **Speed `0x62C970(mode)`**: `+0xD4 = lerp(base→base+MaxSpeedDifference)` with `base = u16 table 0x9CFB28[mode]`. TPW: grade-based
  (OpenTPW `0x504B90`).

## 6. Meters, rest, slacking, training

Member meters (single floats, start 100.0 `0x6263D6`): **`+0x230` happiness, `+0x234` motivation, `+0x238` energy.** Proof: the strike
test compares `+0x238` with `EnergyPercentToStrike` ("fatigue"), `+0x230` with `HappyPercentToStrike` ("unhappiness") and `+0x234`
with `MotivationPercentToStrike` ("lack of decent wages") (`0x63379F`/`0x6337D0`, `0x63381D`/`0x63384E`, `0x63389F`→`0x633F8D`). docs/THEME-PARK-INC.md and `ThemeParkIncStaffMeters` used to read `+0x234` as happiness. Challenge `0x5AFED0` (min of `+0x230`) is therefore min happiness.

| step | TPW | TPI |
|---|---|---|
| moving | `0x5066A0`: energy `−= 0.012·(6−g)` (double), happiness `−= 0.005·(6−g)` | `0x62B3E0`: `d = 1 − exp/MaxExp`; energy `+= d × −0.1f`; happiness `+= d × −0.05f`; clamped |
| working | `0x506760`: energy `−= 0.025f·(6−g)`, happiness `0.01` | `0x62B4D0`: identical to `0x62B3E0` |
| rest | `0x5061D0`: rates `0x785334/38 + 16g` | `0x62A1C0`: `f = exp/MaxExp`; `e = MinRecuperationRate + f` (x87 add, no Max−Min factor); `+= e×RecuperationModifier×0.01` if nonzero; `min(e, MaxRecuperationRate)`; `h = f × 10 × MinHappinessRecuperationRate`, `+= h×HappinessRecuperationModifier×0.01`, `min(h, MaxHappinessRecuperationRate)`; energy += e, **happiness and motivation += h**; at `trunc(energy)&0xFF == 100` end rest (`0x62C230`) and look for work (`0x62D810`) |
| tired test | `0x506680` | `0x62B3B0`: `trunc(energy)&0xFF < RestLevel` |

Look-for-work `0x62D810` dispatches by kind (entertainer `0x5D1A30`, handyman `0x5D52A0`, gardener `0x5D28C0`, guard `0x5D3C10`, mechanic
`0x5DA3D0`, researcher `0x61B820`), then if `trunc(motivation) < MotivationPercentToStrike/2` and display state is 2 or 7 it goes to the
nearest rest area (state 2) or idles (`0x62D866..0x62D94D`). Slacking `0x62D650`: energy byte < `StaffSlackingEnergy` and the nearest rest
area's `dx²+dy²` (cell units) ≤ `MinDistanceForSlacking` (unsquared key) → that rest area (`+0x248`). Rest areas have a capacity
(`StaffRoomCapacity`, `0x629EF0`, `0x62CA90`); research rooms (`0x62BD8D`, `0x62E050`, `0x62E0E0`, "Looking for Research Room") and the
room-inaccessible happiness hit: partly read only.

**Training is a different mechanism.** TPW `0x505A10` (CStaff::Train) spends a monthly budget per role in points (OpenTPW). TPI:
dropping a member on a training cell (`0x629B20`: `0x639BE0` finds it) with `exp ≥ MinExperienceForTraining` and `0x639B20` accepting
(ability activated `0x5F0370`, compatible `0x62B6C0`, exp ≥ 10000 hard-coded `0x639B77`) sets `+0x22C = 2` and state 7 (`0x629BC4..0x629BE0`).
Each training tick `0x62AA50`: `mSAProgress +0x224 += SkillIncrement`; at `≥ ProgessUpperLimit`: the learnt ability `+0x218 = +0x220`,
progress and current reset, notice 0x175, `0x62AB90`, **motivation = 100.0**, `exp += TrainingExtraExperience`, then `+= exp ×
TrainingExperienceGainModifier/100`, cap MaxExp (`0x62AA50..0x62AB3D`). Abilities: `ABILITIES.str` (15 entries: Ride Upgrading,
Construction, …, First Aid, …); HQ counts them in `mTrainedStaff[15]` (message 0x2B, `0x63256F..0x6325AA`). The HQ budget object
(`+0x140`, ctor `0x6394A0`, serializer `0x6394D0`: six budgets, `mHaveEverTrained`) has no reader found in this pass.

## 7. Strikes

| | TPW | TPI |
|---|---|---|
| trigger | HQ message 0x0C → `0x508E70` | HQ message 0x0C → `0x632C20` (handler `0x6323C0`, table `0x6325E4`) |
| park closed / guests | skipped while closed or `0x4C7FA0` counts guests, unless forced (`0x508E7E..0x508EAE`) | **no such test** (`0x632C20` starts with the role loop) |
| per role, once per turn | `+0x24+12r` level / `+0x28` active / `+0x2C` turn; active → just cleared (`0x508F31`) | `+0x15C+12r` / `+0x160` / `+0x164`; active → `0x6342B0(r,0)`, which clears it **and posts message 0x17 kind 5** (`0x6342CF..0x634349`) |
| age gate | months ≥ **24** (`cmp $0x18` `0x508FD6`) | months ≥ **`Strike.MinMonthsForStrike`** (`0x633117`); same clock function `0x608C70` ≙ `0x4F8800` |
| grievance test | `0x509360`: forced → 1; handymen first: analyser ratio > 0.2 → 1 (`0x5093C2`); per role `count > 3` and `avg energy byte < 15`, or happiness the same → 1 | `0x633740`: forced (`+0x1A8`) → 1; **no analyser test**; per role `count ≥ MinStaffOfTypeForStrike` and avg energy byte `≤ EnergyPercentToStrike` → **4**, avg happiness `≤ HappyPercentToStrike` → **2**, avg motivation `≤ MotivationPercentToStrike` → **3** |
| escalation | 0→1 msg kind 0; 1→2, 2→3 kind 2; 3→4, 4→4 kind 2 (`0x509028..0x50921D`); kind 3 message at `0x5092A3` (no-grievance branch per OpenTPW; branch condition unread here) | 0→1 warning msg (`+0xC`=2, `+0x10`=grievance) `0x63316B`; 1→2, 2→3, 3→4 active, msg (2, 0) `0x6331F8`, `0x6332C9`, `0x63339A`; **4→2** active, no strike message (`0x63344A`); no grievance: level ≥1 → msg (3, 0) and `0x6342B0(r,1)` (`0x63349B..0x633524`) |
| message 0x17 | `0x40F6F0`: `+8` role, `+0xC` kind | `0x435CA0`: plus `+0x10` reason |

Strike walk `0x62BBA0` ≙ `0x506A38` (same four world draws into `StrikeArea`, same strings). Force strike toggles: `0x634530..0x634558`.
Receivers of message 0x17 and the advisor cues `0x636B90`: unread.

## 8. Roles

| role | TPW (OpenTPW cites) | TPI counterpart | Differences read |
|---|---|---|---|
| mechanic search | `0x4DA5B0` | `0x5DA3D0` | target `+0x5D8` (TPW `+0x218`); extra check `0x62C6B0(ride)` before "found a ride" (`0x5DA447`); notice text 0x43 vs 0x46; extra branch `0x5E3400(1)` on the ride after `0x5E3950/0x5E3B70/0x5E28A0` (`0x5DA4EF..0x5DA514`); state 21 vs 12. Arrival `0x5DA650` ≙ `0x4DA732`, fix anim `0x5DA7C0` ≙ `0x4DA824`. Ride wear/breakdown rules (attraction side): unread |
| handyman | `0x4D73C0` family | `0x5D4BA0..0x5D6230` | adds **litter bins** ("dirty or burning bin" `0x5D553B`, `mBinToClean`); litter/toilet strings match (`0x5D5502` ≙ `0x4D730F`, `0x5D5B89` ≙ `0x4D7743`, `0x5D59FC` ≙ `0x4D75B1`); range/period §5. Littering `0x5D8C80` ≙ `0x4D93A5`, removal `0x5D90D0` ≙ `0x4D961B` |
| entertainer | `0x4D4810` | `0x5D1A30` search, `0x5D1BC0` performance | performance length from `0x62E4C0` (TPW `0x785398+16g`, `0x4D484A`); constant search range (§5); new "Ouch! You little git!" `0x5D18DC`. `HappinessEffectOnCell` unreferenced in TPI |
| guard / pranks | `0x4D6410`; HQ prank msg 0x0E `0x508A30` | HQ msg 0x0E `0x6323F9` (same strings and security-level test), nearest guard `0x6343C0` ≙ `0x5099C0`, chase `0x5D3960`.. | new first aid (`mPatientId`, `mFirstAidTimestamp`), prosecution stamp, two **release points** ("can't get to either release point" `0x5D42CC`) instead of TPW's ticket-booth keys (absent in TPI data); "Too tired to chase kids" exists only in TPW (`0x4D627F`) |
| researcher | `0x5025F3`.. | `0x61AEC0`..`0x61B94F`, `0x62DF50` | research ability via `0x62E4C0` (§5); research rooms; lab body differs (§9) |
| gardener | — | `0x5D2480..0x5D2F00` | new; pool keys borrowed from handymen (§2) |

## 9. Staff HQ and save layout

HQ ctor `0x632270` ≙ `0x5089C0`: TPI 0x1AC-byte body (`+0x14` 20 staff slots × 12, `+0x104` 15 trained-staff counts, `+0x140` training
budget, `+0x15C` six strike records, `+0x1A4` picked-up id, `+0x1A8` force strike); subscribes to 0x0C, 0x0E, **0x2A, 0x2B, 0x21, 0x23,
0x24** (`0x6322C8..0x632333`; TPW only 0x0C, 0x0E at `0x5089F7..0x508A17`). 0x21/0x2A → `0x634730` (staff slots), 0x23/0x24 → `0x634D20`:
unread. Member/candidate/HQ save layouts are already parsed by OpenTPW (`ThemeParkIncBodyLayout.Data.cs`); the only correction is the
meter naming (§6). TPW member offsets (happiness `+0x1F8`, energy `+0x1FC`, grade `+0x1E4`) have no TPI equivalent for grade.

---

## 10. Port plan (prioritized)

**P0 – data and identity (prerequisite).** A `ThemeParkIncStaffSettings` reading exactly the keys in §2 (no grade arrays); a sixth role
`Gardener` (kind 0x14, role 5, list `+0x1EAB7E`); reproduce the handyman-key borrowing for gardeners (Max/MinInPool/BeginningNumber/
AvgExperience/ExperiencePerMonth) and the constant entertainer search range as read. Fix the TPI meter naming in docs (§6).

**P1 – reuse the TPW port unchanged (structure identical, only offsets/sizes differ):** pool update skeleton and timing (`0x631B40`
≙ `0x5084F0`), candidate timeout and name draw (`0x6300F0` ≙ `0x507580`), MaxInPark/minimum top-up shape, strike-area walk (`0x62BBA0`
≙ `0x506A38`), strike month-consideration loop shape and the clock age (`0x608C70` ≙ `0x4F8800`), prank → nearest guard dispatch
(`0x6323F9` ≙ `0x508A30`), mechanic HQ search skeleton (`0x5DA3D0` ≙ `0x4DA5B0`), rest-area search and rest end at energy byte 100,
patrol rectangle, littering/removal (`0x5D8C80`/`0x5D90D0` ≙ `0x4D93A5`/`0x4D961B`).

**P2 – TPI-specific replacements (exact rules above):**
1. Candidate generation `0x6302C0` (experience, agency by CRT `rand()` `0x870D38`, expected wage `0x62CD30`, modifiers) and the
   pool's extra expiry condition (`0x631B71`). A second RNG stream (CRT) must be modelled for determinism (`0x6302ED`, `0x62D038`).
2. Payroll: monthly debit of `mSalary` (`0x6265DE`), wage review `0x62CDA0`, salary setter `0x62D160`.
3. Daily routine `0x628CA0`: experience gain `0x629650`, wage demands, negotiation `0x559E00`.
4. Meter steps `0x62B3E0`/`0x62B4D0`/`0x62A1C0`, slacking `0x62D650`, low-motivation rest `0x62D866`.
5. Experience-driven work durations `0x62E4C0`, ranges and periods (§5), speed `0x62C970`, idle `0x62CA10`.
6. Strikes: `0x633740` (3 grievances, data thresholds, no analyser test), `0x6330B0` escalation (4→2, reason field), end message `0x6342B0`,
   no park-closed skip, `MinMonthsForStrike`.
7. Training by rooms and abilities (`0x629B20`, `0x639B20`, `0x62AA50`), replacing the TPW budget split.
8. Display states 0–8 and role state renumbering (mechanic 21/22; others unread).

**P3 – unread / still open:** other roles' state numbers and handlers in detail; ride wear and breakdown rules on the attraction side;
`0x62C6B0` and `0x5E3400` in the mechanic search; litter-bin logic (`0x5DB1B0`, `0x5279E0`); gardener plant watering; guard first aid,
prosecution and release points; research rooms and the researcher's lab work (`0x62DF50`, `0x61B820`); staff rooms/slots (`0x634730`,
`0x634D20`, `0x629EF0`, `0x62CA90`); HQ training budget readers; `0x62D1A0`; wage-demand object `0x5CEFB0` and the rest of `0x559E00`;
message 0x17 receivers; pool role flags `+0x41D..+0x422` and the researcher minimum `0x631970`; hire UI and placement; the meaning of
`0xA5EA48`/`0x540E70`; agency promotion level source `0x42A3B0()+0x1060`.

## 11. Port status

Ported as an economy model, without the world simulation, and checked against the executable:

- Settings and roles (P0): `ThemeParkIncStaffSettings`, `ThemeParkIncStaffRole`, the gardener's borrowed handyman keys.
- Candidates and the pool: `ThemeParkIncStaffCandidates.Generate` (`0x6302C0`, `tpi_staff_candidate_oracle.py`), and `ThemeParkIncStaff` with the opening pool
  `0x631F30`, the update `0x631B40` (expiry, refill draw, MaxInPark flags `0x631490`, minimum top-up `0x631690`), name checks `0x631A30` and hire/placement
  `0x630D10`/`0x6319E0`. The top-up stops after a pass that adds nothing instead of the executable's possible endless loop.
- Members: `ThemeParkIncStaffMember` with the wage review `0x62CDA0`, salary setter `0x62D160`, day counters `0x628050`, experience gain `0x629650`
  with the counter reset `0x62D1A0` (not its history rings), the step `0x62B3E0`/`0x62B4D0`, rest `0x62A1C0` and tired test `0x62B3B0`
  (`tpi_staff_formula_oracle.py`, together with the lerp, expected wage and work duration).
- Payday and the day message: `ThemeParkIncStaff.PayMonth` (`0x6265DE`, salary debit then review) and `EndDay` (`0x628CA0`: experience, then the wage
  demand on its day of the month, at most one per 30 game days for all staff via `0xAA07F4`, type 0xA or 0xB), members in ascending handle order (`0x436E97`).
- Strikes: grievance `0x633740`, the month consideration `0x6330B0` and its escalation.
- The two CRT `rand()` draws (agency and demand day) come from the park's world generator (`[EXT:tpi-staff-crt-rand]`, approved 2026-11-21).

Not ported yet: the role state machines and their walking, working, slacking (`0x62D650`) and low-motivation rest; training; the wage negotiation dialog
(`0x559E00`); hiring and placement in the HUD; and the members' world (Theme Park Inc staff cannot be hired in the slice yet).

Since November 21, 2026 the pool runs in a Theme Park Inc park: the opening pool at the new-world init (`0x630CB0`), the pool update in every world update's tail after the
thing loop and before the calendar (`0x649868`), the career rank 1 for the agencies (`0x74BDC7`), the pool in the Science continuation save, and a read-only Staff panel in the HUD.
The plan for the members' world is in OpenTPW-notes (tpi-staff-world-plan.md): a separate Theme Park Inc staff world, handymen first.

