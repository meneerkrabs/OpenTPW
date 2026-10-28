# PTCL-904: Match particle clip-space sizing

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
A particle's quad half-height is size / 1024 original units as in tp.exe 0x57C620 (the half-extents are added in clip space after the world-to-clip transform); the width multiplies it by the frame record's float +8, the frame's width / height (0x588660), for a textured quad (OpenTPW uses the same ratio) and by DAT_008BCBCC, the display's height / width (0x582EB0), for an untextured one (0x57C71F); the original has no height / width factor on a textured quad, so it is (width / height) x (display width / height) on the screen unless something rescales +8 later (nothing found), where OpenTPW keeps the frame's pixel aspect; OpenTPW's own world scale (half-height in world units under its 55 degree camera, a screen-space quad squared on the target) is [EXT:particle-world-scale]; the sprite UVs cover the whole frame

## Source inspection
- `source/OpenTPW/World/Particles/ParticleRenderer.cs:231`: 232: var (halfWidth, halfHeight) = HalfExtents( sprite.Size, scale, sprite.FrameCount, found.Width, found.Height );; 233: if ( !(halfWidth > 0 && halfHeight > 0) ); 234: return false;; 235: var angle = sprite.Spin * (MathF.Tau / 65536f);
- `source/OpenTPW/World/Particles/ParticleRenderer.cs:257`: 258: private bool AddScreenQuad( in ParticleSprite sprite, Batch batch, float screenAspect, ref int vertexCount ); 259: {; 260: var frame = sprite.FrameCount == 0 ? atlas.White : atlas.Find( sprite.SpriteCode, sprite.Frame );; 261: if ( frame is not { } found )

## Bounded work
Resolve textured aspect behavior and separate world-scale extension; coordinate with current particle owner.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Confirm no later frame +8 rescale and how clip-space half-extents project; rebase after #435.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. At 4:3 and 16:9 compare textured non-square frame versus white untextured quad.
2. Screen and world sprites preserve expected aspect across depth, spin and frame changes without altering slot order.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- Claude currently owns particle/tour runtime. This document is planning only; agree file boundaries before runtime work.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Coordinate with Claude, reconcile merged #425 and #435 before any runtime work.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## Source-only reconciliation (2026-10-27)

The preceding plan is preserved as the 2026-10-18 baseline, not a statement that the aspect implementation is still missing. At reviewed main `eb8e3d2018eeeb726b5143caf51f75ce9f0681e1`, `docs/PARTICLES.md:154-160` and `tools/fidelity_retired.txt:224` record PTCL-904 as retired by merged [PR #874, weather and particles](https://github.com/meneerkrabs/OpenTPW/pull/874) (merge `35b78bff94a34273ad1708481e713f61ac6f925b`). Implementation and retirement were added in `66f15a77e4c46685abae2cfcdd97e7e74fc47488`.

- Current source at that main: `source/OpenTPW/World/Particles/ParticleRenderer.cs:251-270,282,312` implements and consumes `TexturedWidthFactor` (frame aspect divided by display height/width) and `ScreenHalfWidth` (frame aspect for textured quads, display height/width for untextured quads). The separate `particle-world-scale` extension remains at line 280.
- CPU regression source in `source/OpenTPW.Tests/ParticleSystemPortTests.cs:727-759` covers half-extents, textured aspect at 4:3, 16:9 and square displays, and screen-space width factors. It was inspected, not executed here. The no-later-frame-rescale claim is the merged source's recorded analysis, not a new binary inspection or original-game observation by this reconciliation.

Proposed disposition: treat the missing-aspect-implementation premise as superseded; do not duplicate the merged runtime work. Preserve the original evidence, acceptance and completion gates and draft status pending a separate disposition review. This note supplies no fresh CPU-test pass, GPU/native acceptance, original-game trace or retirement decision; it changes no gameplay, register or progress fragment and opens no additional GPU work.
