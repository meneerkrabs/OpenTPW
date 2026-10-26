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
the click handler does not itself test the speed flag. The separate P-window input owner now has bounded ordinary-WM pointer routing (PATH-017, still open).

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
original draws its build ghosts from `Data/generic/dynamic/textures` instead ([blueprint markers](reverse/BLUEPRINT.md)). Paths are drawn as a blended layer over the ground, so the textures'
transparent borders show the grass. Before this, every path cell replaced the ground with entry 0 (`squ`, a
lone square), which left a dark outline around each cell.

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
models are baked into the path layer (PATH-015), each vertex on the drawn ground under it and lifted a little as
the path tiles are, so the plank floor shows over the grass and the posts follow a sloped cell.

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
| PATH-017 | PATH-PAUSE-WINDOW | Bounded ordinary-WM pump/admission then drain is implemented for offline motion/button ownership and P: topmost eligible receiver, stable screen/element lifetime, separate movement capture, saved release owner, and the P key's pause backdrop (the 'lolight' layer `0x65dd7b` has flags 1, so `0x65db25` accepts it; the P key (`0x409350`, from the key row `0x40bf77` and the window's own procedure `0x4a2706`) and the offline focus-loss pause (`0x46b74c` -> `0x4092a0(1)` -> `0x481440` -> `0x4a2790`) create it and every resume destroys it (`0x409300` -> `0x4a27c0`), and it covers just the windows that existed then, so a window opened later is above it; the speed buttons, the numpad and a park loaded paused create none, and the backdrop takes the click without stopping the camera). The handler-only speed test does not describe the upstream window routing (`0x65db25`, `0x658b1a`, `0x658b7f`, outer pump `0x45ab13`, drain `0x48a5be`). Still open: alternate buffered-input scheduling; full native keyboard/wheel routing (legacy aggregate wheel/non-P paths can still act below the backdrop); original backdrop/panel rendering and full modal/audio parity; unobserved focus-modifier limits; whether a path or pool drag released over the HUD uses the stored hover cell (the click dispatcher `0x4879d0` takes no message coordinates and acts on the stored cursor mode `0x7b05d8`) is not settled, so the release over the HUD computes the cell under the pointer. |
| PATH-015 | QUEUE-MODELS | queue cells carry the theme's queue models as tp.exe places them (0x535dd0 class 2, 0x5229e0); baked at the cell's mean height, without their .hmp ground deformation, texture-frame animation (the fire0 torches) and quedead's load flag 0x100000 |

Retired: `APPROX:PATH-016` (the links of MAP InitialPath cells: the map init `0x536490` types each cell and links it with the link writer `0x5348d0` in mode 1, the cells in index order, so a cell links only toward the path cells before it and the later cells link back when their own turn comes; `ParkCellMap.FromOriginal` runs the ported writer in that order and no longer links toward non-path neighbours).
