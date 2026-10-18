# Draft proposal: QUEUE-023 — Research placement-triggered queue reevaluation

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Queue/path edits trigger reopen checks, but placement/removal click hooks and model routine 0x4547C0 are missing.

## Source audit
- `source/OpenTPW/World/Level.Objects.cs:217` (APPROX).
- Current source `source/OpenTPW/World/Level.Objects.cs:218`: `private void ReevaluateQueuedRide( OriginalObjectRuntime runtime )`.

## Missing evidence
Trace 0x52537A/0x5259AE/0x529890/0x52FFEC and model routine effect; prove placement can connect.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define event-source matrix and idempotent reevaluation contract before adding hooks.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. Placement that connects queue reopens eligible closed ride.
2. broken/condemned/called states remain closed.
3. removal triggers exact pass.
4. model notification does not duplicate queue edits.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: ECON-049.
- Dependency: ECON-050.
- Dependency: ARRIVAL-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
