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

- a park **without** pool cells saves **byte-identically** with the extension on or off (no `Mods` field; `PoolsLevelTests.RealCapturesWithoutPoolCellsAreByteIdenticalWithTheExtensionOnAndOff`
  compares two real `Level.CaptureResumeSnapshot` captures). A park whose last pool cell was erased records nothing either, but its other bytes are not those of a park that never had water: the cell map keeps
  the block stamps and the version of the writes;
- a park **with** pool cells records `opentpw.pools 1.0.0` (the digest of no bytes, like every extension) and loads only with the extension on: with it off the load refuses with
  `needs opentpw.pools 1.0.0`, and the load and Continue lists keep the save and add "(requires mod opentpw.pools 1.0.0)";
- a save without the record loads with the extension on or off (`ParkResumeMods.Unmet` does not ask for an unrecorded used-only extension: an active used-only extension needs no record, a recorded one must be active);
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
- Pool cells are not drawn on the park map (the minimap), where they show as open ground.
- The path tool does not refuse fence posts (see the placement rules); neither does the area clear touch pool cells.
- `content/shaders/web/pool-*.json` are checked by `WebShaderTests` only with `OPENTPW_NATIVE_SHADER_TESTS=1` and native SPIR-V libraries that match the host; CI does not run it for any web shader.
