# Draft proposal: GUEST-013 — Specify staff track-record placement checks

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
StaffPlacement tests map type/land but cannot resolve track records or extension parents; visit/TPI uses broad occupancy fallback.

## Source audit
- `source/OpenTPW/World/Staff/StaffPlacement.cs:42` (APPROX).
- Current source `source/OpenTPW/World/Staff/StaffPlacement.cs:43`: `public static bool IsValidDropCell( ParkCellMap cells, int x, int y, Func<int, int, bool>? occupied )`.

## Missing evidence
TRACK-002 structural record model and flags for kinds 11/13/16/18/25 and parent kinds 12/17.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan a shared structure query for drop and walking; isolate visit/TPI policy from baseline TPW.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Type 9 entrance accepts legal drop.
2. each blocking direct/parent kind refuses.
3. unowned land always refuses.
4. empty allowed types 0/1/3/9 remain accepted.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TRACK-002.
- Dependency: WALK-005.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
