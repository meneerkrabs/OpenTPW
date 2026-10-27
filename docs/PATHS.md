# Paths

The player-facing path builder (PATH-I), traced in
[PATH-plan](reverse/PATH-plan.md). Everything below without a `PATH-NNN` tag
is traced from the Feral Mac binary; the BIN labels sit at the code sites.

## Data: one cell map

`ParkCellMap` (World, CPU-only) is the source of truth for path (type 1) and
queue (type 3) cells: type byte, flags (`NoModify 0x20`, `Unowned 0x40`), the
same-type placement counter, the cardinal links `1/4/16/64` (−Y, +X, +Y, −X)
and the queue link. `GuestPathGrid` is a view over it: a cell is walkable when
its type is path, and its links are the map's links. The cached flow fields
follow `ParkCellMap.Version`, so a write through the builder, the grid or the
map is seen by guests at once.

`ParkCellMap.FromOriginal(map, save)` loads the Easymode save's path cells
with their saved links, else MAP InitialPath cells linked in every direction.
MAP InitialPath path cells get `NoModify`. Object footprints stay in
`ParkObjects` and are checked through a callback rather than copied into the
map. The map is in the canonical hash (`WorldStateHash` schema 5).

## Rules

Per cell, in this order (`ParkPathBuilder.Validate`):

1. inside the map and the build grid, else `OutsideTerrain`;
2. `Unowned` refuses (`NotOwned`);
3. `CanChangeCellType` (empty, path, or queue under its own rules), else `Occupied`;
4. path over path is `Existing`: free, the placement counter goes up;
5. path over a queue cell is refused (`QueueCell`), except over a queue end: a
   queue cell with one link bit, toward another queue cell (the unconnected back
   of a queue; tp.exe's code 8, `0x535D06`), which the write turns into a path
   cell charged as one; it keeps its link to the queue, so the queue's new back is
   connected to it, and the ride's queue is recomputed with the next map edit;
6. objects, fixed items and the sandbox Totem refuse (`Occupied`);
7. the terrain rule must allow the cell: the map start's cell type, so holes are
   refused and `Blocked` ground is not (tp.exe's validator `0x535670` never reads the
   MAP byte); the save's own path cells count as buildable (`Terrain`);
8. money: balance − cost ≥ 0 (`NotEnoughMoney`); the preview tests the pending
   line cost.

Writing a cell stores the type and spends `Costs.PathCell` (20 in `Standard.sam`) as
other costs (tp.exe `0x534879`: Spend adds it to the total costs, shown as
total costs minus staff costs). There is no batch purchase. A played park always
pays: the original's debit skips only while the layout replay of a visited online park
runs (bank `mWithdrawalsEnabled`, tp.exe `0x404140`), and only a builder without a park
economy is free here. A refused cell stops the line; earlier cells stay built and
charged.

The commit then runs its link pass, as the original does (tp.exe `0x52744E`): every
cell of the line goes through the link writer (`PathLinkWriter`, tp.exe `0x5348D0`
mode 1), which ORs bits into the link byte (map cell `+0x0C`): each cardinal path
neighbour on both sides, a type-10 or type-9 neighbour by its queue link, the
diagonal bits 2, 8, 0x20 and 0x80 toward diagonal path cells whose two sides are
paths (and on a neighbour toward this cell's side neighbours, unless the corner
beyond is a queue), and no link to a queue cell; a link to a queue cell clears the
two diagonals beside it. A queue cell under the writer becomes a path cell and keeps
its links. A queue neighbour that links back is refreshed and plays `cat_ui` 139.

The area clear tool (`AreaClearTool`, tp.exe tool mode 0x3a: the buy window's
Clear Land row and the Clear key, Delete by default) takes two clicks: the first
stores a corner, the second clears the rectangle and ends the tool (tp.exe
`0x525da3`). Row by row, each object cell met (type 4, 9 or 10) starts four scans along the cells of the
same owner word (cell `+0x10`, `ParkCellMap.OwnerAt`) and of an object type; the object is demolished when the runs
end inside the rectangle and none of the scanned cells (the first cell past each run included) has the cell flag
0x10; then every path cell in the rectangle is removed. Queue cells stay
(tp.exe `0x52bdd0`). The owner word of an object's cells is the cell word (`y × 128 + x + 1`) of its anchor, written by the placement
(`0x528A70`, `0x539210`); Easymode's saved cells carry exactly these words (`mParentID`, tested on every cell of the Jungle park's
footprints). The flag 0x10 is written only by the placement's `+` and `#` footprint cells, the track add-ons (`ParkCellMap.WriteFootprint`).
`APPROX:PATH-005` (the missing owner word and the untraced writer of the flag) is retired. A park whose objects do not type their cells (a
Theme Park Inc layout, whose placement is not traced) clears by the object standing on each cell. The developer panel's one-click remove tool
(an object, else a queue cell, else a path cell) is `[EXT:dev-one-click-remove]`: it is OpenTPW's and has no original counterpart. Removal is `ClearCell`'s forced path case (tp.exe `0x52c11f`): every bit of the cell's link byte, diagonals included, is
cleared on both sides (tp.exe `0x5367A0`), no refund. A `NoModify` cell that still has linked neighbours stays; without
neighbours the flag is dropped and the cell is removed. The one exception is `[EXT:removable-access-cells]`: the cells a placement lays outside an
entrance or exit (the queue front and the path stubs, all `NoModify`) can be removed by the path and queue removal tools, which the original's cell clear keeps (`0x5367D4`).
The map start's initial path stays fixed. The ride's entrance and exit then resolve to the nearest walkable path as for a ride nothing laid cells for, the guests standing
on the emptied cells are set down (queued guests leave the queue, QUEUE-015), a queue or path can be laid on the cell again, and demolishing the ride clears what is left.
The entrance and exit stay where they resolved until a removal like this one takes a cell they use; a path laid nearer later moves neither, and nor does removing the plain path cell
an entrance fell back to. A save keeps them as they stand, so a load (and a shared park's join) puts them back there before it finds the queues from them, instead of resolving them on
the loaded grid; a save from before keeps the cells the load resolves.
Backspace over a path cell with no tool on uses the counted case (tp.exe
`0x40BE8B`, `ClearCell(0, 1)`): the cell loses one placement and goes only
when it was laid once (`ParkPathBuilder.RemoveOnce`). The original's key does
nothing over a queue cell (it backtracks only a type-1 cell, tp.exe `0x40BE3C`);
OpenTPW also removes a queue there, from the cell under the cursor to the queue's
back with the remove tool's refund (`[EXT:queue-backtrack]`). A queue cell that no ride's queue reaches
(a stray piece, such as one an older save left) goes alone there and with the remove tool, refunding nothing,
as the original's `ClearCell` clears one queue cell (tp.exe `0x5367A0`).
With the pools extension on, Backspace with no tool on also removes a pool bridge, or one pool water cell, under the cursor (`[EXT:pools-bridges]`, `[EXT:pools-tool]`, docs/POOLS.md "Removing with Backspace"). That is OpenTPW's: the original has no pools, and its key does nothing over such a cell. A bridge wins over the water under it, and over a path laid on one of its approach cells before the bridge: the first press takes the bridge, the next one the path.

## Tool

`CellBuildTool` is shared by the path tool (mode 1) and the queue tool
(mode 3). `SnapEnd` keeps the dominant axis (a tie keeps X); `LayLine` walks
one axis and ignores the other end coordinate. A hover previews the snapped
line (the ghost, no writes), drawn as the original's blue blueprint markers with the refused cell in red
([blueprint markers](reverse/BLUEPRINT.md)); a click pushes the end onto the 1024-entry vertex
stack and commits the line. A click whose ghost has a refused cell ends the
tool and lays nothing (tp.exe `0x524a63`). A click back on the start ends the
tool; once a line exists it lays nothing, as in the original. The tool also
ends when the commit reaches a last cell that already was a path or queue cell,
the ride's own queue included (tp.exe end flag `0x5345ad`, read by the pass
`0x81` at `0x527517`); otherwise the snapped end is the next start. Backspace takes back the last
segment without a refund, as the original's Back (tp.exe `0x52fe50`) does:
each cell of that segment loses one placement count and goes when it falls
below zero. Right click, Escape and Back cancel.
Escape or Back inside the tool only ends the tool; the next one opens the
pause menu (tp.exe key dispatch `0x488921`, Escape row `0x40c180`). While the pause menu is open the tool takes no clicks;
the click handler does not itself test the speed flag. The separate P-window input owner has ordinary-WM pointer routing and takes the camera's input too while it is up (PATH-017, narrowed).

In the park view the hover help is 441 over an empty owned cell, 442 over a
path and 444 over a queue; inside the tools it is 443 and 445. A left press on a
queue cell (444, "Left-click to edit this queue") chooses the HasQueue ride that
owns it (tool 0x14, `0x4879d0`); the release starts the queue tool for it at that queue's back cell, wherever the pointer is then (`[EXT:queue-click-at-back-cell]`, an OpenTPW design choice; the original's first click enters tool 0x14, QUEUE-021). A left press on
an empty owned cell or a path cell enters the path tool without a start (tp.exe
`0x4879d0`, SetMode(1) `0x52f200`; there is no menu button), and the **release** of
that click is the tool's first click, at the cell under the pointer then (every line click of
the tool object is the release handler's, `0x524960`; a press moved to another cell before
it lets go clicks that other cell): a refused single-cell ghost ends the tool again (with
the refused click's sound), otherwise the start check (`0x535670(1, 0)`) makes the
cell the start, or leaves the tool on without one when it refuses the cell; a
later click without a start does the same. The second click of a line commits at its release too, never at its press (docs/UI.md, "The park view's pointer"). After a HasQueue ride is placed, the queue tool starts at the
entrance's outside cell and lays QUEUE-I's queue cells through the same tool.
The remove tool clears an object, else a queue cell, else a path cell. Built
path and queue cells are drawn as described in [Path textures](#path-textures).

## Path textures

Each theme's texture table (`Jungle.tct` etc., in `terrain.wad`) lists 22 `PathTex` entries. Entries 0–15
are the path shapes (14 repeats 13), 19 and 20 are second versions of the straight (2) and the edge (10), and
16–18 and 21 name tool markers (`baseblue`, `grnarrow`, `redarrow`, `dark`) that no archive ships; the
original draws its build ghosts from `Data/generic/dynamic/textures` instead ([blueprint markers](reverse/BLUEPRINT.md)). A path is not a layer over the ground (issue 829, package F9): see [Path cells are terrain cells](#path-cells-are-terrain-cells).

The save stores each path cell's choice (docs/TPWS-PAYLOAD.md): byte +17 is the `PathTex` index and the
u16 at +21 is a clockwise turn in degrees. Both follow from the neighbour bits at +8 (0x01 −Y, 0x02 +X−Y,
0x04 +X, 0x08 +X+Y, 0x10 +Y, 0x20 −X+Y, 0x40 −X, 0x80 −X−Y) by one rule:

- A diagonal counts only when both sides next to it are set. That leaves 15 shapes up to rotation, one per
  texture: `squ` (no neighbour), `end`, `str`, `cnr2` (a corner), `cnr1` (a corner with its diagonal),
  `tju1` (a T), `tju3` and `tju4` (a T with one diagonal, mirrored), `edg` (a T with both diagonals:
  the edge of a wide path), `xrd1` (a crossing), `xrd2` (one diagonal), `tju2` (two diagonals on one side),
  `icn2` (two opposite diagonals), `icn1` (three) and `ctr` (all eight).
- The turn is the quarter turn (−Y to +X) that brings the texture's base shape onto the cell's mask;
  symmetric shapes use the smallest turn.
- The textures have +X to the right and +Y in the top row.

All 78 path cells of the Easymode save match this rule (`PathTilesTests`): every shape and turn. Which of
the two straights or edges a cell has is the original's own choice and is not predicted.
That includes cells whose extra diagonal bits the rule ignores (0x13, 0x53, 0x93, 0x9F, 0xF9). The PC
original's tile chooser is tp.exe `0x535dd0` (49-row path table at `0x763138`, 11-row queue table at
`0x7630b0`, called from the tile refresh `0x5365d0`); OpenTPW's rule is still derived from the data.

`OriginalTerrain` draws a cell with the saved texture and turn when it was a path in the save and still has
the same path neighbours. The saved bits also count ride entrances, which the cell map does not keep.
Other path cells, such as built ones or the neighbours of built ones, follow the rule from their path
neighbours. For straights and edges the original's chooser (tp.exe `0x535dd0`) takes the second version
when the roll its previous call stored is set, and every call stores a new `rand() & 1` (`0x53602c`): each
recomputation of a cell's tile (the refresh `0x5365d0`: the world's set-up pass, the link writer's refreshes
and the commit's refresh pass over the line) re-rolls. `PathTileVariants` keeps that rule with its own CRT-formula
generator, a presentation stub: the original's is seeded from the clock and shared by the whole game. Queue
cells get no path tile: the chooser gives a queue cell a class-2 result from its link byte (`+0x0C`, the 11-row
table `0x7630b0`: a dead end, a straight, a bend, turned by the row; a straight turns half way round unless its
queue link is +X or +Y, a bend takes the other bend for some queue links, and a cell linked to a path that links
back takes the end piece: `queend`, `quebin1`, `quebin2`), and the refresh places that model of the theme's `queue`
archive on the cell (`quedead`, `questra`, `quebnd1`, `quebnd2`, `queend`, `quebin1`, `quebin2`; tp.exe `0x522900`,
`0x5229e0`) at the yaw 360 minus the row's rotation, as a one-cell object would be placed (`QueueModels`). The
models are baked into static batches of their own (PATH-015), each vertex draped onto the landscape as tp.exe does it
([Queue models on the terrain](#queue-models-on-the-terrain)), so the plank floor lies on the ground (the cell is a hole of the ground mesh) and the posts follow a sloped cell.

### Path cells are terrain cells

**(October 26, 2026; issue 829, package F9.)** [BIN:TPW-EXE:0x0046DF50 tile word] The tile refresh `0x5365D0` hands the chooser's result (group, tile, angle) to `0x450470` → `0x46DF50`, the only writer of path art (`0x46DF50` has the one caller `0x450470`, whose callers are `0x5365D0` and `0x5366E0`, the save restore). For a path cell
the writer takes the slot of the tile's row in the theme's texture table (the `PathTex` list of the `.tct`), puts it in the high half of the cell's landscape word, clears the turn, mirror and hole bits (`& 0xFF87`, `& 0xFFFE`), sets `0x180` (art, dirty) and one turn bit for the angle (90: `0x20`, 180: `0x10`, 270: `0x08`). `PathCellWord`.
The terrain draw has no path layer: [BIN:TPW-EXE:0x0056F670 cell draw] it draws every cell with the texture of its slot, its four corners permuted by the word's turn bits (`0x56F4F0`, `TerrainCellTexture`), and nothing tests bit `0x80` there; the permutation is the one the former layer's
`PathTiles.TextureCoordinates` computed (`PathCellArtTests`).

- **Procedural textures on.** [BIN:TPW-EXE:0x0056F090 cell list] a listed cell whose word has `0x80` gets the slot's texture as art; [BIN:TPW-EXE:0x00572440 cache] the cache builds the cell through `0x55F780` with that art (`ProceduralTerrainTexturer.Compose`: the base read in the orientation of the word's bits `0x78`, the art point sampled at `width / N`, `base + ((art - base) * alpha >> 8)`), and builds a hit again when the recorded word had `0x100`. The art is the file's image as the composite reads it (`ObjectFloorBase.FlipRows`). The base is read turned so that the drawn base stays upright while the art turns. The path tile's transparent border (alpha 0) is what shows the grass. `ProceduralTerrain.SetCellArt` is the writer's side: a changed tile marks the cell, which is built again the next time it is listed.
- **Procedural textures off, outside the procedural rectangle, and in the camera modes that suspend them.** The cell is a quad of its own with the path texture and no ground under it (the word replaced the ground's slot), and it draws opaque: [BIN:TPW-EXE:0x0056F753 terrain pass state] the cell draw gives every record the constant state word `0x1256` (no blend, no alpha test; `0x101256` with bit `0x2000` of `[0x8BCBC8]`) and never asks `0x590C40` for the cell texture's own alpha type, so a tile's transparent texels are drawn with their stored colour, which is painted grass in the shipped tiles (alpha 0 texels such as (19, 134, 40) of `jpa_str1`). OpenTPW's ground mesh leaves path cells out (`OriginalTerrain.GroundSkips`) and draws them as static cells without a lift.
- **Restore.** [BIN:TPW-EXE:0x0046DF50 word restore] a cleared path cell takes its backup word again with the dirty bit; the ground and, with the textures on, its plain procedural texture return.
- **Faint seams.** The composite samples the art's first texel row and column (`Y * (height / N)`, never the far edge), and the shipped tiles' edge texels are not all opaque (for example `jpa_str1`'s first row has alpha 241 to 252, its side columns 0 to 70 for the stone fringe), so a thin line of the base can show at the border of two path cells. It is in the data and in the original's sampling (the composite equals the emulated tp.exe, and the page coordinates are the original's texel centres), not an offset of OpenTPW's.
- **Snow.** [EXT:winter-snow-paths] a path cell keeps the slush look under snow; with the textures on the flag is on the cell's whole quad (the procedural mesh), so it covers the painted-grass margin of the tile as well as the path.
- **Nothing else.** The writer is called for path, queue and hole cells only; fences, hoardings and land purchases write no cell art (the land flag `0x400` of `0x450D10` darkens the cell's vertex colour, see [Land outside the park](#land-outside-the-park-is-darker)).

### Land outside the park is darker

**(October 27, 2026.)** [BIN:TPW-EXE:0x00450D10 land refresh] The land refresh (called by the level set-up `0x415140`, the fence `0x5323F0` and the purchase `0x5327E0`) sets bit `0x400` of a heightfield cell's landscape word when the map cell it reads (the column and row clamped to the map, `0x450D3D`) has flag `0x40` (unowned) and not `0x80` (the entrance area), and clears it otherwise. [BIN:TPW-EXE:0x0056FB0E terrain colour] The terrain draw (`0x56F670`) halves the vertex colour of all four corners of such a cell (`shr 1`, `and 0x7F7F7F`) for static and procedural cells alike, unless the render flags `[0x8BCBC8]` have `0x800`; [BIN:TPW-EXE:0x0044DC28 lighting option] the LIGHTING option sets `0x1800` when it is 0 and clears it when it is 1, so lighting on dims. `UnownedLandDim` is the rule, `OriginalTerrain.LandFlagsSource` reads the live cell flags, so a purchase brightens the bought cells at the next refresh; `test.shader` halves the corner's lighting colour (`TextureSlotAddressing.LandDimFlag`), which is the original's colour shift without its 8-bit truncation (at most one level of 255). Presentation only.

### Queue models on the terrain

**The drape (October 26, 2026; issue 829, package D2).** [BIN:TPW-EXE:0x00522900 queue models] The loader `0x522900` loads each of the eight queue models with `0x4629D0` and the load flags
`0x12423` (the two `quedead` entries, whose table byte `+5` is set, `0x112423`). [BIN:TPW-EXE:0x004629D0 loader flags] Of these, bit `0x2` becomes the loader's flag `0x40`, which makes `0x461F10` OR
`0x20000` into every mesh node (`0x462610`..`0x462631`) and set the header's `+0x30` bit `0x80` (`0x4625F4`): the same drape flag the kart and water kits carry (`0x10422`, [TRACKS.md](TRACKS.md), "Terrain drape").
The other bits are the footprint skirt (bit `0x1` → `0x46A5A0`: its footprint-mask scan is skipped for a header with bit `0x80`, which `0x461F10` has set by then, so only `0x46AA10` with the `0x80` argument can matter), header `+0x30` bits `0x800` (from `0x20`) and `0x20000` (from `0x10000`), the dummy flag pass `0x46A550`
(bit `0x2000`) and, for `quedead`, `0x100000` (PATH-015). [BIN:TPW-EXE:0x005229E0 queue model] `0x5229E0` places a model with the creation flags `0x33A` through `0x463060`, the footprint `{1, 1}` that
`0x522900` stores at `0x816D68`, the cell (x, y) and the yaw `360 - rotation` (0 for 0); flag 8 runs `0x469940` → `0x469A80`, the routine that drapes the track pieces:

- **Instance height.** [BIN:TPW-EXE:0x00466D70 base height] `0x463060` asks `0x466D70` for the instance height: half the sum of the lowest and highest of the cell's four corner heights (the all-cells flag
  `0x10` of `0x33A` skips the footprint test). [BIN:TPW-EXE:0x00467030 root placement] `0x467030` (flag `0x20`) then multiplies the yaw into the root node's **own** matrix (`0x46F650`, which turns the x and z
  elements of each row by the table cosine and sine, the table at `0x7B0968` filled by `0x4708D0` with `N = 4096`: the cosine of 90 degrees is entry 2048, `-8.74e-8`, not 0) and overwrites only its translation (`+0x40`/`+0x44`/`+0x48`: cell x times
  size, the instance height, cell y times size; the cell advanced by `0x466F90` for the quarter turns). The Space bends and ends (`quebnd1`, `quebnd2`, `queend`, `quebin1`, `quebin2`) carry a quarter turn in that root matrix. `QueueModelDrape.BaseHeight`, `QueueModelDrape.Placement`.
- **The drape.** [BIN:TPW-EXE:0x00469A80 mesh drape] For every node with `0x20000` each vertex goes through the node's chain matrix (`0x4702D0`, `0x46FC00`) into the grid's frame, takes the height
  `(landscape - instance height) + its height there`, and goes back through the inverse of the chain (`0x470040`); x and z are its own. The landscape value is the cell's triangle chosen by the cell word's
  bit `0x4`, always split, from the cell's four corners by index arithmetic over the `(cells + 1)` wide height array (the cell word at index `x + z * cells`), with no `0x800` planar shortcut and no bounds test
  (`LandscapeSampler.SampleDrape`). The instance height cancels: the vertex stands at the landscape under it plus its own model height, up to the single-precision rounding of the two additions.
- **No lift, no offset.** The drape adds nothing else. The 0.01 cell lift that OpenTPW's path quads once carried (a stand-in for blending over the terrain) was never part of the models, and no path quad has it now (they are cells of their own);
  the queue models no longer have it either (they were baked with it and with a planar-shortcut sampler before). The root's own matrix and the table yaw are `QueueModelDrape.Placement`
  (x86 oracle target `tpw-exe-model-placement`, `ModelDrapeGolden.Placements`).
- **Arithmetic.** tp.exe's thread runs the x87 at the 24-bit control word `0x7F` (COAST-032), so the chain, the transform, the triangle and the two height operations are single-precision operations in the
  original's order (`ModelDrape`). The one deviation is that x and z are the transformed vertex's, not the same after the round trip through the float inverse (an ulp of a position, never of a height).
- **No mode.** The drape has no vehicle or bridge mode: the track pieces' modes 1 and 2 ([TRACKS.md](TRACKS.md)) belong to the vehicle height sampler `0x4511A0`, which never reads a queue model (guests walk
  the cell's own height); the drape's arguments are the same for queue models and track pieces (the header bit `0x100` that would change them is not set by either).
- **Slopes, joints, holes.** A vertex on the line between two queue cells reads the same two corners from either cell, so the cells meet at the same height up to the rounding of their two instance heights
  (`QueueModelDrapeTests`, a few `1e-5` model units). The drape reads only the corner heights and the cell word's bit `0x4`: a cell that is a hole of the ground (the pool holes, the procedural floors'
  skirt cells, the flag `0x1` of the cell word) drapes exactly like any other, and a vertex beyond the map's edge reads what the index arithmetic reaches there.
- **Model indices past the table.** [BIN:TPW-EXE:0x00763388 queue model table] The loader's table holds eight entries (`0x763388`..`0x7633E8`; the 12-byte rows after it are other data, the first of them with a zeroed model slot). The chooser (`0x535DD0`) can answer a larger index (a class-2 base of 0 to 3, plus 1 for a corner, plus 3 for each linked path neighbour, up to 10), where the original would read past its slot array; there is no loaded model there to port. OpenTPW draws no model for such a cell (the cell stays a plain ground cell) and `OriginalTerrain.BuildQueueModels` logs one warning per such cell (a player-built queue can reach it).
- **Face normals.** The refresh `0x473C30` → `0x473A40` rewrites only the entry each face's first word points to; every face of every queue mesh (28 models, 74 meshes, 1536 faces in the four themes) points
  into the face-normal block, none into the corner block, so the stored normals light the draped models as they do the original's (`QueueModelDrapeTests`). No node of a queue model has the flag `0x200` that
  would take it out of the walk (the files' node types are `0x1` and `0x401`).
- **Queue cells are holes of the ground.** [BIN:TPW-EXE:0x0046DF50 tile writer] The refresh writes a queue cell the tile `(0, 8, 0)` through `0x450470` → `0x46DF50`, whose first branch ORs `0x101` into the cell word and
  clears `0x80`; the ground draw `0x56F670` (the static and the procedural ground alike) and the visible-cell list `0x56F090` skip a cell whose word has bit `0x1`. OpenTPW makes every queue cell a render hole of the
  ground (`ObjectCellHoles.IsHole`, type 3, with the object cells), so the planks and the ground never compete for the same pixels. With the procedural textures on the composite is N x N texels (the art point sampled every `width / N` texels), so the painted planks of the base art are softer there than in the static mode, which draws the whole art.
- **The skirt and the composite floor.** [BIN:TPW-EXE:0x0046AA10 queue skirt] Every queue model has the base flag (header `0x09`; the loader's drape flag adds `0x80`, so `0x46A5A0` skips its footprint-mask scan) and, from the 1 x 1 footprint
  `{1, 1}`, one skirt entry for its base mesh (`C8`, `80`, `E0` or `A0`, slot 0: `QueueModelFloor`, `ObjectSkirt.Build` with `drapeLoaded`). The entry's slot draws opaque and clamped and the base mesh takes the normalised coordinates, in both
  texturing modes, so with the procedural terrain textures off the base decal (`jpa_que1`/`jpa_que3`, a dirt patch with a soft grass edge) is painted opaque over the hole. With them on, the placement `0x463060` (flags `0x33A`)
  writes the cell's skirt word (`OriginalCellWords.SkirtWord`, the entry turned with the yaw) and `0x57ACE0` blends the art over the cell's procedural ground (`ObjectFloorBase.Compose`, N x N); the refresh then writes the hole.
  The composites of the queue cells are tiles of an atlas page (`ProceduralTerrainAtlas`, 2048 texels a side, no mips), the base mesh's coordinates taken into the tile from its first texel's centre to its last (`TileCoordinates`), so the
  baked batches need no texture of their own per cell.
- **Path cells are not draped.** Only the chooser's class-2 result (queue cells) places a model (`0x5365D0` → `0x5229E0`); a path cell's result is only written into the landscape cell (`0x450470`), a
  ground texture, so there is no mesh for `0x469A80` to drape. OpenTPW's static quads for path cells follow the ground triangles of the cell (`AddCell`), with no lift: the ground leaves the cell out.
- **The depth compare.** The original's device compares depth with LESSEQUAL (`0x57360F`, once, in the device set-up), so the later draw wins a tie where the queue floor meets the ground of the neighbouring cells; OpenTPW's world materials now compare so too (`Material.DepthComparison`, the floors work of PR 854).
- **Presentation only.** The baked vertices are drawn as parts of their own; no simulation state, world hash or save field reads them (`QueueModelCount` is a count).

Verification: `oracle_x86/model_drape_reading.py` is the plan side of the x86 oracle target `tpw-exe-model-drape` (`0x469940` run in the emulator on synthetic landscapes and trees, bit for bit,
[ORACLE.md](reverse/ORACLE.md) X34); `ModelDrapeGolden.cs` replays its cases in `ModelDrapeTests` without the executable.

## Headless API

```csharp
var builder = new ParkPathBuilder( cells, walk, economy, terrain, isOccupied ); // or level.Paths
CellBuildResult check = builder.CheckCell( x, y );            // no writes
SegmentResult ghost  = builder.Preview( start, cursor );      // no writes
SegmentResult result = builder.BuildSegment( start, end );    // snaps, writes, charges
bool ok = builder.TryLayLine( from, to, out result );         // BuildSegment, true when no cell was refused
CellBuildResult removed = builder.Remove( x, y );             // forced, no refund
```

`SegmentResult` has `SnappedEnd`, `Built` (new, charged cells), `Charged`,
`StoppedBy` (`Ok` when complete), `EndedOnExisting`, `Start`, `Line`,
`Existing` (counter bumped, free) and `StoppedAt`. `Level.BuildPath(from, to)`
and `Level.RemovePathCell(x, y)` wrap it with the read-only visit check.

## Approximation register

| ID | Plan name | Interim behaviour |
| --- | --- | --- |
| PATH-007 | PATH-LAND | the land (`ParkLand`: the Hoardings.sam fence from the gate posts, an imported save's own flags and fence, Buy Land) follows the original, except that the fence fill's cell outcome is modelled instead of its scan (0x5311c0), the fence of level flag 0x79d86c bit 0x400 is not laid and a purchase in front of the gate is refused (the original's validator accepts some; its ring walk `0x52ba50` then likely keeps the old ring and drops the new hoardings, charging for no land, or may not terminate) |
| PATH-017 | PATH-PAUSE-WINDOW | Bounded ordinary-WM pump/admission then drain is implemented for offline motion/button ownership: topmost eligible receiver, stable screen/element lifetime, separate movement capture and a saved release owner. the P key (`0x409350`, from the key row `0x40bf77` and the window's own procedure `0x4a2706`) and the offline focus-loss pause (`0x46b74c` -> `0x4092a0(1)` -> `0x481440` -> `0x4a2790`) build the P window: a full-screen layer (`0x65dd7b(1, 1, 1, 0, 0, 0x7ff, 0x5ff)`, flags 1, the 'lolight' backdrop), appended as the last child of the root (the tail of `0x65dd7b` links a new window after its siblings) under the panel 5094 of table `0x752e00`; every resume destroys it (`0x409300` -> `0x4a27c0`). The hit test `0x65db25` takes a shown window with flag bit 1 and lets the last such child win; the layer is the hit window of every point and, with the focus (`0x65e59b` ends `0x47ed80`), of every key, over the windows that existed when it was built, so a window opened later (the Escape menu, a box) is above it. Its procedure `0x4a2680` uses only the P key and the panel's animation (`0x65e72d`, table `0x752ed0`) and hands the rest to the default procedure `0x65f6d1`, which turns a button pair into a click of its own window and forwards nothing: the park click handler `0x4879d0` (called only from the HUD window's procedure, `0x488768`) and the camera's input `0x42a760` (called only by the procedures of the HUD, the hidden-HUD overlay and the chat mode) get no message under it. Here the backdrop owns every click over what lay below and the park camera takes no input (wheel, drag, edge scroll, keys) while it is up; the speed buttons, the numpad and a park loaded paused build no layer. Still open: alternate buffered-input scheduling; the aggregate wheel and the keys other than P of the screens below the backdrop (the legacy paths can still act); original backdrop/panel rendering and full modal/audio parity; unobserved focus-modifier limits; whether a path or pool drag released over the HUD uses the stored hover cell (the click dispatcher `0x4879d0` takes no message coordinates and acts on the stored cursor mode `0x7b05d8`) is not settled, so the release over the HUD computes the cell under the pointer. |
| PATH-015 | QUEUE-MODELS | queue cells carry the theme's queue models as tp.exe places, drapes and floors them (0x535dd0 class 2, 0x5229e0, 0x469a80, the hole 0x46df50, the skirt and the composite 0x57ace0); drawn without texture-frame animation (the fire0 torches) and quedead's load flag 0x100000 |

Retired: `APPROX:PATH-016` (the links of MAP InitialPath cells: the map init `0x536490` types each cell and links it with the link writer `0x5348d0` in mode 1, the cells in index order, so a cell links only toward the path cells before it and the later cells link back when their own turn comes; `ParkCellMap.FromOriginal` runs the ported writer in that order and no longer links toward non-path neighbours).
