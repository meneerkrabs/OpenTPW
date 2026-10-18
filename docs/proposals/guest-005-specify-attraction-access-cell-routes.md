# Draft proposal: GUEST-005 — Specify attraction access-cell routes

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
AccessCell picks outside entrance/first path neighbour rather than shape's first entrance cell used by bins and other searches.

## Source audit
- `source/OpenTPW/World/Level.Objects.cs:253` (APPROX).
- Current source `source/OpenTPW/World/Level.Objects.cs:254`: `private static (int X, int Y)? AccessCell( OriginalObject item, GuestPathGrid grid )`.

## Missing evidence
Route finder behavior when destination lies inside footprint; +0x36 first entrance writer.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define access versus join versus occupancy cell contracts; coordinate with WALK-005 before changing goal cells.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Bin inside footprint can be route goal if original allows.
2. nearer unreachable bin leaves evidenced stuck state.
3. multiple entrances select original first.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-005.
- Dependency: ECON-022.
- Dependency: GUEST-019.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
