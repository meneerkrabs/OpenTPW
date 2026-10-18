# Draft proposal: TRACK-018 existing-track undo and valid-circuit reopen

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: TRACK-018.

## Observed evidence and current gap

`source/OpenTPW/World/Level.Tracks.cs`, `EnterTrackTool` and `FinishTrackEdit`, consume only `TrackEditor.FindOpenEnd().End`, not its `Corners`. `RewalkTrack` refreshes the controller and transitions tools without opening the attraction after a valid line/edit. The later `station.Open()` belongs to a debug helper.

The recorded original paths are `0x530830` (existing corner stack), `0x52FE50`/mode `0x35` (undo replay), and `0x4DF290` -> `0x4DF390` after a valid edit. Original kart/water definitions and track costs are available in the supplied Data.

## Proposed bounded scope

One coherent track-edit PR: seed the undo history from an existing open circuit and reopen a ride after qualifying successful line/edit operations. Restore/import must not open a deliberately closed ride. Do not rewrite unrelated station placement, junction rules or demolition approximations.

## Work sequence

1. Specify the corner-stack contract shared by `TrackEditor`, the line writer and `CellBuildTool`, including the station boundary.
2. Preserve clear direction, ownership, pass counters, add-on removal and monetary refunds while replaying an existing segment.
3. Route valid-edit reopening through the established attraction/economy/script path, gated by operation cause and ride state.

## Acceptance and test plan (not yet run)

- Extend `TrackEditTests`, `TrackSaveTests` and data-backed track tests for existing and newly drawn segments, junctions, station boundaries and add-ons.
- Repeated Backspace, cancel/re-enter and save/load preserve the remaining circuit and exact refunds without double credits.
- Valid line/edit operations synchronize model door, visitor bridge and economy; invalid circuits stay closed. Broken/upgrading/condemned and deliberately closed restored rides are covered explicitly.
- Preview/cancel do not mutate or charge. Keep any remaining TRACK-018 cut-hover limitation, the separate BLUEPRINT-003 track-symbol work, and TRACK-004/007 unresolved.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Coordinate with track owner and blueprint proposal; no particle or tour changes. Medium effort. Junction replay semantics must be evidenced rather than inferred from the existing approximate clear.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.
