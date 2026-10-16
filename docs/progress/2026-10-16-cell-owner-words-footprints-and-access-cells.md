## Cell owner words, footprints and the cells outside entrances and exits (October 16, 2026)

The park's cell map now follows tp.exe's placement commit (`0x528A70`), read via REA, and OpenTPW's placement writes what the original writes.

**Built**

- **The owner word.** Every cell has the original's owner word (`+0x10`): the cell word `y x 128 + x + 1` of the object's anchor, written on each footprint cell
  a placement types (Easymode's saved cells carry exactly these words on all 44 typed footprint cells of its rides, tested). The queue tool writes the ride's
  word on its queue cells and on the path cell a line connects to (pass `0x83`). `CellData.Owner` is optional in the save and validated (an empty cell may not
  carry one); old saves load.
- **Footprint stamping.** `@` is a NoModify type-1 path cell linked to its path neighbours, `+` and `#` carry the add-on flag `0x10` on the cell and its
  track-layer cell, and an entrance or exit cell takes the symbol's direction byte as queue link, turned by the rotation. Add-on cells are validated as the
  original's add-on mode does (a cell of a straight, unflagged tile of the selected ride), and a track end needs the cell beyond it on the map.
- **The cells outside an entrance and an exit.** The placement validates and lays them: a queue cell (owned by the ride, NoModify) outside the entrance of a
  type with a queue, a NoModify path cell outside its exit, and a NoModify path cell outside the entrance of any other type. The checks are main's
  (`ParkObjects.CheckAccessCells`); this branch adds the laying (`ObjectAccessCells.Lay`), free of charge and refused only when the balance is negative. Easymode's
  eight rides carry exactly these 18 cells (tested).
- **Area clear and track add-ons.** The area clear reads the owner word and the flag `0x10` as the original's `0x52BDD0` does; track add-ons are validated, stamped
  and removed headlessly (`Level.PlaceAddOn`) and the walker reads the flag into kind 12 and 13 sections.
- **Queue owner words.** The queue tool writes the ride's owner word on every queue cell it lays; the queue-link writer `0x53402E` is a BIN reference.

**Retired and narrowed**

- Retired: PATH-005 (the missing owner word) and RIDES-028 (the access cells); `tools/fidelity_retired.txt` records both.
- Narrowed: RIDES-031 (the footprint typing covers Theme Park World; Theme Park Inc footprints stay untyped), RIDES-011 (add-on stamp and validation, the `@` link,
  the entrance and exit link rotation and the track-end rule; the coaster module's marker `0x15` is not written), RIDES-018 (now only what main's access cells and
  path clearing leave: the second-layer link tests and the types with record `+0x288` = 1), TOUR-005 (the object route by owner word matches; the second-layer
  instances `0x53BF30` and the mesh rebuild remain), TRACK-008 (add-on placement, flag and removal built; run values, dummies, the Teleport hook, the walk after
  placement and the offer and sale UI remain) and QUEUE-014 (owner words and the object cover are built; a segment started from a clicked cell and a queue over a
  path remain). ECON-050's text now names the queue clear.

**Behaviour changes**

- **Demolishing a ride clears its queue** (`ObjectAccessCells.ClearQueue`), so the site can be built on again; the original's exact demolition writes are not read
  (ECON-050 stays open).
- **The entrance is the laid queue front.** Where the placement laid a queue cell outside the entrance, that cell is the ride's entrance. Before,
  `ResolveVisitorCells` fell back to the nearest path cell, which could be an island such as the ride's own exit cell.
- **The park-resume fixture** changed in three ways: its queue line skips the front cell the placement already laid; rides stay off the spine, because a ride now
  clears a plain path under its footprint; and wanderers are seeded only on live path cells. In the baseline fixture the first ride's queue never reached a path
  (its join cell was null) and guests worked only through the island-entrance fallback; the first queue now ends on the spine. The second ride's queue is still unconnected, as before.
- **Merge with main.** Main's whole walks (#244), its object-over-path clearing (`PathAt`, `ClearPath`, `ObjectPlacementPlan`) and its access-cell check are kept;
  the branch's duplicate hook (`AccessCells`, `CellsToCheck`) is dropped. One result changed: an object of lower overwrite priority outside an exit now gives way,
  as outside an entrance (the validator's priority test `0x535D66` comes before any path rule), where this branch had refused it.

**Decisions of the user**

- `[EXT:dev-one-click-remove]`: the developer panel's one-click remove tool (`Level.RemoveAt`: an object, else a queue cell, else a path cell) has no original
  counterpart and is labelled, not registered.
- The queue tool no longer asks a cell to touch the back cell (QUEUE-014): a free-standing queue cell is the ride's by its owner word, has no queue link and sits in
  no chain. The remove tool takes it alone; walkers, the queue walk, saves and validation handle it (a test saves and loads one).
- `[EXT:removable-access-cells]`: the NoModify cells a placement lays (the queue front and the path cells outside an entrance or exit) can be removed by the path and
  queue removal tools, where the original's cell clear keeps a NoModify cell (`0x5367D4`). The map start's initial path stays fixed. Afterwards the ride's entrance
  and exit resolve to the nearest path as for a ride nothing laid cells for, queued guests standing on the removed cells are set down and leave the queue
  (`GuestSimulation.SetDownOffQueueCells`, QUEUE-015), the cell can be built on again, and a demolition clears what is left. Tests: `AccessCellRemovalTests`;
  the sandbox smoke removes a laid queue front and a free-standing cell through the level.

**Determinism and saves.** The pinned hash is unchanged: the owner words are derived state and are not hashed, and the merge with main's whole walks did not move
the pin (main's `0xDB3EA9A7AF384B5B`; `DeterminismTests` passes unchanged with and without game data). Saves keep the optional `Owner`; old saves load.

**Verification.** 4964 tests pass without game data (579 skipped) and 5430 with it (113 skipped), after merging main through #266; the fidelity register
(`--write`, `--check`: 187 unresolved unique IDs), `tools/progress.py --check` and the evidence checks (`run_evidence_checks.py`) pass; the five native smokes pass offscreen
(front end, sandbox, the plain smoke and the park-resume capture and restore). The `--load-original-level jungle --smoke-test` mode, which the smoke script does not run, also fails on origin/main (in the HUD queue step on a cell off the heightfield, and, once that step skips such cells as this branch does, in the water track step: the station tiles' first column); on this branch it passes the new queue steps (removing the laid queue front, the entrance fallback, rebuilding, a free-standing cell) and fails at the same water track step, in code this branch does not touch.

**Still open (new IDs needed)**

- The placement's first kart or water track tile (option `0x81ECAC`, `0x5286A0`): fold into TRACK-005 or open an ID.
- The original demolition's exact cell writes (ECON-050 covers part).
- The `EXT:track-tile-owner` label may be wrong: the validator `0x535998` compares the second-layer origin's `+0x10` with the tool's ride; `TrackCellTypes.WaterAlternate`
  (25) is the hoarding, type `0x19`; `0x8187F8` is the upgrade selection set by `0x52F880`.
