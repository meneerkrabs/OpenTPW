# HEADS-004: Verify head reference-basis roll

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
The former world-matrix-only roll description is stale. Current HeadPresentation expresses the camera in the node frame, selects the art frame, builds its reference basis and removes that basis before extracting roll. Native sine-table/x87 precision and rendered reference equivalence remain unresolved.

## Source sites
- `source/OpenTPW/World/Guests/HeadPresentation.cs:22` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Guests/HeadPresentation.cs:23`: `public static class HeadPresentation`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Reference-basis matrix construction and original roll samples for selected row/column frames.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Create a pure matrix oracle from verified instructions and compare current Compute; keep costume-head selection as a separate guest-costume dependency.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- All 7x8 frame directions; pole columns; mirrored/hidden flags; nonuniform transforms and camera rotation; finite fallback.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Guest costume owner and head-renderer owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Current-source reconciliation (2026-10-22)

Inspected public main `1c874922df6e42425dc5638f7e2554c9fde2c68c`, not merely the historical proposal:

- [HeadPresentation.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/Guests/HeadPresentation.cs#L74-L121): `Compute` forms `cameraWorld * inverse`, selects the nearest frame, constructs direction/up/right rows, then computes `cameraInNode * inverseBasis` before `Atan2(-remaining.M21, remaining.M22)`. The selected-frame basis is therefore already implemented and must not be reimplemented as a missing feature.
- [HeadPresentationTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW.Tests/HeadPresentationTests.cs) already covers direction selection, pole columns, opposite quarter-turn bank rolls, uniform scale, mirroring, hidden nodes and singular-matrix fallback. Reading these tests is not a claim that they were executed in this audit.
- The current `HEADS-004` annotation explicitly retains native sine-table/x87 and general-inversion precision differences plus rendered reference comparison. The existing MathF trigonometry and general matrix inverse do not establish bit-exact parity.

Next action: reconcile the evidence ledger with this implemented composition, then obtain an independently evidenced native numeric oracle for nonuniform transforms, camera rotations and frame-boundary/tie cases. Extend regression coverage only for identified gaps. A synthetic 7x8 consistency check alone cannot retire the native precision or rendered-reference residual.

This reconciliation changes no runtime code, executes no original binary or C# test, publishes no original media and makes no whole-ID completion or merge-readiness claim.
