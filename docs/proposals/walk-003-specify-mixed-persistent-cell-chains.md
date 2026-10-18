# Draft proposal: WALK-003 — Specify mixed persistent cell chains

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Separation lists guests then staff, excludes using/held/gone guests, and omits avatar instead of mixed entry-order chain.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:1229` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:1230`: `private (int X, int Y) SeparationForStep( GuestNavigation self )`.

## Missing evidence
Thing membership/position of riding/held/leaving guests and staff cell enter/leave timing.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Design one persistent typed cell-order contract for supported persons; explicitly adjudicate online-avatar scope.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/WalkTests.cs`, `source/OpenTPW.Tests/GuestThingListOrderTests.cs`, `source/OpenTPW.Tests/OriginalThingSlotsTests.cs`.
1. Interleaved guest/staff entries return first five in original order.
2. held/ride transitions have evidenced membership.
3. save/load preserves chain order.
4. moving cells removes old link.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: ECON-022.
- Dependency: SAVE-002.
- Dependency: Online avatar scope.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
