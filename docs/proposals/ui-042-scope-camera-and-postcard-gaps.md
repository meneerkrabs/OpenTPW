# UI-042: Scope camera and postcard gaps

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Panel animates but ends at table geometry; camcorder arms a host walk; postcard photo is empty and preview/address-book controls disabled.

## Source sites
- `source/OpenTPW/Hud/ParkHud.CameraPanel.cs:14` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.CameraPanel.cs:97` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Online/OnlineScreens.cs:860` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Hud/ParkHud.CameraPanel.cs:15`: `private static readonly LayoutTable CameraPanelTable = OriginalLayoutTables.CameraPanel;`
- `source/OpenTPW/Hud/ParkHud.CameraPanel.cs:98`: `internal static float CameraPanelOffset( int step )`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Arm end transform, original camcorder input/eye and 640x240 capture/preview/address-book data flow.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Keep separate phases within this ID: resolve panel geometry, then local photo/preview, then address-book behavior; no sending or contact disclosure in this proposal.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Slide endpoints and ten steps; camcorder immediate activation; photo dimensions; empty/available preview states; cancel leaves no sent message.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Online/postcard owner, UI-095 and camera owner; communication remains separately authorized.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
