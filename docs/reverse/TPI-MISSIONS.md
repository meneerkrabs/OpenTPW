# Theme Park Inc: the mission controller and the obstacles

November 21, 2026. Static reading of Theme Park Inc's no-CD `Game.exe` (SHA-256 `77ea0c41…967a`) with the annotated listings and REA; the original game was not
run. This page says what OpenTPW ports of the mission controller (`ThemeParkIncMissionController`, `ParkEconomy.ThemeParkIncMissions.cs`,
`Level.ThemeParkIncMissions.cs`) and of the obstacles (`ThemeParkIncObstacleTypes`), and what is still open. The research notes behind it are
`OpenTPW-notes/research/tpi-campaign/r3-mission-thing.md`, `r4-mission-ui.md` and `research/tpi-staff/missions-system.md`. The career object and the mission
file are in [TPI-CAREER.md](TPI-CAREER.md), the land in [TPI-BOUNDARY.md](TPI-BOUNDARY.md). No game data is reproduced here.

## 1. The controller thing

The park thing factory builds the controller (class 21, stream tag `OBJH`) after the challenge system (`0x648633`) and keeps its id at `park+0x1EAB6C`.
OpenTPW builds it with the career in the Theme Park Inc economy (`ParkEconomy.TpiMissions`) and updates it in the world pass before the challenge system,
because the newer thing is visited first.

| Field | Meaning | Saved |
| --- | --- | --- |
| `+0x14` | state: 0 idle, 1 researched obstacle shown, 2 obstacle removed, 3 `ObstacleOpened` shown, 4 `CutsceneObject` shown | `mCutsceneState` |
| `+0x18` | the thing the camera shows and message 0x29 acts on | `mCutsceneObject` |
| `+0x1A` | register the obstacle research project on the next update | no; the constructor and the load set it |
| `+0x1B` | the obstacle's research is complete (message 0x23) | no; the load clears it |

- **Constructor (`0x5EEDD0`).** State and thing 0, `+0x1A = 1`, `+0x1B = 0`; it subscribes to 0x23, 0x24, 0x25, 0x26 and 0x29 and, unless the game is finished,
  starts the exit statistics. It runs only when a new world is built (Enter Game), with the player's career.
- **Load (`0x5F03C0`).** The park loader does not construct a loaded controller: `0x64D5B4` allocates it (`0x638120`), runs the thing base (`0x637210`) and the load,
  which reads the two saved fields and sets `+0x1B = 0` and `+0x1A = 1`. Load A Game reads the global save before the park file (`0x7451E0`), so the statuses are the
  player file's. OpenTPW's restore builds the economy first (its controller starts statistics on a new career), then restores the career and the controller over it,
  and `AttachThemeParkIncCareer` swaps in the player file's career while the controller keeps its fields; both end in the original's state. The Science continuation
  save keeps the fields (`ParkSaveFile.Data.TpiMissions`) and the current obstacle (`ParkResumeSnapshot.TpiCurrentObstacle`); a save from before the controller
  continues with it idle and no current obstacle.

## 2. Exit statistics

**Start (`0x5EEF60`).** Every status whose zone is the viewed zone (`0xA06558 +0x14`) or 4, and that has not started, takes its baseline and today's date
(`0x74C620`): types 1 and 2 the bank balance, 5 the guests in the park, 3, 6, 7 and 8 zero; other types are not started. Then message 0x27, and the
mission's tech level: for `TechLevel.Theme` 0 to 2 the zone record's `+0` rises to `NewMaxGroup`.

**Daily test (`0x5EF520`).** Only on world ticks whose low two bits are 0 (`park+0x1EAB40 & 3`). For each running status of the viewed zone (or zone 4):
`days = (now − start) / 0xC92A69C000`, the value is measured, and only when `days` differs from the stored days are both stored (`0x74C5B0`). Then, when
`days >= TargetTime`, a reached target sets state 2; otherwise a nonzero `TargetTime` sets state 3. Every store broadcasts 0x27.

**Values (`0x5EFF00`) and targets (`0x74C670`).**

| Type | Value | Target |
| --- | --- | --- |
| 1 | `cash − loans > 0` | `value >= TargetValue` |
| 2 | `cash − loans` (bank `+0x14` less `0x5C1300`, 32 bits) | same |
| 3 | challenges won in zones 0 to 2 (`+0xF0` of each record) | same |
| 4 | last month's takings: admission, shops, sideshows, rides, facilities (`0x5B9880`) | `value >= (TargetValue + 100) × baseline / 100` |
| 5 | guests in the park (`0x5B2060(4)`) | same as 4 |
| 6 | park awards held (`0x74C860`) | `value >= TargetValue` |
| 7 | shares owned, percent | same |
| 8 | guests in the park | same |

`0x5B2060(4)` counts the guests on a cell of type 0, 1, 3, 9 or 10, Theme Park World's census. `0x5C1300` sums `(term − repaid) × instalment` over the bought
loan records. OpenTPW measures types 1, 2, 3, 5 and 8 from its park. Type 4 sums the newest closed month of five analyser rings (`+0x20148` to `+0x20A88`, stride
`0x250`): the admission, shop, sideshow, ride and facility takings, so last month's revenue, not its profit (`research/tpi-campaign/r8-research-training-awards-shares.md`
§5). The first three are Theme Park World's gate, shop and sideshow rows moved by `+0x268`, which OpenTPW's ledger keeps; but the ledger books ride and facility
takings among the other income, without rows of their own, and a ring with no closed month adds `0x10(%esp)`, the pushed `ecx`, which is the analyser's own address. Type 6 reads awards that no ported code writes, type 7 the share system. Those three are never stored or decided
(`[EXT:tpi-capability-gates]`), and a type-4 status is not started. Of the shipped missions only miss17 needs one (awards and shares), so it cannot be completed. No shipped mission sets a
`TargetTime`, so no shipped mission can fail.

## 3. Requirements and completion

- **IsMissionComplete (`0x5F0600`).** Every used status achieved; `RequiredChallenges.Number` at most the required zone's wins; `Promotion.Number` at most the
  rank; an obstacle mission's obstacle removed; tickets at least `Obstacle.GoldenTickets`.
- **MetRequirements (`0x55A620`).** The career's current zone must be `Completion.EndingTheme` (or `EndingTheme` 3). An obstacle mission (`0x5F0190`: a type and
  `Info.NoResearch` 0) then needs the obstacle state 2, every slot of the staff HQ's 20-entry obstacle table filled, and the tickets; it does not call
  IsMissionComplete. Any other mission is IsMissionComplete. The staff HQ's slots are `ThemeParkIncStaffHeadquarters` (TPI-STAFF.md §12.1): the mission's
  `ObstacleStaff` table, filled by trained staff whose ability matches; a park without Theme Park Inc staff has no HQ and never meets an obstacle mission.
- **Obstacle state (`0x5F01D0`).** 0 without an obstacle (or with `NoResearch`); else 1, or 2 once researched; 3 when the current zone is the ending zone and
  the type's `Obstacle.OpenedAutomaticly` is 1; 4 once removed.
- **OnMissionComplete (`0x5F0770`).** The mission window's button for a mission without an obstacle. In the wrong zone it logs "Mission is complete but we're in
  the wrong theme!!!" and does nothing. With `Completion.ObstacleOpened` the first attraction of that type (`0x647850`) becomes the thing, state 3, and the camera
  flies to it (animation 2, mode 0); with none found the tick runs at once. Else `Completion.CutsceneObject` gives state 4 (the thing is not recorded; no shipped
  mission uses it), else the tick runs at once.
- **Completion tick (`0x5F0940`).** Nothing once the game is finished. The career spends the tickets, opens a theme and finishes the game or advances
  (TPI-CAREER.md §2). The world loads `extent.NNN` for the counter before the advance (`Level.ApplyThemeParkIncExtent`), and after an advance the park
  finds the new current obstacle (`0x6476F0`: the last attraction of `Obstacle.Type`, or 0 for none) and the new mission's statistics start. The end-of-game
  object `0x5CD6E0(0x5D, 6, 1)` and message 0x2C, which nothing in OpenTPW receives, are not posted (`[EXT:tpi-capability-gates]`).
- **Objective letter.** The update after an advance posts the "Objective Complete!" letter of the completed mission (`0x5CEAE0`, `OBJECTIVE_COMPLETE.str`) and
  sets the career's `+0x20`. OpenTPW records the letter (`ParkEconomy.TpiObjectiveLetters`); its texts and window are in §7.

## 4. Messages

The controller's handler `0x5F0F50` ends every message with the 0x27 broadcast.

| Message | Sender | The controller |
| --- | --- | --- |
| 0x23 obstacle researched | the research lab's kind-5 project | `+0x1B = 1`; the next update discovers the obstacle staff's abilities, sets state 1 and flies the camera to the current obstacle (anim 1) |
| 0x24 remove obstacle | the mission window's or the remove button | state 2, the message's thing, camera (anim 2), career `+0x13E5 = 1`; the staff HQ, an older thing, hears it first and rebuilds its slots (`0x634D20`) |
| 0x25 promotion | a challenge win with `PromoGiven`, Get Promoted | `0x74C450(rank)` |
| 0x26 challenge won | the challenge win | no case: only the broadcast |
| 0x29 camera finished | the end of every camera flight (`0x5D0996`, the only sender) | state 2 or 3: the thing opens (`0x466050`, `0x5E8BD0`) and the tick runs; state 4: the thing is destroyed and the tick runs; then state and thing 0 |

OpenTPW routes the challenge system's promotion and win, and the Get Promoted cheat, through the controller. The research lab's project (`0x61CBF0`) is
not registered, because Theme Park Inc's research is not ported (`[EXT:tpi-capability-gates]`); message 0x23 therefore never arrives in play.

## 5. The camera flight

The flight (`0x5D0400`, update `0x5D02F0`, end `0x5D0840`) is the target thing's own model animation played by the cutscene thing (class 22, `CUTS`). The
world keeps running under it at game speed 100. The 0x29 handler reads only the controller's state and thing, not anything the flight produced. But the
flight's end is the only sender of 0x29, so a completion happens when the animation ends. That time depends on the animation module's clock
(`0x6B0740`, `0x6B0780`, `0x6B0C90`) and on how the camera follows the model, which are not read. OpenTPW does not port the flight: a camera request is
recorded and logged, and nothing posts 0x29 (`[EXT:tpi-capability-gates]`). Posting it at once would move the completion's time, which needs an approval
that was not given. Until the flight is ported, the controller's completion paths that show a thing are exercised by tests that deliver 0x29 themselves; the mission
window's Proceed (§7) runs them in play.

## 6. Obstacles

An obstacle is an attraction (class 3) whose type comes from `Data\levels\<zone>\obstacles\*.wad`, a normal ride archive with a `.sam`, models, animations
and a script. `ThemeParkIncObstacleTypes` reads each zone's types with the words the obstacle code uses (key table `0x9A2D00`): `Info.Shape`, the map offsets,
`IsObstacle`, `IsSupportingObstacle`, `IsOutsideFeature`, `GateTo`, `IsSpecial`, `IsCameraPath` and the `Obstacle.*` keys. Science holds 1700 (the gates,
`OpenedAutomaticly`), 1701 (fallen trees, deleted after use), 1702 (bridge, map offset 1, 1), 1703 (the lake, replaced by 1704 at (7, 19)), 1704 (the
drained lake, `IsSupportingObstacle`), 1750 (the gate to the Polar Zone, `GateTo` 2) and 1798 (the camera path). The research note said no shipped
obstacle has a map offset; the bridge has (1, 1).

**Map grids (`0x41AD90`).** `Info.Shape` and `Obstacle.AfterShape` are parsed with the table `0x98DF88` (24 rows): `.` 0, `*` 4, `Q` 3, `@` 1, `+` 11, `#` 16, `H` 8,
`X` 5, `,` 30, `=` 32, `%` 33 (leave untouched), the entrances and exits as in Theme Park World, `D`/`>`/`<` 23, `O` and `I` 27. A space takes no column, a
row ends at the widest row's width with zeros, 40 rows or 40 cells per row is the limit, and the rows are stored bottom first.

**Not ported yet** (work package 6):

- The obstacles as things of the world: the prebuilt Science park's obstacle records are fixed-item records at the sentinel cell, which OpenTPW skips, and
  1703 has no scene record. They are not built, drawn or saved, so `0x647850` finds none in a level.
- The opening `0x5E8BD0`: for an obstacle without `GateTo`, each `AfterShape` cell at `(ShapeOffsetY + row) × 128 + 1 + ShapeOffsetX + column` clears the
  map record's dword `+0x24`, and a cell other than `%` is set to type 0, then its value (`0x67AD00`), then `0x67FB80` and `0x67CDC0`; a cell on the map border
  becomes type 5. Then `ReplaceWithID` places its type at its own shape offset (`0x5DB1B0`), or without one `DeleteAfterUse` decides whether the thing goes.
  OpenTPW's cell map has no `+0x24` word and the Theme Park Inc meaning of `0x67AD00`, `0x67FB80` and `0x67CDC0` is not read.
- The placement in a new world (`0x647BF0`), which writes `Shape` and sets `+0x24 = 1`.
- The lake: the level still draws `dlake.MD2` from `terrain.wad` (`[EXT:tpi-lake-drained]`), not the 1703 object.
- The 1750 zone switch at the camera's end, and `GateTo` travel.

## 7. The mission window

The "Current Objective" window (`ThemeParkIncMissionWindow`; builder `0x55A7F0`, procedure `0x55BA70`, layout table `0x9BE808` with 34 controls, generated by
`tpi_exe_layouts.py` with the Challenges tab's own table `0x9B54CC`) is letter-like: the title UITEXT 461, the Objectives and Challenges tabs (463, 464; the current
tab is disabled), the mission title `OPERATION[counter]`, the `objectim` picture frame `0x9BECCC[counter] − 1`, From/To/Subject (Mr Maybury, the player, the
mission title; labels yellow `(0xEA, 0xEF, 0x66)`), Mr Maybury's mugshot, the body frame, the page toggle (988 "Go to Details" / 989 "Go to Briefing") and Proceed (82).

- **Briefing (`0x55D520`).** Mission 0: `MISSION_DESC[0]` with status 0's target as `{p0}` ("get 40 people"). Later missions: `MISSION_DESC_GOTPROMO[counter]` when
  `Promotion.Number` is set, the rank is at least it and the counter is not 2; else `MISSION_DESC[counter]`. Then a line feed and the signature (173, 174).
- **Details (`0x55C350`).** In the ending zone an obstacle mission in state 1 shows the research lines (UITEXT 86, 103, 105 without a scientist, 106; 104 never
  shows because every world builds the research manager, `0x648423`), or 86 and 91 with the project's progress; states 3 and 4 write nothing, so the last text stays
  and gains a line feed (on every tick once the refresh runs; the buffer at `+0x13B2` holds 0x9C4 wide characters, but the appends `0x871475` take no size and would
  run past it, so OpenTPW stops the text at 0x9C3 characters). A mission without an obstacle, or another zone below the required rank, lists the objectives (`0x55CA60`): 83, the promotion line (99 with
  the rank's name, or the hint by counter 94 to 102; any other counter repeats 83), one requirement line per exit statistic with a type (table `0x9BED18`, STAT_TITLES
  by type), the required challenges (3, always "Polar Zone" in the text), the golden tickets (85, 13/14), then 86, the position (98), one status line per statistic,
  the challenges won (4) and the tickets held (15). In another zone at or above the required rank (also when none is required) it is 86 and 75 with the ending zone's
  name. Integer arguments print with `%d` (`0x8942E0`).
- **Obstacle state 2 (`0x55D1B0`).** The details page shows the group `0xCEC63`: 84, the tickets line (85 and 13/14), and 86 with the tickets held and 93 or 92 by
  the staff HQ's 20 slots. The slots and the staff list's rows (`0x55E020`) are not modelled, so it says 92 and the list has only its headers 87 to 89
  (`[EXT:tpi-capability-gates]`).
- **Proceed (`0x55BD79`).** Enabled by each refresh when MetRequirements (`0x55A620`) holds; a click without it does nothing. An obstacle mission posts message 0x24
  with the current obstacle (`park+0x1EAB6E`), which the dispatcher `0x436DD0` delivers at once in ascending thing handle order, to the staff HQ (`0x634D20`) and then the controller; any other runs
  OnMissionComplete (`0x5F0770`); the window closes. OpenTPW's window calls `ParkEconomy.PressThemeParkIncMissionComplete`, which sends 0x24 through `ThemeParkIncObstacleRemoved` to both. When the requirements are met at opening the builder
  also starts a particle burst at the button (`0x500B70` → `0x7BACC0`), which OpenTPW does not draw.
- **Refresh and lifetime.** Opening arms a 4000 ms timer; its expiry sets `0xA61814`, from which every tick refreshes the window (and a reopened window refreshes at
  once). Only the close and cancel buttons (id −2) clear it. In obstacle state 2 a window opened without the staff list is built again. The builder makes no pause
  request (as the letter's), clears the career's `+0x27` and resets the page to the briefing. It first sends message 5 to the HUD's registered window (`0x5013C0`,
  `0xA568BC`), so an open letter closes as its close button closes it (`0x52698F`; the bank's and the firing letter then lose the park) before the window is built. The Challenges tab closes the window and opens the challenges window
  (`0x529960`), which is not built: the text HUD's challenge panel stands in for it.
- **Openers.** The key table's Objectives row (`0x9A8A14`: O, no modifier, `0x42F020`, not in game mode 3), the HUD buttons (`0x54AC0F`, `0x54F7C1`), a click on the
  `ObstacleOpened` thing (`0x5068C3`), the challenges window's Objectives tab (`0x52A60F`), a letter's go-to kind 0xC (`0x551461`) and, three flight updates after a
  zone-arrival (mode 2) camera flight, the first briefing (`0x5D0369`). OpenTPW has the key and the text HUD's button. The letter go-to is ported but not reachable in play yet, because the slice files no letters without the tag tube; the flight is ported
  separately.
- **Not ported:** the body's scroll text (`0x52C4C0`; the body shows from its top, as at every opening, and the slider does not move it), the interface sounds
  (`0x1B6`, `0x277`), the per-mission advisor panel (`0x5F16C0`, `0x93E3E0[counter]`) and the advisor panel `0x1FA` on closing.

**The "Objective Complete!" letter (`0x5CEAE0`).** Subject TAG_SYSTEM 168, from Mr Maybury to the player, body `OBJECTIVE_COMPLETE[completed]` and the signature,
dated by the tag system like the bank's letter (`ThemeParkIncObjectiveLetter`). Its go-to (`0x5CEC30`, table `0x5CED9C`) is, by completed mission: 0 the thing of type
0x6A7, 1 the mission window, 2/3/4 kind 0x12 with 2/1/0, 5 type 0xE75, 6 the challenges window, 7 kind 0x12 with 1, 8 type 0x6A6, 9 challenges, 10 kind 0x12 with 2,
11 type 0xA8F, 12 type 0xA8E, 13 challenges, 14 kind 0x12 with 0, 15 type 0xE77, 16 challenges. The letter window's go-to (`0x526AA5` → `0x5511B0`) opens the mission
window or the challenge panel; a thing's camera move (`0x5033C0`) and kind 0x12 (`0xA59CC0` → `0x521C90`, not read) are not ported, and the obstacle things are not in
OpenTPW's world yet. The tag system opens a letter's window at once only for kinds 6 to 8 (`0x5CD060`); this letter (kind 4) becomes a row of the tag tube that the player
opens. The slice has no tag tube, so the letters stay in `ParkEconomy.TpiObjectiveLetters`.
