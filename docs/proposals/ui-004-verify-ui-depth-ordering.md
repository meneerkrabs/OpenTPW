# UI-004: Verify UI depth ordering

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
UiModel groups triangles by material and sorts by depth; device depth initialization alone does not establish per-UI draw state.

## Source sites
- `source/OpenTPW/UI/Original/UiModel.cs:138` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/UI/Original/UiModel.cs:141`: `var groups = new Dictionary<string, List<(float Depth, int A, int B, int C)>>( StringComparer.OrdinalIgnoreCase );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Per-UI pass depth enable/write/compare and submission order trace.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Compare crossing textured parts under sort and depth-test approaches; propose isolated UI depth pass only with state evidence.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Interpenetrating parts; same-depth ties; opaque and transparent surfaces; root/child states; world/HUD depth isolation.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-003, UI-034 and render pipeline owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
