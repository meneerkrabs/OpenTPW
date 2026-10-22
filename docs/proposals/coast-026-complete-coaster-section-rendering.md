# COAST-026: Complete coaster section rendering

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-RENDER: the track mesh is the original's extrusion of the coaster.sam cross sections (0x43eb70, 0x43f4e0, 0x43fd60): ten slices per link and one per tag of a pylon, a ring per slice the strips share, emitted again where the section changes, single-sided strips wound as the original winds them, crossings and the cursor link in the copy of the invalid cross section with the marker textures; approximated: the cross-section runs and the flumes' dry/wet blend sections (the run table builder 0x4402d0 steps the sample coordinate by 0.25, asks 0x43e9c0 for the section at each step and writes runs of 16 bytes at 0x792600 and 0x7923f0; the blend path of 0x43fd60) are not built, a section changing per segment by its mean pitch; self-illuminated and marker textures are lit like the rest; a link between two nodes a kept edit left invalid takes the red marker; vertices are shared per texture; scrolling textures shift V by the power factor x elapsed time as the original does, another reader of the stored scroll rate is not excluded; the coaster MD2 models' Add-mode UV animation (0x471860) is not modelled

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterCurve.cs:327`: 328: public static CoasterSlice[] Slices( IReadOnlyList<CoasterKnot> knots, float curviness, bool closed, int segments = SliceSegments, CoasterSuspendedRolls? suspended = null ); 329: {; 330: ArgumentNullException.ThrowIfNull( knots );; 331: var count = knots.Count;
- `source/OpenTPW/World/Coasters/CoasterRenderer.cs:25`: 26: public sealed class CoasterRenderer : Entity; 27: {; 31: internal static int? LinkMarker( IReadOnlyList<CoasterNode> chain, IReadOnlyList<IReadOnlyList<int>> crossings, bool cursorValid, int node, int next ); 32: {
- `source/OpenTPW/World/Coasters/CoasterRenderer.cs:43`: 44: if ( (chain[node].Flags & CoasterNodeFlags.Invalid) != 0 && (chain[next].Flags & CoasterNodeFlags.Invalid) != 0 ); 45: return CoasterTrackMesh.RefusedMarker;; 46: return null;; 47: }
- `source/OpenTPW/World/Coasters/CoasterTrackMesh.cs:19`: 20: public sealed class CoasterTrackMesh; 21: {; 23: public const float MetersPerRepeat = 6f;; 26: public static readonly string[] MarkerTextureNames = { "m_erase", "red", "blue" };

## Bounded work
Build sample-coordinate section runs and flume wet/dry blend; separately cover emissive marker flags and Add-mode UVs within renderer scope.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode 0x4402d0 run boundaries and 0x43fd60 blend vertices plus model UV additive timing.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Track crossing a pitch threshold mid-link emits runs at 0.25 sample steps instead of mean-pitch whole segment.
2. Compare dry-to-wet flume seam and self-illuminated red/blue marker under dark lighting; verify Add UV accumulation.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
