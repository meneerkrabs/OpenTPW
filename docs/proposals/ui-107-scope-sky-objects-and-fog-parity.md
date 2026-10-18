# UI-107: Scope sky objects and fog parity

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Cloud layers/cylinder exist; moon billboards and sky flag variants are absent, while fog state/range and primitive gate remain uncertain. RenderFog comment still cites retired UI-101 camera mismatch, so that rationale needs revalidation.

## Source sites
- `source/OpenTPW/Client/GameFlow.cs:978` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Render/RenderFog.cs:14` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Client/GameFlow.cs:979`: `backgroundBatch.Clear();`
- `source/OpenTPW/Render/RenderFog.cs:18`: `internal static class RenderFog`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Sky-object transform/pass trace, texture page state and actual fog-enable/gate values for a controlled park capture.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Implement only verified sky-object passes separately from fog research; reconcile stale camera rationale and never use evening extension #439 as original evidence.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Hallow rear moon; space front moons; SKYDETAIL off; flag 8/0x2000000; alpha threshold; fog near/far at actual camera pose and presets.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-018, sky renderer and shader owners; #439 is extension-only.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
