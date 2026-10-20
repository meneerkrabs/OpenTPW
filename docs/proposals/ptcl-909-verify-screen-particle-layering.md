# PTCL-909: Verify screen particle layering

## Current bounded implementation (2026-10-20)

Stable weather-only deferral after the last park particle renderer; native HUD/advisor layering remains open. See [implementation contract](../PARTICLE-WEATHER-ORDER.md). Recovered the independently source-reviewed frozen change and integrated current main de0a3bff. Synthetic C# fixtures are included; exact-head CI and rendered validation are not yet claimed. No fidelity identifier is retired. The original plan below is historical and its plan-only description is superseded for this bounded slice; unresolved evidence gates remain.

## Historical proposal

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
screen-space particles (template byte +5: the advisor's ticket and key effects) are drawn inside OpenTPW's world particle pass, in slot order with the world particles and without depth, so the HUD and the advisor's image draw over them; the original's pass is the same loop (0x57C620: flag 0x40000 skips the transform and keeps w = 1; the draw list 0x51F01A), inside the scene view render 0x576a00 after the object lists and the view's callback (+0x78) and before the weather (0x57FF60), and which later view or callback draws the HUD windows and the advisor's model is not read, so they may draw over those layers there

## Source inspection
- `source/OpenTPW/World/Particles/ParticleRenderer.cs:159`: 162: var uniformsWritten = 0;; 163: foreach ( var (kind, first, count) in runs ); 164: {; 165: var material = kind switch

## Bounded work
Establish original callback/view draw ordering before moving screen particles relative to HUD/advisor.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace scene callback +0x78 and later window/advisor draws; account for #435 merged rendering changes.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Capture key/ticket particles overlapping advisor and HUD using identical particle slot sequence.
2. Interleaved world/screen slots retain original order and screen particles keep w=1; no blanket topmost overlay guess.
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
