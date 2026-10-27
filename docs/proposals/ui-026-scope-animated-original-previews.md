# UI-026: Scope animated original previews

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Original proposal baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- Source reconciliation: proposal head `b133518fbdaed8e369df785275faed978b016e45` against main `6b037fbe6bd6ec6df335c06a3d8af81ce860e43a` (2026-10-27); source inspection only, not a new runtime or original-fidelity result.
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
The original animation-absence claim is superseded by merged [PR #715](https://github.com/meneerkrabs/OpenTPW/pull/715). Main now resolves and loops animation 5, variant 0, on each new buy preview and draws the transformed, face-tested triangles through a depth-tested image. The remaining UI-026 register gaps are the preview's unlit vertices versus the original object-pipeline lighting and the untraced 1-pixel edges of footprint-cell rectangles. Object-window previews still use their 30-degree view and 0.8 rad/s turn; the CPU projection/separate image and the object-window view are documented extension choices, not missing buy-preview animation.

## Source sites
- `source/OpenTPW/Hud/PreviewIcon.cs:26,141-169,231-259,292-455` — lighting marker, animation lifecycle, buy transform and depth-image path.
- `source/OpenTPW/Hud/ParkHud.cs:1808-1823` and `source/OpenTPW/Hud/ParkHud.BuyWindow.cs:147-165,373-385` — clip loading, restart on a new preview and UI-clock-driven draw/yaw.
- `source/OpenTPW/Hud/ParkHud.BuyFootprint.cs:49-58` — integer cell dimensions and the remaining edge arithmetic.
- `docs/UI.md` — "The buy window's preview", UI-026 register row and preview extension entries; all source references above are at the reconciliation main.

## Implementation cross-check
- Existing behavior described above was checked against implementation, not only the register summary. Do not duplicate the animation/depth work from PR #715.
- Existing test sources include `BuyPreviewRuleTests` (tilt/turn, fit, matrix/view/viewport and clip-member selection), `PreviewIconResolutionTests` (materials, face/depth behavior), `PreviewDepthOrderTests` (data-dependent preview corpus and animation) and `BuyFootprintTests` (cell classes and clipping). Their presence is not a fresh passing result; none was run in this reconciliation.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original preview lighting for an instance with its own view and footprint edge rasterization; validate the existing animation/transform evidence rather than treating those features as absent.
- The original proposal reported private Data containing assets, not an executable. This source-only reconciliation accessed neither private Data nor original executables and obtained no new binary trace.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Reuse the merged animation, transform and depth-image paths. Investigate only the remaining lighting and footprint-edge distinctions; preserve sign textures and the explicit CPU-projection/object-view and ground-plate extensions.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to retain, extend where needed and run later
- Preview fallback model; animation 5 absent/present; selection changes preserve yaw and restart the clip; 45-degree buy tilt; signs; footprint boundaries and pixel edges; preview lighting.
- Use the existing test suites listed above as regression coverage, add evidence-backed cases for the residual gaps, and report data-dependent skips explicitly. Historical PR #715 measurements are not a rerun or proof that the remaining gates pass.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-024, UI-003 and object-animation/render owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating or reconciling this plan; PR #477 is not claimed implemented or ready to merge. Keep draft until the remaining evidence, implementation where needed, tests and review gates are satisfied.
