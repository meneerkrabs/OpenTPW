# Draft proposal: GUEST-019 — Specify minor-decision spatial inputs

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
MinorDecision indexes rides by entrance and routes to back cell, while original uses thing cell/access destination; major destination missing.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:2414` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:2415`: `internal void MinorDecision( Guest guest )`.

## Missing evidence
Thing-position cell and +0x36 writer; major target consumer and route comparison.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Separate scoring window position, route metric endpoint and final destination fields.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Ride thing inside window but entrance outside is considered correctly.
2. route comparison uses target access cell.
3. twelfth state-10 update only.
4. new destination does not overwrite major target prematurely.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: GUEST-002.
- Dependency: GUEST-004.
- Dependency: GUEST-005.
- Dependency: WALK-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
