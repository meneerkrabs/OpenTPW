## Browser pins: exclusion guard and stricter reports (October 19, 2026)

Follow-up within the browser determinism gate (co-op C1b), from the PR-watch review:
- `BrowserPinsExclusionTests` fails the ordinary desktop run when a test file that uses `OpenTPW.Server` or ASP.NET Core is missing from `DeterminismPinsExcludedTests` (the server transport test from main had broken both browser legs).
- `tools/web-determinism/run.mjs` now also fails on a test that ends inconclusive for any reason other than missing game data, a language pack or the repository sources.
- `compare-digest.mjs` reports an M4 hash list present on one side only and themes present only on the right.
- `DETERMINISM.md` lists `FlatRideBounceRiders.Prepare` and the per-frame entity updates among the work outside the fixed tick.
