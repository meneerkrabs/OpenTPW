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
5. path over a queue cell is refused (`QueueCell`, PATH-008);
6. objects, fixed items and the sandbox Totem refuse (`Occupied`);
7. the object terrain rule must allow the cell; the save's own path cells count
   as buildable (`Terrain`, PATH-006);
8. money: balance − cost ≥ 0 (`NotEnoughMoney`); the preview tests the pending
   line cost.

Writing a cell stores the type, links it to every cardinal path neighbour on
both sides (PATH-004) and spends `Costs.PathCell` (20 in `Standard.sam`) as
other costs (tp.exe `0x534879`: Spend adds it to the total costs, shown as
total costs minus staff costs). There is no batch purchase. A played park always
pays: the original's debit skips only while the layout replay of a visited online park
runs (bank `mWithdrawalsEnabled`, tp.exe `0x404140`), and only a builder without a park
economy is free here. A refused cell stops the line; earlier cells stay built and
charged.

Removal is `ClearCell`'s forced path case (the original's area-clear tool,
tp.exe `0x52bdd0`): links cleared on both sides, no refund. A `NoModify` cell that still has linked neighbours stays; without
neighbours the flag is dropped and the cell is removed (PATH-005).
Backspace over a path cell with no tool on uses the counted case (tp.exe
`0x40BE8B`, `ClearCell(0, 1)`): the cell loses one placement and goes only
when it was laid once (`ParkPathBuilder.RemoveOnce`).

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
the speed pause does not stop it (PATH-013).

In the park view the hover help is 441 over an empty owned cell, 442 over a
path and 444 over a queue; inside the tools it is 443 and 445. A left click on a
queue cell (444, "Left-click to edit this queue") starts the queue tool for the
HasQueue ride that owns it, at that queue's back cell (QUEUE-021). A left click on
an empty owned cell or a path cell starts the path tool (PATH-001; there is no
menu button). After a HasQueue ride is placed, the queue tool starts at the
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
neighbours. For straights and edges, a hash of the cell picks between the two versions (PATH-014). Queue
cells use the separate `QueueTex` list and textures from the theme's `queue.wad`, rather than a normal path tile. Square, end, straight and corner artwork follows the queue's predecessor/successor links; independent queues that merely touch do not visually merge. The back cell connects to its walking-path join. These visual shape/rotation choices are derived from the decoded artwork and existing queue links, not a traced original rendering routine (PATH-015).

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
| PATH-001 | PATH-ENTER | a click on an object-less cell or a path starts the tool (tp.exe 0x4879d0, SetMode(1) 0x52f200); the original enters even when the start check 0x535670(1, 0) refuses the cell (then without a start), OpenTPW does not enter |
| PATH-004 | PATH-CONNECT | path-to-path links match tp.exe 0x5348d0; its type-9/10 links, diagonal bits, OR write and type-3 conversion are not ported |
| PATH-005 | PATH-REMOVE | forced removal (a = b = 0) matches the original's area clear (tool 0x3a, 0x52bdd0); OpenTPW removes one clicked cell, the original a rectangle of type-1 cells and whole objects |
| PATH-006 | PATH-SLOPE | no slope limit, as in the original; the object terrain rule stands in for the original's hole types |
| PATH-007 | PATH-LAND | the land (`ParkLand`: the Hoardings.sam fence from the gate posts, an imported save's own flags and fence, Buy Land) follows the original, except that the fence fill's cell outcome is modelled instead of its scan (0x5311c0), the fence of level flag 0x79d86c bit 0x400 is not laid and a purchase in front of the gate is refused (the original's validator accepts some; its ring walk `0x52ba50` then likely keeps the old ring and drops the new hoardings, charging for no land, or may not terminate); a level without a fence setup, a Theme Park Inc map and a read-only visit keep every cell over the terrain owned |
| PATH-008 | PATH-CODE8 | path over a queue cell is refused; the original turns a queue end (validator code 8, 0x535d00) into a path cell |
| PATH-013 | PATH-PAUSE | the pause menu blocks tool clicks (as tp.exe 0x48c8d0); the speed pause does not; the original's P pause opens a full-screen layer (0x47ed80 → 0x65dd7b) under its panel, whose effect on park clicks is untraced |
| PATH-014 | PATH-VARIANT | built straights and edges pick `str1`/`str2` and `edg1`/`edg2` by a hash of the cell; the original rolls CRT `rand() & 1` on every recomputation (0x535dd0, 0x53602c) |
| PATH-015 | QUEUE-TEXTURES | queue cells use `QueueTex` ground textures; the original places queue models (0x5229e0, `quedead`..`quebin2.md2`) and no `QueueTex` texture |
