# BLUEPRINT-001: Raise occupied-cell markers

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
HEIGHT: marker faces drape over the terrain 1.5 above it and ripple with sin(phase + x + z) as the PC face builder 0x53DDD0 does for a plain cell (SetCell 0x532FC0 passes height 0), but a cell that already holds an object or track is not raised: the original gives a cell of map type 4, 7, 9, 10 or 30 or a second-layer cell 0x4D0AF0 of type 11, 12, 16, 17 or 25 the height 10 x ((int)H + 9) / 10 of the cell height H (0x452AE0: the owner object's .hmp, else the terrain sampler 0x4527F0) and OpenTPW's cell map does not type those cells

## Source inspection
- `source/OpenTPW/World/Original/BlueprintMarkers.cs:221`: 222: public static float VertexHeight( ModelHeightfield field, int cornerX, int cornerY, float phase ) =>; 223: field.GetCornerHeight( cornerX, cornerY ) + Lift + Ripple( phase, cornerX * FaceSize, cornerY * FaceSize );; 225: internal static (Vertex[] Vertices, uint[] Indices) BuildGeometry( ModelHeightfield field, IReadOnlyList<(int X, int Y, BlueprintMarker Marker)> cells,; 226: IReadOnlyDictionary<(int X, int Y), byte>? directions = null, float phase = 0 )

## Bounded work
Provide typed cell/object-height lookup so occupied footprint markers use original quantized raised height.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace first/second layer ownership and rounding for types 4/7/9/10/30 and 11/12/16/17/25.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Plain terrain remains terrain+1.5 with ripple; occupied cell follows height 10*((int)H+9)/10.
2. Check negative and boundary heights plus rotated object .hmp lookup; no whole-footprint constant lift.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## Reviewed bounded implementation update (October 20, 2026)

The initial proposal above is retained as historical scope, not current implementation status. Static PC evidence
now resolves quantization, first/second-layer selection and side-wall construction; see the October 20 section of
[BLUEPRINT.md](../reverse/BLUEPRINT.md). The host already types ordinary object footprints, contrary to the old
residual shorthand. The bounded change samples valid owner height maps on TPW primary types 4/9/10 and builds
raised tops plus stepped sides without changing placement or the marker-selection rules.

Remaining: missing/malformed/out-of-range HMP fallback, types 7/30, all secondary surfaces, and TPI parity. No
approximation is retired. Every nonzero track-layer cell and fence post is excluded from this owner-only path;
unknown surfaces retain their legacy unraised top and suppress unsupported side comparisons. There is no
BLUEPRINT-003 expansion, track-priority substitution, original-media fixture or original-program execution.

Acceptance is represented by synthetic BlueprintHeightTests, existing BlueprintGhostTests and synthetic rotated
height-map checks. Local build/test execution could not be verified: package restore attempted an unauthorized
metadata endpoint and was blocked; it was not retried or bypassed. CI and independent final source review must
establish the published candidate's acceptance. This is bounded implementation progress, not full-ID closure.
