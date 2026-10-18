# AUDIO-904: Separate weather near clipping

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
the rain field lives within +-0.8 engine units of the eye, so the scene camera's 0.1 near plane (0.5 original units; kept for the weather as [EXT:weather-near-plane]) clips the nearest drops where the original's near plane is 0.1 original units (0x54e86f and the camera init 0x42a990 -> 0x578be0: near 0.1, far 1000, field of view 90) and its polygon clipper (clip codes 0x561bc0, clipping 0x562a70 in 0x565bb0) cuts the streaks there, drawing only drops in front of the eye (0x57f1f0)

## Source inspection
- `source/OpenTPW/World/Weather/WeatherRenderer.cs:20`: 22: public sealed class WeatherRenderer : Entity; 23: {; 25: internal static readonly string[] ImagePaths = { "/generic/weather/raindrop.tga", "/generic/weather/snow.tga", "/generic/weather/lightning.tga" };; 27: private const int VerticesPerQuad = 6;

## Bounded work
Specify original weather near plane and clipping versus intentional scene-camera near-plane extension.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Compare original clipper and port projection units; choose compatibility behavior without silently altering main camera.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Drop streak crossing 0.1 original-unit near plane clips continuously rather than vanishing whole.
2. Behind-eye drops remain absent; world terrain near-plane behavior stays unchanged.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
