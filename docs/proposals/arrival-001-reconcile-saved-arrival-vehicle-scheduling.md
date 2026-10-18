# Draft proposal: ARRIVAL-001 — Reconcile saved arrival vehicle scheduling

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
UpdateArrivalVehicle begins with fresh scheduler state although vehicle scripts can resume; pickup cell chains contain guests only.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:674` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:675`: `private void UpdateArrivalVehicle()`.

## Missing evidence
Decode saved +0x1DA72A and vehicle handles; establish scheduler/script consistency at statuses -1 through 4.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify a scheduler restore contract and diagnostic for absent vehicle bindings; share cell ordering with WALK-003 rather than adding a second list.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ArrivalVehicleTests.cs`, `source/OpenTPW.Tests/ArrivalWalkTests.cs`.
1. Resume during delivery without duplicate guests.
2. resume pickup at status 4 with exact board order.
3. absent handle produces explicit fallback.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: SAVE-010.
- Dependency: WALK-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
