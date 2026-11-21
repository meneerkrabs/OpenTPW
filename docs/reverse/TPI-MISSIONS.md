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
  IsMissionComplete. Any other mission is IsMissionComplete. The staff HQ's slots belong to staff roles OpenTPW does not port, so an obstacle mission's
  requirements are never met (`[EXT:tpi-capability-gates]`).
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
  sets the career's `+0x20`. OpenTPW records the letter (`ParkEconomy.TpiObjectiveLetters`); its window is not built yet.

## 4. Messages

The controller's handler `0x5F0F50` ends every message with the 0x27 broadcast.

| Message | Sender | The controller |
| --- | --- | --- |
| 0x23 obstacle researched | the research lab's kind-5 project | `+0x1B = 1`; the next update discovers the obstacle staff's abilities, sets state 1 and flies the camera to the current obstacle (anim 1) |
| 0x24 remove obstacle | the mission window's or the remove button | state 2, the message's thing, camera (anim 2), career `+0x13E5 = 1` |
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
that was not given. Until the flight is ported, the controller's completion paths are exercised by tests that deliver 0x29 themselves, and the slice offers
no Proceed control.

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

**Things of the world.** The prebuilt Science park's obstacle records are fixed items at the sentinel cell, and the lake 1703 is the one attraction thing without a
scene record; the catalogue now reads each zone's `obstacles` folder (`ObjectCategory.Obstacle`, a fixed item: `Info.DontApplyOffset 1`), so the level builds
1702, 1701, 1700, 1750 and 1798 from their records and 1703 from its thing, each with its model, its script and its saved thing id (27 to 32; a loaded park
keeps its things' ids, and the World variable `CurrentObstacle` names thing 29, the 1700 gate). They are drawn from the sentinel anchor like every Theme
Park Inc fixed scene record (`[APPROX:RIDES-040]`).

**Cells (`0x647BF0`, `0x5E8BD0`).** A map square's dword `+0x24` is the obstacle cell flag: the placement sets it with the type and the owner word, the opening
clears it, and only the pick `0x666BE0` and the cursor help at `0x504A0C` read it (the cell types do the blocking: `H` is type 8, a terrain hole; `X` type 5,
blocked land). A Theme Park Inc map cell is saved with 56 bytes, Theme Park World's 52 and this dword; in the prebuilt it is 1 on exactly the obstacle
footprints' non-zero cells. OpenTPW's cell map keeps it as `ParkCellMap.ObstacleFlagAt` (never set on a Theme Park World map) and a layout snapshot as
`TpiObstacles`. The level takes the prebuilt's obstacle cells (type, owner word, flag) as saved; `ThemeParkIncObstacles.Place` ports the placement rule:
every `Info.Shape` cell other than `%` at `(ShapeOffsetX + column, ShapeOffsetY + row)` is set to type 0, then its value, and a non-zero one gets the
flag and the owner word of the anchor `(MapOffsetY + ShapeOffsetY) × 128 + 1 + MapOffsetX + ShapeOffsetX`; over the Science level map it writes the
prebuilt's saved obstacle cells exactly. The square writer `0x67CB70` changes only the type word (Theme Park World's clears the owner word, the links and
the placement counter with type 0) and counts a placement when the same non-zero type is written again. The opening (`ThemeParkIncObstacles.Open`) is the
order of `0x5E8BD0` above; the 1703 lake's `ReplaceWithID` places 1704 without cells of its own and destroys the lake, `DeleteAfterUse` destroys, and an
obstacle with neither stays. Before the opening the park camera's target goes to the obstacle (`0x466050`: the anchor cell plus half the shape's width on
both axes).

**Not ported yet:**

- The placement in a new world: the order in which `0x647BF0` walks the object infos (the info list `+0x0..+0x528`, filled in registration order) is not read,
  and the thing ids of a new world depend on it, so a world without the prebuilt (Water, Arabian) builds no obstacle and no camera path. The rule itself
  is ported and tested.
- The tile refresh `0x67FB80` (type 8 draws no ground); the level's heightfield already leaves the lake's holes, which 1703's and 1704's models cover.
- The pick and the cursor help over the obstacle cells, the obstacle refresh `0x648C90`, the particle effects 81/77, and the gates' travel (`GateTo`) and the
  1750 gate's zone switch at its camera's end (a later package).
