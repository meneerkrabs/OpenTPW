# RIDES-021: Extend build picking to objects

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the build cursor's cell is the cell of the ray's nearest terrain hit (TerrainRayPick: the original's heightfield triangle planes, 0x45bf90; the cell is the hit divided by the cell size, 0x45d560, and the click command 0x46c5d0 hands it to 0x524790); the original's nearest hit also tests the model triangles of the objects in the cells the ray crosses (0x45cdc0, 0x45d090: a node's 8-byte face records unless node state 0x10 or 0x8210, children unless 0x20, the plane hit inside the triangle by the 2 pi angle sum, accepted only when nearer than the terrain hit; which object a cell yields, 0x452ae0, and the distance window are not read), so the cell moves onto an object that covers the terrain hit, and it keeps the previous hit when the ray finds nothing; the port picks the terrain only (the generic sandbox has no heightfield and picks on the Z = 0 plane, [EXT:build-cursor-ground-plane]); the anchor taken from the clicked cell is the original's (0x528b44), so the click is local (W/2, H/2) when each side is odd or at most 5 cells (the only shipped exception is space 3104 Orbiter, 6 x 3: local (3, 1) at 0 degrees, (2, 2) at 90)

## Source inspection
- `source/OpenTPW/World/Level.Objects.cs:1227`: 1228: public OriginalObject? PlaceObject( ObjectCatalogEntry entry, int x, int y, int rotation ); 1229: {; 1230: if ( IsReadOnlyVisit ); 1231: {

## Bounded work
Add nearest object-triangle ray hit and retain previous hit when none found; keep sandbox plane explicit.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace cell owner resolution and allowed distance window for 0x45cdc0/0x45d090.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Near object face occludes farther terrain; skipped node flags 0x10/0x8210 and child flag 0x20 are respected.
2. No hit retains prior cell; rotated 6x3 Orbiter anchor remains original local (3,1)/(2,2).
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
