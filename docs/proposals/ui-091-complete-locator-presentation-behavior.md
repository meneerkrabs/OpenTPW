# UI-091: Complete locator presentation behavior

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Locator cycling uses ID order rather than the original thing lists; camera follow lacks the traced per-frame smoothing; patrol marking, panel sliding and invalid staff-state frame handling remain unresolved. OpenLocator and OpenVisitorLocator already post advisor help IDs 0xC6 and 0x11F. These IDs are not evidence of missing sound events; trace the actual callee and any audio-category dispatch before proposing audio work.

## Source sites
- `source/OpenTPW/Hud/ParkHud.LocatorPanel.cs:154` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.LocatorPanel.cs:155`: `public sealed partial class ParkHud`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Thing-list ordering, frame 9 mapping and slide geometry; confirmed camera smoothing constants.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Separate deterministic selection order, panel-slide state and camera smoothing; do not mistake advisor-help posts for sound events; add patrol rectangle using existing patrol model.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Existing actor/no-actor advisor-help routing; follow at dt 1/1000 boundaries; removal while followed; staff patrol; held actor; list cycling.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-092, UI-095 and staff/patrol/camera owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Implementation-triage correction (October 18, 2026)

Source cross-check of OpenLocator/OpenVisitorLocator and AdvisorToolHelpTests corrected the initial wording about opening sounds. Their 0xC6/0x11F help posts are already present. Any distinct sound behavior remains an evidence question, not an absent feature established by those IDs.
