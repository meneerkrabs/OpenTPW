## Objects over paths, paths over scenery and the access cells from tp.exe (October 16, 2026)

Three more branches of tp.exe's cell validator `0x535670` and the placement `0x528A70` were verified via REA and ported (RIDES-018,
docs/OBJECTS.md "Build rules"):

- **Objects on path cells.** Type 4 over a type-1 cell is allowed (`0x535D21`) unless the cell has NoModify (0x20, the map start's
  initial path; `0x5356B5` refuses it before any other test). The commit clears the path with ClearCell `0x5367A0(0, 0)`, which refunds
  nothing: cell op 0x87 does so when the new priority is above `0x818C14` (`0x534549`), and the footprint write clears every cell before
  typing it anyway (`0x52910C`), so the path goes whatever the priorities. `ParkObjects` now asks the level's cell map (`PathAt`): a plain
  path under the footprint is cleared through `ParkPathBuilder.Remove` before the object is added, a NoModify one refuses, and the
  terrain rule's save and map-start path cells defer to the live map, so a removed save path no longer blocks a build. Guests standing
  on a cleared cell are set down on the nearest path cell (`GuestSimulation.SetDownOffCells`; tp.exe's handling is not traced).
- **Paths and queues over scenery.** The hover (`0x524571`) and the click (`0x527200`, `0x527218`) set `0x818C10` to 2 for the path tool
  and 3 for the queue tool, and the validator compares it with the owner's priority (`0x535D66`) before any path or queue rule. A path
  or queue line may now cover a feature of priority 0 or 1 (`ParkPathBuilder.ObjectGivesWay`, the queue builder's `givesWay`); the
  commit demolishes it whole (`0x527EE0` through op 0x87 of the first line pass, scrap value credited by the level's removal).
- **The access cells.** The task note had the path rule on both outside cells when option `0x81B0CC` is on. tp.exe sets that option to the
  placed type's `Info.HasQueue` when the build tool opens (`0x46D644`, a call Ghidra's function bounds miss); with it on, the entrance's
  outside cell is checked as an object cell (op `0x204`, `0x5297A4`) and the exit's by the path rule (op `0x201`, `0x529A2F`); with it
  off, only the entrance's cell is checked, by the path rule (`0x5298B9`). An access cell off the map refuses (`0x529744`, `0x5299E2`).
  The corpus agrees: every one of the 72 queue types has one entrance and one exit, every other type at most one entrance and no exit.
  `ParkObjects.CheckAccessCells` follows this (refusal `AccessBlocked`) and demolishes a lower-priority object there; laying the
  queue and path cells stays open (RIDES-028).

The shipped priorities were counted over the four catalogues: 5 for every ride, shop, sideshow, upgrade, toilet, staff room, fixed item
and fantasy's Large Sign, 1 for 45 small-scenery types and 0 for the other 48 features. QUEUE-012 lost its open question (the "count" at
`0x818C10` is the tool's overwrite priority). Tests: `ObjectBuildRuleTests` (synthetic objects on a live cell map) and
`OverwritePrioritiesAndAccessCellsFollowTheTypes` (corpus). The smokes' and demos' site searches now take only sites that clear no path
and demolish nothing (`Level.IsUntouchedSite`): the park-resume fixture had built its shops over its own path spine and then spawned
guests on the cleared cells. Verified with the full test suite (5064 passing with game data, 109 skipped) and the native park-resume
smoke, capture and restore (silent). An independent Codex review found three issues that were fixed: guests were looked up by their
next cell, a path cell that could not be paid for still demolished its feature, and queues read stale save path terrain.

Still open (RIDES-018): the second-layer link tests `0x53AE00`/`0x53AE90`, the pending cost of the access cells, the types whose record
+0x288 is 1 (their `Bumper.WhichTrackType` turns the access-cell option off), and what tp.exe does with a guest inside a new footprint.
