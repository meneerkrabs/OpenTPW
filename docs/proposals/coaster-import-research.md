# Draft proposal: SAVE-920–923 original coaster import evidence and restore plan

## Status and boundary

Plan only, October 18, 2026; source reconciliation October 22, 2026. No gameplay implementation or runtime tests are delivered by this draft. Do not merge as an implemented feature; keep this PR draft until the implementation/evidence gates below are met and independently reviewed. No approximation ID is retired and no platform/original-fidelity qualification is claimed.

Original proposal baseline: `4c87d5f4b657278578c77730524bc69f9002270e`. Current source checked at `1c874922df6e42425dc5638f7e2554c9fde2c68c`. Re-check current main and active work before implementation. Register IDs: SAVE-920 / SAVE-921 / SAVE-922 / SAVE-923.

## Observed evidence and current gap

The original proposal's blanket statement that coaster blocks only decode and every park listing a coaster is refused is superseded by current source. `source/OpenTPW/World/Coasters/OriginalCoasterImport.cs` maps station identity, nodes and loop pairs, cut/controller state, power and seats, and in-service trains with saved positions and speeds. Invalid or unsupported shapes still have explicit validation; this is not unconditional import support.

`source/OpenTPW.Tests/OriginalCoasterImportTests.cs` contains synthetic running-circuit, cut-track, deterministic continuation and own-format resume cases. These are existing test definitions inspected here, not tests executed by this reconciliation and not witnessed original-save acceptance.

The current residuals in `source/OpenTPW/Economy/SaveApproximations.cs` remain distinct:
- SAVE-920: train rider lists are reported but not restored because riding/seated guests (states 15/16, SAVE-002) are not imported. `OriginalCoasterImport.Map` explicitly emits this evidence when the inspected in-service train rider lists on a runnable track are nonempty.
- SAVE-921: meanings/readers of controller track flags 0x20, 0x80, 0x100, 0x200 and 0x400 remain untraced; decoded complete/cut flags are not missing work.
- SAVE-922: placement-derived controller words +0x1C/+0x20 remain unresolved; already-mapped state, station bind and slot must not be reimplemented from the old broad plan.
- SAVE-923: train +0x38/+0x64/+0x68, car +0/+0x44 and an original coaster chunk writer remain unresolved.

No new original executable, original save, runtime or browser/GPU execution was performed.

The supplied Data includes coaster definitions/models and Jungle `Easymode.TPWI`, but no witnessed save with a built coaster. Other private corpus references in repository docs do not establish that such a fixture is available here. Several controller flags/words remain partly understood.

## Proposed bounded scope

Research-first draft for the remaining original-save coaster restore gaps. Preserve current supported restoration and its validation; do not reinstate the superseded blanket coaster refusal. The current importer reports omitted riders, so any future rider-support change must validate the complete supported ownership mapping before mutation, rather than silently dropping guests or treating geometry restoration as full fidelity.

## Work sequence

1. Obtain a private legitimate original save containing a built coaster, with edition/theme identity, plus preferably open/closed, occupied and edited cases. Do not distribute original saves or run original software in this task.
2. Verify the existing station/node/controller/train mapping against that fixture, then map the missing guest references with the SAVE-002 owner; keep unknown words explicitly unknown.
3. Extend transactional validation/restore only for demonstrated residuals. Keep existing geometry/train support separate from missing riders; define the supported occupied-train contract and explicit unsupported outcome before claiming rider restoration.

## Acceptance and test plan (not yet run)

- Real private coaster fixtures decode with a pinned manifest and restore a connected, correctly posed runtime track/train; synthetic malformed/bounds/duplicate-reference/cyclic-link fixtures fail safely with explicit memory, count and traversal limits.
- Open/cut/incomplete/closed tracks, train speeds/positions, riders and station membership have explicit supported/unsupported outcomes.
- Import then own-format save/resume preserves supported behavior, deterministic state and ownership; no silent loss of guests or coaster data.
- Keep each of SAVE-920–923 open until its own evidence and implementation acceptance is met; a research plan or decoder-only pass closes none.
- Before publication of runtime changes: focused tests, relevant aggregate suite, exact-head required CI and independent review; private-data skips must be reported as skips.

## Dependencies and ownership

Remaining fidelity work depends on witnessed built-coaster saves, SAVE-002 riding/seated guests and unresolved controller/train semantics. Coordinate current save/track owners; historical #437/#438 are not evidence that current restoration is absent. Do not duplicate the already-landed import mapping.

Proposal coordinator: Codex. Runtime implementation owner is unassigned until the existing topic owners have acknowledged the boundary. Required next decision is technical ownership/evidence, not a claim that implementation is ready. The source/data audit is read-only; original assets remain outside git.

## Private-input identity (October 18 audit)

Relative paths below are inside the supplied local Data tree. SHA-256 pins audit inputs only; it does not authenticate their community-archive provenance or establish an original edition. No asset bytes are committed. WAD evidence above is a read-only directory/member inventory, not a gameplay test.

- `levels/jungle/Easymode.TPWI`: 38479 bytes; SHA-256 `6d89303d098900364bf5e80b236b64bd85976fb947e9e4609d088547f430b39a`.
