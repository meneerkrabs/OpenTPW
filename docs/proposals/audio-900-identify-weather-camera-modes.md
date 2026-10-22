# AUDIO-900: Identify weather camera modes

## Status and classification
- Classification: evidence-only.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the rain and snow field follows the original CRain with its absolute parallax (wrapped fall - factor x the camera position, on the original's 1/16384 grid, 0x57efd0, 0x580013-0x5800b7) and its render states (0x567620: rain and bolts 0x200D52 source alpha over one without depth writes, snow 0x556 alpha blend with depth writes, all alpha-tested at 0x10 of 255), but the factor 0.3 (0x580003-0x58000b) applies in OpenTPW's free-walk and ride cameras, the mapping the listener uses for the same camera-state bits 0x16 of 0x790ab0 (EXT:listener-eye-mode); what those bits select in the original is not read (0x42AB20 toggles bits 2 and 4 from input events 2 and 4, the scene render 0x576a00 turns any of them into render flag 0x100 of 0x8bd508 and with it the alternative scene path 0x56ea90, and no instruction sets 0x10 alone), so which original views take 0.3 is not known

## Source inspection
- `source/OpenTPW/World/Weather/WeatherVisualState.cs:85`: 90: public sealed class WeatherVisualState; 91: {; 93: public const ulong VisualSeed = 0x51EA7E5D5EEDUL;; 96: public const int FieldSize = 1024;

## Bounded work
Resolve which original camera-state bits select rain/snow parallax factor 0.3.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace 0x790ab0 bits 2/4/0x10 through input and alternate render path; existing free-walk mapping is an extension.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Compare identical rain seed in park, free-walk and ride camera under known translations.
2. Assert wrapped fall/parallax remains on 1/16384 grid independent of chosen mode.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
