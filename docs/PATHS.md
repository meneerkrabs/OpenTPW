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
other costs (PATH-010). There is no batch purchase. Without a park economy
paths are free (PATH-009). A refused cell stops the line; earlier cells stay
built and charged.

Removal is `ClearCell`'s forced path case: links cleared on both sides, no
refund. A `NoModify` cell that still has linked neighbours stays; without
neighbours the flag is dropped and the cell is removed (PATH-005).

## Tool

`CellBuildTool` is shared by the path tool (mode 1) and the queue tool
(mode 3). `SnapEnd` keeps the dominant axis (a tie keeps X); `LayLine` walks
one axis and ignores the other end coordinate. A hover previews the snapped
line (the ghost, no writes); a click pushes the end onto the 1024-entry vertex
stack and commits the line. A click back on the start ends the tool; once a
line exists it lays nothing, as in the original. The tool also ends when a cell
is refused (PATH-012) or when the line ends on an existing path or queue cell
(PATH-003); otherwise the end is the next start. Backspace takes back the last
segment without a refund (PATH-002); right click, Escape and Back cancel.
Escape or Back inside the tool only ends the tool; the next one opens the
pause menu (PATH-011). While the pause menu is open the tool takes no clicks;
the speed pause does not stop it (PATH-013).

In the park view the hover help is 441 over an empty owned cell, 442 over a
path and 444 over a queue; inside the tools it is 443 and 445. A left click on
an empty owned cell or a path cell starts the path tool (PATH-001; there is no
menu button). After a HasQueue ride is placed, the queue tool starts at the
entrance's outside cell and lays QUEUE-I's queue cells through the same tool.
The remove tool clears an object, else a queue cell, else a path cell. Built
path and queue cells are drawn as described in [Path textures](#path-textures).

## Path textures

Each theme's texture table (`Jungle.tct` etc., in `terrain.wad`) lists 22 `PathTex` entries. Entries 0–15
are the path shapes (14 repeats 13), 19 and 20 are second versions of the straight (2) and the edge (10), and
16–18 and 21 are tool markers. Paths are drawn as a blended layer over the ground, so the textures'
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
That includes cells whose extra diagonal bits the rule ignores (0x13, 0x53, 0x93, 0x9F, 0xF9). The routine
in the original that writes these bytes was not located (no table for it exists in `TP.ICD` or the Mac
binary), so the rule is derived from the data.

`OriginalTerrain` draws a cell with the saved texture and turn when it was a path in the save and still has
the same path neighbours. The saved bits also count ride entrances, which the cell map does not keep.
Other path cells, such as built ones or the neighbours of built ones, follow the rule from their path
neighbours. For straights and edges, a hash of the cell picks between the two versions (PATH-014). Queue
cells still use entry 0; the theme's `QueueTex` list is not used yet.

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
| PATH-001 | PATH-ENTER | click on an empty owned cell or a path starts the tool; flat ghost markers |
| PATH-002 | PATH-UNDO | Backspace pops the last segment, no refund |
| PATH-003 | PATH-ENDFLAG | a line ending on a path or queue cell ends the tool |
| PATH-004 | PATH-CONNECT | link every cardinal path neighbour, both sides |
| PATH-005 | PATH-REMOVE | forced removal (a = b = 0), NoModify respected |
| PATH-006 | PATH-SLOPE | no slope limit; the object terrain rule applies |
| PATH-007 | PATH-LAND | every in-bounds cell is owned |
| PATH-008 | PATH-CODE8 | path over a queue cell is refused |
| PATH-009 | PATH-FREE | free only without a park economy (`data:0x7de2d` is a mode 4/59 tool flag) |
| PATH-010 | PATH-LEDGER | posted as other costs |
| PATH-011 | PATH-CANCEL | Escape/Back ends the tool first; the next one pauses |
| PATH-012 | PATH-ENDREFUSE | a line refused part-way ends the tool |
| PATH-013 | PATH-PAUSE | the pause menu blocks tool clicks; the speed pause does not |
| PATH-014 | PATH-VARIANT | built straights and edges pick `str1`/`str2` and `edg1`/`edg2` by a hash of the cell |
