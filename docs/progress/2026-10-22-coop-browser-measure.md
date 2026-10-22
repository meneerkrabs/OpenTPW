## Co-op: the join measured in the browser (October 22, 2026)

The co-op join's cost is now measured in the browser too ([COOP.md](../COOP.md), "Measurements"; [BROWSER-PERFORMANCE.md](../BROWSER-PERFORMANCE.md), "Joining a shared park"; `[EXT:online-coop-snapshot-join]`). This is C2c-2b-2c: its numbers decide what C2c-2b-3 splits over frames.

- `scripts/measure-coop-join-browser.sh` records the host phase once with the desktop's Release build, then runs the page's measuring join phase over that recording several times, each in a fresh Chrome profile. `tools/web-coop-join.mjs` records the machine and the checkout beside the runs.
- `tools/coop_join_timings.py` counts the reused host phase once. It also prints each frame's whole call, the frame that checked and swapped the build in, and the frame of the smoke's final check. A native run's evidence does not hold that last frame, so the tool leaves the row out there instead of showing another frame.
- In a page compiled ahead of time, the longest frames of a join are the save's parse (0.39 s), the swap's frame (0.36 s) and the objects' restore (0.32 s). Interpreted, the objects' restore takes 1.4 s. The native measurement's table now shows the swap's frame too (0.33 s).
- Two leads for C2c-2b-3:
  - The wait for the level's files spends each frame's whole budget asking whether they are there, about 6.4 s of 16 ms frames on the first join of a theme.
  - The join's build has none of the steps that prepare a park start's entering frame.
