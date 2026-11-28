# Theme Park Inc: the navigator (guest and staff walks)

Status: phases NV1 to NV4 of the navigator port; every Theme Park Inc park walks with it since NV4. Static reading of the identified no-CD `Game.exe`; the game was never run. Addresses are `Game.exe` virtual addresses
unless marked as tp.exe's. Tags: [R] read in the listing, [O] confirmed by the x86 oracle (docs/reverse/ORACLE.md section 7.19).

## What the navigator is

Theme Park Inc's navigator is Theme Park World's (docs/GUESTS.md, "Walking"; ORACLE.md finding X25) with the same code and constants [R]:

- the 0xBC-byte steering object (person `+0xEC`; tp.exe's guest `+0xD4`), its constructor `0x63ED10`, the steering step `0x63DD20`, avoid_walls `0x63C630`,
  follow_path `0x63D060`, separation `0x63C150`, the walk set-up `0x63E3A0`, progress `0x63E8D0`, SetDest `0x63EE80` and the speed setter `0x63EF90`;
- the step commit `0x63F5D0` (tp.exe `0x510510`; TPI drops the asserts, the latch `0xAA0F44` is tp.exe's `0x7CF38C`);
- the route search `0x640C50` (tp.exe `0x511420`): the line search `0x640CC0`, the line set-up `0x63FA50`, the tracer `0x63FB80` (both directions; tp.exe inlines one),
  the head-on test `0x6400A0`, the shortcuts `0x6401C0` / `0x63FE80` and the refinement `0x640320`. TPI passes the result buffer through the global `0xAA0F70`.

OpenTPW's `GuestNavigation`, `WalkSearch` and `WalkEdges` are therefore Theme Park Inc's navigator apart from the differences below. The port proceeded in
phases (NV1 to NV5). NV1 to NV3 sat behind the hidden switch `--tpi-native-walk` (decision N-3), so that Theme Park Inc's determinism pins moved once; **NV4 turned it on
for good and retired it** (docs/DETERMINISM.md, "Theme Park Inc navigator, NV4"):

- A Theme Park Inc level gives its cell map Theme Park Inc's rules (`WalkEdges.RulesForNewPark`, read once by `Level.ApplyParkEdgeRules`), which select Theme Park Inc's edge test, walk
  step and stamp counter; a Theme Park World level keeps Theme Park World's. There is no way back to Theme Park World's walk in a Theme Park Inc park: the walk scope of
  `[EXT:tpi-twp-guest-rules]` (the walk step, the route search, the wander and patrol destination and the speed step of the guests and the staff) is retired.
- **Saves.** The guest snapshot's optional member `ThemeParkIncNativeWalk` records the rules: true in every Theme Park Inc save, absent in a Theme Park World one. A restore refuses a save
  whose value differs from the park's (`GuestSimulation.RestoreSnapshot`, and the resume file's validation), so every Theme Park Inc Science save made before NV4 (the switch off by default) is
  refused (the standing policy for a change of the Theme Park Inc simulation): a walk set up under one edge test cannot continue under the other (a staff member who stepped off the paths with
  mask 1 under Theme Park World's rules may stand on unowned land that `0x5D7D0D` refuses). The layout's `SimulationStampCounter` must agree with it (a counter of simulation calls in a Theme
  Park World park, or an edit-only counter in a Theme Park Inc one, is refused). NV5's schema bump refuses those saves again; the navigator plan prefers one refusal for the player, so NV4
  and NV5 are best released together.

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

## The navigator object (NV2)

Theme Park Inc's navigator is a 0xBC-byte object at person `+0xEC` (tp.exe's guest `+0xD4`). OpenTPW's `GuestNavigation` now holds every member of it, so that one walk object
is the original's navigator for Theme Park World and Theme Park Inc alike:

| nav | Member | Constructor `0x63ED10` | Writer in the simulation | OpenTPW |
| --- | --- | --- | --- | --- |
| `+0x00` | mass | 0x10000 | none (the steering divides by the literal ONE) | `Mass` |
| `+0x04` | radius | 0x3333 | none | `RadiusMember` (the step reads it, as the original does) |
| `+0x08`, `+0x10` | pos, vel | 0 | the steering step | `X`/`Y`, `VelocityX`/`VelocityY` |
| `+0x18`, `+0x1C` | max_force, max_speed | 0x6666, 0x3333 | the speed setter `0x63EF90` (tp.exe `0x510190`, `GuestSpeed.Caps`) | `MaxForce`, `MaxSpeed`, moved in from `Guest` and Theme Park Inc's staff member (`ThemeParkIncStaffMember`), which forward to them; Theme Park World's staff (`StaffWorldSimulation`) compute their own caps for each step and pass them in. A Theme Park World guest starts with `GuestSpeed.Floor` (655) in both where tp.exe's constructor `0x50FFE0` gives 0x6666 and 0x3333: harmless, as the speed step writes them before the walk reads them |
| `+0x20`, `+0x28` | local_xaxis, local_yaxis | 0 | the steering step after the commit: `0x63E150` (unit velocity) and `0x63E300` (perpendicular (−y, x)) | `LocalXAxis*`, `LocalYAxis*` |
| `+0x30` | (unnamed seek target) | 0 | none; the loader zeroes it, only unregistered behaviours read it | not modelled |
| `+0x38`, `+0x40` | formation_pos, force | 0 | none (research note NV0 §1): the file's value passes through | `FormationPos*`, `Force*` |
| `+0x48` | path_timestamp | not written | the set-up: the raised stamp counter | `Stamp` (the native counter in every Theme Park Inc park since NV4; Theme Park World parks keep the edit counter) |
| `+0x64` | subpath_buffer[5] | zeroed by `0x63D820` | the set-up: the buffered cell centres | `Buffer` (cells) and `BufferSlotsWritten` (the slots written; the rest stay zero) |
| `+0x8C` | subpath_dist[5] | not written | the set-up: slot i = the octile segment from waypoint i to i + 1, for i below the count − 1 | `SubpathDist` |
| `+0xB0` | path_stuck_buffer | 0 | the step: shifted left without a mask; follow_path counts bits 0..14 | `History` (the whole word) |

The other members (`+0x4C` .. `+0xB8`) were already there. The behaviour registration `0x63EA90` binds avoid_walls (`0xE666`), follow_path (`0x8000`) and separation (`0x1999`)
in that order, tp.exe's `0x50FDF0`'s list.

What changed in the walk [R, O]:

- **The local axes.** After the commit the steering step stores the normalised velocity as the x axis and its perpendicular as the y axis (`0x63DECF..0x63DF02`, tp.exe
  `0x50F576..0x50F5A9`). `Fixed16.Normalize` ports `0x63E150`: below 2^12 the root of (x² + y²) << 6 and the factor 0x80000 / root as a 32-bit product, below 2^22 the
  components >> 8 and 0x1000000 / root, else >> 18 and 0x4000 / root, by FixMul; a zero velocity gives zero axes. Nothing but the saver reads them.
- **subpath_dist.** The set-up writes the slots of the buffered segments and keeps the rest; follow_path subtracts the passed waypoint's slot from `+0xA4` (`0x63D2F5`,
  tp.exe `0x50EFAC`). On the last buffered waypoint that slot is stale, but the refill's set-up writes `+0xA4` again, so every result is the one the segment lengths gave.
  A walk restored from OpenTPW's own snapshot rebuilds the slots of its buffered waypoints.
- **The history word.** The step shifts `+0xB0` without masking it; only bits 0..14 are read. OpenTPW keeps the whole word; the digest and the snapshots of Theme Park World
  walks keep the 15-bit window they held.
- **The stamp test.** follow_path's comparison of the block stamp with `+0x48` is unsigned (`jbe`, `0x63D0BF`, tp.exe `0x50ED6F`).
- **The square root.** `0x5D2060` (tp.exe `0x4D49A0`, the same code) is `fild`, `fsqrt`, `_ftol`. At the game thread's control word 0x7F (`ThemeParkIncX87`,
  `[EXT:hardware-d3d-path]`) `fsqrt` rounds to a single's 24 bits, so the root of k² − j can come out as k (5000 for 24,999,999; oracle `tpi-exe-navigator-root`). Theme Park
  Inc's walk takes that root (`Fixed16.Root` with `single`) in its lengths, truncations, the ray test and the axes. Theme Park World's walk keeps the exact root, so its results and
  pins do not move in this phase. That root is not confirmed for tp.exe's game thread: X25 ran tp.exe at the harness's default control word 0x37F (64-bit significand), while
  `[EXT:hardware-d3d-path]` models that thread at 0x7F, and the Theme Park World speed setter of the same update already rounds at 24 bits (`Single24.Product`). Reading tp.exe's walk
  at 0x7F is a Theme Park World follow-up (navigator plan, "Follow-ups"), with its own pin update.

**The record.** The serializer `0x63AD30` (tp.exe `0x50D150`) writes 177 bytes, every member but `+0x30` under its name in alphabetical order: force, formation_pos,
local_xaxis, local_yaxis, mass, max_force, max_speed, mCantReachDest, nav_mode, path_buffer_count, path_count (the index), path_finished (1 byte), path_last_progress,
path_stuck_buffer, path_subpath_dist, path_tail_dist, path_target_pos, path_timestamp, path_total_count, path_total_dist, pos, radius, five (subpath_buffer[p], subpath_dist[p])
pairs, vel (mode 1, versions 0 and 1; a short transfer returns 1). The load half (mode 0) reads the same and zeroes `+0x30`. `SaveNavigation` reads it and `Write` writes it;
`GuestNavigation.ToRecord` and `LoadRecord` map it onto the walk (a record whose waypoint slots are not cell centres after the first unwritten one, or whose flags are not 0 or 1,
cannot be held and is refused, as is one with fewer written slots than its buffered count or an index past it). This is the record the native park writer will store; since NV5 it is also the walk OpenTPW's own Theme Park Inc snapshots store (below).

**Unwritten members.** The constructor leaves `+0x48` and the subpath_dist slots as the allocator's arena (`0x7CA070`, no fill) held them, so a person that has never set a walk up
carries indeterminate bytes there; tp.exe's saves show its debug heap's `0xCDCDCDCD` in unused slots. Nothing observable reads them before a set-up writes them (follow_path's
comparison with `+0x48` and its subtraction of a slot come only after a successful set-up, and a stale slot's subtraction is overwritten by the refill), so any value there is a
possible original outcome. OpenTPW starts them at 0, the value of fresh arena memory, and NV5's native writer will emit 0 there.

**In a Theme Park Inc park** (the switch `--tpi-native-walk` of NV2 and NV3, the default since NV4) a walk is native (`GuestNavigation.Native`): its new members join the digest; a guest starts with the constructor's caps
(0x3333, 0x6666) until its first speed step; and the guest and staff snapshots carry the walk's record in the optional member `NativeRecord`, written for a native walk of either
kind: the original keeps one navigator object and saves it whole, so a walk to the walker's own point (`SetDestination`: standing, riding, placed on the grid) keeps the last set-up's
stamp, lengths and slots, which a later failed set-up still reads. A restore takes from it the members the snapshot's own fields do not hold and refuses a record that disagrees
with them, a record in a park without Theme Park Inc's navigator (a guest's in the guests' restore, a staff member's in the resume file's validation), or a guest record whose caps are not the guest's. A Theme Park World park is unchanged: no member joins the digest and no save gains a field.

**Verified.** The x86 oracle (docs/reverse/ORACLE.md section 7.19) runs Game.exe's constructor, set-up and steering step through 40 whole walks and compares the whole record after
every set-up and step (`tpi-exe-walk-steering`), avoid_walls (`tpi-exe-walk-avoid-walls`), the root (`tpi-exe-navigator-root`) and both halves of the serializer
(`tpi-exe-navigator-record`); all agree with `walk_reading.py`. `ThemeParkIncNavigatorTests` holds three Game.exe walks on one map, every member but path_timestamp after each step,
and loads and saves each of their records unchanged; `tools/tpi-layouts/navigator.py` runs the serializer's round trip (T4). X25's steering target compares every member of tp.exe's
navigator as well.

## The walk step, the speed step and the route requests (NV3)

In a Theme Park Inc park the guests (`GuestSimulation.StepThemeParkInc`, so a native guest's `TpiWalk` and a Theme Park World-rule guest's walk alike) and the staff
(`ThemeParkIncStaffWorld.Walk`) take Theme Park Inc's walk step `0x60B460` (through `0x60B440`), whose pure parts are `ThemeParkIncWalkStep` [R, O]:

1. the walk position (`0x63EF10`), the steering step (`0x63EEF0` → `0x63DD20`), the position again;
2. `mX/mY` = position >> 8 with the cell's thing chain (`0x637B70`, tp.exe `0x50B6A0`: OpenTPW's `Occupy` for a guest; a staff member's `FollowChain` at the end of his update, where
   nothing between reads it);
3. the stranded mark `+0x1B0` = 0, on every call (tp.exe `0x4FA30B` clears `+0x198` the same way; OpenTPW's Theme Park World walk does not, a Theme Park World follow-up);
4. 2 when mCantReachDest (`+0x1A4`, the navigator's `+0xB8`) is set;
5. 0 when the progress (`0x63EF70`) is ONE, **after writing the heading** `+0x2C` = `0x91E074`(x0 − x1, y1 − y0) & 0xFFFF (**D1**; tp.exe returns 0 without the write). `0x91E074` is
   tp.exe's `0x6E7074` byte for byte with the same 257-word table (`HeadingAngle.Of`); a step that did not move faces 0;
6. else the same heading, the run flag (**D2**: the bonus word `+0xD6` above the word `0x9CFB20` (0) and either a guest of generation 0 (`+0x2A4`, a kid) or a guard (kind 7); tp.exe runs
   every guest with a bonus; the block `0x60B5A0..0x60B5BA` is dead), the template (walk 1, run 2, a guard carrying someone `0x1F`, D3; asked of the restart test `0x4E4AB0` first), the
   interval `0x5D10F0` and 1. The interval runs at the game thread's 24 bits: the root of the 32-bit squared step and its product with `0x93E5B4` are singles, doubled without the run,
   62.0 / speed rounded to a single, `_ftol`, clamped to 0..0xFA; a step that did not move gives 0, which queues no interval.

tp.exe's interval (`0x4FA3F8` → `0x4D41D0`) is the same code: the root, its product (stored as a float) and the quotient 62.0 / speed handed to `_ftol` without a store, so at the game thread's
control word 0x7F the root and the quotient round to singles there too. OpenTPW's Theme Park World interval (`GuestSimulation.BodyIntervalForStep`) keeps them in double precision, which is
tp.exe at the harness's 0x37F; the interval only paces the body sprite and is in no digest, and it follows the Theme Park World walk's own 0x7F reading (navigator plan, "Follow-ups"), so no
Theme Park World result moves now.

A native guest stores the heading in its record's `Heading` (saved since P1); every walker's presentation vector is the word's (`ThemeParkIncWalkStep.Vector`). A staff member keeps the
word in `SpriteAngle`, saved since NV5 with the person words (below). The guard's carry (`0x5D3B20`: state 0x1C) needs the guard's chase, which is not ported (`[EXT:tpi-capability-gates]`).
The guest states that branch on the step's result (7, 9, 13, 15, 16, 19, 22) see these results; the rigs of `ThemeParkIncNativeGuestTests`, `ThemeParkIncNativeIdleTests`,
`ThemeParkIncNativeQueueTests` and `ThemeParkIncNativeVisitTests` walk with Theme Park Inc's navigator, as every Theme Park Inc park does since NV4.

**The speed step `0x60BC90`** [R, O] is Theme Park World's `0x4FA870` value for value (TPI adds +3.0 × `+0xDC` with `faddp` where tp.exe subtracts −3.0 × it; the same single):
(`+0xD4` + `+0xD6` + `+0xD8`) / `[0x9CFB2C]` (100) + 3 × `+0xDC`, × 0.25, each operation a single; the caps `0x63EF90`; the boost × 99 / 100 as a word; mPreviousX/Y `+0x1A8/+0x1AC` = the
walk position; the sprite commit `0x5D1080` (the pending template through the table `0x9CDA94`, the pending interval; presentation). `GuestSpeed.Update` computes it, and the copy of the
position is OpenTPW's `StepFromX/Y` (guests) and `TurnFromX/Y` (staff), taken at the speed step, which the walk step leaves.

**The route requests** [R, O]. `0x60B8E0`(x word, y word): the stamp counter raised; a counter below the mark `+0x1B0` (unsigned) clears it; a mark still set is tested by `0x60BB50` and,
held, returns 0 with nothing changed; else `+0x2A`, `+0x1B0` = 0, `+0x28` and SetDest `0x63EE80` with the signed words << 8, and the answer is SetDest's. `0x60B7F0`(cell word) is the
same with the words of the cell's centre (sub-cell bytes 0x80). `0x60BB50` takes the person's cell (`+5`/`+7`), or for a queue cell the back of its queue (`0x5E0A50` followed to 0),
and the largest block stamp (`0x5D8260`) of the nine cells around it (the pairs `0xAA0060` set by `0x60BA80`, stopping once above the mark); it holds while that is below the mark.
The stranded mark `0x60B9A0` stores the raised counter for a nonzero argument and 0 otherwise; its only callers (the wander, `0x60A588` and `0x60A72B`) pass 0. OpenTPW's
`RequestRoute`, `RouteToTpiPoint` and `StrandedHolds` are these, tp.exe's `0x4FA530`, `0x4FA5F0` and `0x4FA770` being the same code. **The staff route `0x62EB10`** (`StaffRoute`,
ported in the staff phase) refuses the entrance cells (0x2F, 0x11) and (0x30, 0x11) while the park is closed, sets the walk mask to 1 when the member's cell or the target's is empty
(type 0), else 0, and calls `0x60B8E0`; a staff member's mark is never set. Since NV4 the patrol's final route takes it too (W2, which closes gap 7 of the plan).

**The stamp counter `0x5D81F0`** [R, O]. One process-wide dword (`0xA9D2E8`), raised and returned by each call, never reset and not saved. A park with Theme Park Inc's navigator
(every Theme Park Inc park since NV4; `ParkCellMap.CountsSimulationCalls`) counts every simulation call OpenTPW makes of the routines that call it, in the original's order: `NextStamp` for a
request's or the wander's gate and for a mark, `SetupStamp` for a set-up's success. Nothing else raises it. The calls, by caller:

| Caller | Address | OpenTPW |
| --- | --- | --- |
| the route requests | `0x60B828`, `0x60B8F4` | `RequestRoute`, `RouteToTpiPoint`, `StaffRoute` (`ThemeParkIncPatrol`), the training walk's fallback cell steer (`EnterToTrainingRoom`) |
| the walk set-up's success | `0x63E79B` | `GuestNavigation.Setup`, behind every `Navigate` (a failed set-up keeps its stamp and raises nothing) |
| a guest's wander: entry gate, final route, off-path tries, the stranded mark | `0x609FBE`, `0x60A55F`, `0x60A6FB` / `0x60A83F`, `0x60A4A5` | `GuestSimulation.Wander` (Theme Park World's `0x4F9490` has the same calls at `0x4F94E2`, `0x4F9CB5`, `0x4F9983` / `0x4F9B2E`, `0x4F9DFC`) |
| a staff member's wander and patrol: entry gate, off-path tries, the final staff route | `0x609FBE`, `0x60A6FB` / `0x60A83F`, `0x60A547` → `0x62EB10` | `ThemeParkIncPatrol.Destination` (NV4; the fallback `0x62C720` counts through its staff routes) |
| the cell edits and the queue length change | `0x67CC50`, `0x67CE47`, `0x682FB3`, `0x5E06B3` | `ParkCellMap.SetType`, `StampBlockAt` (already counted) |
| the sprite placement's simulation calls | `0x60CAC9` (guest constructor), `0x61A70B` (the state setter's entry for state 19) | `ThemeParkIncPlacementStamp` after the constructor's walk to the booth, and in state 19's entry for a teleporting ride before its beam (guest phase P3b) |

The guests of the default path (Theme Park World's states under `[EXT:tpi-twp-guest-rules]`, which still covers their decisions, not their walk) count through the same routines, as tp.exe's own
states do at the same places: their route requests (`RequestRoute`, tp.exe `0x4FA530` with its counter call `0x4FA578`; `RouteToPoint` and `RouteToStopPoint`, `0x4FA5F0` at `0x4FA5FE`), their
wander and their set-ups. In a Theme Park World park none of these calls raises the counter (`NextStamp` answers the next value without raising it, `SetupStamp` the counter itself), so its
counter still counts the edits only.

`[EXT:tpi-stamp-counter-sim-only]` (user-approved 2026-11-25, decision N-5): the sprite placement `0x60AE00` also raises the counter once per person per rendered frame (the render pass
`0x64E270` from the frame loops), which makes the original's stored stamps depend on the frame rate; OpenTPW counts only its simulation calls. The advisor's condition scan reaches the
"is stranded" query `0x60BA30` (once per person examined, `0x722A90` → `0x5B7860`); that scan is not ported, so nothing raises the counter there (decision N-2).

**An unsigned dword.** The counter, the block stamps, a walk's `path_timestamp` and a stranded mark are the original's unsigned dwords and are compared unsigned (the route request's `jae` at
`0x60B833`, follow_path's `jbe` at `0x63D0BF`, the stranded test's maximum). OpenTPW keeps their 32 bits in ints that wrap past 2^31 as the dword does, compares them unsigned
(`ParkCellMap.StampBelow`) and saves and loads any 32 bits, so a session whose counter passes 2^31 still saves and loads. The consistency checks of a save (no block stamp and no walk's stamp
above the counter) compare unsigned too; a session that wraps the counter past 2^32 (about 4.3 billion calls) would fail them, which no measured session approaches.

Every comparison of stored stamps is between values of distinct calls (a block stamp against a walk's stamp in follow_path, a block stamp against a mark, a mark against the next
counter value) and the counter only grows, so counting every call changes no outcome: a Theme Park World map keeps counting the edits only (`NextStamp` answers the next value without
raising it), and `ThemeParkIncWalkStepTests.AThemeParkWorldRunIsTheSameUnderBothCountingModes` runs a Theme Park World guest park with path edits and stranded marks under both modes, tick
for tick alike. In a Theme Park Inc park the counter joins the digest even while no block is stamped, and the layout writes it with the optional `SimulationStampCounter` (docs/PARK-SAVES.md),
which a resume refuses in a Theme Park World park (and its absence in a Theme Park Inc one). A Theme Park Inc layout checkpoint (`ThemeParkIncLayoutCheckpoint`, a fresh session) restores the
counter it saved and counts on from it; one written before NV4 holds the edit counter, at or above every block stamp it saved, so counting on from it changes no comparison.
The re-route after a save and after a load (`0x609B55`, decision N-1) raises the counter too; it is NV5's.

**Verified.** `tpi-exe-walk-step`, `tpi-exe-speed-step`, `tpi-exe-route-request` and `tpi-exe-staff-route` (ORACLE.md section 7.19) agree with `walk_reading.py`; the C# meets Game.exe's
walk steps directly (`ThemeParkIncWalkStepTests`), and the navigator tests now compare path_timestamp too.

## The wander and patrol destination (NV4)

`0x609F70` (thiscall person → 1 when a walk is set) is tp.exe's `0x4F9490` with four differences, all of them the staff's [R, O]. Its order:

1. **The gate** (`0x609FBE`): the stamp counter raised; a counter below the mark `+0x1B0` clears it; a mark still set and held (`0x60BB50`) asks for the thought 0x11 (`0x638960`) and
   answers 0 before any draw; else the mark is cleared (`0x60A004`).
2. **The step count** `R % 5` (`0x60A010`, the world generator `0x6493E0`), before anything else draws.
3. **The patrol rectangle.** A staff member (the kinds 4, 5, 6, 8, 7 and, **W1**, the gardener 0x14) whose start cell's word fails the rectangle test `0x62C610` tries the fallback `0x62C720`:
   success answers 1 (without the wander flag `+0xE8`); failure clears the rectangle out of the walk (the flag `[esp+0x2C]`).
4. **The off-path search** for a start cell without links (`0x65CBF0` below 1, `0x60A5EF`): the eight patterns at distances 0 to 3, a path cell inside the map each through the gate (the counter
   raised at `0x60A6FB`), the mark cleared (`0x60B9A0(0)`) and SetDest to its centre; then five tries of `R % 11 − 5` in x and y (`0x60A7A5`, `0x60A7BE`; no type test), each through the gate
   (`0x60A83F`); none answers 0 without a mark. The same for a guest and a staff member: no staff route here.
5. **The steps.** tp.exe's random walk over the cell links (`GuestWander`): every link bit in the order +Y, +X, −Y, −X less the four removals, a counter of them, the first candidate when it
   is below two, else the first from `R & 3` (`0x60A34C`) that is neither empty nor the way back; one more step each time a step ends on the start with the count at 0. Removal 1 is the
   rectangle: a staff member with the flag clear drops each candidate whose word fails `0x62C610`, and the drop that leaves the counter at 0 calls `0x62C720` at once (`0x60A401`).
   A step with no candidate left: a staff member calls `0x62C720` (`0x60A472`), a guest asks for the thought 0x11 and sets the mark to the raised counter (`0x60A4A5`, not through
   `0x60B9A0`) and answers 0.
6. **The point**: two draws `R & 0x7F` clamped to 5..123 (`0x60A3DA`, `0x60A3EA`), the sub-cell bytes of the last cell.
7. **The final route.** A staff member takes the staff route `0x62EB10` to (x << 8 | bx, y << 8 | by) (**W2**: its walk mask from the type-0 test, the closed park's refusal of the entrance cells,
   the counter raised inside `0x60B8E0`); tp.exe's staff take a guest's route. A guest goes through the gate (`0x60A55F`), the mark is cleared and SetDest runs with the words << 8.
   A walk set raises the wander flag `+0xE8` (`0x60A5D2`), which only a guest's state 7 reads.

**The rectangle test `0x62C610`** (**W3**): the word's cell ((w − 1) & 0x7F, (w − 1) >> 7) against the four dwords `+0x24C`, `+0x250`, `+0x254`, `+0x258` (x0, y0, x1, y1), each pair ordered
first, inclusive, signed. tp.exe's `0x506ED0` reads two corner words and orders nothing. The setter `0x62C3C0` stores the two corners as given, so reversed corners occur in the original.

**The fallback `0x62C720`** (**W4**): the corner words of (x0, y0) and (x1, y1) (`0x4A63F0`), width = x1 − x0 + 1 and height = y1 − y0 + 1 from their cells, **not ordered**; up to 30 tries of
x = `R % width` + x0 and y = `R % height` + y0 with **unsigned** divisions (`div`), a cell inside the map (`0x5D7260`, signed) of type 1 and the staff route to its centre; after 30 the thought
0x1E and 0 (tp.exe's `0x506F30` routes to the cell through `0x4FA530` and asks for 0x16). Reversed corners give a negative width, an unsigned divisor of 2^32 less it, which is above every draw
(at most 2^31), so x = R + x0 and nearly every try leaves the map: a member with reversed corners walks into his rectangle only by the walk. OpenTPW ports this as written
(`ThemeParkIncPatrol.Fallback`). Corners one cell apart and reversed make the width or height **zero**, a divide error at `0x62C7CF` or `0x62C7DC`: a crash in the original. No OpenTPW setter writes
such a rectangle (Theme Park Inc's patrol tool is not ported; a member's rectangle is the whole map unless restored), and a restore refuses it (`ThemeParkIncStaffMember.IsValidWorld`).

**OpenTPW.** `ThemeParkIncPatrol.Destination` is the staff's `0x609F70`, with `GuestWander.PickAfterCount` for the steps (the step count drawn before the rectangle, as `0x60A010` does) and
`GuestWander.OffPath` for the off-path search; `GuestSimulation.Wander` is a guest's, the calls and draws of tp.exe's `0x4F9490`. `ThemeParkIncStaffWorld.Patrol` runs the first for every
role's patrol. The thoughts are presentation: a staff member's thought object is not modelled, and a guest's goes through Theme Park World's bubbles (`[EXT:tpi-twp-guest-rules]`, the thought
timing). OpenTPW's candidate walk drops a link that leaves the map or crosses a pool bridge (`[EXT:pools-cell-type]`); no park cell links out of the map.

Draw order: the gate draws nothing; `R % 5`; the fallback's two draws per try (when the start is outside the rectangle); the off-path tries' two draws each, or `R & 3` per step with two or
more candidates; the two byte draws; the fallback's draws again when the walk runs out of candidates. Theme Park World's staff (`StaffWorldSimulation.Patrol`) draw `R % 5` after their
`0x506F30` instead of before it; that is a Theme Park World follow-up (its oracle `tpw-exe-wander-destination` runs guests only), untouched here.

**Verified.** The oracle target `tpi-exe-wander` (ORACLE.md section 7.19) runs `0x609F70` with the world generator, the counter, the stranded test, the mark setter, the rectangle test, the
fallback, the staff route and the route search on 200 persons (guests and every staff kind, the gardener included; held and stale marks, starts without links, rectangles around and away
from the start, reversed, one cell; the park open and closed): all agree with `walk_reading.py`'s `Wanderer`, and tp.exe's rules on the same persons differ on 86. `ThemeParkIncWanderTests` holds
Game.exe's results on the navigator tests' map for 14 wanders (`GuestSimulation.Wander` and `ThemeParkIncPatrol.Destination`: the answer, the generator's state, the counter, the mark, the mask
and the whole record) and for 7 sets of route requests (`RequestRoute`, `RouteToTpiPoint`, `ThemeParkIncPatrol.StaffRoute`), from `tools/ppc-analysis/oracle_x86/tpi_wander_golden.py`.

## The native walk storage and the load semantics (NV5)

**The person block.** The person serializer `0x609170` writes a guest's or a staff member's walk in one 396-byte block, before the guest's (`0x60D284`) or the staff base's (`0x6267E4`) own members:
the thing base `0x637230` (mX, mY, the chain links), the sprite `0x5D0CD0`, mAccurateDestX/Y `+0x28`/`+0x2A`, mAdjustorSpeed, mBaseSpeed, mCount, mESPSprite, mLastRecordedMapId `+0xE4`, the
navigator record (`0x63AD30`, 177 bytes at block offset 35), mPreviousSpeed, mPreviousX/Y `+0x1A8`/`+0x1AC`, mStrandedTime `+0x1B0`, mPurposeSpeed, mSetDestSuccessfully `+0xE8`, mSpriteAngle
`+0x2C`, mSpriteID (the load takes it modulo the sprite count of mESPSprite, `0x6902D0`), mSpriteUnderRideCtrl, mOnRideSoNoFloatingThingsAllowed, mWhingeTimeout and the thought object `0x638490`
(mode 1 `0x6091D1..0x609756`, mode 0 `0x60975B..0x609B30`, versions 0 and 1). `ThemeParkIncPersonRecord` (OpenTPW.Files) reads and writes it; `ThemeParkIncNativeWalk.Write` builds it from a live walker
(`IThemeParkIncWalker`: a native guest or a Theme Park Inc staff member) and `Apply` puts its walk members back.

**The re-route `0x609B55`.** Both halves end with the route request `0x60B8E0` (thunk `0x403DAF`) to the person's own accurate destination: the stamp counter raised (`0x5D81F0`); a counter below the
mark `+0x1B0` clears the mark; a mark still set and held (`0x60BB50`) answers 0; else the destination words are written again, the mark cleared, and SetDest `0x63EE80` runs with the signed words << 8.
Its answer is only logged (`0x4D15A0`, a `ret`). The load half's navigator transfer leaves `+0x30` zero (`0x63B9AB`). So a save changes the live session (the re-route resets the walk's index and
progress and raises the counter: decision N-1, native, no label), and a load sets every walk up again with the counter of the process that loads, which a new process starts at 0 with every
block stamp zeroed (§2.3 of the plan): a guest whose mark is above that counter is released at once, and the re-route counts from 0.

**Every SetDest writes the accurate destination first.** Every Theme Park Inc SetDest (`0x60A5B3`, `0x60A756`, `0x60A891`, `0x60B884`, `0x60B950`) is preceded by the write of `+0x28`/`+0x2A`: the
wander's final guest route (`0x60A597`), the off-path search (`0x60A73A`, `0x60A86F`), the route request to a cell (`0x60B85E`, `0x60B880`) and to a point (`0x60B92A`, `0x60B94C`). OpenTPW keeps the words
on every walker (`Guest.AccurateDestX/Y`, `ThemeParkIncStaffMember.AccurateDestX/Y`) and writes them at those points. The person constructor `0x608F00` does not write them; a person that never routed
re-routes on load to whatever the arena held. OpenTPW starts them at 0, as NV2 does for the navigator's unwritten members (a possible original outcome; the native writer emits 0 there).

**The placement `0x60BD80`.** The handyman's arrival at his litter puts him at his accurate destination: position = the words << 8, velocity 0 (`0x63EE50`), mPrevious = the position (`0x63EF10`), and the
chain update `0x637B70` (`ThemeParkIncStaffWorld.PlaceAtAccurateDestination`). Before NV5 OpenTPW kept his velocity there.

**The wander flag and the facing.** The staff member's `+0xE8` is the guest's: the final route (`0x60A5D2`, through the staff route `0x62EB10` at `0x60A547`) and the off-path search (`0x60A8C1`) set it on
success, the fallback `0x62C720` does not; only a guest's state 7 reads it (`0x61A9C7`), but the original saves it for every person, so `ThemeParkIncStaffMember.Wandered` is saved (and the mark
`+0x1B0` as `StrandedTime`). The gardener's watering turns him with `0x609E60` (from `0x5D2AD7`; the twin `0x5D2D4A` is unreferenced): the queue link byte `+0xD` of his cell (`0x65CC40`) 1 → 0x7D0,
4 → 0x5DC, 0x10 → 0x3E8, 0x40 → 0x1F4, anything else 0x7D0 (`ThemeParkIncStaffWorld.Facing`). The handyman's state 0x14 calls it too (`0x5D5802`); OpenTPW never enters that state.

**OpenTPW's own snapshots.** A Theme Park Inc staff member's world snapshot and a native guest's snapshot (`--tpi-native-guests`) now store the walk as the original does: the member `Walk` /
`PersonWalk` (`ThemeParkIncPersonWalkSnapshot`: the accurate destination words, mPreviousX/Y and the 177-byte `Navigator` record), with the speed words, the heading word, the wander flag and the mark in
the members they already had (a native guest's `Tpi` block, the staff world's `BaseSpeed`, `SpeedBonus`, `Boost`, `SmoothedSpeed`, and the new `SpriteAngle`, `Wandered` and `StrandedTime`). The walk
fields of OpenTPW's own kind are no longer written for them (`Navigation` with its route, cursor, crossings, extra open cells, previous remaining distance, kind, off-grid flag and reroutes; the heading
floats, the step start, the caps, the cell and previous cell, the on-grid flag, the placement offset and the waypoint flag) and are derived on restore (`GuestSnapshot.ExpandNativeWalk`,
`GuestNavigation.RestoreNativeRecord`):

- the walk is the navigator record alone: no route, no crossings or extra open cells, no reroutes. A Theme Park Inc walk never had a route: since NV5 `Navigate` drops the route bookkeeping in a Theme Park
  Inc map, and the walk step reads the subpath buffer, which the record holds;
- the walk's kind: a record with buffered waypoints or a stuck flag is a route walk; a record that never set a walk up (count 0, not stuck) is a stood walk; the kind changes no step of a native walk
  and leaves the digest;
- the heading floats from the heading word (`ThemeParkIncWalkStep.Vector`), the step start from mPreviousX/Y, the caps from the record;
- the cell is the navigator's (`+5`/`+7` are the position >> 8, which the chain update writes with every step); a native guest's own cell field now follows its navigator every update (the walk step
  `0x60B4BB` writes the cell bytes), where it lagged a step before; the previous cell is never read for a native guest, the on-grid flag is always set, the placement offset is the id's jitter (presentation);
- the waypoint flag (presentation: the moving flag reads it) is the walk's own, cleared under an object's control (state 20's entry `0x61A77E` clears it). Since guest phase P3b every native
  guest state is Theme Park Inc's own, the visit included, so no native guest keeps walk fields of Theme Park World's boarding.

`CellSerial` stays in OpenTPW's snapshot until the native writer writes the chain links (the park writer's N6/N7). The original keeps each cell's things as a chain (mMapChild/mMapParent, newest at the
head), and OpenTPW orders each cell's list by the serial its walkers took on entering it: the order of one cell's list is the serials' order, so either can be rebuilt from the other exactly, and the order
of serials across cells is never read. Deriving the serials now would need the links, which no OpenTPW save holds yet; the writer will write the links from the serials' order and the native loader
will number the chains it reads.

The digest of a native guest no longer hashes its heading floats, its moving flag, its previous cell or its waypoint flag (presentation or derived), and hashes its accurate
destination words; a staff member's digest is his park save, which now holds the new members. No pin moves: no pin runs a Theme Park Inc park.

**The load semantics for the park writer.** `ThemeParkIncNativeWalk.Load` is the load half on one walker (the members verbatim, then the re-route with the park's counter and block stamps as they stand),
`LoadAll` the loader's pass (a fresh process: the counter and the block stamps zeroed, then each person in load order), and `SaveAndReRoute` / `ReRouteAfterSave` the save half with N-1's re-route of the
live walker. OpenTPW's own resume file is not the original's save: it restores a walker verbatim and does not re-route (a continuation), and saving it leaves the live session as it was. A block whose
navigator OpenTPW's walk cannot hold, whose cell bytes are not the navigator's position >> 8, or whose wander flag is above 1 (the game stores 0 or 1), is refused.

**Not stored.** mLastRecordedMapId `+0xE4` (written by the constructor with the cell word and by the walk step's effect cell; read only by the effect-cell test, which OpenTPW derives from the current cell)
and the sprite and thought members belong to the park writer's later phases, which carry them through the block.

**Verified.** The oracle targets `tpi-exe-person-record` (both halves of `0x609170` with the re-route stubbed: 500 blocks byte-equal, `walk_reading.person_load`) and `tpi-exe-person-reroute` (the load
half with the real re-route and then the save half: 200 persons, the counter fresh and running, marks held, released and above the counter, routes found and not) agree with `walk_reading.py`
(ORACLE.md section 7.19). The stranded test reads the nine offset pairs at `0xAA0060`, which the static initialiser `0x60BA80` fills; every target on the person world now runs that initialiser (the NV3 and NV4 targets ran on a
zeroed table, which tests the one cell nine times: they still agree with it filled). `ThemeParkIncNativeWalkTests` holds Game.exe's results for 10 blocks on the navigator tests' map (the load's counter,
mark and record, the bytes the save wrote, and the save's re-route), from `tools/ppc-analysis/oracle_x86/tpi_person_golden.py`; checks that a fresh-process load starts the counter at 0; reads and writes
every block unchanged (T4); continues native guests through five saves of OpenTPW's resume, each restoring the same bytes and the same digest and going on alike (T3);
and loads a played park's person blocks onto blanked walkers with the original's semantics and gets the park the blocks give on the restored ones, turn by turn (T2). `ThemeParkIncHandymanTests` does the same
for a native handyman. T1 (the prebuilt's persons byte-equal after import and export) is vacuous on the Science prebuilt: no prebuilt holds a person; the test checks that and round-trips a person appended
to the prebuilt through the thing-body reader.

## Random numbers

No world draw and no C runtime `rand()` (`0x870D38`) is reached from the edge test, the commit, the search or the neighbour query [R], nor from the constructor, the set-up, the
steering step, the speed setter, the navigator serializer or the person serializer with its re-route [R, O: the oracle allows none of them], nor from the walk step, the speed step, the route requests, the stranded test or the staff
route [R, O: the NV3 targets allow neither routine]. The wander and the fallback draw the world generator `0x6493E0` only (the draws above) and never `rand()` [R, O: `tpi-exe-wander` runs
the generator and allows no `rand()`].

## Later phases

- The park writer (its plan's N2 onwards) calls `ThemeParkIncNativeWalk.LoadAll` and `SaveAndReRoute` and writes the chain links (N6/N7), after which `CellSerial` can be derived.
