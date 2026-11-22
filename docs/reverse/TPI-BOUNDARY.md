# Theme Park Inc: the park boundary (owned land, hoardings.map and extents)

This note records how Theme Park Inc decides which land the player owns, how the land grows during the career, and what OpenTPW ports of it. It uses the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) and the shipped data; the game was not launched. The code is `World/ThemeParkIncLand.cs` (land), `World/ThemeParkIncCatalogue.cs` (catalogue gate) and `Level.OriginalCellMap` / `Level.ApplyThemeParkIncExtent`; the tests are `ThemeParkIncLandTests`.

## Ownership is one bit

A map square is a 72-byte record at `park + 0x418 + id * 0x48` (id = y * 128 + x + 1). Its flag word `+0xE` carries `0x40` for land the park does not own and `0x20` for NoModify, the same bits as Theme Park World (`ParkCellFlags.Unowned`, `ParkCellFlags.NoModify`).

[BIN:TPI-EXE:0x67F970 map square init] The world init runs this for every square: after the type comes from the MAP attributes, every square whose flag word lacks `0x20` gets `0x40` (on terrain at `0x67FA7D`, off it at `0x67FAEB`). A new world therefore owns only its NoModify squares until the fence setup runs.

## The fence setup of a new world

[BIN:TPI-EXE:0x677D35 fence posts] The fence setup `0x677CF0` (Theme Park World's `0x5323F0`) reads the entrance's two cells through `0x5D7A70`, which returns `FixedItemInfo.EntranceAPosX/Y` (`0xA025D0`, `0xA025D4`) for entrance 0 and `EntranceBPosX/Y` (`0xA025D8`, `0xA025DC`) for entrance 1, in the order of the key table at `0x99BCC8`. Post B is entrance 0 moved 4 cells west, post A entrance 1 moved 3 cells east. Every zone's `Standard.sam` names (47, 17) and (48, 17), so post B is (43, 17) and post A (51, 17).

[BIN:TPI-EXE:0x677E90 no career fence] The fence clicks run only with the debug flag `0xA2738C & 0x400` (six fixed clicks) or in coaster-edit mode (game type `[0xC65B80] == 3`, the clicks of `cem_Hoardings.sam`). A career or online game lays **no fence posts**. `Hoardings.sam` is read only by `0x678270`, which has no caller; `EndHoardings.sam` is loaded and never read; a mission's `BoundaryClicks[]` is read only by `0x678470`, which has no caller either.

[BIN:TPI-EXE:0x677F89 gate strip] For x from post B.x + 2 while x < post A.x, the squares (x, A.y) and (x, A.y + 1) clear `0x40` (`0x67ED10`) and refresh (`0x67FB80`): x 45 to 50 on rows 17 and 18.

[BIN:TPI-EXE:0x67806A starting land] Outside debug and coaster-edit mode the setup then loads `<level>\hoardings.map` (`0xB1DFF8`) with the land loader.

## The land loader `0x678B00`

Both `hoardings.map` and `extents/extent.NNN` are 16,384 bytes, one per cell, no header.

- [BIN:TPI-EXE:0x678B2F land loader] The outer loop is x (0 to 127), the inner loop y, so byte k is cell (k / 128, k % 128): the file is transposed against the map's row order. Each byte is read on its own; a failed read returns 0 at once and keeps the cells already applied. A file that cannot be opened returns 0 before touching a cell.
- [BIN:TPI-EXE:0x678B80 border skip] A byte is applied only when it is 0 and the cell is not on the map border (x and y neither 0 nor 127). Any other byte does nothing: **land is never taken away**.
- [BIN:TPI-EXE:0x678BE0 owned square] An applied byte clears the square's `0x40`.
- [BIN:TPI-EXE:0x678BFB post removal] It then looks at the square's structure record (an extension through its parent). A hoarding post (type `0x19`) is removed (`0x685DE0`, then `0x65CCE0` and `0x680A10` twice). Every record clears its own `0x40` and refreshes its mesh (`0x685A70`).

OpenTPW's Theme Park Inc worlds hold no fence posts (no career fence, and the prebuilt park has none) and its track layer has no `0x40` bit, so the post branch and the record step have nothing to act on. A file whose owned cells include a post is refused as a whole before any cell changes, rather than guessed at. **Follow-up:** the real post branch (`0x678C37..0x678C7D`: clear `0x20`, remove the post with `0x685DE0`, then `0x65CCE0(0)` and `0x680A10` twice) belongs with coaster-edit mode, the only mode that lays posts.

## Who calls the loader

[BIN:TPI-EXE:0x5F0B66 mission extent] Only two callers exist. The fence setup above loads `hoardings.map` for a new world. The mission tick `0x5F0940` loads `extents\extent.%03d` (`0x9CF714`, under the data root `0x771420`) when the mission's `Completion.LoadNewHoardings` (`career+0x10DC`) is 1, with the mission counter `career+0x105C` **before** `0x74C1B0` advances it. The extents folder is shared by all zones; the file is applied to whichever park is loaded.

Extent files exist for exactly the missions with `LoadNewHoardings 1`: 0, 1, 3, 5, 6, 8, 9, 11, 12, 13, 15 and 16. By each mission's `EndingTheme` (1 science, 2 water, 0 arabian) they belong to science 0, 1, 8, 9; water 3, 11, 12, 13; arabian 5, 6, 15, 16.

A loaded park never reloads either file, so its ownership is the squares' saved flags (the cell serializer at `0x5C19C9..0x5C1D1A` writes `mFlags`). Whether a GMS load runs the world init before the cells are restored is unread; it does not matter while the saved flags overwrite it.

## Owned squares per zone

`ThemeParkIncLandTests.EachZoneGrowsThroughItsExtents` checks these counts of squares without `0x40` on the new world of each zone and after each of its extents, in mission order:

| Zone | New world | After the extents |
| --- | --- | --- |
| science | 1259 | 3030, 4391, 6360, 7500 |
| water | 1330 | 2456, 4042, 6225, 7500 |
| arabian | 2323 | 3476, 4723, 6325, 7500 |

The campaign plan's counts (science 1259, 3066, 4503, 6472, 7640; water 1343, 2523, 4154, 6337, 7640; arabian 2323, 3517, 4835, 6465, 7640) are the number of 0 bytes in the files, border included. The loader skips the border, so those border zeros never become owned (`extent.000` alone has 36, the water `hoardings.map` 13); the test asserts both sets of numbers. Every zone's NoModify squares and gate strip lie inside its `hoardings.map` land, so the new world's count equals the file's.

[DATA:PreBuilt/prebuilt.TPWS:map flags] The prebuilt Science park, which a new career starts from, stores exactly this land: its 1259 squares without `0x40` are the science `hoardings.map`'s 0 bytes off the border, and none of its squares holds a fence post. `AFreshScienceWorldOwnsExactlyThePrebuiltParksLand` checks that a fresh science world (init, gate strip, `hoardings.map`) gives the same flag on all 16,384 squares.

## What the boundary restricts

- [BIN:TPI-EXE:0x67E4AC cell validator] The cell validator `0x67E320` (Theme Park World's `0x535670`) returns refusal 1 for a `0x40` square in every tool mode that gets past its early exits (modes `0x1F`, `0x22`, `0x3A`/`0x3D`/`0x3E` and `0x36`/`0x88`/`0x89` leave earlier; `0x17` and `0x1B` are treated as 4). The path, queue and line tools (`0x660D80`) call it.
- [BIN:TPI-EXE:0x668603 object footprint] The placement routine `0x667CB0` (Theme Park World's `0x528A70`) validates footprint cells through `0x67AD00(0x204)`; the flag `0x200` only asks the validator in mode 4 (`0x67AD19..0x67AD2B`). Past the NoModify exit and the owner lookup a `0x40` square is refused. Object placement on unowned land is therefore refused, as in Theme Park World.
- The staff drop `0x4D4EB0` tests `0x40` first (already ported, `ThemeParkIncStaffWorld.Drop`).
- There is no Buy Land tool: `Costs.MapCell` is read by no instruction found, and OpenTPW gives a Theme Park Inc park no `ParkLand`, so `BuyLandTool` stays null.

OpenTPW's existing rules already refuse unowned cells for these tools (`ParkPathBuilder.Validate`, `QueueSegmentReducer`, `ParkObjects.Check`), so they now apply to Theme Park Inc land unchanged. **Not read:** whether guests or free-walk cameras are limited by `0x40` in Theme Park Inc (guests walk the path graph only, which can reach unowned land only through paths built on owned land), the callers of `0x683850` (which blocks a hoarding post, type 7 or a `0x40` record), and the zone check on the obstacle-completion path before an extent is applied.

## How the boundary is drawn: not read

[BIN:TPI-EXE:0x4A5E30 land refresh] Theme Park World darkens unowned land: its land refresh `0x450D10` sets bit `0x400` of the landscape word and the terrain draw halves the colours (`UnownedLandDim`). Theme Park Inc's refresh is different: it clears `0x400` and the bits `0xF000`, and sets bits `0x1000` to `0x8000` from the `0x40` of the square and its neighbours (`0x4A5EE2`, `0x4A5F4D`, `0x4A5FB9`, `0x4A6024` and the combinations after them). How the terrain draw uses those bits is not read, and neither is who instances the `ho_*` fence models of `hoarding.wad` (the only instancer `0x65D5A0` has no static caller). OpenTPW therefore draws Theme Park Inc's land as before, with no dimming and no fence (`Level` leaves `OriginalTerrain.LandFlagsSource` null for Theme Park Inc).

## Unread items

These stay open and carry no register entry, because nothing is approximated for them: OpenTPW leaves them out.

- The boundary drawing: how the terrain draw uses the landscape word's bits `0x1000..0x8000` that the land refresh `0x4A5E30` sets from the squares' `0x40`, and who instances the `ho_*` fence models.
- Whether guests, staff walks or a free-walk camera are limited by `0x40`; the callers of `0x683850`.
- The zone check on the obstacle-completion path before an extent is applied.
- Whether a GMS load runs the world init before the cells are restored.

## The catalogue's mission gate (`0x57CA20`)

The list builder of one catalogue tab skips, for each object type:

- [BIN:TPI-EXE:0x57CAB6 hidden types] a type whose `+0x5C` is nonzero. `+0x5C` is `Info.IsSpecial`: in the Info key table at `0x9A2D38` it is the word eight words before `Info.OverwritePriority`, which the validator reads at `+0x7C` (`0x67E464`). Science's special rides are the Victorian house (1110), the water ride (1160), the tour ride (1170) and the log mill (1180), the prebuilt park's rides. Also skipped: `+0x65C` nonzero, `+0x600` other than the tab, and the add-ons (`+0x3D4` `AddOn.UpgradesId`, `+0x3D0` `AddOn.UpgradesAnyRide`).
- [BIN:TPI-EXE:0x57CB00 thinking space] a type whose `+0x1B4` equals 1 while the mission number (`0x5F03A0 +0xC`) is below 1. `+0x1B4` is `UsageInfo.ThinkingSpace` (the UsageInfo key table ends at file offset `0x5A51C8`; the word is 26 before `UsageInfo.AppearanceEffect` at `+0x21C`). The research lab (1404) carries it.
- [BIN:TPI-EXE:0x57CB2A training room] a type whose `+0x1B8` (`UsageInfo.TrainingRoom`, the next word) is nonzero, unless the mission number is above 1, or is 1 and the obstacle status of `0x5F01D0` is 2 (the Park Expansion research is done). The training room (1440) carries it.

`ThemeParkIncCatalogue.IsListed` ports these rules as a function of the type's settings, the mission number and whether the obstacle research is done. The experimental HUD's build list applies it with `Level.ThemeParkIncMissionNumber` and `Level.ThemeParkIncObstacleResearchDone`, which stay 0 and false until the career runs, so the four special rides are no longer offered; they are hidden in every mission, not only before the career starts (the Science smoke now arms the Bellows). The research lab and the training room are Facilities (`Info.WhichUIType` 5): the builder's `+0x600` is the type loader's copy of `Info.WhichUIType` (`+0x70`, `0x43D014`), compared with the tab, and the panel's fifth tab (`0x57A598`) lists type 5, which the catalogue now loads (docs/THEME-PARK-INC.md, "Facilities"). The `0x5CFCF0` record's byte `+0x10` is the research gate (`ThemeParkIncResearch.IsResearched`). The rest of the builder (the `+0x65C` word, the mode-3 exception and the row's built and new marks) is not ported here.

## The API for the mission controller

`Level.ApplyThemeParkIncExtent(int index)` applies `extents/extent.NNN` for the mission counter before it advances to the live world's cells with the loader's rules and returns the loader's result (false for a missing or short file). The caller decides when: the mission tick calls it only for a mission with `Completion.LoadNewHoardings 1`.

## Saves

The ownership is world state. The Science continuation save and the layout checkpoint store it as one bit per cell (`ParkLayoutSnapshot.CellMapData.TpiUnowned`, Base64 of 2048 bytes, row order); the cell records then omit the unowned bit. Nothing reloads `hoardings.map` on a load, as in the original. A continuation or checkpoint written before this port carries no bit map: it would restore every square as owned, which the original never reaches, so it is refused (`ParkResumeSaveFile.RequireThemeParkIncLand`; the checkpoint asks to be saved again).
