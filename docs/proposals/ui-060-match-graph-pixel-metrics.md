# UI-060: Match graph pixel metrics

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Finance and information graphs scale pixels as two canvas units; grid-line color/placement remains host-specific despite implemented series/masks/ranges.

## Source sites
- `source/OpenTPW/Hud/ParkHud.FinanceWindow.cs:192` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.InfoGraph.cs:114` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.FinanceWindow.cs:193`: `var pixel = Math.Max( 1, context.Canvas.Scale * 2 );`
- `source/OpenTPW/Hud/ParkHud.InfoGraph.cs:115`: `var pixel = Math.Max( 1f, context.Canvas.Scale * 2f );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Display-mode-to-canvas pixel mapping and original grid draw 0x4806a0 outputs.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Extract shared graph raster metrics with verified coordinate conversion; preserve established data and series logic.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- 1/3/12-year samples; flat/negative/mixed ranges; hollow endpoint marks; three interior lines; multiple resolutions.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-914 and graph/text rendering owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
