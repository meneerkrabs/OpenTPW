## A join's check and the level's end no longer stall the page (October 23, 2026)

A guest's build of a shared park now prepares the check at the cut and restores the saved simulation in steps ([COOP.md](../COOP.md), "The game's join" and "Measurements"; `[EXT:online-coop-snapshot-join]`). These are the seventh and eighth parts of C2c-2b-3.

- Both were mostly a process's first use of their code. In the page the code a call runs for the first time still costs its compilation by the browser's WebAssembly engine.
- The build makes the economy digest's serializer metadata in its codec stage. A last stage then runs that serialization once over the joined park's economy, a part of its save a step. In the page the check at the cut takes 13 ms instead of 72, on the desktop 12 instead of 40.
- The saved simulation's restore runs a part a step: the runtimes, the scripts, the guests' routing, the guests, the weather, the staff, the coasters, the other rides and the drawn tracks. The level's longest step takes 28 ms in the page instead of 69.
- No build frame of a join in the page now reaches 50 ms.
