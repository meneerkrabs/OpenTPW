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

## Full-register coverage addendum (October 18, 2026)

This reviewed addendum extends the bounded initial plan to cover the remaining registered residuals. All acceptance work remains pending. Source snapshot independently verified by the proposal coordinator: `5ccddcab56ffb9bda4ded1821988a8492bee8943`.


## Status and classification
- Classification: missing.
- Plan-only addendum: no runtime implementation, annotation retirement or merge performed.
- Existing PR: #443; residual addendum only.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
End is not created: the original creates it through 0x516b00 from the lab's message handler 0x503d40 (message 0x11) when the last research item completes, by the GameType word DAT_00fb3b7c (2: Instant Action at once; 0: Full Simulation once every online park record is done, 0x5b0c40; 1: never), after posting the advisor message 0xa7

## Source inspection
- `source/OpenTPW/World/Objects/ParkObjects.cs:624`: 625: public int AddDefaultFixedItems() => AddDefaultFixedItemSteps().LastOrDefault().Placed;; 628: internal IEnumerable<(int Placed, float Share)> AddDefaultFixedItemSteps(); 629: {; 631: var entries = Catalog.Entries.Where( entry => entry.IsFixedItem && (entry.SettingsName is "Gates" or "Lights" || GameEditions.IsThemeParkInc && entry.SettingsName == "Bus") ).ToList();

## Bounded work
Addendum to #443: Full Simulation End creation needs all-online-park research ledger; exclude covered Instant Action.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Decode 0x5B0C40 per-park flags, current park identity and persistence; depend on player-ledger work.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Type 0 with last local item but another incomplete park creates no End.
2. Type 0 with all records complete creates End once after advisor 0xa7; game type 1 never does.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- World/audio implementation owner to be assigned; no runtime ownership claimed.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Review existing PR #443 current head; use residual addendum, not duplicate proposal.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.
