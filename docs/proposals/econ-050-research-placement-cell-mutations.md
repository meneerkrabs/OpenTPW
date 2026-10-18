# Draft proposal: ECON-050 — Research placement cell mutations

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
ClearFootprint empties typed cells; several placement links and pre-write clears are unmodeled and original destructor clearing is not established.

## Source audit
- `source/OpenTPW/World/ParkCellMap.cs:422` (APPROX).
- Current source `source/OpenTPW/World/ParkCellMap.cs:423`: `public void ClearFootprint( IEnumerable<(int X, int Y, ObjectShapeCell Cell)> footprint )`.

## Missing evidence
Trace placement clear/write order, @ links and object removal callers beyond destructor.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify a cell-mutation transcript for place/remove with preserved unrelated layers; no speculative destructor rewrite.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Place across path and nonpath records exact mutations.
2. @ entrance links are bidirectional as evidenced.
3. remove object preserves unrelated track/land flags.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TRACK-002.
- Dependency: WALK-005.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
