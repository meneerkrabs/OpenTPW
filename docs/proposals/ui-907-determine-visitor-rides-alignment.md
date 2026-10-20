# UI-907: Determine visitor rides alignment

## Outcome (2026-10-20)

#495 (merged 2026-10-20, exact-head CI green) corrects the rides-column alignment and its provenance, with actual CPU row-draw fixtures described in [VISITOR-RIDES-ALIGNMENT](../VISITOR-RIDES-ALIGNMENT.md). The plan-only statements below are historical. tp.exe's rides cell constructor `0x48fde0` stores alignment 0 (left) at `0x48fe28`, and the row factory `0x4939d0` sends `0x65c428` with 1, 2 and 1 to the number, cash and time cells and none to the rides cell (`0x493b38`). UI-907 is retired; native pixel and font validation belong to other register IDs.

## Historical proposal status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `evidence_only`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
All Visitors rides column is centered, but original cell constructor does not initialize alignment byte and later value is unknown.

## Source sites
- `source/OpenTPW/Hud/ParkHud.VisitorWindow.cs:17` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.VisitorWindow.cs:18`: `public sealed partial class ParkHud`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Allocation initialization or later alignment message/capture for the actual rides cell.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Investigate actual runtime byte rather than assuming zero from another constructor; preserve current alignment pending evidence.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Fresh/reopened window; empty/one/many rows; one/multiple-digit rides; different display modes; no row ordering changes.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-914 and list/text-control owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- Superseded by the merged implementation above (#495), which changed the visitor list's rides alignment and added `VisitorRidesAlignmentTests`.
- UI-907 is retired by the post-merge follow-up on the evidence above.
