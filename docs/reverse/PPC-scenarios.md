# PowerPC scenario, progression and staffing evidence

2026-10-09 (two follow-up passes the same day). Scenario lane of the nine-lane PowerPC continuation. Static
inspection of the Feral Interactive Mac port of *SimTheme Park* (Theme Park
World); the original program was never run. No original bytes, disassembly,
extracted assets or manual text are stored here. A local disassembler
(`llvm-mc`) was used for review only. The checked-in tools are standard library
only and print interpreted operands, offsets and identifiers.

Scope: game modes and player creation, golden tickets and keys, theme entry,
challenge activation, research progression, strikes; follow-up: new-player key
lifetime, GameType versus front-end/main-loop state, advisor rule/response
tables, staff wages/dismissal/training/rest, bankruptcy, profit year and the
complete `Standard.sam` (CMainBalance) field binding; second follow-up: player-window
lifetime (new-player key closure), map-cell types and guest statistics, secret
tickets, "all researched and built", mystery-item placement, Instant Action UI
gates, a manual-versus-code table and a typed contract with synthetic boundary
tests (*Progression contract*). Every finding below is a
fact about **this Mac binary** unless explicitly stated otherwise. It is not
evidence for the PC `TP.EXE` or Patch 2 runtime (see *Mac and PC relationship*).

## Reproduce

```sh
# instruction-field witnesses (874 checks, identity-pinned; follow-up checks live in
# followup_evidence.py and progression_evidence.py, included in the same JSON report)
python3 -I tools/ppc-analysis/lanes/scenarios/scenario_evidence.py /Users/sander/server/game-assets/mac-feral/bin
# tests (synthetic fixtures; the two corpus cases, including in-memory mutation
# regressions, run only with OPENTPW_MAC_BIN set)
python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
OPENTPW_MAC_BIN=/Users/sander/server/game-assets/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios
# typed contract, synthetic boundaries only (23 cases)
dotnet run --project tools/ppc-analysis/lanes/scenarios/contract/OriginalProgressionContract.Tests.csproj --configuration Release
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
60-byte records `[type][48-byte name][count][spare]`; the follow-up pass
derived every field offset from the parser itself (see *Balance field binding*)
and checks the result against 17 independent code displacements. Advisor IDs
passed to the message builder at `0xb6d8` are advisor **rule** IDs; the lobby
queue uses **response** IDs directly (see *Advisor rules and responses*).

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

### GameType versus other state values (high)

Three different enumerations meet at mode changes and must not be conflated:

| Object | Values | Writers |
| --- | --- | --- |
| GameType, data `0x53d98` (not serialized) | 0 Full Simulation, 1 online, 2 Instant Action | exactly 10 `SetGameType` call sites: lazy construction from the startup flag bits (4), player load (2, skipped while GameType is 1), online entry `0x971ac` and `0x18b0e4(0)` (→1), main-loop state 11 (2) |
| Front-end exit code, `*(data 0x84b80)` + 20 | 1 front end running (set on entry `0x8cfb0`), 2 enter a park (park chosen `0x9665c`, and both online entries), 3 quit (`0x8d698`) | read back by `0x8d3b0` |
| Main-loop state, data `0x15c488` | 0..15, 16-way jump table at data `0x52cc8`; 2 runs the front end, 3 leaves it, 9 loads a park, 11 leaves a park, 12 shuts down | `0x1c1208` |

The online entries write exit code **2 and** GameType **1** together; that is
the "2 then 1" seen at those sites, not a GameType sequence. The player save
persists only `mEasyModeUser`. Leaving a park in main state 11 while GameType
is 1 and the 1028-byte session object (data `0x120da8`) has +1008 == 0 restores
GameType from `mEasyModeUser` (2 or 0). Front-end exit code 2 → main state 9,
anything else → 12.

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

### Theme entry and initial key (high)

- Lobby enter (`0x964c4`): GameType 2 enters **without any key check**;
  otherwise it enters iff the theme record is found and the signed
  `CostToEnter(theme) <= Keys()`. Nothing is
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
- Lifetime (follow-up, high): the flag has one relocated reference (TOC slot
  `0x1cc4`) used at exactly three sites. The only writers are front-end init
  (`0x15c828`: 1 when none of the 4 slots has a name, else 0) and creation
  (`0x15d164`: 1); the award routine only reads it and **never clears it**. The
  award runs only for argument 0 with a loaded player (+96 ≠ −1). Its four
  callers: creation (`0x15d178`), front-end message 5 (`0x15be5c`), slot
  selection after loading the slot (`0x15c068`) and the quit handler (`0x15c1c8`,
  argument 1, no award). Front-end init runs from the front-end entry only
  when no player is loaded, and from the return path `0x1979a8`, which first
  unloads the player (+96 = −1) and then re-runs init (flag = 0, since a player
  now exists).
- Closure (second follow-up, high for synchronous dispatch): every call of the
  award routine first sends message 4 to the player window (`0x15cd88`, before
  the argument test and the flag read). The window is an `InterfaceWindow`
  (vtable `0x4808c`); its default procedure `0x1813d0` handles message 4 with
  the virtual deleting destructor (+16, flag 1) and message 5 by re-sending 4.
  The destructor destroys a live window (`0x17e0b8`): message 20 to itself,
  then byte +0 = 1, after which the sender `0x170f98` returns −1 for it. The
  player-window handler clears the global pointer on message 20. Message 5 and
  slot selection are handlers of that window and its slot children; creation
  needs its dialog, which `0x15d220` refuses to open while the pointer is null.
  The pointer's only non-zero writer is front-end init (`0x15c888`), which
  recomputes the flag (0 once a slot is named). Creation loads the new slot
  (+96) before its award, so the first award sees a loaded player. A repeat
  award is therefore impossible without passing front-end init. Not modelled:
  queued input addressed to an already deleted window (the destroyed byte
  rejects it only while the memory is not reused).

### Golden-ticket checks (high)

- Run **only for GameType 0** (`0xd2f1c`), whenever the world tick counter
  world+0x1E0000−22772 is an unsigned multiple of 100 (`0xd67f0`, `mulhwu`).
  Correction (independent review, round 3): that counter is `mGameTick`
  (incremented at `0x1053a0`) and is restored from saves, not only zeroed at
  world init. Neither Instant Action nor online play earns tickets.
- All tests are **strictly greater than** the threshold and skip already earned
  tickets (balance object data `0x54860`):

| Ticket | Predicate |
| --- | --- |
| Local 0 Visitors | `0xc3b7c(park)` (visitor counter park+0x21C08) > [1872] (100) |
| Local 1 People in park | `0xc3684` (guests in the park, below) > [1876] (200) |
| Local 2 Happiness | float `0xc19e4` (mean guest happiness) > (float)[1880] (75) **and** the same `0xc3684` guest count > [1884] `AtLeastThisManyHappyPeople` (150) — despite the key name, nobody is filtered by happiness |
| Local 3 All researched and built | `0xc5510(park)` true |
| Local 4 Profit year | finance +292 > [1888] (15000); +292 = credits − debits since the last `CMsgEndOfYear` (13 zeroes it), i.e. **year to date**, not a rolling 12 months |
| Local 5 Recent visitors | `0xc3b88(park, [1896]=6)` = counter − the history value [1896] months back (0 when the history lookup fails) > [1892] (350) **and** park age in 30-day months (low word of the 64-bit quotient) > [1896] |
| Global 0–2 | coaster height / go-kart excitement / water length statistics > [1900]/[1904]/[1908] (105/90/50); the value is stored per theme |
| Global 3 Big park | count of 128×128 cells of types {4, 9, 10, 21} or object-occupied > **[1916]** |
| Secret 0 Cameras | coverage % == 100, over cells whose type ∉ {2, 7, 30} |
| Secret 1 Own all land | **never awarded** (below) |

- Offsets map to `GoldenTicketLocal`/`GoldenTicketGlobal` schema order
  (Visitors…RecentVisitorMonths at 1872–1896; CoasterHeight, GokartExcitement,
  WaterLength, MinCellsOwned, MinCellsCovered at 1900–1916). Anchors for the
  sequential 4-byte layout: the 28-byte `global.sam` object with `CostToEnter`
  at +20; the seven local and four used global fields; the `Challenges.*`
  fields 1920–1932 read only by challenge code (1928 = DaysUntilFirstChallenge
  below); `ResearcherConstsPerGrade` stride 12. Under that layout the big-park
  ticket reads **MinCellsCovered (2000)**, `MinCellsOwned` (3000) has **no
  non-stack displacement read anywhere**, and the camera ticket ignores both.
- Follow-up: the complete balance binding (below) now **proves** these offsets
  from the parser: `MinCellsOwned` = 1912 and `MinCellsCovered` = 1916, so the
  big-park ticket reads `MinCellsCovered` (high).
- Award announcements (rule IDs, below) carry `CMsgTag` ids 102–107 for local
  tickets 0–5 and 108–111 for global tickets 0–3. Their order matches
  `TAG_SYSTEM` 180–189 exactly (visitors, people, happiness, all researched and
  built, profit "$", recent visitors; coaster height, go-kart, water ride, "a
  really big park"); 190/191 are the secret texts (park covered by security
  cameras; own all the possible land). The id→text table itself was not traced.
- Second follow-up (high unless marked): the statistic functions `0xc3684`,
  `0xc19e4`, `0xc5510` and `0xc3b88` and the cell types are bound in *Cell
  types and guest statistics* and *Secret tickets and "all researched and
  built"*. Correction: the callbacks at data `0x8a70`–`0x8ad8` write +8 of a
  **stack-local command object**, not of a map cell, so they say nothing about
  cell types; the cell word at +8 is the serialized `mType`. Still not
  established: the coaster/kart/water statistics (`0xc73bc`, `0xc6cec`,
  `0xc710c`), the big-park cell count `0xc8cc0` beyond its type set, and the
  camera coverage function `0xc8b08`.

### Cell types and guest statistics (high; names medium)

- The map-cell word at +8 is `mType`: the base-cell serializer `0xcd408`
  binds the name (string pool `0x1ccd1a` + 80) to cell +8. Cells are 68 bytes,
  indexed y·128 + x (`0xe6c6c`).
- `0xc3684` (ticket 1, ticket 2's second test, the park status panel, strike
  ending in a closed park) ignores its argument and counts, over the global
  thing list, things with class byte +2 = 1 standing on a cell of type
  **0, 1, 3, 9 or 10** (`0xe6c6c`: five predicates `0x85190`–`0x85258`).
- Class 1 is the guest class: staff use classes 5 handyman, 4 mechanic,
  6 entertainer, 7 guard and 8 researcher (wage type selection `0xf46d8`),
  and +412 on class-1 things is the happiness that the shop/sideshow routine
  `0xeaaf8` writes and logs ("Sideshow won - happiness up %d points to %d").
- `0xc19e4` returns 0.0 while the park is closed (`0xc3524` = world field
  −22768 == 0, the field the strike code reads as "closed"), otherwise the
  mean of `(u8)trunc(happiness)` over the same guests. The happiness ticket
  compares it with the signed threshold converted to float.
- Type names are **correlated, not code-proven**: in the jungle
  `Easymode.TPWI` cell grid (formats lane parser) type 1 occupies exactly the
  78 path cells, 2 the 240 water cells, 3 the four Bouncy queue cells
  (49–52, 22), 4 35 object-footprint cells (all with a parent), 9 eight
  entrance cells, 10 the Bouncy exit (52, 26), 7 the 9,077 cells outside the
  park (the whole 128×128 range), 0 6,875 empty park cells, and 30 66 cells
  of the approach road and car park outside the gate. Type 21 does not occur
  in that save.
- Consequences: "people in park" counts guests on empty land, paths, queues,
  entrances and exits, not those on object footprints (type 4: rides, shops
  and other placed objects), on water or outside the park. The camera secret
  ignores water, outside and approach cells; the big-park ticket counts object
  footprints, entrances, exits, type 21 or any object-occupied cell.

### Secret tickets and "all researched and built" (high)

- The secret award routine `0xd381c` has one caller, `0xd3640`, which passes
  index 0 after the camera coverage `0xc8b08` equals 100. The setter
  `0x128efc` (`lbzu +38+i`, set, award code) has that routine as its only
  caller, and the only D-form byte stores with displacement 38/39 in the
  progression code range `0x128000`–`0x12a400` are the reset. Secret ticket 1 ("own all the possible land", TAG_SYSTEM 191)
  is therefore **never awarded** by the Mac binary; it still counts toward
  keys if a loaded save has it set.
- Local ticket 3 (`0xc5510`): false while any lab item is unresearched
  (`0xf151c(lab, 5)`, all categories; researched = progress +16 ≥ cost +12,
  floats, progress accumulated at `0xf2370`). Then every catalog item with
  kind +1960 ≠ 4 and ticket cost +196 = 0 must have a non-zero park record
  +24. Candidate binding (unverified): +1960 is `Info.WhichUIType`, whose 4
  means "not shown in UI" (RIDES-017), and record +24 a built flag/count.

### Mystery items (high)

- Placement (`0xda874`): when the catalog ticket cost +196 is > 0 and the item
  id is **not** in the player-wide set (`0xd30d0` → `0x128d14`), the ticket
  purchase `0xd3000` runs and no money is debited. Otherwise, including every
  later copy of an uncovered item, the money price (catalog +440) is debited
  through `0xcbfdc`. This matches the Mac Easy Guide: an uncovered item "is
  available to buy for cash, even if you load a previously saved park".
- `0xd3000`: GameType 1 returns success without recording the id, so online
  placements of mystery items are free and stay free. Otherwise signed
  cost ≤ earned − spent, then the id is inserted and the cost added to
  `mSpentTickets` once. The placement routine **ignores** the purchase
  result (`0xdacfc` branches on), so affordability must be checked before
  placement; that check was not traced.
- Instant Action players earn no tickets (ticket checks need GameType 0), so
  they cannot uncover mystery items that cost tickets.

### Instant Action availability (high for the gates)

Every gate below is a direct GameType test (`== 2`, or `== 0` for tickets and
challenges). Mac UITEXT indices from 207 on are Windows + 1.

| Feature | Instant Action behaviour | Evidence |
| --- | --- | --- |
| Golden tickets, challenges | not checked | `0xd2f1c`, `0xcfef4` (GameType 0 only) |
| Theme entry | no key test | `0x964c4` |
| Research panel | not opened; message "Research is automatic in Instant Action mode." (Mac 468 / Windows 467); otherwise "hire some researchers" (Mac 467 / Windows 466) under a condition not traced | `0x161910` |
| Research points | researcher staff only, as in Full Simulation | `0xf0728` (see *Research*) |
| Bank panel ("Available Loans", UITEXT 170) | not opened | `0x154aa0` |
| Loan buttons | disabled (virtual +28 `0x17fb64` with 0 sets the disabled bit 0x2 of +68 and notifies message 23) on three panels: controls 324524, 74367, 733 | `0x14eeec`, `0x15161c`, `0x1698e8` |
| Ride upgrades | list skipped; "Upgrades are not available in Instant Action mode" (UITEXT 27) | `0x165a0c` |
| Completion | UITEXT 471 (Windows 470) "now you've completed Instant Action mode…" | `0x13d708` (pointer-dispatched) |
| Balance overlay | `Easy_` theme file added | `0x10474c` |

The three disabled controls carry help texts 196, 219 and 174 ("Click to take
out or repay loans (L)") in their layout records. This binding comes from the
UI lane's layout decoder (`ppc-ui` `corpus.layout_table`), read-only, and is
not re-verified by this lane's witness. Not interpreted: lobby callbacks
`0x966f8`/`0x9677c` and `0x183cc0`/`0x183dd0`/`0x184028` also skip work under
GameType 2 (probably theme navigation and lobby key display). The advisor rule
filters `0xe128`/`0xe4ec` accept every rule under GameType 0, only rules whose
+12 word is 1 under GameType 2, and none online (advisor lane to interpret).

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

### Research (high)

- Points come **only** from researcher staff: `0xf0728` (sole caller `0xf02e4`,
  the researcher work cycle) adds `ResearcherConstsPerGrade[grade].ResearchAbility`
  (balance 1052 + 12·grade); `0xf0df0` is the only point sink and has that one
  caller. There is **no staffless Instant Action research path**. Instant Action
  data instead raise ResearchAbility (6/9/12/16/20) and the Instant Action
  park ships a researcher (manuals: one scientist present). The research panel
  is refused under GameType 2 (*Instant Action availability*); presumably the
  efforts and the workload therefore keep their initial values, but whether
  the workload setter's caller `0x1610b4` is reachable only through that panel
  was not traced.
- Allocation per contribution: `points × effort[c] / Σeffort × lab[5216] / 100`
  to the current item of each category with effort (lab efforts at
  5132+16·c). `lab[5216]` is the workload: park init (`0xf0854`) copies
  `Research.StartingWorkLoad` (balance 1344, shipped 85) into it and the research
  panel setter (`0xf130c`, sole caller `0x1610b4`) stores a player value clamped
  to ≤ 100 (high).
- Group opening (`0xf15b4`): for category c with current group g (lab byte
  5144+16·c), compute 100·researched/total over **all items of c with group ≤
  g**; while that is ≥ balance [1280+4·g] and g < 7, open g+1 (repeatable).
  Layout-proven (follow-up): `ResearchTech[]` has 10 entries at 1276 + 4·i, so
  [1280+4·g] is **`ResearchTech[g+1].PercentageForThisTech`** — group g+1 opens
  at its own percentage (shipped 0, 0, 80, 85, 85 for groups 0–4). Unspecified
  entries 5–9 would open immediately if the parser defaults them to 0
  (defaults unverified).
- Completion: when all research is done the current theme's
  `mAllResearchCompleted` (+185) is set; GameType 2 then queues advisor ID 167
  and calls `0x105b50` (Instant Action finished); GameType 0 queues 167 only once
  every theme's flag is set. The UITEXT congratulation for finishing Instant
  Action (Mac 471) is shown under GameType 2 by the pointer-dispatched handler
  `0x13d708`; its link to rule 167 or `0x105b50` was not traced.

### Advisor rules and responses (high; text binding partial)

- Response table, data `0x18ff4`: 610 records of 32 bytes terminated by id
  9999; +0 response id, +8 speech sample (`:Speech:lips:sp_%03d.lip`, the same
  numbers name `sp_NNN.mp2` in the speech bank), +28 a `CMsgTag` id
  (`0x7274`; 383 = responses without an on-screen tag).
- Rule table, data `0x1f2b4`: 351 records of 48 bytes with consecutive ids;
  +32 first response, +36 variant count (`0xd468`/`0xd5ec`). In-game messages
  (`0xb6d8`/`0xb798`) pass a **rule** id; response = first + variant.
- Resolved ids:

| Id | Kind | Meaning (from the call site) | Responses → samples |
| ---: | --- | --- | --- |
| 167 | rule | all research complete (Instant Action: current theme; offline: every theme) | 380–381 → sp_452/453 |
| 215 / 216 / 217 | rule | local ticket won: award code 1 / 2 (+key) / 3 (+key +new theme); variant = local ticket 0–5 | 434–439 / 446–451 / 458–463 |
| 220 / 221 / 222 | rule | global ticket won, same award codes; variant = global ticket 0–3 | 440–443 / 452–455 / 464–467 |
| 234–237 | rule | global ticket 0–3 "moved here" (award code 4) | 474–477 |
| 390 / 391 | response | front-end init, no named player | sp_465 / sp_466 |
| 398 | response | front-end init, players exist | sp_471 |
| 393 | response | first lobby entry, Full Simulation (extra key) | sp_468 |
| 394 | response | first lobby entry, Instant Action | sp_469 |

  The spoken wording was not transcribed, and the `CMsgTag` id → `TAG_SYSTEM`
  table was not traced (ids are not direct indices; see the ticket ordering
  note above).

### Staff wages, dismissal, training and rest (high)

- Wage (`0xf46bc`) = `PerTypeStaffConsts[t].PayMultiplier` (balance 832+4t) ×
  `PerGradeStaffConsts[g].BaseWage` (748+16g); type index handyman 0, mechanic 1,
  entertainer 2, guard 3, researcher 4 (other 5), selected from the thing
  class byte +2 (5, 4, 6, 7, 8; anything else is generic, with a debug line);
  the product is a low-32-bit `mullw`. Shipped: 10/30/15/20/35 ×
  4/5/6/8/12, e.g. a grade-0 handyman costs 40 and a grade-4 researcher 420.
- Paid per staff member on `CMsgEndOfMonth` (12) through the debit routine
  `0xcbfdc` and posted to the wage statistic.
- Dismissal (`0xf33ec`, callers `0x16e0d8`/`0x16e174`): optional "DISMISS
  EMPLOYEE" confirmation (Mac UITEXT 397 = Windows 396, shown when preference
  byte +55 is set), then **one further wage is debited**, the member enters
  state 19 and is removed.
- Hiring: none of the 16 direct callers of the debit routine is a hire path
  (three are staff: month-end wage, dismissal, training; the rest are
  construction/purchase). A hire fee by another money path is not excluded.
- Training (`0xf3588`): grade 4 cannot train; the paid amount is debited and
  converted to `amount / PoundsPerTrainingPoint[type][grade]` points (cap 100 per
  payment) added to the training byte +488; ≥ 100 raises the grade, keeps the
  remainder and **resets happiness to 100**.
- Rest (`0xf3d8c`): energy +504 += `RecuperationRate[grade]` (0.2…0.75),
  happiness +500 += `HappinessRecuperationRate[grade]` (1…3), each clamped to
  0…100; work resumes at energy 100. Work (`0xf44c0`): energy −= 0.012·(6 −
  grade), happiness −= 0.005·(6 − grade), clamped. +504 is therefore energy
  (high = rested); the strike test's "fatigue" average < 15 means low energy.
  The step cadence (world ticks per call) is not established.

### Finance, bankruptcy and profit year (high)

- Debit `0xcbfdc`: money (+12) −= amount; when the balance first goes negative
  the world tick is stored at +288 (in-the-red start); the amount is posted to an
  expense statistic and subtracted from the year accumulator +292.
- Month end (`0xcc21c`, on message 12): pending income is credited, eight
  32-byte loan slots are serviced; then if money < 0 and the time since +288 is
  ≥ 6 thirty-day months, a `CMsgEvent(2)` (GetType 19) is posted. Its handler
  calls `0x1059e0`, which sets **world mode 4**, unless the world mode is
  already 4 or `0x105c6c` reports that halfword data `0xecdcc` differs from
  halfword data `0x4491e` (meaning unresolved).
- `CMsgEvent(10)` sets money to `BankAccountInfo.InitialCash` (balance 408).
- `CMsgEndOfYear` (13) zeroes +292; the profit-year ticket compares +292.

### Balance field binding (high)

The `CMainBalance` object at data `0x54860` is constructed by `0x198fc` (vtable
`0x38fbc` at +4) and parsed by `0x16f4c`. Its schema is the 283-record table at
data `0x34d10` (`0x19950`: table + 60·i; record count = index of the first
type-12 record, `0x197a4`); values start at `this + 8` (`0x19960`). The parser's
word counter starts at **1**; types 4–11 take one word; a type-2 record opens a
flat struct array closed by a type-3 record (count at +52) that advances
(count − 1)·fields + 1 words; type 1 names the preceding scalar group; types
0/1/2 take no space. Applying this rule binds 487 qualified names (354 of the
362 keys in `Standard.sam`; the other 8 come from one multi-column header line)
and reproduces all 17 displacements that code reads from the object, e.g.
`BankAccountInfo.InitialCash` 408, `BaseWage` 748, `PayMultiplier` 832,
`ResearchAbility` 1052, `ResearchTech[1]` 1280, `StartingWorkLoad` 1344,
`GoldenTicketLocal.Visitors` 1872, `MinCellsCovered` 1916,
`DaysUntilFirstChallenge` 1928. The verifier recomputes the layout and fails
if any anchor moves (mutation-tested).

### Time units (medium)

The ticket, strike and challenge checks use one park-age function `0xe4628`:
age = calendar[+28] × worldTicks / 4 seconds, expressed as a 64-bit time
difference in 100 ns units (×10,000,000), then divided by 30 days or 1 day. The
calendar multiplier and the start date were not traced here. Cross-lane
(independent review, round 3, from the clock lane): `calendar+0x1c` is
`mFunnySecsPerRealSec` = 15000, so a game day is 23.04 ticks, a 30-day month
691.2 ticks, `DaysUntilFirstChallenge` 540 ≈ 12,442 ticks and the 100-tick
ticket check ≈ 4.34 game days, unless a `.sam` or save overrides `+0x1c`
(untraced). Not a resolution of `ECON-001..004`.

## Manual statements versus Mac code

| Manual statement | Source | Mac code | Status |
| --- | --- | --- | --- |
| Instant Action starts with a pre-built park and staff; research is automatic | Mac manual printed pp. 4, 8, 46; Windows manual research section | research panel refused with the matching text; points still come only from researcher staff (the start park ships one) | consistent; "automatic" means fixed efforts/workload, not staffless |
| "Loans are not available in Instant Action Mode." | Mac manual printed p. 49; Windows manual bank section | bank panel not opened, loan buttons disabled | consistent (UI-level) |
| Upgrades in Instant Action | not stated | upgrade list skipped with UITEXT 27 | code-only fact |
| Every third ticket gives a key; buying mystery items does not lose keys | Mac manual printed p. 28; Windows manual | keys = extra + earned/3; spending only changes `mSpentTickets` | consistent |
| Tickets/keys are a global currency; uncovered items stay buyable for cash | Mac Easy Guide printed p. 8 | player-wide record; placement charges tickets once, then money | consistent |
| Two worlds open at start | Mac manual printed p. 6 | Full Simulation first lobby entry grants `mExtraKeys` 1; jungle/hallow cost 1 | consistent |
| Strikes (warnings, strikes of one week to a month) | Windows manual pp. 40–43; Mac manual pp. 11, 16 | state machine present but gated; no week timer | **conditional**: Mac Easy Guide says strikes were removed |
| Secret ticket for owning all land | TAG_SYSTEM 191 text only | no award path | text exists, mechanic absent on the Mac |
| Golden tickets for many park qualities (no thresholds given) | Mac manual printed p. 28 | strict thresholds from `Standard.sam`, Full Simulation only | manual is non-specific |

Manual statements are player-facing descriptions; where they agree with the
Mac code that agreement is still not PC `TP.EXE` evidence.

## Progression contract

`tools/ppc-analysis/lanes/scenarios/contract/` is a dependency-free .NET 8
helper outside the production projects. It types the three separate state
variables (`OriginalGameType`, `OriginalFrontEndExitCode`, `OriginalWorldState`)
and states keys, award codes, theme entry, the ticket schedule and predicates,
cell classes, "all researched and built", challenge activation, research group
opening, mystery placement/uncovering, wages, bankruptcy, Instant Action
availability and the front-end new-player session. Every counter, statistic and
balance value is a caller argument; the 23 synthetic cases use invented
thresholds. It deliberately defines **no** phase rules or game-flow mode and is
not wired into `ParkEconomy`, `PlayerProgress` or the front end; the economy
lane's annuity placeholder is untouched. Its README lists each member's anchor
and the parts it does not model.

## Register impact (proposals; registers not edited here)

| ID / area | Current OpenTPW assumption | Mac binary evidence | Suggested state |
| --- | --- | --- | --- |
| ECON-040 | 1 starting key; keys not consumed | Not consumed: proven. Start: Full Sim gets `mExtraKeys` 1 on first lobby entry; Instant Action 0 keys but no key check; a repeat award needs front-end init, which clears the flag (player-window closure) | Mac-resolved; PC confirmation remains |
| `PlayerProgress.Keys` (untagged) | 1 + Σ per-theme earned / 3 | `mExtraKeys` + (player-wide globals + per-theme locals + player-wide secrets) / 3 | untagged divergence: global/secret tickets are once per player, not per theme |
| ECON-029 | tickets spent on GoldenTicketCost purchases | first placement of a ticket-cost item spends tickets once per ID (player-wide set), no money; later copies cost the money price; online free and unrecorded | partial support; item set persistence and the cash price after uncovering are missing |
| ECON-033 | checked at month end | every 100 `mGameTick` ticks (unsigned modulo), GameType 0 only; about 4.34 game days per check (cross-lane calendar figure) | contradicted |
| ECON-038 | big park = MinCellsOwned, cameras = MinCellsCovered | big park reads 1916 = MinCellsCovered (parser-derived layout); cameras need 100 % coverage; MinCellsOwned (1912) unread | contradicted (high) |
| Ticket predicates (untagged) | `>=`; happiness counts happy guests | strict signed `>`; people in park = guests (class 1) on cell types 0/1/3/9/10; happiness = float mean of truncated guest happiness (0 while closed) and the second test reuses the guest count; RecentVisitors also needs park age > N months | untagged divergence |
| ECON-039 | profit of last 12 closed months | year-to-date accumulator +292, zeroed on end of year, checked every 100 ticks | contradicted |
| ECON-034..036 | challenge semantics/flow | challenges only in GameType 0; first offer at park-age day ≥ DaysUntilFirstChallenge | only activation resolved |
| Challenges/tickets in Instant Action (untagged) | `ParkEconomy` runs both in every mode | both disabled for GameType ≠ 0 | untagged divergence |
| ECON-014 | no strikes, happiness constant | happiness is not constant: work drains it, rest recovers it, training resets it to 100; strike state machine present but gated; Mac guide says strikes were removed | happiness part contradicted; strikes conflict, requires PC/runtime evidence |
| ECON-015 | ResearchAbility per day split by effort | per researcher work cycle, × effort share × workload/100 (workload starts at StartingWorkLoad 85, player-set ≤ 100) | partially contradicted (cycle timing open) |
| ECON-016 | group g opens at PercentageForThisTech[g] % of group g−1 | group g opens at PercentageForThisTech[g] (same index, layout-proven) measured **cumulatively over all groups ≤ g−1**, unsigned percentage and compare, 0 % for an empty set | partially supported: threshold index matches, percentage basis differs |
| ECON-017 | cheapest first | item choice not traced | unresolved |
| ECON-019 | Instant Action: virtual grade-2 researcher | no staffless path; researcher staff only; the research panel is refused with the 'research is automatic' text | contradicted |
| UI-015 | mode asked per park; modes identical | mode fixed at player creation (4 slots); modes differ in balance overlay, start park, keys, tickets, challenges, research completion | contradicted |
| ECON-030 / bankruptcy | stops when bankrupt; 6 months in red | ≥ 6 thirty-day months since the balance went negative, checked at month end → world mode 4 (subject to one unresolved id gate) | count supported; "stops" consistent with mode 4 exit but the mode-4 consequences belong to the calendar/clock lane |
| ECON-046 | upgrades need a mechanic | not traced (only the TAG text "can't upgrade during a mechanics' strike") | unresolved |
| Secret 'own all land' ticket (untagged) | — | no award path in the Mac binary | do not implement as earnable without PC evidence |
| Instant Action UI (untagged) | loans, research panel and upgrades available in every mode | bank panel not opened, loan buttons disabled, research panel refused, upgrade list skipped | untagged divergence (matches both manuals for loans/research) |
| Staff wages/dismissal/training (untagged) | — | wage PayMultiplier×BaseWage monthly; dismissal debits one more wage; training points = amount/PoundsPerTrainingPoint, promotion resets happiness to 100 | new Mac facts for the economy owner |

## Unresolved dependencies (explicit blockers)

- **Advisor text**: rule/response ids are now bound (above); the spoken text of
  the samples and the `CMsgTag` id → `TAG_SYSTEM` table remain.
- **Statistic semantics**: `0xc73bc`, `0xc6cec`, `0xc710c` (coaster/kart/water),
  `0xc8cc0` (big park), `0xc8b08` (camera coverage), the visitor counter's
  increment site and history buffer, `0xc2264/0xc21f4`, `0xc1fb4`, catalog
  +1960 and park record +24, staff floats +500/+504 ranges, and the names of
  cell types beyond the save correlation (type 21 unseen).
- **Balance**: layout resolved; parser defaults for unspecified fields (e.g.
  `ResearchTech[5..9]`) and the bounded-value checks were not traced.
- **Calendar**: the multiplier figure is cross-lane (15000, review round 3);
  `.sam`/save overrides of it and the base date are not traced here.
- **Flows not traced**: hire fees outside `0xcbfdc`, rest/work step cadence,
  the bankruptcy id gate (`0x105c6c`) and world mode 4 consequences, research
  item selection order, Instant Action end (`0x105b50`) consequences, the
  affordability check before mystery placement, the lobby callbacks skipped
  under GameType 2, and queued input addressed to a deleted player window.
- **PC equivalence**: none of these predicates were checked in `TP.EXE` or
  Patch 2; the Mac/PC strike conflict shows behaviour can differ even with
  identical data.

## Limits

Instruction-field checks prove what the selected instructions encode, not that a
path is executed at runtime. Function boundaries were derived from prologues and
returns around referenced sites, not from symbols. Manual statements are
player-facing descriptions. No gameplay code, shared tool or register was
changed by this lane; the contract helper is evidence-only and unwired.
