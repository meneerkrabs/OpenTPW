# Draft proposal: GUEST-007 — Trace prank advice context consumers

- Classification: **evidence-only**.
- Mode: Evidence and compatibility adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Prank outcomes work, but queued advice drops prankster id/cell and cell membership may omit eligible guests.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:1902` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:1903`: `private bool Prank( Guest guest, GuestDecision.Prank prank, LitterType type )`.

## Missing evidence
Find readers of queued record +0/+4 and guest-state thing-list membership.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Evidence-led decision on retaining advice context; share mixed cell-chain model with WALK-003.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Two queued pranks retain correct actor/cell if consumed.
2. balloon victim follows list head order.
3. irrelevant debug-only event remains unmodeled.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-003.
- Dependency: ADVISOR-009.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
