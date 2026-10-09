# PowerPC scenario, progression and staffing evidence

2026-10-09 (four follow-up passes the same day; fifth to seventh passes 2026-10-10). Scenario lane of the nine-lane PowerPC continuation. Static
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
tests (*Progression contract*); third follow-up: player profile creation,
enumeration, selection, persistence and deletion, where the mode is stored, the
missing/unreadable player-file path, the key gate's refusal path, the park
loader's header gate and its application to the PC park files, and a handoff
for a later profile implementation (*Player profiles*, *Profile handoff*); fourth follow-up: the
complete `gms.dat` byte schema and its version gate, read-failure ordering, the
key award's ordering against creation, where the key count comes from, and the
saved first-time and swear-filter flags (*Player file schema and failure
order*, *Key award and key source*); fifth follow-up: the eight front-end
`Keys()` readers, the lobby door display against the door gate, and spent
tickets versus keys in mystery purchases, with a bounded reference calculation
(*Key displays and ticket spending*); sixth follow-up: an immutable `gms.dat`
snapshot reader, writer and JSON envelope with explicit Mac-partial and
strict-host policies (*Profile snapshot reference*); seventh follow-up: the
theme-key string constructor and comparator, the writer's theme/mystery order,
where a short read stores its bytes, and a correction to the reference readers'
partial-member rule (*Native string, order and short reads*). Every finding below is a
fact about **this Mac binary** unless explicitly stated otherwise. It is not
evidence for the PC `TP.EXE` or Patch 2 runtime (see *Mac and PC relationship*).

## Reproduce

```sh
# instruction-field witnesses (2090 checks, identity-pinned; follow-up checks live in
# followup_evidence.py, progression_evidence.py, park_entry_evidence.py,
# profile_evidence.py, player_file_evidence.py, key_display_evidence.py,
# profile_snapshot.py and native_io_evidence.py (which also reads three
# SHA256-pinned shared libraries under bin/libraries), included in the same JSON report)
python3 -I tools/ppc-analysis/lanes/scenarios/scenario_evidence.py /Users/sander/server/game-assets/mac-feral/bin
# tests (synthetic fixtures; the ten corpus cases, including in-memory mutation
# regressions, run only with OPENTPW_MAC_BIN set)
python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios -v
OPENTPW_MAC_BIN=/Users/sander/server/game-assets/mac-feral/bin python3 -m unittest discover -s tools/ppc-analysis/lanes/scenarios
# Mac park-header checks applied to PC park files (interpreted values only)
python3 -I tools/ppc-analysis/lanes/scenarios/profile_evidence.py /Users/sander/server/game-assets/mac-feral/bin \
  /Users/sander/server/game-assets/theme-park-world/Data/levels/jungle/Easymode.TPWI
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

### Park entry (high)

Which park file an offline park entry reads (`park_entry_evidence.py`, 90 checks
including caller and store scans):

- Main-loop state 9 (jump-table entry `0x1c1940`) selects the theme, loads the
  level and its balance (`0x10ef9c` → `0x10474c`, where GameType 2 adds the
  `Easy_` layer). The only save read there is one named on the command line
  (an argument ending in `.TPWS`, flag at data `0x11f5ac` +0).
- If the startup-save object's +1028 is 0 and GameType is not 1, state 9 calls
  `0x198e50`. It searches `*` + `.TPW*` in `<player directory>:<theme>`
  (`0x137c3c`; the theme is the theme object's name string), keeps the newest
  file by `LbFile_CompareFileStamps`, and loads it with the park loader
  `0x11acfc` (mode 2). When nothing matches, the loader is not called.
- +1028 is stored only by the static initializer (`0x116324`, zero) among the
  four functions that load the object's TOC slot; a store through a pointer
  obtained elsewhere is not excluded.
- `easymode` is referenced only at `0x137608`, inside the copy `0x137600`,
  which `CreatePlayer` (`0x13741c`) calls with its easy-flag argument (r6 → r28
  → r5 → r24); the copy runs only when that flag is non-zero. Creating a player
  first deletes any old directory of that name (`0x380c`).
- Leaving a park (state 11, GameType ≠ 1) saves
  `<player>:<theme>:autosave.TPWS` (`0x1987dc` → `0x198960` → `0x11a5f4`).
  State 15, entered after a park load, writes `<player>:<theme>:restart.INTS`
  (`0x1c37b4`), which the `*.TPW*` search cannot match; state 14 reloads it.
- Object text files add the `Easy_` file under GameType 2 and skip a missing
  one (`0x119878`, same `%s:%s%s` pattern).

So the first park of a Full Simulation player in a theme starts from the level
and balance alone; an Instant Action player's first park is the copied
`easymode.TPWI` where the theme ships one. Later entries resume the newest
save in that theme directory for both modes. The other three loader callers
are a debug QuickLoad (`0x112ab8`), a save chosen by name (`0x19882c`) and the
online session (`0x1c200c`, GameType 1). Not established: what `0x11acfc` does
with each file, the wildcard semantics of the imported `CFileStorage`, how a
player starts a theme over, and PC equivalence.

### Park header gate and PC park files (high for the gates)

The park loader `0x11acfc` and its header reader `0x11c880` (sole caller)
apply these checks before the payload, in this order:

1. A little-endian int32 version; the resume load (mode 2) rejects a version
   above 500 (error 9, "future version").
2. A language byte, then 1280 bytes of UTF-16LE text compared with that entry
   of the banner table (`0x120114`, 20 bytes per entry; entry 0 is the
   412-character legal text at code `0x1d6144`). A mismatch logs "legal text
   has been jiggered with". That the compare stops at the first NUL is the
   imported `TbStringBase<w>::operator!=(const wchar_t*)` semantics (inference).
3. 256 bytes passed to `0x109e0c` (object availability; a non-zero result is
   error 8; not traced).
4. Four raw bytes equal to the word at data `0x46fb8` (`0x01221985`; error 6).
5. A little-endian int32: 1 = an embedded header follows, 0 = none.

`profile_evidence.py` applies 1, 2 (entry 0 only), 4 and 5 to PC files:

| File | Version | Language | Banner | Block | Tag | Result |
| --- | ---: | ---: | --- | --- | --- | --- |
| baseline and Patch 2 `levels/jungle/Easymode.TPWI` (identical) | 400 | 0 | equal | all zero | equal | passes the traced gates |
| Theme Park Inc `PreBuilt/prebuilt.TPWS` (negative) | 500 | 1 | unchecked | all zero | different | rejected (tag) |

So the PC file that Instant Action copies passes every header check the Mac
resume applies, except the untraced object block (all zero here). The payload
is not qualified. No other PC save exists in the assets: both PC `save`
directories are empty and no `gms.dat` is present. The PC executables carry
none of the profile names in plain text (`tp.exe` and Patch 2 `TP.EXE` are
small loaders; `TP.ICD` is encrypted), so the PC profile layout is **not
established** by these assets.

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

### Player profiles (high)

`profile_evidence.py` (268 checks). A 212-byte manager (pointer at data
`0x120e2c`, constructor `0x136860`) holds four 24-byte slots (a wide name string
and a word at +20) and the current slot at +96 (−1 = none).

| Path | Built by |
| --- | --- |
| `<base>save` | `0x137058` (`<base>` comes from the object at data `0x53750`, not traced) |
| `<base>save:users` | `0x1370d4` (`online` sits beside it, `0x137d20`) |
| `<base>save:users:<slot+1><name>` | `0x137284`: decimal slot + 1 (`_itow`, radix 10), then the name |
| `…:<name>:gms.dat` | player-info file; static wide string `gms.dat` (`0x137f98`) |
| `…:<name>:<theme>` | park saves (*Park entry*) |

- **Enumeration** (`0x1369e4`, sole caller `0x498` at start-up): all four slots
  are cleared, then every entry of `save:users` (`*`) that is a directory, does
  not start with `.`, is longer than one character and starts with a digit
  1–4 fills slot digit − 1 with the rest of the name; a later entry with the
  same digit replaces the earlier one. Each slot's +20 is the `mEasyModeUser` of
  its `gms.dat`, and the theme directories are created for it. Missing `save`
  or `users` directories are created (`save`, `users`, `online`).
- **Creation** (`0x15cf00` → `CreatePlayer 0x13741c`): trailing spaces are
  removed; an empty name creates nothing and the dialog stays open. The dialog
  supplies the slot; a slot that already has a name loses its directory first.
  The player directory and every `<player>:<theme>` directory are created for
  both modes (`0x137600`); only the `easymode.TPWI` copy needs the flag. The
  record is a fresh one (reset values: no tickets, `mExtraKeys` 0,
  `mSwearFilterOn` 1, `mFirstTimePlayer` 1) with `mEasyModeUser` = flag, and
  `gms.dat` is written at once; then the slot is loaded and the new-player key
  award runs (*Theme entry and initial key*).
- **Selection** (`0x13781c`; callers: slot selection `0x15c060`, creation
  `0x15d034`, state-9 fallback `0x1c1c1c`): +96 = slot; `gms.dat` is read into a
  reset record; GameType = 2 if `mEasyModeUser` else 0, unless GameType is 1;
  each theme's record is found or created.
- **Mode storage**: one byte, `mEasyModeUser` in `gms.dat`, fixed at creation
  (its setter `0x128f54` has two calls, both in `CreatePlayer`; other stores
  through other pointers were not scanned). The slot +20 copy feeds the front-end slot list (four
  readers `0x15c454`…`0x15c7e4`; the control's look is not traced). GameType is
  the runtime copy and is not saved.
- **Persistence**: `gms.dat` = version tag 12, then the named members
  (*Player-global progression record*). It is written on creation, on the
  new-player key award (`0x15ceb4`), when leaving a park (`0x1989b8`, the
  autosave path) and on unload (`0x137b0c`). Every caller ignores the write
  result (an open failure only logs `Failed to open %s`).
- **Missing or unreadable `gms.dat`**: the reader resets the record first and
  returns 0 on open failure, header failure, version < 12 or a member read
  failure; **both callers ignore the result**. So the slot is still listed under
  its directory name, and selecting it gives the reset values overlaid by
  whatever members were read before the failure: with nothing read, a Full
  Simulation player with no tickets and 0 keys. That player cannot enter any
  theme (all `CostToEnter` ≥ 1) and gets no new-player key (the flag is set only
  by creation and an empty slot list). An Instant Action player therefore turns
  into a Full Simulation one silently. Not traced: the partial-read order
  beyond the named members, and whether the front end offers any repair.
- **Unload** (`0x137a88`; callers `0x1979f0`, `0x1c2b38`): writes `gms.dat`, frees
  the record, +96 = −1. **Deletion** (`0x137350`, sole caller `0x15bedc`): deletes
  the player directory and clears the slot.
- **State-9 fallback**: entering a park with no player loaded creates a slot-0
  player named `debug` with flag 0 (Full Simulation) when slot 0 is empty, then
  loads slot 0. It never yields an Instant Action player.

### Player file schema and failure order (high)

`player_file_evidence.py` (457 checks). The serializer `0x129308` has one write
path (`0x128f5c`) and one read path (`0x129060`). Every member goes through a
helper that calls `LbFile_Write`/`LbFile_Read` with a fixed width; multi-byte
values are byte-swapped, so the file is **little-endian**. One exception: the
four 8-byte settings values keep their first word unswapped (Mac native order)
and swap only the second. No names, tags or padding are written.

| # | Field | Encoding | Record offset |
| ---: | --- | --- | ---: |
| 1 | version | i32 | — |
| 2 | `mEarnedGlobalTicket[0..3]` | 4 × u8 | 24 |
| 3 | `mEarnedSecretTicket[0..1]` | 2 × u8 | 38 |
| 4 | `mSpentTickets` | i32 | 28 |
| 5 | `mExtraKeys` | i32 | 32 |
| 6 | `mEasyModeUser` | u8 | 36 |
| 7 | `mSwearFilterOn` | u8 | 37 |
| 8 | `mFirstTimePlayer` | u8 | 72 |
| 9 | theme count (`size`) | i32, signed loop | map +52 |
| 10 | per theme: name length, name bytes (no terminator), theme record | i32, bytes, 160 bytes | — |
| 11 | settings block (static object, data `0x120a14`) | 39 bytes | — |
| 12 | mystery count, then `rideId` each | i32, n × u16 | set +40 |

Theme record (`0x12a0bc`, a 188-byte object, 160 bytes on disk):
`mEarnedLocalTicket[0..5]` (u8, +0), then for i = 0..3 `mAward[i]` (u8,
+6+i) **followed by** `mAwardScore[i]` (i32, +12+4i), then for i = 0..32
`mSignNameA[i]` (u16, +52+2i) **followed by** `mSignNameB[i]` (u16,
+118+2i), then `mNameChanged` (u8, +184) and `mAllResearchCompleted` (u8,
+185). The pairs alternate on disk; they are not two separate arrays. Settings
block (`0x12653c`): `SFXVolume`, `MusicVolume`, `SpeechVolume`, `MovieVolume`
(8 bytes each, object +16/+24/+32/+40), then seven u8 flags `AdvisorOn`,
`TutorialOn`, `TooltipsOn`, `ConfirmDeleteOn`, `RMBScrollOn`, `RMBCancelOn`,
`IsometricOn` (+52..+58). These are game-wide settings stored in **every**
player file: reading any `gms.dat`, including during the start-up scan,
overwrites them. The scan order (`FindFirst`/`FindNext`) is not traced, so
which file's settings win at start-up is not established.

- **Version gate**: written as 12; the reader rejects a version **unsigned**
  below 12 (`cmplwi`), so 13 or `0xFFFFFFFF` is read with the same layout. The
  version is passed to the theme-record and settings serializers, which never
  read it (register scan of both routines). There is no other layout branch.
- **Read order**: scalar members reset (the reset does not touch the theme map
  or the mystery set; both readers pass a newly constructed record), theme
  records freed, open, version, then the members in file order. Any failed
  helper returns 1 and the reader returns 0 at once. The callers ignore it
  (*Player profiles*).
- **Partial read**: members read before the failure keep their file values;
  later members keep reset values. A failing integer read stores whatever bytes
  the import delivered, unswapped (the swap is skipped). A theme whose record
  fails, or whose name repeats an earlier one, is not inserted (the second
  case logs a message); earlier themes stay. Settings values read before the
  failure are already in the global settings object. Consequence for the mode:
  `mEasyModeUser` is file byte 18, so a file of 18 bytes or fewer reads as Full
  Simulation, while a longer file cut later keeps the mode.
- **Only after a complete read**: if `mSwearFilterOn` is set and
  `swears.txt`/`alloweds.txt` (`%s:Language:%s:`) cannot be loaded, the flag is
  cleared in memory and written at the next save. The settings serializer
  applies its values (`0x126460`) after a complete read or write of its block.
- The reference reader `read_mac_player_file` applies these rules to bytes.
  It is tested on synthetic bytes only. No PC player file exists, and nothing
  here says the PC uses this layout.

### Key award and key source (high)

- **Creation then award are two writes**: `CreatePlayer` makes the directories
  (and the `easymode.TPWI` copies for Instant Action), builds a reset record
  (`mExtraKeys` 0, `mFirstTimePlayer` 1, the mode byte) and writes `gms.dat`
  without using the result. Selection then **frees that record and reads the
  file again** (`0x1378b4`, `0x137930`), so the mode and keys come from disk.
  The award (`0x15cd38`, flag set by `0x15d164` after the dialog) is a separate
  step: GameType 2 queues message 394 only; any other GameType does
  `mExtraKeys += 1` (the only increment), writes `gms.dat` without using the
  result, and queues 393.
- Ordering consequences (derived from the above, not observed at runtime):
  if the creation write fails, selection reads reset values, so even an Instant
  Action choice runs as GameType 0 and gets the +1 key in memory (its
  `easymode.TPWI` copies already exist). If only the award write fails, the
  file keeps `mExtraKeys` 0. The in-memory 1 reaches disk only at the next
  successful write (leaving a park, unload). The award cannot repeat once a
  slot is named (*Theme entry and initial key*).
- **Key source**: `Keys()` = `mExtraKeys` + signed `mulhw` truncation of
  earned/3, computed on every call and never stored. Earned counts **non-zero
  bytes** (a stored 2 counts once): 4 global bytes, the 6 local bytes of each
  theme record **whose `global.sam` loads** (`0x12a50c`, which loads it on
  demand and frees it with a log on failure), and 2 secret bytes. Available
  tickets subtract `mSpentTickets`, `Keys()` does not, so spending never
  costs keys. Eleven read-only `Keys()` callers: the theme door, the award code
  (twice) and eight front-end sites, now bound in *Key displays and ticket
  spending*.
- **Instant Action bypass**: only at the theme door (GameType 2 skips the key
  check, *Player profiles*), and in the award (no key). Instant Action records
  are otherwise counted the same way.
- **Saved flags**: `mFirstTimePlayer` has one reader (`0x1c208c`) and one
  clearer (`0x1c20d4`). After a park is loaded or resumed, if GameType ≠ 1 and
  the flag is set, it is cleared **in memory** (no write in that block). Under
  GameType 2 the block then posts two events (10, 0) to the object at data
  `0x11f9bc`. Their meaning is not traced. `mSwearFilterOn` has three writers
  (the read hook and two at `0x1af8c8`/`0x1af944`) and nine readers. The saved
  counters are `mExtraKeys` (award +1 only), `mSpentTickets` (mystery purchase)
  and the ticket bytes.

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

- Refusal path (third follow-up, high): the door's virtual +72 (`0x964c4`,
  vtable `0x3ecc8`) does nothing while already entering (+20) or without a
  target theme. GameType 2 goes straight to +76. Otherwise the per-theme record
  is found or created (`0x129ae0`); if `0x12a50c` fails for it (the same test
  the `CostToEnter` getter makes before reading `global.sam`) the lookup
  returns 0 and the door does nothing; a signed `CostToEnter >
  Keys()` jumps to the epilogue. **The refusal has no message, state change or
  key change** in this routine. +76 sets +20 = 1; +80 (`0x9665c`) selects the
  theme and sets front-end exit code 2. The door's locked appearance and any
  hover text are not traced.

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
  placement. Fifth follow-up: the build-menu proc `0x163748` makes that check
  (*Key displays and ticket spending*).
- Instant Action players earn no tickets (ticket checks need GameType 0), so
  they cannot uncover mystery items that cost tickets.

### Key displays and ticket spending (high for operands; presentation not traced)

Fifth follow-up (`key_display_evidence.py`, 242 checks). Control ids, states
and format strings are operands; what they look like is not traced, and no
label, art or screen name is implied.

- **The eight front-end `Keys()` readers** are two routines:
  - In-park count panel, window proc `0x155b7c` (only address-taken: vector
    data `0x7f70`, installed on child 0x33 by `0x156bac`, sole caller
    `0x13cfb4`). Message 0x15 sets both caches to −1; message 0x1e refreshes.
    Key group (`0x155c74`, `0x155cb4`, `0x155cec`, `0x155d7c`): skipped
    entirely under GameType 2; otherwise when `Keys()` differs from the cache,
    the cache is updated; 0 clears bit 0 of control +0x44 (`0x17fa64`) on
    controls 0x36 and 0x34, any other value sets it and writes
    `swprintf(L"%d  x", Keys())` (8-character buffer) to 0x36. The ticket group
    (0x37/0x35, same pattern, any GameType) shows **available** tickets
    (`0x128a2c`, earned − `mSpentTickets`), initial text `L"0  x"`.
  - Lobby refresh `0x184028` (callers `0x75e0`, `0x183830`, `0x183a94`).
    Door group (0x1e0ea/0x1e0eb): left untouched when the theme lookup
    `0x129ae0` returns 0; GameType 2 clears bit 0 of 0x1e0ea; otherwise
    signed `Keys()` < `CostToEnter` gives state 1 and value cost+4 and stops the
    looping sound, else state 0, value cost−1 and looping sound 0x61 if none
    plays. Count group (0x1e0ec, text 0x1e0ee): GameType 2 or `Keys()` ≤ 0
    clears bit 0; else `swprintf(L"%d x", Keys())`. Every refresh, under any
    GameType, ends by storing `Keys()` in data `0x135fb0` (`0x1843f0`).
- **Cached copy**: `0x135fb0` has exactly three users: one routine zeroes it and
  immediately calls the refresh (`0x183a80`–`0x183a94`; that routine is not
  identified), the refresh writes it,
  and `0x183dd0` (callers `0x960bc`, `0x962dc`, `0x9675c`, `0x967e0`,
  `0x15cee8`) repeats the door-group rule against the cached value.
- **Display gate = door gate**: the door (`0x964c4`) enters iff signed
  `CostToEnter <= Keys()`; the display marks locked iff signed `Keys() <
  CostToEnter`. Both need the theme lookup to succeed first (find or create,
  then 0 unless `global.sam` loads), and the cost getter `0x12a4c8` returns −1
  for an unusable theme (eight callers). Door order: already entering → no
  target → GameType 2 enters → unusable theme → `Keys()`, then cost → signed
  compare. Instant Action therefore bypasses keys before the theme's
  `global.sam` is consulted.
- **Spent tickets versus keys**: `Keys()` reads `mExtraKeys` (+32) and the
  earned bytes and has no +28 (`mSpentTickets`) load; the insert `0x128ca8`
  adds the cost to +28 only for a new id and writes no +32. The build-menu proc
  `0x163748` (vector data `0x8090`) applies the affordability test `0xd313c`
  (signed cost ≤ available, **no GameType test**, four callers) when cost ≠ 0
  and the id is not owned; refusal calls `0x139a40` (`0xbb4fc` with 29, not
  interpreted) and returns. The placement tests cost **> 0** instead, and an
  unaffordable purchase that still reaches `0xd3000` returns 0 with **no
  ticket spend, no record and no money debit**. The owned-set key is the high
  16 bits of the 32-bit item-id word (`stw` at +0x7c, `lhz` at +0x7c).
- **Widths**: `mExtraKeys`, `mSpentTickets`, `CostToEnter`, `Keys()` and
  available tickets are signed 32-bit; `Keys()` adds and available subtracts
  with 32-bit wraparound; all compares above are `cmpw`.

Reference calculation (same module, synthetic values only): `earned_tickets`,
`mac_keys`, `mac_available_tickets`, `mac_theme_door`, `mac_lobby_door_display`,
`mac_count_panel`, `mac_mystery_place` and `mac_menu_affordable`. Preconditions:
GameType ∈ {0, 1, 2}; counters and costs signed 32-bit ints; ticket bytes
exactly 4/6/2 unsigned bytes; theme usability is a caller-supplied bool (the
original loads `global.sam` as a side effect, the reference does not); item
keys unsigned 16-bit. A violated precondition raises before any result.
`test_key_display.py` covers the failure order, equality boundaries, signed and
wrap cases, and 11 in-memory mutations (wrong branch, unsigned compare, extra
cache user, spend into `mExtraKeys`, placement debiting money) that the
witnesses must reject. Nothing here is PC evidence; ECON-040 stays
PC-unqualified.

### Profile snapshot reference (high for the layout; strict policy is a host choice)

Sixth follow-up (`profile_snapshot.py`, 19 more checks; `test_profile_snapshot.py`,
30 synthetic cases and 2 corpus cases). A standalone reference for a later profile
implementation. It is not wired into the front end, the runtime or any save path,
creates no folders and touches no host settings. No `gms.dat` exists in the
assets, so every fixture is synthetic. Nothing here is a PC parser.

Reader details pinned for this pass:

- **Theme name**: the reader allocates length + 1 bytes (`__nwa__`, `0x129798`)
  and uses the result **without a null test**. The byte loop tests first, so
  length 0 reads nothing, and compares the index unsigned (`cmplw`, `0x1297d8`).
  The buffer is NUL-terminated (`0x1297e8`) and passed to
  `TbDynamicStringTemplate<c>(const char *)` (`0x1297f0`), then freed. The map key
  therefore ends at the first NUL (traced in the seventh follow-up). Length
  `0xFFFFFFFF` allocates 0 bytes and the byte loop then stores past the block,
  which the trace does not define. Any other length past the end of the file
  reads to the end and fails without inserting the theme, provided the
  allocation succeeds (a failed allocation is not traced).
- **Mystery set**: the insert result is unused (`0x129964`), so a repeated
  `rideId` is absorbed silently.
- **Mode test**: selection tests the low byte of `mEasyModeUser` for non-zero
  (`clrlwi.`, `0x137990`), so any non-zero byte selects Instant Action.

Module contents (synthetic bytes only):

- `read_profile_snapshot(raw, policy)` has no default policy. It returns a
  frozen `ProfileSnapshot`: the raw version (u32), raw ticket bytes, counters,
  raw mode/swear/first-time bytes, raw signed theme and mystery counts, the
  inserted themes in file order (raw name bytes, 160-byte record with the
  award/score and sign-name pairs kept as pairs), the settings read in block
  order as raw bytes, the `rideId`s in file order, completion, the failing step
  and offset, the player members read in full, trailing bytes and a list of
  issues.
- `mac-partial` follows the traced reader. Reset values are overlaid by every
  member read before the first failure. A theme is inserted only when complete
  and its key is new; a duplicate stops the read. Unsigned version ≥ 12 is
  accepted with the one layout, and versions other than 12 are recorded as an
  issue. A negative count reads nothing. Repeated `rideId`s are kept in order
  and collapse in `mystery_set`. Trailing bytes are ignored. Settings read
  before a failure are kept but `settings_complete` is false (the Mac applies
  the block only after a complete read). A failing player member holds what the
  short import stored (corrected in the seventh follow-up, see *Native string,
  order and short reads*). A one-byte member keeps its value. An `i32` member
  holds the delivered bytes, unswapped, over its earlier bytes. A partly read
  8-byte setting is reported as an issue only, because the snapshot does not hold
  the game-wide object it lands in.
- `strict-host` is a **host policy, not Mac behaviour**. It raises
  `StrictReject` with one fixed reason: `rejected-version` (< 12, as the Mac),
  `unknown-version` (13…`0xFFFFFFFF`, which the Mac reads), `truncated`,
  `negative-count`, `duplicate-theme`, `nul-in-theme-name`, `duplicate-ride-id`
  or `trailing-bytes`. An empty theme name is accepted (the Mac reads it; whether
  its writer can produce one is not traced).
- `serialize_profile_snapshot` writes the traced layout in the snapshot's own
  order with the raw version, raw counts and trailing bytes, so every complete
  read cycles byte-identically. It refuses partial snapshots. After a partial
  read the Mac's next write depends on the game-wide settings object, which a
  snapshot does not hold. This is not the Mac writer's order: that writer
  iterates the theme map and the mystery set in ascending order (now traced,
  `mac_writer_order`) and always writes version 12.
- `to_envelope`/`from_envelope`: JSON envelope
  `opentpw.reference.mac-gms-snapshot`, envelope version 1, bytes as hex,
  order kept, source identity included; unknown schema or envelope versions
  are refused. Derived values are not stored.
- Derived on demand: `selection_game_type(snapshot, current_game_type)` (1 stays
  1, else 2 iff the mode byte is non-zero), `key_counters(snapshot, usable)`
  (reuses `earned_tickets`/`mac_keys`/`mac_available_tickets`; `usable` over
  theme map keys is required because whether `global.sam` loads is a runtime
  fact), and `setting_words` (first word big-endian as stored, second
  little-endian; for the four volumes byte 0 is the enable test and the second
  word the level, see the seventh follow-up).

Tests cover every truncation length (partial under `mac-partial`, `truncated`
under strict), the byte-18 mode boundary, overlay and reset of later members,
cut themes and settings, an oversized name length, duplicate and NUL-colliding
theme keys, negative counts, repeated ids, trailing bytes, the version
boundaries 0/11/12/13/`0x7FFFFFFF`/`0x80000000`/`0xFFFFFFFF`, byte and JSON
cycles, writer refusals, immutability, agreement with `read_mac_player_file`,
key counters (non-zero bytes, usable themes only, spending never lowers keys,
negative and wrapping counters, partial records) and four witness mutations.

### Native string, order and short reads (high, except one Mac OS step)

Seventh follow-up (`native_io_evidence.py`, 140 more checks, 2090 in total;
`test_profile_snapshot.py`, 11 more cases including 3 corpus cases with 8
in-memory mutations). For the first time this lane also reads shared libraries
the executable imports. Each is pinned by SHA256: `bullfrog_shared.data`
(`b67b56b7…ec06`), `c_c++_shared.data` (`5e04f9c0…b27f`) and
`macdoze_shared.data` (`ba11331a…2f0d`). Nothing was executed and no `gms.dat`
exists; every fixture is synthetic.

- **Key constructor** (`bullfrog 0xadcc`): `TbDynamicStringTemplate<c>(const char *)`
  calls `strlen`, then virtual slot 0x10 (`AllocBuffer`: length = n, buffer[n] = 0),
  then `strcpy`. A null pointer gives the empty string. The C library's `strlen` and
  `strcpy` stop at the first zero byte (`0x20db8`, `0x20dd8`). **The map key is the
  name bytes before the first NUL**, which upgrades the previous medium inference.
- **Comparator** (`bullfrog 0xcdc0`, `TbStringBase<c>::operator<`): `strncmp` over
  the shorter length (`cmplw`). The library's `strncmp` compares zero-extended bytes
  (`lbzu`, `cmplw`, returns a − b). On an equal prefix the shorter string is less.
  Keys hold no NUL, so the order is **unsigned lexicographic byte order with the
  shorter prefix first**. Two names collide exactly when their keys are identical.
- **Containers** (`0x12b2ac`, `0x12b5a8`, `0x12b698`, `0x12b868`, `0x116e94`): both
  are red-black trees (left +0, right +4, parent|colour +8, value +0xc). Each caches
  its leftmost node: the theme map at tree+0xc (tree = record+0x34), the mystery
  set at tree+8 (tree = record+0x28). Inserts are unique and use the comparator in
  both directions. `rideId`s are `lhz` values compared with `cmplw`.
- **Writer order** (`0x129458`–`0x129610`): the writer starts at the leftmost node,
  walks the in-order successor and stops at the header. Themes are therefore written
  in ascending key order, with count = map size and name = key (`Length()` +
  `strcpy`). A name read with an embedded NUL is written back truncated to its key.
  `rideId`s are written ascending unsigned, with count = set size, so repeats and
  negative counts do not survive a rewrite. `mac_writer_order(snapshot)` returns
  this order. The byte writer `serialize_profile_snapshot` still preserves file order
  and is not a native rewrite.
- **Short reads**: member import → `LbFile_Read` (`bullfrog 0x994`; status =
  returned count == requested) → the 8-byte handle `LbFile_Open` makes (vptr
  `0x440c`, slot 0x10 forwards to the inner file's slot 0xc) → the disk-file class
  (vptr `0x2c04`, built at `0x3d8c`; `0x368c` is bullfrog's only caller of
  `NS_MacDoze::ReadFile`) → `ReadFile` (`macdoze 0x2ad8`: count = n,
  `FSRead(refNum, &count, buffer)`). The buffer is the **member's own address**
  throughout. The game-side helpers fail unless the full width arrived (`0xc398`,
  `0x126f88`), and they fail before the swap. Consequences:
  - one-byte members: nothing is delivered, so the member keeps its value (exact);
  - `mSpentTickets`/`mExtraKeys`: k < 4 delivered bytes land in the high-order bytes
    of the big-endian member. Example: a file cut one byte into `mExtraKeys`
    holding 0x7f leaves `mExtraKeys = 0x7f000000`, and `Keys()` follows;
  - 8-byte settings: k < 8 bytes land in the game-wide settings member, neither
    word swapped;
  - theme count, mystery count and `rideId`s are read into stack temporaries, and
    the set insert follows success only, so a short read there changes no record
    state.
  The one step not in the assets is Mac OS `FSRead` storing the bytes before end of
  file into the buffer, as Inside Macintosh documents. Everything above it is
  traced.
- **Settings apply** (`0x126460`, after a complete read only): Music, Speech, SFX,
  then Movie. Each tests byte 0 of the raw first word for non-zero (enabled) and
  passes the second word on as the level. The movie level is scaled
  trunc(level × 1023 / 100). It stores nothing into the settings object, and the
  one-byte settings are not read there.

**Correction to the reference readers.** `read_profile_snapshot` (`mac-partial`)
and `read_mac_player_file` previously showed the reset value for a short `i32`
player member. That was not faithful. Both now apply `short_import` (delivered
bytes over the earlier big-endian bytes, unswapped), qualified by the `FSRead`
dependency above, and `mac-partial` records the delivered byte count as an
issue. `strict-host` is unchanged (`truncated`). A partly read 8-byte setting is
reported as an issue only.

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
not re-verified by this lane's witness. Lobby callbacks `0x183dd0`/`0x184028`
are the key displays (*Key displays and ticket spending*); `0x183cc0` (click
sound, UI lane) and `0x966f8`/`0x9677c` also skip work under GameType 2 and are
not interpreted here. The advisor rule
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

## Profile handoff

For a later unified `ParkGameMode`/profile implementation. Mac facts only;
the PC runtime is unproved (*Park header gate and PC park files*).

- Profile identity: slot 1–4 plus a non-empty name (trailing spaces removed).
  The mode is chosen once at creation and stored as one byte; there is no
  per-park mode choice and no mode change after creation.
- Creation: fresh progression record (no tickets, keys from `mExtraKeys` 0,
  swear filter on, first-time flag set), save it, create the player and theme
  directories, copy `easymode.TPWI` per theme for Instant Action, select the
  profile, then the new-player award (Full Simulation: `mExtraKeys` 1 and save;
  Instant Action: lobby message only).
- Selection: mode → GameType (online keeps 1). Theme entry: Instant Action
  ignores keys; otherwise the theme's `global.sam` record must be usable and
  `CostToEnter <= Keys()`; refusal is silent; keys are never consumed.
- Park per theme: first entry from level and balance (plus the copied
  `easymode.TPWI` for Instant Action where shipped), later entries resume the
  newest `*.TPW*` in the theme directory, which must pass the header gate;
  leaving writes `autosave.TPWS`.
- Persistence points: creation, new-player award, leaving a park, unload.
  Write failures are ignored by the original. Selection after creation re-reads
  the file, so a failed creation write loses the mode (*Key award and key
  source*).
- Player file: little-endian, version 12 (any unsigned value ≥ 12 accepted, one
  layout), field order and widths as in *Player file schema and failure
  order*. `profile_snapshot.py` is the byte-level reference for both the Mac
  partial read and a strict host read; a host that rejects unknown versions must
  label that as a divergence from the unsigned Mac gate. The file also carries the game-wide settings block. A reimplementation
  must decide whether to keep that coupling. Keys are derived, never stored:
  only `mExtraKeys` is saved.
- Failure paths with an explicit choice for OpenTPW: a missing or unreadable
  player file silently becomes a Full Simulation profile with 0 keys in the
  original. OpenTPW's own save currently requires `Mode` and `Easy` and refuses
  a mode mismatch; keeping that stricter rule is a deliberate divergence and
  should be labelled as one rather than copied silently.
- Key displays: keys and available tickets are separate numbers; a mystery
  purchase lowers the ticket count and never the key count. The lobby door
  display uses the door's own predicate (*Key displays and ticket spending*).
- Do not invent: the `<base>` directory, `0x109e0c`'s object check, the door's
  locked presentation (only control operands are traced), the front-end slot icon, the research/staff/lab state of
  a resumed save (the economy snapshot needs its own bridge), and anything PC.

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

## Production mode wiring

The production game now applies the mode findings that existing code paths can
carry (docs/ECONOMY.md, "Game modes"); the contract above stays a separate,
unwired reference.

- `ParkStart` (`source/OpenTPW/Economy/ParkStart.cs`) resolves a start kind into
  economy rules, the `Easy_` layer and the shipped-save import as three separate
  decisions. The front-end `GameMode` button maps to it by name (its values are
  in the opposite order to `ParkGameMode`). Neither is the original GameType,
  front-end exit code or main-loop state, and online play (GameType 1) has no
  `ParkGameMode`: read-only visits run no economy.
- Full Simulation: standard balance, `Easymode.TPWI` not read. Instant Action:
  `Easy_` layer and seed where present (jungle only). `--load-original-level`
  and the Load Park entry keep the reference start (shipped save with the
  balance it was made with, Full Simulation rules) unchanged.
- `ParkModeFeatures` gates golden-ticket checks, challenges, loans, research
  lab efforts and upgrades in Instant Action. Research points come only from
  hired researchers in both modes; `ParkEconomy` no longer uses the ECON-019
  staffless rate. The seed's staff are not decoded, so nothing is invented for
  them.
- "Full Simulation starts without the seed" is now traced for the Mac (see
  *Park entry*): it holds for a player's first park in a theme, which is the
  only start OpenTPW has. Resuming the per-theme autosave is not reproduced
  (UI-015). The ticket cadence (ECON-033), keys (ECON-040, `PlayerProgress`),
  research rates and all PC behaviour are unchanged.
- OpenTPW's own save keeps `Mode` by name and `Easy` as required members with no
  defaults; a running park refuses a save of the other mode. The native
  front-end smoke run covers an Instant Action start from the menu.

## Register impact (proposals; registers not edited here)

| ID / area | Current OpenTPW assumption | Mac binary evidence | Suggested state |
| --- | --- | --- | --- |
| ECON-040 | 1 starting key; keys not consumed | Not consumed: proven. Start: Full Sim gets `mExtraKeys` 1 on first lobby entry; Instant Action 0 keys but no key check; a repeat award needs front-end init, which clears the flag (player-window closure); keys are derived (`mExtraKeys` + earned/3, earned = non-zero ticket bytes, locals only for themes whose `global.sam` loads); the award is a second `gms.dat` write after creation, and failures of both writes are ignored | Mac-resolved; PC confirmation remains. Display: lobby door display uses the door predicate, key counts are hidden at 0 keys and never shown in Instant Action |
| `PlayerProgress.Keys` (untagged) | 1 + Σ per-theme earned / 3 | `mExtraKeys` + (player-wide globals + per-theme locals + player-wide secrets) / 3 | untagged divergence: global/secret tickets are once per player, not per theme |
| ECON-029 | tickets spent on GoldenTicketCost purchases | first placement of a ticket-cost item spends tickets once per ID (player-wide set), no money; later copies cost the money price; online free and unrecorded | partial support; item set persistence and the cash price after uncovering are missing. Fifth pass: build-menu gate signed cost ≤ earned − spent (no GameType test); spending lowers available tickets only, never keys; an unaffordable placement that reaches `0xd3000` is neither recorded nor charged |
| ECON-033 | checked at month end | every 100 `mGameTick` ticks (unsigned modulo), GameType 0 only; about 4.34 game days per check (cross-lane calendar figure) | contradicted |
| ECON-038 | big park = MinCellsOwned, cameras = MinCellsCovered | big park reads 1916 = MinCellsCovered (parser-derived layout); cameras need 100 % coverage; MinCellsOwned (1912) unread | contradicted (high) |
| Ticket predicates (untagged) | `>=`; happiness counts happy guests | strict signed `>`; people in park = guests (class 1) on cell types 0/1/3/9/10; happiness = float mean of truncated guest happiness (0 while closed) and the second test reuses the guest count; RecentVisitors also needs park age > N months | untagged divergence |
| ECON-039 | profit of last 12 closed months | year-to-date accumulator +292, zeroed on end of year, checked every 100 ticks | contradicted |
| ECON-034..036 | challenge semantics/flow | challenges only in GameType 0; first offer at park-age day ≥ DaysUntilFirstChallenge | only activation resolved |
| Challenges/tickets in Instant Action (untagged) | disabled in Instant Action (`ParkModeFeatures`) | both disabled for GameType ≠ 0 | applied for the Mac rule |
| ECON-014 | no strikes, happiness constant | happiness is not constant: work drains it, rest recovers it, training resets it to 100; strike state machine present but gated; Mac guide says strikes were removed | happiness part contradicted; strikes conflict, requires PC/runtime evidence |
| ECON-015 | ResearchAbility per day split by effort | per researcher work cycle, × effort share × workload/100 (workload starts at StartingWorkLoad 85, player-set ≤ 100) | partially contradicted (cycle timing open) |
| ECON-016 | group g opens at PercentageForThisTech[g] % of group g−1 | group g opens at PercentageForThisTech[g] (same index, layout-proven) measured **cumulatively over all groups ≤ g−1**, unsigned percentage and compare, 0 % for an empty set | partially supported: threshold index matches, percentage basis differs |
| ECON-017 | cheapest first | item choice not traced | unresolved |
| ECON-019 | unused leftover (declaration reworded to say so; `ParkEconomy` no longer calls it) | no staffless path; researcher staff only; the research panel is refused with the 'research is automatic' text | contradicted; delete code and declaration once PC behaviour confirms |
| UI-015 | mode asked per park entry and every entry starts a new park (declaration reworded) | mode fixed at player creation (4 slots, one byte in `save:users:<n><name>:gms.dat`); each theme resumes the player's newest save (`autosave.TPWS` on leaving); modes differ in balance overlay, first park, keys, tickets, challenges, research completion; an unreadable `gms.dat` silently yields Full Simulation with 0 keys | per-park choice, profiles and the missing resume remain approximations |
| ECON-030 / bankruptcy | stops when bankrupt; 6 months in red | ≥ 6 thirty-day months since the balance went negative, checked at month end → world mode 4 (subject to one unresolved id gate) | count supported; "stops" consistent with mode 4 exit but the mode-4 consequences belong to the calendar/clock lane |
| ECON-046 | upgrades need a mechanic | not traced (only the TAG text "can't upgrade during a mechanics' strike") | unresolved |
| Secret 'own all land' ticket (untagged) | — | no award path in the Mac binary | do not implement as earnable without PC evidence |
| Instant Action UI (untagged) | economy refuses loans, research effort and upgrades in Instant Action; no HUD bank/research panel exists to show the refusal | bank panel not opened, loan buttons disabled, research panel refused, upgrade list skipped | economy gates applied; panel presentation missing |
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
  lobby callbacks `0x966f8`/`0x9677c` skipped under GameType 2, what the
  door-display state/value/sound and the bit-0 flag look like on screen, the
  in-park count panel's screen identity, the other two affordability callers
  (`0x163240`, `0x164094`), queued input addressed to a deleted player window, the
  profile `<base>` directory, the park header's object block (`0x109e0c`), the
  locked-door presentation and the front-end slot icon.
- **Player file**: the `FindFirst` order (which file's settings win at start-up),
  Mac OS `FSRead` storing the bytes before end of file (documented, not in the
  assets), the outcome for theme name length `0xFFFFFFFF` and for a failed name
  allocation, how the inner disk file is chosen by the storage registry behind
  `LbFile_Open` (only one disk class reaches `ReadFile`), the sound setters the
  settings apply calls, the two `mFirstTimePlayer` events (data `0x11f9bc`) and
  the settings toggles at `0x1af8c8`/`0x1af944` are not traced.
- **PC profiles**: no PC player file or player save exists in the assets and the
  PC executables expose none of the profile names; the PC profile layout and
  failure behaviour are unproved.
- **PC equivalence**: none of these predicates were checked in `TP.EXE` or
  Patch 2; the Mac/PC strike conflict shows behaviour can differ even with
  identical data.

## Limits

Instruction-field checks prove what the selected instructions encode, not that a
path is executed at runtime. Function boundaries were derived from prologues and
returns around referenced sites, not from symbols. Manual statements are
player-facing descriptions. No gameplay code, shared tool or register was
changed by this lane; the contract helper and the profile snapshot reference are
evidence-only and unwired.
