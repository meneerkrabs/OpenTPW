## A browser can host a shared park in the smokes (October 23, 2026)

The co-op join smoke's host phase now runs in the browser's game, and the desktop's join phase replays its recording ([COOP.md](../COOP.md), "The browser's host" and "Measurements"; `[EXT:online-coop-snapshot-join]`). This is C2c-2b-4.

- The page builds and shares the Jungle park, takes two captures and records the session. The desktop guest's join, failed check, abandon, resync and every checkpoint to the end match it. A browser host and a desktop guest run one simulation, as a desktop host and a browser guest already did.
- In the page a capture's cut takes 17 to 21 ms and its save serializes in slices, the longest 23 ms in the median run.
- The smokes' host now runs the session digest's code once ahead before it hosts (`CoopSession.PrepareHostSteps`, which the game's own sharing does not run yet), so the first checkpoint after the park is shared no longer takes a frame of about 70 ms in the page.
- `scripts/smoke-coop-join-browser-host.sh` and `scripts/measure-coop-join-browser-host.sh` run and measure it.
