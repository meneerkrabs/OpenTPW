# TRACK-004: Complete junction clear links

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-X2LINK: the flow links of water crossings and tees are written as the original's line op does (0x539670: a tile with more than two arms ORs the line's direction into +0xD, so crossings get 0x14, 0x41, 0x50 or 0x05; the circuit walk 0x52A970 writes +0xD only at corners); not modelled: the clear op (ClearCell 0x53B280 -> 0x5227F0, which ANDs the line's direction bits out of +0xD) counts the overlap word (+0x20) down on every clear and, in its (-1, -1) form, clears the pair of the other axis' bits through 0x522730 (1 and 0x10 clear 4 and 0x40, 4 and 0x40 clear 1 and 0x10, by the link 0x522850), recursing on the extension cells (0xC, 0x11); OpenTPW removes the tile or counts the overlap down only, and the other writers of +0xD (the type-25 walk 0x52BA50 takes the first linked arm by rotation, the edit sub-modes 0x52B400) have no counterpart

## Source inspection
- `source/OpenTPW/World/Tracks/TrackPieceSelector.cs:135`: 136: if ( link == 0x14 ); 137: rotation = 90;; 138: else if ( link == 0x41 ); 139: piece += SecondFamilyOffset;

## Bounded work
Implement direction-specific and cut-axis clear semantics with overlap counts/extension recursion; coordinate TRACK-007 replay.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace remaining type-25 and edit-submode writers separately from known clear operations.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Crossing links 0x14/0x41/0x50/0x05 clear one axis while preserving surviving arms/count.
2. Clear (-1,-1) and directional (+/-2,0)/(0,+/-2) produce different expected masks and recurse extension cells once.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
