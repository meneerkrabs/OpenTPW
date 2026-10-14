# Tour rides

The four original tour rides (Jurassic Tours `jungle/rides/tourride`, Flightmare Tours `hallow/rides/tourride`, Star Tours `space/rides/scitour`, Tweety Tours
`fantasy/rides/twetours`) take and release riders through the `TOUR` opcode. Rules are traced from the Windows `tp.exe` (`TPW-EXE`, through the shared REA
session over the decrypted executable); every approximation carries a `TOUR-` ID in [the register](FIDELITY-REGISTER.md).

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

The per-tick state machine is `[BIN:TPW-EXE:0x0055ABF0]`, called once per 31 ms world step from `0x0054F5EF`. Car states: 1 docked (loading, or unloading when its
rider count is negative), 2 take-off, 3 tour, 4 return, 5 staging (fixed approach), 6 approach, 7 landing, 8 patrol, 9 leaving. A new car appears outside the flight area and
circles the station (state 8) until the landing slot (+0x2C) is free. The first car of a ride is created docked and sets the ready flag.

A run: the script waits for `TOUR 10`, unloads with `TOUR 4` (`WALKOFF`/`WALKGET` on Star and Tweety), loads with `TOUR 3` until the car is full or ten seconds passed since the last
boarding, then `TOUR 11` and `TOUR 8`. The car takes off (the first target 20000 units along the heading at least 14000 up), flies lattice targets (`0x0055C350`: continue
the direction, turn aside when the ground ahead is above the car, targets height max(12000, ground + 5000)) until its timer ends, returns, patrols the four corners 25000 units out
(`0x0055C270`), claims the landing slot, flies the approach line and descends; on landing the rider count turns negative and the script unloads. Cars avoid each other (slower car
loses a sixteenth of its speed, the lower one climbs) and clear stations by 30000.

## Saves (Q4)

The original saves the module as the `FLYR` block (`FLY_`, the active word, the 400-byte controller pointer table, the four bounds, six padding words, the controller size 0x122C and
every controller raw: 452 bytes when empty). OpenTPW's park-resume envelope gets an optional `Tours` part (`TourWorldSaveData`: every live controller, its cars and riders by attraction
id), validated on load and restored onto the rides; the canonical digest includes the module only while a controller exists, so every park without a tour ride keeps its pins. An older
save without the part restores each live tour ride with a fresh controller (TOUR-006). Bridge walkers carry an optional ready time (`WalkingOffReady`, absent for other rides).

## Not done

Sounds and effects (TOUR-004), the car animations and the exact mesh orientation (TOUR-008), building-top ground (TOUR-005), exact pad orientation (TOUR-003); see the register.

## Drawing

`TourRenderer` draws every live car as the ride's supplemental mesh 0 (`Bird.md2`, balloon, craft, kite) at the car's pose, and the riders hang from the mesh's head dummies through the ride-head renderer, like the go-karts' riders.
