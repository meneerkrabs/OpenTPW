# Draft proposal: ECON-061 — Research TPI flagged scrap values

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Scrap uses year rule except condemned zero and omits +0x43 bit 0x10 script-count branch.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:1381` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:1382`: `if ( Rules.ThemeParkInc && ObjectState( item ) == RideObjectStates.Condemned )`.

## Missing evidence
Trace flag writers and script-side count producing 100/67/34/0 percent.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Write TPI-specific branch evidence and proposed contract; await scope decision before runtime work.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Counts 0 through 3 map to witnessed percentages.
2. condemned/flag precedence verified.
3. unflagged year brackets retain TPW behavior.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TPI scope decision.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
