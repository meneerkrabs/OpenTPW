# Draft proposal: ARRIVAL-006 — Adjudicate booth timing clock policy

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
The booth already waits N+1 executed park turns; only the fixed-step loop's no-drop policy differs.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:1376` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:1377`: `guest.BoothWait = wait;`.

## Missing evidence
Capture a slow original loop pass and the 0x54F680 drop cap; distinguish simulation turns from wall time.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Research whether fidelity needs an optional clock policy; preserve the fixed-60Hz extension until explicit scope decision.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ArrivalVehicleTests.cs`, `source/OpenTPW.Tests/ArrivalWalkTests.cs`.
1. N=50 and 99 each wait N+1 turns.
2. delayed host frames show clock-policy divergence only.
3. no duplicate booth verdict on catch-up.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: EXT:fixed-60hz-clock.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
