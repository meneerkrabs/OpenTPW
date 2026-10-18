# Draft proposal: ARRIVAL-003 — Bound initial ride point eligibility

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
ParkPoints uses AcceptsGuests including a three-sample guard and a kart/water track flag initially false.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:1770` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:1771`: `public int ParkPoints()`.

## Missing evidence
Witness placement-to-first-circuit ordering and original complete-track flags before samples exist.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Separate safe runtime sample availability from original economic eligibility; define import dependency without pretending coaster reconstruction exists.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ArrivalVehicleTests.cs`, `source/OpenTPW.Tests/ArrivalWalkTests.cs`.
1. Compare points before and after first circuit walk.
2. incomplete/open-cut coaster gives no false admission.
3. imported coaster stays explicitly blocked pending #447.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: SAVE-920.
- Dependency: QUEUE-023.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
