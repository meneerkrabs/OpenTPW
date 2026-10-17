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

## Walk-off time (TOUR-007 retired)

WALKON (`0x00556F40`) stores its walker's slot in the ride's walk table: the operands in order are the visitor, nodes u1 and u2 (the walk on), nodes u3 and u4 and the walk action. WALKOFF
(`0x005571A0`) finds the slot by visitor and takes `ftol(the distance between node u3 and node u4) x 100` ms (100 ms for 0) before WALKGET hands the walker out; u3 is the first dummy of class 0x800
whose number matches (class 0x80 for the head action 4), u4 one of class 0x800 (`0x0044B220`), both read live (`0x00556B90`). `RideVisitorBridge` keeps the slot per rider (`WalkSlots`, an optional
saved part) and the ride runtime measures the model's dummies (`OriginalObjectRuntime.WalkNodeDistance`). The Star and Tweety Tours' slots lie 10 model units apart (the Cosmic Cruiser's
10.03, the Tweety Tours' exactly 10), so both ride 1000 ms, as before (`TourAssetTests.TheTourWalkOffsSpanTheModelsSlotNodes`, `WalkOffTimingTests`). TOUR-007 retired. The beam effect of action 2 at WALKOFF
is built (`RideVisitorBridge.WalkOffEffect`: effect `0x13` at the slot's start node, class `0x800`, each coordinate truncated to whole units and shifted left by 10, `0x00521E60` at `0x005572EB`; the handle is not kept). WALKON's own walk-on
time (`ftol(distance between u1 and u2) x 100`, stored by 0x00556F40 at +0x0C) is read by the walk update `0x00557D80` (state 1): it moves the walking body along its nodes and, at the end, moves the entry to state 2 (a head action 4 hangs the head then). It gates only the body and the head, not the script (the state 4 WALKGET hands out comes from WALKOFF's walk), and is not modelled: riders board at once and a head action 4 head hangs at once (TOUR-011).

## Pose sines (TOUR-008 narrowed)

The rotation builder behind the car pose (`0x0046F6F0`) reads sine and cosine from a 4096-entry table filled at start (`0x004708D0`: `float(fsin(i / 4096 x 2 pi))`, index `round(angle x 4096 / 2 pi) & 4095`, a cosine the sine of the angle plus a quarter turn). `OriginalSineTable` is that table and `TourPose.Rotation` turns the matrix with it; the quantisation is up to 7.7e-4 on a sine. Only the shadow question stays under TOUR-008.

## Not done

A shadow under the cars (TOUR-008: the car draw and the MESHSHADOW flag show none, the generic instance draw was not read for one), the mesh-derived and second-layer-owned ground (TOUR-005); see the register.

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

## Sounds, effects and animations (TOUR-004 retired, TOUR-008 narrowed)

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
are the typed cells of one owner word). Still terrain: cells with a second-layer mesh instance (a track piece of a kart or water ride, a park fence post), whose map and height the original reads, and objects whose map the original rebuilds from the mesh (`0x00451880`). The object's base height is RIDES-014's vertex midpoint.

## Register changes (October 15 round)

- Retired: TOUR-001 (the tick is 62 ms; the wall-clock catch-up, its 2 s clamp and the parity phase against the script loop are a determinism decision, `[EXT:tour-fixed-step]`), TOUR-009 (stale slot words kept and saved) and
  TOUR-010 (the Ready override on closing is gone, see Q3).
- Reclassified: TOUR-006 is `[EXT:legacy-tour-save]`.
- Reclassified: TOUR-002 is `[EXT:tour-thrown-riders-released]` (formerly `APPROX:TOUR-002`: riders a severe breakdown threw off are handed out through TOUR 4, which lowers VAR_ONRIDE; the original's loop in `0x0055DA70` discards the id `0x0055D720` returns, so the script never releases them and **thrown-off riders inflate the ride's rider count**, VAR_ONRIDE, for ever; OpenTPW fixes that on purpose).
- Narrowed: TOUR-004 (all
  triggers located, nothing played), TOUR-005 (implemented for the shipped maps), TOUR-008 (animations and
  shadow only).

## Verification

The native jungle smoke places Jurassic Tours through the build path, sends two real guests to it, and when a car flies with riders freezes the world and reads back the GPU pixels with the cars shown and hidden (they differ); the run goes on until the car landed and both riders were handed back through `TOUR 4`, then removes the ride. The park-resume smoke with `OPENTPW_SMOKE_TOUR=1` (both phases) buys the ride with a golden ticket, saves with a car in flight, restores through the menu load and matches the 600 continuation ticks of canonical hashes. The saved car is empty in that run; riders in flight are covered by the unit round trip and the asset test. The asset test closes each real tour script's ride after ten minutes and accepts both original outcomes (everything unloaded, or the TOUR-010 car docked with its riders), then deletes the ride and requires every boarded guest released.
