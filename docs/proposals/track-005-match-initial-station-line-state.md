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
