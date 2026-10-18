# Draft proposal: RIDES-019 Instant Action research finale

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: RIDES-019.

## Observed evidence and current gap

`source/OpenTPW/World/Objects/ParkObjects.cs`, `AddDefaultFixedItemSteps`, creates Gates/Lights rather than End. `ParkEconomy` emits `ResearchAllDone`; `AutomaticAdvisor` already handles advice 167 for Instant Action. The recorded lab completion path creates End after that advice.

All four supplied `features/end.wad` archives contain `End.RSE`, `End.MD2`, `Endm1..3.MD2`, `AnimCtrl.RSE`, settings, height data and textures. Existing source records the script's DIPMUSIC, ADDOBJ and EVENT 17–20 use. This inventory is not a completed dependency/runtime test.

## Proposed bounded scope

Implement only the Instant Action completion event initially. Spawn the theme's original End object exactly once at the evidenced transition, using existing script/object facilities. Full Simulation's player-wide park research ledger and online visits are excluded.

## Work sequence

1. Trace the End script's referenced objects, event handlers, sounds and effect dependencies against current runtime support, without executing original programs.
2. Define a persisted completion/spawn lifecycle that cannot repeat on load or duplicate notifications; do not presume a new flag is necessary before inspecting existing state.
3. Integrate the completion producer and verify ordering relative to advice, music and thing allocation.

## Acceptance and test plan (not yet run)

- In each theme, completing the final research item creates the correct End once; partial research, repeated events and online/read-only visits do not.
- Save/resume and restart during/before/after the finale preserve the intended lifecycle without duplicated scripts or music changes.
- Every invoked script/effect hook has a test or explicit blocker; all four themes have visual/audio checks using private local assets.
- Review determinism/thing-slot changes against expected behavior. RIDES-019 remains open for Full Simulation until its ledger semantics are evidenced.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Coordinate End event/effect dependencies with the particle owner before implementation; no parallel rewrite of those hooks. Medium effort, contingent on dependency audit. Full Simulation is blocked on player-wide per-park research records.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.

## Private-input identity (October 18 audit)

Relative paths below are inside the supplied local Data tree. SHA-256 pins audit inputs only; it does not authenticate their community-archive provenance or establish an original edition. No asset bytes are committed. WAD evidence above is a read-only directory/member inventory, not a gameplay test.

- `levels/jungle/features/end.wad`: 58072 bytes; SHA-256 `a1898729b27235fd1d4387366311be744e54584c9aa65db964eab7bfbba63bfe`.
- `levels/hallow/features/end.wad`: 58013 bytes; SHA-256 `3d662ee2fe0a5ad35229b068926565aefbb43b5d8ff8da7ea765c1eb658a703f`.
- `levels/fantasy/features/end.wad`: 58011 bytes; SHA-256 `2efaf470cd6c5f6769a64b779caac607ef1a4b5fbb472cde24acf56611ec5ee8`.
- `levels/space/features/end.wad`: 58011 bytes; SHA-256 `4aa636170895a090e24d27f92dec3284e1a563eb74bd415c06ac122e310f5fc2`.
