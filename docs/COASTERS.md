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
| `World/Coasters/CoasterTrack.cs` | Nodes, the station's three nodes, the placement validator `0x36ba4`, invalid marking, closure, completion, the height clamp, the pylon cost, node positions, node tangents, auto-banking and the knots. |
| `World/Coasters/CoasterStationLayout.cs` | The station frame: the End, Mid and Start dummies of the station model (`0x436ac0`) that the three station nodes are built from. |
| `World/Coasters/CoasterCurve.cs` | The curve: Hermite tangents along each knot's direction, one cubic Bézier per knot pair (`0x41b00`), the sampler `0x44ff4` (parameter steps of 0.05, one sample every 3.0 world units), yaw and pitch, and the 4096-entry sine table. `ICoasterCurve` keeps the curve swappable. |
| `World/Coasters/CoasterRating.cs` | The rating pass `0x3fc18`, the step `0x415b4`, the final `0x41900`, the train count and the guests' ride excitement. |
| `World/Coasters/CoasterRide.cs` | The controller of one station: samples, rating, trains, the admission ring, the `COAST` commands of the station script, the train motion `0x43ce20`, the station step `0x43b8b0` and the collision exchange `0x43cd60`. |
| `World/Coasters/CoasterCollision.cs` | The self-intersection pass `0x439ab0` → `0x439c40` → `0x43a2b0`: its count feeds the guest predicate. |
| `World/Coasters/CoasterBuilder.cs` | The headless build API: place, move, height, close, complete, reopen, delete, and the money of each. |
| `World/Coasters/CoasterWorld.cs` | The coasters of one park and the boarding generator `CoasterRng`. It is owned by the park's `RideScriptWorld`. |
| `World/Coasters/CoasterRenderer.cs`, `CoasterAssets.cs` | Presentation: per coaster the track mesh, the pylon models and the cars, rebuilt only when the track or the build cursor changes (see "Presentation" below). |
| `World/Coasters/CoasterCrossSections.cs`, `CoasterTrackMesh.cs` | The `coaster.sam` cross sections and their extrusion along the curve slices. |
| `World/Coasters/CoasterPylonRig.cs`, `CoasterCarRig.cs` | The pylon models posed with their loft and rotate clips, the car models, their extents, spacing and pose. |
| `World/Coasters/CoasterCarBend.cs` | The bend of a `bBendCar` boat's mesh along the samples around it (`0x438800`, `0x438ab0`). |
| `World/Level.Coasters.cs`, `Hud/ParkHud.cs` | The coaster tool of the park view. |

## Building

When a coaster station is placed, the HUD starts the coaster tool. The station
is **three nodes** (COAST-029, `0x436ac0`), built from the three dummies of the station model that carry attribute
`0x400000` (numbered 1, 2, 3: End, Mid, Start; in the 12 PC stations End is half a cell before the `<` column, Start half a
cell past the `>` column and Mid between them, with the placement's rotation applied):

| Node | Where | Node flags | Creation parameters |
| --- | --- | --- | --- |
| S0 (ordinal 0) | a StdPylon in the cell of End: the cell before `<`, outside the footprint | `0x400`, winch, no previous link (`0x80000`) | `0x274` |
| S1 (ordinal 1) | pylon-less, at the exact position of Mid (its knot is the dummy, not a cell centre) | `0x200 \| 0x400`, winch | `0x3c` |
| S2 (ordinal 2) | a StdPylon in the cell of Start: the cell after `>` | `0x200`, no next link (`0x40000`) | `0x28c` |

All three carry descriptor flag 2: they are never picked up, no pylon stacks on them (the bottom-node flags `0x200`/`0x400`
refuse it) and the entrance limits apply after S2. The station pylons are as high as the dummies (`dummy y − the station's
base`), **without** the fMinHeight/fMaxHeight clamp (creation parameter bit 4, `0x433790`), so the loft parameter is
`clamp((h − h0)/(h1 − h0), 0, 1)`; the station's cost belongs to the station. The track leaves at S2, runs through the player's
pylons and returns to S0: the closing click is on S0's cell (OpenTPW also takes a click on the station's own `<` and `>` cells).
S0 leaves straight toward S1, S1 is straight between S0 and S2 and S2 leaves straight along the platform whatever the first
pylon (the tangent routine `0x431660` drops the previous neighbour on `0x80000` and the next on `0x40000`). The station links
S0 → S1 and S1 → S2 are ordinary links of the collision pass.

A station whose dummies lie below the StdPylon's h0 (Ghosta Coasta 2.04 against 4, Scatty Batty 3.5 against 5, Candy Coaster 7.09
against 10) stands on pylons raised to h0, so S0 and S2 sit above the middle knot: the original's knot pass clamps the height
parameter to [0, 1] and writes it back (`0x430ca0`) and stands the node at ground + h0 + t(h1 − h0) (`0x430ed0`). A station placed without its model (a headless test) takes the
same frame from its cells (`CoasterStationLayout.FromCells`, `[EXT:coaster-station-frame]`).

- **Pylons.** A click places a pylon at the tool's height (the last pylon's height; the
  panel's loop button switches to loop pylons on coasters that have them and are upgraded to level 1: the panel enables the button only when the ride's level byte is at least 1, `0x4980D6`, and hides it at level 0, `0x49880F`). The height is clamped to `[max(fMinHeight, h0), fMaxHeight]`,
  never refused. `h0` and `h1` are the heights of the pylon model's track dummy at the two
  loft poses (the dummy the model's attribute record `0x800000` names), so the node height is
  `h0 + t(h1 − h0)` (`0x430590`, `0x433280`). A pylon stacked on another stands the pylon's stack
  offset above it (the stack dummy's height above `h0`, `0x4332c3`). Loop mode places a
  **pair** of loop pylons and then ends: the first has loop rotate 0 and node flags `0xe0`
  (loop, `0x20`, `0x80`), the second loop rotate 0.5 and `0x60` (`0x434153`, `0x435020`); the
  first pylon's tangents and knot sides are mirrored (flag `0x80`), a loop pair counts as a
  section of the rating's final, and both pay the StdPylon length and height rates.
- **Knots.** The knot pass (`0x430ed0`) poses each pylon's model with its clips (yaw and pitch of the node tangent unless
  set by hand, the bank) and takes the node's knots from the posed track tags of its table (`0x4344c0`): the attribute records
  with `0x400000` whose values are the table's base + k, each knot at its tag's position with the tag's forward row as its
  direction (`0x4321c0`; a tag of a pylon turned back is turned back by −π first, `0x4315f0`). A model with one `0x1000000`
  record has one table (the In, Centre and Out tags 3 to 5 units apart on most PC standard pylons, one tag on the Gorilla,
  Skull and Ape pylons and every loop pylon); one with two such records (pylon flag 8, the Temple Of Gloom's) has a
  one-tag table and a three-tag table for the lower nodes of a cell's stack, which the stack pass (`0x432d50`) marks `0x100`
  (no bank). The table's centre is the track dummy's tag: its knot's sample coordinate is the node's sample index (`+0xfc`).
  The sampler walks every knot, the closing pair runs from the end knot to S1's first knot and the station window ends at
  S2's first knot. `fTrackHeightAdjust` lowers only the drawn cross section (`0x42d6d0`); the knots stand at the posed tags.
  Without the pylon model (a definition from settings alone, the headless tests) every node is one knot at its position.
- **Validation.** The validator `0x36ba4` runs its tests in order: the map
  bounds; a no-ground cell (new pylons only); the pylon control (LoopPylon after
  a loop node or in loop mode); the same cell as the previous node; the squared
  distance window (StdPylon 3 to 8 cells, LoopPylon 3 to 9); the turn window,
  interpolated by distance (entrance limits after the station's last node S2); and
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
  is posted as other costs and income (COAST-031); the economy's posting follows the original debit `0x4d01f0`
  (the bank's withdrawals gate, the last balance and the turn it entered the red), and the station's total spent is
  OpenTPW's own.
- **Closing and completing.** A click on the head's cell (S0) or on a station end cell closes the circuit
  without adding a node. A second click, or Enter, completes it: one station end
  node is appended (the loader always sets definition bit `0x20`, so
  `bVisibleEndPylon` selects nothing), the curve is fitted, the track is rated and
  the trains are placed (controller state 2). Completion moves no money. The end node is a pylon-less node **on the knot of
  S0** (the world position of S0's pylon track dummy, `0x435570`), called with the
  creation parameters `0x144`, which the PC node constructor maps to node flags
  `0x80000` and `0x100000` (`0x434060`): it is no loop node, its tangent has no previous neighbour and its next is S1, and
  it starts no link. OpenTPW completes only a closed circuit without invalid marks, in
  two clicks (`[EXT:coaster-completion-click]`); the original completes on one click on any
  cell of type 4, 9 or 10 and refuses nothing (`0x435570`, cell evaluator `0x5345b8`). The closing click is refused while
  the closing link from the last pylon fails the validator or a pylon is still marked invalid: the circuit stays open, the
  hovered closing target is already marked refused, and the HUD says why ("The track can't be completed from the last pylon…",
  "Move the pylons marked red…"). A closed circuit whose closing link fails (closed by an earlier build, or a pylon moved
  after the closing) can never complete and takes no new pylon; entering the tool opens it again
  (`[EXT:coaster-failing-closure-opens]`), so it is built on from its last pylon. The ride window shows such a coaster as
  TRACK INCOMPLETE and greys its door ([UI.md](UI.md)); the reopening guard's coaster test is the guests' predicate
  (complete, not crossing itself, not in the tool, `0x441970`).

Headless API (tests, the M3/M4 gates):

```csharp
var ride = runtime.Coaster;                               // OriginalObjectRuntime of a coaster station
var builder = new CoasterBuilder( ride, CoasterSite.FromGrid( grid ), () => economy );
builder.SetStation( depart, back, layout );               // CoasterBuilder.StationEnds( shape, x, y, rotation ); layout: the model's dummies
builder.Height = 40;
builder.Place( x, y );                                    // per pylon: charges trunc(cost), marks invalid links
builder.Place( back.X, back.Y );                          // closes the circuit
builder.Complete();                                       // end node, curve, rating, trains
// ride.Rating, ride.Trains, ride.Command( n, p ), ride.Advance( dt ), builder.Move / SetHeight / Reopen / Delete
```

`Level` exposes the same through `GetCoasterBuilder`, `EnterCoasterTool`, `CoasterClick` (a click on the
closed circuit's station end completes it) and `DeleteCoasterTrack`; the tool's writes are park commands
(`CoasterCommands.cs`, [COOP.md](COOP.md), "Tools").

## The tool panel

The coaster tool shows the original's slide-out panel (COAST-028, tp.exe `0x4982a0`, `0x446fa0`, `0x445910`, table `0x751530`): a left box with the
build (`cb_track`, control `0x1f`) and loop (`cb_loop`, `0x20`) buttons, a middle box with move (`cb_move`, `0x1b`), height (`cb_loft`, `0x1a`),
stack up (`cb_swapup`, `0x1e`), rotate (`cb_rotate`, `0x1c`), tilt (`cb_incline`, `0x19`) and stack down (`cb_swapdown`, `0x1d`), and the demolish
button (`cb_dellast`, `0x21`) at the right. The rectangles, models and tooltip help ids come from the table; the click help is `0x486b00` with `0x125`
(build), `0x126`/`0x127` (loop), `0x121` (move) and `0x124` (demolish). The tool's entry puts the panel into the HUD arm (`0x4988c0`: `0x4a2590`, message 10 of the arm drawer `0x4a14f0`), so it slides out as the camera and
message panels do: the 675 x 274 root (859, 916) starts at the arm's left less its width less 50, centred on the arm, and moves right by 88 units per 20 ms tick
to left 486, top 1145 (the video's measured offset (−372, +232) was these (−373, +229) within a pixel or three); the tool's exit and the arm's retract button
slide it in, and another panel that takes the arm ends the tool (message `0x1001d8`, `0x4a17dc`). The loop button's model and help follow the flume flag
(`cb_trough`, help `0x134`; else help `0x132`, `0x4987f0`). Its background takes the clicks of the area it covers.

**Way in.** The ride window's track button (`b_track`, control `0x3e2a`, help 10; shown when the object's type record has a track type, `0x4ad53a`) calls `0x4af200`, which sends a coaster
(type 3) to `0x4988c0`: it enters the tool (`0x4474d0`, state 2 for a finished circuit) and selects the first button (`0x4455c0`). The table-driven ride window (`ParkHud.ObjectWindow.cs`) has that
button at its original place and `ParkHud.EditTrack` is its click, so select a coaster, press it, cut, extend, complete.
The build buttons' pylon click is the tool object's release (`0x524960`, mode 0x15: the click sound `0x65`, the placement and the help `0xCD`, at the cell under the pointer at the release); the move, height, rotate and tilt buttons pick a pylon up on the press (`0x524790` in mode 0x16 and the drag class `0x6FEAA0`, `0x446300(1)`) and settle on the release (`0x446300(0)`), as before; a quick right click leaves the tool on its release.
A drag does not fit the controller on every pointer move (the original rebuilds the track each frame): the renderer draws the nodes as they are and the track is fitted, rated and the trains
placed once on the release. Backspace waits for a drag; a drag let go while the game is paused ends there. A save taken while the tool is open lets the ride out of state 2 on load, as leaving the tool would. `CoasterToolPanel` is the headless controller, `ParkHud.CoasterPanel.cs` the pointer side.
One tool is on at a time, as the original has one tool mode: the coaster tool's entry ends every other tool, the track tool's entry ends the coaster tool, and a coaster tool still on next to a tool that started after it ends at the HUD's next frame; the track button on the coaster whose tool is on starts nothing again.

**Shared parks.** In a co-op session the coaster tool works through the coaster's claim, as a kart or water ride's track tool does: one player edits a coaster at a time, a guest's clicks, Backspace, swaps and cuts wait for the host one at a time, and a guest's drags go to the host as their latest values at most every 0.2 s (`[EXT:online-coop-claims]`, `[EXT:online-coop-commands]`; docs/COOP.md, "A guest's coaster tool (C3c-2)"). The claim's end leaves the coaster's tool state as the tool's end does, so no player that leaves leaves a coaster closed. Backspace takes back only the player's own last pylon, placed in that run of the tool and unchanged since (`[EXT:online-coop-undo]`; docs/COOP.md, "The coaster tool (C5-3)").

- **Enable mask** (`0x445910`): the seven edit buttons always; build unless the first pylon of a loop pair waits for its second; loop when the coaster has a
  loop pylon; a finished circuit (complete, not cut open) turns build and loop off, any other track turns demolish off. The tool opens on build (loop while a
  pair is pending) for an unfinished track and on height for a finished one (`0x4455c0`).
- **Build and loop**: a click places a pylon (loop mode places a pair, `0x442cd0`); the cursor cell is a blue or red blueprint face, the cost shows beside
  it, and the **placement area** is the box of the maximum place distance + 1 cells around the last node (`0x434f10`) whose cells pass the validator and
  pass the original's cell test (`0x5243a6`, `Level.IsCoasterAreaCell`: cell flag `0x40` clear, no track tile or fence post, none of the cell types 3, 4, 7, 9, 10 and `0x1e`, no path with flag `0x20`), drawn with the level's own translucent orange texture (`BlueprintMarker.Area`: face type `0x11f` takes slot 2 of the dynamic textures, `0x535670`, which is `orange.wct` of the level's `dynamic.wad`). Hovering the chain start marks it
  as the closing target; a click closes the circuit.
- **Height** (`0x441a00`): a press on a pylon picks it up; the loft parameter follows the pointer at 0.005 per pixel upward within `[max(0, (fMinHeight − h0)/(h1 − h0)),
  t + (fMaxHeight − the stack top's height)/(h1 − h0)]`; a clamped value moves the drag's origin so the pointer has to come back; the release credits
  `trunc(old cost)` and debits `ceil(new cost)` (`0x4d0190`, `0x4d01f0`).
- **Rotate** (`0x441e80`): the yaw (node `+0xE0`, flag `1`) follows the pointer at 0.02 per pixel to the left within the automatic yaw ± `fMaxRotnAngleDeviance`.
- **Tilt** (`0x442010`): the bank (`+0xD8`, flag `4` = no auto-bank, range ±π) and the incline (`+0xDC`, flag `2`) follow the pointer at 0.02 per pixel, the drag turned by the camera's
  heading against the pylon's; a loop pylon (`0x441cd0`) moves its loop rotation at 0.01 per pixel in `[0.15, 0.85]`, the first of a pair in `[−0.35, 0.35]`. Which of these a
  pylon allows follows its model's clips (`0x430590`: L loft, R rotate, I incline, B bank, R2 loop tilt).
- **Move** (`0x446d50`, `0x443340`, `0x445a70`): a press picks a player's pylon up, the node follows the hovered cell (marked invalid when the validator refuses it) and the release drops it,
  settling the money of the node and, on a finished track, the next one; a release over no cell puts it back.
- **Stack up and down** (`0x4423f0`, `0x4425c0`): a release over a pylon swaps it with its neighbour in the cell's stack (stack index and loft parameter), no money moves.
- **Demolish** (`0x446740`, enabled on a finished circuit only): the first release over a pylon starts the range, the second ends it. The nodes between are removed without a refund,
  the controller goes to state 1 (the trains and riders leave) and the build button comes back. S0 counts as the last node, so a cut from a pylon to S0 removes everything after that pylon
  and leaves an unfinished track to **extend** again (build, close on S0, complete); a cut in the middle opens a **gap**: new pylons are inserted into it, the node after the cut is the chain
  start, and a click on it closes the circuit and completes it at once (state 2, `0x442c70`; OpenTPW first requires the closing link and every node to pass, `[EXT:coaster-completion-click]`).
  A loop pair is never split and the same node twice cuts that node out. The cut ends carry `0x10000`/`0x20000` and the knots break there.
- **Keyboard**: Backspace deletes the last pylon (`0x444f30`; the original reaches it only through its undo list, `[EXT:coaster-tool-controls]`); the mouse wheel and Tab are gone (the original has
  no wheel height, the loop button replaces Tab, and a new pylon takes the last pylon's height).
- **Cursor link** (COAST-035, retired; `0x435d50`, `0x43f4e0`): the cursor node is the original's model-less spare node, drawn without a pylon; the
  link into it is drawn in a copy of the invalid cross section (the first section: no PC `coaster.sam` sets `uiInvalidCrossSection`)
  with every edge in `blue.tga`, or `red.tga` when the validator refuses the cell (the marker textures the loader appends after the
  coaster's own, `0x42f390`). A hovered chain start (the closing target) takes the node flag `0x800000`, or `0x804000` when the closing link is refused (`0x4360c4`), and the marker
  textures draw that node's own tag pairs (`0x43f4e0` with both nodes the same); the rebuild `0x43eb70` draws no link into the start. `0x4360c0` also sets `0x200000` on the ends and their neighbours: it is the mark that makes the self-intersection pass `0x439ab0` re-test a node (`0x439b6b`), redundant here because the pass runs over every node. The rebuild `0x43eb70` samples and tests every track it draws, so while a coaster is built (and while a pylon is dragged) a link that crosses the track, the cursor's link included, is drawn with the crossing marker.

Every loop routine of the original (the mode starter `0x442cd0`, the resumption `0x442d10`, the pending test `0x444c00`, the mode setters `0x444c50`/`0x444c80`, the selection `0x4445b0`) first tests the definition's flag 2, which the definition builder `0x42fad0` sets when pylon control 1 (the loop pylon) has a mesh (`0x42fd57`; `CoasterDefinition.HasLoopPylon`): on a coaster without a loop pylon the loop button is still shown and enabled (the panel only looks at the ride's level, `0x4980D6`, `0x4987f0`) and its selection just picks the build mode. The auxiliary dispatcher `0x447a10` is the layout replay's action `0x38` (the replayer `0x785a08` rebuilds a downloaded online park from its recorded actions, `0x4045b0` at `0x40463e`); OpenTPW runs no layout replay. COAST-035 is retired (October 30, 2026).

The loop tool resumes a pending pair from the open chain endpoint after closing/reopening the panel or using an edit button: the second pylon keeps counter 1, its cursor omits `LoopFirst`, and placing it ends loop mode. The endpoint is the incomplete chain's tail or a completed cut circuit's predecessor, not an arbitrary picked pylon. Build stays disabled while that endpoint carries `0x80`, independently of the active tool. Availability and upgrade gates remain in force. See [loop-pair resumption](COASTER-LOOP-RESUMPTION.md) for the static evidence, synthetic coverage and remaining COAST-035 dispatch/loader qualifications.

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
not complete takes no guests. For track type 3, the guests' ride
excitement is `trunc(0.5·E + 50)`, where `E` is the coaster's excitement rating. The sickness has no guest consumer
(COAST-013 retired: the guest rating `0x4e0560` fetches it from `0x43e0b0` and discards it). The guest predicate
`0x441970` tests controller `+0x140 == 0`, which is the **self-intersection count** (not riders on board: its only writers
are the pass `0x439c40` and its inverse `0x43ad50`), that the track is complete and not open, and that the coaster is not the
one being edited (`0x790fec`). `CoasterRide.AcceptsGuests` is that predicate: `BeingEdited` stands for the global `0x790fec`, which the tool's entry (`0x447520` through `0x430ac0`) sets and its exit clears (`Level.EnterCoasterTool`, `ExitCoasterTool`). The choice (`0x4dd920`), the reopen guard and the queue's state 11 read it: a guest queueing for a failing coaster, an edited one included, thinks 13 and is dismissed at its own update (tp.exe `0x500612`, in GuestSimulation), while the join (`0x4ffbc0`) and the ride's own admission (`0x4e1220`) do not read it, so a guest already walking to the coaster joins and an open ride still calls its head forward (`APPROX:COAST-024`, retired).

The count comes from `CoasterCollision` (`0x439ab0` → `0x439c40` → `0x43a2b0`, COAST-004). Every node with a successor has a
box of the samples of its link grown by its pylon's `fCollisionPossibleRadius` (`0x439920`). A link is tested against every link
that shares no node with it (two nodes away, forward and backward): the boxes must overlap, the samples of each link inside the
shared box become segments, and two segments whose boxes overlap are tested as prisms. The five collision points of the
definition (`(4, 8), (4, 0), (0, −3), (−4, −1), (−4, 8)`, no PC `.sam` names others) are posed at both samples of each segment
(roll, pitch, yaw through the PC sine table with truncated indices) and a profile edge of one prism cuts the plane of a side face
of the other when its cut lies inside the face (the four corner angles sum to at least `2π − 0.001`). A crossing puts an entry in
the lists (eight at most) of the two links' first nodes and adds 2 to the count. The original runs this incrementally per edit; here
a pass runs over the finished track at every rebuild.

Trains move in hops of half a sample (`0x43ce20`). Each hop averages the cars'
speed after the height change to the point half a sample ahead. It then applies
station braking (`x⁴` toward the winch floor when a car is past `ctl+0x134`, unless the train is flagged bumped)
or friction (on the sample the train stood on at the start of the step), the floor (winch speed when
a car's half-sample point is on a chain-lift section, else the minimum speed,
both times the power factor) and the cap. A hop takes `d / ((v + v')/2)`
seconds, and the last hop of a step is partial; its end speed is capped but not
floored, so a train at rest builds up speed over several frames. Below speed 2 the speed decays by 0.95 per hop,
and below 0.01 it is 0. The trains of a frame hop one at a time from a queue (`0x43ced8`): the train with the most of the frame's
time left hops next, a full hop puts it back before the first train with no more time left, a partial hop takes it out.

**The frame's time.** The motion tick takes as its frame time the scaled game clock since the controller's clock word `ctl+0x120`,
in whole milliseconds × 0.001 (`0x43ce20`; the clock `0x7b496c` is the one that drives the substep clock, refreshed once per frame
by `0x473440`). It writes the word only at its end, and the first tick of a controller (flag `0x10` clear) sets it first and takes no
time (`0x43ce85`). The controller tick runs the motion tick only in states 2, 4 and 8 (`0x435ac9`), so while a track stands cut or
reopened (state 1) the word stands and the clock runs on. The first tick after the track is completed again takes **all** the time
since the last tick: one tick, no clamp, no substeps, every train hopping from the queue until it has spent it. The game pause
(`0x4092a0`: the pause menu, the map, the postcard, message boxes, the pause key) freezes the clock, so a paused stretch adds nothing.
OpenTPW counts that clock in fixed steps: they stop while the park is paused and multiply with the game speed, as the substep
scheduler's clock does (`CoasterRide.MotionClockLagSeconds`, the steps since the last motion tick). A reopened coaster therefore
catches up exactly as the original does. The catch-up stays cheap in practice: measured on the test circuit, a coaster cut open for six
hours of simulation time catches up in one tick of about 145,000 hops (about 0.1 s). Like the original's float frame time, the
catch-up only stops progressing once a hop is shorter than half the float step of the remaining time, which takes weeks of
simulation time cut open. A restored lag that is not a finite time of 0 or more is refused. What stays OpenTPW's own (`[EXT:coaster-fixed-step]`) is the frame: the original runs one
controller tick per rendered frame with that frame's milliseconds, OpenTPW one per 60 Hz simulation tick of 1/60 s. While the
game clock stands still, the original still runs a zero-time motion tick and the station step on every frame (`0x435a30` is
gated only by the application's activation test at `0x54F8E0`), which caps the speeds and decays the slow trains (×0.95 below 2.0); OpenTPW's paused
park runs no tick. Both depend on the rendered frames, which no reproducible simulation can read.

**The speed setter's gate.** The script's speed word reaches the controller through `0x43b2b0` (`COAST 8` at `0x554ab4` and the slice
tail at `0x5517ea`), which skips a changed word when controller bit `0x8000` is set and no layout replay runs (`0x785a2c`, the action
replayer `0x785a08`, which rebuilds a downloaded online park from its recorded actions). Only the replayed action 0x45 sets the bit
(`0x447e30`: it applies the speed through the same setter and then sets `0x8000`), and the saver does not write it. So a speed a
layout replay set holds against the script's word afterwards. `CoasterRide.SpeedSetByReplay` ports the bit and the gate; OpenTPW
runs no layout replay, so the bit stays clear and the gate stays open.

**The train.** A train's position is its middle car's, car `(n − 1) >> 1` (train `+0x2c`); every other car stands its offset ahead or
behind (`0x43bc20`): the car types' extents (`0x430130`: the model's header bounds around its root node plus `fFrontDistanceAdd` and
`fRearDistanceAdd`) give three pair distances (`0x42fad0`: front type's rear + centre type's front, centre rear + centre front, rear type's
front + centre rear), and going forward from the middle car each car adds the pair its own index selects (front for car 0, rear for the last
car, centre otherwise), going back each car subtracts it. The hop measures its distance on the middle car, the braking blend on car 0, and the
train's span is car 0's offset and front extent to the last car's offset and rear extent. Only the cars of the centre type seat
(`0x43ba90`): a train's capacity, by default its head sockets (the dummy records with flag `0x80` of each car's model, summed over the cars,
definition `+0x2f8`), is split over them; the caterpillar's head and tail carry none. `COAST 6` bounds a request by 0, the fleet's sockets and
one train's sockets, and 0 stands for the train's sockets. The admission ring holds one train's sockets, the leaving ring twice the fleet's.
After every rating pass (`0x43c450`) the trains are placed again (`0x43c1f0`): train 0 keeps its last car where it stood (on a new track
where it last ran, since the idle train slot keeps its cars' coordinates), each next train's last car stands where the walk over the rated
sample speeds from the previous one's reaches `uiTrainReleaseRate`, a train's cars are laid out from the last car forward, and its speed is the
mean rated speed under its cars. Trains below a new count keep their riders, the ones past it give theirs to the leaving ring. Without the car
models (a definition from settings alone) every car type is 3.5 units each side of its pivot with four sockets (`[EXT:coaster-car-fallback]`).

**Trains never stop.** The motion tick has no zero-speed branch (`0x43ce20`): in the station a train's speed blends toward the
chain-lift floor and stays there. The controller tick (`0x435a30`) runs the motion in the built, open and closed states
(`state & 0xE`) and the station step (`0x43b8b0`) only in the open and closed states outside mode 64 (a breakdown). The
station step works on the train's span (front and rear on the sample list, which starts on S0, the start of the platform) and the
window `[ctl+0xf8, ctl+0xfc]` (the sample coordinates of S0 and S2, narrowed toward its middle by `fEntranceSizeInterval`, 0 in every
PC `.sam`; `0x43b750`). The sampler walks every pair from S0 and, at the end node, the closing pair E → S1: it emits **one** sample
there (`0x440c10`; E stands on S0, so the pair repeats S0 → S1), so the list ends just before the platform and the braking zone
`[ctl+0x134, n)` is the stretch before it:

- When a train's span first overlaps the window (flag `0x20` clear) it **unloads**: the riders of the lap go to the leaving ring, whether the ride is open or closed.
- While the ride is open (state 4) the cars that are inside the window **seat** guests from the admission ring: the ring is filled by `COAST 1`, and each guest takes car `(s >> 16) % n + first` of the generator (`s = s·0x343fd + 0x269ec3`) or the first car after it with room; the span of cars grows from car 0 to the last car seen in the window.
- When the span leaves the window the remaining seats are filled, the guests seated at the station become the riders of the lap (every car has two lists that swap) and the flag clears.
- `COAST 2` is the capacity `COAST 6` stored minus the guests on the admission ring, whatever the state or the trains; closing the ride moves the admission ring into the leaving ring (`0x43b390`) and `COAST 3` pops the leaving ring.
- While the ride is closed trains keep circulating empty. In mode 64 the power factor is 0, the floors vanish and the trains coast to a stop; leaving it flags every train `0x80`.

**Headway** comes from the winch floor, halved (`0x43d041..0x43d05d`) for a train with flags `0x80` (since the power came back) and `0x20`
(in the station), and from the collision exchange `0x43cd60`: when the rear of the train ahead (the next index) is behind a train's front
and the train ahead is slower, the two speeds become `(T + 2·O)/3` and `(O + 2·T)/3` and the train ahead is flagged `0x40` (no station
braking until it is past the window). Flag `0x80` clears at the end of the tick when the time to the train ahead (the samples between them
times spacing over the speed of the sample whose index is the train's own index, `0x43d85f`) reaches `uiTrainReleaseRate`. The crash
effect `0x439840` plays its sound (AUDIO.md, "Finishing pass") when the two new speeds differ by more than 1.0: `cat_rides` 0xC at the bump (`CoasterRide.Crashed`); its particles are not reproduced.

The station commands follow the Windows twins (tp.exe `0x43b1f0..0x43b330`, which agree with the Mac readings).
`COAST 5` is gated: argument 0 opens (state 4) only from state 8 or 2, a nonzero argument closes (state 8) only from
state 4, so a repeated command changes nothing and a ride whose track is not finished (state 1) takes neither; closing moves the
admission ring into the leaving ring that `COAST 3` pops.

**Controller states** (COAST-012, `0x435730`): 1 the track is not finished (no trains), 2 built or out of service (the trains circulate
empty, no station step), 4 open, 8 closed. Completion sets 2; the script's `COAST 5` makes it 4 or 8. The coaster tool's entry
(`0x4474d0` → `0x447520`, which round 2 labelled the tool's exit) closes the ride object (`0x4df300`: the guest being loaded is
dropped) and sets state 2 for a finished track with its circuit closed, else 1; leaving the tool opens the ride object again when
the state is 2 (`0x4df390`). The transitions: 1 → 2 sets the trains up, 2 → 1 removes them, 4 → 2 and 8 → 2 unload both seat lists of every
train (8 → 2 then runs the closing move), 1/2/4 → 8 run the closing move, every other pair changes nothing. OpenTPW lets the guests of the
admission ring leave with the riders on 4 → 2: the original does the same, since 4 → 2 and 8 → 2 unload and then fall through to the closing move `0x43b390`
(`0x43587f`, `0x4358c9` → `0x4358cb`). It does nothing on 4 → 1 and 8 → 1, which the tool never reaches (it goes through state 2 first); OpenTPW's headless edits take the trains off there
(`[EXT:coaster-state-clear]`). A finished
coaster entered by the tool keeps its trains (empty) and its pylons can be moved and re-heighted there; extending the circuit goes through the panel's
demolish button, whose cut sets state 1 (see "The tool panel").
`COAST 4` maps 0/1/2 to the modes 16/64/32; mode 32 is treated exactly like 16 by the mode setter `0x435730` and nothing
tests it. In mode 64 the station step (`0x43b8b0`) is skipped, so a train on the platform does not unload until
the power is back (an arriving train unloads open or closed; boarding is state 4 only). `COAST 6` takes
`min(max(request, 0), definition +0x318, first train definition +8)`: the lower bound is the literal 0 on the PC where
the Mac reading takes a global (documented difference, OpenTPW follows the PC); the upper bounds are the fleet's and one train's head
sockets. `COAST 8` makes the ride script's speed bias (script +0xC0, 50 at creation,
`0x558c3c`) the speed setting through `0x43b2b0`/`0x43c3d0`, so the speed cap is `lerp(maxAtMin, maxAtMax, 0.5)`
instead of the constructor's setting 60; the loop calls the setter every pass, and the bias changes only when an upgrade completes (it then becomes that level's `InitSpeed` when above 0, 0x4DF8F0).
A suspended coaster's roll is the swing of pass `0x43c450` (`0x43c76c..0x43ca45`): the force plus gravity rotated into
the sample frame by −yaw, −pitch and −old roll through the runtime sine table (4096 entries, `T[k] = f32(fsin(k/4096 · f32(2π)))`,
scale `f32(4096 · f32(1/2π))`, the index rounded to nearest, `0x4708d0`), approached through `atan2(−X, Y)` at rate
`min(|F′|·fForceMultiplier, 1)` and faded to 0 over the braking zone (COAST-018 retired; the x87 intermediates stay with COAST-032).

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
coaster generator, then every coaster's track (with each node's loop rotate and the middle station node's exact position), controller state
(1, 2, 4 or 8), the station window, the intersection count, samples, rating, trains (position, speed, flags, boarding span, the riders of the lap and the
guests seated at the station per car), the admission ring and the leaving ring. The park save holds them all
([PARK-SAVES.md](PARK-SAVES.md)): the track verbatim, the controller scalars
and trains with their riders, and the shared generator through the script
world; the curve samples, rating, winch marks and braking point are re-derived
on load by the same rebuild completion ran, so a loaded coaster continues
bit-for-bit. The original's own coaster chunks between EMAK and SAOC are read
structurally (`SaveCoasterBlock`: each coaster's 32-byte header, auxiliary link
block, 34-byte sections with links, and the train slots and cars once the coaster
type's train and car counts are known; docs/TPWS-PAYLOAD.md "Coasters block") — proven
on the tp.exe saver and loader, the Easymode empty boundary and a labelled Theme Park
Inc cross-check of the sections — and the block is imported into a runtime coaster with its trains and riders (`OriginalCoasterImport`; SAVE-920 and SAVE-923 are retired, SAVE-921 is narrowed to the track flags' readers); nothing is written. `CoasterSections` exposes recognized scene records and attraction
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
  sections and the selects. The mesh is the section extruded along slices: the curve at ten uniform steps per link between two
  nodes and at the knots alone between the tags of one pylon (`0x43eb70`, `0x43f030`).
  Each point is turned by the slice roll, pitch and yaw and moved to the slice position; U is the point's U and V grows
  by the distance to the previous slice over six, so a texture repeats every six world units. A slice is one ring of vertices
  the strips before and after it share (emitted again where the section changes); every edge is a strip of two single-sided
  triangles per segment, wound as the emitter winds them (the diagonal alternating edge by edge). A link whose node has
  crossings is drawn with the red marker texture in the invalid cross section, as is the cursor's refused link. The select picks the section by pitch (a flume's climbing window of -90 to -1 degrees is its
  dry, ratcheted section, the rest is water) and a chain-lift segment uses `uiWinchCrossSection`. Rails, sleepers,
  struts, ropes, gutters and the flume's water all come from these sections and the textures of the coaster's wad
  (`gtexture` first). Scrolling textures (water, ratchet, chain) shift V by the power factor times the elapsed seconds on their own few
  vertices, the same shift for every scrolling texture as in the controller tick `0x435adf` (a breakdown stops them); everything
  else is a static model.
- **Building.** The original treats the track under construction as the same chain: the rebuild pairs every node with
  its next node until there is none, and the edit step puts a cursor node at the hovered cell in the chain after the last
  node. OpenTPW draws the curve (an open chain: the end knots take the direction toward or away from their neighbour,
  chord 1.0) through the placed nodes and a ghost pylon at the cursor with the tool's height; a closed but unfinished
  circuit ends in the station end node. A link between two nodes a kept edit left invalid takes the red marker.
- **Pylons.** `StdPylon.MD2`/`LoopPylon.MD2` with five clips played in "add" mode, which only the TrackMaster loader asks for
  (`0x430ca0`; a position or vertex key is added, a rotation key multiplied into the node, `0x470d10`): the loft clip `<name>L`
  (a vertex animation of the pylon mesh and a 0 to 80 or 90 position key on the track tag and stack-height dummies) at the node's
  height parameter clamped to [0, 1], so outside the loft range the pylon stops at the loft's end; the rotate clip `<name>R`
  (`R1`) at the yaw over 2π; the incline clip `<name>I` at the pitch over 2π and the bank clip `<name>B` at the bank over 2π
  (wrapped into [−½, ½)), both centred on ½; and a loop pylon's `<name>R2` at its loop rotate parameter. Each value goes through
  the pylon control's map `f…AnimMultiplier`/`Add` and the caps `f…AnimStartCap`/`EndCap` (defaults 1, 0, 0, 1; the PC sets
  `fInclineAnimMultiplier` 4.5, 3 or −3 and `fBankAnimMultiplier` −1 or −4.5). The yaw and pitch are the node tangent's
  (`0x43e590`) unless set by hand, the bank the auto-bank or the hand-set one. A suspended coaster's pylon on the outside of a
  turn takes the rotate clip half a turn on (node flag `0x10000000`), which a straight or loop-pylon node keeps from its last
  curve pass. Every mesh of the model is drawn, the ground tile too, except the stack parts the pylon's place in its cell's stack
  hides (`0x432df0`; the first four `0x80000` records by value): part 1 (the bridge a pylon above stands on) only under a pylon,
  part 2 only on the top, part 3 (the connector onto a pylon below) only on a stacked pylon, part 4 only on the ground.
- **Cars.** The `asCarTypes` meshes, front, centre and rear by `sTrainType`. A car's pivot is its root node; its front
  and rear extents come from the model's header bounds plus `fFrontDistanceAdd`/`fRearDistanceAdd`, the train's pair distances
  space the cars around its middle car (see "The train"), and a car stands on the curve itself at its coordinate (`0x43c0a0`:
  the knot pair whose knot coordinates enclose it, at t = (c − c₀)/(c₁ − c₀)) with the roll, pitch and yaw of that point and
  `fYOffset` along its up axis; the model's +Z runs along the track (`0x43d8f0`), the pose of the root node replacing the model's own
  root rest (the mine carts', the Candy cars' and the caterpillar's roots are turned half a circle, which would face them backward).
  A car of a type without `bBendCar` plays its model's clip `<model>m` (`cartm`, `carm`: the texture coordinates of the four wheel
  meshes) at 0.1 × the train's speed (`0x43ce20` → `0x474050`); the two flumes' boats (`bBendCar`) bend instead, every vertex of the
  mesh taking the yaw and pitch of the track at its distance along the car relative to the car's heading and rising by
  `fBendElevationLow` (`fBendElevationHigh` on a chain-lift sample) (`0x438800`, `0x438ab0`, `CoasterCarBend`).
- **Debug.** In a Debug build `--debug-coaster built|open [--debug-coaster-id N] [--debug-coaster-height H]` with
  `--load-original-level` builds a small coaster for captures (`--capture-world`): finished with trains, or open after
  four pylons with the cursor on the fifth cell.

Used since the register round 2 (a model whose track dummy is missing or does not move with the loft clip keeps the settings' heights, which no PC model needs since the clips turn the nodes relatively; the original applies them as they come): the pylon loft heights, the track dummy's height at loft 0 and 1 (15.56 and 95.56 for the Gorilla Thrilla pylon): they are the
node heights h0 and h1, read when the coaster's definition is built from its catalogue entry
(`CoasterDefinition.FromCatalogEntry`; a definition built from settings alone keeps fMinHeight and fMaxHeight). The model's attribute
records name the track dummy (the node of the first record with `0x800000`: record i belongs to the node stored as header `+0x46` + i,
`0x44ab90`) and the stack dummy (record `0x100000`); for the Skull coaster's standard pylon that is its `TrackCentreDummy` (10 to 100,
the dummy its loft, incline and bank clips move) with the stack dummy `TrackHeightDummy` 20 above it.

## Tests

- `CoasterTests` (no game data): the loader, each validator test with its
  boundaries, invalid marking, the no-ground site, cost and rounding, the money
  of placing, moving, height changes and deletion, closing and completion, the
  rating against oracle-verified values, the train count, the sine tables (Mac and PC),
  Bézier, chord, knot controls, node tangents, the turn angle and auto-bank against the
  COAST-R2 witness, the speed cap and braking, a run through the station, mode 64, and the hash.
- `CoasterStationTests` (no game data): the three station nodes with the original's flags, the no-clamp station height, the exact middle
  knot and the end node on S0's knot with the straight tangents, the refusal to stack on a station pylon, the closing pair's single
  sample and the window from S0 to S2, a track over the platform counted by the collision pass, and the controller states: completion,
  the tool's entry on a finished and an unfinished coaster (riders and the admission ring leave, trains run empty without a station step,
  no guest is stranded) and the reopened and deleted track.
- `CoasterDispatchTests` (no game data): the station window, trains that never stop, boarding only while the
  ride is open, unloading in any state, the seat recurrence and the admission ring, the halved winch floor and the headway
  flag, and the collision exchange.
- `CoasterCollisionTests` (no game data): crossing, missing and fly-over links, adjacent links, the eight-entry lists and the
  overflow flag, the pose with the truncating table, a completed figure-8 track whose count refuses guests.
- `CoasterPylonRuleTests` (no game data): the loop pair (flags, rotates, cost, mirrored tangent and knot sides, the rating's
  counted section), the stack offset, the loft keys, and the older save's end node flags.
- `CoasterAssetTests` (`OPENTPW_GAME_PATH`): the twelve PC coasters and their
  values; the pylon heights and stack offsets from the models; the three station dummies of every station model in all four rotations (the
  frame's cells lie beyond the `<` and `>` cells); every PC coaster builds, rates and runs; and on a fresh Jungle park
  guests board Temple Of Gloom through its original script, ride a lap and leave (boarded equals released).
  Closing makes the trains unload on arrival while they keep circulating, and deleting removes every pylon.
- `CoasterSaveTests` (no game data) and `CoasterSaveAssetTests`
  (`OPENTPW_GAME_PATH`): the coaster part of the park save — a running coaster
  with riders round-trips through the codec and continues deterministically, an
  unfinished track round-trips and keeps building, corrupted track data is
  refused with a reason, a real station validates against the real catalog, and
  two real stations resume into fresh runtimes and continue deterministically.
  Real guests boarding through the original script stay save-consistent at every
  tick boundary, including the boarding hand-over (a guest on the admission ring after `COAST 1`
  one host step before the bridge marks it aboard). A save from before the station rule (trains with a
  stop-and-dispatch state and boarding clock, no flags, no admission ring, an end node with flags `0x144`, no loop rotate) still
  loads: a boarding train keeps its guests seated at the station, an unloading one hands its riders to the leaving ring. The older saves were
  rated with the earlier pylon heights, so the re-derived rating can place another number of trains or cars (coasters 1190, 2180, 2190, 3190 and
  4185 at height 20): the new placement then stands and every saved rider waits to leave, nobody is dropped; a save of the current shape with a wrong count is refused.
  A save made with the two-node station (one station node at the `>` cell, the end node at the `<` cell) migrates on load: the placement's three
  nodes stay, the saved pylons and circuit flags follow them, the end node returns onto S0, the whole circuit is sampled again and, because the
  new train placement is another one, the riders of the old trains wait to leave (the admission ring is kept).
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
- `CoasterRollTests` (no game data): the second rail's roll, the second rail's points, the sample roll that is the second rail's alone and the slice,
  car and suspended-coaster frame rolls; `CoasterCarBendTests` (no game data): the bend tables on straight, curved and crested tracks and the vertex bend.
- The native smoke test of an original level builds a coaster through HUD clicks
  (pylons, close, complete), sees a train leave and the track drawn, and cuts it out
  through the panel's demolish button.
- `CoasterPanelTests` (no game data): the enable mask and the loop pair, the height, rotate, tilt, loop-tilt and move drags (rates, ranges, clamps, money),
  the stack swaps, the cut to the end that reopens a finished circuit so it is extended, completed and every boarded guest is released, the cut in the middle and
  its gap, the loop pair rule, the save round trip, the placement area and the cursor.

## Approximation register

`CoasterApproximations` lists every `[APPROX:COAST-NNN]` tag: ten open
IDs. COAST-022 (the train layout, the hop queue and the placement by the release time) and COAST-023 (the seat types, COAST 6's bounds
and the ring sizes) were retired by the coaster train round of October 15 (`[EXT:coaster-fixed-step]`, `[EXT:coaster-car-fallback]` and
`[EXT:coaster-full-ring]` are its deliberate differences). COAST-012 (the controller states and the tool's entry and exit) and COAST-019 (the sampler's closing pair) were retired by the
three-node station round of October 15, which narrowed COAST-004 (the platform links are ordinary links now) and COAST-029 (to the
stations below the pylon's h0 and the middle knot's height adjust) and took the station clauses out of COAST-014, 015, 024 and 030
(`[EXT:coaster-state-clear]` is the one deliberate difference of the states). COAST-013 (the sickness has no guest consumer) was retired by the register round of October 14 and COAST-007
(the distance waiver exempts the station's own internal nodes), COAST-016 (the completion click), COAST-018 (the swing's
sine table) were retired by round 2; COAST-025 (own-format save), COAST-027 (generator seed) and COAST-031 (the station's total
spent) became extensions (`[EXT:coaster-own-save]`, `[EXT:coaster-rng-seed]`, `[EXT:coaster-ledger]`) and COAST-016 the extension
`[EXT:coaster-completion-click]` (APPROX:COAST-007, APPROX:COAST-016, APPROX:COAST-018, APPROX:COAST-025, APPROX:COAST-027 and
APPROX:COAST-031 no longer exist). The IDs below 15 are the unknowns of COAST-plan §10 that are still open, narrowed to what
the PC executable did not settle. COAST-015..019 are the new unknowns of §10.1, COAST-024..030 are rules OpenTPW
chose while implementing the plan, now narrowed. COAST-032 is the arithmetic precision of the knot chord, the node tangent,
the knot-control norms and the swing, where the original uses x87 steps with extended intermediates. COAST-020 (cost rates) and COAST-021 (force
term) were resolved by the COAST-V review and are traced now. The generated
table is in the [fidelity register](https://opentpw.io/docs/FIDELITY-REGISTER.html) (generated by `tools/fidelity_register.py`).

The coaster tool panel round of October 15 narrowed COAST-028 (the panel, every sub-mode handler, the drag rates and ranges and the placement area's box are traced and built; the pointer's pixel scale,
picking by cell, the area's occupancy test, the guessed clip angles of the hand-set yaw and incline and the panel's window position stay approximations) and COAST-035 (the cursor node and its flags
are built as a translucent ribbon; the original's renderer for the flags is not found) and the cut-end flags of COAST-030.

The read round of October 15 (tp.exe) narrowed COAST-004 (the collision count +0xB0 is read by the section builder `0x43F4E0`, which draws a
crossing link with material variant 2; flag 0x400000 at `0x4376CC` is the coaster save writer's), COAST-014 (the PC cost `0x447830` is the Mac
formula with x87 steps scaled by 0.1), COAST-032 (tp.exe sets single precision only around the renderer `0x576EC0`; the simulation runs with
the start-up or 64-bit precision) and COAST-035 (the section builder recolours a link into a node with 0x800000/0x2000, or 0x4000); the cursor now keeps its last cell
beyond the 300-unit reach of `0x435D50`, as the original does.

The presentation is traced but still approximated in places: COAST-026 (track mesh details), COAST-033 (pylons),
COAST-034 (cars) and COAST-035 (the build cursor's ribbon). A rider's head hangs from its car's socket of its index in the
car's seated list (`0x43b600` → `0x44b410`: socket j is the node of the car model's (j + 1)-th attribute record with flag `0x80`,
`CoasterCarRig.Seats`). The ride view (`RideCameraMode`) rides the car `0x436880` selects: the train with the smallest reference
position (controller `+0x124`, `0x43d7dd`) and its car 0, or car 1 behind a front car of another type than the seats' (train `+0x34`, `0x436f15`), on that
car model's camera seat; `CoasterRide.TryGetRideView` gives the same car's pose. `FIRSTPERSONVIEWDISTANCE`, `COASTERSMOOTHNESS` and
`Info.FirstPersonSprites` have no reader in tp.exe that was found (COAST-011).

The wheel-lifecycle slice of October 18 moves the looping wheel clip's start time from the first renderer update to train-slot
setup (`0x43be10`: id 5, sub 0, loop flag 1, initial speed 1.0). The rating pass sets up only newly activated slots
(`0x43cad6`); existing slots keep their start through re-rating and speed changes. The state setter's incomplete → built
transition (`0x435730`, 1 → 2) starts every slot again, while opening, closing, entering the tool, changing capacity and
changing power do not. A replacement renderer reads the same live slot epoch; a removed and reactivated slot gets a fresh
one. The existing `WheelTick` arithmetic and its current-speed multiplier are unchanged. This is presentation-only state,
excluded from the simulation hash and save format: restoring a park initializes fresh clips at the restored controller time,
without promising wheel-phase continuity across saves. COAST-034 was narrowed by the presentation follow-up of October 30 (below) to the untraced flume
clip invocation. CPU phase-query and private-asset UV fixtures cover this slice; they do not establish
GPU or full original-game visual parity.

The pylon tag round of October 16 built the knot pass over the pylon models' posed track tags (COAST-015 narrowed to the
knot pass's write-back of the clamped height parameter and the no-model fallback; COAST-028's guessed clip angles apply only
without the model; COAST-029 lost its middle-knot height-adjust clause, the knots taking no `fTrackHeightAdjust`). Saves record
the knot pass (`KnotPass`): a save from before it places the trains of a coaster with pylon models again and its riders leave. The same round traced the
stack parts (`0x432df0`) and the kept half turn (COAST-033 narrowed), the mesh's slices, shared rings, winding and marker links
(COAST-026 narrowed to the cross-section runs and blends, lighting and the Add-mode UV animation; the runs, blends and UV keys were built on October 30) and the cursor link (COAST-035
narrowed: the ribbon is gone; the closing target's marks are not set). COAST-015 is retired (October 30, 2026): the only difference left, the pylon heights of a definition built without game data (tests and tools), is `[EXT:coaster-pylon-fallback]`, user-approved.

The coaster rest round of October 16 settled COAST-017 and COAST-030 and narrowed COAST-011 and COAST-034. COAST-017: a sample's roll is the
angle of the second rail's point about the sample's tangent (`0x440c10`), a slice's or a car frame's adds the lerp of the nodes' tag rolls
by the fraction of the nodes' ordinals' lerp (`0x43e6b0`), and a suspended coaster's frames lerp the swing's sample rolls instead; the second
rail stands one unit above every knot, two units along the tag's up row on a loop pylon and at (−sin a, 0, −cos a) on a model-less loop node
(`0x430ed0`), fitted with its own chords (`0x4321c0`). The node's tag roll is written only for a posed pylon without node flag `0x40`, so the
rating step reads sample rolls that are zero up to the sag of the sampler's chords where the nodes' banks used to enter; the coaster hash
content changes with the rolls (the run pin does not). COAST-030: no edit is refused for riders any more (`0x446300`, `0x446740` have no such
test; the tool's entry already put the controller in state 2), the move validator skips the turn test across the cut of a reopened circuit
(`0x4348af..0x4348d8`) and stops re-validating at a cut end (`0x434e06`, `0x434ea1`), and the close step OpenTPW keeps for a fresh circuit
is the extension `[EXT:coaster-closing-check]` (the original closes a reopened circuit by a click without a test and completes a fresh one
by `0x435570`); APPROX:COAST-030 and APPROX:COAST-017 no longer exist.

The second read round of October 16 narrowed COAST-004 (the candidate readers of the node's collision list `+0xB4`: loads at `0x430867`, `0x43420A`, `0x43B159`, `0x43B3E5`, `0x43B63F`,
`0x443087`, `0x44309C` and `0x44ACE9`, not decoded), COAST-011 (the option table `0x7442C0` has 0x3C-byte named entries and no instruction addresses an entry; its only reference is its start in the
stub at `0x402BFD`, so a reader reads fields a generic key loader filled), COAST-014 (the PC's sampler `0x440C10` rounds its sine-table indices with `fistp` at `0x44123F..0x441271`, as the
knot pass does at `0x4313A5`; C#'s curve keeps two truncating Mac-table lookups), COAST-015 (the knot pass `0x430CA0` clamps the loft parameter and writes it back, which every node built here
already has: `ClampHeight`, `StationHeight`), COAST-029 (the PC rating pass `0x43C450` only sets the node's winch mark `0x800` on a floor speed) and COAST-032 (tp.exe's `SetCooperativeLevel` calls
`0x563914` and `0x563B92` carry `DDSCL_FPUSETUP` without `FPUPRESERVE`, so DirectDraw sets single precision for the thread; the control word itself is not read back).

The fidelity round of October 18 (tp.exe) retired COAST-029 and narrowed COAST-004, 011, 014, 028, 032, 033, 034 and 035. COAST-029: the rebuild
`0x43eb70` runs the knot pass `0x430ed0`, then `0x432b50`, which clears the winch mark `0x800` on every node without `0x1000`, then the sampler
`0x440c10` and the rating pass `0x43c450`, which sets it again on a floor speed; the creation parameter `0x20` of S0 and S1 gives them `0x1800`, so
the two keep their mark (`CoasterNodeFlags.WinchKept`; a save from before it gets the pair again on load). COAST-004: the pass re-tests only the nodes
marked `0x200000`; a node dropped out of an overflowed holder's list re-marks the holder (`0x43ad50`), so the pass repeats until no mark is left, the cut-end
skip `0x10000` is built, and the rebuild runs the sampler and the pass on an unfinished chain too, so the build cursor's link and a dragged pylon show the
crossing marker live (`CoasterRide.ChainCrossings`). COAST-014: the sampler's sample angles (`0x4411db` -> `0x43e590`) and the loop knot factor (`0x432430`)
round the sine-table index to the nearest (the Mac lookups truncated); only the x87 arithmetic (COAST-032) is left. COAST-028: the placement area's cell test
(`Level.IsCoasterAreaCell`, `0x5243a6` with the predicates `0x535db0`, `0x536310`..`0x536450`) and the tilt drag's camera yaw (`0x443371`, the park camera's
`OriginalYaw`) are the original's. COAST-011: the options table's accessor `0x402bf0` is a virtual method (vtable `0x6fd630`), which explains why no static reader
was found.

The fidelity round 8 of October 18 (tp.exe, coasters-2) retired COAST-011 and narrowed COAST-014 and COAST-032. COAST-011: the options table `0x7442C0` has a single static reader, the virtual
accessor `0x402bf0` (vtable `0x6fd630`), which only the options instance's own loader and initialiser use: the instance `0x785868` is built by `0x401030` and filled by the loader `0x401650`/`0x4017a0`, and no
code reads the value slots of COASTERSMOOTHNESS (`0x7858C0`) or `Info.FirstPersonSprites` (`0x78596C`), and FIRSTPERSONVIEWDISTANCE (`0x7858B4`) is read only by the option apply `0x44DB60`, which turns it into the fog range and far plane of every view (UI-107), so the ride view's fixed parameters are the original's. COAST-032: the thread's x87 precision is 24 bits
(DDSCL_FPUSETUP at `0x563914`/`0x563B92`, then the shipped D3DIM device creation sets the control word 0x7F and nothing restores it), so every PC multiply, add, subtract and square root
rounds to a single, which C#'s single arithmetic does one operation at a time. The pylon cost (`0x447830`, `0x4346a0`), the suspended swing (`0x43C76C`), its cosine index (`0x43C7D0`) and the loop
knot factor (`0x432430`) are now built from the instruction sequences (`fpatan` and `fmod` are exact, the subtractions with the double constants round to a single); the knot chord, the knot norms
and tangents and the self-intersection pass are still to be read on the PC side.

The presentation follow-up of October 30 (round 10, package N) built the track mesh's run table and compared the bend tables. **COAST-026:** the
section of a normal link is read from the run table `0x4402D0` builds over the sampled track (steps of 0.25 on the sample coordinate, the select
table `0x43E9C0` with its speed, direction, pitch and bank windows in radians, a run written at every change of the answer), and a type whose sections blend (the Jungle
flume: sections 3 and 4 blend point blocks 2 and 3 over +1 and -1 sample coordinates) adds the transition runs: a positive length opens the blend after the change,
a length of zero or below puts it before the change, and two blends that meet share the stretch by their lengths (`CoasterExtrusionRunBuilder`). The slice lists `0x43F030` hold
every run key strictly inside two neighbouring values, a ring is emitted again when the run moves, and a transition run's rings blend the section's points to its
blend-to row by (coordinate - previous run's position) / (this run's position - previous run's position) x weight, plus 1 - weight for a positive length
(`CoasterTrackMesh.BlendWeight`, `0x43FD60`). The run table is compared bit-exactly with isolated tp.exe runs (ORACLE X42, `CoasterRunsGolden`: non-suspended chains of single-knot nodes with zero rolls only); a chain the
demolish button cut open has no sample list here and keeps the per-segment pitch selection. **COAST-034:** the bend car's tables `0x438AB0` are compared bit-exactly
with tp.exe (ORACLE X43, `CoasterBendGolden`; the PC acos at single precision was an ulp off); the bend writes only the body mesh's vertices, so a rider's head socket
stays unbent as ported; the Croc and Maggot boats' vertices all lie inside their tables. The boats' own clips (no start behind the bend flag was found) and the widening of a bend
type's model bounds (`0x4302AA..0x43041A`) stay open.
The track mesh's vertices carry the texture's lighting as an assumption: a texture with `bIsSelfIlluminating` gets the texture flag 4 (`0x42f390`: 2, plus 4 for a nonzero flag word before the scroll rate, plus `0x20` when it scrolls) and the three marker textures flags 7, and their corners take the renderer's self-lit vertex flag (`TextureSlotAddressing.SelfLitFlag`, `CoasterRenderer.TrackVertex`); a self-lit texture named like a light source (`IsLightSource`) also takes the evening extension's light-source flag, the markers do not. That the original's dynamic model draw reads texture flag 4 as "constant colour instead of the scene light" is **not traced** (the only traced unlit path, `0x580C51`, tests the model material word `+0xC` bit `0x1`); it stays in COAST-026. The shader (native and the web JSON build) already honours both vertex flags.
The pylons' loft, incline and bank clips carry texture-coordinate keys (record flag `0x10000`: the Jungle pylon's loft adds 14.985 to the V of its top corners over the
clip); the add mode adds them to the stored coordinates (`0x4745C0` through `0x471860`, `ModelUvAnimation.AddTo`), so a lofted pylon's coordinates run past 1 along its height. Whether they wrap or clamp follows the shared texture's accumulated flags: the texture cache `0x575870` keeps one object per (directory, name),
the slot setup `0x575E10` and the rebinding `0x575C60` OR the wrap bits into it for good (nothing clears them, `0x590FA0` clears only 0x4000), so a clamping slot wraps as soon as any model of the ride
(the other pylon, the P* previews) wraps the same texture (`CoasterAssets.WrapNames`, `ASlotThatClampsWrapsWhenAnotherModelOfTheRideWrapsTheSameTexture`). Open: whether a preview's texture object outlives the build menu.
