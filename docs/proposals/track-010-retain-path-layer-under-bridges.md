# TRACK-010: Retain path layer under bridges

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-BRIDGE: a kart or water tile with at most two arms and no corner (straights, single-arm ends, armless tiles, 0x538AA4..0x538B05) is a bridge when it replaced path cells; the original tests the map layer under the track (a path or queue under any of its four cells, kept there and re-tested when a path is laid later) and only type 25 tests map types 2, 7 and 30 (water, outside the park and the fixed walkway); OpenTPW's track replaces the paths, so it remembers them when the tile is laid, writes no type 25, and takes the ahead and behind neighbours of the second pass from the flow link (an end or armless tile without a link along it takes the positive axis), whose PC twin is not traced

## Source inspection
- `source/OpenTPW/World/Tracks/TrackBuilder.cs:376`: 377: public void Refresh( int x, int y ); 378: {; 379: var region = new List<(int X, int Y)> { (x, y) };; 380: foreach ( var heading in TrackHeading.All )

## Bounded work
Model under-track path/queue retention and refresh on later path placement; type-25 remains separately gated.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace second-pass PC neighbor choice and 0x5370E0 operations that trigger bridge rewrite.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Place track then path and path then track: same bridge flag and underlying path survives.
2. Straight/single/armless tiles and corners obey distinct rules; type-25 water/outside/walkway rule only after type exists.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
