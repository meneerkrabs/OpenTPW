# TRACK-007: Replay directional track demolition

- **Superseded (October 27, 2026):** PR #879 (round 9 F) ports the clear op `0x53B280` and the replays from the corner stacks in full and retires `APPROX:TRACK-007` (docs/TRACKS.md, "The clear op"). The rest of this proposal is kept as history.

## Status and classification
- Classification: approximation.
- Original status: plan-only local proposal. Its bounded part merged in #593 on 2026-10-20 (exact-head CI green); see "October 20 bounded implementation" below. TRACK-007 is not retired.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-DEMOLISH: removing a kart or water ride refunds the track cell cost x the ride's scrap percentage / 100 per tile its two replays clear (ClearCell 0x53B280 credits it at 0x53B622 for a cleared type 11 or 16 tile; the replays run with flag 0x81B46C = 1, so no end tile is skipped) and then charges 2 x the cell cost x the scrap percentage / 100 once (0x4D01F0, 0x528157..0x528179), both only for a type with Info.HasQueue (def+0x40) as the original; the replays are the original's: the chain from the station's exit to the open end of the track (0x530830, which also resets the pass counters, 1 at a corner or junction and else 0) and the chain from the tile before the station (0x530B70), so a tile no chain reaches stays in the park and refunds nothing, and a junction a chain passes loses one pass only (TrackEditor.Demolish); approximated: the original replays each line between two corner points through the click handler's mode 0x35, whose per-tile arm writes for the line's step ((0x53B280 with (+-2, 0) or (0, +-2): the arm the line used goes, the other stays while a pass remains) are modelled as one clear per tile in chain order, with the cut's pass rule (0x53B280 with (-1, -1)) for a junction's arms

## Source inspection
- `source/OpenTPW/World/Level.Tracks.cs:150`: 151: private void OnTrackStationRemoving( OriginalObject item ); 152: {; 153: if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || BuilderOf( item ) is not { } builder ); 154: return;

## Bounded work
Use line-segment clear steps rather than cut (-1,-1) rule per tile; keep already-traced refunds and reachability.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace corner-stack replay boundaries and visit sequence; share clear primitives with TRACK-004.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Demolish junction with surviving pass and assert directional surviving arms/link and correct refund.
2. Disconnected unreachable tile remains and refunds zero; HasQueue false suppresses replay/charge as original.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## October 20 bounded implementation

TrackEditor now preserves the remaining flow of supported two-line full crossings when replaying the matching collected direction. TrackDemolitionFlowTests covers both collectors, kart/water families, rotation/save round trips, callbacks, add-on removal and the unchanged contrary-flow policy. This does not implement the complete native directional line replay, establish bank refund amounts or retire TRACK-007. The cleared bit is checked against tp.exe: with two or more link bits the line step `0x53B280` clears the bit opposite to the step (dx -2 → `0x04`, +2 → `0x40`, dy -2 → `0x10`, +2 → `0x01`) and both arms of the step's axis, and `0x52FE50` replays each line from the later collected corner point back to the earlier one, so the cleared bit is the chain's own heading, as `TrackEditor.Replay` clears it. The tests ran in exact-head CI before #593 merged; no original-media or rendered validation is claimed.
