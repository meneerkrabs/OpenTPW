# PATH-R: path-building evidence and the PATH-I implementation plan

Lane: PATH-R (research). Input: the strict M3 gate (`ea09cda`, `docs/M3-GATE.md`) fails
`build.paths`. OpenTPW has no player-facing path builder: nothing outside the gate calls
`GuestPathGrid.SetPath`, and the HUD has no path tool. The gate lays a scripted spine instead
(`APPROX:GATE-002`). This document consolidates what was already known, adds new bounded traces
of the Feral Mac `SimThemePark.data` (SHA-256 `04809cd4…e295f5`), and specifies PATH-I. PATH-I
must share its build-tool plumbing with QUEUE-I (`docs/reverse/QUEUE-plan.md`, commit `58c0d92`).

Reproduce (private assets stay outside Git):

```sh
python3 -I tools/ppc-analysis/lanes/path/path_evidence.py /path/to/mac-feral/bin --pc-data /path/to/Data
python3 -I tools/ppc-analysis/run_evidence_checks.py --mac-bin /path/to/mac-feral/bin --pc-data /path/to/Data
```

Confidence uses the PPC-rides convention. **High** means an identity-pinned instruction or
relocation path whose operations were interpreted. **Medium** means a role inferred from callers,
strings or data. **Unresolved** means the path was not recovered. "Bounded" marks a claim that
holds only for the enumerated sites or ranges. All results are static. Nothing original was
executed. Addresses are code-section offsets unless marked `data:`.

## 1. What was already known (consolidated)

| Source | Claim | Status after this lane |
| --- | --- | --- |
| QUEUE-plan §3.3 | 68-byte map cell: `+8` type, `+12` connection bits, `+13` queue link. Type 3 = queue. 46 link writers in the build family `0x70b98..0x8c7c0`. Placement, cost and tool flow unresolved | Kept. Type 1 = path (section 2). The tool flow is now traced (section 4) |
| APPROX-TRACE ECON-028 | "Build-mode selector `0x82ac4`, modes 1 and 3" checks the balance against cost globals filled by `0x10f29c` | **Reinterpreted**: `0x82ac4` is `SetCellType(cell, type)`. Types 1 and 3 are path and queue cells, priced with Costs.PathCell and Costs.QueueCell (section 3) |
| APPROX-TRACE RIDES-018 | No build-refusal string tied to a footprint/slope/path/land check | Path rules found without strings (sections 3, 5). Slope: still none (section 5.5) |
| APPROX-TRACE RIDES-028 | Strings "No path attached to end of queue!", "Removing path cell with no neighbours but NOMODIFY set" | The NOMODIFY string is now placed: the path-removal case of `ClearCell` `0x859b4` (section 6) |
| UI-MAP, PPC-ui UI-031 | No path tool documented. UI-031: "the manual says the queue tool follows ride placement" | Park-view hover help and the drag tool are traced (section 4). `SetMode(3)` after placing a HasQueue ride is seen (section 8) |
| `SaveCell` (OpenTPW, `TPWS-PAYLOAD.md`) | Save byte 13 bit 0 = path. Byte 8 = cardinal links `0x01/0x04/0x10/0x40`. Every cardinal neighbour that is a path is linked (78 of 78 Easymode cells) | Kept as DATA evidence. The same four bit values are the in-memory cardinal links (section 5.3). The save record layout is **not** the memory layout (memory `+8` is the type) |
| OpenTPW runtime | `GuestPathGrid` (walkable + 4-bit links, BFS flow fields). `ParkEconomy.TryBuyCells(Path, n)` posts `Costs.PathCell × n` to `OtherCosts`. `OriginalParkGrid.CheckTerrain` refuses objects on save path cells. HUD build arm: 4 category buttons (rides, shops, sideshows, features). `ObjectCatalogEntry.IsTool` hides shapes with no occupied cells | No path tool, no cell-type map, no NOMODIFY, no land ownership, no placement counter. PATH-I adds them (section 9) |

## 2. Costs and cell types (high)

The main SAM descriptor table (`data:0x34d10`, the economy lane's `schema.py`, no embedding)
binds:

| Key | Balance offset | Cost global (TOC slot → data) | Read by |
| --- | ---: | --- | --- |
| `Costs.QueueCell` | 1468 | `0x1260 → 0x84ad0` | `0x7bc70` (type 3) |
| `Costs.PathCell` | 1472 | `0x1264 → 0x84ad4` | `0x7bc58` (type 1) |
| `Costs.KartTrackCell` | 1476 | `0x125c → 0x84acc` | `0x7bc88` |
| `Costs.WaterTrackCell` | 1480 | `0x1258 → 0x84ac8` | `0x7bca0` |
| `Costs.MapCell` | 1484 | read directly by Buy Land (`0x7ec34`) | section 5.2 |

The loader `0x10f29c` reads the level balance record (medium name; TOC slot `0xa7c → data:0x54860`) at
`+1472/+1468/+1476/+1480` and passes each to a setter (`0x7bc4c/64/7c/94`). An independent
anchor pins the record: the same function reads `+1684` and passes it straight to the engine's
`SetFogColor`, and 1684 is `ThemeEngine.FogColour` in the schema.

`SetCellType` (`0x82ac4`) prices **type 1 with the path cost** and **type 3 with the queue
cost**, and the park-view hover shows help 442 "extend this path" over type 1 and 444 "edit this
queue" over type 3 (section 4.1). So **cell type 1 = path, type 3 = queue** (high).

PC data (`Data/levels/Standard.sam`): `Costs.PathCell 20`, `Costs.QueueCell 75`,
`Costs.MapCell 100` (`Online_Standard.sam` has the same three). `jungle/Easy_Standard.sam`
overrides only kart, water and map costs. Which `.sam` layers feed `data:0x54860` per game mode
is not traced; OpenTPW's `BalanceSettings` already reads `Costs.*`.

Other types seen in the traced code, named only by behaviour (medium): 0 empty; 2, 5, 7, 30
are written by map initialisation from the MAP attribute byte (water, blocked, outside, fixed
walkway in OpenTPW's `MapCellFlags` terms); 4, 9, 10, 11, 16, 21, 24 belong to rides, queue ends
and tracks. `0x851ac` accepts 3 or 9 (queue walk), `0x85258` = type 0, `0x85190` = type 1.

## 3. The charging write: `SetCellType` and its gate (high)

### 3.1 `CanChangeCellType` `0x8408c(cell, new)`

Allowed when, in this order:

1. `new == 0` (clearing);
2. not (`new == 4` and `old == 4`);
3. `new == 21` and `old == 21`;
4. `new == 1` and `old == 3` (path over queue);
5. `new == old`, or `old == 0` (empty cell);
6. `new == 4` and `old == 1`;
7. `new == 3` and `old == 1` **only while the last-cell flag** (`data:0x84b2c`) is set.

Everything else is refused. So a **path cell can only go on an empty cell, an existing path cell
or a queue cell**, and a queue may overwrite a path only with its last cell (the "click onto path
to connect the queue" end).

### 3.2 `SetCellType` `0x82ac4(cell, type)`

1. Refuse when `CanChangeCellType` refuses.
2. **Same nonzero type**: the signed placement counter `cell+32` goes up by one and the call
   succeeds. **No charge.**
3. `cost = PathCost` for type 1, `QueueCost` for type 3, else 0. When the byte `data:0x7de2d` is
   nonzero, `cost = 0`. The byte is a short-lived park-view tool flag: the hover routine sets it
   in tool mode 4 (`0x6f5dc`/`0x6f5f4`), the click handler sets it at `0x71330` and in mode 59 at
   `0x71cb8`, and it is cleared at `0x70ca4`, `0x71c94` and `0x722ec` (REVIEW-PATH §1; a TOC-slot
   scan plus one r13 site, so other writers through cached registers may exist). It is never
   raised in path or queue mode by these writers. Treating "no park economy" as free stays
   `APPROX:PATH-FREE`.
4. Path over queue (`new 1`, `old 3`) is remembered for step 7. Queue over path runs
   `ClearCell(cell, 0, 0)` first, with the conversion flag `data:0x84adc` raised.
5. **Money test** only while `game+36 == 0`: `affordable = balance − cost ≥ 0` (signed, balance
   is bank `+12`, `0xcbf48`). Types 1 and 3 are refused when not affordable. There is no
   bankruptcy test here.
6. Store the type at `+8`, then update the map (`0xd7dc8/0xd7ddc` on `game+728`).
7. A queue cell turned into path calls `QueueEdited` (`0xdd57c`) on the ride found through the
   cell's `+16` owner cell, when that owner cell is type 4, 9 or 10 (`0x82c6c..0x82cc8`).
8. **Spend** (`0xcbfdc`) the cost for type 1 and type 3. Spend lowers the balance and adds to
   total costs (`game+0x1f5a0`, the UI-MAP ledger offset) only when bank `+276` is nonzero.

The charge is **per cell, at write time**. There is no batch purchase.

### 3.3 The placement validator `0x84154(cell, type, …)` (high for listed operands, bounded)

`SetCell` (`0x80c50`) with the type flag `0x200` only runs this validator. A return of 1
refuses. For type 1 (bounded to the operands below):

- prices the cell (`0x843c0..0x843dc`) and, unless the cell already has this type or the free
  byte is set, adds the price to the **pending line cost** (`0x7bcb8`, `data:0x84ac4`);
- while `game+36 == 0`, refuses when the pending line cost exceeds the balance (`0x84550`);
- refuses a cell whose flag `0x40` is set (`0x8432c`, unowned land, section 5.2);
- path over path returns 0 (`0x84864`);
- path over type 4 or 9 refuses (`0x8497c`);
- path over a queue cell (`0x849a4`): code **8** when the queue cell has exactly one link and the
  linked neighbour is also a queue (a queue end), else refused. What code 8 makes the caller do
  is unresolved (`APPROX:PATH-CODE8`).

## 4. The path tool (high for operations, medium for the session interpretation)

### 4.1 Entering the tool

The park-view hover routine (`0x139c64`) picks the help line from the cell under the cursor:

| Cell under cursor | Hover state | Help (UIHELPTEXT) |
| --- | --- | --- |
| empty (`0x85258`) | 2 | 441 "Left-click to build path" |
| path (type 1, jump table `data:0x47b7c[1]`) | 1 | 442 "Left-click to extend this path" |
| queue (type 3, `[3]`) | 3 | 444 "Left-click to edit this queue" |
| types 4, 9, 10 | object-dependent | not needed here |

Inside the tool, 443 reads "Left-click to build the path, BACKSPACE to undo / Click onto
existing path to complete it", and 445 is the queue tool's line. So **there is no build-menu
button for paths**. The path tool starts from a left click on empty ground or on an existing
path in the park view. The click handler that sets mode 1 was **not traced**: mode changes go
through `SetMode` (`0x7b320`), whose callers pass the mode in registers or through the
`ACTION_SET_MODE` replay record (`ACTION_*` strings at code `0x1d422b`). `APPROX:PATH-ENTER`.

The tool mode is the global `data:0x84b04` (getter `0x7b878`, test `0x7b890`). **Mode 1 is the
path tool and mode 3 the queue tool**: the drag code writes the cell type equal to the mode
(section 4.3).

### 4.2 `LayLine` `0x84ea4(type, x0, y0, &x1, &y1)` (high)

- Cell id = `1 + x + (y << 7)`. Record = `game + 728 + 68 × (id − 1)`.
- If `|x1 − x0| > |y1 − y0|`, walk X along row `y0`. Otherwise walk Y along column `x0`. **The
  other end coordinate is ignored.** A segment is always straight, never diagonal.
- Each cell before the end: `SetCell(cell, type, dx, dy)` with the unit step. The **end cell**
  is written with the last-cell flag (`data:0x84b2c`) raised.
- The first refused cell stops the walk and returns 0. Cells already written stay written, and
  stay charged.
- A cell outside the 128 × 128 array (`0xd70d8`) is **skipped**, not refused. When the skipped
  cell is the end, `LayLine` returns 1 with the last-cell flag left raised. The tool's snapped
  ends are grid cells, so play never reaches this (REVIEW-PATH N1).
- `LayLine` itself never calls Spend (negative witness over `0x84ea4..0x85174`).

### 4.3 Preview, commit, session (high for the calls; medium for the sequence)

- **Preview** (`0x6f380`, cursor move): in mode 1 or 3, snap the cursor to the dominant axis of
  `start → cursor` and call `LayLine(mode | 0x100, start, end)`. Flag `0x100` is the ghost
  preview. With no start yet, preview a single cell. **Snap rule**: `|dx| ≥ |dy|` keeps X
  (`end.y = start.y`), otherwise Y. A tie keeps X.
- **Commit** (`0x70f84..0x71304`, click): snap the same way. When the end differs from the
  start, or when it equals the start while exactly one vertex is stored (`0x7bce8() == 1`), push
  the end onto the **vertex stack** (`0x7bcf4`: up to **1024** points, count `data:0x84ab8`).
  Then clear ghosts (`LayLine(0x87 …)`) and commit with **`LayLine(mode, start, end)`**: the raw
  mode, no preview flag. Then clear the ghosts of the other kinds
  (`0x80, 0x82, 0x85, 0x86, 0x83, 0x81`). In mode 3 it also calls `QueueEdited`. Where the first
  vertex is pushed (`0x70f24` after the reset at `0x70f18`) was not read in detail.
- **Session end**: when the clicked end equals the start with more than one vertex stored,
  `bf eq` at `0x71084` jumps to `0x71174`, past the push and **every** `LayLine` (the commit at
  `0x710c0` included), so the ending click lays nothing. When the clicked end equals the start,
  the start is reset to −1 and `SetMode(0)` ends the tool. Otherwise the end becomes the next
  start. This is "click again to stop building". The flag read at `0x7123c` (`sp+2380`) is
  `cntlzw` of the **last** `LayLine` result, the ghost clear `LayLine(0x81)` at `0x71164`, not of
  the commit; whether a refused commit ends the tool is therefore not traced
  (`APPROX:PATH-ENDREFUSE`).
- **Completing on a path**: when the last cell of a write lands on a cell that is already type 1
  or 3, `SetCell` sets the flag `data:0x84b34` (`0x829f0..0x82a1c`). Its consumer was not found
  outside `SetCell` (`APPROX:PATH-ENDFLAG`). The help text says such a click completes the path.
- **BACKSPACE undo**: only the help text and the vertex stack are proved. The key handler and
  what an undo removes or refunds are **unresolved** (`APPROX:PATH-UNDO`).
- The validator's pending line cost (section 3.3) is what the preview checks. Whether the
  commit is blocked while the preview is invalid is not proved. The commit itself re-checks
  money per cell (section 3.2).

## 5. Placement constraints

### 5.1 Cell content (high)

Section 3.1: empty, path or queue only. Path refuses types 4 and 9 (`0x84984/0x8498c`) and
every other nonzero type through `CanChangeCellType`. Objects occupy cells with their own types
(the ride placement code `0x75618` calls `SetCell` with types 4, 11, 16, 24 and others),
so **paths cannot overlap rides, shops, entrances or fixed items**.

### 5.2 Land ownership (high for the operands, medium for the name)

- Map initialisation (`0x85538`) sets cell flag `0x40` on every cell that is not an
  InitialPath cell.
- Buy Land (`0x7ebf0..0x7ec70`) counts the cells in its rectangle that have flag `0x40`, and
  charges **`Costs.MapCell` (+1484) per counted cell** against the balance. 24 call sites clear
  the flag (`0x84aa4(cell, 0x40)`, in `0x7d7e4..0x7df2c` and `0x881b0..0x8832c`). Which of them
  is the Buy Land write, and which mark the starting park as owned, is not traced.
- The validator refuses any cell with flag `0x40` (`0x8432c`).

So **paths can only be laid on owned land**. "0x40 = unowned" is the analyst label.

### 5.3 Connections, corners, diagonals (high / medium / DATA)

- Cardinal link bits are `1, 4, 16, 64`. Removal visits them by rotating left 2 bits from 1
  (`0x85c44`). `1/16` and `4/64` are opposite pairs: `0x85310` returns the byte only for two
  perpendicular links (a corner). The diagonal bits (2, 8, 32, 128) are not used by any traced
  path code.
- The writer that links a new path cell to its neighbours (`0x82d6c`, which uses the run-time
  neighbour tables `data:0xec52c…`) is **not traced** (`APPROX:PATH-CONNECT`). DATA: in the
  Easymode save every cardinal path neighbour is linked (78 of 78). PATH-I links all cardinal
  path/queue-compatible neighbours.
- No diagonal placement exists: `LayLine` is axis-aligned (section 4.2).

### 5.4 Fixed paths: InitialPath and NOMODIFY (high)

`0x85538` reads the MAP attribute byte at cell `+38`. Bit `0x08` (OpenTPW's
`MapCellFlags.InitialPath`) makes the cell **type 1 with flag `0x20` (NOMODIFY)**, linked via
`0x82d6c(cell, 1, …)`. In the same branch, the other cells end as type 0 with flag `0x40`.
`0x10` (EntranceArea) adds flag `0x80` and `0x04` adds `0x400`. The other branch (when
`0x4d5d0(x, y)` is zero) writes types 2/30/7 from bits `0x02/0x80`. Whether `0x4d5d0` means
"inside the park" is not established (bounded).

### 5.5 Height and slope (negative, bounded)

No height or slope operand occurs on the type-1 route through `SetCellType`,
`CanChangeCellType`, `LayLine` or the type-1 branches of the validator that were read. The
preview branch of `SetCell` (`0x80cf8..0x82390`, read only in part) calls `0x509e8` (role not
traced; returns a float) at `0x80dcc`, and only when the record returned by `0xcd36c(cell)` has
type 11, 12, 16, 17 or 25. **No slope rule for paths is established** (`APPROX:PATH-SLOPE`).
Captures of the original on hills would be needed to rule one out.

## 6. Removal and refund (high for `ClearCell`; player route unresolved)

`ClearCell` `0x859b4(cell, a, b)`, path case (types 1 and 10, jump table `data:0x3e530`):

1. With NOMODIFY (`+14 & 0x20`): when the cell has no neighbours (`0x6e110`), print "Removing
   path cell with no neighbours but NOMODIFY set" and clear the flag. A NOMODIFY cell that keeps
   its flag is **not removed**, unless the conversion flag is set (`data:0x84adc`, queue over
   path).
2. Counter `+32`: `a == b == 0` sets −1 (forced removal), otherwise counter − 1. The cell stays
   while the counter is ≥ 0 (and no conversion is running).
3. Removal: for each cardinal link (1, 4, 16, 64), clear both sides and refresh the neighbour
   (`0x85740`). A path cell whose neighbour is type 9 (a queue end) notifies the owning ride
   (`QueueEdited`, "Back of queue is %sconnected"). Then it clears all links, the queue link,
   `+14` and `+16`, and calls `SetCellType(cell, 0)`.
4. **No refund**: the path case calls no Earn (negative witness over `0x85a9c..0x85ca0`). In
   contrast, the queue case refunds `Earn(QueueCost × 0xe2424(ride) / 100)` (`0x85dcc..0x85df8`),
   where `0xe2424` is the ride's age-based scrap percentage (ECON-025 family).

The player's bulldozer route into `ClearCell`, and which `a`, `b` it passes, are **not traced**
(`APPROX:PATH-REMOVE`).

## 7. Money primitives (high)

- `0xcbf48` balance = bank `+12`.
- `0xcbf50` **Earn**(bank, n): balance += n; cash-in (`game+0x1fc90`) += n.
- `0xcbfdc` **Spend**(bank, n): only when bank `+276 ≠ 0`. Balance −= n; total costs
  (`game+0x1f5a0`) += n; bank `+292` −= n. Spend never checks the balance: the callers do.

The ledger category the original shows for path spending is not identified beyond "total costs"
(`APPROX:PATH-LEDGER`). OpenTPW posts `OtherCosts`, which UI-MAP derives as total costs minus
staff and loans, so it is consistent.

## 8. Queue interplay (QUEUE-I context, medium)

- A HasQueue ride (type record `+64`) that is placed through `0x748a0` enters **mode 3**
  (`SetMode(3)` at `0x7497c`). This matches UI-031 "the queue tool follows ride placement".
- Queue and path share `SetCell`, `LayLine`, the validator, `CanChangeCellType`, the cost
  globals, the vertex stack and the preview/commit code. Only the mode (1 or 3) differs.
- A queue's last cell may overwrite a path (section 3.1, rule 7). That is how a queue connects
  to a path ("Click onto path to connect the queue to it"). Overwriting a queue with path is
  allowed, and the ride gets `QueueEdited`.

## 9. PATH-I implementation spec

Everything without an APPROX tag below carries the confidence of the section it cites.

### 9.1 Data (one cell map for paths and queues)

Add a CPU-only **`ParkCellMap`** (World, no GPU), sized like the MAP grid:

| Field | Values | Source |
| --- | --- | --- |
| `Type` (byte) | `Empty 0`, `Path 1`, `Queue 3`, `Blocked` (any object or terrain type) | §2 |
| `Flags` (ushort) | `NoModify 0x20`, `Unowned 0x40` (keep `0x80`, `0x400` raw) | §5.2, §5.4 |
| `PlacementCount` (short) | extra placements of the same type | §3.2, §6 |
| `Links` (byte) | cardinal bits `1/4/16/64` (−Y, +X, +Y, −X as in `SavePathConnections`) | §5.3 |
| `QueueLink` (byte) | QUEUE-I only | QUEUE-plan §3.3 |

Initialisation:

- MAP `InitialPath` → `Path` + `NoModify`, linked to cardinal path neighbours.
- Easymode save path cells (`SaveCell.IsPath`) → `Path` with the saved links.
- Object footprints (`ParkObjects`) → `Blocked`.
- `Unowned`: set from the MAP for cells outside the owned park. The owned-land source is
  `APPROX:PATH-LAND` until the save and MAP ownership bits are decoded. Until then, PATH-I
  treats every non-`Blocked` in-bounds cell that passes `IParkGrid.CheckTerrain` as owned.

`GuestPathGrid` stays the guests' walk graph. It is **derived** from `ParkCellMap` (Path and,
after QUEUE-I, Queue cells, with their links). Every edit calls `SetPath` and `Invalidate` for
the touched cells only, and the version bump re-plans guests as today.

### 9.2 Validation (per cell, in this order)

1. In bounds, else `OutsideTerrain`.
2. `Unowned` → refuse `NotOwned`.
3. `CanChangeCellType(old, new, lastCell)` exactly as §3.1 → else `Occupied`.
4. For path: old type 4 or 9 → `Occupied`. Old queue → refuse unless it is a queue end
   (`APPROX:PATH-CODE8`: PATH-I refuses until traced).
5. `IParkGrid.CheckTerrain(x, y)` must be `Allowed` (water/blocked/entrance area). This is the
   existing terrain rule, kept as `APPROX:PATH-SLOPE` (no slope test).
6. Money: §3.2 step 5 (`balance − cost ≥ 0` per cell), skipped when the economy is off (the
   sandbox, the equivalent of `game+36`).

### 9.3 Cost and refund

- Path cell: `Costs.PathCell` (20), queue cell: `Costs.QueueCell` (75), charged **per cell when
  written**, through a new `ParkEconomy.TrySpendCell(CellPurchase kind)`. It is the same
  posting as `TryBuyCells(kind, 1)` (OtherCosts, `CellsBought` event), but the money test is
  §3.2's. Keep `TryBuyCells` for callers that buy batches.
- Writing path onto path: `PlacementCount + 1`, no charge.
- Removing path: **no refund**. Removing queue: refund `QueueCell × scrapPercent(ride) / 100`
  (QUEUE-I, ECON-025's scrap percentage).
- A refused cell stops the segment. Earlier cells stay built and charged (§4.2).

### 9.4 Tool and HUD (input → tool → preview → commit)

`CellBuildTool` (CPU-only, shared with QUEUE-I) has `Mode ∈ {None, Path 1, Queue 3}`, `Start`
(`−1` when unset), and a vertex stack (capacity 1024):

1. **Enter**: a left click in the park view on an empty owned cell, or on a path cell, starts
   path mode with `Start = cell`. The hover help is 441, 442 or 444 (§4.1). A HUD "build path"
   button is **not** original: `APPROX:PATH-ENTER` allows one in the build arm as an extra
   entry point.
2. **Preview** on cursor move: `end = SnapEnd(Start, cursor)`. Compute the `LayLine` cells.
   Validate each one (no writes). Show ghosts. Show the total cost and refuse the preview when
   `total > balance` (§3.3).
3. **Commit** on a left click: `end = SnapEnd(Start, click)`. Push the vertex. Write every
   `LayLine` cell through `SetCellType` (charging per cell). Then:
   - if `end == Start` with more than one vertex stored, write nothing and end the tool
     (§4.3, `0x71084`);
   - if `end == Start` (the first click on the start lays that one cell) or a cell was refused,
     end the tool (mode None; the refusal case is `APPROX:PATH-ENDREFUSE`);
   - else `Start = end`, and continue.

   Ending on an existing path or queue also completes (`APPROX:PATH-ENDFLAG`: PATH-I ends the
   tool there).
4. **Undo** (Backspace): pop the last vertex and remove that segment's cells. PATH-I refunds
   nothing on undo, which matches §6 for removals. `APPROX:PATH-UNDO`.
5. **Cancel** (right click or Escape) ends the tool without writing. Escape inside the tool
   does not also open the pause menu; the next Escape does. `APPROX:PATH-CANCEL`.
   While the pause menu is open the tool takes no clicks, neither through the HUD nor through
   the park click; the economy's speed pause does not stop it (`APPROX:PATH-PAUSE`).
6. **Remove** (the existing remove tool on a path cell): §6 with `a = b = 0` (forced),
   respecting NoModify. `APPROX:PATH-REMOVE`.

`ParkHud` gets no new art. It routes the park-view click to `CellBuildTool` when no catalog item
is pending, and it reuses the help strings 441–445 from `UIHELPTEXT`.

### 9.5 Headless API (for the M3 gate)

The `ParkPathBuilder` namespace is `OpenTPW` (World). It has no GPU and no `Level` dependency:

```csharp
public sealed class ParkPathBuilder
{
    public ParkPathBuilder( ParkCellMap cells, GuestPathGrid walk, ParkEconomy? economy, IParkGrid terrain );
    public CellBuildResult Validate( int x, int y, CellType type, bool lastCell );    // no writes
    public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor );      // snapped cells + total
    public SegmentResult BuildSegment( (int X, int Y) start, (int X, int Y) end );    // snaps, writes, charges
    public bool Remove( int x, int y );                                               // §6, no refund
}
public sealed record SegmentResult( (int X, int Y) SnappedEnd, IReadOnlyList<(int X, int Y)> Built,
    long Charged, CellBuildResult StoppedBy, bool EndedOnExisting );
```

`Level` owns one instance, and `CellBuildTool` (the HUD) calls it. The gate replaces GATE-002 as
follows:

```text
var builder = new ParkPathBuilder( cells, grid, economy, buildGrid );
var result  = builder.BuildSegment( spineStart, spineStart + 14 * laneDirection );
build.paths PASS iff result.Built.Count > 0, every built cell is walkable and linked to the
entrance walkway, Charged == Built.Count * Costs.PathCell, and the ledger rose by Charged.
```

The gate keeps its site search for objects. `IsBlocked` then reads `ParkCellMap` instead of its
own spine set (removes divergence D2').

### 9.6 Shared plumbing with QUEUE-I (contract)

| Piece | Owner | Notes |
| --- | --- | --- |
| `ParkCellMap` (type, flags, counter, links, queue link) | PATH-I creates; QUEUE-I adds `QueueLink` and type 9 | one map, no second grid |
| `CanChangeCellType`, `SetCellType` (cost by type) | PATH-I | rules 4 and 7 already cover queue ↔ path |
| `LayLine`, `SnapEnd`, vertex stack, preview/commit | PATH-I (`CellBuildTool`) | the mode decides the type written |
| last-cell flag | PATH-I | lets a queue's end overwrite a path |
| link byte, back-of-queue, `QueueEdited`, queue refund | QUEUE-I | called from `SetCellType` / `Remove` hooks |
| HasQueue ride placed → mode 3 | QUEUE-I | `Level.PlaceObject` calls `CellBuildTool.Enter(Queue, entranceFront)` |

QUEUE-I should not add a second cell store or a second tool. If QUEUE-I lands first, PATH-I
adopts its cell store and adds the fields above.

## 10. Unknown, or must stay APPROX

| ID | What is unknown | Interim PATH-I behaviour |
| --- | --- | --- |
| `PATH-ENTER` | The click handler that sets mode 1; whether a menu entry exists | click on empty owned cell or on a path starts the tool; an optional HUD button |
| `PATH-UNDO` | BACKSPACE handler, what it removes and refunds | pop the last segment, no refund |
| `PATH-ENDFLAG` | Consumer of `data:0x84b34` | ending on path/queue ends the tool |
| `PATH-CONNECT` | Link writer `0x82d6c` and its neighbour tables | link all cardinal path neighbours (Easymode DATA) |
| `PATH-REMOVE` | Player bulldozer route, its `a`, `b` arguments | forced removal (`a = b = 0`), NoModify respected |
| `PATH-SLOPE` | Any slope or height limit (none found, bounded) | none, plus `IParkGrid.CheckTerrain` |
| `PATH-LAND` | Source of owned land in saves and MAPs | owned = terrain-allowed and in bounds |
| `PATH-CODE8` | Validator code 8 (path over a queue end) | refuse |
| `PATH-FREE` | `game+36`; byte `data:0x7de2d` is a park-view flag set in tool modes 4 and 59 (§3.2), not a sandbox switch | free only without a park economy |
| `PATH-LEDGER` | Ledger row of path spending | `OtherCosts` |
| `PATH-CANCEL` | Escape/Back key handling in tool modes 1 and 3 | Escape ends the tool; the next Escape opens the pause menu |
| `PATH-ENDREFUSE` | Whether a refused commit ends the tool (`0x7123c` reads the ghost-clear `LayLine(0x81)`) | a line refused part-way ends the tool |
| `PATH-PAUSE` | Park-view input while the game is paused | the pause menu blocks tool clicks; the speed pause does not |
| — | Type names for 4, 9, 10, 21, 24, 30; `0x4d5d0` meaning; `+0x80`/`+0x400` flag uses | not needed for paths |
| — | Which `.sam` layers fill the cost record per mode | `BalanceSettings` as today |

## 11. Witnesses and tests

Lane `tools/ppc-analysis/lanes/path/`: `path_evidence.py`, `test_path_evidence.py`. The runner
discovers it automatically.

- **Identified binary**: 16 block digests, 76 decoded field witnesses, 49 call targets, 11 TOC
  relocations, 2 relocated jump tables (14 entries), 4 mask decodes, 2 compare shapes, 2 negative
  call witnesses, 1 diagnostic text, 6 schema offsets.
- **PC data**: `Costs.PathCell/QueueCell/MapCell` = 20/75/100 and UIHELPTEXT 441–445, decoded
  with the BFMU/BFST layout of OpenTPW's readers.
- **Tests**: 27 in total. 21 run without assets: rules, decoders, a synthetic field mutation
  check and a synthetic negative-witness check. 5 need the identified binary. 4 of those are
  binary mutations: the PathCell operand 1476, path type 2, hover help 443, and an injected
  Earn call in the path-removal case. 1 needs PC Data.
- **Module mutation check**: 15 single-point mutations of `path_evidence.py`, each killed. A
  no-op control survives. The mutations are:
  - path priced with the queue cost;
  - `≥` → `>` in the money test;
  - the same-type counter;
  - the strict dominant-axis test;
  - the snap tie;
  - a refund on removal;
  - the NOMODIFY lonely-cell clear;
  - the queue-over-path last-cell flag;
  - the unowned refusal;
  - the schema offset;
  - the field operand;
  - the vertex cap;
  - the hover help id;
  - the link rotation;
  - the refund divisor.
