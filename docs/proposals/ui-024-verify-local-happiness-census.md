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

## Current-source reconciliation (2026-10-22)

This addendum supersedes the 2026-10-19 census description where it conflicts. Source inspection is pinned to public main `1c874922df6e42425dc5638f7e2554c9fde2c68c`; it is not a new original-game trace or a runtime test.

### Census work already present

- `source/OpenTPW/Hud/ParkHud.ObjectGauges.cs:72–79` returns null for a read-only visit or missing guest simulation. Otherwise it calls `LocalHappiness(guests.CellListHappiness(), item.Placement.X, item.Placement.Y)`, then `UiGauge.OfPercent`. It no longer filters through `IsOnParkCell`.
- `source/OpenTPW/World/Guests/GuestSimulation.cs:1381–1387` enumerates the existing `occupants` lists, newest member first, and yields the list's cell coordinates, not each guest's potentially unsynchronized navigation position. The reader performs no synchronization and imposes no additional state predicate.
- `LocalHappiness` still performs the radius-one census, low-byte conversion, integer division and empty-neighborhood zero. The existing `RideWindowGaugeTests.TheLocalHappinessIsTheAverageHappinessByteOfTheGuestsOnTheThreeByThreeCells` covers these arithmetic/boundary cases. `GuestCellChainLifecycleTests` additionally contains synthetic movement, same-cell stability and save/restore ordering checks. These tests are source-inspected here, not newly executed.
- The host comment records a zero-MapOffset anchor match for inspected shipped objects. This is an existing recorded scope claim, not a new corpus inspection or proof for arbitrary custom objects.

### Remaining boundary and next action

Do not implement another radius loop or reintroduce a park-state filter. The next evidence work is to compare the actual membership writers and removal timing for queued, riding, held and swimming guests against native cell lists; the host explicitly retains its swim exclusion and synchronization policy. A regression fixture must exercise a demonstrated lifecycle distinction rather than invent membership by assigning a state alone.

For anchors, retain the existing placement anchor until a nonzero MapOffset or multi-cell example establishes a different native thing-cell position. Record its input geometry, native thing position, census cell and expected included guests separately. The object-window orthographic preview remains its own UI-024 residual in `ParkHud.ObjectWindow.cs:18` and is not fixed by census work.

The census-reader implementation is already present, but full UI-024 acceptance, original membership equivalence, custom-anchor equivalence and preview parity are not established by this audit. Keep this proposal draft. No runtime change, approximation retirement, C# execution, GPU execution or new original-media inspection is claimed.

