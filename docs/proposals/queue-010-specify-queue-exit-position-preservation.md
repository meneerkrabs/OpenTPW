# Draft proposal: QUEUE-010 — Specify queue exit position preservation

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
StandAfterQueue keeps path/queue positions but teleports entrance-cell dismissals to front because entrance has no usable links.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:2991` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:2992`: `private void LeaveQueue( Guest guest, IRideVisitorBridge attraction, GuestThought thought )`.

## Missing evidence
Original route from entrance after dismissal or price refusal.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan entrance link support then remove fallback only for evidenced walkable original cells.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. Head price refusal remains on entrance cell.
2. ordinary queue exit stays in place.
3. next wander can leave entrance.
4. malformed unsupported geometry remains diagnosed.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-005.
- Dependency: QUEUE-003.
- Dependency: GUEST-010.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
