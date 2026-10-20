# Draft proposal: ECON-063 — Specify initial calendar day dispatch

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Calendar caches begin synchronized instead of day=-1, omitting original initial day message and ring roll.

## Source audit
- `source/OpenTPW/Economy/ParkCalendar.cs:106` (APPROX).
- Current source `source/OpenTPW/Economy/ParkCalendar.cs:107`: `public static CalendarChange Update( ref CalendarFields stored, long turn )`.

## Missing evidence
Verify new-park and restart-save day cache against listener ordering.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan explicit initialization/import cache contract, keeping first day dispatch distinct from later day changes.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`.
1. New park first turn rolls heads -1 to 0 once.
2. imported day=-1 dispatches once.
3. already synchronized save does not roll.
4. month/year listeners avoid duplicate charges.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: SAVE calendar cache import.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
