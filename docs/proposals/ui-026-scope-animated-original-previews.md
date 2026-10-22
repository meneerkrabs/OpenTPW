# UI-026: Scope animated original previews

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
PreviewIcon CPU triangles are present; original 3D pipeline animation 5 is absent, object-window tilt/speed differ, grid edge parity is untraced.

## Source sites
- `source/OpenTPW/Hud/PreviewIcon.cs:15` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/PreviewIcon.cs:16`: `public sealed class PreviewIcon`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original preview pipeline, animation lifecycle, transform and footprint edge rasterization.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Separate animation 5 support from replacement of explicit CPU projection extension; prototype transform comparison and keep sign textures intact.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Preview fallback model; animation 5 absent/present; selection changes preserve yaw; 45-degree buy tilt; signs; footprint boundaries.
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
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
