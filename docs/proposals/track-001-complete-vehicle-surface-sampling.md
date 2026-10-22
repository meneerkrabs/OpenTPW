# TRACK-001: Complete vehicle surface sampling

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TRACK-HEIGHT: the vehicles sample the object under each probe in the original's modes (0x450EA0: surface records, terrain + the header's +0xA0, the instance mesh) and the terrain mode reads the original's triangle sampler 0x4527F0 at the probe, as do the tile instances (0x463060: the midrange of the tile's 3 x 3 landscape vertices, 0x466D70); the surface-record mode is taken for a model with a dummy record of type bit 2 (the instance block's bit 8 the original tests is not read), faces count from either side and are baked with the full rest transforms, the instance-mesh mode reads every 0x8000 face of the model, the Halloween and space kart stations (face-sampled station models) use the straight piece's floor, and a cell with no track gives the terrain where the original samples its owner object (or keeps the previous height without one)

## Source inspection
- `source/OpenTPW/World/Tracks/TrackRenderer.cs:470`: 471: private float? SurfaceHeight( TrackCellLayer layer, float x, float y ); 472: {; 473: var cellX = Math.Clamp( (int)MathF.Floor( x / 10 ), 0, layer.Width - 1 );; 474: var cellY = Math.Clamp( (int)MathF.Floor( y / 10 ), 0, layer.Height - 1 );
- `source/OpenTPW/World/Tracks/TrackSurface.cs:71`: 72: public static TrackSurface FromObject( ModelFile model ); 73: {; 74: ArgumentNullException.ThrowIfNull( model );; 75: var records = new List<(int Mesh, bool FirstHit)>();

## Bounded work
Resolve instance-bit selector, one-sided faces, translation-only transforms and owner/station fallback.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace instance bit 8 and record iteration/first-hit rules before modifying rest-transform baking.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Back-facing triangle rejects; overlapping faces obey original order with node translation versus rotation fixture.
2. Halloween/space stations sample their own faces; unowned no-track probe retains previous height rather than terrain.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
