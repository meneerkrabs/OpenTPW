# Pools (`opentpw.pools`)

An official extension (docs/MODS.md), **off by default**, in which the player paints **pool water cells** of any size and shape on the park grid. It is modelled on the
pool tiles of Roller Coaster Tycoon 3 Soaked! (ideas only; no asset of that game or of Theme Park World is used or shipped). The original Theme Park World has no pools: everything
here is OpenTPW's own and carries `[EXT:pools-*]` labels in the code and in this page.

This is **phase P1**: the cell type, the paint tool, pricing, saves and rendering. There are **no guests in the water yet** (see [Not done yet](#not-done-yet)).

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
| `[EXT:pools-render]` | the mesh, the textures and the water shader |

## The extension and the save rule

`opentpw.pools` is registered in `OfficialExtensions` with `DefaultEnabled: false` and `AffectsSimulation: true`; the gate is `FeatureGates.Pools`, read the way
`FeatureGates.CrossEditionObjects` is. Turn it on in `mods.json` (`"opentpw.pools": true`), on the Extensions and mods screen, or for one run with `--enable-mod opentpw.pools`.

**A save records the extension only when the park actually has pool cells.** `AffectsSimulation: true` alone would tag every save of every park once the extension is on
(`ModSet.SimulationMods`), so the extension is one of `ParkResumeMods.UsedOnlyExtensionIds`: `ParkResumeMods.Capture(active, inUse)` leaves it out unless `inUse` names it, and
`Level.CaptureResumeSnapshot` passes `ParkResumeMods.InUse(cells)`, which names it when the cell map holds a `PoolWater` cell. So:

- a park **without** pool cells saves **byte-identically** with the extension on or off (no `Mods` field; `PoolsTests.SavesWithoutPoolCellsAreByteIdenticalWithTheExtensionOnAndOff`);
  a park whose last pool cell was erased saves like one that never had any;
- a park **with** pool cells records `opentpw.pools 1.0.0` (the digest of no bytes, like every extension) and loads only with the extension on: with it off the load refuses with
  `needs opentpw.pools 1.0.0`, and the load and Continue lists keep the save and add "(requires mod opentpw.pools 1.0.0)";
- a save without the record loads with the extension on or off (`ParkResumeMods.Unmet` does not ask for an unrecorded used-only extension);
- two guards stop a save from hiding its water: the layout validator refuses a pool cell while the extension is off (`cell (x, y) is a pool cell, which needs the opentpw.pools extension`),
  and the shape check refuses pool cells in a save that does not record the extension;
- exporting a park as a `.tpwpark` package is refused only while the park has pool cells (`ParkSharing.ModsRefusal(level)`), because packages record no mods yet.

A used-only record was feasible because `ParkResumeMods.Capture` sees the live cell map; nothing else had to give way.

## The cell type

`ParkCellTypes.PoolWater = 0x40` (64), also `ParkCellType.PoolWater` and `CellToolMode.Pool` (a tool's mode is the cell type it writes). No original type uses the value:
the original's cell types are 0, 1, 2, 3, 4, 5, 7, 9, 10, 11 to 13, 16, 18, 0x15, 0x19 and 30 (`ParkCellTypes`, `TrackCellTypes`, docs/MAP.md; the placement writes only the shape types 0 to 23, the map
init 0, 1, 2, 7 and 30, and the saves hold the type in one byte). `PoolsTests.ThePoolValueCollidesWithNoOriginalCellType` pins that.

- **Rules** (`ParkCellMap.CanChangeCellType`): pool water goes on **empty cells only** (or stays where it is); clearing (type 0) always works, so erasing returns the cell to empty; every
  other type over a pool cell is refused (paths, queues, objects, tracks, entrances). Objects are refused through the existing reservation (`Level`'s `IsReserved`: any type other than empty,
  path and the object types is taken), tracks through their own cell check.
- **Walkability stays as it is:** `GuestPathGrid.IsWalkable` is `TypeAt == Path`, so a pool cell is not walkable whatever its links, and guests route around it
  (`PoolsTests.GuestsRouteAroundPoolCellsAndNeverStandOnThem`). No guest code changed.
- **The canonical hash** (`ParkCellMap.AddCanonicalState`) already covers every non-empty cell, so a pool cell joins it with no schema change, and a park without any keeps every pin.
- **Saves:** the layout's `Cells` records hold the raw type byte, so a pool cell is a record with type 64 and no links, queue link, owner or placement count (the validator insists).

## The tool

`PoolBuilder` (headless, CPU-only, like `ParkPathBuilder`) validates, paints, charges and erases; `PoolTool` is the stroke tool on top of it (`CellToolMode.Pool`); the HUD and the tests drive
both. `Level.PoolBuilder` and `Level.PoolTool` exist for original levels that are not read-only visits.

**Painting.** Press on a cell and drag: every cell between the previous and the current pointer cell is written (a 4-connected walk, so a fast pointer leaves no hole), the tool stays on for another
stroke, right click or Escape leaves it, Backspace takes back the last stroke (`PoolTool.Undo`: refunds a paint stroke, charges an erase stroke again). The path tool's snapped straight lines are not used:
pools are areas. `PoolBuilder.Rectangle` fills a rectangle for headless callers. A refused cell is skipped and the stroke goes on (the first refusal is reported); only a cell that cannot be paid for ends the stroke.

**Erasing.** The erase mode (a second buy-list row) takes pool cells back to empty cells the same way. The path tool's own removal is the forced `ClearCell` (Backspace, the area clear) and
refunds nothing; erasing pool water **refunds the full cell price** instead, a deliberate difference (`[EXT:pools-cost]`): a pool painted by mistake costs nothing once erased. The area clear and the
developer remove tool leave pool cells alone.

**Placement rules** (`PoolBuilder.Check`, in order; each has its own `PoolBuildResult` reason and a refusal message):

1. `ExtensionOff`: the extension is off;
2. `OutsideTerrain`: outside the map or the terrain grid;
3. `NotOwned`: land the park does not own (cell flag 0x40);
4. `Existing`: already pool water (free, no change);
5. `Occupied`: a path (a fixed one too), queue, entrance, exit, object cell, track cell or the fence is there, or the host reserves the cell (a placed object, the Totem);
6. `Terrain`: the terrain rule refuses the cell (water, a hole, rock, the entrance area, the fixed walkway: the object build grid's `CheckTerrain`);
7. `TooSteep`: the ground is not level enough (below);
8. `NotEnoughMoney`: balance minus the stroke so far minus the price would be negative.

**The ground rule** (`[EXT:pools-terrain-rule]`). The heightfield cannot be dug, so a pool is a basin standing on the land and its cell must be nearly level. The rule is the **corner height spread**: the highest
minus the lowest of the cell's four corner heights, in engine units (`PoolBuilder.CornerSpread`; a cell is 2 wide and the heightfield rises in steps of 0.2, one model unit). It must not exceed
**0.25** (`PoolBuilder.MaximumCornerSpread`: one step of 0.2, plus a little), which is a slope of about 7 degrees across the cell. The corners are the very values `LandscapeSampler` interpolates inside the
cell, and the sampler's surface of a cell never leaves their range, so the spread bounds how far the drawn ground can lie from the basin inside it. The original validates no slope for any
tool (tp.exe `0x535670` has no float operation), so this is OpenTPW's own rule.
Measured on the shipped land (`PoolsTerrainCorpusTests`, with `OPENTPW_GAME_PATH`): the buildable land of the four themes is already flat. About 27 to 29 percent of its cells are exactly level, almost all the
rest differ by one step of 0.2, and the rule refuses 4 of 28,000 cells (the few two-step cells, spread 0.4). It is a safeguard for steeper maps and edits rather than a limit players meet on the original levels.

## Pricing

**75 per cell** (`ParkEconomy.PoolCellCost`), the price of a pool tile in Roller Coaster Tycoon 3 Soaked!, through the economy's cell costs (`CellPurchase.Pool`, `ParkEconomy.CellCost`, `TrySpendCell`):
spent as other costs with a `CellsBought` event, tested like a path cell (the commit refuses when balance minus the price is negative, the ghost when the stroke's total exceeds the balance). The original's
balance files have no pool price, so the number is a constant of the extension. Erasing credits 75 as other income (`ParkEconomy.RefundPoolCell`). A pool built with no economy (the headless sandbox) is free.

## The HUD entry

The original HUD has no entry for it, and the least invasive place is the buy window's **Miscellaneous** list, which already holds the two land rows (Buy Land, Clear Land). While the extension is on, two rows
follow them (`ParkHud.PoolPaintRow`, `PoolEraseRow`, ids -3 and -4, `ParkHud.Pools.cs`): "Pool water" (value 75) and "Remove pool water" (value 0). Choosing a row closes the window and arms the tool. The
rows exist only while `FeatureGates.Pools`; with the extension off the list is exactly the original's. Hover help, the start hints and the "ground too steep" message are OpenTPW's own strings in
`SupplementaryStrings` (`OpenTpwText.PoolRowPaint` and its siblings), translated for all nine languages the table has (English, Danish, Dutch, French, German, Korean, Polish, Spanish, Swedish); the other
refusals reuse "You can't build here" and "Not enough money". The cursor is the path tool's (the cash pointer when the cell cannot be paid for); the hovered cell shows the blueprint marker, blue when it can be written and red when it is refused.

## Rendering

`PoolRenderer` (an `Entity`, created by `Level.RefreshPools` when the first pool cell exists, rebuilt whenever the cell map changes) draws three meshes from `PoolMeshBuilder`, which is CPU-only and unit-tested:

- **Auto-joined edges.** Every cell is cut into 3 x 3 tiles at the rim width (0.2 of a cell). The middle is water; an edge tile is coping when the neighbour on its side is not pool; a corner tile is coping unless both
  side neighbours and the diagonal between them are pool (`PoolEdges`, from the 8-neighbour mask). So an interior cell has no rim, a straight edge three coping tiles, an outer corner five, an inner (concave) corner
  one, and a lone cell eight. All cells cut at the same breaks, so any shape joins without a seam. Between a coping tile and a water tile the mesh adds an inner wall; along the outside of the rim it adds a skirt down to the ground.
- **Heights.** The land cannot be dug, so the pool is a **raised basin**: at each cell corner the water lies 0.15 above the ground (`PoolMeshOptions.WaterLift`, more than half the largest corner spread the ground
  rule allows, 0.125, so the land never shows through the water) and the coping 0.1 above the water. Ground heights come from `LandscapeSampler` (`IParkGrid.GetTerrainHeight`); both levels are interpolated across the cell
  and the ground is continuous across cells, so the water is one surface without steps. The brief asked for the water "a little below the surrounding ground": that is not possible without digging, so
  it is a little above it.
- **Water.** `content/shaders/pool-water.shader`, adapted from `water.shader` (which the lake entity `World/Terrain/Water.cs` uses and the park never instantiates; that entity is unchanged): scrolling,
  wobbling texture coordinates from the position on the ground, a deep to shallow blue mix from a caustic texture, translucent, fogged. The web build has its precompiled `content/shaders/web/pool-water.json`.
- **Textures.** Our own, generated in code (`PoolTextures`, 64 x 64, tiling): pale stone slabs for the coping, light blue ceramic tiles for the walls, caustic light for the water. Nothing from the original game
  and no binary asset is committed.

Tests (`PoolMeshTests`): every one of the 256 neighbour masks tiles the cell exactly once (areas sum to the cell, vertex and triangle counts match the tile counts); the named cases have the expected rim; normals are
unit length, the top surfaces face up, walls are vertical and the winding agrees with the normals; UVs lie in 0 to 1; for six shapes (rectangle, L, ring, diagonal touch, lone cell, two blobs) on flat and on sloped
ground the mesh is closed: no edge is shared by three triangles and the only open edges are the three skirt-bottom edges per outline side, lying on the ground; the water is one surface across cells.
The GPU side (`PoolRenderer`, the shader) is not exercised by tests; **a visual check is needed** before the look is called right.

### Checking it on screen

`--debug-pool [x,y,w,h;...]` (`[EXT:debug-pool]`, smoke-only) paints pool cells through the tool's own builder once the
original level is up. The shape is a list of rectangles, measured from the first place near the camera target that takes
the whole shape and charged as the tool charges. The default shape is a 6 x 4 basin, an L-shaped arm and one single
cell, which together show straight edges, inner and outer corners and an isolated cell. It needs the extension. The log
prints the shape's engine centre, which `--capture-camera` can aim at:

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --load-original-level jungle --enable-mod opentpw.pools \
  --debug-pool default --silent --capture-world pool.png --capture-frames 300 --capture-camera 28.2,-38.6,45,-90
```

First on-screen check (October 17, 2026; Jungle, 43 cells): the water animates, and the coping joins at straight edges,
inner and outer corners and around the single cell. The basin sits on top of the land, so the water reads as a shallow
tray rather than a deep pool. Depth (a darker floor, a visible inner wall) is a follow-up.

## Saves and determinism

See [the save rule](#the-extension-and-the-save-rule). The pins (docs/DETERMINISM.md, "Pools"): every existing pin is unchanged with the extension on or off
(`PoolsTests.TogglingThePoolsExtensionKeepsEveryExistingPinWithoutPoolCells`), and the fixed synthetic run on a cross park with 17 pool cells painted through the builder has its own pin,
`DeterminismTests.PoolsFixedRunHash`.

## Not done yet

- **Guests in the water.** Pool cells are obstacles: guests walk around them. Swimming, changing rooms, swimwear sprites and the guest decision seam are phase P2.
- **Pool objects and extras** (ladders, diving boards, showers, loungers, lamps, lifeguard chair, wave machine, spa): P3.
- **Water slides** on the track system, and attractions that end in a pool: P4 and P5.
- Depth levels (each cell has one depth), diagonal or curved edges (the tiling is square), digging the terrain, a pool-deck cell type, pool water on the Theme Park Inc layout, exporting parks with pools as packages.
- Name and description rows for the Extensions and mods screen are not translated: the screen shows the extension's English name.
