# TRACK-005: Match initial station line state

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-STATION: the station is the original's line of 0x52A1A0 from the attraction cell (the anchor): two tiles of type 13 or 18 on the footprint's rows 1 and 2 across the heading, the circuit starting at the first, the controller reference between the tiles (0x529E10, 0x545890) and the piece walks 0x52A490 and 0x52A700 starting where it does; the line ops of 0x53AAC0 are read: 0x80 writes the arms of a line (0x539220: each cell after the first gets the arm toward the previous cell and the previous cell the arm back, the first cell of a line none, because the step of a line's first cell is zeroed at the tool's start), 0x82 the flow link (0x539670), 0x83 the origin word of the tile and its three type 12 extension cells; approximated: the placement (0x524960 mode 4, 0x525254..0x525690) also lays a type 11 or 16 track tile at the exit cell and at the entry cell with the arm toward the station (0x87, the type op, 0x80, 0x82, 0x83 on each, the tool's first vertex at the exit), where OpenTPW writes both station tiles with both arms at once and lays the exit and entry tiles with the first and the last line, so the arms the exit and entry tiles hold before the circuit closes, the first line's start on an existing tile and what the placement charges for those two tiles are not reproduced

## Source inspection
- `source/OpenTPW/World/Tracks/TrackStation.cs:54`: 55: public static TrackStation FromPlacement( int anchorX, int anchorY, int rotation ); 56: {; 57: var heading = TrackHeading.FromRotation( rotation );; 58: var (first, second) = rotation switch

## Bounded work
Replace premature dual-arm station initialization with original exit/entry tile placement and cost timing.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace initial line start on existing tile and price accounting for station-adjacent two tiles.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Immediately after placement assert type/owner/arm/link for both station and entry/exit tiles in four rotations.
2. First partial line and final circuit close produce original arm transitions and charge each tile once.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## 2026-10-22 source reconciliation: retain the arm/order residual

This addendum supersedes the earlier snapshot's missing-adjacent-tile and price-timing description where it conflicts with the source below. The original proposal text above is retained as historical context, not a description of current implementation. This remains an approximation and a draft evidence package: no runtime change, fresh test execution, original-executable trace, parity claim, or annotation retirement is supplied here.

### Pinned provenance and present implementation

- Proposal inspected: PR #588, head `5de5b1c1cb0453fbebf78d407e8e712f2820346c`, proposal blob `a2abd91267ac13e6291aecaa698810723b6d14f4`.
- Current-source baseline inspected: main `16e4213ad5e45ee0d31bb5b15646c9a3a3f39364`. References in this addendum are to that commit, not moving main.
- [TrackBuilder.cs](https://github.com/meneerkrabs/OpenTPW/blob/16e4213ad5e45ee0d31bb5b15646c9a3a3f39364/source/OpenTPW/World/Tracks/TrackBuilder.cs), `PlaceInitialStation`: stamps the station, then attempts exit and entry in that order via `PlaceTile(..., free: true)`. The free flag skips the economy debit, not validation or the existing-tile overlap increment. A refused adjacent origin remains untouched; successful entry placement receives the station heading as its flow link. This is not an atomic four-origin transaction.
- The same file's `PlaceStation` still assigns both axial arms to each station origin directly. New adjacent origins acquire station-facing arms through `ConnectDangling`; this helper also examines compatible neighbouring origins. The ordinary empty-surroundings fixture therefore does not prove behaviour with every pre-existing neighbouring topology.
- [TrackStation.cs](https://github.com/meneerkrabs/OpenTPW/blob/16e4213ad5e45ee0d31bb5b15646c9a3a3f39364/source/OpenTPW/World/Tracks/TrackStation.cs), `FromPlacement`, still carries `[APPROX:TRACK-005]` for station writes by line modes `0x80`/`0x83`. Keep that residual. Source annotations are prior reverse-engineering evidence, not a newly reproduced trace.
- [TrackLineWriter.cs](https://github.com/meneerkrabs/OpenTPW/blob/16e4213ad5e45ee0d31bb5b15646c9a3a3f39364/source/OpenTPW/World/Tracks/TrackLineWriter.cs), `Run`: an existing origin is recorded without adding tile cost; new tiles add cost; consecutive reached tiles are linked, then reached tiles receive the line-direction flow link. This describes current code, not proof of original instruction ordering.
- [TrackEditor.cs](https://github.com/meneerkrabs/OpenTPW/blob/16e4213ad5e45ee0d31bb5b15646c9a3a3f39364/source/OpenTPW/World/Tracks/TrackEditor.cs) supplies the initial editing vertex, directional undo, protected initial ends, and demolition replay used by the tests below.
- [Level.Tracks.cs](https://github.com/meneerkrabs/OpenTPW/blob/16e4213ad5e45ee0d31bb5b15646c9a3a3f39364/source/OpenTPW/World/Level.Tracks.cs), `RegisterTrackStation`, routes fresh placement to `PlaceInitialStation`, existing registration to `PlaceStation`, and returns before stamping when resume state is present; legacy migration also uses `PlaceStation`. This file is read-only context for this addendum, not an integration-change target.

### Existing synthetic coverage mapped to acceptance cases

The six methods in [TrackInitialStationTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/16e4213ad5e45ee0d31bb5b15646c9a3a3f39364/source/OpenTPW.Tests/TrackInitialStationTests.cs) already provide reproducible source fixtures. All six iterate kart/water families and rotations 0/90/180/270. The assertions below were inspected, not executed in this reconciliation.

1. `FreshStationHasFreeAdjacentOriginsAndOneInitialVertexInEveryRotation`: acceptance 1 and the synthetic-fixture portion of acceptance 3. At anchor (30,30), zero cash, it asserts four origins / sixteen cells, origin types, ownership, zero overlap, station dual axial masks, exit opposite-heading mask with zero flow link, entry heading mask/link, extension types/parents/owners, a sole exit editing vertex and no undoable initial segment. These are explicit OpenTPW expectations; owner IDs are its extension, not a native owner-field parity claim.
2. `FirstLineChargesOnlyNewTilesAndReopenedUndoPreservesBothInitialEnds`: the first-partial-line cost portion of acceptance 2. Budgets for zero, one, and two new tiles assert the initial exit is existing, only affordable new tiles are charged (kart 400, water 500), and refusal is `NotEnoughMoney` until both fit. Reopened undo preserves both initial ends and their station-facing masks, refuses clearing/refunding them, and checks demolition refund versus separately rounded compensation. It does not close a circuit or assert native arm transitions at each operation.
3. `ImmediateAndZeroLengthUndoKeepStationFacingArmsWithoutRefundingInitialOrigins`: related regression protection for acceptance 2/4. It checks existing-exit overlap after a one-tile line, immediate and zero-length undo, one clear callback, four retained origins, and the initial end masks. It does not establish the original station-write sequence.
4. `FreePlacementDoesNotOverrideRefusedAdjacentCells`: acceptance 1 boundary case / acceptance 4 regression. A foreign-owned exit and a no-modify path at entry survive unchanged; station origins remain and placement does not debit the balance. This covers those two refusal inputs, not all terrain/ownership combinations.
5. `DemolitionConsumesBothVisitsOfACornerExitWithoutLeavingAnOrphan`: acceptance 4 regression. A perpendicular first line produces a corner exit with overlap; demolition clears four distinct non-station origins including both initial ends, leaves the two station origins, and a repeated demolition adds no clear callbacks. This is demolition idempotence coverage, not final-circuit-close evidence.
6. `SaveRoundtripRetainsInitialCellsButLegacyStationStampDoesNotInventThem`: acceptance 4 regression. Fresh and station-only inputs round-trip every track-cell record through save serialization/restoration, with sixteen versus eight cells and retained exit overlap. This is a cell-save fixture, not a full level-load or browser walkthrough result.

Acceptance 1 thus has explicit current-model assertions; acceptance 2 is partly covered (first-line cost and undo) but these six methods do not assert final circuit closure and operation-by-operation native state; acceptance 3 has synthetic fixtures but no newly established original reference trace; acceptance 4 still requires an actual focused test run and recorded result for the reviewed revision. No passing build, CI, browser run, or original-game comparison is claimed by this source-only ledger.

### Narrow next evidence step, before any implementation

Do not reimplement free adjacent placement, first-line charging, undo protection, refusal preservation, or save-cell retention merely because the historical proposal calls them missing. Do not duplicate the six fixtures above. Preserve the fresh/restored distinction and keep level integration changes outside this docs-only scope.

The remaining question is whether direct dual-arm station stamping and subsequent adjacent-origin connection reproduce the observable station/entry/exit state of the original's ordered line operations. A same-looking final initial layout alone cannot establish equivalence, nor does a different internal write order alone establish a gameplay defect.

Obtain a bounded, reproducible permitted reference for station creation and line-start/close operations, identifying executable/version and function offsets. Record inputs (family, rotation, anchor, pre-existing tile topology, and budget), operation order, and resulting type, arm mask, flow link, origin/extension association, overlap where meaningful, and debit events. Compare at observable boundaries: station placement, exit/entry writes, first line starting on the existing exit, partial failure, and final connection to the existing entry. Distinguish native fields from OpenTPW-only ownership bookkeeping. Include compatible pre-existing adjacent topology if claiming that boundary; the foreign-owner refusal fixture is not a substitute.

Only a demonstrated observable counterexample should motivate a narrowly owned runtime change and an additional non-duplicating regression. Until that evidence and actual test/review results exist, leave TRACK-005 open and keep this proposal draft. Do not execute or publish original executables/media or bypass access restrictions to obtain the reference.
