# HEADS-008: Document bounce node mutation scope

## Status and classification
- Plan-only draft proposal; no implementation, tests-run claim, retirement or merge.
- Classification: `out_of_scope`.
- Baseline: `meneerkrabs/OpenTPW` main `5ccddcab` (2026-10-18).
- One ID per proposal; shared dependencies are coordination edges, not duplicated ownership.

## Source-verified residual
CollectBouncing derives node from current bounceNodeBase plus index; original snapshots node at insertion, but no shipped bounce script changes base mid-bounce.

## Source sites
- `source/OpenTPW/World/Guests/RideVisitorBridge.cs:1329` — APPROX annotation and adjacent implementation inspected.

## Implementation cross-check
- `source/OpenTPW/World/Guests/RideVisitorBridge.cs:1330`: `internal void CollectBouncing( List<(int Guest, int Node, double Start)> into )`
- Existing behavior described above was checked against implementation, not only the register summary.
- The presence of an APPROX marker alone is not evidence of an absent feature.

## Evidence needed
- No further evidence needed for unchanged shipped scripts; synthetic mutation establishes hypothetical difference.
- Available private Data contains assets, not an original executable; no new binary trace was obtained in this audit.
- Existing address annotations are provenance leads, not a claim that this audit independently verified instructions.
- Any original captures/assets remain private; fixtures should be synthetic or references/hashes, not redistributed game media.

## Bounded investigation and implementation plan
1. Confirm current ownership and rebase the proposal against the latest main before runtime changes.
2. Inventory the four shipped scripts and add a synthetic snapshot-vs-live-base case; only implement stored node identity if custom-script compatibility is explicitly included.
3. Turn each verified behavioral distinction into a small deterministic regression fixture before editing runtime paths.
4. Keep unknown behavior explicitly unresolved; an evidence-only outcome is valid if no mismatch is established.
5. Submit any later implementation with exact evidence, changed scope and test output for review.

## Acceptance tests to author and run later
- Existing four-script corpus unchanged; base changes after insertion distinguish old riders from new; slot removal and reuse remain consistent.
- Run the nearest existing unit suite plus targeted integration/visual checks for changed behavior.
- Compare baseline and candidate with identical inputs; label screenshots by platform, display settings and state.
- Report unsupported/corpus-external cases separately; synthetic consistency alone is not original-fidelity proof.

## Ownership and overlap
- Consult: RideVisitorBridge owner; custom-script compatibility boundary.
- Do not overwrite another worker's runtime edits or duplicate an existing active implementation PR.
- Merged UI #433 and languages #423 are baseline work, not open implementation dependencies.
- Evening #439 is an extension and is not proof of original lighting or behavior.

## Completion boundary
- This deliverable is a documentation proposal only; no gameplay source files are changed by this PR.
- Runtime work remains blocked on the evidence/ownership decisions listed above where applicable.
- No approximation is retired by creating this plan; no PR is claimed implemented or ready to merge.

## Current-main supersession (2026-10-27)

This section supersedes the historical missing-implementation and scope statements above, without removing their audit history. It is a public-source reconciliation against main `60c941de233ac45a0d8c7d680aef2269f655b8b6`, not a new original-executable trace, runtime change, test result, retirement or PR closure.

### Existing implementation and recorded status

- [RideVisitorBridge.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Guests/RideVisitorBridge.cs#L1575-L1615) now keeps a node number per occupied bounce slot. `CollectBouncing` reads `BounceNodeOf(index)`; `StoreBounceNode` records the current base plus the slot index. The [BOUNCE dispatch](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Guests/RideVisitorBridge.cs#L1676-L1707) stores that node only after a successful insertion. Later `BOUNCESETNODE` therefore does not move existing riders.
- [Snapshot capture](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Guests/RideVisitorBridge.cs#L350-L354) includes `BounceNodes` only when occupied slots differ from the current base-plus-index form. [Restore](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Guests/RideVisitorBridge.cs#L569-L575) retains stored nodes and derives the historical form when the field is absent. [Validation](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Guests/RideVisitorBridge.cs#L520-L521) rejects a stored-node count inconsistent with the bounce table.
- Existing [BounceRiderTests.cs](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW.Tests/BounceRiderTests.cs#L76-L113) contains synthetic base-mutation, snapshot round-trip, malformed-length and omitted-field checks. These test bodies were inspected, not executed in this reconciliation; their presence is not a new pass claim.
- Current main already records HEADS-008 as retired in [tools/fidelity_retired.txt](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/tools/fidelity_retired.txt#L177) and [docs/GUESTS.md](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/docs/GUESTS.md#L1150-L1152). This proposal is not performing or re-approving that retirement.

### Remaining action and boundary

Review this documentation supersession against the existing implementation and recorded retirement. Do not implement a second per-slot node store, revive the old live-base premise, or broaden the proposal into custom-script work on the strength of its historical text. Any newly demonstrated regression needs its own bounded evidence and verification.

The original proposal and progress fragment remain historical records. This addition changes no runtime code or register, runs no C# or original-game executable, publishes no original media, and makes no new test-pass, merge-readiness or closure claim.
