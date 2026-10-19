# UI-024: Verify local happiness census

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
LocalHappiness already counts a radius-one neighborhood. The host anchors at object placement X/Y and filters guests through IsOnParkCell rather than walking native thing-cell lists; anchor equivalence and membership equivalence remain distinct evidence questions. Previews are a separate CPU extension.

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

## Acceptance coverage and remaining cases
- Existing `RideWindowGaugeTests` covers neighbor/boundary cells, zero guests and byte conversion; preserve this coverage.
- Remaining evidence-gated cases: multi-cell object anchor and queued/riding/held guest membership; author new fixtures only for an established uncovered contract.
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

## Current-source reconciliation (2026-10-19)
At public main `fd8ff52eec12354fba02f23d23c5a217b2515a3b`, the host caller is present: `ParkHud.SelectedInfo` constructs `ObjectInfo` with `ObjectStats(item)`; the Happiness row calls `LocalHappinessGauge`, then `UiGauge.OfPercent`. The gauge uses the object's placement X/Y as its anchor, filters guests using `IsOnParkCell` (navigation-cell membership), and samples their `CellX`/`CellY` and happiness.

`RideWindowGaugeTests.TheLocalHappinessIsTheAverageHappinessByteOfTheGuestsOnTheThreeByThreeCells` already covers radius inclusion/exclusion, integer averaging, an empty neighborhood, the upper cell boundary and negative-value low-byte conversion. `UiGauge.OfPercent` scales with `(long)percent << 10` divided by 100 and clamps the result. These existing tests must not be recreated as missing averaging coverage.

No runtime defect was established by this public-source review. The unresolved native contract is equivalence of thing-cell list membership and object anchoring, especially for queued, riding, held and multi-cell cases. Public annotations are evidence leads, not a new native verification. Any additional host integration fixture should first establish an uncovered null-versus-zero or state/cell-membership contract; previews remain a separate residual. The historical baseline above is retained. No C# or GPU execution was performed for this reconciliation.
