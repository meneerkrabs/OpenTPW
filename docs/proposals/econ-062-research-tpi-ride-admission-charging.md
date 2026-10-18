# Draft proposal: ECON-062 — Research TPI ride admission charging

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Catalog has TPI ride price keys but prices are enabled only for shops/sideshows, so rides earn no fee.

## Source audit
- `source/OpenTPW/Economy/EconomyObjectCatalog.cs:149` (APPROX).
- Current source `source/OpenTPW/Economy/EconomyObjectCatalog.cs:150`: `var hasPrices = kind is ParkObjectKind.Shop or ParkObjectKind.Sideshow;`.

## Missing evidence
TPI charge point, refusal outcome, refund rules and expected-price thresholds.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Prepare TPI-only payment lifecycle proposal and adjudication, preserving free TPW rides.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Price 20 produces exactly one debit/credit at evidenced point.
2. refusal does not board or double-charge.
3. TPW ride remains free.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TPI scope decision.
- Dependency: QUEUE admission protocol.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
