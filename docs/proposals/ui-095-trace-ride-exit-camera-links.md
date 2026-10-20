# UI-095: Trace ride-exit camera links

## Status and classification
- Bounded implementation added on 2026-10-20; remains draft pending CI. No local test-pass, full-fidelity retirement or merge is claimed.
- The original audit below is historical. Independent native evidence and correction (see `docs/progress/2026-10-20-ride-exit-cell-direction.md` in the repository) supersede its owner-anchor/link premise: the native consumer requires entrance ownership, targets mExitPos, and reads the exit direction byte +0x0D, not neighbour links +0x0C.
- Classification: `missing_behavior`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
Exit restores prior camera and recenters, but placed object link bytes are absent so original yaw rule never fires; host pose/smoothing also differs.

## Source sites
- `source/OpenTPW/World/Level.RideView.cs:44` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Level.RideView.cs:45`: `public void ExitRideView()`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Original ride-cell link writers and camera pose update trace.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Map placed-object link ownership first; add exit yaw only where links are established, retaining terrain-walk instance restoration.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Placed ride with each masked 0x55 direction; no link; CannotRide object; prior walking camera; repeated enter/exit; target removed.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: UI-091, UI-094, placement and camera owners.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
