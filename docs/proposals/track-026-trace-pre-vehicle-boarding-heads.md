# TRACK-026: Trace pre-vehicle boarding heads

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-HEADS: a kart's, boat's or bumper car's riders are drawn as head sprites at the head dummies (class 0x100000B1: numbers 1 and 2 in the karts, 1 to 5 in the wr_ring boats) through the ride-head renderer in the original's seat order (rider list order, 0x549C60, called by the vehicle creation 0x549DB0 for every family); 0x0044B410 is the PC twin of ADDHEAD (it marks the head record 0x14 bytes apart and has 0x00475A10 create the head thing, type 1 or 3 for a costume head; its callers are the seat code 0x549C60, 0x43B600, 0x548E80, 0x557D80 and 0x55D830), whose sprite the shared thing renderer draws (the sizes belong to the HEADS entries); the boarding guest between BUMP 1 and the car's creation is not traced

## Source inspection
- `source/OpenTPW/World/Tracks/TrackRenderer.cs:443`: 444: internal void CollectHeads( List<HeadSource> sources, int? onlyAttraction ); 445: {; 446: foreach ( var visual in visuals ); 447: {

## Bounded work
Investigate guest presentation interval between BUMP 1 and vehicle creation; preserve existing seat-order head sprites.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace guest/head thing ownership through 0x549C60 and creation transition before assigning new visual.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Guest boarding before vehicle exists has original visibility/position with no double head.
2. Creation transfers riders to sockets in rider-list order; hidden vehicles remove and restore heads once.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
