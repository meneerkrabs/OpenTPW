# TRACK-002: Audit height-map consumers

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-HMP: the .hmp height profile is the original's per-object height map (loader 0x451640 from the mesh loader 0x4629D0, rebuilt from the mesh by 0x451880 when missing or mis-sized); the vehicles' sampler does not read it (0x4511A0 samples faces or the terrain sampler 0x4527F0, TRACK-001) and neither do OpenTPW's vehicles; the kit parses it; the readers of the object's map (+0xCC) are the cell-height lookup 0x452AE0 (the cell owner's map byte x 100/255 plus its base height, else the landscape; callers 0x45CDC0, 0x532FC0, 0x536CA0, 0x5370E0 and the tour code 0x55ABF0, 0x55C350, 0x55CC00, TOUR-005), the instance bounding box 0x466B70 (the map's +0x1C/+0x28 extremes plus the instance height; callers 0x556AF0, 0x556B90, 0x556DE0, 0x5587F0, 0x559060, 0x5597A0, 0x55A100), the ride preview 0x4689F0 (0x486410 from 0x4AB1B0) and the cell-flag copy of the mesh creation 0x463060 (bit 0x80 cells of the map written into the landscape cells; every shipped map's flags are zero), none of which belongs to the kart or water vehicles; what those readers decide in OpenTPW's tour, ghost and bounding-box code is not traced

## Source inspection
- `source/OpenTPW/World/Tracks/TrackPieceKit.cs:76`: 77: public HmpFile? GetHeightProfile( int piece ); 78: {; 79: if ( StemOf( piece ) is not { } stem ); 80: return null;

## Bounded work
Evidence-only consumer audit: vehicles correctly do not read .hmp; attribute residual to tour/ghost/bounds/preview owners.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace map rebuild 0x451880 and all +0xCC consumers in current port; avoid inventing vehicle profile implementation.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Malformed/missing map fixture identifies fallback for bounding box and preview separately.
2. Tour lookup tests live under TOUR-005 and ghost elevation under BLUEPRINT-001; shipped zero flags do not prove nonzero behavior.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
