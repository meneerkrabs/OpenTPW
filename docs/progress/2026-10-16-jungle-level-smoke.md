## The jungle level smoke runs again (October 16, 2026)

`--load-original-level jungle --smoke-test` had stopped passing without anyone noticing: it needs the original game data, so CI does not run
it, and the shared smoke script covered only the front end, the sandbox, the plain smoke and the park-resume pair. Five things had drifted:

- **The water-track step** still expected the station tiles on the footprint's first tile column. Since the station follows tp.exe's line
  `0x52A1A0` the tiles are the footprint's rows 1 and 2 across the heading (rotation 0: `(x, y+1)` and `(x+2, y+1)`), and the controller
  reference is the cell between them. The smoke now asserts that layout and lays its circuit from the first tile, as the park-resume fixture does.
- **The staff step** scanned the guest map for clickable cells and `Level.TryProjectCell` threw for a cell off the landscape (the jungle's guest
  map is larger than its heightfield). It now answers false for a cell off the grid (`LevelProjectCellTests`). The queue step's off-heightfield
  skips of #270 and #267 are unchanged.
- **The rebuilt ride** of the economy checks started closed (RIDES-015) like every built ride, so no guest boarded it and the rider-head readback never
  found a rider. The smoke opens the rebuilt ride as the player would.
- **The rides the smoke builds** are no longer reachable: since the placement lays only the cell outside the entrance (RIDES-028), no path touches
  a ride built on a free site and the park's guests never board it, so the rider-head readback (which needs a rider on a ride with heads, the
  built Crazy Ape) found nobody. The smoke now prefers a site whose entrance touches a path and joins the laid queue front to it, as ending a
  queue line there does (`QueuePaths.TryConnect`), also for the ride it rebuilds.
- **The Totem step** armed its motion check on the tail of the ride cycle the guest checks had just watched, and its start limit (30 s of script
  time) sat close to the 24-26 s the first riders take. It now waits for that cycle to end and allows 60 s.

No game behaviour changes except the false answer of `TryProjectCell` for off-grid cells, which no caller can use. It stays out of CI (no game
data there); the shared smoke script now runs it (about 85 s, four passes in a row).
