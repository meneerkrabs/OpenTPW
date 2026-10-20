# PTCL-905: Complete particle render-state mapping

## Bounded runtime update (October 20, 2026)

A bounded runtime follow-up now implements only untextured non-additive world alpha0/>=F0 opaque/depth state after interpreter and AOT both measured the same five failures across 17 cases. Strict acceptance remains unchanged and fresh execution is required. Textured/fractional/additive/screen behavior remains outside that correction; PTCL-905 stays open.

The [synthetic particle framebuffer diagnostic](../PARTICLE-RENDER-ORACLE.md) uses the real ParticleRenderer and strict presented-pixel assertions across 17 cases. Its harness-only baseline was compiled and measured before this correction; fresh exact-head validation of the correction remains pending. Local Node acceptance-rule checks are not GPU execution. The original plan below is retained as historical evidence, not current implementation claims.

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
OpenTPW draws the quads in the original's order (slots ascending, newest particle first, world and screen-space particles together) as runs of one blend ([EXT:particle-blend-runs]); the original submits one batch per particle (0x565F90 -> 0x565240) with its own state word, which the decoder 0x567620 turns into: 0x1256 opaque (no blend, no alpha test), 0x556 alpha blend with an alpha test at 0x10/255, 0x200522 one-plus-one and 0x200552 source-alpha-over-one with that alpha test, 0x2756 alpha blend with an alpha test at 0xF0/255, all with depth writes unless flags 0x4000 add 0x800; an untextured particle is opaque (0x1256) when its alpha byte is 0 or >= 0xF0, else 0x556 (0x57C6FA-0x57C71B), and flags bit 4 (0x200522, +0x30 for 0x2000) and 0x20000 (MAGFILTER point) modify it; a batch whose state lacks 0x1000 and 0x2000 (the renderer mask 0x3000, 0x5667D0) has its triangles depth-sorted within it (0x565590 -> 0x565490, only in renderer mode DAT_008BD524 = 1, or 4 without 0x1000), but a batch is one particle's two coplanar triangles, so the sort has no visible effect and is not a difference; the state words and their alpha tests and depth writes are not reproduced

## Source inspection
- `source/OpenTPW/World/Particles/ParticleRenderer.cs:18`: 19: public sealed class ParticleRenderer : Entity; 20: {; 21: private const int VerticesPerQuad = 6;; 23: private const float UntexturedWidthFactor = 1f;

## Bounded work
Plan original state words, alpha thresholds, depth writes and point filtering after reconciling #425/#435.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Verify merged shader/material work and identify genuinely remaining state gaps; no stale blanket claim.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Exercise alpha bytes 0,15,16,239,240,255 under states 0x1256/0x556/0x2756.
2. Overlap additive/alpha/opaque particles with geometry; assert depth-write flags and point sampling while preserving slot order.
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
