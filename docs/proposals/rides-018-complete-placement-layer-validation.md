# RIDES-018: Complete placement layer validation

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
terrain, NoModify paths, overwrite-priority overlap (objects, paths and queues), owned-land and access-cell rules of tp.exe's validator 0x535670 (the access cells are laid by the placement, ObjectAccessCells); the second-layer link tests 0x53ae00/0x53ae90 and the types whose record +0x288 is 1 (their Bumper.WhichTrackType turns the access-cell option 0x81b0cc off, 0x46d605) are not modelled; guests on a path cell a placement clears are set down on the nearest path cell, where tp.exe's handling of a guest inside a new footprint is not traced; Level enforces economy purchases

## Source inspection
- `source/OpenTPW/World/Objects/ParkObjects.cs:191`: 194: public OriginalPlacementResult Check( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation, out ObjectPlacementPlan plan ); 195: {; 196: var overwritten = new List<OriginalObject>();; 197: var paths = new List<(int X, int Y)>();

## Bounded work
Add second-layer/access-rule tests and resolve guest displacement semantics before changing it.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode 0x53ae00/0x53ae90 and WhichTrackType access-option branch; trace guest relocation on overwritten path.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Place over conflicting second-layer link and verify exact refusal while valid add-on stays allowed.
2. Guest inside cleared path footprint follows evidence-based relocation; owned-land and NoModify regression remains intact.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
