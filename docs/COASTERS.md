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
| `World/Coasters/CoasterRenderer.cs` | Presentation: the track as a box-section tube, the pylons and the cars. |
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
  is posted as other costs and income (COAST-031).
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
excitement is `trunc(0.5·E + 50)`, where `E` is the coaster's excitement rating.

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

The train count is `min(uiMaxTrains, max(1, trunc(lap / (uiTrainReleaseRate/1000))))`.

## Determinism

Coasters are in the canonical state hash (schema 6, docs/DETERMINISM.md): the
coaster generator, then every coaster's track, controller state, samples,
rating, trains and riders. The park save does not hold coasters yet (COAST-025).

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
- The native smoke test of an original level builds a coaster through HUD clicks
  (pylons, wheel height, close, complete), sees a train leave and the track
  drawn, and removes it.

## Approximation register

`CoasterApproximations` lists every `[APPROX:COAST-NNN]` tag: twenty-two open
IDs. The IDs below 15 are the unknowns of COAST-plan §10 that are still open.
COAST-015..019 are the new unknowns of §10.1. COAST-022..031 are rules OpenTPW
chose while implementing the plan. COAST-032 is the single-precision arithmetic
of the knot chord, the node tangent and the knot-control norms, where the
original uses fused and double-precision steps. COAST-020 (cost rates) and COAST-021 (force
term) were resolved by the COAST-V review and are traced now. The generated
table is in [FIDELITY-REGISTER.md](FIDELITY-REGISTER.md).

The track is drawn as a flat-coloured box-section tube along the samples, not
with the original cross sections and pylon meshes (COAST-026). The ride camera
is not implemented. `CoasterRide.TryGetRideView` gives the front car's pose
(COAST-011).
