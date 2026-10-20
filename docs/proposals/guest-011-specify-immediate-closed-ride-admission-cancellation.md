# Draft proposal: GUEST-011 — Specify immediate closed-ride admission cancellation

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Close clears loading call only at next ride update instead of immediately; booth gate wait lacks distinct state.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:860` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:864`: `if ( guest.State == GuestState.LeavingPark && GuestLifecycle.ShouldRecall( guest.Happiness, guest.ExitLevel, Payments?.ParkClosed ?? false )`.

## Missing evidence
Trace close versus AdmitPerson within same turn and gate status transitions.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define close-time cancellation and gate waiting transitions without mass-teleporting queues.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Close between call and admit rejects boarding immediately.
2. already taken rider completes ride.
3. one queue-head dismissal per turn.
4. paid guest waits only for evidenced gate state.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: QUEUE visitor bridge.
- Dependency: ARRIVAL-006.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
