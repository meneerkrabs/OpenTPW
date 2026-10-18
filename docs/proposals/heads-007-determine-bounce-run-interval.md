# HEADS-007: Determine bounce run interval

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `approximation`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
BounceRiders.RunStep divides elapsed milliseconds by 100, a guessed interval for the run-template stepper.

## Source sites
- `source/OpenTPW/World/Guests/BounceRiders.cs:77` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Guests/BounceRiders.cs:78`: `internal static int RunStep( int elapsedMilliseconds ) => elapsedMilliseconds / 100;`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Decoded 0x0074DBC0 template/0x00694340 interval or frame-timed original bounce capture.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Determine actual stepping/time units, then change only body animation interval and boundary handling.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Frames immediately before/at/after each boundary; long elapsed time; bounce start reset; pause/resume; all shipped bounce rides.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: Ride script timing owner and rider-renderer owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
