# ADVISOR-005: Resolve advisor frontend lighting

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the advisor is lit like the original (advisor.shader: ambient 0.15 grey, 0x573FD0, plus the directional light of colour 1.0, 0x429BA0, per vertex, clamped; 0x574660 -> 0x5741B0), with the ambient of an ordinary park; the ambient words 0x879DD8-0x879DE4 are (alpha 1.0, R, G, B), 0.15 grey by default (0x573FFA-0x57404F), and the frontend states set them: black by 0x5D8B50 and 0x5D8D70 (0x5D8C3B-0x5D8C51, 0x5D8D7B-0x5D8D99), a colour by 0x5D9230(1, rgb) from 0x5DCFE0 (0x202020, 0x5DD078) and at 0x5E13A9, 0x5E1924 and 0x5E2445 (0x444444); which lobby screen each state is, which of them show the advisor, and whether the original fogs the advisor model are not read, so the park ambient is used and no fog is drawn

## Source inspection
- `source/OpenTPW/World/Advisor.cs:604`: 605: g_vLightPos = LightPosition,; 606: g_vLightColor = System.Numerics.Vector3.One,; 607: g_vCameraPos = System.Numerics.Vector3.Zero,; 608: g_flTime = Time.Now,

## Bounded work
Map frontend states to ambient and fog settings, preserving ordinary-park lighting.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Identify which lobby states actually draw the advisor and their fog pipeline.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Compare park default 0.15 against black/0x202020/0x444444 frontend states.
2. Capture the same mouth pose with and without scene fog; do not infer fog from world rendering.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
