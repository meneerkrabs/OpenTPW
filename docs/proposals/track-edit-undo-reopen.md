# Draft proposal: TRACK-018 existing-track undo and valid-circuit reopen

## Implementation update (October 18, 2026)

This draft now implements existing-track Backspace and guarded Line/Edit reopening. Independent source review passed; documentation explicitly preserves the separate existing add-on wrapper reopening. Local C# execution is unavailable because dotnet is absent; exact-head CI and gameplay evidence remain pending. TRACK-018 stays open for residual replay/bounds qualifications and BLUEPRINT-003 cut markers. The historical proposal below retains its still-open acceptance scope.

## Historical status and boundary

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

## Full-register coverage addendum (October 18, 2026)

This reviewed addendum extends the bounded initial plan to cover the remaining registered residuals. All acceptance work remains pending. Source snapshot independently verified by the proposal coordinator: `5ccddcab56ffb9bda4ded1821988a8492bee8943`.


## Status and classification
- Classification: approximation.
- Plan-only addendum: no runtime implementation, annotation retirement or merge performed.
- Existing PR: #441; residual addendum only.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-ANIM: the pieces' arm masks and their m clips play as the original triggers them (BUMP 17, the placement, line and edit walks), and the renderer draws texture-coordinate clips only, which is all the shipped pieces need (the Crypt Karts' bridge clips gk_trckH*m carry one texture-frame track on the one-frame slot 0 whose 20 keys all name frame 0, the bind the piece already has, and no member of the archive animates the flame slot); the track edit sub-modes 0xE and 0x13 of the ride window's track button are built (TrackEditor, TrackEditWriter: the pick test 0x539760, the cut 0x52B400 with the clears 0x53B280, the end re-links 0x53BC60 and the station repair 0x52FC80, the walk 0x526BD0 that starts or stops the pieces, and the line tool from the open end 0x530830); not built: Backspace in the track tool takes back only the lines this tool laid, where the original pops the corner stack that 0x530830 filled from the existing track and replays it (0x52FE50, mode 0x35, the replay the demolition uses), the hover's marking of the cut's tiles (the ghost op 0x10F of 0x52B400) is drawn as plain blueprint cells, and the ride is not opened again when a cut or line leaves a valid circuit (0x4DF290 -> 0x4DF390 after the walk, which the ride's door button does in OpenTPW)

## Source inspection
- `source/OpenTPW/World/Level.Tracks.cs:259`: 260: public bool EnterTrackTool( OriginalObject station ); 261: {; 262: if ( IsReadOnlyVisit || waterTracks == null || !trackStations.TryGetValue( station, out var layout ) ); 263: return false;

## Bounded work
Existing #441 owns undo/reopen and #445 owns marker remainder; no duplicate runtime proposal.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Verify current heads jointly cover original corner-stack Backspace and ghost op 0x10F.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Reenter old open track and Backspace removes original last line with correct refund/masks.
2. Cut leaving valid circuit reopens ride, and hover uses correct cut markers before click.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Review existing PR #441 current head; use residual addendum, not duplicate proposal.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
