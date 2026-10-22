# Draft proposal: ECON-060 — Research TPI monthly running costs

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Current TPI costs enter TPW daily rings and omit accumulated monthly statistic amount; debit ordering is untraced.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:1295` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:1296`: `private void PayRunningCosts()`.

## Missing evidence
Game.exe +0x938 accumulator and month message order versus bank/wages/loans.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
TPI-only ledger/ring design and scope adjudication, without replacing TPW daily accounting.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Month boundary debits base plus accumulated cost once.
2. state 1/4 running cost zero.
3. monthly and daily heads distinguish.
4. TPW ledger baseline unaffected.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TPI scope decision.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
