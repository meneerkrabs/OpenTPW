# UI-024: Verify local happiness census

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
LocalHappiness already counts a radius-one neighborhood, but its input filters IsOnParkCell and uses placement anchor instead of traced thing-cell lists; previews are an explicit CPU extension.

## Source sites
- `source/OpenTPW/Hud/ParkHud.ObjectGauges.cs:73` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.ObjectWindow.cs:17` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.ObjectGauges.cs:74`: `return UiGauge.OfPercent( LocalHappiness( around, item.Placement.X, item.Placement.Y ) );`
- `source/OpenTPW/Hud/ParkHud.ObjectWindow.cs:19`: `internal enum ObjectRowKind { None, Users, Age, Excitement, Reliability, Repair, Life, Scrap, Customers, CostOfGoods, Takings, Profit, Satisfaction, Happiness, Winners }`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Thing cell coordinates and park membership for queued, riding and held guests near multi-cell objects.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Compare census predicates and object anchor semantics; do not misdescribe the existing radius loop as an absent neighborhood calculation.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Neighbor and boundary cells; zero guests; byte truncation; multi-cell object anchor; queued/riding/held guests.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-026, UI-914 and guest spatial-state owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
