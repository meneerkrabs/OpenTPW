# Draft proposal: GUEST-010 — Specify wandering from linked nonpath cells

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
GuestWander rejects linked cells other than path or queue and restricts candidate link types.

## Source audit
- `source/OpenTPW/World/Guests/GuestWander.cs:125` (APPROX).
- `source/OpenTPW/World/Guests/GuestWander.cs:146` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestWander.cs:127`: `if ( !grid.IsWalkable( start.X, start.Y ) && !grid.IsQueue( start.X, start.Y ) )`.
- Current source `source/OpenTPW/World/Guests/GuestWander.cs:148`: `var linked = (links & GuestPathGrid.LinkValue( direction )) != 0 && (grid.IsWalkable( cell.X + dx, cell.Y + dy ) || onQueue && grid.IsQueue( cell.X + dx, cell.Y + dy ))`.

## Missing evidence
Original linked-cell type removal table, including entrance type 9/10 and mask-1 exit behavior.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Describe generalized link traversal once cell links are available; preserve stranded caching already implemented for guests.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Linked nonpath start reaches legal path destination.
2. queue does not reverse its own predecessor link.
3. mask-1 gate second pass can leave path.
4. stranded guest consumes no new draw.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-005.
- Dependency: ECON-050.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## October 20 bounded implementation

The linked entrance/exit start slice is implemented; the broader plan above remains residual. See [bounded contract](../LINKED-ACCESS-WANDER.md) and LinkedAccessWanderTests. This does not claim generalized linked-cell fidelity or retire GUEST-010. #517 merged on 2026-10-20 with exact-head CI green; no original-media or rendered result is claimed.
