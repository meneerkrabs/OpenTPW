# UI-107: Scope sky objects and fog parity

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Original baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- Source reconciliation (2026-10-30): proposal head `123899acf01b7ebed3b64651c59e74071292a51f` against main `0910d275638548f12ebfa41915bb71e64f091119`. Source and test inspection only; no tests or original captures run.
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Cloud layers/cylinder and moon billboards now exist. `Sky.ReadObjects` reads level/shared settings; `TryMoonQuad` supplies roll/aspect-aware geometry; rear objects draw before clouds and front objects after the cylinder, gated by SKYDETAIL. `GameFlow` enables the implemented linear fog range only for ride/free-walk cameras; `RenderFog` no longer cites the retired UI-101 camera mismatch.

UI-107 remains open: flag 8/0x2000000 sky variants, alpha threshold, texture-page scale/offset and state bit 0x1000, vertex colour mask and conditional clear, device fog depth (w versus z), and ride/walk far-plane behavior remain unimplemented or unqualified. The front-end fog mapping, old-device workaround and particle fog trace also remain outside the established park behavior. See the current UI-107 row in `docs/UI.md`; source completion is not rendered/original fidelity.

## Source sites
- `source/OpenTPW/World/Sky.cs:129-315,626-631` — settings, moon geometry, SKYDETAIL gate and rear/front submission.
- `source/OpenTPW/Client/GameFlow.cs:1133-1149` — first-person fog selection, conditional-clear residual and sky draw.
- `source/OpenTPW/Render/RenderFog.cs:23-98` — preset ranges, enable state and packed shader range.
- `docs/UI.md:1005` — current UI-107 implemented scope and residual evidence.

## Implementation cross-check
- `Sky.Draw` submits rear moons, clouds, cylinder, then front moons; `DrawMoons` checks `RenderQuality.SkyObjects`.
- `GameFlow` calls `SetParkRange` for `RideCameraMode`/`FreeWalkCameraMode`, otherwise `Disable`; the clear remains the fog colour.
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Evidence for the remaining render flags, texture-page/state values, device fog depth and ride/walk far plane, plus controlled rendered comparisons of sky pass ordering and fog at matched camera poses/presets.
- Existing source annotations and committed moon goldens document earlier trace work; this reconciliation obtained no new binary trace, original assets or capture.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Do not reimplement the existing moons or fog range/gate. Investigate only the residual sky state and fog/device questions separately, and never use evening extension #439 as original evidence.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance coverage and remaining validation
- Inspected, not run: `SkyTests` covers synthetic hallow rear/space front settings and shared defaults, SKYDETAIL 0/1, moon roll goldens, aspect/depth geometry and far-plane containment. This does not exercise GPU pass ordering or original texture pages.
- Inspected, not run: `RenderFogTests` covers level colours/uniform layout, all four FIRSTPERSONVIEWDISTANCE ranges, enable/disable state, packed-range decoding/scaling, and shader-source assertions. These do not establish device depth equivalence or rendered camera-mode transitions.
- Still required for changed behavior: Hallow rear/space front rendered ordering; SKYDETAIL off; flag 8/0x2000000; alpha threshold and texture-page state; fog near/far at matched camera poses and presets, including overview-to-first-person transitions and ride/walk far-plane behavior.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-018, sky renderer and shader owners; #439 is extension-only.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
