## Co-op: a joined park's objects restored over steps (October 23, 2026)

A guest's build of a shared park now restores the park's saved objects one a step instead of all in one, the longest frame of a join so far ([COOP.md](../COOP.md), "The game's join" and "Measurements"; `[EXT:online-coop-snapshot-join]`). This is the third part of C2c-2b-3.

- The first step checks the whole saved layout before it places anything, each step places one object in the saved order with its saved seed and ids, and the last restores the allocators, so the park is the same as before, object for object. A failure clears the half-built park as before; a build given up part way is dropped whole.
- On the desktop the saved types' signs are composed on the sign worker before their objects are placed, as the buy window has its listed types' signs composed.
- The longest build frame of a join falls from 357 to 83 ms on the desktop and from 290 to 87 ms in the page compiled ahead of time. The longest frame of a join is now the swap's: 173 ms on the desktop, most of it the advisor's first update, and 110 ms with its drawing in the page.
- The native join smoke now abandons its second build at the first frame with saved objects placed, and checks that nothing of it is left in the live scene.
- An ordinary resume of a saved park still restores its objects in one call; only the co-op join and resync build their park over frames.
