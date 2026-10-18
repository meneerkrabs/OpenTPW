# UI-914: Scope information history residuals

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Original-save history rings are not imported; stay history derives simulated seconds; count alignment/icon re-show and census semantics remain unverified despite complete main window.

## Source sites
- `source/OpenTPW/Economy/ParkStatisticsHistory.cs:95` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.InfoWindow.cs:19` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.InfoWindow.cs:213` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/World/Guests/GuestSimulation.cs:1490` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Economy/ParkStatisticsHistory.cs:96`: `public sealed class ParkStatisticsHistory`
- `source/OpenTPW/Hud/ParkHud.InfoWindow.cs:20`: `private bool[]? parkInfoIcons;`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original ring serialization and arrival stamp, icon message 0x80 handler, count alignment byte and ride/queue cell state.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Stage original-history import with save owner; keep guest time conversion separate; investigate UI-only questions without claiming the whole window missing.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Known original ring values and wrap; empty save; stay turn boundaries; icon empty-to-nonempty/reopen; count alignment; riding/queued census.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: SAVE-920..923, GUEST-920, UI-024, UI-060, UI-907 and guest/history owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
