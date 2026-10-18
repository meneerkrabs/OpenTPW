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
