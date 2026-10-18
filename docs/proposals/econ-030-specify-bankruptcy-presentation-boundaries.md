# Draft proposal: ECON-030 — Specify bankruptcy presentation boundaries

- Classification: **missing**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
World loss is implemented, but loss panel is missing, warning producers absent, and purchase/hire restrictions lack original evidence.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:1339` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:1340`: `private void HandleBankruptcyMessage()`.

## Missing evidence
Trace 0x4A2925 plus state-4 UI paths and advice 102-105 month-in-red scores.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan loss panel/tool closure and evidence-led interaction policy; delegate warning producer ownership to ADVISOR-009.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Entering loss closes active tool and hides correct windows.
2. advice 106 repeats per bank event.
3. pause/play remain functional.
4. unsupported purchase restrictions are not asserted original.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: ADVISOR-009.
- Dependency: HUD loss panel.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
