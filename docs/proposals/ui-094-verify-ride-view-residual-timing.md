# UI-094: Verify ride-view residual timing

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Ride camera exists and shares original projection; static duration is measured 0.33s, seat-forward axis/look signs remain inferred, prototype heading drops Z, the invisible original window is absent, and crowd counting keeps the pre-ride park target without a traced original target source.

## Source sites
- `source/OpenTPW/Client/GameFlow.cs:469` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.RideView.cs:15` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Hud/ParkHud.RideView.cs:20` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/World/RideCameraMode.cs:13` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW.Tests/RideCameraModeTests.cs:17` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Client/GameFlow.cs:470`: `if ( level.Guests == null || (Camera.Mode as ParkCameraMode ?? level.ParkCameraWhileRiding) is not { } camera )`
- `source/OpenTPW/Hud/ParkHud.RideView.cs:16`: `public const float RideStaticSeconds = 0.33f;`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Frame-timed static capture, seat-axis/look matrix trace, prototype seat evidence, crowd-call camera-cell source and function of invisible original ride window.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Constrain work to timing, seat/fallback axes, crowd target and invisible-window role; preserve traced right-click two-second guard and existing camera-car choice.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- 0.30-0.35s capture range; absent seat fallback; sloped prototype heading; pointer right/down signs; moving ride with stationary park target and crowd groups; earliest allowed right-click; Escape; shared projection.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-095, ride camera and train/seat owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
