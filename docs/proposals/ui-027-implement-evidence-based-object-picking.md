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

## 2026-10-22 source reconciliation

This addendum supersedes conflicting historical implementation claims above. Source baseline: main `1c874922df6e42425dc5638f7e2554c9fde2c68c`. This is source inspection, not a new original-executable trace or an executed C# / browser test. The proposal remains draft; no whole-identifier retirement is claimed.

### Already implemented and corrected evidence

- [Level.Objects.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/Level.Objects.cs), `ComputeGroundHit` / `TryPickObject` / `PickOwnerAt`, includes object-model hits before returning the cursor point. `Objects.FindAt(cursorX, cursorY)` at the HUD is therefore not evidence of a terrain-only pick.
- [ObjectRayPick.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/World/Original/ObjectRayPick.cs) walks cells from the camera, tests eligible owner models until the first model hit, and accepts that point only when nearer than the terrain hit (or when terrain was missed). It is not a global nearest-face sorting algorithm.
- [ParkHud.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/Hud/ParkHud.cs) now records the traced distinction: model-ray cells select rides/shops; pick sprites select people, not class-3 ride/shop objects. The visitor locator/Shift and active-tool guards remain. [ThemeParkIncHud.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW/Client/ThemeParkIncHud.cs) uses the same cursor-cell contract. Do not implement the historical proposal's object pick-sprite path.
- [ObjectRayPickTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/1c874922df6e42425dc5638f7e2554c9fde2c68c/source/OpenTPW.Tests/ObjectRayPickTests.cs) already includes synthetic tall-object hits, terrain occlusion, off-grid clamping, absent height maps, model node/material traversal, and a ParkHud click that selects a tall ride rather than starting a path behind it. The shipped-model test is corpus-dependent and can be inconclusive without private assets; this review did not run it.

### Remaining verification and ownership

Do not duplicate the model picker. Map any remaining UI-027 acceptance cases to existing tests and add only uncovered arbitration/tool-suppression or Theme Park Inc integration witnesses. Record actual focused test execution for the reviewed revision and any visual comparisons separately.

The current picker explicitly retains RIDES-021 residuals for second-layer track-piece models, grouped instances, node-state/normal-refresh behavior and hover highlighting. These are separate ownership boundaries, not permission to silently broaden UI-027 or claim full model-pick parity. A closure decision needs that boundary reviewed, plus evidence for the original proposal's still-relevant acceptance cases; the obsolete sprite premise is not a reason to replace working code.

## Current-main supersession (2026-10-27)

This section updates the 2026-10-22 reconciliation and supersedes conflicting historical claims above. Baseline: public main `60c941de233ac45a0d8c7d680aef2269f655b8b6`. This is source inspection only, not a new original-executable trace, runtime change, test result, retirement or PR closure.

### Existing implementation and recorded status

- [Level.Objects.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Level.Objects.cs#L1552-L1592) resolves object-model hits before returning the cursor point. Consequently, `Objects.FindAt(cursorX, cursorY)` in either HUD does not mean the pick is terrain-only.
- [ObjectRayPick.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Original/ObjectRayPick.cs#L27-L78) walks cells from the camera and takes the first model hit only when it beats the terrain distance (or terrain was missed). Do not replace this with a global nearest-face sort or the proposal's historical object pick-sprite algorithm.
- [ParkHud.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/Hud/ParkHud.cs#L1254-L1268) documents the distinction between object-owner selection and people picked through sprites; [ThemeParkIncHud.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/Client/ThemeParkIncHud.cs#L431-L439) consumes the shared model-ray cursor-cell contract. This does not assert identical input timing across the two editions.
- Existing [ObjectRayPickTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW.Tests/ObjectRayPickTests.cs#L404-L459) includes a synthetic tall-ride HUD witness plus press/release and drag cases; the file also covers terrain occlusion, off-grid traversal, missing height maps, and model node/material traversal. [VisitorWorldSelectionTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW.Tests/VisitorWorldSelectionTests.cs#L89-L162) already covers numerous input-owner and locator/modifier gates. These bodies were inspected, not executed. Corpus-dependent tests are not media-free acceptance evidence.
- Current main already lists UI-027 in [tools/fidelity_retired.txt](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/tools/fidelity_retired.txt#L325) and explicitly describes its retirement in [docs/UI.md](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/docs/UI.md#L989). This addition reports that existing status; it does not perform or re-approve retirement.

### Remaining action and boundary

Review this supersession rather than implementing the obsolete object pick-sprite premise. Any optional additional integration coverage should first identify an actual uncovered case; do not duplicate the existing tall-object and input-owner tests or call their source inspection an executed result.

The picker still names separate [RIDES-021 residuals](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Original/ObjectRayPick.cs#L44-L49) for second-layer track-piece models, grouped instances, node-state/normal-refresh behavior and hover highlighting. Preserve that ownership boundary rather than silently treating it as unfinished UI-027 or asserting full model-pick fidelity.

The original proposal, earlier addendum and progress fragment remain historical records. No runtime code or register changes, C# or original-game execution, original-media publication, new test-pass, merge-readiness or closure claim is made here.
