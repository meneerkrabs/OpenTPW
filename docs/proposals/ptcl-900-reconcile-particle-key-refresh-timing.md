# PTCL-900: Reconcile particle key refresh timing

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
The key's pose is read from the current animator hierarchy when the EVENT runs; the original reads its key table (0x556B90), which 0x44AB30 refreshes (gated by instance-header bit 0x1 and table bit 0x1) when the instance is created (0x463851, after animations 0 and 13 start), when it is drawn (render callback 0x463030 -> 0x44E380 -> 0x473C70 -> 0x473E1B) and at 0x42A6D6 and 0x438A53, so the frozen start position differs from the original's by the animation advanced since the last refresh and is not stale while the object is culled

## Source inspection
- `source/OpenTPW/World/Particles/ParticleKeyResolver.cs:49`: 50: public ParticleKeyResult Resolve( int key, uint selector, IReadOnlyList<Matrix4x4> nodeTransforms,; 51: ObjectPlacement placement, bool includeDirection = false ); 52: {; 53: ArgumentNullException.ThrowIfNull( nodeTransforms );

## Bounded work
Model original cached key refresh cadence only after coordination; distinguish event-time pose from last draw-time pose.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace instance/table bit-1 gates, creation and non-draw refresh callers against #425/#435 state.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. EVENT immediately after animation advance uses captured last-refresh transform when original does.
2. Cull object across several frames then emit: test cached origin and resumed refresh without rerolling particle stream.
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
