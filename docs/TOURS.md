# Tour rides

The four original tour rides (Jurassic Tours `jungle/rides/tourride`, Flightmare Tours `hallow/rides/tourride`, Star Tours `space/rides/scitour`, Tweety Tours
`fantasy/rides/twetours`) take and release riders through the `TOUR` opcode. Rules are traced from the Windows `tp.exe` (`TPW-EXE`, through the shared REA
session over the decrypted executable); every approximation carries a `TOUR-` ID in [the register](https://opentpw.io/docs/FIDELITY-REGISTER.html).

Code: `source/OpenTPW/World/Tours/` (`TourController`, `TourCar`, `TourWorld`, `TourSave`), the `TOUR` routing in `OriginalObjectRuntime.Tours.cs`, the launch pad in
`OriginalObject.ComputeTourStation`, `Level.Tours.cs`.

## What a tour ride is (Q1)

There is **no track and no build tool**. The ride is placed like any ride; its `.sam` has `Bumper.WhichTrackType 0` and `Info.RunsContinuously 1`. The controller
is the flying-car module (`FLY_`): the dispatcher at `[BIN:TPW-EXE:0x005542FE]` reaches a 99-slot table of 0x122C-byte controllers (`0x008791F8`), each holding 20 cars
of 0xE4 bytes. A car is a free-flying vehicle (the ride's supplemental mesh 0: a bird, balloon, craft or kite carrying the riders on its head nodes 1..9) that lifts off from the
ride's launch pad (model node of class 0x800, number 99), flies a random walk over the park's lattice for the ride's duration at a height that clears the ground, comes
back, circles the station until the landing slot is free and lands. Everything is integer maths on 3000 units per cell (300 per model unit) and 4096 angle units per turn,
with a 4096-entry sine table (`0.5 + 1024 sin`, truncated) and fixed flight bounds x 70000..190000, z 30000..140000.

## TOUR commands (Q2)

Jump table `0x005569D0`, command − 1 indexes it; the first argument of every handler is the controller id kept in the script's +0x9C.

| Cmd | Handler | Meaning |
| --- | --- | --- |
| 1 | 0x0055A620 | create the controller and its first car (the script operand is the "small" flag, always 0) |
| 2 | 0x0055D3D0 | destroy |
| 3 | 0x0055D830 | board a guest id into the car standing at the station (state 1, count not negative) |
| 4 | 0x0055D610 | hand out the next rider of the station car that is unloading, newest seat first; 0 when none (output variable and flags) |
| 5 | 0x0055DCF0 | number of cars requested (applies at once while open) |
| 8 | 0x0055D950 | flight duration: operand x 1000 / 64 ticks; running flights are cut to it |
| 9 | 0x0055DA70 | condition 1 fine, 2 worn, 3 broken (first car breaks, flight ends), 4 as 3 and flying riders thrown off |
| 10 | 0x0055DD20 | **ready test**: a car stands at the station (flags) |
| 11 | 0x0055DD80 | **depart**: needs a ready car and an open ride, riders or several cars; clears ready |
| 12 | 0x0055DDD0 | approach mode (the scripts set 1: take off and land along the station heading) |
| 14 | 0x0055D9F0 | open / close (close: one car wanted, flights end, a station car with riders starts unloading) |
| 15 | 0x0055DD40 | the first car is broken and standing at the station |
| 16 | 0x0055DE30 | running: anybody aboard, no car ready, or more than one car (output variable and flags) |
| 17 / 18 | 0x0055DDF0 / 0x0055DE10 | car-avoidance scale and bank scale (1024 = 1.0; Flightmare sets 2048) |
| 6, 7, 13 | | refused with a log line; no script issues them |

## Cars, boarding, end of a run (Q3)

The per-tick state machine is `[BIN:TPW-EXE:0x0055ABF0]`, called from `0x0054F5EF` on **every second** world step: the world loop (`0x0054E360`) advances its clock by 31 ms and counts the
steps (`[0x00877D34]`, raised at `0x0054F4D3`), and the tour tick sits behind `test al, 1` on that counter (`0x0054F5D7`), so it runs once per 62 ms; OpenTPW runs it in the park's substep scheduler on the even passes (`SimulationSubstep`, DETERMINISM.md "The substep scheduler"), after the script pass and not from the objects' own tick. The draw divides the time since that tick by the
same 62 ms (`0x00700F90` is 1/62), and TOUR 8's `operand x 1000 / 64` ticks are about `operand` seconds at that rate. `TourController.TickSeconds` is 0.062. Car states: 1 docked (loading, or unloading when its
rider count is negative), 2 take-off, 3 tour, 4 return, 5 staging (fixed approach), 6 approach, 7 landing, 8 patrol, 9 leaving. A new car appears outside the flight area and
circles the station (state 8) until the landing slot (+0x2C) is free. The first car of a ride is created docked and sets the ready flag.

A run: the script waits for `TOUR 10`, unloads with `TOUR 4` (`WALKOFF`/`WALKGET` on Star and Tweety), loads with `TOUR 3` until the car is full or ten seconds passed since the last
boarding, then `TOUR 11` and `TOUR 8`. The car takes off (the first target 20000 units along the heading at least 14000 up), flies lattice targets (`0x0055C350`: continue
the direction, turn aside when the ground ahead is above the car, targets height max(12000, ground + 5000)) until its timer ends, returns, patrols the four corners 25000 units out
(`0x0055C270`), claims the landing slot, flies the approach line and descends; on landing the rider count turns negative and the script unloads. Cars avoid each other (slower car
loses a sixteenth of its speed, the lower one climbs) and clear stations by 30000. The ground they clear is the original's cell lookup `0x00452AE0`: the height map of the object that
owns the cell (the `.hmp` byte x 100/255 plus the object's base height, see below), otherwise the terrain.

**Closing the ride (TOUR 14 0)** touches only the wanted count, the flight timers, a station car's rider count (it turns negative) and the open word (`0x0055D9F0`); the ready flag is left as it
was. A departure the script had ordered (TOUR 11 clears ready) that the closing stops before the lift-off therefore leaves the sole car docked with its riders aboard and ready clear
(`0x0055ABF0` state 1 lifts a car only with ready clear, no breakdown and, for the first car of a one-car ride, an open ride): TOUR 10 reads 0 and the script waits. When the ride reopens the car flies
once more with its negative count, lands (ready set, only positive counts are negated) and the script unloads it. OpenTPW does the same; the riders are not stranded for good: reopening
releases them, and deleting the ride releases everybody through the visitor bridge (`RideVisitorBridge.ReleaseAll`).

**Stale slot words (TOUR-009).** Car creation (`0x0055A720`) does not write a slot's heading, patrol corner, seat ids or approach heading (only the ride's first car takes the station's heading), so a new car in a
reused slot starts from the last car's values. OpenTPW keeps them: they are saved (every seat word of a live car, and a `FreeSlots` list for free slots that hold a non-zero word) and hashed, so
a saved and an uninterrupted run agree. A handed-out or thrown-off seat keeps its id too (`0x0055D610` and `0x0055D720` read the slot without clearing it).

## Saves (Q4)

The original saves the module as the `FLYR` block (`FLY_`, the active word, the 400-byte controller pointer table, the four bounds, six padding words, the controller size 0x122C and
every controller raw: 452 bytes when empty). OpenTPW's park-resume envelope gets an optional `Tours` part (`TourWorldSaveData`: every live controller, its cars and riders by attraction
id), validated on load and restored onto the rides; the canonical digest includes the module only while a controller exists, so every park without a tour ride keeps its pins. An older
save without the part restores each live tour ride with a fresh controller (`[EXT:legacy-tour-save]`, was TOUR-006: the original's save always carries `FLYR`, so this is OpenTPW's save-compatibility
decision). Bridge walkers carry an optional ready time (`WalkingOffReady`, absent for other rides). The tick clock of a saved controller may hold up to one tick (`TourController.MaximumSavedRemainder`, 62 ms);
a save from the 31 ms clock holds less than 31 ms and restores unchanged (the value is elapsed seconds, so the first advance runs it off). The `FreeSlots` and the full seat lists are trailing optional
parts: a save without them restores with zero stale words. No schema version moved: the canonical digest still includes the tour module only while a controller exists.

## Launch pad (TOUR-003 retired)

TOUR 1 (`0x005542FE`) reads the pad node live (`0x0044B220`, class 0x800 number 99, then `0x004386D0`) and the instance angle `+0x10` (`0x00554385`). `+0x10` is the build angle the constructor stores in degrees (`0x004DB0CD`, handed over by the placement `0x00529E10`) and, for a saved ride, the save's `mAngle`, which the attraction reader (`0x004DB7D0`) loads straight into `+0x10`: the placement rotation is the angle either way. All four tour scripts run TOUR 1 before any clip is triggered (`WAITANIM 0 0` on an idle channel, then `TOUR 1`), so the live node is the model's rest pose; `TourAssetTests.TheLaunchPadIsReadBeforeAnyClipPlays` pins that. TOUR-003 retired.

## The walk table and the walk-off time (TOUR-007 retired, TOUR-011 narrowed)

The script's walk table (header walk size entries of 0x20 bytes) is the original's, in `RideVisitorBridge`. WALKON (`0x00556F40`) takes the first free entry and stores its seven operands (the visitor,
nodes u1 and u2 for the walk on, nodes u3 and u4 for the walk off, the walk action and a last word), state 1, the start now and the end now + `ftol(distance(u1, u2)) x 100` ms (100 ms for 0; u1
is the first dummy of class 0x800 whose number matches, u2 one of class 0x80 for the head action 4, else 0x800, `0x0044B220`, both read live through `0x00556B90`); a full table logs and stores nothing. The walk update
of the main loop pass (`0x00557AB0`, called at `0x0054FA08` of `0x0054E360`, once per rendered frame after the park's substeps, then `0x00557D80`) moves state 1 to state 2 once the end has passed: the head action 4 hangs the rider's head on head id u2 (`0x0044B410`) and
action 2 creates effect `0x13` at u2. WALKOFF (`0x005571A0`) finds the entry by visitor (none: it logs and does nothing) and, whatever its state, sets state 3, the start now and the end now +
`ftol(distance(u3, u4)) x 100` ms (u3 of class 0x80 for the head action 4, else 0x800, u4 of class 0x800); action 2 creates effect `0x13` at u3 and a head action 4 head that is up (state 2) comes down (`0x0044B4C0`). The update moves state 3 to
state 4 at the end, and WALKGET (`0x00557110`) hands out the first entry of the table in state 4 and frees it. The ride runtime measures the model's dummies for every object that walks riders
(`OriginalObjectRuntime.WalkNodeDistance`; every literal node pair of the 47 shipped WALKON instructions exists in its model, `WalkNodeCorpusTests`), so a ride's riders now leave after the measured walk (a few hundred milliseconds to two seconds) and not at once; the Star and Tweety
Tours' slots lie 10 model units apart, so both still ride 1000 ms (`TourAssetTests.TheTourWalkOffsSpanTheModelsSlotNodes`, `WalkOffTimingTests`, `WalkTableTests`). The slot order, a WALKOFF restart in any state, the table size and the unknown rider are tested in `WalkTableTests`. The bridge saves the table entry by entry
(`WalkTable`, optional; a save from before restores its on-board riders and walkers into the table). Left under TOUR-011 (the register text): the template frame steppers, the terrain height of a body without the height bit, the host-step state changes where the original runs the update once per rendered frame, and a node missing from the model, which takes
no walk-on time, the ride's default walk-off time and draws no body (the original logs and keeps its stack words). The bodies themselves are the next section.

## The walking body (TOUR-011)

The walk update draws every entry it passes (`0x00557D80`, each through the body routine `0x005580A0`), so the riders of every ride that runs WALKON are seen (47 WALKON instructions in 38 shipped rides, not only the tours). `WalkerBodies` and
`FlatRideBounceRiders.CollectWalkers` pose them for the guest renderers (the same path as the bounce riders):

- **Walking on (state 1)** and **walking off (state 3)**: the body is between the two nodes of the state (u1 class 0x800 to u2, class 0x80 for the head action 4; u3 class 0x80 for the head action 4, else 0x800, to u4) by the share
  `(now x 1000 - began x 1000) / (end - began)` in thousandths (unsigned; the body routine clamps a share above 1000 to 1000, a signed compare at `0x0055810B`), position `((b - a) x share - a x -1000) x 0.001` per axis, height from the nodes (every shipped script sets bit 0 of the last
  WALKON word). **Ready (state 4)** stands at u4 until WALKGET hands the rider out. All three draw template 1, the walking slot (1), facing the entry's heading.
- **Heading**: WALKON stores `atan2(z2 - z1, x2 - x1)` of its two nodes (`0x005570C2`: the original's `fpatan` of the angle a, then `ftol(0.5 - (6.28318 - (a - 1.570795)) x -1.2732406)`, a rounded eighth of a turn from 6 to 14, with the sign kept and the magnitude masked to three bits); the sprite pass keeps its low three bits. The original's x and z are the engine's X and Y.
- **On board (state 2)**: the head actions 1, 2 and 4 draw no body (`0x004F9ED0`); the actions 3, 5 and 6 draw template 3 (standing, slot 0), 8 (the short gesture, slot 4: the strength games Strength Bird and Skull and Fruit Shy) and 5 (cheering, slot 12: the Mayan Spinner, Jungle Spray, Laughing Hyenas,
  Devils Disc, Zero G, Martian Mooners and Aqua Spray) at node u2, with the heading taken every frame from the node's orientation row (`0x00556B90` output, x and z, the same rounding to an eighth of a turn and sign-kept mask, `0x00557F52`).
- **Walk float** (WALKST_FLOAT, Zero G): while the script's float word is set the body on board swings about u2 in template 2 (running): with A = 0.4 + 0.1 (id & 7), B = 0.4 + 0.25 (id & 3), C = 0.5 + 0.01 x the speed bias and the two words a (swing) and b (height) x 0.001,
  env = sin(pi x (clock - start) / duration) from the sine table, the offset is x = sin(f / 300) B env 3 a, z = sin(B clock C / 300 + pi / 2) A env 3 a and the height 12 + 3 sin(0.004 f) times env b (f = A clock C), and the heading adds `clock / ftol(B x 300)` (`0x005581D4`..`0x005583F5`).
- The slot of a template is read from the template tables at `0x0075A418` (templates 1, 2, 3, 5 and 8 select the slots 1, 2, 0, 12 and 4 of the guest sprite sets; the table's other templates are not drawn by the walk update).

Open under TOUR-011: the frame step of each template (the stepper handlers are not decoded, every body steps each 100 ms), the terrain-height form, the per-frame cadence of the state changes and the missing-node case. `WalkerBodyTests` pins the rules and a placed
Mayan Spinner's table entries in every state; the native Jungle capture with a Mayan Spinner shows the body walking on and cheering on board.

## Pose sines (TOUR-008 narrowed)

The rotation builder behind the car pose (`0x0046F6F0`) reads sine and cosine from a 4096-entry table filled at start (`0x004708D0`: `float(fsin(i / 4096 x 2 pi))`, index `round(angle x 4096 / 2 pi) & 4095`, a cosine the sine of the angle plus a quarter turn). `OriginalSineTable` is that table and `TourPose.Rotation` turns the matrix with it; the quantisation is up to 7.7e-4 on a sine. Only the shadow question stays under TOUR-008.

## Not done

The hoarding posts' and station tiles' ground (TOUR-005); see the register. There is no shadow under the cars in the original either (TOUR-008 retired: the car draw, the MESHSHADOW flag, the vertex writer and the batcher `0x00581DB0` show none).

## Drawing

`TourRenderer` draws every live car as the ride's supplemental mesh 0 (`Bird.md2`, balloon, craft, kite) and the riders hang from the mesh's head dummies through the ride-head renderer, like the go-karts' riders.
The pose is the original's draw pass `[BIN:TPW-EXE:0x0055CE60]` (`TourPose`):

- Target: position `previous + (current - previous) x f` over 300 (model units), heading and roll by the shortest way round (the difference masked to 12 bits, `0xFFF` taken off from `0x800`; the original's
  constant is `0xFFF`, not `0x1000`), with `f` the time since the last tick over 62 ms, at most 2. A car's first draw snaps to its current pose.
- Smoothing: drawn = (target + 3 x drawn) / 4 for the position (a float), heading and roll, and (target + 7 x drawn) / 8 for the pitch, the angles truncated and masked to 12 bits. OpenTPW does this once
  per simulation step (60 Hz), with `f` from the tick clock's remainder, so the drawing is the same on every machine.
- Rotation: the helper `0x0046F6F0` with mode 0xE (`"ZXY"`, string `0x0074DA7C`) reads three angles: roll about Z, pitch about X, yaw about Y. Each helper (`0x0046F5C0`, `0x0046F350`, `0x0046F650`)
  post-multiplies the instance matrix, whose translation is row 3 (`+0x40..+0x48`), so a model vertex v maps to v x Rz x Rx x Ry, exactly System.Numerics' row-vector products.
  The angles are roll `-r x 2pi/4096`, pitch `-p x 2pi/1024` (so the pitch counts **four times** finer than the heading and roll) and yaw `pi/2 - h x 2pi/4096`.
- Axis: with the car moving along `(cos h, sin h)` on the module's two ground axes (the model frame's x and z, y up), the yaw `pi/2 - h` carries the model's **+Z** axis onto the velocity, so the
  car mesh's nose is +Z (the old drawing assumed +X). The original reads a sine table for its angles; OpenTPW uses exact sines.
- Heads: TOUR 3 (`0x0055D830`) hangs rider i (0-based) on head node `((random + i + 1) mod heads) + 1`, with `random` the car's own random word (`+0xD8`, drawn at creation and saved) and `heads` the
  count of consecutively numbered head nodes of the car mesh (`+0xDC`, counted at `0x0055AA2D`); more riders than nodes share nodes. `TourPose.HeadNode` is a presentation function of that word:
  it draws nothing, so the simulation's random stream is untouched.

## Sounds, effects and animations (TOUR-004 and TOUR-008 retired)

The controller raises `TourEffect`s (`TourController.Effect`, presentation only: no world draw) and `OriginalObjectRuntime` plays them:

- **Engine voice.** A new car starts the EventMap's variable 0 event in the level `cat_rides` at its place (`0x0055A944`, through `0x51EEB0`; the place is x / 300, height / 300, z / 300 in
  sound units, truncated toward zero). Every state then sets its parameter (code = the EventMap's variable 10, `0x55A3E0`) through `0x51BC40`: 80 docked (`0x55B09F`) and on touchdown (`0x55BBF5`),
  15 taking off (`0x55B65B`), 60 when broken else 30 for the cruising states and the descent (`0x55ACE4`/`0x55AD11` and the matching pairs). At the end of every car's tick the voice moves to the car
  (`0x55C20A`); removing the car stops it (`0x0055CDA0`, `0x51C2C0`). The voice is a sentence with the placeholder's parameter table, as the karts' (docs/TRACKS.md, "Engine voice").
- **Clips.** `0x0055AA80` triggers the car mesh's clip on channel 0 only when (animation, channel, loops) changes; creation resets the last trigger to −1 (`0x0055A8D9`), so the creation's
  animation 0 always plays. The animations are 0 created, 3 landed, 4 taking off (loops), 5 flying (loops), 6 descending, 9 flying broken (loops), 10 repaired. `TourRenderer` plays them from the car
  mesh's own letter members (`Bird.md2`: `BirdC` 0, `BirdL` 3, `BirdS` 4, `BirdM` 5, `BirdE` 6, `BirdB` 9, `BirdR` 10) with an `ObjectAnimator` per car instance, advanced by the world step; the
  birds flap by vertex tracks (each car moves its own copy of the mesh), the riders' heads and the smoke follow the animated nodes. `TourCar.AnimationSerial` counts the triggers (presentation, not saved).
- **TOUR 9.** Worn (2) replaces the first car's emitter by particle 39 (`P_EFFECT_FlySmoke`) created at the car's raw module coordinates taken as particle units (`0x0055DB95`, unlike 3 and 4);
  broken (3, 4) by particle 40 (`P_EFFECT_FlyFire`) at the car's place, `(x << 10) / 300` (`0x0055DBD8`); mended (1) on a broken car triggers clip 10, plays the global `cat_rides` sound 0xBB at the car
  (`0x0055DB2B`) and destroys the emitter (`0x0055DB6B`). Every draw moves a live car's emitter to its node class 0x100 number 1 (`0x0055CE60`, `0x51FE30`). The emitter is saved with the car
  (`TourCarSaveData.Emitter`, optional, 0, 39 or 40; not hashed) and restarted on resume, with the engine voices.

## Object ground (TOUR-005)

The original's `.hmp` is the height map of every object (loaded beside its mesh by `0x00451640`, whose header words +0xC, +0x10 and +0x14 become pointers; +8 is the width in cells). The lookup `0x00452AE0` for
a map cell takes the mesh instance of its second layer (`0x0053BF30`: a track piece or a park fence post), else the object that owns the cell (the owner word of a type-4, 9 or 10 cell and the class-3 thing in the owner's anchor cell, `0x527E80`, `0x527D60`) and, when that instance has a map, returns the byte of the per-cell maxima table x 100/255 (`0x006FE408`) plus the
object's height (`+0x44` of its instance matrix). The table index is `width x row + column`, with row and column the truncated tenths of the cell corner's offset from the instance position,
turned by the instance rotation (0, 90, 180, 270 swap and reflect the two offsets, the `+10` terms being a cell's width). `ObjectHeightMaps` reads `<mesh>.hmp` of the catalogue entry and does the same
from OpenTPW's placement: the instance position is the model-to-engine translation, the rotation the placement's. All 274 catalogue objects ship their map and every footprint cell of every one of
them lies inside it at all four rotations (`TourAssetTests`, `TourRegisterRoundTests.TheMapIndexFollowsThePlacementsRotation`), which fixes the orientation. The object route is matched (the footprint cells
are the typed cells of one owner word). The second layer is read first, as the original does: a kart or water track tile's piece map (`TrackRenderer.PieceGround`). The tile's instance sits at the origin cell plus two cells of the engine rotation's offset (`360 - rotation`: 90 y + 1, 180 x + 1 and y + 1, 270 x + 1; added once by
the instancer `0x00522B90` / `0x00522D50` and once by `0x00466F90`), its map is 2 x 2 cells indexed from that corner with the engine rotation, and its height is the tile height `0x00466D70` (the midrange of the 3 x 3 vertices); every shipped piece map holds the tile's four cells at all four rotations
(`TrackAssetTests.EveryShippedPieceMapHoldsItsTilesFourCellsAtEveryRotation`). A tile with the add-on bit has no piece instance and falls through to the object (the add-on). The mesh loader `0x004629D0` rebuilds a map only when the file is missing, malformed or of another size than the object's shape
(`0x00451640`), and every shipped object map has its shape's size (`TourAssetTests.EveryShippedHeightMapHasTheSizeOfItsObjectsShape`), so the rebuild never runs for the catalogue. The remaining hoarding lookup is not implemented. The native type-25 selector (`0x0053A540`, type-specific tail `0x0053A844`–`0x0053A995`) returns family 5, whose second-layer instancer calls `0x00522F20` and reads table `0x00763608` (`ho_fos1`, `ho_end1`, `ho_fob1`). The previously cited `0x005229E0` / `0x00763388` belongs to primary-cell family-2 queue meshes (`quedead`, `questra`, `quebnd`, `queend`), not these hoarding instances. The hoarding constructor computes `360 - rotation`, replacing exactly 360 by zero; for the intended cardinal rotations it adds the corresponding cell offset before the common constructor. The complete downstream transform, map shape and laid-cell ownership still need qualification; an offset-once rule is not established.

Station types 13 and 18 take the selector's default table `0x00763138`, whose entries are family 1. The traced second-layer instancer `0x0053AFA0` constructs only families 3, 4 and 5, so this normal selection path does not establish a separate station mesh to sample. This does not prove that every historical or transitional station handle is zero. OpenTPW's existing object/terrain fallback stays unchanged. TOUR-005 remains open for the hoarding lookup and track-map rebuild qualification; no tour runtime behavior or ownership is changed by this evidence correction. The object's base height is RIDES-014's vertex midpoint.

## Register changes (October 15 round)

- Retired: TOUR-001 (the tick is 62 ms; the wall-clock catch-up, its 2 s clamp and the parity phase against the script loop are a determinism decision, `[EXT:tour-fixed-step]`), TOUR-009 (stale slot words kept and saved) and
  TOUR-010 (the Ready override on closing is gone, see Q3).
- Reclassified: TOUR-006 is `[EXT:legacy-tour-save]`.
- Reclassified: TOUR-002 is `[EXT:tour-thrown-riders-released]` (formerly `APPROX:TOUR-002`: riders a severe breakdown threw off are handed out through TOUR 4, which lowers VAR_ONRIDE; the original's loop in `0x0055DA70` discards the id `0x0055D720` returns, so the script never releases them and **thrown-off riders inflate the ride's rider count**, VAR_ONRIDE, for ever; OpenTPW fixes that on purpose).
- Narrowed: TOUR-004 (all
  triggers located, nothing played), TOUR-005 (implemented for the shipped object maps and the track tiles' piece maps), TOUR-008 (retired: the cars have no shadow in the original).

## Verification

The native jungle smoke places Jurassic Tours through the build path, sends two real guests to it, and when a car flies with riders freezes the world and reads back the GPU pixels with the cars shown and hidden (they differ); the run goes on until the car landed and both riders were handed back through `TOUR 4`, then removes the ride. The park-resume smoke with `OPENTPW_SMOKE_TOUR=1` (both phases) buys the ride with a golden ticket, saves with a car in flight, restores through the menu load and matches the 600 continuation ticks of canonical hashes. The saved car is empty in that run; riders in flight are covered by the unit round trip and the asset test. The asset test closes each real tour script's ride after ten minutes and accepts both original outcomes (everything unloaded, or the TOUR-010 car docked with its riders), then deletes the ride and requires every boarded guest released.
