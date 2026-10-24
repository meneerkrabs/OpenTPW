# GATE-006: Validate queue walk bounds

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `evidence_only`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
M3Gate uses enumerated QueueCellWalkTurns=27 and move-up terms, not a proof for all separation/neighbor configurations; the looser bound was already accepted.

## Source sites
- `source/OpenTPW/Client/M3Gate.cs:1338` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Client/M3Gate.cs:1451` — APPROX annotation and adjacent implementation inspected.
- `source/OpenTPW/Client/M3Gate.cs:1480` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/Client/M3Gate.cs:1339`: `internal static (long Turns, string Derivation) HeadNotReadyBound( int queueCells )`
- `source/OpenTPW/Client/M3Gate.cs:1452`: `internal const long QueueCellWalkTurns = 27;`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- Adversarial walk/separation traces and, for fidelity claims, original executable reference traces.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Extend deterministic enumeration to uncovered neighbor and corner states; document counterexamples or bounded domain without changing accepted gates merely to silence failures.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Speeds at 0.59 and 2.0; 0/1/25-cell queues; all short turn sequences; maximum/off separation; riding/held/staff neighbor ordering.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: WALK-003, WALK-005 and gate acceptance owner.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.
