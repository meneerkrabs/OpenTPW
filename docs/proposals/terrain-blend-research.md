# Draft proposal: RIDES-014 terrain blending evidence and implementation gate

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: RIDES-014 / PATH-015.

## Observed evidence and current gap

`source/OpenTPW/World/Objects/ParkObjects.cs`, `GetBaseHeight`, computes the midpoint of terrain extrema without the original ground blend. `OriginalTerrain.BuildQueueModels` lifts vertices over existing terrain. The register names `0x467030`/`0x57ACE0` but still requests blend and slope-instance evidence.

Supplied object/queue WADs include `.hmp` data (the queue inventories have seven named profiles per theme). That establishes available input, not the blending algorithm or whether a given consumer uses it.

## Proposed bounded scope

Research-first draft. Produce a bounded evidence specification and fixture plan before any terrain mutation or renderer patch. Do not implement generic flattening and call it original behavior. This PR changes documentation only until the gate below is met.

## Work sequence

1. Inventory representative flat/sloped object and queue profiles and the existing HMP reader/consumers; keep assets private and publish only original test constructions/metadata where appropriate.
2. Resolve profile sampling, footprint limits, blend weights, rotation, adjacency and removal semantics from allowed evidence. Request a suitable original capture/fixture if local Data cannot establish them.
3. Identify whether the behavior is render-only deformation or changes simulation/picking/collision height. Propose the smallest shared boundary, with no tour-height rewrite.

## Acceptance and test plan (not yet run)

- An evidence ledger distinguishes known bytes, recorded code semantics, observations and unresolved assumptions.
- Synthetic fixtures cover slopes, four rotations, adjacency, overlapping influence, paths/queues and removal restoration.
- Original comparison evidence exists before a fidelity implementation claim; missing capture/runtime evidence remains a blocker.
- A later implementation passes picking/collision, save/load, native/browser visual and performance checks. No register retirement from the research document alone.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Research effort medium; implementation potentially large and deliberately uncommitted. Coordinate terrain, tour-height (TOUR-005) and queue animation owners. The available extraction contains Data, not an executable/reference runtime.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.

## Private-input identity (October 18 audit)

Relative paths below are inside the supplied local Data tree. SHA-256 pins audit inputs only; it does not authenticate their community-archive provenance or establish an original edition. No asset bytes are committed. WAD evidence above is a read-only directory/member inventory, not a gameplay test.

- `levels/jungle/queue.wad`: 99066 bytes; SHA-256 `d4955c8b99187af0d356b3bad9c39c87a4f07f4607824a2b058e8b699bc27770`.
- `levels/hallow/queue.wad`: 71938 bytes; SHA-256 `06dc7df1b7d50e0f7515a73ff26f166c894ad2692d20db61e04a30275a770ec2`.
- `levels/fantasy/queue.wad`: 150264 bytes; SHA-256 `2b51f5e5de61052ee36123cf3ee926be5f987ee0bbe6f3f3820904d81e6e72c5`.
- `levels/space/queue.wad`: 64453 bytes; SHA-256 `bf645dd6592aefda4abbe0c44c4c75a5d4660e0a8ae3d96e7237cc640a47721a`.

## Full-register coverage addendum (October 18, 2026)

This reviewed addendum extends the bounded initial plan to cover the remaining registered residuals. All acceptance work remains pending. Source snapshot independently verified by the proposal coordinator: `5ccddcab56ffb9bda4ded1821988a8492bee8943`.


## Status and classification
- Classification: approximation.
- Plan-only addendum: no runtime implementation, annotation retirement or merge performed.
- Existing PR: #446; residual addendum only.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
base height = (lowest + highest terrain vertex) / 2 under the footprint's shape cells as the original's 0x466D70; the ground under the ride is not blended flat as the original does (0x467030), and the instance words +0xF0/+0xF4 are taken as the shape size

## Source inspection
- `source/OpenTPW/World/Objects/ParkObjects.cs:470`: 471: public float GetBaseHeight( ObjectCatalogEntry entry, int anchorX, int anchorY, int rotation ) => GetBaseHeight( entry.Shape, Grid, anchorX, anchorY, rotation );; 474: internal static float GetBaseHeight( ObjectShape shape, IParkGrid grid, int anchorX, int anchorY, int rotation ); 475: {; 476: var lowest = float.PositiveInfinity;

## Bounded work
Existing #446 owns footprint ground flatten/blend; coordinate queue deformation dependency PATH-015.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode 0x467030 influence area and instance +0xF0/+0xF4 meaning before implementation.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Irregular footprint on sloped terrain gets original blended boundary and base midpoint.
2. Preview/cancel must not deform terrain; committed build and save/load preserve resulting heights.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Review existing PR #446 current head; use residual addendum, not duplicate proposal.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
