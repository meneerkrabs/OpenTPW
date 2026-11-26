# Theme Park Inc: the navigator (guest and staff walks)

Status: phases NV1, NV2 and NV3 of the navigator port. Static reading of the identified no-CD `Game.exe`; the game was never run. Addresses are `Game.exe` virtual addresses
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
  sets `ParkCellMap.EdgeRules` (`Level.ApplyParkEdgeRules`), which selects Theme Park Inc's edge test, walk step and stamp counter. Without it the Theme Park Inc walkers keep Theme Park World's rules under `[EXT:tpi-twp-guest-rules]`, as before; Theme Park World
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
| `+0x48` | path_timestamp | not written | the set-up: the raised stamp counter | `Stamp` (the native counter under the switch since NV3, else the edit counter) |
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
cannot be held and is refused, as is one with fewer written slots than its buffered count or an index past it). This is the record the native park writer will store; OpenTPW's own snapshots keep their format until NV5.

**Unwritten members.** The constructor leaves `+0x48` and the subpath_dist slots as the allocator's arena (`0x7CA070`, no fill) held them, so a person that has never set a walk up
carries indeterminate bytes there; tp.exe's saves show its debug heap's `0xCDCDCDCD` in unused slots. Nothing observable reads them before a set-up writes them (follow_path's
comparison with `+0x48` and its subtraction of a slot come only after a successful set-up, and a stale slot's subtraction is overwritten by the refill), so any value there is a
possible original outcome. OpenTPW starts them at 0, the value of fresh arena memory, and NV5's native writer will emit 0 there.

**Under the switch.** With `--tpi-native-walk` a Theme Park Inc walk is native (`GuestNavigation.Native`): its new members join the digest; a guest starts with the constructor's caps
(0x3333, 0x6666) until its first speed step; and the guest and staff snapshots carry the walk's record in the optional member `NativeRecord`, written for a native walk of either
kind: the original keeps one navigator object and saves it whole, so a walk to the walker's own point (`SetDestination`: standing, riding, placed on the grid) keeps the last set-up's
stamp, lengths and slots, which a later failed set-up still reads. A restore takes from it the members the snapshot's own fields do not hold and refuses a record that disagrees
with them, a record without the switch (a guest's in the guests' restore, a staff member's in the resume file's validation), or a guest record whose caps are not the guest's. Without the switch nothing changes: no member joins the digest, no save gains a field, and the default walk is as before.

**Verified.** The x86 oracle (docs/reverse/ORACLE.md section 7.19) runs Game.exe's constructor, set-up and steering step through 40 whole walks and compares the whole record after
every set-up and step (`tpi-exe-walk-steering`), avoid_walls (`tpi-exe-walk-avoid-walls`), the root (`tpi-exe-navigator-root`) and both halves of the serializer
(`tpi-exe-navigator-record`); all agree with `walk_reading.py`. `ThemeParkIncNavigatorTests` holds three Game.exe walks on one map, every member but path_timestamp after each step,
and loads and saves each of their records unchanged; `tools/tpi-layouts/navigator.py` runs the serializer's round trip (T4). X25's steering target compares every member of tp.exe's
navigator as well.

## The walk step, the speed step and the route requests (NV3)

Under the switch the guests (`GuestSimulation.StepThemeParkInc`, so a native guest's `TpiWalk` and a Theme Park World-rule guest's walk in a Theme Park Inc park alike) and the staff
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

A native guest stores the heading in its record's `Heading` (saved since P1); every walker's presentation vector is the word's (`ThemeParkIncWalkStep.Vector`). A staff member keeps the
word in `SpriteAngle`, not saved yet (NV5 stores the person words). The guard's carry (`0x5D3B20`: state 0x1C) needs the guard's chase, which is not ported (`[EXT:tpi-capability-gates]`).
The guest states that branch on the step's result (7, 9, 13, 15, 16, 19, 22) see these results; the native-guest tests pass with both switches on
(`OPENTPW_TEST_TPI_NATIVE_WALK=1` runs every rig of `ThemeParkIncNativeGuestTests` and `ThemeParkIncNativeIdleTests` that way).

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
(type 0), else 0, and calls `0x60B8E0`; a staff member's mark is never set. The patrol still bypasses it (gap 7 of the plan): the staff's wander is NV4.

**The stamp counter `0x5D81F0`** [R, O]. One process-wide dword (`0xA9D2E8`), raised and returned by each call, never reset and not saved. A park with Theme Park Inc's navigator
counts every simulation call of its native routes (`ParkCellMap.CountsSimulationCalls`; `NextStamp` for a gate or a mark, `SetupStamp` for a set-up's success), in the original's order. Theme Park World-rule walkers in such a park (`[EXT:tpi-twp-guest-rules]`, until NV4) route through Theme Park World's requests, which raise nothing:

| Caller | Address | OpenTPW |
| --- | --- | --- |
| the route requests | `0x60B828`, `0x60B8F4` | `RequestRoute`, `RouteToTpiPoint`, `StaffRoute`, the training walk's fallback cell steer (`EnterToTrainingRoom`) |
| the walk set-up's success | `0x63E79B` | `GuestNavigation.Setup` (a failed set-up keeps its stamp and raises nothing) |
| the wander: entry gate, a guest's final route, the off-path tries, the stranded mark | `0x609FBE`, `0x60A55F`, `0x60A6FB` / `0x60A83F`, `0x60A4A5` | `Wander` (the guests'; Theme Park World's `0x4F9490` has the same calls) |
| the cell edits and the queue length change | `0x67CC50`, `0x67CE47`, `0x682FB3`, `0x5E06B3` | `ParkCellMap.SetType`, `StampBlockAt` (already counted) |
| the sprite placement's simulation calls | `0x60CAC9` (guest constructor), `0x61A70B` (the state setter's entry for state 19) | `ThemeParkIncPlacementStamp` after the constructor's walk to the booth; state 19 is a later guest phase's |

`[EXT:tpi-stamp-counter-sim-only]` (user-approved 2026-11-25, decision N-5): the sprite placement `0x60AE00` also raises the counter once per person per rendered frame (the render pass
`0x64E270` from the frame loops), which makes the original's stored stamps depend on the frame rate; OpenTPW counts only its simulation calls. The advisor's condition scan reaches the
"is stranded" query `0x60BA30` (once per person examined, `0x722A90` → `0x5B7860`); that scan is not ported, so nothing raises the counter there (decision N-2). The staff's patrol
wander comes with NV4, so under the switch the counter counts none of its calls until then.

Every comparison of stored stamps is between values of distinct calls (a block stamp against a walk's stamp in follow_path, a block stamp against a mark, a mark against the next
counter value) and the counter only grows, so counting every call changes no outcome: a Theme Park World map keeps counting the edits only (`NextStamp` answers the next value without
raising it), and `ThemeParkIncWalkStepTests.AThemeParkWorldRunIsTheSameUnderBothCountingModes` runs a Theme Park World guest park with path edits and stranded marks under both modes, tick
for tick alike. With the switch the counter joins the digest even while no block is stamped, and the layout writes it with the optional `SimulationStampCounter` (docs/PARK-SAVES.md).
The re-route after a save and after a load (`0x609B55`, decision N-1) raises the counter too; it is NV5's.

**Verified.** `tpi-exe-walk-step`, `tpi-exe-speed-step`, `tpi-exe-route-request` and `tpi-exe-staff-route` (ORACLE.md section 7.19) agree with `walk_reading.py`; the C# meets Game.exe's
walk steps directly (`ThemeParkIncWalkStepTests`), and the navigator tests now compare path_timestamp too.

## Random numbers

No world draw and no C runtime `rand()` (`0x870D38`) is reached from the edge test, the commit, the search or the neighbour query [R], nor from the constructor, the set-up, the
steering step, the speed setter or the serializer [R, O: the oracle allows none of them], nor from the walk step, the speed step, the route requests, the stranded test or the staff
route [R, O: the NV3 targets allow neither routine].

## Later phases

- NV4: the wander and patrol destination `0x609F70`; the switch turns on and the walk scope of `[EXT:tpi-twp-guest-rules]` retires.
- NV5: the native walk storage and load semantics.
