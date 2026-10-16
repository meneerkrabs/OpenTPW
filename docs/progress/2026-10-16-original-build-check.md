## The original build check: Blocked ground is buildable (October 16, 2026)

tp.exe's build check was traced via REA. The cursor handler `0x5234D0` places an object through `0x528A70`, which tests every
footprint cell through `0x532FC0` (mode 0x200) with the cell validator `0x535670`. The validator reads the cell's kind (+8), flags
(+0xE: 0x40 unowned, 0x20 path), path links and owner, never its MAP byte (+0x26). The map start `0x536490` gives a surface cell
kind 0 or, for the initial path, 1: the kinds that bits 0x01, 0x02 and 0x80 pick there are overwritten right after
(`0x536524..0x536548`). Only holes keep a refusing kind (2, 0x1E, 7).

`OriginalParkPlacement`'s terrain rule now uses that map-start kind (`ParkLand.OriginalCellType`, the heightfield cell word's bit 0)
instead of refusing every `Blocked`, `Water`, `EntranceArea` and `FixedWalkway` cell. Measured over the four base maps, 318–326
surface cells per theme carry `Blocked` (all value 1: the edge ring and the ground round rocks), plus fantasy's value-16 cell
(27, 17). Only space (45, 18) and (50, 18) and fantasy (27, 17) lie in the starting land; the rest become buildable for objects,
paths and queues once bought. Holes are refused as before, so water rides keep their water cells. PATH-006 (PATH-SLOPE) is retired:
neither the original nor OpenTPW has a slope test, and the terrain rule no longer stands in for the original's cell types.

The native smokes' and the demo builders' site searches (`Free` in `SandboxSmokeTest.Tracks/Tours/Coasters`,
`ParkResumeSmokeTest.Tour/Water/Bumper`, `Level.BuildDemoCoaster`, `Level.BuildDemoTrackRide`) now also require owned land: they
leaned on the Blocked refusal to keep off the unowned rim, and on main the jungle smoke already failed at the water track's preview
(68, 10), an unowned cell. Verified with the full test suite (5024 passing with game data, 109 skipped). The jungle sandbox smoke
now passes the track steps; its tour stage (wait for a quiet moment with every rider released) takes about four minutes on this
machine with either rule and exceeds the smoke's 180-second watchdog, so it is not counted as passing.

Still open (RIDES-018): the validator's object branches (existing ride cells against the count `0x818C10`, entrance and exit cells
with their path cell `0x5286A0`, the cost test `0x52FBC0`).
