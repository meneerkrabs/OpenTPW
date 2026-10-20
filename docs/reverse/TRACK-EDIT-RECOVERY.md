# Bounded track edit recovery (TRACK-018)

October 18, 2026. This implements existing-track Backspace and reopening after explicit valid line/edit operations. It does not retire TRACK-018, TRACK-004 or TRACK-007, or implement the BLUEPRINT-003 cut-hover symbols.

## Contract

- Entering the line tool calls `TrackEditor.FindOpenEnd`, including its existing pass-counter reset, and seeds `CellBuildTool` with the recorded corners and one segment between each pair. A repeated first corner is intentional: its zero-length final replay removes the remaining exit tile, not the station.
- Backspace walks from the last point to the preceding point. Only the selected family's non-station origin tiles compatible with the ride owner are changed. Corners and junctions consume a pass before removal; a zero-length replay forces the final clear. The existing protected entry and station-repair rules are retained.
- Only an origin actually removed invokes the existing refund and add-on removal hooks. New lines and collected old lines use this same path in the live level tool. The standalone line writer without an editor retains its existing undo behavior.
- Re-entry rebuilds history from the remaining map. Preview and cancel do not clear or refund. Tool history is not persisted: loading the saved track cells and re-entering reconstructs it.
- `RewalkTrack` itself may reopen only for `TrackWalkCause.Line` and `Edit`. The controller must have accepted the rebuilt circuit, and the ordinary attraction opening guard must allow its state, maintenance status and queue connectivity. Model and economy door state synchronize immediately; the visitor bridge reads that door and the controller validity. Restore, placement and add-on causes do not reopen within `RewalkTrack`. The existing `WalkAfterAddOn` wrapper separately retains its valid-ride opening through `Gate.MayOpen`; this patch does not change that behavior.
- An invalid explicit edit closes the model and economy door as a conservative consistency extension (`EXT:track-edit-invalid-close`). The observed valid-walk opening calls do not by themselves establish the original invalid-walk door behavior.

## Static evidence, not an executable oracle

A locally supplied reference archive yielded only a partial executable image: extraction ended with a fatal read result before EOF. No software from it was executed. Each function interval below was mapped through the PE section table and checked to lie wholly within the available prefix before static disassembly. This establishes the local instruction paths, not the identity, integrity or behavior of a complete executable; no original binary, private archive metadata or disassembly bytes are included here.

- `0x52FE50`: Backspace refuses a one-point stack, pops its final point, switches to mode `0x35` and clicks the removed point (`0x52FF3A`) and new top (`0x52FFB2`). Station repair follows at `0x530005`.
- Mode `0x35` passes line op `0x32` at `0x526001` to `0x53AAC0`. The line steps by two cells back toward the preceding corner. `0x538CCE` supplies that step to the directional clear `0x53B280`.
- `0x53B36E..0x53B38E` forces the pass word negative for a zero step or decrements it for an ordinary replay. The branch at `0x53B3F3` uses `0x522860`, which counts the cardinal bits of the **flow link**, not the arm mask.
- With two or more flow bits, `0x53B3FF..0x53B477` removes the flow bit opposite the replay direction and both mask arms on that axis. Otherwise `0x53B47C..0x53B543` protects the end, removes only the opposite arm and reverses the remaining flow link when a former corner has one arm left. This is different from the cut's `(-1, -1)` clear and is implemented separately; demolition remains unchanged.
- After a valid edit, `0x526C54` calls the opening guard `0x4DF290` and `0x526C69` calls `0x4DF390`. The valid-line path has the same pair at `0x526F1E` and `0x526F2D`.

## Remaining qualifications

The reused station layout and protected-end mapping remain the existing approximation. The shared tool holds 1024 points while the original track collector holds 1025. When the complete recorded end is available, the tool retains only the most recent 1024 points; if the collector overflowed before recording the end, it seeds only that end and does not invent a segment across missing corners. Ownership checks and these bounds are conservative OpenTPW safeguards. Full original replay, pathological junction/overflow and restore equivalence still need an executable or independently recorded oracle. No new fidelity conclusion applies to TRACK-004's walk or TRACK-007's demolition. The cut-hover marker remainder stays with BLUEPRINT-003.

## Regression coverage and execution status

`TrackEditRecoveryTests` adds asset-free cases for both families, the repeated first corner, old and new lines, directional crossing preservation, exact per-clear refunds, add-on hook uniqueness, station/foreign protection, cancel/re-entry and saved-cell reconstruction. `TrackAddOnLevelTests` uses its existing CPU-only level fixture for live cut/mend ingress, model/visitor/economy synchronization, all operation causes, broken/condemned/upgrading/maintenance/queue guards and deliberately closed snapshot restore. `TrackEditRecoveryDataTests` optionally uses the player's own data to check shipped family costs and used-ride scrap rows; it is inconclusive when `OPENTPW_GAME_PATH` is absent.

These C# tests were added but could not be executed in the implementation environment because `dotnet` was unavailable. The tests are specifications until run, not pass claims. Before merging, run the focused track tests, the relevant aggregate suite and current-head CI, and independently review the implementation.
