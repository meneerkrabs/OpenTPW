## The jungle level smoke reaches its ride checks again (October 16, 2026)

`--load-original-level jungle --smoke-test` needs the original game data, so CI does not run it. After the station-line and guest merges it failed on
the economy checks, because the rides the smoke builds are not reachable:

- **The rebuilt ride** of the economy checks started closed (RIDES-015, `0x4DB712`) like every built ride, so no guest boarded it. The smoke opens
  it as the player would.
- **The rides the smoke builds** have no path to them: the placement lays only the cell outside the entrance (RIDES-028, `0x528A70`), so a ride on a
  free site is reached only when a path touches that cell. The smoke now prefers a site whose entrance touches a path and joins the laid queue front
  to it, as ending a queue line there does (`QueuePaths.TryConnect`), also for the ride it rebuilds. The rider-head readback needs guests on the
  built Crazy Ape.
- **`Level.TryProjectCell`** answers false for a cell off the grid instead of throwing from the heightfield (the jungle's guest map is larger than its
  landscape), pinned by `LevelProjectCellTests`. Every caller is a screen-hit or ghost-label lookup that already treats false as "not clickable".

No assertion was loosened: the Totem start limit stays at 30 s of script time. The shared smoke script runs the jungle smoke (about 85 s).
