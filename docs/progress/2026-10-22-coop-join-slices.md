## Co-op: a join's build ends a frame's slice where it should (October 22, 2026)

The guest's build of a shared park now ends a frame's slice at the two places a park start does, and makes the save codec's metadata in steps ([COOP.md](../COOP.md), "The game's join" and "Measurements"; `[EXT:online-coop-snapshot-join]`). This is the first part of C2c-2b-3.

- The wait for a theme's files asks once a frame and leaves the rest of the frame to the live game. A browser copies those files between frames on the theme's first use, and the old loop asked until the frame's 12 ms budget was spent, most likely keeping the copy waiting. In the page compiled ahead of time the files now arrive in 9 frames instead of 371, and the whole join, from asking to the swap, takes 2.7 s instead of 8.9.
- The save codec's reflection metadata is made in steps of its own, one type each, before the save is read, as a park start makes it for its first snapshot. A process's first parse made it in one go, most of a page's 384 ms parse, which now takes 88 ms.
- A construction step that announces a long one next (`Level.NextStepIsLong`, from the build steps split for a park start) ends the slice, so the long step starts a slice of its own.
- The measuring join smoke records the named work of the frame that swaps the built park in, and of the frame after it. In the page compiled ahead of time that frame is now a join's longest: the joined park's first update of its level, the check at the cut and the advisor's first line. The save's parse is timed in parts: the JSON, the shape check and the mods check.
- The tables of every co-op measurement moved to [COOP-MEASUREMENTS.md](../COOP-MEASUREMENTS.md), with the baseline of main and this change's runs.
