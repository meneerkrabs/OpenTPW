# UI-025: Restore message admission and actions

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
MessageTubeCapacity is five with oldest-drop policy; original has eight slots, per-type caps and priority replacement; most action kinds lack events.

## Source sites
- `source/OpenTPW/Hud/ParkHud.MessageTube.cs:16` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.cs:263` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.MessageTube.cs:17`: `public const int MessageTubeCapacity = 5;`
- `source/OpenTPW/Hud/ParkHud.cs:264`: `hud.Add( new UiButton { Id = "messageDelete", Model = "mb_erase", Clicked = CloseMessagePanel, Bounds = OriginalLayoutTables.MessagePanel.Model( "mb_erase" ).Rect, Anchor = UiAnchor.BottomLeft, Visible = false } );`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Priority/cap registry mapping and message-kind event handlers beyond challenge accept/decline.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Design pure admission policy and typed action routing, preserving explicit art/status-line extensions and original no-pending-message-save behavior.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Eight slots; ninth higher/equal/lower priority; per-type caps; deletion; challenge actions; missing target actions fail safely; no pending-message serialization.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Advisor producers, challenges and online/message owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
