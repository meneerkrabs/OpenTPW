## Browser event hitches (October 19, 2026)

The game stuttered in the browser at some in-game events. A new harness scenario, `events` in `tools/web-perf/run.mjs`, shows which ones and why. It drives a Jungle park through window opens, buy previews and builds, weather, research, the autosave, month and year ends, a coaster build, a crowd and a fast soak. For every slow frame it records the game's named work (`FrameMarks`) and, with `--profile --symbols`, the AOT functions a CPU profile found there.

Published AOT build, medians of three runs:

| Event | Worst frame before | Worst frame after |
| --- | --- | --- |
| Entering the park | 572 ms | 147 ms |
| Largest frame of the park start | 713 ms | 234 ms |
| First Belly Bounce preview | 202 ms | 55 ms |
| Advisor line during a window open | 13-42 ms | 8-18 ms |
| Year end | 26.5 ms | 7.6 ms |
| Fast soak | 21.3 ms | 10.6 ms |

The park is ready 0.5 s sooner.

Four behaviour-preserving changes made the difference:
- bit-identical faster x87 sign arithmetic, with the relief terms reused;
- advisor speech decoded over frames in the browser (a line's audio may start 50-150 ms later there);
- the first restart snapshot's JSON metadata and asset manifest prepared in steps before entering;
- remembered file-system existence and listing answers for the browser's game data.

World hashes, sign hashes and determinism pins did not move.

Remaining hitches are listed in docs/BROWSER-PERFORMANCE.md under "Event hitches":
- the entering frame (first draw and park HUD);
- the autosave capture (37 ms);
- the first preview of a ride with a sign (55 ms).

The entering frame fell further, from 216 to 85 ms, on builds after merging origin/main (medians of three runs):
- the first restart snapshot is now validated after entering, in steps;
- the capture's code is warmed during the build;
- the advisor's settings, models, clip lengths and theme costume are read during the build.

The restart snapshot's guarantees are unchanged: the capture still happens in the entering frame, and the pending marker still blocks an earlier park until the snapshot is committed. The first draw (19 ms) and the HUD (16 ms) remain.
