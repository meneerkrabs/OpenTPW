# Theme Park Inc: the navigator (guest and staff walks)

Status: phase NV1 of the navigator port. Static reading of the identified no-CD `Game.exe`; the game was never run. Addresses are `Game.exe` virtual addresses
unless marked as tp.exe's. Tags: [R] read in the listing, [O] confirmed by the x86 oracle (docs/reverse/ORACLE.md section 7.19).

## What the navigator is

Theme Park Inc's navigator is Theme Park World's (docs/GUESTS.md, "Walking"; ORACLE.md finding X25) with the same code and constants [R]:

- the 0xBC-byte steering object (person `+0xEC`; tp.exe's guest `+0xD4`), its constructor `0x63ED10`, the steering step `0x63DD20`, avoid_walls `0x63C630`,
  follow_path `0x63D060`, separation `0x63C150`, the walk set-up `0x63E3A0`, progress `0x63E8D0`, SetDest `0x63EE80` and the speed setter `0x63EF90`;
- the step commit `0x63F5D0` (tp.exe `0x510510`; TPI drops the asserts, the latch `0xAA0F44` is tp.exe's `0x7CF38C`);
- the route search `0x640C50` (tp.exe `0x511420`): the line search `0x640CC0`, the line set-up `0x63FA50`, the tracer `0x63FB80` (both directions; tp.exe inlines one),
  the head-on test `0x6400A0`, the shortcuts `0x6401C0` / `0x63FE80` and the refinement `0x640320`. TPI passes the result buffer through the global `0xAA0F70`.

OpenTPW's `GuestNavigation`, `WalkSearch` and `WalkEdges` are therefore Theme Park Inc's navigator apart from the differences below. The port proceeds in
phases (NV1 to NV5) behind one hidden switch, so that Theme Park Inc's determinism pins move once, when the last phase turns it on:

- `--tpi-native-walk` (`GuestSimulation.ThemeParkIncNativeWalkRequested`), off by default. A Theme Park Inc level reads it once when it builds its cell map and
  sets `ParkCellMap.EdgeRules`. Without it the Theme Park Inc walkers keep Theme Park World's rules under `[EXT:tpi-twp-guest-rules]`, as before; Theme Park World
  parks never read it. A Science save records it (the guest snapshot's optional member `ThemeParkIncNativeWalk`, written only when the switch is on) and a save
  whose setting differs from the running game's is refused in both directions: a walk set up under one edge test cannot continue under the other (a staff member
  who stepped off the paths with mask 1 under Theme Park World's rules may stand on unowned land that `0x5D7D0D` refuses).

## The edge test `0x5D7B60` (NV1)

`cdecl (x byte, y byte, direction byte, mask dword)`, 1 = blocked. The cell record is 72 bytes at `[[0xAA131C]] + 0x418 + 72 × id`, id = y × 128 + x + 1
(tp.exe: 68 bytes from `+0x294`). Directions 0 −Y, 1 +X, 2 +Y, 3 −X (jump table `0x5D7F1C`). The fields it reads are tp.exe's plus the dword `+0x24`, the obstacle
cell flag (`ParkCellMap.ObstacleFlagAt`) [R].

The rules in the order the routine tests them [R, O]:

| # | Address | Rule | tp.exe `0x4D8750` |
| --- | --- | --- | --- |
| R0 | `0x5D7B6C` | the map's edge (x 0 with direction 3, y 0 with 0, x 127 with 1, y 127 with 2) → blocked | same |
| R1 | `0x5D7C0E` | a path target from a cell that is no queue → open | same |
| R2, R3 | `0x5D7C29`, `0x5D7C46` | type 4 to type 4 → open; any other type 4 target → blocked | same |
| R4 | `0x5D7C55` | a walkway target (`0x1E`) **without the obstacle flag** (`0x67F6F0`: `+0x24 == 0`), mask not 2 → open | any `0x1E` target |
| R5 | `0x5D7C70` | mask 3: a `0x1E` or `0x20` target → open (`0x67F750`), else on to R7 | absent |
| R6 | `0x5D7C8B` | mask 2: the target's flag `0x400` **and no obstacle flag** (`0x67F720`) → open; a type 9 target: the first class-3 thing on the cell named by the **from** cell's `+0x10` decides through `0x4666E0` | flag `0x400` only; the target's owner cell, `0x42A440` |
| R7 | `0x5D7D0D` | an **unowned** target (flag word `+0xE` bit `0x40`, `0x67ECA0`) → blocked | absent |
| R8 | `0x5D7D1E` | a target of type 5, 7, 2 **or 8** (`0x67F780`) → blocked | 5, 7, 2 |
| R9 | `0x5D7D2D` | a target of type 2 (`0x67F590`) → blocked; R8 already refused it | the same test at `0x4D8914` (`0x536300`) |
| R10, R11 | `0x5D7D3C`, `0x5D7D97` | queue boundaries read the target's link back | same |
| R12 | `0x5D7DFA` | a target structure record of a track kind whose flag nibble is 0 → blocked | same |
| R13 | `0x5D7E4D` | from a cell that is neither path nor queue → open | same |
| R14 | `0x5D7E68` | mask 1, from a path to a cell that is no path, queue or type 4 → open | same |
| — | — | mask 2, from a path to type 0 → open | `0x4D8A6D` (TPI has no such rule) |
| R15 | `0x5D7E9E` | from type 9 or 10 into type 0 along the queue link `+0xD` → open; else blocked | same |

For a person this means:

- **The obstacle's walkway** (E1). A `0x1E` cell of an obstacle's footprint loses the walkway fast path. Its type blocks nothing, so it is open from a cell that is
  neither path nor queue (R13) and from a path with mask 1 (R14), and blocked from a path with mask 0 and from a queue (R15).
- **Unowned land** (E5). R7 comes after R1 to R6: an unowned path is open from any cell that is no queue, and an unowned walkway without the obstacle flag is open.
  Every other unowned target is blocked: a queue, a path entered from a queue, ground under mask 1, an entrance or exit approach, an obstacle's walkway.
  OpenTPW's `ParkCellFlags.Unowned` is the same bit, set by Theme Park Inc's land (`ThemeParkIncLand`).
- **Terrain holes** (E6). Type 8 blocks. It is the obstacle grids' `'H'` symbol (`0x98DF88`, `ThemeParkIncObstacleTypes`), written by the obstacle placement.

**Masks 2 and 3 are unreachable in Theme Park Inc** (research note NV0 §3) [R]. Every writer of the walk's mask (person `+0x1A0`, the navigator's `+0xB4`) stores 0 or
1: the route requests and states at `0x5D4291`, `0x6146AC`, `0x614C63`, `0x615C9D`, `0x615ED2`, `0x615F4F`, `0x6186D8`, `0x628480`, `0x62850F`, `0x62BBE9`, `0x62EB70`,
`0x62EBBC` (1) and `0x5D4688`, `0x618667`, `0x62854D`, `0x628872`, `0x62A03D`, `0x62A494`, `0x62EBD9` (0), the constructor `0x63EDCF` (0) and the loader (the file's
value). Every caller of the edge test passes the walk's mask, the search's (the same), the literal 0 (the queue code `0x5E0998`), or the camera's 0 then 1 (`0x468A9E`,
`0x468CBD`, `0x468AAF`, `0x468CCE`; tp.exe's camera passes 2). So R5, R6's TPI form and the missing mask-2 rule never decide a Theme Park Inc walk, and OpenTPW does not
port them: `WalkEdges.Blocked` keeps tp.exe's mask-2 branch. Its only OpenTPW caller with mask 2, the free walk (`Level.FreeWalk`, tp.exe's camera walk), runs in
Theme Park World parks alone (`EnterFreeWalk` refuses every other edition), so no walk with Theme Park Inc's rules reaches it. Theme Park Inc's own camera walk
(`0x468A9E`, masks 0 then 1) is not ported.

**OpenTPW.** `WalkEdgeRules.ThemeParkInc` on the park's `ParkCellMap` selects R4's obstacle test, R7 and R8's type 8 inside `WalkEdges.Blocked`; one field read per
test, no delegate and no allocation, so the search's inner loop stays as it was for the browser's AOT build. The commit and the search call the same test, so they
follow. With `WalkEdgeRules.ThemeParkWorld` (every Theme Park World park and the default) the test is tp.exe's, bit for bit.

**Verified.** The dev-only x86 oracle runs `0x5D7B60`, `0x63F5D0` and `0x640C50` on synthetic park maps with holes, obstacle walkways and unowned land, masks 0 and 1
(`tpi-exe-walk-edge-test`, `tpi-exe-walk-commit`, `tpi-exe-walk-search`, ORACLE.md section 7.19): all agree on every input (503, 503, 500) with `walk_reading.py`, the Python statement of the port, and tp.exe's rules on the same maps differ on 130, 107 and 90.
The maps also carry second-layer structure records (R12) and set the land flag `0xB1A94C` in half of them. The C# meets Game.exe directly in
`ThemeParkIncWalkEdgeTests`, which holds Game.exe's results on one map for every edge, masks 0 and 1, and six searches (its Theme Park World strings come from
the Python reading of tp.exe, which X25 verified, not from a run of tp.exe).

## Separation and the gardener (NV1)

Separation's neighbour query `0x63F1E0` [R] scans the cells around the walker (the in-bounds test `0x5D7260`), follows each cell's thing chain (`+0x28`, next `+0xA`),
takes the kinds 1, 4, 5, 6, 8, 7, **0x14** and 0x12 (`0x63F2AD..0x63F2CB`), skips the caller and things at zero distance, and calls back at most five times. tp.exe's
`0x5102F0` is the same without 0x14, the gardener.

OpenTPW needs no new code for it: `ThemeParkIncStaffWorld` files every placed member on the staff chain (`GuestSimulation.StaffChain`), gardeners included, and
`GuestSimulation.AppendCellChain` reads that chain without a kind test. `ThemeParkIncGardenerTests.AGardenerIsASeparationNeighbourOfTheGuestsAndTheStaff` pins it.

## Random numbers

No world draw and no C runtime `rand()` (`0x870D38`) is reached from the edge test, the commit, the search or the neighbour query [R].

## Later phases

- NV2: the navigator's remaining members (mass, radius, the local axes, `formation_pos`, `force`, `subpath_dist[5]`, the speed caps) and its 177-byte serializer
  `0x63AD30`.
- NV3: the walk step `0x60B460` (the heading on arrival, only kids run), the speed step `0x60BC90`, the route requests `0x60B7F0` / `0x60B8E0` and the stamp counter
  `0x5D81F0`, which counts simulation calls only (the user-approved label `tpi-stamp-counter-sim-only`, 2026-11-25).
- NV4: the wander and patrol destination `0x609F70`; the switch turns on and the walk scope of `[EXT:tpi-twp-guest-rules]` retires.
- NV5: the native walk storage and load semantics.
