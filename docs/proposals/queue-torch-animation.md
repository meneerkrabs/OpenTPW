# Draft proposal: PATH-015 queue torch texture animation

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: PATH-015.

## Observed evidence and current gap

`source/OpenTPW/World/Original/OriginalTerrain.cs`, `BuildQueueModels`, bakes queue models into static batches. It explicitly leaves texture-frame animation out. `source/OpenTPW.Tests/ObjectTextureFrameOriginalDataTests.cs` demonstrates the existing object animator/texture-frame machinery; that is reusable infrastructure, not proof that queue animation already works.

Read-only member inventories of the supplied original Data verify `queendm.MD2`, `quebin1m.MD2`, `quebin2m.MD2` and `fire0.wct` through `fire04.wct` in Jungle, Halloween and Fantasy `queue.wad`. Space has the queue models but not that fire texture set. Presence proves available input, not the exact animation-start rule.

## Proposed bounded scope

Bind the queue models' existing texture-frame tracks to the rendered material slots. Keep static geometry batched and avoid rebuilding the whole queue each frame. Preserve pause/game-speed semantics once the activation and clock evidence is pinned. Do not invent a torch cycle for Space or add point lights/night behavior.

## Work sequence

1. Census all queue clips and referenced frame names, recording which actually change a frame.
2. Identify the original activation/clock binding from recorded loader evidence; leave it unresolved if that evidence is insufficient.
3. Add the smallest presentation-only binding and bounded resource lifetime. Do not alter simulation RNG or state hashes.

## Acceptance and test plan (not yet run)

- Data-backed tests cover all four themes, referenced frame availability, static slots and frame selection across a complete clip.
- Missing, malformed or out-of-range clip/frame data has a bounded, diagnosed fallback without renderer failure.
- Rendered frames demonstrably change on torch-bearing queues; pause, speed and removal/rebuild do not desynchronize or leak resources.
- Native and browser captures show the same intended frames. A large-queue frame-time/allocation comparison shows no per-frame geometry rebuild.
- Keep PATH-015 open for terrain deformation and the `quedead` load flag; no blanket retirement.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Coordinate with renderer/evening owner (#439) before runtime changes. Particle emitters are outside this slice. Medium implementation effort after the animation-clock evidence check.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.

## Private-input identity (October 18 audit)

Relative paths below are inside the supplied local Data tree. SHA-256 pins audit inputs only; it does not authenticate their community-archive provenance or establish an original edition. No asset bytes are committed. WAD evidence above is a read-only directory/member inventory, not a gameplay test.

- `levels/jungle/queue.wad`: 99066 bytes; SHA-256 `d4955c8b99187af0d356b3bad9c39c87a4f07f4607824a2b058e8b699bc27770`.
- `levels/hallow/queue.wad`: 71938 bytes; SHA-256 `06dc7df1b7d50e0f7515a73ff26f166c894ad2692d20db61e04a30275a770ec2`.
- `levels/fantasy/queue.wad`: 150264 bytes; SHA-256 `2b51f5e5de61052ee36123cf3ee926be5f987ee0bbe6f3f3820904d81e6e72c5`.
- `levels/space/queue.wad`: 64453 bytes; SHA-256 `bf645dd6592aefda4abbe0c44c4c75a5d4660e0a8ae3d96e7237cc640a47721a`.
