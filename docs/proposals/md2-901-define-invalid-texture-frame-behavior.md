# MD2-901: Define invalid texture frame behavior

## Status and classification
- Evidence-only draft reconciliation; no implementation, tests-run claim, retirement or merge.
- Classification: `evidence_only`.
- Original baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- Source refresh: main `6b037fbe6bd6ec6df335c06a3d8af81ce860e43a` (2026-10-27).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Invalid slot/frame tracks are ignored defensively. The PC writer is unchecked; the binder `0x57A420` and draw reader `0x580530` are now traced through the unchecked frame-pointer accessor `0x575F90`. MD2-901 remains a deliberate defensive divergence, not a missing consumer trace; no portable fallback, clamp or wrap is established.

## Source sites
- `source/OpenTPW/World/Objects/ObjectAnimator.cs:317` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Objects/ObjectAnimator.cs:318`: `public int GetTextureFrame( int slot )`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence status and residuals
- The [existing consumer trace](https://github.com/meneerkrabs/OpenTPW/blob/6b037fbe6bd6ec6df335c06a3d8af81ce860e43a/docs/reverse/TEXTURE-FRAME-LIFETIME.md#invalid-frame-consumer-md2-901), [October 22 progress entry](https://github.com/meneerkrabs/OpenTPW/pull/747) (`docs/progress/2026-10-22-md2-901-consumer-trace.md`) and `docs/OBJECTS.md` MD2-901 row already record the evidence closure.
- The writer's slot access is unchecked. Loader validation and exact slot-array allocation remain untraced; an absent slot is not a consumer-clamping question because consumers follow the part's own slot pointers.
- This refresh reads public repository source and documentation only; it obtains no new binary trace, original assets, runtime comparison or test result.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Reconcile this proposal with the existing trace and retain safe malformed-input handling; do not duplicate the consumer investigation or port out-of-bounds reads.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Existing coverage and future acceptance scope
- `ObjectTextureFrameTests.InvalidSlotsAndFramesAreRefused` already covers an absent slot and oversized frame. Held-history, missing-art and malformed-snapshot tests cover adjacent host policies; these tests were inspected, not run in this refresh.
- Before any runtime change, check whether exact slot 12 and valid/invalid neighboring-track coverage add distinct value rather than duplicate existing tests. Wire slot/frame fields are unsigned 16-bit (`ModelAnimation.cs:586-588`); negative snapshot frames are a separate malformed-state case.
- The 458 in-range tracks are a documented corpus baseline, not a new result. Private corpus acceptance is outside this source-only refresh.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: MD2-900 and parser/security owner; unsafe original behavior excluded.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- No runtime mismatch is established by this refresh. Any further runtime work needs a distinct evidence-backed scope and ownership decision, not reproduction of unsafe native accesses.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
