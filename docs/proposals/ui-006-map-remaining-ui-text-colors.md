# UI-006: Map remaining UI text colors

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Several traced colors are exact, but other UiColors and screen assignments still use capture-derived values.

## Source sites
- `source/OpenTPW/UI/Original/UiText.cs:273` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/UI/Original/UiText.cs:274`: `public static class UiColors`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Remaining color setter call sites mapped to controls/screens, especially register/table-sourced colors.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Create screen/control/color provenance matrix and correct only unmatched assignments; preserve already traced title and menu colors.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Title, pause, disabled, negative and focused states; each changed control has address/capture evidence; translated labels keep layout.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-007, UI-037 and text rendering owner; merged language #423 is baseline.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Public-source audit (2026-10-22)

This section advances the proposal's provenance inventory, not its runtime
acceptance. Source snapshot: main `dcdf4d0c53723e7b7df9c7c7a5ae3834c4f7130b`.
The values below are RGBA bytes. "Traced in source" means that the public source
already records an original address; this audit did not independently inspect an
executable. A traced constant does not prove every screen that reuses it is exact.

| Constant / state | Current value | Evidence class and actual source use |
| --- | --- | --- |
| `Text` | `(255,255,255,255)` | Traced in source for buy-list rows (`UiText.cs`, `0x4ad340`). Also the default `UiLabel.Color` and ordinary list text; those broader assignments are not proven by that buy-list annotation. |
| `Title` | `(234,239,102,255)` | Traced shared title setter (`UiText.cs`, `0x492d20`). Examples include `FrontEndMenu`'s island name and `UiDialogs` titles. Preserve the constant; verify new assignments separately. |
| `MenuItem` | `(0,175,190,255)` | Traced initialization and restore in `UiMenuItem.cs` / `MenuLineRamp`. This is the idle pause-line colour, not the focused output. |
| Focused pause line | grey `33,65,97,129,161,193,225,255`, alpha `255` | `MenuLineRamp.Step` advances to white over eight ticks. `Enter` changes the slot, not the immediate colour; `Leave` restores cyan on the next tick. Public source records `0x48b220`. The OpenTPW focus adapter and reference-frame timing remain explicitly marked extensions. |
| `MenuItemFocused` | `(255,255,255,255)` | Declared in `UiText.cs`, but the actual pause-line draw reads `ramp.Color`, not this constant. Do not describe the focus behavior as an immediate constant swap. |
| `Highlight` | `(255,230,70,255)` | Capture-derived under UI-006. Selected list rows and generic focused button labels use it; pause lines do not. |
| `Disabled` | `(140,140,160,255)` | Capture-derived. Disabled menu lines, fallback button labels, option-row values and text-entry prompt/disabled states use it. This is not a universal button rule: the successful model-draw branch selects `Highlight`/`Text` without the fallback branch's `Enabled` colour test (`UiWidgets.cs`). No original mismatch is established by that difference. |
| `Value` | `(150,255,120,255)` | Capture-derived. Option-row enabled values, text-entry active values, list column headers and employed-staff mini values use it. A role name is not evidence that every numeric field uses it. |
| `Negative` | `(255,40,40,255)` | Capture-derived hire-window cost colour; `ParkHud.Staff.cs` applies it to non-employed mini rows 1 and 2, rather than testing the displayed number's sign. |
| `Shadow` | `(0,0,40,200)` | Capture-derived colour, emitted by `UiContext` text helpers when shadowing is requested. Shadow policy remains separately owned by UI-007 / #467. |
| `Backdrop` | `(0,0,30,120)` | Capture-derived; `UiScreenStack.Draw`'s screen backdrop rectangle. Not a text control's diffuse colour. |
| `HelpBackground` | `(20,30,90,230)` | Capture-derived; fallback panel/list/button/option backgrounds. Distinct from the popup-help box. |
| `HelpBox` | `(0,0,0,170)` | Capture-derived fallback popup-help rectangle in `UiScreenStack.DrawHelp`, used only when `DrawModel("f_helpbg", ...)` fails. The source describes about 65–70% black over grass, not an independently traced alpha of 170. |

`OptionText` remains exclusively UI-037 / #476. This audit does not change its
value, interpretation, evidence or publication gate. Likewise, successful model
rendering and missing-art fallbacks are distinct paths; UI-008 / #469 owns the
button-art residual, not this palette audit.

### Existing coverage and the next evidence gate

- `PauseMenuLayoutTests.LinesAreTheOriginalCyan` already pins the cyan bytes.
  `MenuLineRampTests.EnteringRampsFromGreyToWhiteInEightSteps` and
  `LeavingRestoresTheCyanOnTheNextTick` already cover the transition, delayed
  updates and eventual idle state. Another constant-only test would duplicate
  coverage rather than resolve an unknown colour assignment.
- `HudVideoRoundTests.TheHelpBoxSitsAtTheBottomCentreOnTranslucentBlack` checks
  black RGB, a broad translucent-alpha range and actual synthetic batch geometry.
  It does not establish original alpha 170 or composited-pixel fidelity. #748
  currently changes that test file for a separate message-tube task; leave it alone.
- The matrix was checked against the public source and the existing test bodies.
  Those C# tests were inspected, not executed in this audit. No browser, GPU or
  original-game validation was performed, and no runtime values were changed.
- A colour change now needs a named control and state, evidence for its colour
  assignment (not merely the shared setter), and the relevant font/texture/blend
  path. Compare enabled/disabled, selected/unselected and model/fallback paths
  separately. Capture-derived colours stay approximations until that evidence
  exists; successful synthetic tests alone cannot retire UI-006.

The bounded deliverable is this source/control/evidence matrix. The earlier plan's
future screen mapping is no longer wholly unstarted, but the remaining original
call-site mapping and rendered comparison are still open. Keep #468 draft and do
not claim implementation completion or register retirement.
