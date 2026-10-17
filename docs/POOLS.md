# Pools (`opentpw.pools`)

An official extension (docs/MODS.md), **off by default**, in which the player paints **pool water cells** of any size and shape on the park grid. It is modelled on the
pool tiles of Roller Coaster Tycoon 3 Soaked! (ideas only; no asset of that game or of Theme Park World is used or shipped). The original Theme Park World has no pools: everything
here is OpenTPW's own and carries `[EXT:pools-*]` labels in the code and in this page.

**Phase P1** is the cell type, the paint tool, pricing, saves and rendering. **Phase P2** (below, [Changing rooms and swimming](#changing-rooms-and-swimming-phase-p2)) lets guests really swim: a changing room is the pool complex's
entrance, guests choose it like an attraction, change, swim freely through the connected pool cells with whole-body swim sprites, climb out, change back and return to the path. What is not done is in [Not done yet](#not-done-yet).

| Label | What |
| --- | --- |
| `[EXT:pools-extension]` | the extension, its switch (`FeatureGates.Pools`) and the save rule |
| `[EXT:pools-cell-type]` | the cell type `ParkCellTypes.PoolWater` |
| `[EXT:pools-cell-rules]` | the `CanChangeCellType` rule |
| `[EXT:pools-tool]` | the paint tool, the builder, its placement rules |
| `[EXT:pools-terrain-rule]` | the ground rule |
| `[EXT:pools-cost]` | the price and the refund |
| `[EXT:pools-save]` | saves, validators and the pins |
| `[EXT:pools-ui]` | the buy-list rows, the hover help and messages |
| `[EXT:pools-render]` | the mesh, the textures and the water shader; the swimmers' sprites |
| `[EXT:pools-changing-room]` | the changing room object and its placement rule |
| `[EXT:pools-swim]` | the swim session: guest states, steering, the numbers, saves |
| `[EXT:pools-content]` | the extension's own shipped content (`content/pools`) |
| `[EXT:pools-slides]` | water slides: the tower, the track, the rules, the ride and the activity |
| `[EXT:debug-pool]`, `[EXT:debug-swimmers]`, `[EXT:debug-slide]` | the smoke-only options that build a pool, a room, swimmers and a slide for captures |

## The extension and the save rule

`opentpw.pools` is registered in `OfficialExtensions` with `DefaultEnabled: false` and `AffectsSimulation: true`; the gate is `FeatureGates.Pools`, read the way
`FeatureGates.CrossEditionObjects` is. Turn it on in `mods.json` (`"opentpw.pools": true`), on the Extensions and mods screen, or for one run with `--enable-mod opentpw.pools`.

**A save records the extension only when the park actually has pool cells.** `AffectsSimulation: true` alone would tag every save of every park once the extension is on
(`ModSet.SimulationMods`), so the extension is one of `ParkResumeMods.UsedOnlyExtensionIds`: `ParkResumeMods.Capture(active, inUse)` leaves it out unless `inUse` names it, and
`Level.CaptureResumeSnapshot` passes `ParkResumeMods.InUse(cells)`, which names it when the cell map holds a `PoolWater` cell. So:

- a park **without** pool cells (and without a changing room) saves **byte-identically** with the extension on or off (no `Mods` field; `PoolsLevelTests.RealCapturesWithoutPoolCellsAreByteIdenticalWithTheExtensionOnAndOff`
  compares two real `Level.CaptureResumeSnapshot` captures). A park whose last pool cell was erased records nothing either, but its other bytes are not those of a park that never had water: the cell map keeps
  the block stamps and the version of the writes;
- a park **with** pool cells records `opentpw.pools 1.0.0` (the digest of no bytes, like every extension) and loads only with the extension on: with it off the load refuses with
  `needs opentpw.pools 1.0.0`, and the load and Continue lists keep the save and add "(requires mod opentpw.pools 1.0.0)";
- a save without the record loads with the extension on or off (`ParkResumeMods.Unmet` does not ask for an unrecorded used-only extension: an active used-only extension needs no record, a recorded one must be active);
- two guards stop a save from hiding its water: the layout validator refuses a pool cell while the extension is off (`cell (x, y) is a pool cell, which needs the opentpw.pools extension`),
  and the shape check refuses pool cells in a save that does not record the extension;
- exporting a park as a `.tpwpark` package is refused only while the park has pool cells (`ParkSharing.ModsRefusal(level)`), because packages record no mods yet.

**The asset manifest holds only what the park places.** A save's `AssetHashes` hold, and a load requires, the `mod:opentpw.pools:` keys of the extension objects the park has **placed** (a changing room; none for a park of pool cells only),
`ParkResumeMods.AssetsForUse`. The keys of unplaced extension objects are ignored in both directions, so a pool save made before the room existed still loads, and adding objects to the extension (more rooms, extras, slides) never invalidates an older
save. A placed object keeps the exact-hash check: **changing the files of an object a save places changes that save; adding objects does not** (`PoolsContentTests`: a pool save without the room's keys loads, a placed room requires its keys,
a placed room with another hash is refused).

A used-only record was feasible because `ParkResumeMods.Capture` sees the live cell map; nothing else had to give way.

## The cell type

`ParkCellTypes.PoolWater = 0x40` (64), also `ParkCellType.PoolWater` and `CellToolMode.Pool` (a tool's mode is the cell type it writes). No original type uses the value:
the original's cell types are 0, 1, 2, 3, 4, 5, 7, 9, 10, 11 to 13, 16, 18, 0x15, 0x19 and 30 (`ParkCellTypes`, `TrackCellTypes`, docs/MAP.md; the placement writes only the shape types 0 to 23, the map
init 0, 1, 2, 7 and 30, and the saves hold the type in one byte). `PoolsTests.ThePoolValueCollidesWithNoOriginalCellType` pins that.

- **Rules** (`ParkCellMap.CanChangeCellType`): pool water goes on **empty cells only** (or stays where it is); clearing (type 0) always works, so erasing returns the cell to empty; every
  other type over a pool cell is refused (paths, queues, objects, tracks, entrances). Objects are refused through the existing reservation (`Level`'s `IsReserved`: any type other than empty,
  path and the object types is taken), tracks through their own cell check.
- **Walkability stays as it is:** `GuestPathGrid.IsWalkable` is `TypeAt == Path`, so a pool cell is not walkable whatever its links, and guests on the paths route around it
  (`PoolsTests.GuestsRouteAroundPoolCellsAndNeverStandOnThem`). Walkers that leave the paths (guests set down or leaving, guard escorts, employees heading for a strike) are kept out by two small
  changes to the guest code, `[EXT:pools-cell-type]`: `WalkEdges.Blocked` blocks every step onto a pool cell for every walk mask, and `GuestWander.OffPath` skips a random candidate in the water (its draws are
  unchanged). Neither changes anything without pool cells, so no pin moved; with pools the fixed run's pin includes them (docs/DETERMINISM.md, "Pools").
- **The canonical hash** (`ParkCellMap.AddCanonicalState`) already covers every non-empty cell, so a pool cell joins it with no schema change, and a park without any keeps every pin.
- **Saves:** the layout's `Cells` records hold the raw type byte, so a pool cell is a record with type 64 and no links, queue link, owner or placement count (the validator insists).

## The tool

`PoolBuilder` (headless, CPU-only, like `ParkPathBuilder`) validates, paints, charges and erases; `PoolTool` is the stroke tool on top of it (`CellToolMode.Pool`); the HUD and the tests drive
both. `Level.PoolBuilder` and `Level.PoolTool` exist for original levels that are not read-only visits.

**Painting.** Press on a cell and drag: every cell between the previous and the current pointer cell is written (a 4-connected walk, so a fast pointer leaves no hole), the tool stays on for another
stroke, right click or Escape leaves it, Backspace takes back the last stroke (`PoolTool.Undo`: refunds a paint stroke, charges an erase stroke again; switching between the paint and the erase row keeps the strokes). A drag that leaves the park view ends there: coming back with the button held does not join the two places. The path tool's snapped straight lines are not used:
pools are areas. `PoolBuilder.Rectangle` fills a rectangle for headless callers. A refused cell is skipped and the stroke goes on; only a cell that cannot be paid for ends the stroke. The HUD plays the refusal sound and posts the message once, when the stroke ends, for the first refusal.

**Erasing.** The erase mode (a second buy-list row) takes pool cells back to empty cells the same way. The path tool's own removal is the forced `ClearCell` (Backspace, the area clear) and
refunds nothing; erasing pool water **refunds the full cell price** instead, a deliberate difference (`[EXT:pools-cost]`): a pool painted by mistake costs nothing once erased. The area clear and the
developer remove tool leave pool cells alone.

**Placement rules** (`PoolBuilder.Check`, in order; each has its own `PoolBuildResult` reason and a refusal message):

1. `ExtensionOff`: the extension is off;
2. `OutsideTerrain`: outside the map or the terrain grid;
3. `NotOwned`: land the park does not own (cell flag 0x40);
4. `Existing`: already pool water (free, no change);
5. `Occupied`: a path (a fixed one too), queue, entrance, exit, object cell, track cell or a fence post (`ParkLand.IsPost`) is there, or the host reserves the cell (a placed object, the Totem). The path
   tool has the same gap for fence posts (it does not look at `ParkLand.IsPost`); it is left as it is, because its pins and the saved parks that lay paths beside posts depend on it;
5b. `Standing`: a guest or an employee stands on the cell (`Level.IsStoodOn`), so no water is painted under anyone;
6. `Terrain`: the terrain rule refuses the cell (water, a hole, rock, the entrance area, the fixed walkway: the object build grid's `CheckTerrain`);
7. `TooSteep`: the ground is not level enough (below);
8. `NotEnoughMoney`: balance minus the stroke so far minus the price would be negative.

**The ground rule** (`[EXT:pools-terrain-rule]`). The deck follows the ground and the water of a connected pool is level, so each cell must be nearly level. The rule is the **corner height spread**: the highest
minus the lowest of the cell's four corner heights, in engine units (`PoolBuilder.CornerSpread`; a cell is 2 wide and the heightfield rises in steps of 0.2, one model unit). It must not exceed
**0.25** (`PoolBuilder.MaximumCornerSpread`: one step of 0.2, plus a little), which is a slope of about 7 degrees across the cell. The corners are the very values `LandscapeSampler` interpolates inside the
cell, and the sampler's surface of a cell never leaves their range, so the spread bounds how far the ground (and so the deck, which follows it) can lie from the level water. The original validates no slope for any
tool (tp.exe `0x535670` has no float operation), so this is OpenTPW's own rule.
Measured on the shipped land (`PoolsTerrainCorpusTests`, with `OPENTPW_GAME_PATH`): the buildable land of the four themes is already flat. About 27 to 29 percent of its cells are exactly level, almost all the
rest differ by one step of 0.2, and the rule refuses 4 of 28,000 cells (the few two-step cells, spread 0.4). It is a safeguard for steeper maps and edits rather than a limit players meet on the original levels.

## Pricing

**75 per cell** (`ParkEconomy.PoolCellCost`), the price of a pool tile in Roller Coaster Tycoon 3 Soaked!, through the economy's cell costs (`CellPurchase.Pool`, `ParkEconomy.CellCost`, `TrySpendCell`):
spent as other costs with a `CellsBought` event, tested like a path cell (the commit refuses when balance minus the price is negative, the ghost when the stroke's total exceeds the balance). The original's
balance files have no pool price, so the number is a constant of the extension. Erasing credits 75 as other income (`ParkEconomy.RefundPoolCell`) and nothing while the bank's withdrawals are disabled (a debit then charges nothing, so there is nothing to give back and paint and erase cannot make money); a stroke's `Charged` and `Refunded` are what the balance really did. A pool cell is worth what empty land is in the park value (`[EXT:pools-value]`, 2 points; it would otherwise score 0). A pool built with no economy (the headless sandbox) is free.

## The HUD entry

The original HUD has no entry for it, and the least invasive place is the buy window's **Miscellaneous** list, which already holds the two land rows (Buy Land, Clear Land). While the extension is on, two rows
follow them (`ParkHud.PoolPaintRow`, `PoolEraseRow`, ids -3 and -4, `ParkHud.Pools.cs`): "Pool water" (value 75) and "Remove pool water" (value 0). Choosing a row closes the window and arms the tool; choosing the other row while the tool is on only switches its mode. The
rows exist only while `FeatureGates.Pools`; with the extension off the list is exactly the original's. Hover help, the start hints and the "ground too steep" message are OpenTPW's own strings in
`SupplementaryStrings` (`OpenTpwText.PoolRowPaint` and its siblings), translated for all nine languages the table has (English, Danish, Dutch, French, German, Korean, Polish, Spanish, Swedish); the other
refusals reuse "You can't build here" and "Not enough money". The cursor is the path tool's (the cash pointer when the cell cannot be paid for); the hovered cell shows the blueprint marker, blue when it can be written and red when it is refused.

## Rendering

A pool is drawn **dug into the ground**, in the picture only. The terrain renderer treats pool cells as **render-only holes** while the extension is on: `OriginalTerrain.RenderHoleSource` (the level wires
`PoolRenderHoles.IsHole(cells, x, y, FeatureGates.Pools)`) leaves those cells out of the drawn ground mesh (`OriginalTerrain.HeightfieldCells`), and nothing else. The heightfield, `LandscapeSampler`, the
cell words (the real hole bit 0x1 is the heightfield's own and is not touched), placement, the cell map and every other part of the simulation never read the rule, so heights and every pin are the same
whether or not a hole is drawn (`PoolsTests.TheGroundMeshLeavesOutRenderHolesAndTheHeightsStayWhatTheyWere`). With the extension off the ground is drawn whole. The mesh is rebuilt (one model, same material) when the
set of holes changes; a stroke of pool cells changes no path or queue drawing, so those layers are not rebuilt for it (`ParkCellMap.PoolWrites`).

`PoolRenderer` (an `Entity`, created by `Level.RefreshPools` when the first pool cell exists) draws five parts from `PoolMeshBuilder`, which is CPU-only and unit-tested, rebuilt at most once a frame when the set of
pool cells changes:

- **Auto-joined edges.** Every cell is cut into 4 x 4 tiles at the rim width, **a quarter of a cell**: the two outer rows and columns are rim, the middle two water. Which tiles are coping comes from the 8-neighbour
  mask (`PoolEdges`): an edge tile is coping when the neighbour on its side is not pool, a corner tile unless both side neighbours and the diagonal between them are pool. So an interior cell has no rim, a straight edge four
  rim tiles in a row (a strip and its two corners), an outer corner seven, an inner (concave) corner one, and a lone cell twelve. All cells cut at the same breaks, so any shape joins without a seam.
- **Coping (the deck).** Flat slabs on the ground (`LandscapeSampler` through `IParkGrid.GetTerrainHeight`) lifted 0.03, textured in world units so a slab is a quarter of the rim wide, with joints and a slight bevel in the texture.
  A short skirt closes the outside of the deck, down 0.2 below the ground, so the deck meets the grass without a gap.
- **Walls and floor.** Straight down from the inside of the coping to a flat **floor 1.75 below the water** (`PoolMeshOptions.Depth`). The water of each group of 4-connected pool cells is one level surface, 0.12
  below the lowest ground of the group (`WaterDrop`), so a pool on a slope is a level basin and its deck follows the ground. The upper wall (coping to the water line) is drawn in the deck's material; the lower wall
  and the floor use `pool-basin.shader`, which darkens and tints the tiles toward the deep water colour by depth (the first texture coordinate: 0 at the water line, 1 at the floor) and adds moving caustic light on the floor.
  Two cells that meet only at a corner are two pools. The levels do not step: a pool over a long slope is as deep on its high side as the slope rises.
- **Water.** `pool-water.shader`, adapted from the unused `water.shader` (the lake entity `World/Terrain/Water.cs` is unchanged): two layers of a generated caustic texture at different scales scrolling different ways
  (so the repeat does not show), a wobble, a deep to shallow colour mix that is lighter near the edge (the first texture coordinate is the proximity to the edge, `PoolMeshBuilder.EdgeProximity`), a style glow, and
  translucency (0.72 in the middle down to 0.42 at the edge) so the floor and the walls show through.
- **The park's own look.** The deck, walls and floor are **opaque**, and all three shaders light and fog exactly as the park's ground and objects do (`test.shader`: the sun's diffuse plus 0.4 ambient, the exponential
  fog with the graphics settings' view distance scale). The pool-water shader keeps the view-distance scale `water.shader` drops. The parts are drawn opaque first, the water last. (The first version used `3d.shader`, whose
  alpha is the texture's times 0.75 and whose fog is linear: the rim looked translucent and fogged unlike the grass.) GPU code: the web build has its precompiled `content/shaders/web/pool-*.json`.
- **Not done in the geometry:** the coping's bevel is in the texture (a light edge and a shadowed edge on each slab), not a chamfer in the mesh.

### Styles

The look follows the park's theme (`PoolStyles.ForTheme(OriginalPark.LevelName)`, the way objects pick their theme's data), and all of it is one table, `PoolStyles` (`PoolStyle`: coping pattern and colours, wall
and joint colours, the deep and shallow water colours, glow). A theme without a row, such as a Theme Park Inc world, gets `Generic`; a TPI row is a new line in the table. The textures are generated in code from the
row (`PoolTextures`, 64 x 64, tiling, 16-pixel slabs); nothing from the original game and no binary asset is committed.

| Theme | Coping | Wall and floor | Water | Glow |
| --- | --- | --- | --- | --- |
| jungle | wooden planks with grain | sandy stone tiles | lagoon green-blue, turquoise shallows | none |
| hallow | dark mossy stone | dark slate tiles | murky purple deep, sickly green shallows | faint |
| space | riveted metal panels | blue-grey metal tiles | deep blue, bright cyan shallows | soft emissive |
| fantasy | pastel candy tiles (pink, lilac, mint) | pink tiles with white joints | blue-lilac deep, pink-lilac shallows | slight |
| generic | pale stone slabs | blue ceramic tiles | blue | none |

### Tests

`PoolMeshTests`: all 256 neighbour masks tile the cell exactly once and give the expected tile counts for the rim, the water and the floor; the named cases (interior, straight, outer and inner corner, lone cell) have the
expected walls and skirt; the basin has the depth, the water level and the deck heights described above; the rim is a quarter cell wide; normals are unit, the top surfaces face up, walls are vertical and the winding
agrees; coordinates are in range; the water is lighter toward the edge and cells agree at a shared vertex; for six shapes on flat and sloped ground the mesh is closed apart from the skirt-bottom edges (four per outline
side); the textures tile, have joints and a bevel, and differ by style; each theme gets its style and anything else the generic one.
`PoolsLevelTests` run on a real `Level`: real resume captures with and without pool cells, sharing, objects, access cells, track tiles and coaster pylons over water through the level's own hooks
(`Level.ConfigureObjectHooks`, `Level.ReservesCell`), guests and employees standing on cells and fence posts. The GPU side (`PoolRenderer`, the shaders, the terrain rebuild) is exercised only by the on-screen
check below.

### Checking it on screen

`--debug-pool [x,y,w,h;...]` (`[EXT:debug-pool]`, smoke-only) paints pool cells through the tool's own builder once the
original level is up. The shape is a list of rectangles, measured from the first place near the camera target (the `--capture-camera` target when one is given, so a capture is repeatable) that takes
the whole shape and charged as the tool charges. The default shape is a 6 x 4 basin, an L-shaped arm and one single
cell, which together show straight edges, inner and outer corners and an isolated cell. It needs the extension. The log
prints the shape's engine centre, which `--capture-camera` can aim at:

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --load-original-level jungle --enable-mod opentpw.pools \
  --debug-pool default --silent --capture-world pool.png --capture-frames 300 --capture-camera 28.2,-38.6,45,-90
```

First on-screen check (October 17, 2026; Jungle, 43 cells): the water animates, and the coping joins at straight edges,
inner and outer corners and around the single cell. The basin sits on top of the land, so the water reads as a shallow
tray rather than a deep pool.

Second check (October 17, 2026; all four parks, `--debug-pool default`, camera `x,y,45,-90` and `x,y,45,-150`; the camera clamps the height to 70, so a lower height gives the same view): the pool is dug into the
ground with a visible inner wall, a tiled floor seen through the translucent water, lighter water at the edge, the coping a quarter of a cell wide with joints, and each theme's own look (wooden coping and
lagoon water in the jungle, mossy dark stone and murky purple water in hallow (made purpler after the first look, October 17, 2026), riveted metal and bright cyan water in space, candy tiles and lilac water in fantasy). The images are `reports/pools-p1-<theme>-top.png` and `-angle.png`.

## Changing rooms and swimming (phase P2)

### The changing room (`[EXT:pools-changing-room]`)

A **changing room** is an object of the Features tab, available only while the extension is on: a 2 x 2 cabin whose entrance (the lower left cell of the shape `**` / `2*`, facing the path like a toilet's) is on the path side and
whose back, opposite the entrance, touches the pool. It is a facility like a toilet (a feature that guests choose, `Info.IsChoosable 1`, no queue, a stand cell in front of the entrance), but it gives no relief. Its `.sam` carries OpenTPW's own key
`Pools.ChangingRoom 1`, which the engine reads (`PoolChangingRooms.IsChangingRoom`; `RideVisitorBridge.IsChangingRoom` is set only while the extension is on).

- **Placement rule.** At least one of the **pool-side cells** must hold pool water: the cells beyond the back of the cabin, that is for every footprint cell whose neighbour opposite the entrance's outward direction is not part of the
  footprint, that neighbour (two cells for the 2 x 2 cabin, in all four rotations, `PoolChangingRooms.AccessCandidates`). The rule is the extra `ParkObjects.PlacementRule`, the level wires `PoolChangingRooms.PlacementRule` into it
  (`Level.ConfigureObjectHooks`); a refusal is `OriginalPlacementResult.NeedsPool`, which the HUD words as "A changing room must stand beside a pool". Objects over water were already refused (P1), so the cabin itself stands on dry land.
  The first of the back cells that holds water *now* is the room's **pool-side cell** (`GuestSimulation.PoolAccessOf`), where swimmers step in and out; the **pool region** is the 4-connected pool cells connected to it
  (`PoolSwimRegion`, rebuilt when a pool cell is written, `ParkCellMap.PoolWrites`). Two rooms on one pool share the water.
- **Never a research item.** The room is on offer from the start, whenever the extension is on. `content/pools/objects.json` declares `"research": false` ([MODS.md](MODS.md#objects)), so the room (and every later object of the file)
  is in no research table, never in a save's research set, does not count in the group percentages and `OpenGroups`, and does not hold back the golden ticket for "everything researched and built". A park without a pool or a room has the same research
  tables, completed set and saved research bytes with the extension on or off (`PoolsResearchTests`), and a park saved with the extension on and no pool loads with it off (and the other way round).
- **Erasing.** The pool tool refuses to erase a pool cell with a swimmer in it (`PoolBuildResult.Swimmers`) and any pool cell on the back of a changing room (`PoolBuildResult.ChangingRoomAccess`): the room needs its pool, and a save that holds a
  room records the extension even when its pool cells are gone (`ParkResumeMods.InUse(cells, poolsObjects)`).
- **Price and capacity.** Build cost 400, capacity 2, `UsageInfo.InitPricePerUse 5`, excitement 30; all are OpenTPW's own values. The park economy charges a feature nothing per use (as a toilet is free, `GuestEconomyBridge.TryPayVisit`
  books a price for shops and sideshows only), so the 5 is read only by the standalone guest simulation; swimming is free in a park, like the pools of Roller Coaster Tycoon 3.
- **The script** (`tools/pools/cabin.rse`) is the kit's placeholder pool script without heads: guests aboard are hidden (a guest in a room is "changing in"), the fill loop waits 1.5 s for the next guest and the room then holds them 3 s (`WAIT 3000`) before it releases
  them one by one, 4.5 s for a lone guest.
- **Info.Ids.** OpenTPW reserves 65000 to 65535 (pool extras 65000 to 65099, water slides 65100 to 65199, water attractions 65200 to 65299, changing rooms 65500 to 65519, the rest spare) (`PoolContent.FirstReservedId`, `LastReservedId`; inside the 5000 to 65535 range of the object ids) for the objects of the pools extension; the four themes' rooms are 65500 (jungle), 65501 (hallow),
  65502 (space) and 65503 (fantasy).

### Choosing to swim

The changing room is an attraction for the normal attraction choice, so **no new decision point and no change of `IGuestDecisions`** was needed. A guest scores it with the one score of every attraction (`ScoreAttraction`: distance, queue length, excitement against its
taste, the needs and the rest); the default policy picks it like a ride, and so does any other policy that scores attractions. `PoolsSwimSessionTests.ACheerfulGuestChoosesTheChangingRoomLikeAnyAttractionAndSwims` has a guest choose, queue, change and swim with nothing forced,
and in the fixed run with a room (below) guests choose it in a park that also has a shop. A swim-appeal term (warmer months more, rain less) is **not** part of P2: it would be an extension rule inside the host and is left for a later phase. A guest in a swim session makes
no attraction choices (only a state-6 guest decides), so the room stays an ordinary attraction for the decision layer; the views of `GuestDecisionViews` did not change.

### The swim session (`[EXT:pools-swim]`)

Host-side simulation in `GuestSimulation.Swimming.cs` and `PoolSwimming.cs`, gated by the room: a park without one reaches none of it, draws nothing from the world generator and hashes nothing extra.

| State | Value | What |
| --- | --- | --- |
| changing in | 16 (`Using`) | aboard the room, hidden: the script holds the guest for the changing time. **Deviation from the brief:** not a new enum value, because the guest is aboard the room's script exactly like a toilet visitor and a second state would duplicate every `Using` check (bridge membership, saves, demolition) for no difference in behaviour. `GuestSimulation.IsChangingIn(guest)` tells it, and the visitor window shows "Changing" |
| `Swimming` | 22 | in the water: a sub-cell position (16.16 cells), a heading of 64, a speed, a goal and its own random stream; drawn with the swim sprites |
| `ClimbingOut` | 23 | swimming back to the pool-side cell (still drawn in the water) |
| `ChangingOut` | 24 | hidden in the room, 12 park turns (about 3 s) |

When the script releases a guest from a changing room (`OnVisitorReleased`), everything else of a release still happens (the visit counts, the used history, the visit's effects) and the guest steps into the water at the pool-side cell instead of walking on. **If it cannot** (no
pool beside the room any more, or the park is closed) it walks on as any released guest. After `ChangingOut` it is put on the room's path-side exit through the existing `ReturnToPath` with the release's `ExitingRide` hold and decides again like any guest.

**Steering** (`PoolSwimming.Step`, integers only, 64 headings with a fixed table, 16.16 positions): a swimmer swims to a goal point drawn in the region (a random cell, a random point in it), picks another when it is within 0.3 cell, turns at most 2 steps (11.25 degrees) per park turn towards its
wish, which is the unit step to the goal plus a push away from every swimmer within 0.5 cell, and drifts a little (a heading step one turn in eight). It keeps to the water: the **free water** of a cell is the part 0.37 cell from the sides without a pool neighbour (the rim is a quarter of a cell and the body takes 0.12) and outside the corner
square of an inner corner (`PoolSwimRegion.IsWater`); when the point ahead is not free water it tries the headings either side in turn and, boxed in, stands and picks a new goal. It never moves nearer than 0.25 cell to another swimmer (a move that increases the distance is always allowed, so swimmers who start on one point swim apart). On the way out the crowd within 0.7 cell of the steps is
ignored so nobody blocks the steps. Speed is the guest's base speed (60 to 140) times 90 in 16.16 cells per park turn: 0.08 to 0.19 cells a turn, about 0.33 to 0.78 cells a second (a park turn is 0.248 s).

**Randomness.** One draw of the world generator seeds the swimmer's own xorshift32 stream when the swim starts (`BeginSwim`, only for a guest released from a room); every later choice (the length, the stroke, the heading, the goals, the drift) comes from that stream, saved with the guest. A park
without a changing room takes no draw, and one with a room perturbs the other guests only by that one draw per swim.

**Numbers** (`PoolSwimSettings`, all OpenTPW's own, none measured):

| What | Value |
| --- | --- |
| swim length | 160 to 279 park turns (40 to 70 seconds), drawn at the start |
| stroke | front crawl 50 %, breaststroke 30 %, treading water 20 % (sprite slots 8, 9, 10) |
| climbing out | ends within 0.3 cell of the steps; at most 160 turns, then the guest is put in the room |
| changing back | 12 park turns |
| happiness | +12 (meter 0 to 100) when the guest is back in the room |
| other meters | unchanged by the swim (hunger, thirst, toilet and the rest run as in every state); the brief allowed "a little" relief and none is given |

**What else knows the new states.**

- *Catchable:* `IGuardGuests.IsCatchable` is unchanged, so a guard cannot catch a swimmer (it keeps to the water cells, and walkers are kept out of pool cells since P1); a guest in the room is hidden and out of `AnyGuestWithin` and `WalkersOn`.
- *Activity counts* (`GetStatistics`): `Swimming` and `ClimbingOut` count as walking, `ChangingOut` counts with the riders (indoors).
- *Leaving the park:* only a guest that decides (state 6) goes home, so a swimmer finishes its session first (40 to 70 seconds), then decides; `PoolsSwimSessionTests.AGuestWhoWantsToLeaveFinishesItsSwimFirstAndThenGoesHome`.
- *Park close:* a closed park ends the swim at once (`Payments.ParkClosed`: the swimmers swim to the steps, change and leave), and a guest released from a room while the park is closed does not swim.
- *Demolishing the room*: the runtime marks the room's visitors stopped (`IRideVisitorBridge.IsStopped`) before it releases the guests aboard, so a guest changing inside steps out on the exit like a rider of any attraction and starts no swim (no world draw) and takes no second treatment from `Unregister`;
  swimmers, climbers and guests changing back are then set down on the room's exit cell by `Unregister`, deciding again, with the small happiness loss riders get (`PoolsLevelTests.DemolishingAChangingRoomThroughTheLevelStartsNoSwimForTheGuestsChangingInside` goes through `ParkObjects.Remove`).
- *Pool capacity:* a pool takes at most **2 swimmers per water cell of its region, at least 4** (`PoolSwimSettings.SwimmersPerWaterCell`, `MinimumCapacity`, `PoolSwimRegion.Capacity`), whichever room they came from. `BeginSwim` checks it: a guest released while the water is full leaves the room the normal way (exit, `ExitingRide`, deciding again), takes no draw and keeps the visit.
- *The pool is split* (a cell in the middle erased): the region is rebuilt around the room's pool-side cell. A swimmer left in a part that no longer belongs to the region is put back at the middle of its cell when that cell is still in the region, else at the pool-side cell (`SwimStep`); this is kept on purpose, no swimmer is lost and none ends up on the deck.
- *Census:* a guest in a swim session counts as in the park wherever the cell test would leave it out (`ParkGate.CountCensus`: the rating's visitor term, the gate that closes only on an empty park, the end of a strike; `GuestSimulation.IsCountedInPark`: the thoughts ranking, the balloon and costume shares). The thought pick and the leaving recall keep the cell test: a swimmer neither thinks nor leaves. With the extension off nothing changes.
- *The pool is gone* (every cell of the region erased, or the room's back cells no longer water): the session ends at once on the room's exit; *the water shrinks* under a swimmer: it is put back in the water within a turn.
- *Staff:* a staff member cannot be put down in the pool (`StaffPlacementRules.IsDropCellType` knows only grass, path, queue and queue end); the strike and escort walks never enter pool cells (P1).
- *Walkers' lists:* a swimmer is in no cell list that walkers separate by (`GuestSimulation.Occupy`).
- *Balloons:* the balloon is put away for the whole session (state 16 does it already) and comes back on stepping out.

**Saves.** `GuestSnapshot` has optional fields `SwimHeading`, `SwimTurns`, `SwimStroke`, `SwimGoalX`, `SwimGoalY`, `SwimHasGoal`, `SwimRandom`, written only inside a session (the JSON ignores default values), so older saves load with no swimmers and a park without swimmers saves the same bytes. The validators accept the three
new states for a guest bound to an attraction, require a changing room for them and refuse swim fields outside a session. The canonical hash adds the swim fields only for a guest in a session (`GuestSimulation.AddCanonicalState`). The validators also bound `SwimTurns` per state (a swim by its longest, 279; the climb out by `ClimbOutLimitTurns`, 160; changing back by 12; at least 1) and refuse a session bound to an attraction that is no changing room. The asset manifest holds the extension's own files
(`mod:opentpw.pools:` keys) only for the objects the park places (see [the save rule](#the-extension-and-the-save-rule)): the catalog lists every room whenever the extension is on, and the manifest would otherwise make every park with the extension on refuse to save.

### Rendering

`PoolSwimSprites` loads the eight swim sets from `content/pools/esprites/Swim` beside the game's kid atlases: set n belongs to guests of type n (both lists are in name order). `Level.SwimmerRenderer` is a second `GuestRenderer` over them (`Draws` the swimming guests, `SlotChooser`
picks slot 8, 9 or 10 and a frame from the clock at 6 frames a second, 3 for treading water, `FeetChooser` puts the hotspot, the torso at the water line, on the water surface of the cell's group, `PoolRenderer.WaterLevelAt`). It is made right after the pool renderer, so it draws after the water, and the water writes no depth
(`MaterialFlags.DisableDepthWrite`), so the lower half of a sprite, tinted blue by the art, shows through the surface; the opaque floor and walls still hide what is behind them. The kids' and the costume renderers skip swimmers. A swimmer in a costume swims in the swim sprites (the costume is kept for later). Guests changing are hidden like riders. The swim sets are looked for once per level (`Level.RefreshPools`); if there are none (the folder is missing or broken) one warning is logged and the swimmers are drawn by the normal guest renderer with their kid sprite (standing, not swimming), which is why `Level.GuestRenderer` skips swimmers only while the swimmer renderer exists. Picking
a swimmer works like any visitor's (`Level.PickVisitor`).

### Content (`[EXT:pools-content]`)

The extension ships `content/pools` (copied next to the program with the rest of `content/`, embedded in the browser build by `OpenTPW.Web.csproj`): `objects.json` and four object archives (a cabin per theme: `cabin.MD2`, a few 64 or 128 pixel TGA textures, `Cabin.sam`, `Cabin.RSE`), and `esprites/Swim/SPR_xx.ESP` + `.TPC` for the eight Theme Park World kid sets. About 0.9 MB in
all. The catalog reads `objects.json` as a content root of the id `opentpw.pools` (`PoolContent.CatalogRoot`, from `ModContentRoots.CatalogRoots` while the extension is on): the objects are listed in the theme catalogs, priced, bought, placed, run and saved like a mod's, and a park save names their files as `mod:opentpw.pools:/...`. No
data-only mod is involved and nothing has to be enabled besides the extension. **Everything is our own art, generated from code and committed** because CI has no Blender: `tools/pools/build-content.sh` (Blender 5.x, the .NET SDK and python3; about 3 minutes) rebuilds all of it from `tools/blender/opentpw_changing_room.py` (the cabins: one low-poly model with a front and a back door, themed
textures: planks and thatch, mossy stone, riveted metal with a cyan strip, pastel candy), `tools/pools/cabin.sam`, `tools/pools/cabin.rse`, the swimmer generator `tools/blender/opentpw_guest_sprites.py --part swim` and `tools/pools/pack_swim_sets.py` (which packs them with `opentpw-kit sprites`, `detailFlag` 0, so only the `.TPC` bank is written). Nothing from the original game
is used or committed; [CONTENT-KIT.md](CONTENT-KIT.md) describes the tools.

### Checking it on screen

`--debug-swimmers N` (smoke-only, `[EXT:debug-swimmers]`; it needs `--debug-pool` and the extension) builds the theme's changing room beside the painted pool through the level's own placement (it is charged, and the rule needs the pool at its back; `Level.BuildChangingRoomBeside`), opens the park door and starts N guests in the water at
random points. `--debug-pool-offset dx,dy` moves the place the pool shape is searched from the camera target by whole cells, to frame the room:

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --load-original-level jungle --enable-mod opentpw.pools \
  --debug-pool default --debug-swimmers 8 --debug-pool-offset 3,0 --silent --capture-world pool.png --capture-frames 400 --capture-camera -29.5,-50.6,45,-90
```

First on-screen check (October 17, 2026; all four parks, eight swimmers each, camera `x,y,45,-90` and `x,y,45,-150`; 3024 x 1686 captures, shown at 1600 wide): the swimmers swim in all directions with different strokes (arms out of the water, the foam ring, the lower body tinted), keep inside the coping, and the
cabin stands beside the pool with its themed textures. The images are `reports/pools-p2-<theme>-top.png` and `-angle.png` and a short clip `reports/pools-p2-swimming.gif` (`--capture-sequence 40,4`). The art was judged on these captures only, not against a capture of the original game (which has no pools).

The save roundtrip in the middle of a swim is also checked natively: `OPENTPW_SMOKE_POOLS=1 bash scripts/run.sh ... --enable-mod opentpw.pools --park-resume-smoke capture ...` then `restore` (`ParkResumeSmokeTest.Pools.cs`) paints a pool, builds a room, puts three guests in the water at the save, and the restore phase finds the room and the swimmers
in the water of the 24-cell pool and continues with the capture phase's canonical hashes.

### Tests

`PoolSwimmingTests` (the pure part): the heading table and the random stream, the region and the free water (rim, body, inner corner), steering in four shapes for thousands of turns (always in the water, a wander that reaches every cell, a turn at the rim, avoidance and the minimum distance, two swimmers on one point, determinism, boxed in).
`PoolsSwimSessionTests` (a real `GuestSimulation` with a real ride script): the access cell and the region, the whole session (queue, change, swim in the water only, climb out, change back, path), the happiness, several swimmers, the walkers' lists, the catchable list and the counts, demolition at each stage, a closed park, the pool erased or shrunk, the pool tool's refusal, a guest who wants to leave, staff, the balloon, two rooms
in one pool, the snapshot roundtrip in each state, an older snapshot, the validator, the hash and a guest that chooses the room. `PoolsContentTests`: the reserved ids, the four catalog entries (and none with the extension off), each cabin's model, textures and script (it releases after the changing time), the visitor bridge, the pool-side cells for every rotation, the placement rule, the eight sprite sets,
the content's size, the asset keys of a save, and the whole chain with the shipped room and script. `DeterminismTests.FixedRunWithAPoolAndAChangingRoomIsTheSameInEveryProcessAndGuestsSwim` is the pin (docs/DETERMINISM.md, "Pools").

## Activities (`[EXT:pools-activities]`)

An **activity** is something a swimmer does during its session besides swimming: a ladder to a diving board, a shower, a lounger, a water slide, a water attraction. Extras (P3), slides (P4) and water attractions (P5) are all `IPoolActivity`s (`source/OpenTPW/World/Pools/PoolActivities.cs`); the host is in `GuestSimulation.Swimming.cs`. Guests never reach them from the park paths (they are not attractions, `IGuestDecisions` has no entry for them): they are reachable only from the pool, as in RCT3, and they are free.

**State 25, `PoolActivity`.** The guest still names its changing room as its attraction and is part of the session (`InSwimSession`), visible, drawn with the swim sprites, in no walker cell list, counted with the walkers, and not catchable. Its generic fields (`PoolActivityId`, `PoolActivityTarget`, `PoolActivityPhase`, `PoolActivityTurns`, `PoolActivityX/Y` in 16.16 cells) belong to the activity, which keeps no per-guest state of its own. They are snapshotted (optional fields, written only in state 25, so every older JSON is byte-equal), validated (an id, non-negative phase and turns, a point inside the map, none of it outside state 25) and hashed only while the guest is in state 25.

**The registry** is `GuestSimulation.PoolActivities` (ordered by id; `RegisterPoolActivity`); `Level` fills it from `PoolActivityRegistry.Create()` (`Level.RegisterPoolActivities`) once, when the level's guests are made, while the extension is on, so no path runs with an empty registry. A simulation with an empty registry behaves exactly as before.

**The cadence.** Each turn of the water phase (`Swimming`, at least 24 turns left of the swim), the host asks the registered activities for an offer (`Offer` is pure), stopping at the first that has one. **With no offer nothing is drawn.** With an offer, one draw of the swimmer's own stream decides (one in 90 turns on average, `PoolActivitySettings.OfferIntervalTurns`); only when it is taken does the host collect every offer, in registry order, and a second draw pick one; then `Start` is called with the stream. An activity's `Id` and its **target** are saved with the guest: the target (`PoolActivityOffer.TargetId`, `Guest.PoolActivityTarget`) is always the **thing id of the object that provides the activity** (`OriginalObject.ThingId`), or 0 when there is none (a river); attraction ids are a separate counter and are never used. An activity whose `StartOfferPercent` is above 0 (the shower) is offered at the start of the session only: as the swim begins the host draws once per such activity that has an offer, and the guest does it with that chance before entering the water. A declined `Start` leaves the guest swimming.

**A turn of the activity** (`Update`): `Continue` goes on; `BackInWater` puts the guest back to `Swimming` (the host sets it at the pool-side cell if the activity left it outside the water); `EndSession` sends it to climb out like a finished swim. The swim's remaining turns do not run down during an activity, so an activity must bound itself. `PlacePoolGuest` and `MovePoolGuest` set or step the guest's position for an activity.

**Ambient effects.** A registered activity that also implements `IPoolAmbient` acts on every free swimmer (state 22 that took no offer this turn) once per park turn, before its stroke: a wave's push, a jet's spray. It must not draw from any random stream and must not move the guest. **Drawn height.** `IPoolActivity.FeetHeight( simulation, guest, heights )` returns the engine height of the guest's feet for the frame, or null for `heights.Default` (the water surface on a pool cell, else the terrain); `PoolFeetHeights` also holds `Water` (the pool region's surface) and `Deck` (the terrain at the guest's point). A slide returns its track height, a ladder interpolates `Water` to `Deck`, a board or swing uses `Deck` or `Default` plus its lift. Presentation only; `GuestSimulation.PoolFeetHeight` is what the swimmer renderer calls.

**Aborts.** `Abort` is called, and the session ends at once on the room's exit (`EndSwimAtOnce`), when the room is demolished, the pool beside it is gone or `AbortPoolActivitiesOf( thingId )` is called for the object that provides the activity (it was erased; 0 aborts nothing). When the park closes, the activity is aborted and the guest is put back in the water (at the middle of its own cell when that is water of the region, else at the pool-side cell), then climbs out and changes back like a swimmer. A guest whose activity id this build does not register (a save made with an activity that is not registered now; the validator accepts it) ends its session on the room's exit at its next turn. An activity keeps `PoolActivityTurns` at most `PoolActivitySettings.MaximumTurns` (2400); the validator refuses a save above it, and the swim's own turns stay frozen in state 25 within the swim's range. The pool tool refuses to erase a cell under a guest in an activity.

**Sprites.** The swimwear slots are `PoolSwimSlots` (0 stand, 1 walk, 2 run, 3 ladder, 4 dive, 5 lying, 6 shower, 7 sliding, 8 to 10 the strokes, 11 splash-down, 12 sitting, 13 ring, 14 rope swing); the activity chooses the slot (`IPoolActivity.SpriteSlot`, default: treading water). A slot a set has no art for is drawn as tread water, then breaststroke, then crawl (`GuestSpriteAtlas.UsableSlot`, in the swimmers' renderer only).

**Tests.** `PoolsActivityHostTests` proves the host with a test-only fake activity: offer, start, continue, back in the water, end of session, declined start, the shower-like start offer, no draws and no hash change with an empty registry or an activity that offers nothing, the aborts (demolish, pool erased, park closed, target erased, unknown id), a save in the middle of an activity, the validator and the hash.

## Extras (phase P3, `[EXT:pools-extras]`)

Six objects of the pools extension, one set per theme (Info.Ids 65000 to 65035: a theme takes a block of ten, 65000 jungle, 65010 hallow, 65020 space, 65030 fantasy, in the order ladder, diving board, shower, lounger, lamp, underwater light). They are features in the build menu's Features tab with the .sam key `Pools.Extra n` (`PoolExtras`), **no entrance and no exit** (nobody walks in), and `Info.IsChoosable 0` so the park's guests never choose them: swimmers meet them in the pool, as activities (above). They cost 50 (ladder), 250 (board), 80 (shower), 100 (lounger), 30 (lamp) and 120 (light), OpenTPW's own values.

**Deck.** There is no new deck cell type. The "deck" of a pool is ordinary empty owned ground beside the water, which every extra takes with its own paving slab (the slab is part of the object's model, so the ground under it needs no cell type and reads as deck). That keeps the cell map, the save and every pin as they are; a real walkable deck cell (guests walking around a pool on tiles) belongs with the later "pool deck" item below.

**Orientation.** The back of the object (the top row of `Info.Shape`, +Y of the model, as the changing room's) is the water side; the rotation turns it. A land cell and its water cell lie in a line, which the placement rules and the activities share (`PoolExtras.WaterDirection`).

| Object | Footprint | Placement rule (`PoolExtras.PlacementRule`, refused as "needs a straight pool edge") | Activity |
| --- | --- | --- | --- |
| Ladder | 1 x 1 | the water cell behind it and the two beside that one (along the edge) hold water: a straight edge, not a corner | `extra.ladder` |
| Diving board | 1 x 1, the board reaches 1.4 cells over the water | the same, for two water cells out (a pool at least 2 cells deep from the edge) | `extra.board` |
| Shower | 1 x 1 | a changing room beside it (4-neighbour of the room's footprint) | `extra.shower` |
| Sun lounger | 2 x 1, two seats | water beside any cell of the footprint | `extra.lounger` |
| Pool lamp | 1 x 1 | water beside it | none (decoration) |
| Underwater light | 1 x 1, a glow disc over the first water cell | a straight edge, as the ladder | none (decoration) |

The water beside a ladder, board, lounger, lamp or light cannot be erased (`PoolBuildResult.ExtraNeedsWater`: "A pool object needs this pool water", and the second cell out of a board), so a save that holds an extra always holds its pool; the shower holds none, its changing room does. Removing or demolishing an extra aborts its activities at once (`AbortPoolActivitiesOf`).

**The activities.** All four share the host's rules (one swimmer per ladder, board or shower, one per seat, read from the host's exact occupancy `GuestSimulation.IsPoolActivityTaken`, rebuilt only when a guest starts or ends an activity or a save is restored; the placed extras are read through `PoolExtraSites`, a list rebuilt once per park turn and whenever an object is placed or removed, so an erased shower is never offered, not even in the same turn; offered only when the swimmer has a straight line of free water to the foot of the extra; integer 16.16 motion; draws only from the swimmer's stream). The swimmer stays in state 25 and the activity keeps its step in the low bits of `PoolActivityPhase` (the direction to the water in the rest).

| Activity | Steps (sprite slot) | Numbers |
| --- | --- | --- |
| Ladder | swim to the edge (crawl 8), climb (3; the drawn feet rise from the water surface to the deck), stand on the deck (0), walk to the rim (1), jump in (4; the feet fall back to the water), splash (11), back in the water | stand 5 to 10 turns (1.25 to 2.5 s); happiness +2 |
| Diving board | swim to the foot (8), climb the steps (3), walk out (1), pause at the end (0), dive (4) into the middle of the second water cell, splash (11), back in the water; the drawn feet rise from the water to the board with the steps, stay at the deck plus 1.32 engine units on the board and fall to the water with the dive (`FeetHeight`, by the way covered, not by a turn count) | pause 3 turns, splash 4; happiness +6 |
| Shower | offered at the **start of the session** (60 percent) when a free shower lies within 4 cells of the room's pool-side cell: walk from the pool-side cell (1), shower (6), walk back, back in the water | 12 to 20 turns (3 to 5 s); happiness +2 |
| Sun lounger | swim to the seat's water side (8), climb (3), lie (5) 60 to 120 turns (15 to 30 s), then 60 percent walk to the rim, jump in (4), splash (11) and swim on, 40 percent end the session (the guest is put back at the room's steps and climbs out; the lie ends by teleport, cosmetic) | happiness +5 |

Speeds (16.16 cells a turn, `PoolExtraSettings`): swimming to the edge 0.12, walking 0.10, climbing 0.07, jumping 0.20, diving 0.22. The host offers one in 90 turns on average while any activity has an offer (`PoolActivitySettings.OfferIntervalTurns`; at least 24 turns of the swim left), so a swimmer of 40 to 70 seconds takes about two offers.

**Not done.** The brief's "swimmers climb out by the ladder" and "use it to reach the board" are done as the ladder's own climb-out and jump back in and the board's own steps: the swim session still ends at the changing room's pool-side cell, and the board has its own tower, so no swimmer ends its session on a ladder (and a lounger's end of session is a teleport to the steps). Without a ladder, a swimmer simply climbs out at the steps; the optional cosmetic "haul out / jump in with a splash" at a plain edge was not made. A particle splash on landing was not added (the splash is the swim sprite slot 11, still the treading-water art until the hi-res sets land).

**Content.** `tools/blender/opentpw_pool_extras.py` builds the six objects for each theme (themed textures from the changing room's generators, 32 x 32 each so the browser build stays small) and `tools/pools/build-content.sh` turns them into `content/pools/objects/<theme>/<extra>` with `tools/pools/extra-*.sam` and `extra.rse` (an inert script). `objects.json` is append-only: the 24 entries follow the four rooms.

**Checking it on screen.** `--debug-extras` (with `--debug-pool` and `--debug-swimmers`) builds one of each extra through the level's own placement beside the painted pool and sends the first swimmers straight into a ladder, a board, a lounger and a shower (`Level.BuildPoolExtrasBeside`, `GuestSimulation.DebugStartActivity`). `--debug-sim-minutes 0.14` runs the simulation until the activities are under way before the capture. The images are `reports/pools-p3-<theme>-extras.png`; the swimmers on the deck are drawn with the treading-water slot until the swimwear sprites arrive, so the activities read as heads behind the ladder.

**Tests.** `PoolsLevelTests.RealExtrasThroughTheLevel...` places real extras through the level (the placement hooks, `PoolExtras.Sites`, the site cache, `RegisterPoolActivities`, the erase abort through `ObjectRemoving`, `PoolEraseBlock` giving `ExtraNeedsWater`) and saves in the middle of a shower with an identical hash after the restore. `PoolsExtrasTests`: the 24 objects (ids, shapes, no guest choice, models and textures load, stay inside their cells), the placement rules, the erase guard, each activity's steps, positions, slots, durations and happiness, two seats and no shared seat, no draw without a placed extra, a save in the middle of the ladder, the board and the lounger. `DeterminismTests.FixedRunWithPoolExtrasIsTheSameInEveryProcessAndGuestsUseThem` is the pin.

## Water slides (phase P4, `[EXT:pools-slides]`)

Water slides are modelled on the pool slides of Roller Coaster Tycoon 3 Soaked!: built like coasters, **reachable only from the pool, free to ride, and ending in the pool**. They are an `IPoolActivity` (`slide.ride`) of the swim session, so no guest ever reaches one from the paths and `IGuestDecisions` has no entry for them.
Code: `source/OpenTPW/World/Pools/Slides/`, `Level.Slides.cs`, `GuestSimulation.Slides.cs`; the track rule is also described in [TRACKS.md](TRACKS.md#slides-open-tracks-ending-in-a-pool).

**The least invasive build.** A slide is **not** a kart or water track and not a coaster: those need a station object, a closed circuit and a controller (`BumpController`, `TrackCircuitWalker`), none of which a slide has. The slides share the *vocabulary* of the track system (a track of one-cell pieces: straight, bends, an end piece; the cell-by-cell tool; the piece price; the pylons) and nothing of its code, so every other track type keeps its closed-circuit rule untouched
(`PoolSlidesTests.TheRulesAreOffWithTheExtensionOffAndEveryOtherTrackKeepsItsClosedCircuitRule`; the files of the track code are untouched) and a slide writes **no cell** of the map: its land cells are reserved through the existing hooks (`Level.IsPathBlocked` for the path, pool, staff and track tools, `ParkObjects.IsReserved` for objects) and its water cells stay pool water.

### The tower (`PoolSlideTowers`)

A **slide tower** is an object of the Features tab (Info.Ids **65100 to 65103**, the open-flume tower of jungle, hallow, space and fantasy, and **65104 to 65107**, the closed-tube tower), a 2 x 2 platform with a staircase (`tools/blender/opentpw_slide_tower.py`, then `opentpw-kit object`). Its `.sam` carries OpenTPW's own keys `Pools.SlideTower 1`, `Pools.SlideProfile` (0 flume, 1 tube) and `Pools.SlideCapacity` (4);
it has no entrance, `Info.IsChoosable 0` and costs **600**. Like the changing room it is placed with **water beside its front**: the stairs face the pool, on the shape's top row (the local -V side at rotation 0), and the cells beyond that face (two for the 2 x 2 tower) are its **pool-side cells** (`PoolSlideTowers.PlacementRule`, `OriginalPlacementResult.NeedsPool`; the HUD says "A slide tower must stand beside a pool"). The first pool-side cell that holds water now is the **foot of the stairs**. The platform's top is exactly
five engine units above the highest ground of the footprint (`PoolSlideTowers.TopHeight`; 25 model units in the model), where the track starts and the waiting riders stand. Capacity: how many riders wait on the platform (a tower's `Pools.SlideCapacity`, 4).

### The track (`PoolSlide`, `PoolSlideRules`, `PoolSlideWorld`)

- **Pieces**, one per cell, in riding order: `Straight`, `Left`, `Right` (quarter turns of radius half a cell) and the last, **`Splash`**, a chute from the cell's edge to its middle, which lies over **pool water**. Lengths in 1/1024 cell: 1024, 804, 512. Up to 24 pieces. A helix is not built (a 2 x 2 piece with its own geometry; see "Not done yet").
- **One constant slope.** The track runs from the tower's top down to **0.2 engine units above the water** at the splash, in a straight line over its length. A short slide is steep and a long one gentle, so there are no height controls: the player lays the plan. The slope (drop over run, both in engine units) must be **0.2 to 0.8**, about 11 to 39 degrees: with the 5 unit tower that is **3 to 12 cells** of track; shorter is refused as too steep, longer as too gentle.
- **The open-track rule** (the rule that replaces the closed circuit, gated to slides): the first piece lies beside the tower on any side but the stairs' (`PoolSlideTower.StartOptions`) and the track leaves the footprint outwards; every piece's cell is empty ground or pool water, owned, not the tower's and not used twice; the last piece is the splash over a pool cell **of the pool the tower faces** (the splash cell is in the water connected to the foot of the stairs); the track clears the ground under every piece by 0.15 (`PoolSlideRules.Clearance`, measured at the piece's exit against the highest ground corner, or the water); the recorded heights are the tower's top and the pool's water (the end may stand up to 1.5 above it, as the pool's level can drop when cells are added). The reasons are `PoolSlideResult`s, each with its test.
- **The builder** (`PoolSlideWorld.AddCell`) lays the track one cell at a time: the first cell is a start cell, each next cell is a neighbour of the end, not behind it, and the previous piece becomes a straight or a turn towards it; a pool cell ends the slide with the splash piece. Pieces cost **90** each (`ParkEconomy.SlidePieceCost`, `CellPurchase.SlidePiece`, OpenTPW's own value; riding is free) and are refunded in full when taken back or when the tower is demolished. The world is also the store the activity, the renderer and the saves read.
- **The tool.** The slide tool is a third mode of the pool tool (`PoolTool.BeginSlide`), so the cursor, the blueprint markers, Escape and right click are the pool tool's: placing a tower starts it at once (`ParkHud.OnObjectPlaced`), and a new row in the Miscellaneous list, "Water slide track" (id -5, price 90), continues the first unfinished slide. Press and drag over the cells beside the track's end; a pool cell ends the slide and the tool; **Backspace takes back one piece**. A refusal says why in the message line (too steep: make it longer; too gentle: shorten it; would touch the ground; continue beside the end; must end over the tower's own pool; start beside the tower), in the nine languages the extension's texts have. `PoolSlideRoutes.Find` (shortest valid route by iterative deepening) is a test and capture aid, not a player feature.

### The ride (`PoolSlideRide`, `PoolSlideSettings`; every number OpenTPW's own)

One rider at a time per position; there are no cars. Every rider runs the same integer physics along the track: each park turn **speed += slope-per-mille x 100 / 1000 - speed / 8**, at least 24, from a launch speed of 64 (1/1024 cell per turn), so the speed settles at about **eight times the pull**: 0.6 cells a turn on the steepest slide (0.8), 0.15 on the gentlest (0.2). Because the physics depends only on the slide, a rider's place is a **pure function of the turns since its launch** (`PoolSlide.PositionAtTurn`, a table built once), and a rider saves nothing but that counter. The standard 7-piece, 6-cell slide takes 25 park turns (about 6 seconds). The next rider leaves when the one before is **1.5 cells** down the track.

### The activity (`PoolSlideActivity`, `slide.ride`)

- **Offer:** the swimmer's water holds both the foot of a usable slide's stairs and its splash cell, and the tower has room (guests on their way, on the stairs and waiting count: the capacity). A slide is *usable* while it is complete and valid for its tower and pool now (`PoolSlideWorld.IsUsable`, kept until the slides or the pool cells change). The slide with the fewest occupants wins, ties by tower order. `Offer` is pure and draws nothing.
- **Phases** (`PoolSlidePhases`, in `PoolActivityPhase`; `PoolActivityTurns` counts within a phase): 0 swim to the foot of the stairs with the swimmers' own steering, crowd ignored (at most 160 turns, else back in the water); 1 climb the stairs, 12 turns on a straight line from the foot to the guest's place on the platform, the feet rising to the top; 2 wait on the platform in line, the guest who has waited longest first (ties by id), and launch when it is first and the start is clear; 3 ride (the turns since the launch); 4 splash for 4 turns at the splash cell, a little off its middle by the guest's id, then `BackInWater`. A guest leaves the tower after 240 turns of waiting.
- **Randomness:** the slide world has no random state, `Offer`, the climb, the wait, the ride and the splash draw nothing, and the activity's one draw is the swimmers' own steering step on the way to the stairs (`PoolSwimming.Step`, one draw of the swimmer's own stream per turn, as in the water; the contract allows it). The ride itself is deterministic from the slope.
- **Sprites and heights:** slots 3 (ladder climb), 0 (stand), 7 (sliding) and 11 (splash-down) of the swimwear table (`PoolSwimSlots`); until the hi-res art lands, `GuestSpriteAtlas` falls back to the nearest slot the set has (tread water, breaststroke, crawl). The riders' feet follow `IPoolActivity.FeetHeight`: the water at the foot of the stairs rising to the platform, the track's height with 0.3 of lift on the ride (the hotspot of the sliding slot is the torso centre, which lies about a body's half thickness above the bed).
- **Targets:** the offer's target, and so `PoolActivityTarget` of every guest on the slide, is the **thing id of the tower object** (`PoolSlideTower.ThingId`, `OriginalObject.ThingId`), as the host requires; the slide world keeps its own key (the tower's anchor) and finds a slide by thing id (`PoolSlideWorld.FindByThing`).
- **Park close:** a guest in the activity when the park closes is let go by the host, whatever the phase: `Abort` runs (the activity holds nothing outside the guest's own fields), the guest is put in the water (at the changing room's steps when it was on the stairs, the platform or the track) and climbs out. A ride is aborted rather than finished: nobody is left in the air, and a closed park's riders are not counted again (`PoolSlideActivityTests.WhenTheParkClosesARiderOnTheTrackIsPutInTheWaterAtTheStepsAndClimbsOut`).
- **Cost of an offer:** `Offer` runs per swimmer per turn, so it allocates nothing: the slides are an array rebuilt only when they change, the park as the slide rules see it is one instance for the level's life (`Level.SlideSite`; its water levels are rebuilt on the first question after a pool cell was written), the usability of a slide is kept until the slides or the pool cells change (a tower placed or removed touches it), and the tower's occupants are counted by one loop over the guests, only for a slide usable in the swimmer's water. The line in the tower is a rank counted by a loop, not a sorted list: a guest's place is the number of guests ahead of it, the longest waiting first (the furthest up the stairs for the climbers, who come after the waiters), ties by guest id, each read with the counters of the start of the turn (a guest already updated this turn is read one turn back, whatever the order the guests are updated in), so two guests never share a spot.
- **Edge cases:** a tower demolished mid-ride takes its slide with it (the pieces refunded) and ends every activity on it at once (`GuestSimulation.AbortPoolActivitiesOf`), and the host ends their sessions at once on the changing room's exit; a slide whose pool or foot cell was erased while guests are only on their way or waiting sends them back into the water at the foot of the stairs (the session ends where there is no water). **The pool tool never erases the splash cell of a built slide or a pool-side cell of a slide's tower** (`PoolBuildResult.SlideWater`, "A slide needs this pool water"; as a changing room's access cells are protected, a tower without a slide keeps no water), and also refuses the cell a rider or climber is in (`SlideRiders`). Any other pool edit is legal and may make a slide unusable (the pool split in two, the level of its water moved out of the 0.2 below to 1.5 above window by painting downhill): a slide whose pool no longer holds both the foot of its stairs and its splash cell is simply not offered, and it is usable again with the water. A save never fails to load for that (see "Saves and determinism"), and a guest on the stairs or platform when it happens is sent back into the water at the foot of the stairs.
- **Queue and paths:** the slide's land cells are kept from the path, queue, object, staff and track tools through the level's reservation hooks (`Level.IsPathBlocked`, `Level.IsQueueBlocked`, `ParkObjects.IsReserved`; the splash and every other water cell are not reserved).

### Art

The tower is a Blender-made model (the kit's `object`, `tools/pools/build-content.sh` step 2b) in the changing room's four themed textures, a finial telling the profiles apart (a pennant on the flume's tower, a wide cap on the tube's). **The track and the pylons are generated in code**, not rigid kit models, because the track must follow any slope and turn: `PoolSlideMeshBuilder` sweeps the profile along `PoolSlide.PointAt`, an open half pipe with lips for the flume and a ring with a slit along its top for the tube (so the riders stay visible in it), with a square pylon under every piece (on the ground, or on the pool floor under the splash chute). The textures are generated per style (`PoolSlideTextures`, from `PoolStyles`: the flume takes the style's bright shallow-water colour, the tube the coping's colours), drawn with the pool deck's shader (`pool-deck.shader`, so the browser build has nothing new) by `PoolSlideRenderer`, which the level makes with the pool renderer so the riders and swimmers draw after it.

### Saves and determinism

`ParkResumeSnapshot.PoolSlides` (`PoolSlidesSaveData`: tower, profile, start, heading, pieces and the two heights per slide, in tower order) is written only when the park has a slide, so every other park saves the same bytes. It is shape-checked on load (`PoolSlideSaves.ShapeProblem`: bounds, order, known pieces, the extension recorded) and restored over the towers by **structure only** (`PoolSlideRules.ValidateStructure`: the tower is there with the slide's profile, the start beside it, the cells owned, empty or water and used once, the recorded top height the tower's; two slides never share a cell). **Nothing that depends on the pool is checked at load**, because the pool tool may legally change it after the slide was built: whether a slide is usable is `IsUsable`, and a state the game can be in always loads. The guests on slides are then checked against the slides (`PoolSlideWorld.ValidateGuests`: a phase 0 to 4, a slide whose tower has the guest's target thing id, and a finished slide for riding and splashing guests), so a forged save cannot send a guest down a track that is not there; `PoolSlideActivity` also treats an unfinished slide in those phases as missing (the guest leaves the session). The guests' activity fields are the host's (ride position = the turn counter). The canonical hash covers the slides only when there are any (`PoolSlideWorld.AddCanonicalState`, from `GuestSimulation.AddCanonicalState`). New pin: `PoolSlideActivityTests.PoolsSlidesFixedRunHash` (docs/DETERMINISM.md, "Pools").

### Tests

`PoolSlidesTests`: geometry (cells, lengths, continuity, quarter circles, heights), every rule's reason, the builder, refunds and money, the land reservation, usability, the ride physics (monotone, repeatable, speed from slope), saves, the hash, the route search and the tool's slide mode. `PoolSlideActivityTests` (the swim session's real rig and ride script): the whole ride phase by phase with the track positions and feet heights, offers, the capacity, the order and the gap, demolition in every phase, an unusable slide, a save in the middle of a ride that continues with the same hash, no draws and no hash change without slides, and the pin. `PoolSlideLevelTests` (a real `Level`): placement, registration, the reservations, demolition, the erase refusals, the resume capture. `PoolSlideContentTests`: the eight towers, their models, scripts and placement. `PoolSlideMeshTests`: the shell, the pylons and the textures.

### Checking it on screen

`--debug-slide flume|tube` (smoke-only, `[EXT:debug-slide]`; it needs `--debug-pool` and the extension) builds the theme's tower of that profile beside the pool through the level's own placement (charged; the rule needs the pool at its front), finds the shortest valid route with `PoolSlideRoutes` and lays it piece by piece through the slide world; the log prints the engine points of the track's middle and splash for `--capture-camera`. `--debug-pool-at N` builds the pool at frame N instead of 59 frames before the capture, so swimmers have time to climb and ride:

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --load-original-level jungle --enable-mod opentpw.pools \
  --debug-pool 0,0,7,6 --debug-swimmers 14 --debug-slide flume --debug-pool-at 100 --silent \
  --capture-world slide.png --capture-frames 1500 --capture-sequence 60,6 --capture-camera 51,-12,70,-90
```

First on-screen check (October 17, 2026; Jungle with a flume tower, Fantasy with a tube tower, a 7 x 6 pool, 14 swimmers, 3024 x 1686 captures shown at 1600 wide, the camera at 51,-12,70,-90 with `--debug-pool-offset 2,-3` for the track and without it for the splash, and 31,-32,70,-90 in Fantasy): the swimmers swim to the foot of the stairs, climb, wait on the platform, ride down the flume (riders drawn lying in it, one behind the other at the gap), and splash down in the pool; the tower stands with its stairs facing the pool and the track leaves it from the side. The images are `reports/pools-p4-jungle-flume.png` (tower, track and a rider), `reports/pools-p4-jungle-splash.png` (riders landing), `reports/pools-p4-fantasy-tube.png` (the tube slide with its pastel tower) and a short clip `reports/pools-p4-slide.gif` (`--capture-sequence 60,6`). The art was judged on these captures only, not against a capture of the original game (which has no pools). The riders are drawn with the crawl slot until the swimwear art for slots 7 and 11 lands.

The save roundtrip in the middle of an activity is also checked natively: `OPENTPW_SMOKE_POOLS=slide` (both phases, with `--enable-mod opentpw.pools`) adds a slide tower and its slide to the resume fixture and saves while guests are on the stairs; the restore phase finds the slide complete and usable over its tower with the guests on it and continues with the capture phase's canonical hashes (`ParkResumeSmokeTest.Pools.cs`).

## Saves and determinism

See [the save rule](#the-extension-and-the-save-rule). The pins (docs/DETERMINISM.md, "Pools"): every existing pin is unchanged with the extension on or off
(`PoolsTests.TogglingThePoolsExtensionKeepsEveryExistingPinWithoutPoolCells`), the fixed synthetic run on a cross park with 17 pool cells painted through the builder has its own pin,
`DeterminismTests.PoolsFixedRunHash` (a park with pools and no changing room behaves exactly as in P1: the pin did not move), and the same run with a pool and a changing room in which guests swim has `DeterminismTests.PoolsChangingRoomFixedRunHash`.
The extension's version stays 1.0.0: phase P1 was not released, a P1 park holds no room and behaves the same under P2, and a P2 park with a room does not load in a P1 build (the object is unknown there).

## Not done yet

- **Swimwear on the deck:** guests walk the path in their normal sprites; only the swimmers have swim sprites (the changing room hides the change). The sprites of the standing, walking and sitting swimwear guests, and the swimmers for the Theme Park Inc sets and the costume sets (a costumed guest swims in its kid set), are not made.
- **A swim-appeal term** (warmer months more, rain less) and any new decision point for `IGuestDecisions`: not needed for P2, nothing was added.
- **Climbing out at ladders:** swimmers still enter and leave the session only at the room's pool-side cell (the extras are [Extras](#extras-phase-p3-extpools-extras)); the wave machine and the rest: P5.
- **Water quality, lifeguards and a handyman for the pool:** not done.
- **Pool objects:** the lifeguard chair, wave machine and spa are not done (the rest are the [extras](#extras-phase-p3-extpools-extras)).
- **Water slides:** see [Water slides](#water-slides-phase-p4-extpools-slides) for what is built. Not done: a helix and other piece shapes (the pieces are straights, quarter turns and the splash), a slide tower on sloping ground (the platform is five units above the highest corner, so the model's feet may float on a slope), slide tours or a coaster-style camera, a separate Thrill rating for slides, exporting parks with slides, the hi-res riders' art (the sliding and splash-down slots fall back to the strokes until it lands). Attractions that end in a pool: P5.
- Depth levels (each cell has one depth), diagonal or curved edges (the tiling is square), digging the terrain, a pool-deck cell type, pool water on the Theme Park Inc layout, exporting parks with pools as packages.
- Name and description rows for the Extensions and mods screen are not translated: the screen shows the extension's English name.
- Pool cells are not drawn on the park map (the minimap), where they show as open ground.
- The changing room's `UsageInfo.InitPricePerUse` is not charged in a park (the economy charges features nothing); no swimming fee exists yet.
- The cabin, the swimmers and the pool were looked at in captures only; whether tp.exe accepts the authored MD2, RSE and sprite files is unknown (the original was not run), see [CONTENT-KIT.md](CONTENT-KIT.md#what-is-unverified).
- A swimmer's sprite is drawn on the water of its cell's group; the sprite's feet part is below the surface and shows through the (translucent) water, which is what the art's tint is for. A swimmer next to the near coping can be hidden behind it, as a person behind a wall would be.
- The path tool does not refuse fence posts (see the placement rules); neither does the area clear touch pool cells.
- `content/shaders/web/pool-*.json` are checked by `WebShaderTests` only with `OPENTPW_NATIVE_SHADER_TESTS=1` and native SPIR-V libraries that match the host; CI does not run it for any web shader.
