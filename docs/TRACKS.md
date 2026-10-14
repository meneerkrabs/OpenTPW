# Kart and water ride tracks

Phase 1 of the original kart and water tracks was **one water ride, Splish Splash on the Jungle level, end to end** (the data tables, the track
tool, the circuit walk, the section geometry, the boat physics and the script bridge). Phase 2 adds **the go-karts in all four themes**, run end to end
for the Jungle's Dino Karts (see "Go-karts" below); they share the piece tables, the templates, the tool and the controller with the water rides. Rules are traced from the Mac container (`STP-PPC`) and the Windows `tp.exe` (`TPW-EXE`, through the
shared REA session over the decrypted executable); every approximation carries a `TRACK-` ID in
[the register](FIDELITY-REGISTER.md).

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

Not in phases 1 and 2: jumps and tunnels (add-ons), the tee pieces' arm masks and the
water texture animation, sounds and particles of the boats, advisor messages, the three other themes' water rides were checked for data only (the
tables and kits load for all four; only the Jungle ride was run).

## Cells

A track tile is 2 × 2 map cells (a mesh is 20 world units wide). The origin cell carries the tool's type, **11 kart** or **16 water**; the other
three cells are **type 12** and record the origin cell. The station tiles use **13 kart / 18 water** (presumed from the types the walk accepts,
low confidence); 17 has no known writer; the walk accepts 11, 13, 16, 18 and 25. `ParkCellMap.Tracks` (`TrackCellLayer`) mirrors the original's
40-byte record at game +0x1102D8:

| Field | Offset | Meaning |
| --- | --- | --- |
| type | +8 | cell type (above); the map's own type byte stays in sync, so guests and the object rules see an occupied cell |
| mask | +0xC | connections: 0x01 −Y, 0x04 +X, 0x10 +Y, 0x40 −X |
| link | +0xD | flow heading bit the last walk moved along here (ORed when a crossing is walked twice) |
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

Bridges: a straight tile over water (or over the paths it cleared, TRACK-010) gets bit 1 (original: map type 2, 7 or 30, `[STP-PPC:0x10088590]`);
the second pass (`[STP-PPC:0x1007A354]`) turns it into H (neither neighbour bridged), H_U (only ahead), H_D (only behind) or H_A (both), ahead
and behind following the flow link.

Piece tables (12 bytes: handle, name, arm byte): kart 20 entries, water 25: 0 none; 1 Q; 2 S; 3 B; 4 X; 5–8 X with arm masks 0x15/0x54/0x51/0x45; 9–11 H;
12–14 H_U; 15–17 H_D; 18 H_A; 19 B2; 20–24 X2 (water only). `TrackPieceKit` loads `wr_trck<stem>.md2` / `.hmp` (water) or `gk_trck…` (kart) from the
ride's archive; all four water and all four kart archives load completely.

### Height profiles (`HmpFile`)

48-byte header: u16 version 3, u16 magic 0xAB1E, u16 samples per cell 5, u16 100, u16 width, u16 height, u32 body offset 48, u32 maxima offset, u32 flags
offset, six floats (the bounding box, at most 20). Body: (5w) × (5h) bytes of profile (about 0.4 world units per step), then w × h per-cell maxima and
w × h zero flags. A piece profile is 2 × 2 cells (156 bytes), the station's 4 × 4 (480 bytes). The reader is bounded (1 MiB), strict about the
offsets and the length, and rejects malformed files. Nothing consumes the data (TRACK-002, RIDES-014).

## The tool (`TrackLineWriter`, `TrackBuilder`)

`CellToolMode.WaterTrack = 16` and `KartTrack = 11`: the mode is the cell type the line writes. A click snaps to the dominant axis (a tie keeps X)
and then to whole two-cell steps (rounding toward the start, TRACK-009; `[TPW-EXE:0x53AAC0]`). Each tile validates its four cells in the path
tool's order (bounds, owned land, objects, terrain — water is allowed for water tracks, queues refuse), charges `Costs.WaterTrackCell` once when
written (existing tiles are free and bump their overlap counter) and clears path cells (`[STP-PPC:0x10088CB8..0x10088DEC]`). A new tile takes
the arm toward any neighbour whose arm already points at it (the station's open ends, an earlier line's end); consecutive tiles of a line connect.
Removing a tile clears its neighbours' arms; the station tiles never lose theirs. A line ending on an existing tile ends the tool, like the path tool.

The level reaches the tool through placing the ride (`ParkHud.OnObjectPlaced` → `Level.EnterTrackTool`); when the circuit becomes valid the tool
closes and the queue tool starts. `Level.BuildTrack` is the headless entry.

### The station (TRACK-005)

The 4 × 4 footprint holds two station tiles along the heading, on the rows beside the entrance (`TrackStation`). The circuit starts at the first one
(`[TPW-EXE:0x52A970]`: the start cell is the station-derived cell shifted four cells by the rotation; which cell that is was not resolved). The heading
follows the rotation (0° +X, 90° −Y, 180° −X, 270° +Y). The tile before the first station tile (`Entry`) is where the player's loop returns; the tile after
the second (`Exit`) is where the first line starts.

## The circuit walk (Q3)

`TrackCircuitWalker`, `[TPW-EXE:0x52A970]`. From the start tile the walk follows the masks two cells at a time. Each step needs a track tile with at least
two arms and an arm toward the flow; a bend leaves by the other arm and rewrites the link; a three- or four-arm tile continues straight ahead and fails
when that arm is missing or the cell ahead has type 0 (**the tee refusal**); more is untraced (TRACK-014). The segment number goes to the tile. The circuit is
valid when the walk arrives back at the start tile; no minimum length. The result is the attraction's validity flag, which `BUMP 5` returns and which the
park points counts-test and guest boarding read. A re-walk (commit mode) also resets the controller: boats leave, the ride closes
(`[TPW-EXE:0x545450]`).

Messages: the original raises advisor and event ids 0xCC, 0x151, 0xB8, 0xD6 after a walk; their texts are unresolved (TRACK-006).

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
| 11 (crossing) | keep-in circle √(s² + 0xC00²) at the centre and four keep-out circles 0xC00 − s at the tile corners (their clipping is TRACK-017) | centre |

Object flags: 1 used; 0x02 a point with x ≤ the centre is inside, 0x04 x >, 0x08 y >, 0x10 y ≤ (all four = a full object); 0x20 box; 0x40 keep-out; 0x80
reserved for water crossings; 0x100 add-on. `0x87C` is 3072 · cos 45°. The inside test, the adjacency test and the next-object search are ported from
`0x5493F0`, `0x5491E0` and `0x549070`. The first two straights of the walk (the station) carry no buoy.

Buoys: the station's finish buoy first (flag 4), then the section buoys in walk order. Water: the finish buoy lies 0xC00 from the station reference along
the orientation word (controller +0xB0: bit 1 the axis, bits 2 and 4 the signs) and the unload point (+0x90/+0x94) a third of that offset
(`[TPW-EXE:0x545890]`); OpenTPW puts the reference at the centre of the first station tile with the offset behind it (TRACK-005). A launched boat first aims
at the buoy after the finish buoy (controller +0xA4).

The excitement the guests see for a water ride is the controller's base: `level × 60 / 100 + clamp(2 × corners + longest straight run + 3 × crossings, 0, 40)`,
clamped to 100, from the section counts `0x545310` (`[TPW-EXE:0x4E0560]`) — this narrows QUEUE-004 to the kart rides.

## Boat physics (Q4)

`BumpController`, `BumpVehicle`, `BumpMath`. All state is integer; the controller runs **30 ticks per second** (BUMP 13 stores seconds × 30; the rate is
TRACK-011). Templates (`BumperTemplates`, `[TPW-EXE:0x764178]`, 14 records indexed by −type − 1): water types −5, −8, −12, −13 hold the same values; kart types
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
  chance every 32nd tick from the module LCG (counter only, TRACK-016). The LCG `s · 214013 + 2531011` only gates cosmetics; gameplay draws use the world
  generator (the park's seeded stream, DET-013).
- **Walls** (`0x5497B0`): outside its object a boat first looks for another object that holds it; else the velocity is reflected about the radial direction
  (circle: rotation by `2(h_radial − 128) − 256 − (h_v − 256) − h_v`, × restitution; box: the offending component flips), the boat is projected back to the
  wall and, for water, the spin gets ∓0x30 × the penetration. **Keep-out** (`0x5494D0`): moving into a keep-out circle adds the normal speed ×
  (restitution + 1024) / 1024 along the radius and projects the boat to the rim. **Vehicles** (`0x546C80`): overlapping boats exchange the normal part of
  the relative speed (closing) or are pushed apart (separating); a boat that touched walls on more than 16 ticks of a 32-tick window is stuck and aims at the
  next buoy. The pairs run in both roles, as in the original.
- **Laps and unload** (`0x5474B0`, `0x54A040`, `0x54AE50`): at the finish buoy the lap counter (starts at the controller's laps, −1 for water) increments; reaching 0 turns
  the boat to the unload point (flag 0x20); within the buoy radius it retires, its guests go to the leaving list and the count falls. Otherwise it is relaunched.
  Capturing a buoy aims at the next one with a fresh jitter (new value on x, the previous on y). The unload also plays particle 15 (BeamUpCar) and EventMap slot 1
  (not played, TRACK-016). When the last boat is gone the original resets the controller to loading (state 1) — except for the water types −5, −8, −12 and −13, which
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
| 8 / 9 | broken (state 2) or worn out (state 1) for a nonzero parameter, fixed (0) for zero (TRACK-013) |
| 10 | eject all boats and close |
| 11 | boats running (also into the variable) |
| 12 | load the waiting guests into the first empty boat |
| 13 / 14 | laps ← parameter × 30 (a duration) / −parameter |
| 16 | unload one boat (the first without guests) |
| 17 | water pieces on (0) / off (nonzero); stored and saved, not played (TRACK-018) |

Guests board through the existing ride-visitor protocol: the host writes the id to `VAR_LETMEON`, the script moves it with `BUMP 1`, and `BUMP 2`
writes it to `VAR_LETMEOFF` when the boat has unloaded.

## Rendering

`TrackRenderer`: one entity per piece part per tile, the mesh baked with its rest transform; the tile turns counter-clockwise by its rotation about its own
centre (the original's one-cell instance offset is unresolved, TRACK-003) and stands on the mean ground height of its four cells; boats are
`wr_ring.md2` at `position / 307.2` model units. A boat's height, pitch and roll follow the original's four probes (`[TPW-EXE:0x546280]`): ± the vehicle radius
along the facing and across it, each the highest surface triangle under the point (here the piece's water meshes, `TrackSurface`; the station tiles use one water
height), else the terrain; the mean is the height, the front/back and right/left differences the pitch and roll. The original's surface-face flag (bit 0x8000 of the
face's first u16) is not decoded (TRACK-001). The pieces are static (TRACK-018). `--debug-water-track` (Debug builds) places a ride with a circuit for captures.

## Rides side by side, pools and demolition

Every tile records the ride it was laid for (`TrackCell.Owner`: the station's first-tile cell index plus one, saved and hashed; TRACK-021). A tile of one ride never
connects to, links to or overwrites a tile of another, so two water rides can touch without sharing a circuit, and a demolition removes exactly the ride's own tiles
and station. A circuit that overflows the section, collision-object or buoy pool (1024, 2048, 1024) is invalid whatever the walk said, with no geometry and no boats
(TRACK-020). A controller counts as live while its runtime is not stopped and it is in the park's controller list; the park-resume capture and restore both use that
one rule, so a faulted or removed ride is skipped by both.

## Saves and determinism

The envelope's optional `Tracks` part holds the cell layer, every controller (scalars, tick clock, waiting and leaving guests, the sections for
verification, every boat field and its riders) joined by attraction id, and the cosmetic generator. On load the cells are restored, each circuit is walked again
and must reproduce the saved validity and sections, then the controller state is restored. The shape check refuses a save whose guests are not the park's (or are listed twice), whose boats lie outside the park or carry velocities and scalars outside fixed bounds, whose tick remainder is not within one tick, or whose cells carry a piece, rotation, link or owner the layer cannot hold; the restore refuses boats that aim at a buoy or sit in an object the walked circuit does not have. The collision objects and buoys are rebuilt, not saved (as in the
original). A park without a track and a controller writes no part and hashes as before: the track layer and the controllers join the canonical hash only while they
exist (the hash schema stays at 14). The digest covers every restored boat field (targets, jitter, velocities before collision, object index, wall counters, riders), the tick clock and each tile's owner. Boats draw jitter from the park's seeded world stream, which therefore advances with boats.

## Tests

`TrackSelectorTests` (the 16 masks, adjustments, tables, templates), `TrackCircuitTests` (tool, cost, clearing, closed loop, open track, tee refusal, crossing,
bridges), `BumpControllerTests` (sine table, section and buoy geometry golden values, commands, a golden boat run, determinism, collisions, saves),
`HmpFileTests`, `TrackSaveTests` (shape check, codec, round trip, hash gating) and, with `OPENTPW_GAME_PATH`, `TrackAssetTests` (the kits and profiles of all eight
ride archives, the real `Wateride.RSE` against the controller). The native smoke run lays a small closed circuit on the Jungle level and runs a boat, and the park-resume smoke with `OPENTPW_SMOKE_WATER=1` (both phases) buys the ride, saves a park with a boat on the water, restores it through the menu load and matches 600 continuation ticks of canonical hashes.
The golden values freeze this implementation of the traced rules; they are not captures of the original game.

## Open points

- Jumps and tunnels, the bumper cars' chase mode, effects and drawing (TRACK-015, 029, 030, 031), the water animation and arm masks, the boats' spray particles (TRACK-016), the
  kart sounds' catalogue ids and the engine pitch (TRACK-025), the advisor messages, the demolition payment, the height source and the instance offset, the station-derived start cell and buoy placement, the walk's
  untraced junction conditions (see the `TRACK-` entries in [the register](FIDELITY-REGISTER.md)).
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
| engine | every tick | EventMap slot 0; pitch parameter speed / 3 (raised, not applied: TRACK-025) |
| smoke | broken | particle 2 every 32 ticks (cadence assumed: TRACK-025) |
| animation | speed above 40 / at most 40 | 5 / 12 (the hovering karts stay on 5) |

### Drawing

`TrackRenderer` is one per family. A kart is drawn along its drawn heading (+0x54, which follows the velocity by (angle difference × speed / 1000) per tick) with the
four-probe height, pitch and roll over the piece's `.hmp` profile (2-unit samples of 0.4 units; the profile equals the mesh's top surface sample for sample, TRACK-001).
Space and fantasy karts hover 1.6 units above the front/back mean with a sine bob (±0.43 units, period 512/6 ticks), have no roll of their own, bank into turns
(TRACK-024) and stand on a `gk_shadow` instance on the four-probe mean. The riders' heads are `RideHeadRenderer` sources at the kart's head dummies (TRACK-026).
`--debug-kart-track` (Debug builds) places Dino Karts with a circuit, three karts and a long race for captures.

### Saves

The vehicle record carries the kart fields (drawn heading, yaw rate and change, animation, effect request and latch, last height; flags, lap counter, thrust bias and
target were already saved); the controller's phase is the saved state. The kart fields join the digest of kart controllers only, so water saves keep their hashes.

### Tests

`KartControllerTests` (grid for every orientation, slot targets and parking, the eight-kart limit, model index per type, hover flags, the start and the bias, bias
re-roll per lap, race end and leaving order, phase 1 after the last kart, close, BUMP 13/14, broken, horn, crash, exhaust, animation, pose maths, saves) and, with
`OPENTPW_GAME_PATH`, `TrackAssetTests` (the four `GoKarts.RSE` BUMP sequences pinned, templates, meshes, dummies and EventMap ids of the four rides, and Dino Karts' real
script through a race). The native smoke test has a kart phase: the circuit from the kart tool's clicks, two karts parked on the grid, a lap and the unload, a capture
(`artifacts/native-smoke-original-kart-track.png`), the removal.

### Findings of phase 2

The trace's corrections: `BUMP 5` reads the track-valid flag, `BUMP 8` has no return value, `BUMP 17` 1 stops the pieces; the × ¾ corner thrust applies to any vehicle whose
object lacks all four quadrant bits. The lap count of a kart script is `VAR_DURATION`, which the ride's `InitDuration` (3) sets at creation
(`[TPW-EXE:0x4db565]`). Still open: the `cat_rides` catalogue ids, the add-on handler `0x548E80` (jumps, tunnels), what else besides `BUMP 9` sets the worn state, and whether
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

**Open**: chase mode (TRACK-029), the arena centre (TRACK-030), drawing and effects (TRACK-031).

**Tests**: `BumperCarAssetTests` (with `OPENTPW_GAME_PATH`): each theme's real script with 16 guests for ten simulated minutes: every guest boards and is released, the ride cycles at least twice,
nobody stays in `GuestState.Using`, the phase returns to loading after each session; `BumpControllerTests` pins the arena, the duration countdown and the release.
