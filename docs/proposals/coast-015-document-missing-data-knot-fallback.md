# COAST-015: Document missing-data knot fallback

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
COAST-TAGS: the knot pass poses each pylon model with its clips and takes the node's knots from the posed track tags of its table (0x430ed0, 0x4344c0, 0x4321c0; the stack pass 0x432d50 gives a stack's lower nodes the 0x100 mark and, on a model with two tables, the three-tag one) and the station end node's knot from S0's posed track dummy; approximated: without the pylon model (no game data) every node is one knot at its position along its tangent and the pylon heights are fMinHeight and fMaxHeight

## Source inspection
- `source/OpenTPW/World/Coasters/CoasterDefinition.cs:410`: 411: private static void SetMeshHeights( CoasterPylonControl pylon ); 412: {; 413: pylon.MeshHeightLow = pylon.MinHeight;; 414: pylon.MeshHeightHigh = pylon.MaxHeight;

## Bounded work
Classify model-free one-knot/min-max fallback separately from shipped-data fidelity; improve only if concrete original behavior with absent assets is proven.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Establish original failure/fallback when pylon MD2 is unavailable, rather than assuming model-free parity.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. With real pylon models assert posed multi-tag knots and station dummy are used.
2. With model unavailable assert deterministic safe fallback and explicit unsupported-fidelity diagnostic, not crash or fabricated tags.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
