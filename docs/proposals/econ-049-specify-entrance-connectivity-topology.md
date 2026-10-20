# Draft proposal: ECON-049 — Specify entrance connectivity topology

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
QueueConnected returns true for no-queue and fallback single-cell entrances lacking typed entrance links.

## Source audit
- `source/OpenTPW/World/ParkGate.cs:262` (APPROX).
- Current source `source/OpenTPW/World/ParkGate.cs:263`: `internal static bool QueueConnected( GuestPathGrid grid, RideVisitorBridge ride )`.

## Missing evidence
Trace entrance link creation and no-queue object's front-cell test.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define typed entrance link requirements with cell-map owner; remove unconditional connectivity only after representation supports original test.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Disconnected shop fails original connectivity test.
2. connected no-queue attraction passes.
3. real queue back link comparison unchanged.
4. repair does not spuriously close open ride.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-005.
- Dependency: ECON-050.
- Dependency: QUEUE-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## Bounded implementation (2026-10-20)

Typed, owned TPW no-queue entrance geometry now requires reciprocal links with the actual outside raw path cell before reopening. Synthetic tests cover rotations, ownership and geometry fallback, state/RNG hash and script preservation, and the unchanged real-queue rule. Unsupported/untyped geometry, prototypes and TPI retain the permissive fallback; the guard does not close already-open rides. Exit connectivity and fallback geometry remain unresolved under ECON-049. No original-game execution or browser qualification is claimed.
