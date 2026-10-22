# Draft proposal: ECON-022 — Audit remaining staff world residuals

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Staff separation order, stranded-state cache, imported object-chain order, access cells, pickup environment rows and guard fallback remain; animation and guest watching claims are stale.
- Stale exclusion: StaffSprites.cs:133–147 selects performance clips; GuestSimulation.cs:1695–1724 turns guests toward performers. Do not propose those missing again.

## Source audit
- `source/OpenTPW/World/Staff/StaffWorldSimulation.Handymen.cs:39` (APPROX).
- Current source `source/OpenTPW/World/Staff/StaffWorldSimulation.Handymen.cs:41`: `public void UpdateStaff( ParkEconomy economy, long turn )`.

## Missing evidence
Original destination failure caching, attraction mNext import order and guard booth-route failure; pickup row lifetime.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Bound staff-state parity tests and shared navigation interfaces; exclude implemented StaffSprites performances and TurnToShowOrPerformer.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Unchanged blocked surroundings short-circuit route search.
2. pickup removes/restores environment rows once.
3. guard failed booth route sets guest down.
4. performance/watch regression stays green.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-003.
- Dependency: WALK-005.
- Dependency: GUEST-005.
- Dependency: SAVE-006.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
