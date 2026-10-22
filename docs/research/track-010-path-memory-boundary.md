# TRACK-010: path memory is not an underlay

Public-source audit at `f4be81cbbe120d1638cfb0355a701bdac3d59f1e` (2026-10-22). This is bounded host regression coverage for proposal #594, not native acceptance or a runtime correction.

## Current data flow

- `TrackBuilder.PlaceTile` validates all four cells, clears ordinary paths with `ClearPath`, and replaces their primary type with track origin/extension types. It ORs the four clear results into one origin `FlagOverPath` bit. There is no per-cell path subset left in that bit.
- `TrackBuilder.RefreshOne` combines that bit with the current shape. The eligible cardinal masks are 0, 1, 4, 16, 64, 17 and 68. Corners, tees and crossings suppress the bridge nibble but retain the memory bit; changing back to a straight can restore the bridge.
- `TrackSave.Capture` records the bit in `TrackCellSaveData.Flags`; `RestoreCells` reconstructs track records then refreshes their pieces. `BaseType` is a byte, not the path links, queue direction, owner and placement count needed to reconstruct a full primary cell. Ordinary `PlaceTile` does not assign it.
- `ParkPathBuilder.Validate` applies `ParkCellMap.CanChangeCellType` to the primary type. A path over a track origin/extension is refused by that table. Merely refreshing bridges after a successful path write cannot solve this because this write is currently refused.
- `TrackBuilder.ClearTile` clears track cells and primary types. It has no saved per-cell underlay to restore. Retaining paths needs a coherent placement, walking, rendering, removal and save model, not just a changed bridge predicate.

These are statements about current source, not new executable traces. The existing TRACK-010 annotation records original-game evidence that the primary path/queue layer survives; this audit has not independently executed or disassembled that game.

## Added coverage

`TrackBridgePathMemoryTests` exercises the actual `PlaceTile` path, not only seeded track flags:

- All 16 subsets of the four-cell footprint, for both kart and water families, including empty and each single-cell subset.
- All 16 cardinal shape masks per placement (512 combinations), with explicit independent expected eligibility, complete flag checks, owner/mask preservation, selected-piece consistency and refresh idempotence.
- Crossing-to-straight recovery of the remembered path bit.
- JSON track-payload capture/restore for all 32 family/subset placements, while a corner hides the bridge nibble, followed by a straight refresh.

Existing TrackCircuitTests cover selected path clearing, single-arm and connected bridge examples. TrackBridgeRouteTests and TrackBridgeRouteLevelTests cover route-directed classification and restore. The new matrix targets footprint-subset acquisition and subsequent shape transitions; it does not duplicate #775 object placement refusal tests.

No assertion requires the destructive primary-layer behavior to remain forever. A later faithful underlay implementation can retain the behavioral cases while replacing the memory-bit assertions. The tests do not cover queues beneath tracks, later path placement, guest traversal beneath a retained primary path, full level resume or browser rendering.

## Remaining work before an underlay change

1. Trace the path/track writer and rewrite operations around the original `0x5370E0` route, including path-before-track, track-before-path, queue placement, removal and no-modify behavior. Preserve the distinction between context-free fallback and station-aware route normalization.
2. Inventory primary-type consumers and define how live track occupancy remains visible when the primary type is path/queue. Include guest routing, object/add-on validation, path rendering, demolition and original-save import.
3. Specify an explicit migration for old saves that retain only a boolean. Fifteen distinct nonempty footprint subsets collapse to the same bit, so inventing a specific lost layout is not lossless migration.
4. Test both build orders, all four footprint cells, path/queue metadata, removals, full resume and deterministic hashes. Type 25 and its water/outside/fixed-walkway rule remain separately gated.

## Verification

Source/API inspection and a Python enumeration of fixture expectations were performed. C# compilation, MSTest execution, browser execution and original-game comparison were not performed. CI must compile and run the tests before treating them as passed. Keep #594 open and its approximation annotation intact.
