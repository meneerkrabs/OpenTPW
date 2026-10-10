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
stack and commits the line. The tool ends when the click's end is the start,
when a cell is refused, or when the line ends on an existing path or queue cell
(PATH-003); otherwise the end is the next start. Backspace takes back the last
segment without a refund (PATH-002); right click, Escape and Back cancel.

In the park view the hover help is 441 over an empty owned cell, 442 over a
path and 444 over a queue; inside the tools it is 443 and 445. A left click on
an empty owned cell or a path cell starts the path tool (PATH-001; there is no
menu button). After a HasQueue ride is placed, the queue tool starts at the
entrance's outside cell and lays QUEUE-I's queue cells through the same tool.
The remove tool clears an object, else a queue cell, else a path cell. Built
path and queue cells are drawn with the theme's path texture.

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
| PATH-009 | PATH-FREE | free only without a park economy |
| PATH-010 | PATH-LEDGER | posted as other costs |
