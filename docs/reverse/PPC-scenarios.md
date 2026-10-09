# PowerPC scenario, progression and staffing evidence

2026-10-09. Scenario lane of the nine-lane PowerPC continuation. Static
inspection of the Feral Interactive Mac port of *SimTheme Park* (Theme Park
World); the original program was never run. No original bytes, disassembly,
extracted assets or manual text are stored here. A local disassembler
(`llvm-mc`) was used for review only. The checked-in tools are standard library
only and print interpreted operands, offsets and identifiers.

Scope: game modes and player creation, golden tickets and keys, theme entry,
challenge activation, research progression, strikes. Every finding below is a
fact about **this Mac binary** unless explicitly stated otherwise. It is not
evidence for the PC `TP.EXE` or Patch 2 runtime (see *Mac and PC relationship*).

## Reproduce

```sh
# instruction-field witnesses (374 checks, identity-pinned)
python3 -I tools/ppc-analysis/lanes/scenarios/scenario_evidence.py /Users/sander/server/game-assets/mac-feral/bin
# tests (synthetic fixtures; the corpus case runs only with OPENTPW_MAC_BIN set)
python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
OPENTPW_MAC_BIN=/Users/sander/server/game-assets/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios
```

Mac data comparison: copy `hfs.img` to a scratch directory first, then use
hfsutils with an isolated `HOME` (hfsutils keeps state in `$HOME/.hcwd`, which is
shared between concurrent sessions) and copy data forks only (`hcopy -r`) of
`:Theme Park Data:data` into a scratch tree (`Language/American/...`,
`Advisor/Advisor.sam`, `levels/...`). Then:

```sh
python3 -I tools/ppc-analysis/lanes/scenarios/mac_data_compare.py <scratch-mac-data> \
  /Users/sander/server/game-assets/theme-park-world/Data \
  --patch2-data /Users/sander/server/game-assets/theme-park-world-patch2/Data
```

Identities: `SimThemePark.data`
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`; `hfs.img`
`46edf2f94ce9a36834f7760ef3e3852e623863a8a8ef99ef629872b15d599365` (volume
"Theme Park", created 2000-11-09); Mac manual `STP_Manual.pdf` (78 pages)
`0bd911e353a9d587d640976b5adbb9261ca88daa47a8f6ab5028eee7c2a09f5f`; Mac
`STP_Easy_Guide.pdf` (14 pages, "US Easy Guide 25.10.00")
`e3c6ce419c748f52777a1f8ed96d8153e6b9c135a30fac65ec06f4a9026bd70f`; supplied
Windows manual `c96eb25f…0668` (39 pages, see `docs/REFERENCE-MANUAL.md`).

## Method

String literals sit in the code section and are reached through relocated TOC
words (TOC base `0x8000`, from the main transition vector). Functions were
located from those references and from direct `bl` call targets only; the
heuristic traceback finder was not used. "Sole caller" claims mean: exactly the
listed direct calls exist and no relocated data word (transition vector or
pointer) names the routine. The balance (`.sam`) schema is a static table of
60-byte records `[type][56-byte name]`; struct field offsets were anchored by
independent uses (below). Advisor message IDs passed to the message builder at
`0xb6d8` are advisor-rule IDs, not `.str` indices, and stay unresolved.

## Mac and PC relationship

- All 18 compared scenario files match the Windows baseline byte for byte
  (`Challenges.sam`, `Advisor.sam`, `Standard.sam`/`Online_Standard.sam` and
  every theme's `global.sam`/`Standard.sam`/`Online_Standard.sam`, jungle
  `Easy_Standard.sam` and `Easymode.TPWI`), except fantasy `Standard.sam`, which
  equals the **Patch 2** version (a single `ThemeEngine.AmbientLightLevel`
  value). Patch 2 equals the baseline for the other 17.
- Mac strings are American English (*janitor*, *researcher*, *line*, "Click"
  instead of "Left-click"). `TAG_SYSTEM` (332), `UIHELPTEXT` (589),
  `THEMENAMES`, `STAFFSTATES` keep Windows indices. **`UITEXT` has one extra Mac
  entry at index 207** ("cannot load this park … new rides … not yet
  downloaded"); Mac index = Windows index + 1 from 207 on (e.g. Game Mode /
  Instant Action / Full Simulation are Mac 240–242, Windows 239–241; the
  Instant Action completion text is Mac 471, Windows 470). Any Mac UITEXT
  constant must be shifted before it is compared with OpenTPW's Windows
  indices.
- Consequently the rules below operate on PC-identical data, but whether
  `TP.EXE` (baseline or Patch 2) implements identical predicates is **not**
  established. The Mac executable also differs functionally in at least one
  documented respect (strikes, below).

## Verified facts

Confidence: **high** = operands and control flow checked by the verifier and
reviewed manually; **medium** = depends on a stated layout or semantic
inference; values in parentheses are the shipped `.sam` data.

### Game modes (high)

A global *GameType* object (data `0x53d98`) has three values:

| GameType | Meaning (evidence) |
| ---: | --- |
| 0 | Offline Full Simulation: set on player load when `mEasyModeUser` is 0. |
| 1 | Online: balance loader uses `data:levels:online_Standard.sam` and the theme `online_Standard.sam`; player load never overrides type 1. |
| 2 | Instant Action ("easy user"): set on player load when `mEasyModeUser` is 1; balance loader adds the theme file with the `Easy_` prefix (`%s:%s%s`, missing file is non-fatal). |

- Startup flag bits `0x02000000/0x01000000/0x00800000` select types 2/1/0;
  `SetGameType` (`0x12bbf4`) stores the value and mirrors it into one bit.
- The `Easy_` prefix comes from the path object at data `0x1577c0`, string
  member at +17020 constructed from `Easy_`; the loader reads +17028. That +8 is
  the string's character pointer is an **inference** about the imported
  `TbDynamicStringTemplate<char>` layout.
- Player creation (`0x15cf00`): easy flag = (selected control ID == 1805; the
  other option is 1804). `CreatePlayer` (`0x13741c`) stores the flag in the
  24-byte player slot (+20), recreates the player save info and sets
  `mEasyModeUser`. Four player slots exist. The 1805→"Instant Action" label
  binding is inferred from the flag's use (the UI layout record was not
  traced); the Mac manual (printed p. 4) places the style choice in player
  creation and marks Instant Action players with a joystick icon.
- Instant Action player creation copies each theme's `levels:<theme>:easymode.TPWI`
  into the player's directory when it exists (`0x137600`, `LbFile_Exists` then
  `LbFile_Copy`). Only jungle ships one.

### Player-global progression record (high)

A 76-byte singleton (pointer at data `0x120d84`) is reset by `0x1287d4` and
serialized by `0x129308`, which binds names to members:

| Member | Offset | Reset |
| --- | ---: | ---: |
| `mEarnedGlobalTicket[i]` (4 bytes) | 24–27 | 0 |
| `mSpentTickets` (word) | 28 | 0 |
| `mExtraKeys` (word) | 32 | 0 |
| `mEasyModeUser` (byte) | 36 | 0 |
| `mSwearFilterOn` (byte) | 37 | 1 |
| `mEarnedSecretTicket[i]` (2 bytes) | 38–39 | 0 |
| `mFirstTimePlayer` (byte) | 72 | 1 |

Also present: a per-player set of purchased mystery item IDs (+40, serialized
as `rideId`), a per-theme record list (iterated from +64 to the +56
sentinel) and a current-theme pointer (+68). Per-theme records hold six
local-ticket bytes (0–5), four "global ticket held here" bytes (6–9) with recorded values at 12+4·i, the lazily
loaded `global.sam` object (+28) and `mAllResearchCompleted` (+185); the
per-theme serializer names (`mEarnedLocalTicket[i]`, `mAward[i]`,
`mAwardScore[i]`, `mSignNameA/B[i]`, `mNameChanged`, `mAllResearchCompleted`)
were seen but their offsets were not individually bound.

### Tickets and keys (high)

- Earned tickets = global (4 player-wide bytes) + Σ over themes of local
  tickets (6 per theme) + secret (2 player-wide bytes). Global and secret
  tickets are therefore **player-wide, earnable once**; only local tickets are
  per theme.
- **Keys = `mExtraKeys` + trunc(earned / 3)** (`0x128b60`, multiply-high by
  `0x55555556`). No starting constant is added.
- Available tickets = earned − `mSpentTickets` (`0x128a2c`). Spending never
  touches keys, matching both manuals.
- Mystery purchase (`0xd3000`): GameType 1 allows it without tickets; otherwise
  it requires catalog cost (catalog record +196) ≤ available tickets, then adds
  the item ID to the player-wide set and adds the cost to `mSpentTickets` once
  per ID (`0x128ca8`; a repeated ID adds nothing). The Mac Easy Guide (printed
  p. 8) describes tickets/keys as a global currency and uncovered items as
  staying available.
- A newly earned ticket returns an award code (`0x129cdc`): 1 = ticket only; 2 =
  ticket and key (earned is a multiple of 3 and keys > 0); 3 = ticket, key and
  "park" — some theme in the player's theme list has `CostToEnter` **equal** to
  the new key count; global
  awards also return 4 = "moved here" when a better value moves a player-wide
  global ticket to the current theme (`0x128de4`).
- The only `mExtraKeys` writers found are reset, deserialization and the single
  increment `0x128f3c` (sole caller `0x15ce80`). No Keys() consumer writes it.

### Theme entry and initial key (high, one residual)

- Lobby enter (`0x964c4`): GameType 2 enters **without any key check**;
  otherwise it enters iff `CostToEnter(theme) <= Keys()`. Nothing is
  decremented: **keys are not consumed by entering a theme.**
- `CostToEnter` is the third int of the 28-byte per-theme `global.sam` object
  (+20; −1 if unloaded); path `data:levels:%s:global.sam`. Data: jungle 1,
  hallow 1, fantasy 3, space 5; all themes earn and spend tickets.
- **Initial key:** creating a player sets a "new player" flag (data
  `0x134db0`); front-end init sets it to 1 when no player exists and to 0
  otherwise. On lobby entry with the flag set (`0x15cd38`), GameType 2 queues
  lobby ID 394; any other type does `mExtraKeys++`, saves, and queues 393. So a
  new Full Simulation player has 1 key (jungle and hallow open), matching the
  Mac manual's two initially available worlds (printed p. 6); an Instant Action
  player has 0 keys but ignores key costs.
- Residual: no clear of the flag was found inside `0x15cd38`; whether a second
  `0x15cd38(0)` call can occur before front-end init re-evaluates it in the same
  session (repeat award) is unproven.

### Golden-ticket checks (high; offsets medium-high)

- Run **only for GameType 0** (`0xd2f1c`), whenever the world tick counter
  (world+0x1E0000−22772, zeroed at world init) is divisible by 100 (`0xd67f0`).
  Neither Instant Action nor online play earns tickets.
- All tests are **strictly greater than** the threshold and skip already earned
  tickets (balance object data `0x54860`):

| Ticket | Predicate |
| --- | --- |
| Local 0 Visitors | `0xc3b7c(park)` > [1872] (100) |
| Local 1 People in park | `0xc3684(park)` > [1876] (200) |
| Local 2 Happiness | float `0xc19e4(park,0)` > [1880] (75) **and** `0xc3684(park)` > [1884] (150) — the second operand is the same people-in-park count, not a count of happy people |
| Local 3 All researched and built | `0xc5510(park)` true |
| Local 4 Profit year | `0xccfa8(…)` > [1888] (15000) |
| Local 5 Recent visitors | `0xc3b88(park, [1896]=6)` > [1892] (350) **and** park age in 30-day months > [1896] |
| Global 0–2 | coaster height / go-kart excitement / water length statistics > [1900]/[1904]/[1908] (105/90/50); the value is stored per theme |
| Global 3 Big park | count of 128×128 cells of types {4, 9, 10, 21} or object-occupied > **[1916]** |
| Secret 0 Cameras | coverage % == 100, over cells whose type ∉ {2, 7, 30} |

- Offsets map to `GoldenTicketLocal`/`GoldenTicketGlobal` schema order
  (Visitors…RecentVisitorMonths at 1872–1896; CoasterHeight, GokartExcitement,
  WaterLength, MinCellsOwned, MinCellsCovered at 1900–1916). Anchors for the
  sequential 4-byte layout: the 28-byte `global.sam` object with `CostToEnter`
  at +20; the seven local and four used global fields; the `Challenges.*`
  fields 1920–1932 read only by challenge code (1928 = DaysUntilFirstChallenge
  below); `ResearcherConstsPerGrade` stride 12. Under that layout the big-park
  ticket reads **MinCellsCovered (2000)**, `MinCellsOwned` (3000) has **no
  non-stack displacement read anywhere**, and the camera ticket ignores both.
- Not established: semantics of the statistic functions (`0xc3b7c`, `0xc19e4`,
  `0xc5510`, `0xccfa8`, `0xc3b88`, the coaster/kart/water functions), the cell
  type enumeration, and how secret ticket 1 (all land) is awarded (no caller in
  this routine; likely the land-purchase path).

### Challenges activation (high)

`0xcfef4`: challenges run **only for GameType 0**. They switch on (persisted
`mChallengeOn`, +984) once a park-open style predicate `0xc3524` holds and park
age in days ≥ `DaysUntilFirstChallenge` ([1928], 540). The day divisor is
864,000,000,000 in the 100 ns time-difference unit = 86,400 s.

### Strikes (high for the code; reachability unresolved)

- Considered on `CMsgEndOfMonth` (message type 12; 11 = end of day, 13 = end of
  year, via vtable → transition vector → `GetType`), for each of the five staff
  types once per tick: a type that is **currently striking stops striking**;
  otherwise `UpdateStrike` runs.
- Skipped while the park is closed (`0x1091ac` ≠ 0), and also whenever the
  cached count `0xc1fb4(park, 5)` is non-zero (both print the same "park is
  closed" debug line). The meaning of that count is unresolved; it may make the
  path rare or unreachable in normal play.
- No escalation before park age 24 thirty-day months.
- Grievance (`0xf8410`): forced flag (+100, debug "Forcing strike"), or more than
  3 staff of the type with average fatigue (+504) < 15 ("striking through
  fatigue") or average happiness (+500) < 15 ("striking through unhappiness");
  handymen also when the ratio `0xc2264/0xc21f4` > 0.2 (debug text pairs it
  with "Clean your park up").
- Per-type level (12-byte records at +36): with grievance 0→1 warning (message
  kind 0), 1→2, 2→3, 3→4, 4 stays 4, each of these starting a strike (striking
  flag +40, kind 2). Debug texts label the strikes one week / two weeks / a
  month, but the only strike-ending sites are the next month-end consideration
  and a closed park with zero visitors (`0xf3f64`); **no week timer exists** in
  this binary. Without grievance the level returns to 0, emitting "called off"
  (kind 3) only from level 1.
- **Documentary conflict:** the Mac Easy Guide FAQ (printed p. 8) says the
  strike feature was removed and only fatigue remains, while the Windows manual
  (PDF 21/22, printed 40–43) and Mac manual (printed pp. 11, 16) describe
  strikes; `TAG_SYSTEM` 38–62 and UITEXT strike labels still exist. Static code
  cannot settle whether the gate above made strikes unreachable on the Mac;
  this must not be generalized to PC.

### Research (high; offsets medium-high)

- Points come **only** from researcher staff: `0xf0728` (sole caller `0xf02e4`,
  the researcher work cycle) adds `ResearcherConstsPerGrade[grade].ResearchAbility`
  (balance 1052 + 12·grade); `0xf0df0` is the only point sink and has that one
  caller. There is **no staffless Instant Action research path**. Instant Action
  data instead raise ResearchAbility (6/9/12/16/20) and the Instant Action
  park ships a researcher (manuals: one scientist present).
- Allocation per contribution: `points × effort[c] / Σeffort × lab[5216] / 100`
  to the current item of each category with effort (lab efforts at
  5132+16·c). `lab[5216]` is presumably the workload (`Research.StartingWorkLoad`
  85); its initialization was not traced.
- Group opening (`0xf15b4`): for category c with current group g (lab byte
  5144+16·c), compute 100·researched/total over **all items of c with group ≤
  g**; while that is ≥ balance [1280+4·g] and g < 7, open g+1 (repeatable).
  The 8-entry per-group array is identified as `ResearchTech[].PercentageForThisTech`
  (0, 0, 80, 85, 85 shipped) by context; its exact base offset is not
  layout-proven. Unspecified entries beyond index 4 would open immediately if
  they default to 0 (parser defaults unverified).
- Completion: when all research is done the current theme's
  `mAllResearchCompleted` (+185) is set; GameType 2 then queues advisor ID 167
  and calls `0x105b50` (Instant Action finished); GameType 0 queues 167 only once
  every theme's flag is set. The UITEXT congratulation for finishing Instant
  Action (Mac 471) is a candidate text for 167, not proven.

### Time units (medium)

The ticket, strike and challenge checks use one park-age function `0xe4628`:
age = calendar[+28] × worldTicks / 4 seconds, expressed as a 64-bit time
difference in 100 ns units (×10,000,000), then divided by 30 days or 1 day. The
calendar multiplier and the start date were not traced; this is a lead for the
calendar lane, not a resolution of `ECON-001..004`.

## Register impact (proposals; registers not edited here)

| ID / area | Current OpenTPW assumption | Mac binary evidence | Suggested state |
| --- | --- | --- | --- |
| ECON-040 | 1 starting key; keys not consumed | Not consumed: proven. Start: Full Sim gets `mExtraKeys` 1 on first lobby entry; Instant Action 0 keys but no key check | Mac-resolved; PC confirmation and repeat-award residual remain |
| `PlayerProgress.Keys` (untagged) | 1 + Σ per-theme earned / 3 | `mExtraKeys` + (player-wide globals + per-theme locals + player-wide secrets) / 3 | untagged divergence: global/secret tickets are once per player, not per theme |
| ECON-029 | tickets spent on GoldenTicketCost purchases | spent once per item ID into a player-wide set; online free | partial support; item set persistence is missing |
| ECON-033 | checked at month end | every 100 world ticks, GameType 0 only | contradicted (tick scale still open) |
| ECON-038 | big park = MinCellsOwned, cameras = MinCellsCovered | big park reads offset 1916 (MinCellsCovered under the anchored layout); cameras need 100 % coverage; MinCellsOwned unread | contradicted (medium-high) |
| Ticket predicates (untagged) | `>=`; happiness counts happy guests | strict `>`; happiness second test uses people in park; RecentVisitors also needs park age > N months | untagged divergence |
| ECON-039 | profit of last 12 months | value from `0xccfa8` | unresolved |
| ECON-034..036 | challenge semantics/flow | challenges only in GameType 0; first offer at park-age day ≥ DaysUntilFirstChallenge | only activation resolved |
| Challenges/tickets in Instant Action (untagged) | `ParkEconomy` runs both in every mode | both disabled for GameType ≠ 0 | untagged divergence |
| ECON-014 | no strikes, happiness constant | strike state machine present but gated; Mac guide says removed | conflict; requires PC/runtime evidence |
| ECON-015 | ResearchAbility per day split by effort | per researcher work cycle, × effort share × lab[5216]/100 | partially contradicted (cycle timing open) |
| ECON-016 | group g opens at % of group g−1 | g+1 opens at cumulative % of groups ≤ g against threshold[g] | contradicted (offset medium-high) |
| ECON-017 | cheapest first | item choice not traced | unresolved |
| ECON-019 | Instant Action: virtual grade-2 researcher | no staffless path; researcher staff only | contradicted |
| UI-015 | mode asked per park; modes identical | mode fixed at player creation (4 slots); modes differ in balance overlay, start park, keys, tickets, challenges, research completion | contradicted |
| ECON-030 / bankruptcy | stops when bankrupt | not traced (TAG 123–127 describe six months in the red) | unresolved |
| ECON-046 | upgrades need a mechanic | not traced (only the TAG text "can't upgrade during a mechanics' strike") | unresolved |

## Unresolved dependencies (explicit blockers)

- **Advisor IDs** (167, 215–217, 220–222, 393, 394, 398): map the advisor-rule
  table (Advisor schema at data ~`0x234dc`) to rule names/speech; required to
  bind completion and lobby messages.
- **Statistic semantics**: `0xc3b7c`, `0xc3684`, `0xc19e4`, `0xc5510`, `0xccfa8`,
  `0xc3b88`, `0xc73bc`, `0xc6cec`, `0xc710c`, `0xc2264/0xc21f4`, `0xc1fb4`,
  `0xc3524`, cell types at cell +8, staff floats +500/+504 ranges.
- **Full balance layout**: the parser's per-type sizes and array counts (type
  codes 3–8, 10) to prove offsets outside the anchored regions (1280 base,
  5216 workload source).
- **Calendar**: calendar object world+672 (+28 multiplier, base date) for tick
  → day conversion.
- **Flows not traced**: staff hiring/firing/termination costs and the dismiss
  path, staff fatigue recovery, bankruptcy counter, secret "all land" award,
  research item selection order, Instant Action end (`0x105b50`) consequences,
  new-player flag lifetime.
- **PC equivalence**: none of these predicates were checked in `TP.EXE` or
  Patch 2; the Mac/PC strike conflict shows behaviour can differ even with
  identical data.

## Limits

Instruction-field checks prove what the selected instructions encode, not that a
path is executed at runtime. Function boundaries were derived from prologues and
returns around referenced sites, not from symbols. Manual statements are
player-facing descriptions. No gameplay code, shared tool or register was
changed by this lane.
