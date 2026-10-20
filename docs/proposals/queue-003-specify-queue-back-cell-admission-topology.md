# Draft proposal: QUEUE-003 — Specify queue back-cell admission topology

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Guests join from attached path instead of physically reaching back queue cell; front stored separately from entrance link.

## Source audit
- `source/OpenTPW/World/Guests/RideVisitorBridge.cs:746` (APPROX).
- Current source `source/OpenTPW/World/Guests/RideVisitorBridge.cs:747`: `private (int X, int Y)? FindJoinCell( GuestPathGrid grid )`.

## Missing evidence
Original edge-test route onto queue back and entrance-link writer.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Design typed entrance/back links and admission precondition shared with walking; preserve fourth-place path exception.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. First three queue places originate back cell.
2. fourth can occupy attached path.
3. rotated entrance link selects exact front.
4. disconnected back refuses route.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-005.
- Dependency: ECON-050.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## October 20 bounded implementation follow-up

The historical plan above is retained as scope history. Back-cell admission and
fourth-place geometry were already implemented by other changes. The current
[typed-front contract](../reverse/QUEUE-003-typed-front.md) narrows this change to
nonzero front selection from supported live typed entries. Zero-front semantics,
untyped compatibility, divergent imported mEntryPos and native cache timing remain
explicit residuals. Synthetic source tests are added; runtime results await CI.
This is not full QUEUE-003 retirement or original-game runtime verification.
