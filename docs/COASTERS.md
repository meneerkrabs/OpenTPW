# Roller coasters

Players build roller coasters in OpenTPW the way the original does: a chain of
pylons placed on map cells, joined by a smooth track that leaves and returns
through the station. This page describes the implementation (COAST-I). The
evidence and the specification are in
[reverse/COAST-plan.md](reverse/COAST-plan.md), sections 9, 9.7 and 12 to 19.
The register of what is still approximated is at the end.

Coasters are the ride objects with `Bumper.WhichTrackType 3`. The PC catalogue
has twelve, three per theme, and three of them are water flumes (troughs). Go
karts (track type 1) and water rides (type 2) use cell tracks. They are not part
of this work.

## Code

| File | Contents |
| --- | --- |
| `World/Coasters/CoasterDefinition.cs` | The definition: code defaults of `0x2d274`, then the `coaster.sam` values (only nonzero values replace a default; the three pylon cost rates are copied as given). The StdPylon and LoopPylon controls. |
| `World/Coasters/CoasterTrack.cs` | Nodes, the placement validator `0x36ba4`, invalid marking, closure, completion, the height clamp, the pylon cost, node positions, node tangents, auto-banking and the knots. |
| `World/Coasters/CoasterCurve.cs` | The curve: Hermite tangents along each knot's direction, one cubic Bézier per knot pair (`0x41b00`), the sampler `0x44ff4` (parameter steps of 0.05, one sample every 3.0 world units), yaw and pitch, and the 4096-entry sine table. `ICoasterCurve` keeps the curve swappable. |
| `World/Coasters/CoasterRating.cs` | The rating pass `0x3fc18`, the step `0x415b4`, the final `0x41900`, the train count and the guests' ride excitement. |
| `World/Coasters/CoasterRide.cs` | The controller of one station: samples, rating, trains, the `COAST` commands of the station script and the train motion `0x40650`. |
| `World/Coasters/CoasterBuilder.cs` | The headless build API: place, move, height, close, complete, reopen, delete, and the money of each. |
| `World/Coasters/CoasterWorld.cs` | The coasters of one park and the boarding generator `CoasterRng`. It is owned by the park's `RideScriptWorld`. |
| `World/Coasters/CoasterRenderer.cs`, `CoasterAssets.cs` | Presentation: per coaster the track mesh, the pylon models and the cars, rebuilt only when the track or the build cursor changes (see "Presentation" below). |
| `World/Coasters/CoasterCrossSections.cs`, `CoasterTrackMesh.cs` | The `coaster.sam` cross sections and their extrusion along the curve slices. |
| `World/Coasters/CoasterPylonRig.cs`, `CoasterCarRig.cs` | The pylon models posed with their loft and rotate clips, the car models, their extents, spacing and pose. |
| `World/Level.Coasters.cs`, `Hud/ParkHud.cs` | The coaster tool of the park view. |

## Building

When a coaster station is placed, the HUD starts the coaster tool. The track
leaves the station at its `>` cell. That is the station node.

- **Pylons.** A click places a pylon at the tool's height. The mouse wheel
  changes the height in steps of 10, and Tab switches to loop pylons on coasters
  that have them. The height is clamped to the pylon's `[fMinHeight, fMaxHeight]`,
  never refused.
- **Validation.** The validator `0x36ba4` runs its tests in order: the map
  bounds; a no-ground cell (new pylons only); the pylon control (LoopPylon after
  a loop node or in loop mode); the same cell as the previous node; the squared
  distance window (StdPylon 3 to 8 cells, LoopPylon 3 to 9); the turn window,
  interpolated by distance (entrance limits after the station node); and
  stacking. A failed test does **not** refuse the pylon. It marks the pylon and
  the node before it invalid (flag `0x4000`), and the mark clears when the
  validator passes again. Slope and deviance limits are loaded but never
  checked, as in the original.
- **Blocked cells.** Only cells without ground, as the map stood when the park
  started, block a pylon. Paths, queues, scenery and rides do not. Another
  coaster's pylon does (the stacking test).
- **Money.** Placing a pylon charges `trunc(cost)` at once through
  `ParkEconomy`, even when the balance cannot cover it (the original reads the
  balance and ignores it). The cost is `fCostPerUnit + fCostPer2DLength ×
  length/10 + fCostPerHeight × height/10`; a loop or trough pylon pays the
  StdPylon length and height rates unless it stands on another loop or trough
  pylon. Moving a pylon refunds `trunc` of its old cost and charges `ceil` of the
  new one, both **without** `fCostPerUnit`, together with the next pylon's cost
  on a complete track. A drop on the same cell is free. The height change works
  the same way. Deleting pylons or the whole track refunds nothing, and there is
  no undo stack (Backspace deletes the last pylon without a refund). Track money
  is posted as other costs and income (COAST-031); the original debit `0x4d01f0` also tests the bank's withdrawals
  gate (ECON-051) and keeps the last balance and the turn it entered the red (SAVE-008), which OpenTPW leaves to the
  economy and save owners, and the station's total spent is OpenTPW's own.
- **Closing and completing.** A click on a station end cell closes the circuit
  without adding a node. A second click, or Enter, completes it: one station end
  node with node flags `0x144` is appended (the loader always sets definition bit
  `0x20`, so `bVisibleEndPylon` selects nothing), the curve is fitted, the track
  is rated and the trains are placed. Completion moves no money. It is refused
  while a pylon is marked invalid (COAST-016: `0x37ef0` itself does not test it).

Headless API (tests, the M3/M4 gates):

```csharp
var ride = runtime.Coaster;                               // OriginalObjectRuntime of a coaster station
var builder = new CoasterBuilder( ride, CoasterSite.FromGrid( grid ), () => economy );
builder.SetStation( depart, back );                       // CoasterBuilder.StationEnds( shape, x, y, rotation )
builder.Height = 40;
builder.Place( x, y );                                    // per pylon: charges trunc(cost), marks invalid links
builder.Place( back.X, back.Y );                          // closes the circuit
builder.Complete();                                       // end nodes, curve, rating, trains
// ride.Rating, ride.Trains, ride.Command( n, p ), ride.Advance( dt ), builder.Move / SetHeight / Reopen / Delete
```

`Level` exposes the same through `GetCoasterBuilder`, `EnterCoasterTool`,
`CoasterClick`, `CompleteCoaster` and `DeleteCoasterTrack`.

## Running

The station runs its original script (`Coaster.RSE`). The script's `COAST`
commands reach the station's `CoasterRide`:

| Command | Effect |
| --- | --- |
| `COAST 8` | creates the controller; the speed cap is `80·(1 − s/100) + 130·(s/100)` with the setting `s = 60` (110) |
| `COAST 5` | closes or opens the ride |
| `COAST 6` | the seats of a train |
| `COAST 2` | the free seats of the train at the station (0 when closed, broken or not runnable) |
| `COAST 1` | seats the guest the admission called (`VAR_LETMEON`) in a random car (`CoasterRng`) |
| `COAST 3` | lets one rider off into `VAR_LETMEOFF` |
| `COAST 4` | argument 0, 1 or 2 selects ride mode 16, 64 (the breakdown) or 32; mode 64 sets the power factor to 0, so moving trains coast to a stop |

Guests queue and are admitted by the same machinery as other rides
(`RideVisitorBridge`). Track type 3 skips the capacity test. A coaster that is
not complete and closed takes no guests. For track type 3, the guests' ride
excitement is `trunc(0.5·E + 50)`, where `E` is the coaster's excitement rating. The sickness has no guest consumer
(COAST-013 retired: the guest rating `0x4e0560` fetches it from `0x43e0b0` and discards it). The guest predicate
`0x441970` also tests controller `+0x140 == 0` and that the coaster is not the one being edited (`0x790fec`); OpenTPW has
neither state, and the queue's state 11 gives a failing coaster thought 13 and sends the guest on (not modelled, GUESTS.md).

Trains move in hops of half a sample (`0x40650`). Each hop averages the cars'
speed after the height change to the point half a sample ahead. It then applies
station braking (`x⁴` toward the winch floor past `ctl+308`) or friction (on the
sample the train stood on at the start of the step), the floor (winch speed when
a car's half-sample point is on a chain-lift section, else the minimum speed,
both times the power factor) and the cap. A hop takes `d / ((v + v')/2)`
seconds, and the last hop of a step is partial; its end speed is capped but not
floored, so a train leaving the station at rest builds up speed over several frames. Below speed 2 the speed decays by 0.95 per hop,
and below 0.01 it is 0. The hops run on OpenTPW's fixed 60 Hz simulation step
instead of the original's per-frame elapsed time, which keeps runs reproducible
(COAST-022). Trains leave the station at the release headway
(`uiTrainReleaseRate`) and come back to unload.

The station commands follow the Windows twins (tp.exe `0x43b1f0..0x43b330`, which agree with the Mac readings).
`COAST 5` is gated: argument 0 opens (state 4) only from state 8 or 2, a nonzero argument closes (state 8) only from
state 4; OpenTPW keeps a closed flag, so a repeated command changes nothing, and the original's admission ring (moved
into the leaving ring that `COAST 3` pops when the ride closes) does not exist here because a guest is seated at once.
`COAST 4` maps 0/1/2 to the modes 16/64/32; mode 32 is treated exactly like 16 by the mode setter `0x435730` and nothing
tests it. In mode 64 the station step (`0x43b8b0`) is skipped, so an unloaded train does not move on to boarding until
the power is back (an arriving train unloads open or closed; boarding is state 4 only). `COAST 6` takes
`min(max(request, 0), definition +0x318, first train definition +8)`: the lower bound is the literal 0 on the PC where
the Mac reading takes a global (documented difference, OpenTPW follows the PC); the two upper bounds are not applied
(the seat sockets are not read). `COAST 8` makes the ride script's speed bias (script +0xC0, 50 at creation,
`0x558c3c`) the speed setting through `0x43b2b0`/`0x43c3d0`, so the speed cap is `lerp(maxAtMin, maxAtMax, 0.5)`
instead of the constructor's setting 60; the loop calls the setter every pass, and the bias changes only when an upgrade completes (it then becomes that level's `InitSpeed` when above 0, 0x4DF8F0).
A suspended coaster's roll is the swing of pass `0x43c450` (`0x43c76c..0x43ca45`): the force plus gravity rotated into
the sample frame by −yaw, −pitch and −old roll, approached through `atan2(−X, Y)` at rate `min(|F′|·fForceMultiplier,
1)` and faded to 0 over the braking zone (COAST-018, libm instead of the runtime sine table).

## Rating

The rating pass runs once when the track is completed. It starts at the winch
speed and marks every section whose speed sits on the winch floor as a chain
lift. Each sample's force is `a·(v/10)` with
`a = −((p₋ − p) + (p₊ − p))/spacing`, so a dip gives a negative Fy. The first
sample gets the winch speed and no force, the last copies the one before it.
The step and the final are single precision, with the original's fused decay
and final sum. The step's discomfort term uses Fz when the inverted arm does not
apply, and the final counts the sections with both node flags `0x40` and `0x80`;
node flag `0x80` is never set in OpenTPW, so that count is 0. Both are bit-exact
against the original routines in the PPC differential oracle (ORACLE.md F7, F8;
REVIEW-COAST.md).
`CoasterRating.Step`, `CoasterRating.Restore` and `CoasterRating.FinalFor` are
the seams for the oracle hooks `coaster_rating_step` and `coaster_rating_final`.
The Windows executables (tp.exe and the Theme Park Inc `Game.exe`) differ from this Mac rule in the step's
non-inverted arm, where they add `u` instead of Fz, and in the last bits of S, E and the final excitement
(x87 rounding); C# follows the Mac and is unchanged (ORACLE.md 7.8, X11 and X12).

The train count is `min(uiMaxTrains, max(1, trunc(lap / (uiTrainReleaseRate/1000))))`.

## Determinism

Coasters are in the canonical state hash (schema 6, docs/DETERMINISM.md): the
coaster generator, then every coaster's track, controller state, samples,
rating, trains and riders. The park save holds them all
([PARK-SAVES.md](PARK-SAVES.md)): the track verbatim, the controller scalars
and trains with their riders, and the shared generator through the script
world; the curve samples, rating, winch marks and braking point are re-derived
on load by the same rebuild completion ran, so a loaded coaster continues
bit-for-bit. The original's own coaster chunks between EMAK and SAOC are read
structurally (`SaveCoasterBlock`: each coaster's 32-byte header, auxiliary link
block, 34-byte sections with links, and the train payload as raw bytes; docs/
TPWS-PAYLOAD.md "Coasters block") — proven on the static Mac contract, the
Easymode empty boundary and a labelled Theme Park Inc cross-check — but nothing
is imported into a runtime coaster and nothing is written (COAST-025, SAVE-920
to SAVE-923). `CoasterSections` exposes recognized scene records and attraction
ids joined by cell and `Info.Id`; this does not identify a controller's station.
Nonempty original TPW controllers are refused at `ParkObjects.ImportOriginal`
before any runtime object is created. This includes controllers with no saved
sections: an empty topology is not proof of an empty controller.

## Presentation

The coaster is drawn the way the original builds it (traced in tp.exe: the rebuild `0x43eb70`, the slice emitter
`0x43f4e0` with `0x43fd60`, the cross-section loader `0x42d5a0`/`0x42d6d0`, the select `0x43e9c0`, the model loader
`0x42fad0`, the car setup `0x42f1a0`/`0x430130`, the car pose `0x43ce20`/`0x43d8f0` and the edit step `0x435d50`).

- **Track.** `coaster.sam` holds the track textures (`asTextureData`, with `fScrollRate`), the cross-section point
  blocks (X, Y, U, normal; `fTrackHeightAdjust` is added to every Y) and edge blocks (texture, point, point), the
  sections and the selects. The mesh is the section extruded along slices: the curve at ten uniform steps per knot pair.
  Each point is turned by the slice roll, pitch and yaw and moved to the slice position; U is the point's U and V grows
  by the distance to the previous slice over six, so a texture repeats every six world units. Every edge is a strip of
  quads between two slices. The select picks the section by pitch (a flume's climbing window of -90 to -1 degrees is its
  dry, ratcheted section, the rest is water) and a chain-lift segment uses `uiWinchCrossSection`. Rails, sleepers,
  struts, ropes, gutters and the flume's water all come from these sections and the textures of the coaster's wad
  (`gtexture` first). Scrolling textures (water, ratchet, chain) shift V by their rate per second on their own few
  vertices; everything else is a static model.
- **Building.** The original treats the track under construction as the same chain: the rebuild pairs every node with
  its next node until there is none, and the edit step puts a cursor node at the hovered cell in the chain after the last
  node. OpenTPW draws the curve (an open chain: the end knots take the direction toward or away from their neighbour,
  chord 1.0) through the placed nodes and a ghost pylon at the cursor with the tool's height; a closed but unfinished
  circuit ends in the station end node. An invalid link is flat red.
- **Pylons.** `StdPylon.MD2`/`LoopPylon.MD2` with the loft clip `<name>L` (adds a vertex animation to the pylon mesh and
  a 0 to 80 or 90 position key to the track tag and stack-height dummies, played in "add" mode, which only the TrackMaster
  loader asks for) and the rotate clip `<name>R` (turns the pylon about the cell centre; the parameter is the knot yaw
  over 2π). The pylon stretches until its track tag stands at the node height; outside the loft range it is scaled or sinks.
- **Cars.** The `asCarTypes` meshes, front, centre and rear by `sTrainType`. A car's pivot is its root node; its front
  and rear extents come from the model bounds plus `fFrontDistanceAdd`/`fRearDistanceAdd` and give the spacing; it stands
  on the interpolated sample with roll, pitch and yaw of that point and `fYOffset` along its up axis.
- **Debug.** In a Debug build `--debug-coaster built|open [--debug-coaster-id N] [--debug-coaster-height H]` with
  `--load-original-level` builds a small coaster for captures (`--capture-world`): finished with trains, or open after
  four pylons with the cursor on the fifth cell.

Evidence found but not used: `uiInvalidCrossSection` and the node flags 0x800000/0x804000 mark the invalid presentation;
the pylon loft heights are the tag's height at loft 0 and 1 (15.56 and 95.56 for the Gorilla Thrilla pylon) and so
the true h0 and h1 of COAST-015, which would change the node heights and costs and is left to a follow-up.

## Tests

- `CoasterTests` (no game data): the loader, each validator test with its
  boundaries, invalid marking, the no-ground site, cost and rounding, the money
  of placing, moving, height changes and deletion, closing and completion, the
  rating against oracle-verified values, the train count, the sine table, Bézier,
  chord, knot controls, node tangents, the turn angle and auto-bank against the
  COAST-R2 witness, the speed cap and braking, a run through the station, two
  trains at one station, mode 64, and the hash.
- `CoasterAssetTests` (`OPENTPW_GAME_PATH`): the twelve PC coasters and their
  values; every PC coaster builds, rates and runs; and on a fresh Jungle park
  guests board Temple Of Gloom through its original script, ride a lap and leave.
  Closing lets the trains finish, and deleting removes every pylon.
- `CoasterSaveTests` (no game data) and `CoasterSaveAssetTests`
  (`OPENTPW_GAME_PATH`): the coaster part of the park save — a running coaster
  with riders round-trips through the codec and continues deterministically, an
  unfinished track round-trips and keeps building, corrupted track data is
  refused with a reason, a real station validates against the real catalog, and
  two real stations resume into fresh runtimes and continue deterministically.
  Real guests boarding through the original script stay save-consistent at every
  tick boundary, including the boarding hand-over (a rider seated by `COAST 1`
  one host step before the bridge marks it aboard).
- `OriginalCoasterSaveTests` (no game data for the synthetic part): the original
  save's Coasters block reader — every traced field, truncation inside each part,
  bounds, non-finite floats, out-of-map cells, the two-coaster refusal and the
  park import with cell joins; plus two gated fixtures (the Easymode empty
  boundary, and the Theme Park Inc prebuilt body as a labelled structural
  cross-check).
- `CoasterPresentationTests` (no game data): the cross-section loader and select, the extrusion (vertex count, frame
  orientation, bank, V), an open and a closed unfinished track, the pylon height fit and the car pose and spacing;
  `CoasterPresentationAssetTests` (`OPENTPW_GAME_PATH`): every coaster wad's cross sections, track textures, pylon
  models (the tag reaches the node height) and car models.
- The native smoke test of an original level builds a coaster through HUD clicks
  (pylons, wheel height, close, complete), sees a train leave and the track
  drawn, and removes it.

## Approximation register

`CoasterApproximations` lists every `[APPROX:COAST-NNN]` tag: twenty-four open
IDs (COAST-013 is retired). The IDs below 15 are the unknowns of COAST-plan §10 that are still open.
COAST-015..019 are the new unknowns of §10.1. COAST-022..031 are rules OpenTPW
chose while implementing the plan. COAST-032 is the single-precision arithmetic
of the knot chord, the node tangent and the knot-control norms, where the
original uses fused and double-precision steps. COAST-020 (cost rates) and COAST-021 (force
term) were resolved by the COAST-V review and are traced now. The generated
table is in [FIDELITY-REGISTER.md](FIDELITY-REGISTER.md).

The presentation is traced but still approximated in places: COAST-026 (track mesh details), COAST-033 (pylons),
COAST-034 (cars) and COAST-035 (the build cursor). Rider heads are not seated in the cars yet (the head renderer
takes providers of head sources; a coaster provider reading the cars' seat sockets, `CoasterCarRig.Seats`, is the
next step). The ride camera
is not implemented. `CoasterRide.TryGetRideView` gives the front car's pose
(COAST-011).
