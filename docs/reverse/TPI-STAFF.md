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
| mechanic search | `0x4DA5B0` | `0x5DA3D0` | target `+0x5D8` (TPW `+0x218`); extra check `0x62C6B0(ride)` before "found a ride" (`0x5DA447`); notice text 0x43 vs 0x46; extra branch `0x5E3400(1)` on the ride after `0x5E3950/0x5E3B70/0x5E28A0` (`0x5DA4EF..0x5DA514`); state 21 vs 12. Arrival `0x5DA650` ≙ `0x4DA732`, fix anim `0x5DA7C0` ≙ `0x4DA824`. Ride wear/breakdown rules (attraction side): see "Mechanics and ride wear" in §11 |
| handyman | `0x4D73C0` family | `0x5D4BA0..0x5D6230` | adds **litter bins** ("dirty or burning bin" `0x5D553B`, `mBinToClean`); litter/toilet strings match (`0x5D5502` ≙ `0x4D730F`, `0x5D5B89` ≙ `0x4D7743`, `0x5D59FC` ≙ `0x4D75B1`); range/period §5. Littering `0x5D8C80` ≙ `0x4D93A5`, removal `0x5D90D0` ≙ `0x4D961B` |
| entertainer | `0x4D4810` | `0x5D1A30` search, `0x5D1BC0` performance | performance length from `0x62E4C0` (TPW `0x785398+16g`, `0x4D484A`); constant search range (§5); new "Ouch! You little git!" `0x5D18DC`. `HappinessEffectOnCell` unreferenced in TPI. Ported (see "Entertainer and guard" in §11) |
| guard / pranks | `0x4D6410`; HQ prank msg 0x0E `0x508A30` | HQ msg 0x0E `0x6323F9` (same strings and security-level test), nearest guard `0x6343C0` ≙ `0x5099C0`, chase `0x5D3960`.. | new first aid (`mPatientId`, `mFirstAidTimestamp`), prosecution stamp, two **release points** ("can't get to either release point" `0x5D42CC`) instead of TPW's ticket-booth keys (absent in TPI data); "Too tired to chase kids" exists only in TPW (`0x4D627F`). Ported without the chase and first aid (see "Entertainer and guard" in §11) |
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

Not ported yet: the guard's chase, carry and first aid (see "Entertainer and guard" below); the wage negotiation dialog (`0x559E00`). Slacking (`0x62D650`) and the low-motivation rest (`0x62D866`) are ported with the rest areas (§13). Training is ported since November 21, 2026 (§12.2).

Since November 21, 2026 the pool runs in a Theme Park Inc park: the opening pool at the new-world init (`0x630CB0`), the pool update in every world update's tail after the
thing loop and before the calendar (`0x649868`), the career rank 1 for the agencies (`0x74BDC7`), the pool in the Science continuation save, and a Staff panel in the HUD.

### Handymen in the park (WP3, November 21, 2026)

A separate Theme Park Inc staff world (`World/Staff/ThemeParkInc/ThemeParkIncStaffWorld.cs`) runs the placed members; Theme Park World's staff code is untouched (the slice
refuses its staff capability, so its staff world is not built in a Theme Park Inc park).

- **Hire and drop.** The staff panel's candidate rows hire: a Cleaner's row starts the hire (`0x630D10`) and puts him in the hand; a park click drops him (`0x4D4EB0`). A
  type-4 cell whose chain holds an object's thing (its own cell) goes through the gate `0x4D1FC0` and the redirect `0x4D2180` by the object's `Info.WhichUIType` (type record `+0x600`,
  copied from `+0x70`): a ride's drop makes him on its exit cell (`+0x48`), a shop's, a sideshow's and kinds 4 and 5 on its entrance cell (`+0x46`), a feature's on the path cell beside its
  footprint nearest the click, whose queue link byte then points from the footprint to it (`0x5E9320`, `0x5E9230`); without such a path cell the drop ends silently and he stays in the
  hand. Every other cell takes the ladder (flag 0x40, fence post, track record, type 0, 1, 3 or 9, as tp.exe's, with no occupancy test; the other footprint cells refuse by their type 4)
  and refuses with the message "Cannot place staff member here". Right-click or Esc cancels (`0x6319E0(1)`). The drop builds the member: its thing takes the free list's head and enters the thing list at its head (`0x637100` → `0x649270`), the
  person's start draws once for a base speed (`0x608FA4`, overwritten), the member's start sets the meters, the patrol rectangle (0, 0, 0x7F, 0x7F) and the base speed
  `lerp(exp, 0, MaxExperience, 100, 100 + MaxSpeedDifference)` (`0x626390`), and the handyman's start (`0x5D4980`) ends in his search.
- **Thing update.** The world pass walks the thing list newest first (`0x6495C1`); a member's update is the handyman's pre-update `0x5D5000` (the common part `0x628050`: the speed
  smoothing and one tick of the display state's day counter by `0x62B010`; then, while idle, a litter scan every `trunc((1 − exp/MaxExp) × 50)` turns) and the state dispatch
  `0x5D5710`.
- **States.** 0 idle (`0x5D5863`: the 1-in-16 voice draw, then the idle end `0x62CA10`: the turn above `+0x23C − trunc((1 − exp/MaxExp) × −50)` looks for work `0x62D810`, which after
  the search sets a member whose motivation byte is below MotivationPercentToStrike / 2 and who now works back to 0); 1 walk (`0x5D5731`: the voice draw, the walk step, arrived or
  stuck searches, walking costs the moving step `0x62B3E0`); 0x0F to litter (`0x5D59D0`: the claim must still be his, arrival on litter snaps to the walk's point and sets 0x10);
  0x10 sweep (`0x5D5AE0`: the working step, the work duration `0x62E4C0`, then the target's litter is removed `0x5D90D0` when his own cell holds litter, and he searches).
  The setters are `0x5D5630` and `0x628180` (0: the idle stamp after a walk, the bonus 0, template 3; 1: template 0xF; 0x0F: the bonus 25, template 0xF; 0x10: the work stamp, template 0x19).
- **Search `0x5D52A0`.** The gate `0x62BBA0` first (see below); tired (`0x62B3B0`) goes to the tail; the litter finder `0x5B1B50` (the box of radius `lerp(exp, 0, MaxExp,
  Min/MaxDetectionRange)`, unclaimed or own claims, nearest by Manhattan distance, the first on a tie) and, inside the patrol rectangle (`0x62C610`), the reseed by the cell word
  and the two sweep-spot draws, a route there claims the litter (`0x5D8FE0`: the member's thing id and the turn) and sets 0x0F; without litter a world draw picks between the loo
  and the bin; the tail patrols (1) or idles (0).
- **Gate `0x62BBA0`.** Read in full for a handyman: the strike branch needs the role's strike flag (no strike runs in a park yet); an energy byte at most RestLevel looks for a rest
  area (`0x62BA20`): one he can reach sends him there (state 2, §13); with none he takes a world draw whose low nibble 0 costs HappyHitCosNoRestArea, and a working member is set to 0; above RestLevel the base speed follows the energy's
  tens through the words at `0x9CFB28`, read past the five-entry table as the executable does (index 7 is 0x4020, index 9 is 0x40C0, the high words of the floats 2.5 and 6.0),
  and a happiness byte above 97 takes a world draw for the mood thought; the gate then answers 0 (`+0x27E` is written only for researchers by `0x62E240`).
- **Economy.** The day message runs each member's daily routine (`0x628CA0`: experience and the wage demand, shown as a note; the negotiation `0x559E00` is WP6; each day message replaces the previous day's demands, which the HUD takes every frame, so a run without the HUD holds at most one day's) and the counter
  reset (`0x62D1A0`); the month message debits each salary and reviews the wage (`0x6265DE`); the members hear both in ascending thing-handle order.
- **Guest litter.** The guests' litter step is Theme Park Inc's (`0x614B71`): the urge byte at least 90, then a world draw whose low two bits must be 0 (else no litter and the urge
  kept), the bin search of radius 3, the type draw `C % 5 + 1` taken before the cell test, and an occupied cell skips both the drop and the urge's reset. The drop (`0x5D8C80`)
  reseeds and draws as Theme Park World's, but its sprite lies 3 to 6 tenths into the cell (`((d & 3) − 2) × 0.1 + 0.5`).
- **Sprites.** The handymen are drawn by Theme Park World's staff renderer from Theme Park Inc's `esprites.wad` (`Generic\Handymen`, `Spr_ha`, found by name). The look is the candidate's
  sprite draw reduced as `0x6902F0` does (`(R >> 2) mod` the loaded banks of category 9). The handyman's start makes the source in template 3 (`0x5D4980` → `0x5D1000`), the setters
  queue 3, 0xF and 0x19 (`0x5D5630`, `0x628180`), the walk step asks for the walk 1 while walking (`0x60B541`; the run 2 is never a handyman's) and the interval `clamp(trunc(62 / v), 0, 250)`
  (`0x5D10F0`), and the pre-update applies them (`0x5D1080`). Theme Park Inc's template table `0x9CDA94` holds tp.exe's programs (`0x0075A418`) under other numbers, opcode for opcode:
  1 walk, 2 run, 3 stand, 0xF the patrol walk (tp.exe 9), 0x19 the sweep (tp.exe 0x11). The sprite pass itself (direction by heading and camera, mirroring, the pull towards the camera)
  and the draw point's interpolation over the turn are Theme Park World's (`[EXT:tpi-twp-guest-rules]`). The sprite source is presentation and is not saved: a restored member gets a
  fresh source in his state's template at his first update. The thoughts (`0x638960`, the `Generic\Thoughts` bank) are not drawn.
- **Saves.** A placed member's thing (state, stamps, target, speed words, patrol rectangle, template and walk) is in the TpiStaff snapshot; the members' cell-chain entry numbers
  are in the continuation save (`TpiStaffChain`); a litter claim by a placed member's thing id is valid.

Labels: the walk step `0x60B440`, the route search behind the staff route `0x62EB10` (SetDest `0x63E3A0`), the patrol destination `0x609F70` and the speed step `0x60BC90` use Theme Park World's walk, wander and speed
smoothing (`[EXT:tpi-twp-guest-rules]`, user-approved 2026-11-21 for the staff's walk; WP0 found D1/D2/D5 differences and read `0x609F70` as a call skeleton only), as do the
guests' bin search, bin walk and bin arrival; the loo finder `0x5D6230` and the bin finder `0x5D5F10` find nothing to clean because Theme Park Inc's toilet wear and the bins' fill
are unported (`[EXT:tpi-no-maintenance-wear]`, reworded: the handymen ship without loo and bin cleaning); every role is hired since the entertainer and the guard (WP11, WP12).

The rest-area finder `0x62BA20` (in the gate, the slack test `0x62D650` on entering state 1 and the low-motivation check of `0x62D810`) tests a thing for the flag `+0x42` bit 2 (and `+0x7C`
nonzero), which the thing builder sets only from the type record's `+0x1B0` (`UsageInfo.ChillsYouOut`, `0x5DB6E2`). Until the facilities were loaded no object of the catalogue set the key and
the finder answered none; the staff rooms of the facilities folders set it, and the rest areas are ported in §13.

Not read or not ported in this package (inert, no stand-in): strikes in the park (WP5: no role's strike flag is set); the wage negotiation (WP6); the drop fallback's scan
of a 180 or 270 degree object covers the rectangle `0x5E9320` computes, which is not the turned footprint (ported as the executable has it); the thoughts and voices' presentation (`0x638960`, `0x60BE90`:
their draws are taken, the voices queued); TPI litter types 9 to 12 (the pending-item drop `0x613D00`, whose gate
is unread); the stinkbomb particle's TPI position; the dead stale-claim sweep `0x5D73A0` (no static caller).

### Mechanics and ride wear (WP9, November 21, 2026)

Read from Game.exe (static, with the routines run in `tools/ppc-analysis/oracle_x86/tpi_ride_wear_oracle.py`, which agrees with the port on 3000 random cases at the 24-bit and
the 53-bit control word; `--golden` prints native wear-amount and wear-tick cases, which `ThemeParkIncMechanicTests` pins as numbers, a clamped repair, a clamped life and a worn
ride among them). Ported in `ParkEconomy.ThemeParkIncRides.cs` and `ThemeParkIncStaffWorld.Mechanics.cs`; tested in `ThemeParkIncMechanicTests` and, for the level's side (the
request's close, the repair tail and the relief object's `VAR_WORN`), `ThemeParkIncRepairTailTests`.

**Type record.** The key table at `0x9A2CC0` (60-byte descriptors: type word and name) lays the type record out four bytes a key from `Id` at `+0x10`, sections adding nothing and a
count word after each array. That puts `MaxCapacity` at `+0x1E4`, `MaxSpeed` at `+0x1F4`, the `Upgrades[n]` records (72 bytes) at `+0x270` (`RedLineCapacity` `+0x290`,
`RedLineSpeed` `+0x2A0`, `WearRate` `+0x2A4`, `DurationOfUpgrade` `+0x2B0`, `RunningCost` `+0x2B4`), `RideBreakDown.Imminent` at `+0x3C8` and `RideBreakDown.WarnPlayer` at `+0x3CC`;
the `AddOn` keys after them land at `+0x3D0..+0x3D8`, where their readers (`0x4D63C0`, `0x594E19`) take them. `Info.IsSpecial` is `+0x5C` and `Info.RunsContinuously` `+0x6C`
(the thing flag `+0x43` bit 1, `0x5DB782`); `UsageInfo.HoldsLitter` `+0x1A8` sets `+0x42` bit 0x40. Both readers of `Info.IsSpecial` (`0x5E26FB`, `0x5E493B`) compare it with 1,
so only the value 1 makes a type special. Every wearing ride of the three worlds has `Imminent` 10 and `WarnPlayer` 5.
The constructor starts the life gauge `+0x58` at `Info.WorkingLife` (`+0x8C`), 100 when it is 0 (`0x5DB53A`); the prebuilt Science park's rides were saved at exactly that.

**Object update `0x5E4790`** (kind 3 in the thing pass `0x6375E0`, before the state dispatch `0x5E4CB0`): state 3 or 4 returns; with the state not 1, the open word `+0x7C` set
and the turn & 7 zero, the wear tick `0x5E25B0` runs, then the breakdown request: a life or repair byte of 0 on an object that is not a relief object and whose `VAR_BREAKSTAT` is 0
loses 5.0 life (clamped) unless its life byte is 0 or its type is special, and writes `VAR_BREAKSTAT` = 1 ("Object %d: requested breakdown"); this is the writer research item U1
was missing. The state dispatch's state 0 (`0x5E5960`) turns the script's `VAR_BROKEN` into state 1, or 4 at a life byte of 0, as Theme Park World's. A relief object below a
repair byte of 25 writes `VAR_WORN` = 1 on every update (`0x5E3990`). Two blocks of `0x5E4790` are not part of this port:
- `0x5E47A1..0x5E4800`: a thing with `+0x42` bit 0x80 (set by the constructor at `0x5DB75B` from the type word `+0x1CC`, `UsageInfo.IsFireworks` in the key table, which also adds the
  RegionFX row 7 at its cell) whose script variable 0 is 1 takes row 7 off its cell (message 7, `0x5D74D0` → `0x5D7560` with the subtract flag) and writes variable 0 = 2. This is
  Theme Park World's fireworks rule `0x004E0B90`, which OpenTPW already runs for every edition: `Level.SyncEconomyObject` calls `ParkEconomy.FireworksFired` for any object whose type
  declares `UsageInfo.IsFireworks`. No type of the three Theme Park Inc worlds declares it, so the shipped data never reaches the block.
- `0x5E49C8..0x5E4A1E`: the word `+0x4C` (the queue head, `mFirstInQ`) clears when its thing is gone or the guest is in none of the queue states 14 to 17 (`0x61AC40`), as Theme Park
  World's `+0x3C` at `0x004E0D51`; OpenTPW's queue is the visitor bridge's list (`RideVisitorBridge`), whose head leaves with its guest. The byte `+0x3E`, when set and the script names a
  track ride, sets bit 0 of the track controller word (`0x475220`), clears bit 0x200000 on every piece of the object's track (`0x485AD0` → `0x48E780`) and clears itself: track-ride
  state, outside the wear and the mechanics (Theme Park Inc's track rides are a later package).

The level wires the ride events itself (`Level.SetupThemeParkIncRideEvents`): a Theme Park Inc park runs no park gate (`[EXT:tpi-park-open]`), so the Theme Park World wiring in
`SetupParkGate` does not run there. The economy's script writes reach the ride's script, a maintenance request closes the ride, and a finished repair runs the closing tail, whose
guard is the reopening guard `ParkGate.MayOpen` over the level's path grid.

**The request's cancel `0x5E3400(0)`** (`0x5E3509`): "has cancelled its request for maintenance"; nothing when `+0x78` is 0; else `+0x78` = 0 and, unless the object is a bin or a plant
(`+0x42` bits 0x40 and 0x1000), the open `0x5E1DC0`, whose guard only logs "Opening non-openable ride": the open word, `VAR_RIDECLOSED` = 0 and `SetState(0)` (`0x5E5160` stores the
state at once, so a broken ride's state goes to 0 until its script's `VAR_BROKEN` turns it into state 1 again); then a claim (`+0x72`) ends: the claimant's common setter `0x628180(0)`,
which keeps his target `+0x5D8`, and `+0x72` = `+0x74` = 0. Its callers are the native ride panel's cancel button `0x5563F0` (it pushes 0; the call `0x5562E0` beside it pushes 1, both
on the panel's object `0xA60384`, through the thunk `0x4089E0`) and the toggle `0x50FCD0` (`sete` on its flag). The text HUD replaces that panel and has no call or cancel control
(`[EXT:tpi-text-hud]`), but the shared `RequestMaintenanceCommand` (the co-op guest's call button, `ParkHud.GuestAttractions`) has no edition gate, so a park with Theme Park Inc's staff
runs it as Theme Park Inc's request and cancel (`ParkEconomy.RequestMaintenance` → `RequestThemeParkIncMaintenance`, `CancelThemeParkIncMaintenance`), and the level opens the ride on
`MaintenanceCancelled` whatever its guard, as `0x5E1DC0` does. The command's call follows the ride panel's button state `0x5511B0` (case 0x13, `0x5511FB`): it is enabled while the
staff HQ (world `+0x1EAB54`) counts a thing of the mechanics' kind 4 (`0x5B2DB0(4)`: things of that kind whose byte `+3` is 0), that is a placed mechanic, and no strike is tested;
the cancel needs no mechanic. (`0x556300`, the enabled test beside the cancel `0x5563F0`, counts kind 20, the gardeners, for its panel.) Two corners are left as they are: in state 2
the original's `SetState(0)` would end an upgrade, which the port refuses because Theme Park Inc's upgrades are not sold; and the store would overwrite a state 3, which no request
reaches.

**Wear tick `0x5E25B0`.** `Upgrades[level].WearRate` 0 returns; the script's `VAR_RUNNING` (variable 9) above 0 or `Info.RunsContinuously`; the turn & 0x3F zero. `VAR_ONRIDE`
(variable 5) 0 gives the wear 0, else `0x5E21A0`: riders clamped to `0..MaxCapacity`; `s = 0.9 × speed / MaxSpeed` (the script's speed `+0xC0`, `0x6B0C30`), then `s + 0.1`, or
`(s + 1.1) × 0.5` at or above `RedLineSpeed`; `c = 0.9 × riders / MaxCapacity`, then `c + 0.1`, or `(c + 1.1) × 0.5` when `RedLineCapacity <= riders × 0.1`; the wear is
`(c + s) × WearRate × 0.5`. Repair loses the wear and life `0.02 ×` the wear unless the type is special, each clamped to 0..100; a repair byte below `WarnPlayer` writes `VAR_WORN` = 1;
below `Imminent` with the byte plus the wear not below it posts advice 0x40 (not posted: the advisor queue carries the forecasts only). No random number is drawn. The world
update runs at the 24-bit x87 precision the Direct3D device sets (`[EXT:hardware-d3d-path]`); the port rounds every step to a single.

**Mechanic.** Kind 4, constructor `0x5D9B50` (the person and member start `0x626190`, sprite category 0xA in template 3, `+0x5D8` and `+0x5D4` 0, the setter's state 0, the removal
message 10 subscribed, the search). The update is the common pre-update (`0x5DA3B0` jumps to `0x628050`) and the dispatch `0x5DA280`: 0 idle (voice 0xA3, the idle end
`0x62CA10`), 1 patrol (voice 0xA2, the walk, the search on arrival or stuck), 21 to the ride `0x5DA650`, 22 repairing `0x5DA7C0`; 2 and 3 are the rest area's (`0x629EF0`, `0x62A1C0`, the handyman's entries; with the staff rooms built a tired mechanic runs them, §13), and 0x23 goes through the common handler `0x6287A0` to `0x62CA90`.
- Search `0x5DA3D0`: the gate `0x62BBA0`; too tired goes to the tail; `+0x5D8` = the finder; the patrol area `0x62C6B0` (the whole map answers true; another rectangle needs the
  unported patrol-area tool, and a saved mechanic with one is refused); "found a ride"; the route `0x62EAC0` to the ride's entrance word `+0x46` at the point (0x80, 0x80), which calls the staff route `0x62EB10` (the closed park's entrance refusal and the walk mask, `StaffRoute`); the claim
  `0x5E3780` (`+0x72` = his thing id, `+0x74` = the turn); advice 0x43 (not posted); a ride not broken down, not called for and below `Imminent` gets the request `0x5E3400(1)`
  (`+0x78` = 1 and, unless a bin or a plant, closed); state 21; the tail patrols or idles.
- Finder `0x5DAAD0` on the HQ thing of world `+0x1EAB4E` (cursor `+0x14`, kept as `ParkEconomy.MechanicCursor`): the cursor moves one object on from the previous start over the object
  list (world `+0x1EAB88`, next `+0x14`, newest first, `0x64F3E0`) and walks it round once; a candidate is broken down, below its line, called for (not a relief object, bin or
  plant), or (for a mechanic whose learnt ability `+0x218` is 1) upgrading, and unclaimed; the nearest by `dx² + dy²` of the cell bytes, first broken or called-for ones, then the
  others, the first met on a tie. `+0x218` is 0 from the member start and only training writes it; a mechanic trained in Ride Upgrading would take an upgrading ride, but none reaches state 2.
- The object list (`ParkEconomy.ThemeParkIncObjectList`). The attraction constructor `0x5DB1B0` puts every attraction, obstacles included, at the head (`0x64F3E0`, called at `0x5DB28C`:
  `+0x14` = the old head, world `+0x1EAB88` = the new one); the destroyer `0x637C40` → `0x5DE780` → `0x64F430` unlinks it. The save keeps the list: the World variable `FirstObject` and
  each attraction's `mNext`. In the prebuilt Science park it holds 24 things, newest first (thing ids 37 down to 14: Info.Id 1414, 1413, 1413, 1403, 1101, 1798, 1750, 1703, 1700, 1701,
  1702, 1710, 1707, 3708, 1709, 1706, 1705, 1110, 1170, 1160, 1180, 104, 103, 1601). OpenTPW imports the objects in the save's record order (the four rides first, then 1403 to 1414
  and 1101, then the fixed items), so the economy's instance ids do not give this order; and 7 of the 24 things have no economy record: the obstacles 1700 to 1703, the Polar Zone gate
  1750 and the camera path 1798 are not in the economy catalogue, and 1710 is a tool entry, which is not imported. Both matter to the walk: the cursor steps over every thing, so the
  list's length and order decide which search finds nothing (the one whose previous start was the last thing) and which candidate a tie keeps. So the economy keeps the list itself:
  a park built from the save takes the saved order (`Level.SeedThemeParkIncObjectList`, by the saved thing each imported object was built from), an object with an economy record as
  that record and any other thing as its negated thing id. An object with a record enters at the head when the economy registers it and leaves with its sale; an obstacle's
  `ReplaceWithID` replacement enters at the head as its thing id (`ThemeParkIncObstacleThings.Place`), and a thing without a record that is removed (an opened obstacle, the mission
  controller's destroy) leaves through the level's removal handler. An id entered again drops its earlier entry first, and an object with a record that takes a freed thing id drops
  that id's entry, so the list never holds a thing twice. The Science continuation saves the list and the cursor. The HQ word is a thing id, so the cursor follows one: a cursor on a
  sold object keeps the object's thing id (negated), and when a new thing takes a freed id (the free list hands it back last in, first out) the cursor names the newcomer, a ride by its
  record (`ParkEconomy.LinkThemeParkIncThing`) or another thing by the same negated id, so the search starts at its successor as the original's does. The cursor may name a thing that is
  gone: the finder reads an id it does not find as "start at the head", so a save accepts any cursor in the thing range (a negated id down to `-ThingPool.Capacity`). A park built from
  the save holds the slot of a list thing OpenTPW does not rebuild (the tool entry 1710's thing 26), as the original keeps every saved thing alive (the World loader rebuilds the free
  list from the slots no saved thing occupies, `0x64D759`, as tp.exe's `0x518D43`); a slot that cannot be held refuses the park. This is a partial fix: the held slot takes the newest
  place of the used list rather than the saved thing's, and the slice's other saved things that it does not rebuild keep the new world's slots; a resumed list must name exactly the placed
  objects without an economy binding and, besides them, only such held attraction slots (`ParkResumeSaveFile.ThemeParkIncObjectListProblem`). The seven things were saved in state 3 with a full state of repair and no request, the object update skips state 3, and their `.sam` files declare no
  `RideBreakDown` keys, so they are never candidates. The prebuilt's HQ cursor word is 0.
- Claimant `0x5E37F0`: a claim older than 100 turns lapses unless the claimant still targets the object (`0x62B280`: a mechanic's `+0x5D8`), and the claimant then goes idle.
- To the ride `0x5DA650`: the claimant must be him ("UNUSUAL INCIDENT"); stuck clears `+0x5D8` and searches; arrived at a ride broken down, upgrading or called for sets 22,
  else searches with the ride kept.
- Setter `0x5DA150`: 21 queues template 0xF, 22 template 0x1A (tp.exe's repair program 0x12, `0x0074EE38`, opcode for opcode) and the timer `+0x5D4` =
  `(100 − repair byte) × the work duration 0x62E4C0 / 100` (32-bit, toward zero).
- Repairing `0x5DA7C0`: the working step `0x62B4D0`; the timer counts down; at 0 a broken ride gets `VAR_BREAKSTAT` = 0 and waits while `VAR_BROKEN`; then `0x5E2910`: repair 100,
  `VAR_WORN` = 0, the claim and the call cleared, `VAR_BREAKSTAT` = 0, the open `0x5E1DC0` (open word, `VAR_RIDECLOSED` = 0, state 0) and the closing tail: the guard again (the queue
  link `0x5E07B0`, which is tp.exe's `0x004DE4A0` instruction for instruction over 0x48-byte cell records, a running type-42 challenge, the park closed `+0x1EAB44`, the spell's
  `ClosedDuring*` word, the coaster test) closes the ride unless a running type-46 challenge holds it; then the spell's close sets `+0x41`, and a coaster that refuses guests closes.
- Removal message `0x5D9DD0`: a mechanic whose ride is removed forgets it and goes idle.
- Training: the dispatch sends every state outside 0..3, 21 and 22 to the common handler `0x6287A0` (the byte table `0x5DA358`, entry 6, `0x5DA333`). Its byte table `0x628B10` (state − 4, up
  to 0x24) has cases for 4, 5, 6, 7, 8, 9, 11, 12, 14, 24, 25, 35 and 36; a mechanic reaches the training's 7 (`0x62A7C0`) and 8 (`0x62AA50`) through the panel's ability choice (`0x580550`),
  and the suspended 0x25 and 0x26 lie above the table, with no case. So a mechanic walks to the training room, trains and is rewarded as a handyman is (§12.2), his saved state 7, 8, 0x25 or
  0x26 is valid, and after the training he looks for work through his own search (the look-for-work table `0x62D974`: kind 4 `0x5DA3D0`). Ride Upgrading (`+0x218` = 1) opens the finder's
  upgrading branch, which finds nothing while no ride reaches state 2.

Gated: the upgrade job (the economy sells no upgrade, so no ride reaches state 2; a mechanic does learn Ride Upgrading, `+0x218` = 1, in the training room, see above); the advisor's ride messages 0x40, 0x43 and the clearing of the ride's queued notices 6..9
(`0x636D10`) (`[EXT:tpi-capability-gates]`, the advisor's other messages; they draw nothing); the patrol area `0x6256A0` (WP15). The loo and bin finders and the toilet wear
stay under `[EXT:tpi-no-maintenance-wear]`, which no longer covers the rides. No `rand()` call is reached: the paths above draw only the world generator, in the gate and the
patrol, as the handyman's.


### Gardener and plants (WP10, November 22, 2026)

Read from Game.exe (static; the plant update and the chooser run natively in `tools/ppc-analysis/oracle_x86/tpi_gardener_oracle.py`, which agrees with the port on 3000 random cases at the
24-bit and the 53-bit control word). Ported in `ThemeParkIncStaffWorld.Gardeners.cs` and `ParkEconomy.ThemeParkIncPlants.cs`; tested in `ThemeParkIncGardenerTests` and
`ThemeParkIncPlantTests`. `research/tpi-staff/gardener.md` (in the notes) was the starting point; every address below was read again.

**Plants.** A type with `UsageInfo.RequiresWatering` (`+0x1AC`) gives its thing the flag `+0x42` bit 0x1000 and the RegionFX row 10 at the thing's cell (`0x5DB63F..0x5DB675`). The
thing's script loader `0x5DE610` tests that flag first and loads `data\levels\vegetation.rse` (`0x9CEE9C` with `.rse`) instead of the type's own script (`0x5DE61A`); the constructor then writes
`VAR_TIMEOUT` (2) = `Vegetation.TimeBetweenWaterings`, `VAR_TEST` (5) = `TestAnimation` and `VAR_SCALE` (14) = 1000 (`0x5DB850..0x5DB88A`). The script waits `VAR_TIMEOUT × VAR_SCALE`
(`SETTIMER` after `MULT`) and then moves `VAR_CONDITION` from 0 (healthy) through 1 and 2 to 3 (dead, `VAR_DEAD` = 1), noting the old condition in `VAR_OLDSTATE`; `VAR_WATER` = 1
brings 1 or 2 back to 0 (2 through 1) and restarts the timer, but not 3; `VAR_SETFIRE` and `VAR_ONFIRE` run the fire objects. A plant is not choosable (`Info.IsChoosable` 0), so
its object state is 3, and the attraction update (kind 3: `0x5E4790`, then the state dispatch `0x5E4CB0`, `0x63760B`) runs the plant block `0x5E4CDE` on every turn:
- `s = VAR_SCALE / 1000` (toward zero); the weather thing's state (`0x64EB80` → `+0x70`) 1 to 4 picks `Vegetation.HeatwaveWither`, `RainWither`, `StormWither` or `SnowWither`
  (`0xA029A4..0xA029B0`, `+0xF44..+0xF50` of the Standard object), any other 1.0; a wither unequal to `s` writes `VAR_SCALE = ftol(wither × 1000.0f)` (`0x5E4D82`).
- `+0x54 = (3 − VAR_CONDITION) × 33` as a float (99, 66, 33, 0), every update.
- A condition unequal to `VAR_OLDSTATE` writes `VAR_OLDSTATE` = the condition, takes row `10 + old` off the thing's cell and puts row `10 + new` on it (the conditions 0..3 only, tables
  `0x5E5044` and `0x5E5054`); a dead plant also clears the advisor's queued notices 0x2C..0x2F about it (presentation).
The destructor takes off the row of the **live** `VAR_CONDITION` (`0x5DE9D3`, table `0x5DEBF0`), not the row the update last moved, so a plant sold between a script change and its
next update leaves the difference of the two rows on the cells, as in the original. The level keeps a sold plant linked to its object until the economy has sold it, so the sale
reads the live condition from the plant's script (the remove tool's path). The script copies `VAR_CONDITION` into `VAR_OLDSTATE` before each change and follows each change with a
`WAITANIM` of at least 300 script ms, longer than a 248 ms turn, so the update runs between two changes (a watering from 2 runs 2 → 1, an update, 1 → 0): at any save point
`VAR_OLDSTATE` names the row on the cells, which is the row a load puts back. Theme Park Inc's RegionFX table has 15 rows (`3,RegionFX,0,0,15` in the schema; rows 10 to 13 are the
plants'). A plant has no wear (`WearRate` 0) and no repair line (`RideBreakDown.Imminent` 0), so a dead plant (`+0x54` = 0) is no mechanic's job. The plants of the three worlds are
features that neither provide relief nor hold litter (`ThemeParkIncPlantTests`).

**Gardener.** Kind 0x14 (`SaveThingModel.Gardener`, 20), dropped through `0x4D4FB5` into the constructor `0x5D2230`: the person and member start `0x626190`, `+0x5D0` = 0 and the gardener list
(`0x64F510`: world `+0x1EAB7E`, the members' gardeners newest first), the sprite category 0x12 (`+0x34`, `0x5D1000(3, 0x12, variant)`: `esprites.wad`'s `Generic\Gardeners`, `Spr_gd`), `+0x5D8`
and `+0x5D4` = 0, the removal message 10 subscribed, then the search. The thing update `0x63763E` runs the pre-update `0x5D2820` and the dispatch `0x5D2A60`.
- Pre-update: `0x628050`, then from state 0 the search on turns that `p = trunc((1 − exp / MaxExperience) × 50)` divides (every turn at `p` = 0); no finder runs first, unlike the
  handyman's.
- Dispatch (byte table `0x5D2B68`, jump table `0x5D2B4C`): 0 the idle end `0x62CA10` alone (no voice draw; look for work `0x62D810`, whose kind table `0x62D974` sends kind 0x14 to the
  search `0x5D28C0`); 1 a world draw for the walking voice 0xA0 (1 in 16), the walk step: arrived or stuck searches, walking takes the moving step; 2 and 3 the rest area's (`0x629EF0` and
  `0x62A1C0`, the jump-table entries 2 and 3; §13); 0x1F `0x5D2C70`; 0x20 `0x5D2ACE`; every other state, and anything above 0x20, the common handler `0x6287A0` (7 and 8: training; 0x23, the rest search, `0x62CA90`).
- Search `0x5D28C0`: the gate `0x62BBA0`; tired (`0x62B3B0`, "Too tired to water plants") goes to the tail with `+0x5D8` kept; else `+0x5D8` = the chooser's answer; a plant: "Gardener found dry
  plant, walking there now", the maintenance request `0x5E3400(1)` unless already set (a plant only sets `+0x78`: no closing), the speed bonus 25 (`0x60B3F0(1)`), template 0xF and the
  state 0x1F; none: the patrol `0x609F70` (state 1) or idle.
- Chooser `0x5D2DC0`: the object list from its head (world `+0x1EAB88`, next `+0x14`; no cursor), each thing with `+0x43` bit 0x10; the claimant `0x5E37F0` (read twice) neither 0 nor his
  thing id skips; the area `0x62C6B0`; `d = dx² + dy²` of the cells; `R` = the lerp of `Gardener.MinDetectionRange` and `MaxDetectionRange` (3 and 5) by experience; a plant that asked for
  maintenance with `d` below the best and not burning (`0x5E3B20`: `VAR_ONFIRE`) is taken; otherwise a dry one (`0x5E3990`: `VAR_CONDITION` > 0 and alive or burning) with `d` below `R × R`
  and not burning. A taken plant needs the drop's fallback `0x5E9320` (the path cell beside its footprint nearest the gardener; it writes that cell's queue link) and a route to it
  (`0x62EAC0` → the staff route `0x62EB10`); then best = `d`. The dry branch never compares with the best, so the **last** dry plant in the list wins whatever its distance (the list is
  newest first). After the list the chosen plant is claimed (`0x5E3780`).
- 0x1F: walking walks on; stuck goes idle with `+0x5D8` kept; arrived, his own claim (`0x5E37F0`) sets `+0x5D4` = the turn, template 0x19 and 0x20, another goes idle.
- 0x20: the working step, the facing `0x609E60`, the work duration `0x62E4C0` (`Gardener.MinWorkDuration` 30 to `MaxWorkDuration` 5, the normal form); past `+0x5D4 + duration` the
  watering ends (`0x5E31B0`: `VAR_WATER` = 1, `+0x54` = 100, the claim word `+0x72` and the request cleared (the claim turn `+0x74` keeps its stamp), the advisor's notices 0x2C..0x2F cleared), template 3 and state 0. No tiredness test.
- State hook `0x5D29D0` (through `0x62AECD`): 0x1F and 0x20 as above, the others the common setter `0x628180`.
- Claims: the validator `0x62B280` compares the claimant's `+0x5D8` with the object for kinds 4 and 0x14 (`0x62B2B3`), so a gardener on his way or watering keeps his claim past 100 turns.
- Removal message `0x5D2480`: message 10 for his `+0x5D8` ("plant I was watering was deleted"): `+0x5D8` = 0, template 3 in state 0x20, state 0.
- Load `0x5D2510`: `mTimeStartedWatering` (`+0x5D4`), `mPlantToWater` (`+0x5D8`) and `mNext` (`+0x5D0`); the continuation save keeps `PlantId` and the work stamp.

Training: the gardener learns abilities 10 to 14 (`0x62B6C0`) through §12.2 and fills a staff HQ slot that asks for one (the rebuild's staff type 5, the finder `0x6348C0`).

Not ported (inert, no stand-in): the facing `0x609E60` (the heading word `+0x2C` from the queue link of his cell, in the 2048 units of `0x91E074`: presentation; he keeps his walk's
last heading under `[EXT:tpi-twp-guest-rules]`, D1); the destructor's claim release `0x5D2430` → `0x5E37B0` (Theme Park Inc staff cannot be fired yet); the advisor's notices
(`[EXT:tpi-capability-gates]`, the advisor's other messages); the patrol area `0x6256A0` (WP15); the guests' fire prank on plants (`0x618F30`, `VAR_SETFIRE`, with the guests' Theme Park
Inc pranks); the plant counts of the statistics (`0x5B4470`, `0x5B8960`); the handyman's bin finder's plant branch for ability 7 (`0x5D5FAC`, with the bins, whose fill is unported: `[EXT:tpi-no-maintenance-wear]`). No `rand()` call is reached:
the gardener draws only the world generator, in the gate, the walking voice and the patrol.

**x87 precision.** `0x5D2820` runs at the game thread's 24-bit control word (`0x007F`, `[EXT:hardware-d3d-path]`), and so do the handyman's shared helper `SearchPeriod`, the idle
term `IdleTerm` and the step `member.Step` (the moving step `0x62B3E0` and the working step `0x62B4D0`, which are identical) since November 22, 2026 (§12.4): each x87 step rounds to a
single. With `MaxExperience` 100000, experience 34000 gives a search every 33rd turn (33.0); at 53 bits it would be 32 (32.99999999999999).

### Entertainer and guard (WP11, WP12, November 22, 2026)

Both are hired and dropped like the other roles (thing models 6 and 7 of the pool; the type codes follow the role order, §12.2). Static reading of `Game.exe`; the routines named were read
at the addresses given and the entertainer's and guard's machines run natively in `tools/ppc-analysis/oracle_x86/tpi_performer_oracle.py`.

- **Entertainer** (kind 6): pre-update `0x5D1960`, dispatch `0x5D1E20` (byte table `0x5D1F70`, jump table `0x5D1F58`: 0 → `0x5D1F1D`, 1 → `0x5D1EC7`, 2 → `0x5D1F05` → `0x629EF0` and 3 → `0x5D1F11` →
  `0x62A1C0`, the rest area's (§13), 4..0x16 and anything above 0x17 the common handler, 0x17 → `0x5D1E44`; the bytes are `00 01 02 03`, then `05` for 4..0x16 and `04` for 0x17).
  - State 0: a world draw whose low nibble 0 plays voice 0xA5 (`0x60BE90`), then the idle end `0x62CA10` (`stamp − trunc((1 − exp/MaxExperience) × −50)`, `0x62D810` kind 6 → the search `0x5D1A30`).
  - State 1: a draw for voice 0xA4, the walk step `0x60B440`: arrived or stuck searches, walking takes the moving step `0x62B3E0`.
  - State 0x17 (performing): the working step `0x62B4D0`, the work duration `0x62E4C0` (`Entertainer.MinWorkDuration` 10 to `MaxWorkDuration` 75, the normal form); past `+0x5D4 + duration` sound 0x87 at the
    sprite (`0x4E3D90`, `0x652860`) and the search. 0x13..0x16 are not states but the performance templates the setter queues.
  - Search `0x5D1A30`: the gate `0x62BBA0` first; unless tired (`0x62B3B0`, "Too tired to entertain": no draw), the radius `0x62C900(0, MaxExperience, exp, MaxActiveRange, MaxActiveRange)` (always
    `Entertainer.MaxActiveRange` 5), a world draw % 3 == 0 and the finder `0x5B1B20(kind 1, cell word, radius)` (the square clipped to the map, the first thing of kind 1: a guest) set 0x17 through the
    setter; the tail: the world turn & 3 == 0 sets 0, else the destination `0x609F70` sets 1, none leaves the state. A performing member whose `0x42A8D0` ("a dodgy version of GetCurrentTheme": the word
    `0xA0656C` mapped 0→4, 1→1, 2→2, 3→3, else 1) is 1 with variant 1 or 2 with variant 0 spawns a visual effect (`0x60B700` → `0x65BD10`, a pool slot, no draw): presentation, not ported.
  - Setter `0x5D1D30`, used by his search and constructor only (the base code's `0x628180` calls it never; `0x62D9F0`, the by-kind setter, is reached from the picked-up states only): it draws **once from the
    world whatever the state** (`R`, before the state is looked at), `n = 0x690330(+0x34, +0x30)` the bank's performance count (`+0x20C`, a world without banks takes 1) and the theme record's index
    (`0x42A3B0`, `0x74F4B0`, `+0x64`: the position of the park's theme in the table `0x9D53B4` "arabian", "science", "water", "wIntro", "Unknown"); a state other than 0x17 goes to `0x628180`; 0x17: `R % n == 0`
    gives template 0x13 when the theme index or the variant byte is nonzero, else 0x14; otherwise theme 2 with variant 1 gives 0x15, any other 0x16; the template, `+0x1B4 = 0x17`, `+0x5D4` = the world turn.
    The templates are the programs `0x9B04E4`, `0x9B0530`, `0x9B056C`, `0x9B05B8` of the table `0x9CDA94`: SLOT 4 (0x13, 0x14) or 0xA (0x15, 0x16), FRAME 0..3 (then back 2, 1 for 0x13 and 0x15), JMP.
  - Constructor `0x5D1380`: `0x626190` (kind 6), category 8 and the variant byte on the sprite, the base speed `0x62C970(0)`, **the cell effect** (below), `+0x5D4 = 0`, then the search.
- **Guard** (kind 7): pre-update `0x5D3B40` (the cell effect with row 3), dispatch `0x5D3C60` (byte table `0x5D3DD8`, jump table `0x5D3DB4`: 2 → `0x5D3D67` → `0x629EF0`, 3 → `0x5D3D71` → `0x62A1C0`, the rest area's (§13); anything above 0x1E the common handler), decision `0x5D3C10`, setter `0x5D3E70`, constructor `0x5D3140` with
  `0x5D3230` (category 0xB). State 0: a draw for voice 0xA7, the idle end (→ `0x62D810` kind 7 → `0x5D3C10`). State 1: a draw for voice 0xA6, the walk step: walking takes the moving step, arrived or stuck runs
  the decision. The decision: the gate, then the world turn & 3 nonzero and the destination `0x609F70` set 1, else 0, both through the guard's setter (`0x60B3F0(0)`, the speed bonus 0, before `0x628180`).
  Guards learn ability 9 (`0x62B6C0`) through §12.2.
- **The cell effect.** The person constructor stores the cell word `(y << 7) + x + 1` at `+0xE4` (`0x608F74`); the constructors put the row down (`0x5D7500(row, cell, radius)`, row 0 for the entertainer, 3 for the guard,
  radius `0x62C900(0, MaxExperience, exp, Min, Max)` with `Entertainer.MinActiveRange`/`MaxActiveRange` 2 and 5, `Guard.MinDetectionRange`/`MaxDetectionRange` 5 and 10 at `0xA022EC`/`0xA022F0` and `0xA02314`/`0xA02318`).
  The pre-update runs `0x609E20`, which hands the stored word and stores the current one; when they differ the row comes off the old cell (`0x5D7530`) and goes down on the new one with the radius of the moment. The
  writer `0x5D7770(flag, row, cell, radius)` takes the radius as an argument (its low 16 bits, not the row's own `RegionFX[i].Radius`) and adds `sign × coefficient / (|dx| + |dy| + 1)` (`idiv`, toward zero) to the
  five halfwords of each cell of the clipped square. Experience grows by the day, so a removal can run with a larger radius than the add it undoes and leave the difference in the grid (the guard's security reaches nine
  cells out; the entertainer's happiness 2 is 0 beyond one cell, so his radius never shows). `ParkEnvironment.ApplyRadius` is the writer; the difference is kept (`KeepRadiusResidue`) so that a save's rebuild of the
  sources plus its residue is the live grid. The guests' needs phase reads the grid, so the entertainer cheers the cells around him and the guard's security row stays under the guests (Theme Park World's mechanism).
- **Saves.** The member's thing keeps the state, the idle stamp, the performance start (`+0x5D4`), the speed words, the walk, the template and the cell effect (the cell word and the radius it went down with); the
  grid's residue is the environment residue the park save already has. A Theme Park Inc save made before the entertainers and the guards (no `TpiPerformers`) is refused with a start-again message before its assets
  are checked (`ParkSaveFile.ValidateEdition`). This is the standing policy for older Science saves (user decision 2026-11-21: refuse and document), not a fidelity necessity: such a save would be a valid park. Neither role claims an object, so the claim validators and `ValidateThemeParkIncJobKinds` refuse a claim by either (they accept mechanics and gardeners only).
- **Sprites.** The entertainer's category 8 is the park's theme folder of `esprites.wad` (`Arabian`: Spr_ge and Spr_ph, `Science`: Spr_bu and Spr_sp, `Water`: Spr_fs and Spr_pg, `wIntro`: Spr_bu and Spr_sp);
  the guard's category 0xB is `Generic\Guards` (Spr_gu). The entertainer keeps the candidate's sprite draw (variant = draw >> 2 modulo the loaded count, as for the handyman and the mechanic); the guard's start ignores it and takes his own: `0x5D3253` calls `0x6902F0(0xB)`, which draws the world once, `(R >> 2)` modulo the guards' loaded banks, right after the person start's base-speed draw. The staff renderer draws every role but the researcher.

Not ported, with the reason:

- **The guard's chase, carry, after-release walk and first aid** (states 0x1A, 0x1C, 0x1D and 0x1E; `0x5D3960`, `0x5D4100`, `0x5D4620`, `0x5D3CA0`, `0x5D43D0`, `0x5D3A20`) are entered from the staff HQ's prank message
  `0x6323F9` (security word of the cell's environment record `> 0`, the nearest free guard `0x6343C0`, `0x5D3960`) and the first-aid loop `0x557E00`, and decide on **the guests' own Theme Park Inc states**: the catch
  test `0x614720` reads the guest's `+0x2C0` (not catchable: 0..5, 0xE..0x15 and 0x17..0x1E; catchable: 6..0xD, 0x16 and 0x1F upward; the research note's 0x17 was 0x16) and the first aid takes a guest in state 0x1B (written by `0x554C60`'s caller, not
  found). The guests of a Theme Park Inc park run Theme Park World's rules (`[EXT:tpi-twp-guest-rules]`) with Theme Park World's state numbers (6, 7, 9 and 10 are the catchable ones there), so neither the catch test nor
  the first aid's patient can be read from them without inventing a mapping. The release points are resolved (`KidRelease[0..1]` at `0xA02638`, from `levels/Standard.sam`, and the entrances `EntranceA/BPos` at
  `0xA025D0` and `0xA025D8`), the chase timer is `5 × 0x62E4C0` and the first aid's `5 ×` then 10, but the states stay unported. A guard in one of them is refused in a save, and no prank reaches the guards.
  `[EXT:tpi-capability-gates]` covers this.
- **The guests' reaction to a performing entertainer** (in the guest's own update; `[EXT:tpi-capability-gates]`: Theme Park World's turn towards a performer, `0x004FEFF4`, reads Theme Park World's staff, so a Theme Park Inc park's guests get neither reaction; `0x614D41..0x615414`: a performing entertainer in the guest's 3 × 3 cells turns the guest and writes its state through the jump table `0x615414`; the
  amounts are unread) is not ported; only the row 0 cheer reaches the guests.
- The effect's removal by the destructors (`0x4D4620`-like; staff cannot be fired or picked up yet), the dead routines `0x5D1BC0` and `0x5D18D0` (the "Ouch! You little git!" message; no reference anywhere in
  `Game.exe`), the duplicates `0x5D1C80` and `0x5D1CC0`, the voices' log lines, the advisor's message `0x3A` of the guard's constructor and the patrol area `0x6256A0` (WP15).
- The shared helpers (`SearchPeriod`, `IdleTerm`, `member.Step`, at the game thread's 24 bits) are used as for the gardener; no new formula of the two roles has a float. No `rand()` call is reached
  (the ranges `0x5D1960..0x5D1F60`, `0x5D3B40..0x5D3FB0`, `0x5D3230..0x5D3340` and `0x62CA10` hold none): the roles draw only the world generator, including the guard's variant draw at his drop (the entertainer's drop draws none besides the person start's and his first search's).

## 12. Staff HQ obstacle slots, training and research (November 21, 2026)

### 12.1 The staff HQ's obstacle slots

`ThemeParkIncStaffHeadquarters` (held by `ThemeParkIncStaff.Hq`, saved in its snapshot) is the slot part of the HQ thing (class 9, `park+0x1EAB4C`, constructor `0x632270`):

- **Table `0x634600`.** The mission's five `ObstacleStaff` records (StaffType `+0x2E0`, Ability `+0x2E4`, Number `+0x2E8`, stride 12) append `Number` empty slots each, whatever the count dword; the rest up to 20
  are `{7, 0, 0}` ("no requirement"). The append has no bound in the executable; OpenTPW refuses a mission asking for more than 20.
- **Staff type codes `0x62AF70`** are the role order: handyman 0, mechanic 1, entertainer 2, guard 3, researcher 4, gardener 5 (r7 §8.1 had guard and entertainer swapped; the table `0x62AFD0` settles it).
- **Assign `0x634B00`**: a member with slot index -1 takes the first slot whose ability equals his trained ability and whose thing word is 0. The staff type is not compared (`0x62AF70`'s answer is dropped
  at `0x634B50`), so an untrained member can fill a padding slot and any role with the ability can fill a requirement.
- **Release `0x634730`** (message 0x21 for a staff thing, 0x2A gone to train): the ability's count falls (also for ability 0); ability 0 stops; a member in a slot leaves it (only gardeners, handymen and
  mechanics have the slot cleared, at their own index) and the first member of his role with the same ability who fills no slot (the finders `0x6348C0`/`0x634980`/`0x634A40` over the role's list, the
  table excluding the slotted, `0x55E3B0`) is assigned.
- **Training finished `0x63256F`** (message 0x2B): count up, assign.
- **Rebuild `0x634D20`** (messages 0x23 and 0x24): every slotted gardener/handyman/mechanic leaves his slot, the table is rebuilt, and for an obstacle mission each requirement's finder by staff type (0
  handyman, 1 mechanic, 5 gardener; another type repeats the last answer) assigns a member.
- **MetRequirements `0x55A620`** reads the real slots (`ThemeParkIncStaffHeadquarters.AllFilled`). A park file's HQ is loaded, not constructed: the prebuilt Science park keeps its saved twenty `{7, 0, 0}`.
- Not ported: the per-object trained-staff requirement lists at `0xAA0330` (`0x623FB0` on 0x2B, `0x6241B0` on a release), which an attraction's activation reads (UITEXT 985).

Message delivery: the queue `0xA09728` reaches its listeners in ascending handle order; OpenTPW calls the listeners at the post (`ParkEconomy.ThemeParkIncObstacleResearched`, `ThemeParkIncObstacleRemoved`),
the HQ before the mission controller.

### 12.2 Training and abilities

`ThemeParkIncTraining` (rules) and `ThemeParkIncStaffWorld.Training.cs` (states 7 and 8 of a placed member) port Theme Park Inc's training, which replaces Theme Park World's budget split:

- **Abilities** (`ABILITIES.str`, member `+0x218`, one at a time): compatibility `0x62B6C0` is mechanic 1–6, handyman 7–8, guard 9, gardener 10–14, entertainers and scientists none. A member may learn an
  ability (`0x639B20`) when the career discovered it (`+0x1064`; 1, 7 and 9 at a reset, the obstacle research adds the mission's), his role takes it and he has **10000** experience (hard-coded; the
  drop path alone also tests `MinExperienceForTraining`).
- **Choosing** (`0x580550`, the "Select Ability To Learn" panel `0x580860`): the panel lists every learnable ability, or UITEXT 713 when three members are in states 7, 8, 0x25 or 0x26 (`0x639F40`), or the
  role's line 707–712. The member's own ability refuses (714); another one asks for the retraining confirmation (715); then `+0x22C` = the choice and state 7, even from state 8.
- **The drop path** (`0x629B20` on a training room's cell) hard-codes ability 2, so only a mechanic who may learn Construction trains by a drop; any other drop falls through to the normal drop.
  OpenTPW's drop (handymen only) therefore never trains, as the original's.
- **Training room**: the first thing of the object list `park+0x1EAB88` whose flag word has 0x400 (`UsageInfo.TrainingRoom`, set by the attraction builder `0x5DB79A`); one room serves every trainee.
- **The staff route** `0x62EB10` (litter, patrol entry, training room, research room): a closed park (`park+0x1EAB44`) refuses the entrance cells (47, 17) and (48, 17); the walk mask (`+0x1A0`, the
  walk's `+0xB4`) is 1 when the member's cell or the target cell is empty (type 0), else 0; then `0x60B8E0` sets the walk. Its stranded-mark test (`+0x1B0`, `0x60BB50`) never holds for staff: the
  only writer with a stamp (`0x60A4AE`) is reached by guests alone. **The cell steer** `0x60B7F0` (Theme Park World's `0x4FA530`) is the same walk to a cell's centre.
- **State 7** (entry `0x628492`, tick `0x62A7C0`): the staff route to the centre of the room's entrance cell (`+0x46`), and when it is refused the walk mask 1 and the cell steer `0x60B7F0` to the same
  point (the second search may leave the paths), with the speed bonus 25; arrived, the room's script variable 0 rises, state 8 and
  message **0x2A** (the HQ releases his old ability, §12.1); stuck, every update costs `HappyHitCosRoomInaccessible` (the CRT `rand()` there only picks a thought bubble, which is not drawn, so no draw is taken; user decision 2026-11-21).
- **State 8** (entry `0x62852A`, tick `0x62AA50`): the sprite source is deleted; `+0x220 = +0x22C` and the progress 0 (experience ≥ 10000); `+0x224 += SkillIncrement` (10) each update; at
  `ProgessUpperLimit` (3000: **300 updates**) the ability is learnt, the advisor's line 0x175, the finish `0x62AB90` (message **0x2B**, the letter of kind 0xC, the room's variable 0 falls, the sprite
  again, state 0), motivation 100, `TrainingExtraExperience` (5000) and then `StaffAgencies[agency].TrainingExperienceGainModifier` percent of the new experience, capped at MaxExperience, and a look
  for work. No wage change.
- **A destroyed room** (`0x637C40`): the demolish runs at the click (the world click handler `0x4D30F8` → the cell-op engine `0x660D80` → `0x666DA0` → `0x6674E1`), and the destroy broadcasts message
  0x21 and then the attraction's message 0xA (`0x5DE780`, which first unlinks the room from the object list with `0x64F430`, so the room finders no longer see it) through the dispatcher `0x436DD0`, which calls every listener at once in ascending thing handle order; only the thing's free waits for the
  turn's tail (`0x649814`). OpenTPW's removal hook (`ParkObjects.ObjectRemoving`) runs at the same click (`ThemeParkIncStaffWorld.RoomDestroyed`).
- **Cancel** `0x62E7A0` (the staff message 0xA for a removed training room, or state 8 entered with nothing chosen): nothing for a member learning nothing; else the ability in training is dropped,
  the room's variable 0 falls, a member in the room is shown again, the advisor's line 0x1C9, the letter of kind 0xD, state 0 and a look for work.
- **What abilities unlock**: Ride Upgrading (1) for upgrades, Firefighting (7) for burning bins, First Aid (9), the HQ's mission slots, ride activation (UITEXT 985) and research consultants. Of these
  only the HQ slots and the consultants (§12.3) are ported; the others belong to unported role work.
- **HUD** (`[EXT:tpi-text-hud]`): a hired member's row in the staff panel opens his ability list (with a training room in the park); choosing the same ability twice confirms a retraining; the
  advisor's lines show as notes.
- Not ported: the training-room panel's three slots (`0xA6C128`) and its suspend/resume (states 0x25/0x26, `0x62B960`), which only it sets.

### 12.3 Research and the researchers

`ThemeParkIncResearch` (the lab thing, class 14, and the object controls' researched words) and `ThemeParkIncStaffWorld.Researchers.cs` port Theme Park Inc's research, which replaces Theme Park
World's effort split:

- **The lab** of a park file is loaded, not filled: the prebuilt Science park brings 50 projects, both slots at -4 (the lab start's value, which every reader takes for none), the workload 85 and the
  object controls (`0x5CF9B0`, researched `+0x10` and level `+0x14`). Projects are `+0x78` records of 0x24 bytes (`0x61F670`); `mType` stays whatever the record held (the prebuilt's free records are 0xCD).
- **Tick** `0x61E980`, every sixth world tick (`W+0x1EAB40 % 6`, after the turn's increment): slot 0 then 1; a slot with a project and a consultant (or a project whose `StaffRqd` maps to no role,
  `0x576FE0`) sums, over its 20-member team, each thing in state **24** (no kind or listing test): `work × workload × SpeedScaleDown + sum`, work = `0x62E4C0` (the researcher's
  `Researcher.Min/MaxResearchAbility` lerp, 1 for a salary below 10). The game thread's x87 runs at the control word 0x7F (24-bit significand) after DDSCL_FPUSETUP (`0x6BF33F`, `0x6BF5A8`), so the
  `fimul`, `fmuls` and `fadds` each round to a single, as do the advance's cash product, the percent done (`0x620210`) and the researcher's step below; no x86 oracle covers these routines yet.
- **Advance** `0x61FF80`: a finished project only releases its team; else the points (at most the rest of the cost) are added, `trunc(CashPerBrainPoint × points)` is debited through the bank (also 0),
  and at the cost the object control is researched with the project's level, message **0x11** goes out, every type `LinkedWith` the object is researched, and the team is released (`+0x274 = 0`; the
  researchers keep their state, the slot keeps the finished project).
- **Message 0x11** `0x61DC20`: the career is told (`0x74D740`: the CDK entry of the type clears its flag); a **Park Expansion** (category 5) posts **0x23** (the HQ rebuilds its slots and the controller
  shows the obstacle, §12.1); then the next upgrade level's project (`0x61FE70`).
- **Obstacle project** `0x61CBF0` (the controller's registration and every message 0x27): HasObstacle, the obstacle thing, obstacle state 1, the type in the viewed zone's range (arabian ≥ 3700, science
  ≤ 1800, water 2700–2800), no project of the type yet: category 5, cost `Obstacle.ResearchEffort` (no CostScaleUp), one cash a point, no consultant.
- **Consultant** `0x61D970`: none for a project without a role; with `StaffSA` the first staff member of any role with that ability, else the first of the role (never a researcher). In the original a consultant who
  leaves is replaced by the pick and a leaving scientist leaves both teams (`0x61DAD0`, message 0x21); no member leaves a Theme Park Inc park in OpenTPW (the fire and the quit are not ported), so this
  listener, like the HQ's release on a destroyed member (`0x634730`), has no sender and is not modelled.
- **Catalogue**: a type whose control is not researched is not listed (`0x57CB66`; the mode-3 exception is not the career game's). A park without a seeded lab (a new zone, the headless economy) keeps
  every buyable object on offer (`[EXT:tpi-no-research]`). The lab's save keeps the object types' research words it read (`Research.Category`, `LinkedWith`, `Group` and the levels' costs), so a
  lab restored without a level completes its projects as the level's would; a lab without them waits instead of ticking (`[EXT:headless-economy]`).
- **The panel** (text HUD rows, `[EXT:tpi-text-hud]`): no scientist → 328/329; "Start New Project" takes slot 0 when it is empty or finished, else slot 1, else 336/337 (`0x575F98`); the chooser
  (`0x574D00`) offers, in project order, every project not finished, not the other slot's, whose group is within the current zone's tech level, and a level project only when its object stands in the
  park; the consultant is the automatic pick (none → 330/331); the team lists the researchers not on the other team; OK with nobody asks 333, a cancel asks 334/335 (`0x61F560`). The original's
  "Edit Project" commits the last project chosen in that slot's chooser (`0xA67C24`), which can be project 0 after a load; the text HUD edits the slot's own project.
- **Researchers** in the park: hired and dropped like the handyman (`0x61B070`); the start runs the look-for-work sequence (`0x61B820`: the gate `0x62BBA0`, too tired → idle, the room finder
  `0x62E0E0` → state 25, none → happiness −2 and idle) and sets the lab's active flag (`0x61E4E0`). The finder takes the **first** room flagged 0x200 (`UsageInfo.ThinkingSpace`), with no distance,
  capacity or tie test, when a walk to its entrance reaches it. State 25 (`0x62DCB0`): arrived → the room's script variable 0 rises, state 24 (the sprite deleted, `+0x278` stamped); stuck →
  `HappyHitCosRoomInaccessible` and idle. State 24 (`0x6289BD`): energy `+= x × −0.1f`, happiness `+= x × −0.05f` (x = `1 − exp/MaxExp`, every operation rounded to a single, the clamps on the rounded sums); at an energy byte of at most RestLevel
  he leaves (variable 0 falls, shown again), idles and looks for work — at exactly RestLevel the tired test (`<`) lets the finder send him back. Research ends no other way. The display state counts
  "Researching" only for a listed researcher (`+0x274`). A removed research room clears both teams (message 0x21, `0x61DB90`) and then sends its researchers out (message 10, `0x61B570`), at the click as above.
- **Level projects** `0x61FE70`: after a completion, levels without a research cost count as researched at once and the first level with one becomes a category-4 project (cost × CostScaleUp). Every
  shipped `Upgrades[n].CashPerBrainPoint` is 0, so the original calls the CRT `rand()` (`0x61FF1A`) and throws the result away: the project always costs 1 cash per brain point and no draw is taken
  (user decision, 2026-11-21).
- Not ported: the lab fill of a new zone (`0x61C9E0`, `rand() % 2 + 1` per project at `0x61FD50`): it waits for the user's decision at the zone package. Also not ported: the category efforts
  (computed, read by no tick), the lab-active messages 5/6 (advisor), the "project suspended" texts 300–304, and the per-object staff requirement lists of §12.1. Ride projects need a mechanic consultant, and mechanics are not hired in the slice yet.

### 12.4 The game thread's x87 precision (November 22, 2026)

Every Theme Park Inc routine that runs on the game thread is read at the x87 control word `0x007F` (24-bit significand, round to nearest even), as tp.exe's are
(`[EXT:hardware-d3d-path]`); the earlier convention "`[BIN:TPI-EXE:0x5BCD60 precision]` 53 bits" is withdrawn. Static reading of `Game.exe`:

- The CRT start-up (`0x8746EC` → `_cinit` `0x87043A`, the pointer `0x9F6F18` → `0x86F430` → `0x8759B5`: `_controlfp(_PC_53, _MCW_PC)`) leaves 53 bits; DirectDraw's
  `SetCooperativeLevel` with `DDSCL_FPUSETUP` and without `DDSCL_FPUPRESERVE` (`0x851` at `0x6BF33F`, `0x808` at `0x6BF5A8`) lets the Direct3D device creation set `0x7F` and keep it.
- Nothing on the game thread puts 53 bits back. `_fpreset` (`0x880B57`, the only `fninit`) and `_control87` (`0x880B0C`) have no caller but the start-up's `_controlfp`. The renderer's
  pair `0x7071E0` (precision field cleared, the old word returned) and `0x707210` (the saved word back) brackets each frame (`0x6DD38B`/`0x6DD7FC`, `0x6DDB03`/`0x6DDF7A`); its
  extended-precision restore `0x707230` runs only when bit 1 of the render state `0xBE2328` is set, and no writer sets it (initial value `0x3000141` at `0x6F2705`; the other writers
  set or clear bits 0, 6, 8, 27, 28, 30 and 31, or put back a saved copy; the only pointer use, `0x702A2A`, writes the word to a debug log). This is tp.exe's arrangement
  (`0x591220`, `0x591240`, `0x591250`, `0x8BD508`). The renderer's check-and-set `0x707160` (thunk `0x40B2DF`) has no caller. The Bullfrog screen library's guard `0x812560`
  (`0x7D5A98`) changes only the exception masks, and its own `SetCooperativeLevel` (`0x7D5AAD`: `0x51` or `0x08`, with `0x800` only under a flag and never `FPUPRESERVE`; no direct
  caller) creates no Direct3D device. `0x47AC30` sets rounding toward zero and restores the word at `0x47ADA2`; `0x86DF20` masks the exceptions (`0x3F`) and restores it at `0x86E364`;
  `_ftol` (`0x86FAB0`), `0x86B720` and the C runtime's math helpers save the word and put it back. The other `fldcw`/`fldenv` hits (`0x6AB738`, `0x66D969`, `0x67DD75`) are data in jump tables.
- Switched to single-precision steps (`ThemeParkIncX87`, which rounds an exact integer-and-single result once): the wage review `0x62CDA0`, the daily experience gain `0x629650`,
  the step `0x62B3E0`/`0x62B4D0`, the rest `0x62A1C0`, the meter add `0x60D0D0`, the search period of `0x5D5000`/`0x5D2820` and the idle term `0x62CA10`
  (`ThemeParkIncStaffMember`, `ThemeParkIncStaffWorld`), the shop cost of goods `0x5E6260` (`ThemeParkIncGoodsCost`), the arrival group `0x5BCD60` (`GuestSimulation`) and the
  watering scrap percentage `0x5E7613` (`ParkEconomy.ThemeParkIncWateringPercent`). The guest base speed (`0x60C712`, #975), the research, the ride wear, the plants and the weather
  were already single-precision.
- Oracles at `0x7F` with `0x27F` beside: `tpi_staff_formula_oracle.py` (now also the idle term and the search period), `tpi_goods_cost_oracle.py`, `tpi_scrap_percent_oracle.py`
  (new) and the x86 target `tpi-exe-arrival-group` (`TPI_X87_CONTROL_WORD`). The price test (`tpi_price_oracle.py`), the staff candidates, the weather roll, the performers
  and the park points are precision-independent (their native answers are the same at both words).
- What changes in a retail park: with the shipped `Arrival` words (80 + `NewParkBonus` 20 over `PointsPerVisitor` 25, or 480 + 20) a group is one guest larger without
  weather and in rain and snow (100 × 0.01f rounds to 1.0); a plant at count 0 sells for 101 percent (3 × 33.333332f rounds to 100.0); the search period at experience 34000 of
  100000 is 33. The staff meters move in their last bits only (the retail `Standard.sam` cases of `ThemeParkIncStaffTests` are unchanged).
- Not switched: the loan offers `0x5BFB70` share tp.exe's `LoanMath` (the Mac's binary64 and `[EXT:deterministic-math]`'s `pow`); three of 506 random offer sets move by one at
  `0x7F`, the shipped TPI offers do not (`test_loan_offers_shipped_instalments_at_every_control_word`), as for tp.exe (ORACLE.md X4).

## 13. Rest areas (November 22, 2026)

The facilities folders (docs/THEME-PARK-INC.md, "Facilities") bring one staff room per zone (Science 1400, Water 2420, Arabian 3418) with `UsageInfo.ChillsYouOut` 1. The thing builder sets the thing
word `+0x42` bit 2 from the type record's `+0x1B0` (`0x5DB6E2`), so the staff rooms are the only rest areas (`ThemeParkIncRestTests.TheStaffRoomsAreTheOnlyRestAreasOfTheThreeZones`).
`ThemeParkIncStaffWorld.Rest.cs` ports the rest for every role: the handyman's table (`0x5D58C4`), the mechanic's (`0x5DA358`), the researcher's (`0x61B774`), the gardener's (`0x5D2B68`: the bytes `00 01 02 03` for states 0 to 3, the
jump table `0x5D2B4C` entries 2 and 3 calling `0x629EF0` and `0x62A1C0`), the entertainer's (`0x5D1F70`, jump table `0x5D1F58`: entries 2 and 3 at `0x5D1F05` and `0x5D1F11`) and the guard's
(`0x5D3DD8`, jump table `0x5D3DB4`: entries 2 and 3 at `0x5D3D67` and `0x5D3D71`) send states 2 and 3 to `0x629EF0` and `0x62A1C0`, and the common handler (`0x6287A0`, reached by every role's dispatch for 0x23: above the handyman's 0x14, the researcher's 3, the mechanic's 0x16, the gardener's 0x20, the entertainer's 0x17 and the guard's 0x1E) sends 0x23 to `0x62CA90` (state − 4 = 0x1F
in its byte table `0x628B10` is entry 10, `0x62897B`). The member's rest area (`+0x248`) is saved with him (`RestArea`). Read in this package (the WP4 notes, `research/tpi-staff/wp4-rest.md`, verified and
corrected):

- **Finder** `0x62BA20`: the attraction list (`park+0x1EAB88`, next `+0x14`, newest first) for a thing with `+0x42 & 2` and `0x5E3C20` answering 0, which is the open word `mCanLoad` (`+0x7C`)
  nonzero: the constructor sets it to 1 (`0x5DB5FB`) and closes only rides, shops and sideshows, so a built staff room is open. Key `dx² + dy²` of the absolute column and row differences of the
  cell words (bytes `+5` and `+7`), unsigned `jae` against the best so far (`0xFFFFFFFF` to start), so the first of equal keys stays. No kind, capacity or route test. The thing being destroyed is off
  the list while its messages go out.
- **Tired gate** `0x62BCA9` (energy byte at most `RestLevel`): the finder, then the staff route to the centre of the area's entrance cell (`+0x46`, `0x62EAC0`). A route sets `+0x248` and state 2
  and the gate answers 1, with no draw. No area or a refused route: "Staff member couldn't find a rest area", a world draw whose low nibble 0 costs `HappyHitCosNoRestArea`, and a working member (display 2)
  is set to 0.
- **Slack** `0x62D650`, run by the state setter's state 1 (`0x62822F`): an energy byte below `StaffSlackingEnergy` (signed), an area within `MinDistanceForSlacking` cells squared (unsigned), and a
  route set `+0x248` and the setter stores state 2 instead of 1 (`0x62823C`). Motivation plays no part.
- **Low motivation** `0x62D866`, the tail of every look for work: a motivation byte below `MotivationPercentToStrike / 2` (toward zero) with display state 2 or 7 walks to the nearest reachable area
  (state 2), else stops (state 0). No draw.
- **State 2** `0x629EF0` (entered with template 1): arrived, the area's script variable 0 against `StaffRoomCapacity` (signed). Full: the route back to its entrance (its result dropped), `+0x1A0 = 0`, state
  0x23. Room: variable 0 + 1, the area's customer counts (`0x5E5DA0`: history `+0x110` and `+0x1278`, OpenTPW's customer ring and month count), state 3 and message 0xF. Stuck: "Staff member couldn't get to
  his rest area", happiness − `HappyHitCosRoomInaccessible` (`0xA0225C`, not the no-rest-area constant), state 0. While walking: nothing, no moving step.
- **State 3** `0x62A1C0` (entered by `0x62858A`: the thought stops and `0x5D1050` deletes the sprite source, so a resting member is not drawn): `ThemeParkIncStaffMember.Rest` (§6), and at an
  energy byte of exactly 100 the rest end and a look for work.
- **Rest end** `0x62C230`: message 0xF, variable 0 − 1 (no floor), `+0x248 = 0`, the sprite source made again in template 3, state 0.
- **State 0x23** `0x62CA90` (entered like state 0; its display state is 2): `+0x248` = the finder's area; none sets 0 with no draw. With room in it: the finder again and a route set `+0x248` and
  state 2 ("Staff: couldn't get to nearest rest area..." otherwise). Full: the walk step, and when it answers arrived or stuck two world draws give the fractions of the member's own cell (column, then
  row) for the staff route. Then every branch, the successful one included, takes a world draw whose low nibble 0 costs `HappyHitCosNoRestArea`. The state stays 0x23 while the room is full, so the
  member waits beside it.
- **Removal** `0x626638` (message 0xA, every member in ascending handle order): a member resting in the removed area gets the rest end and then the nearest other area he reaches as `+0x248` (he walks
  there in state 0); one walking to it gets `+0x248 = 0` and state 0; any other state nothing.
- **Message 0xF** (`0x93E86C`, the member and 1 on arrival, 0 at the end) has one listener, `0x63A1E0` → `0x4F8A50` → `0x58DA50`/`0x58DB30`, which adds and removes the member in an interface list:
  presentation, not ported.
- The global words, read from the key table at `0x5994B4` (stride 0x3C): `RestLevel 0xA02254`, `HappyHitCosNoRestArea 0xA02258`, `HappyHitCosRoomInaccessible 0xA0225C`, the recuperation rates
  `0xA02270`..`0xA0227C`, `StaffSlackingEnergy 0xA02294`, `MinDistanceForSlacking 0xA02298`, `StaffRoomCapacity 0xA0229C` (retail `RestLevel` 1, `StaffSlackingEnergy` 50, `MinDistanceForSlacking` 10,
  `StaffRoomCapacity` 10).

The rest formula `ThemeParkIncStaffMember.Rest`, the moving step and the daily gain run at the game thread's control word 0x7F (24-bit significand, §12.4, `tpi_staff_formula_oracle.py`). The removal's
walk in state 0 and the debug notices (`0x4D15A0`, `0x4D15C0`) are as read; the notices are not shown.

The finder `0x62BA20` walks the object list (park `+0x1EAB88`, `ParkEconomy.ThemeParkIncObjectList`), newest first, not the thing list; `ThemeParkIncRooms.RestAreas` and `First` walk the same list.
