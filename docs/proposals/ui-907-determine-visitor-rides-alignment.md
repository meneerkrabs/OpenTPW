# UI-907: Determine visitor rides alignment

## Current bounded implementation (2026-10-20)

This PR now corrects only the rides-column alignment and its provenance, with actual CPU row-draw fixtures described in [VISITOR-RIDES-ALIGNMENT](../VISITOR-RIDES-ALIGNMENT.md). The plan-only statements below are historical. Exact-head CI is pending; no executed C# or native rendered parity claim is made. UI-907 remains registered and full native pixel/font validation is not claimed.

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
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
