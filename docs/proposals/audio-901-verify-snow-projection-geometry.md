# AUDIO-901: Verify snow projection geometry

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
snow draws the jitter version (((rand & 31) - 16) / 2048 a frame on every axis), as the PC always does (the swirl's flag 0x8bcbc8 bit 0x100 is never set), each flake one triangle over its atlas quadrant (apex at the top middle, base at the lower corners; 0x57f490) with its depth moved by 0.1501 (the depth-buffer value is [EXT:snow-depth-value]); the flake's base half-width is h x 0x8bcbcc (the display's height / width) in the original and w = h here, the same only if OpenTPW's aspect-correct projection stands for that factor, and the space of the vertex's z + 0.1 and fourth word z + 0.1501 (camera or clip) is not read

## Source inspection
- `source/OpenTPW/World/Weather/WeatherVisualState.cs:89`: 90: public sealed class WeatherVisualState; 91: {; 93: public const ulong VisualSeed = 0x51EA7E5D5EEDUL;; 96: public const int FieldSize = 1024;

## Bounded work
Establish flake aspect correction and coordinate space for depth offsets before modifying geometry.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Resolve z+0.1 and fourth word z+0.1501 and projection relationship to display height/width.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Render same flake at 4:3 and widescreen and compare pixel width/height to reference.
2. Test near/far flake intersections and depth writing while preserving triangle atlas quadrant and jitter seed.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## 2026-10-22 source reconciliation

This addendum supersedes conflicting historical implementation claims above. Source baseline: main `1c874922df6e42425dc5638f7e2554c9fde2c68c`. This is source inspection, not a new original-executable trace or an executed C# / browser test. The proposal remains draft; no whole-identifier retirement is claimed.

### Corrected evidence boundary

[WeatherVisualState.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/Weather/WeatherVisualState.cs) now records `0x57f490`'s vertex as homogeneous clip-space `(x, y, z + 0.1, w = z + 0.1501)`, supported by the clipping comparisons at `0x565bb0` / `0x561bc0`. The old AUDIO-901 annotation immediately below still says that space is unread; that part is stale relative to the newer source evidence. This review checked those public annotations, not the original binary.

`WriteSnow` still emits a triangle using equal camera-space half-extents and moves the centre depth by `SnowProjectionBias`; it preserves atlas quadrant selection and jitter. [WeatherRenderer.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/Weather/WeatherRenderer.cs) converts the generated camera-space vertices to the host view and projects them through the scene camera.

### What remains unproven

- The original x half-width has the display height/width factor. Establish, at both 4:3 and widescreen, whether the host projection's aspect correction yields the same pixel dimensions. Equal camera-space half-extents alone do not prove equivalence.
- The explicit `EXT:snow-depth-value` limitation remains: native depth is `(z + 0.1) / (z + 0.1501)`, whereas the host scene projection determines its depth. The precompiled browser weather shader does not expose the required clip-space vertex route. Do not silently fold this platform extension into a proven AUDIO-901 match.
- Use synthetic flakes with fixed input/jitter and known projection matrices for a deterministic screen-space oracle, then validate near/far scene intersections on supported renderers. Record platform, viewport, projection and depth behavior. This source reconciliation supplies neither those captures nor their test results.

The next action is the bounded projection oracle and aspect comparison, not another investigation of whether the vertex's fourth word is camera or clip space. Change geometry only after a measurable discrepancy is established; preserve the independent browser depth-path limitation and the existing atlas/jitter contract.

