# RIDES-032: Resolve object update routes

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
every placed instance takes the callback update route; the PC list pass 0x44e410 (updates without posing via 0x473c70(inst, 0, 8), posing only with instance flag 0x400000) is not modelled, so an instance the renderer does not pose is posed here; the callback at model record +0x140 has no call site in the executable (no call or jump through that offset), so which path invokes it is not found

## Source inspection
- `source/OpenTPW/World/Objects/ObjectAnimator.cs:26`: 29: public sealed class ObjectAnimator; 30: {; 34: public const float DefaultTicksPerSecond = 30f;; 37: public const int NoAnimation = 12;

## Bounded work
Distinguish no-pose list update path from callback route and determine whether +0x140 callback is invoked externally.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace instance flag 0x400000 and unknown indirect invocation rather than assuming absent direct call means unused.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Visible/culled instances with same animation time exhibit correct update-without-pose behavior.
2. Toggle pose flag across frames and compare key-cache/clip advancement without double stepping.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
