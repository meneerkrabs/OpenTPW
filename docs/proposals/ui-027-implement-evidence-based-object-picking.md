# UI-027: Implement evidence-based object picking

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
ParkHud and ThemeParkIncHud use Objects.FindAt on terrain-hit cell instead of original pick-sprite selection.

## Source sites
- `source/OpenTPW/Client/ThemeParkIncHud.cs:434` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.cs:855` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Client/ThemeParkIncHud.cs:435`: `if ( inPark && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsRemovingObjects && input.LeftReleased && !toolPress )`
- `source/OpenTPW/Hud/ParkHud.cs:856`: `if ( inPark && !IsVisitorPickState( Locator.Mode, Input.ModifierMaskDown ) && level.BuildEntry == null && !level.IsPlacing && !level.CellTool.IsActive && !level.IsCoasterToolActive && level.StaffPlacement is not { IsActive: true } && level.PatrolTool is not { IsActive: true }`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Object pick sprite anchor/scale/flags and draw order, plus staff/visitor arbitration.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Specify shared pick record generation and original 3x3 owner resolution; replace cell-only selection after geometric oracle exists.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Overlapping tall objects; nearest w and first-drawn tie; neighboring cells; Shift/locator state; tool suppression; both HUD variants.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-930, guest picker and render pass owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
