# Draft proposal: SAVE-920–923 original coaster import evidence and restore plan

## Status and boundary

Plan only, October 18, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Source baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Re-check current main and active work before implementation. Register IDs: SAVE-920 / SAVE-921 / SAVE-922 / SAVE-923.

## Observed evidence and current gap

`source/OpenTPW/Economy/SaveApproximations.cs` and `docs/TPWS-PAYLOAD.md` record that coaster blocks decode but no runtime coaster is restored; a park listing a coaster is refused. `OriginalCoasterSaveTests` covers decoder work, not a completed playable restore.

The supplied Data includes coaster definitions/models and Jungle `Easymode.TPWI`, but no witnessed save with a built coaster. Other private corpus references in repository docs do not establish that such a fixture is available here. Several controller flags/words remain partly understood.

## Proposed bounded scope

Research-first draft for a safe original-save coaster restore. Preserve current refusal until a validated all-or-nothing restoration exists. Do not silently discard coaster state or claim successful import from synthetic decoder tests alone.

## Work sequence

1. Obtain a private legitimate original save containing a built coaster, with edition/theme identity, plus preferably open/closed, occupied and edited cases. Do not distribute original saves or run original software in this task.
2. Map station identity, node/link flags, controller state, train/car lists and guest references to existing runtime types; keep unknown words explicitly unknown.
3. Design transactional validation/restore so malformed or unsupported state fails before mutating the park. Separate empty/unoccupied track geometry support from occupied train restoration only if the unsupported subset is explicitly rejected.

## Acceptance and test plan (not yet run)

- Real private coaster fixtures decode with a pinned manifest and restore a connected, correctly posed runtime track/train; synthetic malformed/bounds/duplicate-reference/cyclic-link fixtures fail safely with explicit memory, count and traversal limits.
- Open/cut/incomplete/closed tracks, train speeds/positions, riders and station membership have explicit supported/unsupported outcomes.
- Import then own-format save/resume preserves supported behavior, deterministic state and ownership; no silent loss of guests or coaster data.
- Keep each of SAVE-920–923 open until its own evidence and implementation acceptance is met; a research plan or decoder-only pass closes none.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Blocked on witnessed built-coaster saves and unresolved controller semantics. Coordinate save/track owners and #437 snapshot work; #438 already corrects save status documentation and should not be duplicated. Research medium, restoration large.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.

## Private-input identity (October 18 audit)

Relative paths below are inside the supplied local Data tree. SHA-256 pins audit inputs only; it does not authenticate their community-archive provenance or establish an original edition. No asset bytes are committed. WAD evidence above is a read-only directory/member inventory, not a gameplay test.

- `levels/jungle/Easymode.TPWI`: 38479 bytes; SHA-256 `6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`.
