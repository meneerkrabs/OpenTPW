# Draft proposal: GUEST-002 — Separate queue scoring from route failure

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
ScoreAttraction returns zero for missing JoinCell while original scores back cell then fails edge routing.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:2200` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:2201`: `public uint ScoreAttraction( Guest guest, IRideVisitorBridge attraction )`.

## Missing evidence
Queue back-cell entry proof and original scorer-to-route order.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define scoring independent from endpoint reachability, coordinated with QUEUE-003 topology.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. No-join attractive ride is scored then rejected by route.
2. alternate reachable ride still selected.
3. tie-breaking and RNG transcript unchanged.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: QUEUE-003.
- Dependency: WALK-005.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
