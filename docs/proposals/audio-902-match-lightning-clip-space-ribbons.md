# AUDIO-902: Match lightning clip-space ribbons

## 2026-10-27 current-source supersession

This qualification supersedes the historical residual and future-work instructions below. Public main `eb8e3d2018eeeb726b5143caf51f75ce9f0681e1` includes merged [PR #874](https://github.com/meneerkrabs/OpenTPW/pull/874); [AUDIO.md](https://github.com/meneerkrabs/OpenTPW/blob/eb8e3d2018eeeb726b5143caf51f75ce9f0681e1/docs/AUDIO.md#L693-L707) retires AUDIO-902 and records the projection, mitred joins and quad order. This reconciliation reads public code and its evidence annotations; it does not repeat their original-instruction analysis.

[WeatherBoltRibbon](https://github.com/meneerkrabs/OpenTPW/blob/eb8e3d2018eeeb726b5143caf51f75ce9f0681e1/source/OpenTPW/World/Weather/WeatherBoltRibbon.cs#L13-L109) now keeps per-depth projected corners/directions, emits joins and the closing cap, and supplies the ordered quad. [WeatherRenderer](https://github.com/meneerkrabs/OpenTPW/blob/eb8e3d2018eeeb726b5143caf51f75ce9f0681e1/source/OpenTPW/World/Weather/WeatherRenderer.cs#L134-L139) projects each bolt point; its `AddBolt` path consumes those quads rather than creating unjoined world-facing quads. The proposal's missing implementation/projection/winding claims are superseded. The half-width is 1.3/(branch depth + 1) in clip units, with screen width additionally divided by w; it is not constant pixel width at different camera distances.

[WeatherClipTests](https://github.com/meneerkrabs/OpenTPW/blob/eb8e3d2018eeeb726b5143caf51f75ce9f0681e1/source/OpenTPW.Tests/WeatherClipTests.cs#L11-L147) already contains trunk/branch/repeated-point, clip-width and emitter-order fixtures. They were not run here, and their checked-in vectors are not a fresh original-runtime capture or GPU seam/winding acceptance. Additional backend/render-capture qualification remains separate evidence work, not a reason to repeat the old implementation plan or restore AUDIO-902. This historical proposal is not made merge-ready by this note, which requests no PR state change.

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the bolt follows tp.exe: the sky and ground points (0x512C50: the ground point is the strike's own x and z at y = 0, the sky point 300 up and offset by up to 50), the midpoint displacement and branch heading on the CRain stream (0x57E4B0, 0x57E810: stop threshold tested on each half, branch odds 9/512, depth limit 4), the colour pulse (0x57EE30) and the lifetime; but the ribbon is a camera-facing world quad of half-width 1.3 / (depth + 1) original units per segment without joins, where the emitter 0x57DD10 builds it in clip xy with joins between consecutive segments of one depth and a width of 1.3 / (depth + 1) clip units: it keeps per depth the previous projected point, direction (scaled to that width) and corner pair, a joint's corners are the point plus and minus the perpendicular of the normalised sum of the two directions around it (the point's clip z and w kept), the start corners use the first direction and the end corners the last, and a quad is emitted when the point after next arrives (the closing call 0x57EC50 -> 0x57DD10(0, end) emits the last quad from the corners two points back), the projection FUN_00578d20 and the quad's winding being the parts not yet matched

## Source inspection
- `source/OpenTPW/World/Weather/WeatherRenderer.cs:181`: 182: private bool AddBolt( WeatherBolt bolt, ref int vertexCount ); 183: {; 184: var eye = Camera.Position.GetSystemVector3();; 185: var grey = WeatherVisualState.BoltGrey( bolt );

## Bounded work
Replace world-facing unjoined quads with traced projected segment joins once projection/winding are established.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode FUN_00578d20 and final quad winding; keep existing stochastic bolt topology.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. At two camera depths compare constant clip-space width 1.3/(depth+1).
2. Sharp two-segment bends and terminal closing call must emit joined corners without cracks or inverted quads.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
