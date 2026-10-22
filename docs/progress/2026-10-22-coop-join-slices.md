## Co-op: a join's build ends a frame's slice where it should (October 22, 2026)

The guest's build of a shared park now ends a frame's slice at the two places a park start does, and makes the save codec's metadata in steps ([COOP.md](../COOP.md), "The game's join"; `[EXT:online-coop-snapshot-join]`). This is the first part of C2c-2b-3.

- The wait for a theme's files, which a browser copies between frames on the theme's first use, asks once a frame and leaves the rest of the frame to the live game. Before, it asked until the frame's 12 ms budget was spent: about 16 ms of update in every frame of a six-second wait.
- The save codec's reflection metadata is made in steps of its own, one type each, before the save is read, as a park start makes it for its first snapshot. A process's first parse made it in one go: most of the 374 ms parse of a page's first join.
- A construction step that announces a long one next (`Level.NextStepIsLong`, from the build steps split for a park start) ends the slice, so the long step starts a slice of its own.
- The measuring join smoke records the named work of the frame that swaps the built park in, and of the frame after it, so the tables can show where that frame's time goes. The save's parse is timed in parts: the JSON, the shape check and the mods check.
