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
