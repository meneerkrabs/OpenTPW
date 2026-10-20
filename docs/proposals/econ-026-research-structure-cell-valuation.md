# Draft proposal: ECON-026 — Research structure cell valuation

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Track cells substitute map type 0; hoardings use owned ring posts instead of traced piece footprints; maps without land count all terrain owned.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:1571` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:1572`: `private static (byte Type, bool Structure) CellForValue( ParkCellMap cells, int x, int y )`.

## Missing evidence
Track-tool cell writers/footprints and saved analyser +0x1F0EC net worth.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define structural layer valuation inputs independently of cell terrain type; compare saved net worth before implementation.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Same terrain under two track types retains base-cell value.
2. hoarding segment values exact occupied post cells.
3. owned/unowned and no-land profiles distinguished.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TRACK-002.
- Dependency: PATH-007.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
