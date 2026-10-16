# Kart and water ride tracks

Phase 1 of the original kart and water tracks was **one water ride, Splish Splash on the Jungle level, end to end** (the data tables, the track
tool, the circuit walk, the section geometry, the boat physics and the script bridge). Phase 2 adds **the go-karts in all four themes**, run end to end
for the Jungle's Dino Karts (see "Go-karts" below); they share the piece tables, the templates, the tool and the controller with the water rides. Rules are traced from the Mac container (`STP-PPC`) and the Windows `tp.exe` (`TPW-EXE`, through the
shared REA session over the decrypted executable); every approximation carries a `TRACK-` ID in
[the register](https://opentpw.io/docs/FIDELITY-REGISTER.html).

Code: `source/OpenTPW/World/Tracks/`, the track layer in `ParkCellMap`, the tool mode in `CellBuildTool`, `Level.Tracks.cs`, the `BUMP` bridge in
`OriginalObjectEffects`, `HmpFile` in `OpenTPW.Files`.

## What works

- Placing Splish Splash writes its two station tiles; the water track tool starts at the tile after the station. A line is two clicks, snapped
  to the dominant axis in two-cell tile steps, charged `Costs.WaterTrackCell` per tile, clearing the path cells under a tile, connecting
  consecutive tiles, marking bridges over water and paths, then the hump/ramp pass.
- Every edit walks the circuit again (`TrackCircuitWalker`): a loop back to the station is valid, the attraction's validity flag (the PC's
  `mIsTrackRideValid` word at +0x2C, which the park-points counts-test reads) is written, segment numbers and flow links are stored, pieces are
  chosen, and the controller rebuilds its sections, collision objects and buoys. A valid circuit ends the tool and starts the queue tool.
- The ride's own `Wateride.RSE` drives a `BumpController`: it waits for a valid track (`BUMP 5`), opens (`BUMP 7`), takes guests (`BUMP 1`),
  launches boats (`BUMP 4`) and hands guests back (`BUMP 2`). Boats run the traced integer physics around the loop and unload at the station.
- Pieces are drawn from the ride's `wr_trck*.md2` meshes, boats from the supplemental mesh `wr_ring.md2`.
- The cells, the controller and its boats are saved in the park-resume envelope (optional `Tracks` part) and restored exactly.

Not in phases 1 and 2: jumps and tunnels (add-ons); the three other themes' water rides were checked for data only (the
tables and kits load for all four; only the Jungle ride was run). The pieces' arm masks and their water clips came later ("Piece animation and arm masks").

## Cells

A track tile is 2 × 2 map cells (a mesh is 20 world units wide). The origin cell carries the tool's type, **11 kart** or **16 water**; the other
three cells are **type 12** and record the origin cell. The station tiles use **13 kart / 18 water** (presumed from the types the walk accepts,
low confidence); 17 has no known writer; the walk accepts 11, 13, 16, 18 and 25. `ParkCellMap.Tracks` (`TrackCellLayer`) mirrors the original's
40-byte record at game +0x1102D8:

| Field | Offset | Meaning |
| --- | --- | --- |
| type | +8 | cell type (above); the map's own type byte stays in sync, so guests and the object rules see an occupied cell |
| mask | +0xC | connections: 0x01 −Y, 0x04 +X, 0x10 +Y, 0x40 −X |
| link | +0xD | flow heading bit: the line's drawing direction at lay time (ORed on a tile with more than two arms), rewritten by the walk at corners |
| flags | +0xE | bits 0–3 the bridge state, 0x10 add-on (TRACK-008); OpenTPW keeps 0x20 for "replaced path cells" (TRACK-010) |
| parent | +0x10 | origin cell index in an extension cell |
| family/piece/rotation | +0x14/+0x18/+0x1C | the chosen piece |
| overlap | +0x20 | how often the same tile was written again (free of charge) |
| alt mask | +0x22 | preview mask (unused) |
| segment | +0x24 | the walk's segment number |

The layer joins `ParkCellMap`'s canonical digest only while a track exists; a trackless park keeps its pins.

## Piece selection (Q1)

`TrackPieceSelector`, `[BIN:STP-PPC:0x1008A7C4]`. A table per family maps the mask to piece and rotation: exact match first, else the first entry whose
mask is a subset. The 16 masks: 0x11/0x44 straight 0°/90°; 0x10/0x40/0x01/0x04 end stub 0/90/180/270; 0x14/0x50/0x41/0x05 bend 0/90/180/270;
0x15/0x45/0x51/0x54 tee pieces 5–8; 0x55 crossing; 0x00 none. Rotations turn counter-clockwise in the (x, y) plane.

Adjustments, in order: (1) a straight with link 0x40 or 0x10 turns +180°; (2) pieces that are neither bends nor junctions become bridge pieces: flag
0x1 adds 9, 0x2 adds 12, 0x8 adds 15, 0x4 selects 18; (3) types 11/16: a bend with (link, mask) in {(0x40,0x50), (0x10,0x14), (0x01,0x41),
(0x04,0x05)} adds 16 (B → B2); (4) type 16 crossings: link 0x14 sets 90°, 0x41 adds 16, 0x50 sets 270° and adds 16 (X2, pieces 20–24). Types 4, 5,
7, 9, 10, 12, 17 and the station tiles draw nothing.

Bridges: a tile with at most two arms and no corner (a straight, a single-arm end or an armless tile) over the paths it cleared (TRACK-010) gets bit 1. The PC original (`[TPW-EXE:0x538A92]`) sets the bridge word on a kart or water
tile with at most two arms and no corner whose four map cells hold a path or queue, which stay in the map layer under the track; water is no bridge
(inside the park water cells are map type 0), and only type 25 tests map types 2, 7 and 30, which exist only outside the park;
the second pass (`[STP-PPC:0x1007A354]`) turns it into H (neither neighbour bridged), H_U (only ahead), H_D (only behind) or H_A (both), ahead
and behind following the flow link. A park-resume load runs the bridge pass over every tile (`TrackSave.RestoreCells`), so a save written while
straights over terrain water still became bridges loads with plain pieces there.

Piece tables (12 bytes: handle, name, arm byte; PC kart `0x7633E8`, water `0x7634D8`): kart 20 entries, water 25: 0 none; 1 Q; 2 S; 3 B; 4 X; 5–8 X with arm masks 0x15/0x54/0x51/0x45; 9–11 H;
12–14 H_U; 15–17 H_D; 18 H_A; 19 B2; 20–24 X2 (water only). `TrackPieceKit` loads `wr_trck<stem>.md2` / `.hmp` (water) or `gk_trck…` (kart) from the
ride's archive; all four water and all four kart archives load completely.

### Height profiles (`HmpFile`)

48-byte header: u16 version 3, u16 magic 0xAB1E, u16 samples per cell 5, u16 100, u16 width, u16 height, u32 body offset 48, u32 maxima offset, u32 flags
offset, six floats (the bounding box, at most 20). Body: (5w) × (5h) bytes of profile (about 0.4 world units per step), then w × h per-cell maxima and
w × h zero flags. A piece profile is 2 × 2 cells (156 bytes), the station's 4 × 4 (480 bytes). The reader is bounded (1 MiB), strict about the
offsets and the length, and rejects malformed files. The profile is the original's per-object height map (`[TPW-EXE:0x451640]`, called from the mesh loader
`0x4629D0` after swapping the extension, checks magic 0xAB1E0003 and (5, 100), and rebuilds the map from the mesh with `0x451880` when the file is missing or the size
differs). The vehicles' sampler never reads it (`0x4511A0` samples faces or the terrain, `0x4527F0` is the landscape's height, see "Vehicle height" below), so OpenTPW's
vehicles do not either; the kit still parses it (TRACK-002). The `.hmp` is read by the cell-height lookup `0x452AE0`, whose callers are the build ghosts and the tour code.

## The tool (`TrackLineWriter`, `TrackBuilder`)

`CellToolMode.WaterTrack = 16` and `KartTrack = 11`: the mode is the cell type the line writes. A click snaps to one axis (X only when |dx| > |dy|, a tie goes to Y)
and to whole two-cell steps: the end is `start + 2 · floor((cursor − start) / 2)`, so an odd distance rounds toward the start in the + direction and away from it in the −
direction (`[TPW-EXE:0x53AAC0]`, instruction level 0x53AAC0..0x53AB21; TRACK-009 retired October 14). The path tool's own snap (a tie keeps X) is a different routine. Each tile validates its four cells in the path
tool's order (bounds, owned land, objects, terrain — water is allowed for water tracks, queues refuse), charges `Costs.WaterTrackCell` once when
written (existing tiles are free and bump their overlap counter) and clears path cells (`[STP-PPC:0x10088CB8..0x10088DEC]`). A new tile takes
the arm toward any neighbour whose arm already points at it (the station's open ends, an earlier line's end); consecutive tiles of a line connect.
Removing a tile clears its neighbours' arms; the station tiles never lose theirs. A line ending on an existing tile ends the tool, like the path tool.

The level reaches the tool through placing the ride (`ParkHud.OnObjectPlaced` → `Level.EnterTrackTool`); when the circuit becomes valid the tool
closes and the queue tool starts. `Level.BuildTrack` is the headless entry.

### The station (TRACK-005)

The 4 × 4 footprint holds two station tiles along the heading, on the rows beside the entrance (`TrackStation`). The circuit starts at the first one.
The original's station is the line of `0x52A1A0` (`[TPW-EXE:0x52A1A0]`: attraction, &exitX, &exitY, kind; kind 1 writes cell type 13 for karts, 2 type 18 for water). From the
attraction cell (x, y) it runs the line routine `0x53AAC0` over the station segment, stores the segment start as the walk's start cell (`DAT_007639E0/E4`) and returns the exit
cell:

| rotation | station line (2 tiles) | walk start | exit | heading bit |
| --- | --- | --- | --- | --- |
| 0° | (x, y+1) → (x+2, y+1) | (x, y+1) | (x+4, y+1) | 0x04 +X |
| 90° | (x+1, y) → (x+1, y−3) | (x+1, y) | (x+1, y−5) | 0x01 −Y |
| 180° | (x−1, y−2) → (x−3, y−2) | (x−1, y−2) | (x−5, y−2) | 0x40 −X |
| 270° | (x−2, y) → (x−2, y+2) | (x−2, y) | (x−2, y+4) | 0x10 +Y |

The exit offsets equal the fixed offsets `0x529E10` adds to the controller reference (below). OpenTPW places its station on the footprint's first two tile columns.
The placement's cursor cell is OpenTPW's anchor: `0x818C3C` = (anchorY << 7) + anchorX + 1 is written at `0x528E12` from the build click's anchor (`0x528B44`,
`Level.GetCentredAnchor`) right before `0x529E10` is called (`0x528E19`). The attraction `0x52A1A0` reads (the object `0x528A70` returns) is constructed at that cell
plus the per-rotation offset `0x4D9CC0` returns from type +0x4B0 (`0x529F86`); that offset, whether the attraction's +8/+0xC is the constructed cell, and the line
modes 0x80, 0x82 and 0x83 are not traced (TRACK-005). The heading
follows the rotation (0° +X, 90° −Y, 180° −X, 270° +Y). The tile before the first station tile (`Entry`) is where the player's loop returns; the tile after
the second (`Exit`) is where the first line starts.

## The circuit walk (Q3)

`TrackCircuitWalker`, `[TPW-EXE:0x52A970]`. From the start tile the walk follows the masks two cells at a time. A cell that is not a track tile, has fewer than
two arms or no heading fails the walk; a corner (two perpendicular arms) takes the new heading `mask ^ opposite(heading)` (the heading itself when the opposite
arm is missing) and rewrites the flow link `+0xD`; every other cell keeps the heading without testing its arms. A three-arm cell fails when the cell two ahead
has type 0 or is a track cell without arms while the heading has bit 0x01 (a literal bit test, `0x52B234..0x52B280`); a four-arm cell has no rule. The walk
writes the 0-based step to `+0x24` and has no step limit in the original (OpenTPW cuts it off after as many steps as the layer has tiles plus the station, `[EXT:track-walk-step-limit]`; the readers of the step number are the track tool's edit sub-modes, TRACK-014). The cells
are written in both of the original's modes. The flow link of a straight or junction is written at lay time by the line op (`0x539670`, op `0x82` of
`0x53AAC0`): a tile with at most two arms takes the line's direction, a tile with more arms ORs it in (a crossing of two lines gets a two-bit link such as `0x14`, TRACK-004), and the
station tiles take the heading of the rotation (`0x524F1E`), so a straight drawn against the flow keeps its drawing direction. The circuit is
valid when the walk arrives back at the start tile; no minimum length. The result is the attraction's validity flag, which `BUMP 5` returns and which the
park points counts-test and guest boarding read. A re-walk (commit mode) also resets the controller: boats leave, the ride closes
(`[TPW-EXE:0x545450]`).

Messages: the walk posts no advisor or event message (`APPROX:TRACK-006` retired: `0x52A970` and the walk sites of the tool handler `0x524960` call no message builder `0x59B590`).
The ids once listed for it are tool help of the cell build tool: 0xCC (204) is posted by the kart and water tool for a click with no line start (`0x526D41`),
which OpenTPW's tool never has (the placement presets the start at the station's exit, `0x52A1A0`; the original reaches it only when a line closes with exactly Ctrl
held and no queue to follow, UI-031), and 0xB8 (184, the path tool's first click), 0xD6 (214, the third later click of the path or queue tool) and 0x151 (337,
the end of the queue tool) belong to the path and queue tools (`0x5272F5`, `0x527351`, `0x527620`; their producers are open under ADVISOR-009).
The tool handler's other direct calls of the message builder are tool help too (0xCD at `0x52644C`, in the cell-write path before the walk), and the tool-mode switch `0x52F580`
posts the new mode's help (0xCB for mode 3); none follows a walk call site directly, so any help shown after a closed circuit would come from the mode switch, not from the walk (not traced).

## Sections, collision objects, buoys (Q2)

`TrackSections`, `[TPW-EXE:0x54B2F0]`. Units: a cell is 0xC00 = 3072 (10 world units × 307.2), a tile 0x1800. The lateral slack is `s` = half width − vehicle radius
(1281 − 1024 = 257 for water, 2048 − 900 = 1148 for karts). A section holds a kind (bit 0x10000: no buoy), the tile corner and its objects; the pool refuses a
duplicate tile ("Duplicate track section - IGNORED!", so a crossing walked twice builds one section).

| Kind (mask) | Collision objects | Buoy |
| --- | --- | --- |
| 5 (0x05) | pivot (X0 + 0x1800, Y0); keep-in circle 0xC00 + s over x ≤ and y >, keep-out 0xC00 − s | pivot + (−0x87C, +0x87C) |
| 6 (0x41) | pivot (X0, Y0); x > and y > | pivot + (+0x87C, +0x87C) |
| 7 (0x50) | pivot (X0, Y0 + 0x1800); x > and y ≤ | pivot + (+0x87C, −0x87C) |
| 8 (0x14) | pivot (X0 + 0x1800, Y0 + 0x1800); x ≤ and y ≤ | pivot + (−0x87C, −0x87C) |
| 9 / 12 (0x44) | box at the tile centre, half extents (0xC20, s); 12 adds the add-on flag | centre |
| 10 / 13 (0x11) | box, half extents (s, 0xC20) | centre |
| 11 (crossing) | keep-in circle √(s² + 0xC00²) at the centre and four keep-out circles 0xC00 − s at the tile corners, each clipped to the tile's interior (`0x54B2F0` case 11: flags `& ~0x14`, `~0x12`, `~0x0A`, `~0x0C`); water controllers add 0x80 to the centre circle (TRACK-017 retired) | centre |

Object flags: 1 used; 0x02 a point with x ≤ the centre is inside, 0x04 x >, 0x08 y >, 0x10 y ≤ (all four = a full object); 0x20 box; 0x40 keep-out; 0x80
water crossing (the section builder sets it on the centre circle of a crossing for the water types −5, −8, −12 and −13, `[TPW-EXE:0x54B986]`; a vehicle inside such an object takes
part in no vehicle–vehicle collision, as the mover or as the other vehicle, so boats pass through each other in a crossing); 0x100 add-on. `0x87C` is 3072 · cos 45°. The inside test, the adjacency test and the next-object search are ported from
`0x5493F0`, `0x5491E0` and `0x549070`. The first two straights of the walk (the station) carry no buoy.

Buoys: the station's finish buoy first (flag 4), then the section buoys in walk order. Water: the finish buoy lies 0xC00 from the station reference along
the orientation word (controller +0xB0: bit 1 the axis, bits 2 and 4 the signs) and the unload point (+0x90/+0x94) a third of that offset
(`[TPW-EXE:0x545890]`); OpenTPW puts the reference at the centre of the first station tile with the offset behind it (TRACK-005). A launched boat first aims
at the buoy after the finish buoy (controller +0xA4).

The excitement the guests see for a water ride is the controller's base: `level × 60 / 100 + clamp(2 × corners + longest straight run + 3 × crossings, 0, 40)`,
clamped to 100, from the section counts `0x545310` (`[TPW-EXE:0x4E0560]`) — this narrows QUEUE-004 to the kart rides.

## Boat physics (Q4)

`BumpController`, `BumpVehicle`, `BumpMath`. All state is integer; the controller ticks once per **31 ms world substep** (`BumpController.TickSeconds = 0.031`, about 32.26 ticks per
second: the game loop adds 0x1F to its clock per substep, `[TPW-EXE:0x54F4C4]`, and calls the bump module tick `0x546C80` from it, `[TPW-EXE:0x54F55F]`; TRACK-011 retired October 14).
The duration unit of BUMP 13 stays 30 per second of the script (it stores seconds × 30, the original's constant), so a duration of 30 units lasts 0.93 s. A new vehicle starts at
heading 0 and drawn heading 0 (TRACK-012 and TRACK-023 retired: the 0xAC-byte record is zeroed and no water or kart launch writes +0x50 or +0x54); only a bumper car takes a random heading
at its first launch. A save from before the 31 ms tick may carry a tick clock up to 1/30 s; it restores and runs off on the next advance. Templates (`BumperTemplates`, `[TPW-EXE:0x764178]`, 14 records indexed by −type − 1): water types −5, −8, −12, −13 hold the same values; kart types
−4, −7, −9, −10 too (the model range differs).

| Field | Water | Kart |
| --- | --- | --- |
| +4 laps (negative) | −1 | −3 |
| +8 vehicle radius | 1024 | 900 |
| +0xC half width | 1281 | 2048 |
| accel min..max | 4..9 | 16..30 |
| damping per 1024 | 970..1000 | 960..950 |
| turn per tick (512ths) | 7..14 | 18..34 |
| restitution per 1024 | 980 | 900 |
| max vehicles / buoy radius | 64 / 512 | 8 / 2048 |

Performance 0..100 (the script's speed bias, default 50) interpolates each pair as `min + (max − min) · p / 100` with truncating division
(`[TPW-EXE:0x545180]`); `OriginalObjectRuntime.ScriptPerformance` feeds it.

- **Sine table**: `table[i] = trunc(256 · sin(2i · 0.00613591796875))`, 512 entries (`[TPW-EXE:0x5442E0]`; the table is built at start-up, so the rounding rule is
  verified from the init code: the constant is just below π/512 and the float-to-int conversion truncates). A heading is in 512ths of a turn clockwise from +Y:
  the thrust is `(sin h, cos h)`.
- **Steering** (`0x547C60`): while flag 0x10000 is set the aim is recomputed through the object graph (an interim waypoint when the target buoy's object is
  not adjacent, otherwise the buoy plus the jitter); the angle error is `(−h − (atan2(dx, −dy) · 256/π − 256)) mod 512`; the boat turns by the full rate while
  the error exceeds half the rate (and the ride is not broken); it thrusts `accel + bonus` (× ¾ inside a clipped corner object) along the heading when the
  error is below `clamp(distance / 512, 22, 48)`; velocity `+= table · a · 4 / 1024`.
- **Integration** (`0x547F50`): `v = v · d / 1024` with d the damping (1000 while the ride is broken, × 7/10 under flag 0x80000), the post-damping velocity is kept
  for the collisions, `position += v`. Water boats also keep a wall-hit spin (decays by 0.998 above 0x30), a roll `spin / 128` clamped to ±16 and a spray
  chance every 32nd tick from the module LCG: in phase 2, `(s >> 16) & 7 == 2` raises particle 96 (`P_EFFECT_SmallSplash`) at the boat (`[TPW-EXE:0x5480B7]`; TRACK-016 retired). The LCG
  `s · 214013 + 2531011` only gates cosmetics; gameplay draws use the world generator (the park's seeded stream, DET-013). A boat's first launch (`0x54A909`) starts an engine voice
  from the ride's EventMap slot 0 (kept until the boat unloads); the water types have no crash cue, no horn and no smoke, and their templates hold one model (no variants).
- **Walls** (`0x5497B0`): outside its object a boat first looks for another object that holds it; else the velocity is reflected about the radial direction
  (circle: rotation by `2(h_radial − 128) − 256 − (h_v − 256) − h_v`, × restitution; box: the offending component flips), the boat is projected back to the
  wall and, for water, the spin gets ∓0x30 × the penetration. **Keep-out** (`0x5494D0`): moving into a keep-out circle adds the normal speed ×
  (restitution + 1024) / 1024 along the radius and projects the boat to the rim. **Vehicles** (`0x546C80`): overlapping boats exchange the normal part of
  the relative speed (closing) or are pushed apart (separating); a boat that touched walls on more than 16 ticks of a 32-tick window is stuck and aims at the
  next buoy. The pairs run in both roles, as in the original.
- **Laps and unload** (`0x5474B0`, `0x54A040`, `0x54AE50`): at the finish buoy the lap counter (starts at the controller's laps, −1 for water) increments; reaching 0 turns
  the boat to the unload point (flag 0x20); within the buoy radius it retires, its guests go to the leaving list and the count falls. Otherwise it is relaunched.
  Capturing a buoy aims at the next one with a fresh jitter (new value on x, the previous on y). The unload plays particle 15 (BeamUpCar) for every type, and a boat's engine voice
  ends silently (`0x51C300`); the kart types play EventMap slot 1 over theirs. When the last boat is gone the original resets the controller to loading (state 1) — except for the water types −5, −8, −12 and −13, which
  the routine's type switch skips (`0x54AE50`); OpenTPW follows that reading, so a water ride stays in phase 2.

### BUMP commands

The PC dispatch (`0x554726..0x554A3D`, table at `0x556A18`) differs from the first reading of the Mac trace in two places: **BUMP 5** is the track-valid read
(`[esi + 0x2C]`), and **BUMP 17 with parameter 0 starts the water (animation 5), a nonzero parameter stops it (animation 12)**.

| Command | Effect |
| --- | --- |
| 1 | add the guest (a variable) to the next boat; refused while closed |
| 2 | the next guest who left a boat (0 when none), into the variable |
| 3 | start (kart phase 1 → 2); no effect for water; 3, 6, 7, 8, 9 and 10 leave the script's accumulator unchanged |
| 4 | launch a boat: needs phase ≠ 0 and fewer than the maximum; takes the waiting guests |
| 5 | track valid (the walk's verdict) |
| 6 / 7 | close / open (water opens straight into phase 2; closing sends waiting guests to the leaving list) |
| 8 / 9 | broken (state 2) or worn out (state 1) for a nonzero parameter, fixed (0) for zero; BUMP 8 also starts or removes the break smoke emitters (below); BUMP 9 with 0 calls the same fix routine as BUMP 8 with 0 (`0x5548C3` → `0x544E50`), which kills the emitters only when the old state was broken, and a jungle bumper car (type −1) plays animation 12 at the break and 5 at the fix; the worn state is read only by the kart exhaust branch (TRACK-013 retired) |
| 10 | eject all boats and close |
| 11 | boats running (also into the variable) |
| 12 | load the waiting guests into the first empty boat |
| 13 / 14 | laps ← parameter × 30 (a duration) / −parameter |
| 16 | unload one boat (the first without guests) |
| 17 | water pieces on (0) / off (nonzero): a value equal to the stored word (−1 after the ride's placement and after a load) does nothing, any other triggers the pieces from both station sides ("Piece animation and arm masks") |

Guests board through the existing ride-visitor protocol: the host writes the id to `VAR_LETMEON`, the script moves it with `BUMP 1`, and `BUMP 2`
writes it to `VAR_LETMEOFF` when the boat has unloaded.

## Rendering

`TrackRenderer`: one entity per piece part per tile, the mesh baked with its rest transform; the tile turns counter-clockwise by its rotation about its own
centre (the original's one-cell instance offset is unresolved, TRACK-003; the `Bumper.*Adjust` keys are the controller-reference offsets of the ride placement `0x529E10`, not a mesh pivot) and stands on the mean ground height of its four cells; boats are
`wr_ring.md2` at `position / 307.2` model units. A boat's height, pitch and roll follow the original's four probes (`[TPW-EXE:0x546280]`): ± the vehicle radius
along the facing and across it, each sampling the object on the probed cell ("Vehicle height" below); the mean is the height, the front/back and right/left differences the
pitch and roll.

### Piece animation and arm masks (October 16, 2026)

**Arm masks.** The instancer (`0x522D50` water, `0x522B90` kart) passes the low byte of the piece table entry's arm word to the mesh creation `0x463060`, which
hides (node flags `0x80000010`, `0x44B140`) every dummy record's node whose third word shares a bit with it. The pieces' end caps are such dummies, each naming
its arm (`wr_end01` 0x01, `wr_end02` 0x10, the crossing's four 0x01/0x04/0x10/0x40), so a straight shows no caps, an end stub (arm 0x01) loses the cap of its connected arm,
a tee only its missing arm's and a crossing none; bends and the bridge pieces 9, 12, 15 and 18 (arm 0) keep theirs. `TrackPieceKit.ArmMaskedNodes`; the renderer leaves those meshes out.

**Water clips.** A piece's animation 5 is its mesh's `m` member (`wr_trckSm.md2`: a texture-coordinate scroll of the water mesh, 50 ticks in the jungle kit, 100
in Halloween and space; the space kart pieces scroll too, the Halloween kart bridges flicker by texture frames). Two walks trigger it, `0x52A490` from the
tile after the station along the flow and `0x52A700` from the tile before it against the flow (`TrackPieceWalk`): each turns at corners, runs straight through
junctions, and stops at a tile with no arm ahead or before a station tile, and on every tile with a piece instance calls `0x4732A0(instance, 5 or 12, 0, 7, 1.0, 0)`:
animation 5 (start) or 12 (stop), flags 7 (loop, at once; on a stop the tile keeps its last frame). Callers (`TrackPieceAnimations.ForWalk`):

| Caller | Valid circuit | Invalid circuit |
| --- | --- | --- |
| ride placement walk (`0x524D0A`) | start, forward | stop, both walks |
| a track line ends (`0x526F02`) | stop, forward | stop, both walks |
| edit sub-modes 0xE/0x13 (`0x526BD0`) | start, forward | stop, both walks (no OpenTPW counterpart, TRACK-018) |
| `BUMP 17` (`0x5549ED`) | 0: start, other: stop, both walks | same |

A new or re-chosen piece is a new instance without a clip (`0x53AFA0`), and the load creates every piece that way. `BUMP 17` compares its value with the word
`+0xE6` of the ride's script, which the script attach (`0x558C71`) and the load's script restore (`0x5597A0`) set to −1, so the first `BUMP 17 0` of the water
ride's loop starts the water and the later ones do nothing; a track edit stops it until the ride closes (`BUMP 17 1`) and opens again. The state is presentation only
(`Level.TrackPieceAnimations`, a clock advanced with the world step, not saved or hashed); the renderer plays one animator per group of tiles of a piece that started
(and froze) together and draws their moving meshes from one copy. The walks start at OpenTPW's station exit and entry, whose relation to the original's
cells is TRACK-005 (the original's backward walk starts one cell off the tile grid for rotation 90). Open (TRACK-018): the edit sub-modes and texture-frame clips.

**Vehicle height** (TRACK-001 narrowed, TRACK-031 retired). Each probe of `0x546280` takes the object on its cell (`0x450AC0`: the cell's owner, a track cell through `0x53BF30`)
and samples it in the mode `0x450EA0` chooses (`TrackSurface.FromObject`):

| Mode | When | Height |
| --- | --- | --- |
| 2, surface records | the instance block's bit 8 (OpenTPW: a dummy record with type bit 2) | the 0x8000 faces of the records' meshes (`0x451290`, `0x451390`): a record with bit 4 answers with its first covering face, else the highest; none: no surface |
| 1, terrain | header flags 0x80, or the header float +0xA0 nonzero | the terrain (`0x4527F0`) + the header's +0xA0 |
| 4, collision mesh | otherwise | the instance's +0x2C mesh, first covering face; none: the instance's height |

Every mode adds the header's +0xA0 (`0x4511A0`, a NaN reset to 0); a probe with no surface (−100) keeps the vehicle's last height. In the shipped kits the flat pieces are terrain
mode with their floor in +0xA0 (water 3.254, The Blobulator 4.161; karts 3.1 jungle, 5 Halloween and space, 5.005 fantasy: the whole tile, banks and walls included), the hills and ramps
(`H`, `H_U`, `H_D`) read their surface record (`7/0`: bits 2 and 4), the jungle and fantasy kart stations and every water station are terrain plus the station model's +0xA0
(3.1, 5.005, 3.254, 4.072), and the bumper rides stand on a platform: The Hot Pot +29.8, Pumpkin Castle +15.005, Uforia +10.924 above the terrain. OpenTPW takes the terrain as the mean ground of
the tile's (bumper: the cell's) four corners and keeps the straight piece's floor for the Halloween and space kart stations, whose station model reads faces (TRACK-001). `--debug-water-track` (Debug builds) places a ride with a circuit for captures.

## Rides side by side, pools and demolition

Every tile records the ride it was laid for (`TrackCell.Owner`: the station's first-tile cell index plus one, saved and hashed; TRACK-021). A tile of one ride never
connects to, links to or overwrites a tile of another, so two water rides can touch without sharing a circuit, and a demolition removes exactly the ride's own tiles
and station. The demolition pays as the original's (`0x527EE0`, TRACK-007): every tile laid for the ride refunds the track cell cost × the ride's scrap percentage / 100 (`ClearCell` `0x53B280` credits it at `0x53B622`; the two replays run with flag `0x81B46C` = 1, so end tiles refund too, and with the queue flag `0x81D7A8` clear, so a tile another line still overlaps stays until its last clearing visit) and the demolition then charges 2 × the cell cost × the percentage / 100 once (`0x528157..0x528179`), both only for a type with a queue. A circuit that overflows the section, collision-object or buoy pool (1024, 2048, 1024) is invalid whatever the walk said, with no geometry and no boats
(TRACK-020). A controller counts as live while its runtime is not stopped and it is in the park's controller list; the park-resume capture and restore both use that
one rule, so a faulted or removed ride is skipped by both.

**One module for every ride** (TRACK-019 narrowed, `0x546C80`). The bump module is the park's (`BumpWorld`): one frame counter (`0x877B5C`) that the 32-tick gates of every
controller read, one vehicle pool of 256 (`0x877B68`, first free record, `0x549DB0`) whose live vehicles are stepped and integrated in pool order and then collided with every
other live vehicle of the pool whichever ride owns them (the restitution is the other vehicle's controller's, the crash code the vehicle's own type), one collision-object pool of
2048 (`0x877B70`, first free record, `0x54B1E0`) that every object search walks in pool order (the station drop, the steering's target and route, the walls and the
keep-out circles), so a vehicle can enter a neighbouring ride's object (`BumpVehicle.ObjectOwner`), and the buoy budget of 1024 (`0x54B170`) counted over every ride. A circuit or arena
that does not fit the shared pools is refused like an overflowing one. The module ticks once per world step before every ride's script step (`0x54F55F`), driven by the object pass. Saves carry the frame
counter and clock (`TrackWorldSaveData.FrameCount`, `TickRemainder`), each controller's object pool slots (`ObjectSlots`) and live buoy count (`BuoyCount`) and a vehicle's foreign object (`ObjectAttraction`), all optional:
an older save takes the first controller's clock, the slots the restore gives and no foreign object. A vehicle's `Slot` is its module pool slot (an older save whose rides
used the same slot gets the first free one; a save with the module clock must name slots unique across the rides). Because the shared pools' overflow verdicts depend on the
order the rides were built in, a restore of a save with the module clock trusts the save (`[EXT:track-pool-overflow-refused]`): every vehicle and object keeps its saved slot,
a ride saved invalid gets the station-only geometry even when its circuit walks valid now, a ride saved with no objects and no buoys stays without them, and no pool check runs
on the restore's re-walk, so a park near the 2048-object or 1024-buoy limit loads as saved. A vehicle of another ride whose object is rebuilt or removed loses it (`[EXT:bump-foreign-object-forget]`; the
original keeps its stale pointer). The drop-in overlap test walks the whole vehicle pool (`0x54A040`); the chase candidates need the same controller as the original's.

## Saves and determinism

The envelope's optional `Tracks` part holds the cell layer, every controller (scalars, tick clock, waiting and leaving guests, the sections for
verification, every boat field and its riders) joined by attraction id, and the cosmetic generator. On load the cells are restored, each circuit is walked again
and must reproduce the saved validity and sections, then the controller state is restored. The shape check refuses a save whose guests are not the park's (or are listed twice), whose boats lie outside the park or carry velocities and scalars outside fixed bounds, whose tick remainder is not within one tick, or whose cells carry a piece, rotation, link or owner the layer cannot hold; the restore refuses boats that aim at a buoy or sit in an object the walked circuit does not have. The collision objects and buoys are rebuilt, not saved (as in the
original). A park without a track and a controller writes no part and hashes as before: the track layer and the controllers join the canonical hash only while they
exist (the hash schema stays at 14). The digest covers every restored boat field (targets, jitter, velocities before collision, object index, wall counters, riders), the tick clock and each tile's owner. Boats draw jitter from the park's seeded world stream, which therefore advances with boats.

## Tests

`TrackSelectorTests` (the 16 masks, adjustments, tables, templates), `TrackCircuitTests` (tool, cost, clearing, closed loop, open track, tee refusal, crossing,
bridges), `BumpControllerTests` (sine table, section and buoy geometry golden values, commands, a golden boat run, determinism, collisions, saves),
`TrackPieceAnimationTests` (the two piece walks, the clip state, the walk causes and BUMP 17's stored word), `TrackRegisterRoundTests` (the 31 ms tick, start headings, the crossing flag and its effect on boat collisions, spray, engine voice and unload cues, the break smoke emitter, the unclamped bank), `HmpFileTests`, `TrackSaveTests` (shape check, codec, round trip, hash gating) and, with `OPENTPW_GAME_PATH`, `TrackAssetTests` (the kits and profiles of all eight
ride archives, the arm masks and water clips of the jungle kit, the real `Wateride.RSE` against the controller). The native smoke run lays a small closed circuit on the Jungle level and runs a boat, and the park-resume smoke with `OPENTPW_SMOKE_WATER=1` (both phases) buys the ride, saves a park with a boat on the water, restores it through the menu load and matches 600 continuation ticks of canonical hashes.
The golden values freeze this implementation of the traced rules; they are not captures of the original game.

## Design decisions and retired items (October 14 register round)

OpenTPW decisions with no original rule (`[EXT:name]` at the code sites):

- `[EXT:track-pool-overflow-refused]` (was TRACK-020): a circuit that overflows the section, collision-object or buoy pool is refused. The original skips sections silently when the section pool
  is full, **crashes** when the collision-object pool overflows (it logs "Out of Collision Objects" and writes the flags through a null pointer, `0x54B1E0`, `0x54B97E`, `0x54BAC8`) and
  drops buoys ("Out of Buoys").
- `[EXT:bump-foreign-object-forget]` (was part of `APPROX:TRACK-019`, now retired with the rest of it): a vehicle whose foreign object is rebuilt or removed loses it. The original keeps a pointer to the freed or reused pool record.
- `[EXT:bump-pre-shared-save-order]` (was part of `APPROX:TRACK-019`): a park-resume save from before the shared pools restores their order from the restore (controllers by attraction id); kept so older saves load.
- `[EXT:track-walk-step-limit]` (was part of TRACK-014): the circuit walk stops after as many steps as the layer has tiles plus the station. The original `0x52A970` has no limit and would hang on a flow that never returns to the start.
- `[EXT:track-tile-owner]` (was TRACK-021): every tile records its ride (`TrackCell.Owner`). The original's 40-byte cell has no owner field; its vehicles can cross into a neighbour ride's adjacent objects (TRACK-019).
- `[EXT:legacy-kart-save]` (was TRACK-027): a park-resume save from before the kart or bumper controllers restores its rides freshly (save compatibility).
- `[EXT:late-kart-guard]` (was TRACK-028): `BUMP 4` on a kart ride in phase 3 is refused. The original creates a kart whose relaunch gives it no movement flags (`0x54A040`), so it never moves and keeps the
  controller's count above 0, which blocks the race reset; no shipped script issues `BUMP 4` in phase 3.
- `[EXT:kart-roll-convention]`: the absolute sign of a kart's roll (above).

Retired with a code change: TRACK-009 (snap), 011 (tick), 012 and 023 (start heading), 016 (boat spray, engine voice, unload), 017 (crossing flag), 024 (bank clamp), 029 (chase, already ported).
October 16: `APPROX:TRACK-006` retired as already matching (the walk posts no message, above); TRACK-018 narrowed (arm masks and water clips built).
Retired as already matching: TRACK-013 (worn is read only by the kart exhaust branch, `0x5486FC`). Narrowed: TRACK-002, 005, 019, 026, 030 (texts in the register); TRACK-025 and 031 were retired in a later round (below).

## Add-ons (placement built, the rest read: TRACK-008)

The track add-ons are objects of the Upgrades category with `AddOn.UpgradesId` = their ride and `AddOn.UpgradeType` 1: Dino Karts Jump 1500 and Tunnel 1501,
Splish Splash Tunnel 1502, CryptKarts Chopper 2500 and Jump 2501, Ooze Crooz Ogre 2502, Space Racers Teleport 3500, Blobulator Tunnel 3501, Bumble Buggies Jump
4500 and Tunnel 4501 (Tubes Of Zob Extra Tube 3502 is type 2). Each has its own script, an `m` clip and footprint codes 0xB (kart) or 0x10 (water track). What the original
does with them (`TPW-EXE`):

- **Placement** (`0x528A70`): a footprint cell with code 0xB or 0x10 must lie on a straight, unflagged tile of the selected ride's kart or water track (validator passes 0x10B/0x110,
  0x20B/0x210 in the validator's add-on mode, `0x535953`–`0x5359B3`: the head's owner word is the ride's, no corner, no junction, no flag 0x10 on the cell yet), and placing the object writes the cell's flags word to 0x10
  (`0x53A510`, also on the second-layer cell). A flagged tile gets no piece instance (`0x53AFA0`): the add-on's mesh replaces it. OpenTPW: `TrackBuilder.CanStandAddOn` is the rule, `ParkObjects.AddOnCellRule`
  applies it to the '+' and '#' cells, `ParkCellMap.WriteFootprint` and `ClearFootprint` write and take off the flag on both layers (selling the add-on is the ordinary removal), and `Level.CheckAddOn` and
  `Level.PlaceAddOn` are the headless entry points (the build panel does not offer add-ons, RIDES-017). The next circuit walk reads the flag into kind 12 and 13 sections.
- **Walk** (`0x52A970`): a flagged straight builds section kind 12 (0x44) or 13 (0x11) with +8 = its place in the run of add-on tiles (0, 1, …; any other tile resets it),
  +0xC = the add-on's instance and +0x10 = its type id; on the run's second tile it shows or hides the add-on's class-1 dummies 1 and 0x10 (its open ends) by whether the
  tiles ahead and behind are plain pieces or stations ("Found upgrade... modify?").
- **Vehicles** (end of `0x547F50`, `0x548CA5`): only the Space Racers Teleport (type 0xDAC) has a hook. In its first tile a vehicle within ±0x4B0 of the centre along the
  section's axis is hidden (`0x548E80`: flag 0x200, the vehicle's instance, its shadow and its riders' heads), remembering the side it came from (0x800000), and the
  teleport plays animation 5 and then animation 2 looping; past 0x4B0 on the far side of the second tile the vehicle shows again, as it does on leaving add-on objects.

Not traced: where a ride's add-ons are offered and sold (the object window code around `0x481BC0..0x486D90` reads an add-on's ride through `0x4E2710`). Not built: the sections'
run counters and values, the dummies' show and hide, the Teleport's vehicle hook and the walk after a placement.

## Open points

- Jumps and tunnels (TRACK-008: the add-ons' placement is built headless; their offer and sale, section values and the teleport hook are not, see "Add-ons"), the edit
  sub-modes' piece trigger and texture-frame piece clips (TRACK-018), the demolition's replayed action list, the height source and the instance offset, the station-derived start cell and buoy placement, the walk's
  untraced junction conditions (see the `TRACK-` entries in [the register](https://opentpw.io/docs/FIDELITY-REGISTER.html)).
- The script performance value is the ride's speed bias (`Script.SpeedBias`, 50 at creation), pushed to the controller every tick.

## Go-karts (phase 2)

Source: `[TPW-EXE:0x544F90, 0x549DB0, 0x54A040, 0x5474B0, 0x547F50, 0x547170, 0x546C80, 0x546280, 0x545890, 0x54AE50]`. The rides: Dino Karts (jungle, 1150,
type −4), Crypt Karts (Halloween, 2150, −7, two riders per car), Space Racers (3150, −9) and Bumble Buggies (fantasy, 4150, −10); each has a `GoKarts.RSE` and an
`EventMap.RSE`. Rocky Racers is not a kart ride. Kart tiles are cell type 11 (station tiles 13) and use the kart piece kit `gk_trck*` of the ride's own archive;
`Level.KartTracks` is the kart builder next to `WaterTracks`, both over one cell layer, and `Level.BuilderOf` picks the one of a station. A tile costs
`Costs.KartTrackCell`.

| Theme | Template | Model base, count | Riders per car | Hover |
| --- | --- | --- | --- | --- |
| jungle | −4 | 0, 4 (`gk_blue`, green, orange, purple) | 1 | no |
| hallow | −7 | 0, 4 | 2 | no |
| space | −9 | 1, 4 (`gk_shadow` is mesh 0) | 1 | yes |
| fantasy | −10 | 1, 1 (`gk_blue` only) | 1 | yes |

The mesh of a kart is `count % count_of_template + base`, counted with the new kart. The kart meshes carry the dummies: head seats (class `0x100000B1`, number 1,
and 2 in the Halloween karts), the sparks node (`0x131`) and the camera (`0x1031`).

### The race

- **Grid** (`0x545890`): eight grid buoys (flag 2) then the finish buoy (flag 4). Slot i lies (8 − i − 2) × 0x400 along the flow and 0x400 across, even slots on the
  positive side; the finish buoy 0xC00 behind the station; the orientation word swaps the axes (bit 1) and flips x (bit 2) and y (bit 4). The station reference is the
  spawn and the unload point.
- **Loading**: the script opens the ride (`BUMP 7`, phase 0 → 1), boards each guest (`BUMP 1`) and creates a kart (`BUMP 4`: the pending guests ride, lap counter =
  controller laps, animation 12, particle 15, engine voice). In phase 1 the n-th kart aims at the n-th grid buoy in grid mode (flags `0x10048`) and parks within a
  quarter of the buoy radius (flag `0x80000`: damping × 0.7, no thrust; its target becomes the buoy after the finish).
- **Start** (`BUMP 3`, phase 1 → 2): every kart clears `0x80000` and grid mode, takes the controller's laps (`BUMP 14` stored −3 from the ride's `InitDuration`) and is
  relaunched with a thrust bias (3 − (draw & 7)) × accel / 9, drawn again at every relaunch (every lap).
- **Laps and the end**: crossing the finish buoy counts the lap (not when the phase is 0 or 3); at 0 the kart ends its run, and in phase 2 the first finisher sets the
  phase to 3 ("GoKart Race Over"); the others go home at their next crossing. A closed ride (`BUMP 6`) does the same through phase 0. A finished kart drives to the unload
  point, hands its riders to the leaving list (particle 15, EventMap slot 1) and is freed; the last kart out sets the phase back to 1 (water rides skip that reset).
- **Broken** (`BUMP 8`, state 2): no thrust, no turning, damping 1000, the lap countdown paused. **Worn** (`BUMP 9`, state 1) only changes the exhaust (no shipped
  script issues it).

### Effects

The controller raises `BumpEffect`s (`BumpController.Effect`); the runtime plays them through the existing hosts: `GameAudio.PlayRseEvent` (types 5 for the global
`cat_rides`, 3 for the ride's level events, the ids read from the ride's `EventMap.RSE`: jungle 194/195/199, Halloween 165/166/170, space 167/168/172, fantasy
134/135/139 for engine/stop/horn) and `OriginalObjectRuntime.ParticleRequested` (so `ParticleRendererModule` draws them).

| Effect | Trigger | Output |
| --- | --- | --- |
| crash | impulse above 130, once per tick per pair | request 3: global `cat_rides` 12 |
| horn | phase 2, every 32 ticks, speed above 230 | request 2: the module LCG draws every tick until `(lcg & 0x70000) == 0`, then EventMap slot 3 |
| exhaust | worn, every 32 ticks, speed above 80 | draw r = (lcg >> 16) & 7: r < 2 particle 1 (Sparks), r = 2 particle 10 + request 1 (`cat_rides` 24) |
| engine | every tick | EventMap slot 0 as a sentence: every tick the voice moves to the vehicle's voice place and takes parameter code = EventMap variable 10 at speed / 3 (below) |
| smoke | `BUMP 8` break | a persistent particle-2 emitter per car (below) |
| animation | speed above 40 / at most 40 | 5 / 12 (the hovering karts stay on 5) |

### Drawing

**Break smoke** (TRACK-023 and TRACK-025 retired): `BUMP 8` with a nonzero parameter (`0x544C80`) creates, for every car of the controller that carries guests (flag 0x4000), has a sparks
node and no emitter yet, a particle-2 (Smoke) emitter (`0x521E60`) stored in the car's +0x2C; `0x547F50` moves it to the node every tick (`0x51FE30`), and the fix (`BUMP 8` with 0,
`0x544E50`) and the unload (`0x54AE50`) destroy it (`0x51FEB0`). It is not phase-gated and cars created later get none; water boats have no sparks node. In OpenTPW the controller raises
`EmitterStart`, `EmitterMove` (every tick) and `EmitterStop` effects and the runtime keeps one `ParticleSystem` emitter handle per car slot (`ParticleSystem.Move` repositions it); the
flag is saved with the car (`Smoke`) but is presentation state and not in the hash, and a resume restarts the emitters of smoking cars. The emitter sits on the sparks node: `TrackRenderer`
stores the node's drawn place on the car (`BumpVehicle.SparksNode`, class 0x100 number 1 on a kart and 2 on a bumper car, `0x44B220` from `0x54A040`) and the start (`0x544D4D`) and every
move (`0x548593`) use it; a headless host has none and uses the car's point.

**Engine voice** (TRACK-025 retired): the engine event (EventMap slot 0; every shipped engine is a repeating sentence, flags 0x6) starts at the vehicle (`0x51EEB0`, then `0x51C270`) and
the integration of every family (water `0x548104`, karts `0x5484A4`, bumper cars `0x548A53`) moves it each tick to the voice place `((int)(x × 0.0033036), 0, (int)(y × 0.0033036))` in
sound units (the float at `0x700F20`, truncated by `0x67A830`; height 0, not the vehicle's) and sets its parameter: code = the EventMap's variable 10 (`0x55A3E0`; 16, or 17 in the fantasy
rides), value = speed / 3 as a byte (`0x51BC40` → `0x6BBB80`). The placeholder's parameter table (`0x6B7150`) holds the event's code (+0x12) in slot 0 and the chosen sound's two selectors
(+0x16, +0x1A) in slots 1 and 2 (`0x6BBF00`); a set that matches slot 1–3 re-evaluates the playing segment's volume and pitch (`0x6BBBE0`), and a sound whose mask (+0x18 / +0x1C) has bit 2
takes its pitch as min + value × (max − min) / 100 without a draw (`0x6BC170`, bit 1 the volume likewise, `0x6BC090`). The kart and bumper engines bind selector 16/17 to pitch (range −24..36),
so the pitch follows the speed; the water engines have no binding and play unchanged. `SoundSentence.SetParameter` and `MoveTo` implement the table and the move for voices started by
`GameAudio.StartRseSentence`. The original stores `0x51C270`'s return value back into the vehicle's voice handle (+0x20, `0x548137`;
what `0x51C270` returns besides the handle is not read); OpenTPW keeps the voice it started.

`TrackRenderer` is one per family. A kart is drawn along its drawn heading (+0x54, which follows the velocity by (angle difference × speed / 1000) per tick) with the
four-probe height, pitch and roll over the sampled pieces ("Vehicle height").
Space and fantasy karts hover 1.6 units above the front/back mean with a sine bob (±0.43 units, period 512/6 ticks), have no roll of their own, bank into turns
(`roll += yaw / 13 · π/2` with no clamp, `[TPW-EXE:0x5469C3]`; TRACK-024 retired. The original's roll is the negative of this pose's slope roll with the bank added in its own sign, so the
bank's sign relative to the slope is the original's and the absolute roll sign is a rendering convention, `[EXT:kart-roll-convention]`) and stand on a `gk_shadow` instance on the four-probe mean. The riders' heads are `RideHeadRenderer` sources at the kart's head dummies (TRACK-026).
`--debug-kart-track` (Debug builds) places Dino Karts with a circuit, three karts and a long race for captures.

### Saves

The vehicle record carries the kart fields (drawn heading, yaw rate and change, animation, effect request and latch, last height; flags, lap counter, thrust bias and
target were already saved); the controller's phase is the saved state. The kart fields join the digest of kart controllers only, so water saves keep their hashes.
The last height is the drawn height `TrackRenderer` writes for every family (zero in a headless run); it is presentation state outside the digest, so a rendered and a headless
run of the same park save the same state except for it, and it only seeds the next probe that finds no surface and the effects' height.

### Tests

`KartControllerTests` (grid for every orientation, slot targets and parking, the eight-kart limit, model index per type, hover flags, the start and the bias, bias
re-roll per lap, race end and leaving order, phase 1 after the last kart, close, BUMP 13/14, broken, horn, crash, exhaust, animation, pose maths, saves) and, with
`OPENTPW_GAME_PATH`, `TrackAssetTests` (the four `GoKarts.RSE` BUMP sequences pinned, templates, meshes, dummies and EventMap ids of the four rides, and Dino Karts' real
script through a race). The native smoke test has a kart phase: the circuit from the kart tool's clicks, two karts parked on the grid, a lap and the unload, a capture
(`artifacts/native-smoke-original-kart-track.png`), the removal.

### Findings of phase 2

The trace's corrections: `BUMP 5` reads the track-valid flag, `BUMP 8` has no return value, `BUMP 17` 1 stops the pieces; the × ¾ corner thrust applies to any vehicle whose
object lacks all four quadrant bits. The lap count of a kart script is `VAR_DURATION`, which the ride's `InitDuration` (3) sets at creation
(`[TPW-EXE:0x4db565]`). Still open: the `cat_rides` catalogue ids, the add-ons' vehicle hook `0x548E80` (the Space Racers Teleport hides a vehicle between its two tiles, TRACK-008), what else besides `BUMP 9` sets the worn state, and whether
the late-to-grid extra lap is observable (the code reproduces it as written).

## Bumper cars (October 14)

Source: `[TPW-EXE:0x544840, 0x544A10, 0x544F90, 0x544B50, 0x549B80, 0x549DB0, 0x549C60, 0x54A040, 0x5474B0, 0x54AC70, 0x54AD90, 0x54AE50, 0x545890]`. The bumper rides
are `jungle/rides/bumper` (type −1), `hallow/rides/bumper` (−6), `space/rides/bumper` (−14) and `fantasy/rides/bbugs` (Bumble Bugs, −11): `Bumper.WhichTrackType` 0
with a negative `Bumper.BumperType`. They have no track; `BumperTemplates.ForRide` gives them a `BumpController` (`BumperTemplate.IsBumper`, `BumpController.HasTrack`
false, `IsTrackValid` true as the attraction's constructor leaves it). Before this controller existed the four `bumper.RSE` scripts boarded their guests with `BUMP 1`
and `BUMP 12` and then waited for `BUMP 2` for ever: a headless probe of the jungle ride boarded 16 guests and released none.

**The script cycle** (all four scripts; Bumble Bugs differs in detail): `BUMP 7` opens; a loop keeps `VAR_CARS` equal to `VAR_CAPACITY` (`BUMP 4` makes a car, `BUMP 16`
unloads an empty one, never when every car holds a guest); each boarding guest is `BUMP 1 VAR_LETMEON` (pending list) followed by `BUMP 12` (the pending guest goes into the
first car without riders); when the cars are full or ten seconds passed after the last boarding, `BUMP 13 VAR_DURATION` stores seconds × 30 in the controller's duration,
`BUMP 3` starts; then the script loops on `BUMP 2 VAR_LETMEOFF` until `VAR_ONRIDE` is 0, handling breakdown (`BUMP 8`), wear (`BUMP 9`) and closing (`BUMP 6`) on the way.

**Controller rules** (all traced):

- Open (`0x544840`): phase 1 whatever it was. Close (`0x544A10`): pending guests to the leaving list, phase 0, every car gets laps 0 and flags `(flags & 0xFFFFBFA1) | 0x10020`.
- Create (`0x549DB0`, `BUMP 4`): needs a phase ≠ 0 and fewer cars than the type's maximum (8); the car carries `0x4000` until its first launch, the mesh is
  `count % ModelCount + ModelBase`. The first launch (`0x54A040`) draws up to 100 random spots (radius = `rand % arena radius`, a random angle) around the station until no car overlaps,
  clears `0x4000` (an empty car neither steers nor thrusts), takes a random heading and sets the wall-stuck counter to −1.
- Load (`0x549B80`, `BUMP 12`): the first car without riders takes the pending guests, flags `0x84000` (carrying, slowed: no thrust) and the controller's duration.
- Start (`0x544F90`): phase 1 → 2; every car clears `0x80000|0x40`, takes the duration as its counter and is relaunched. The jungle type also switches carrying cars to animation 5.
- Arena (`0x545890`): one full keep-in circle at the station centre (radius 0x1200 jungle, 0x1100 fantasy, 0x1600 hallow and space) and eight buoys on a ring (radius 0xC00, 0xB00,
  0x1000) turned 45° at a time with the 8-bit sine factors. No sections, no finish buoy.
- Wandering (relaunch): flags `(flags & 0xFFFBFF89) | 0x10008 | 0x8000`; the target is a random buoy, or one time in four (rand & 0xF ≤ 3, only when already wandering) the buoy after the current;
  buoy countdown 3, so a car changes its mind after three captures. The shared steering, walls and car-car collisions of the other controllers apply.
- **Session end** (`0x5474B0`): the duration counts down one per tick while the ride runs (phase 2) and is not broken; at 0 a car drops `0x4000` and gets flags `0x10020`. A car with
  `0x20` hands **every rider** to the front of the leaving list at once (`0x54AC70`); when no rider is left anywhere in the controller the phase becomes 1 ("loading"), the car's flags
  `&= 0xFFFFFF81` and its animation 12. The cars are not unloaded: they stay, ready for the next session. `BUMP 2` then returns the guests one by one.
- `BUMP 16` (`0x54AD90` is called with its flag constant 1: the first empty car, else the first car) retires a car at once; the last one going back sets the phase to 1, as for karts.
- The ride's excitement uses the controller formula like karts and water (level × 60 % plus the piece terms, which are 0 here).

**Saves**: a bumper controller is saved like a kart's (phase, state, duration, guest lists, cars); it has no sections. A park-resume save from before the controller restores
the ride freshly (TRACK-027) and hands the riders it still held to the new leaving list.

**Chase** (TRACK-029 retired: the choice and the flag-4 step are ported as traced, the round-2 report matches this text point by point): at a relaunch a car that is neither chasing nor just bumped draws `rand & 0xF`; at 0..2, with two or more cars, it locks onto a random other car that is not unloading
(+0x80 = its slot, countdown 90, flags `(flags & 0xFFEBFF85) | 0x8004`). Each step the countdown falls; at 0, or when the target's flags `& 0x40021` are not exactly 1 (bumped or going home), the car relaunches.
Otherwise it aims at the target's position plus its velocity times the ticks to reach it (at most 24), pulled onto the arena rim when outside, and steers there with the shared steering.

**Effects and drawing**: the crash asks for code 5 (the CRUNCH sound, global `cat_rides` 12) above impulse 0x82 for hallow, fantasy and space; the jungle type (−1) has no case in the crash switch and is silent
(`0x546C80`). Non-jungle cars that carry a rider throw sparks (particle 1, code 1 = `cat_rides` 24) on every 32nd tick when `(lcg >> 16) & 3 == 2`. The first carrying car of a running ride
starts the engine voice (EventMap slot 0), stopped when it hands its riders over or is removed (no stop sound, unlike karts). The drawn heading follows the steering heading (jungle) or the velocity (others) at
speed / 1000 of the angle gap. Cars are the ride's supplemental meshes (`b_car` jungle, `halcar`, `spacecar`, `bbugscar`; model base 1 in the jungle, 0 elsewhere) drawn by the kart renderer without a kit
(`TrackFamily.Bumper`) on the ride's platform (the four probes sample the ride object, terrain plus its +0xA0, "Vehicle height"), the jungle `b_wake` (mesh 0, flag 0x2000000) as the original's
second instance: behind the car by `(sine[h] × speed × 4) / 49143` and `(sine[h + 128] × speed × 4) / 49143` model units (`0x764CD8`), 0.7 above the car's height, turned by the drawn heading
only (`0x546280`); the riders' heads hang from the car's head dummy and the break smoke from its sparks node (number 2).

**Saves** (park-resume): the validator accepts a bumper session (durations up to 0x10000 ticks, the wall counter −1, the chase target). `OPENTPW_SMOKE_BUMPER=1` on both phases of the park-resume smoke buys and places the theme's
bumper ride, saves with a session running and guests in the cars, restores through the menu load and matches the 600 continuation hashes. The digest holds the chase target and the drawn heading of bumper cars only.

**Arena centre** (`APPROX:TRACK-030` retired with a code change): the ride placement `0x529E10` (`[TPW-EXE:0x529E10]`) takes the cursor cell `0x818C3C`, which is the build click's
anchor (written at `0x528E12` from `0x528B44`, OpenTPW's placement anchor), adds the type's `Bumper.<Side>X/YAdjust` for the rotation (0° North at type +0xA4/+0xA8, 90° East,
180° South, 270° West; the key table at `0x74541C` puts them after `WhichTrackType` +0x9C and `BumperType` +0xA0) and the fixed offset (+4, +1), (+1, −5), (−5, −2), (−2, +4),
scales by 0xC00, and the arena builder `0x545890` adds 0x600 on both axes. `Level.BumperArenaCentre` computes exactly that. All four shipped bumper rides are 5 × 5 with
Adjust North (−2, 1), East (1, 3), South (3, 0), West (0, −2), so the arena sits on the middle of footprint cell (2, 2) in every rotation, the same point as the footprint's
middle the earlier approximation used (`BumperArenaTests`).

**Open**: nothing of the arena; TRACK-031 retired (platform probes, the wake instance and the engine voice's parameter are the original's).

**Tests**: `BumperCarAssetTests` (with `OPENTPW_GAME_PATH`): each theme's real script with 16 guests for ten simulated minutes: every guest boards and is released, the ride cycles at least twice,
nobody stays in `GuestState.Using`, the phase returns to loading after each session; `BumperControllerTests` (asset-free) pins the arena, the countdown, the release, the chase, the crash codes and the saves. The native smoke places a bumper ride, loads two riders, runs a short session and captures
`artifacts/native-smoke-original-bumper-cars.png`.
