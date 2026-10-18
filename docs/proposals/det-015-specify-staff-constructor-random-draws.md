# Draft proposal: DET-015 — Specify staff constructor random draws

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Staff lacks person-constructor base-speed/reseed/variant draws; weather uses an intentional separate stream.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:80` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:81`: `public WorldRng World { get; private set; }`.

## Missing evidence
Per-role constructor call order and hired versus loaded-member variants; distinguish candidate draw from member construction.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define constructor draw contract with thing allocation order; retain weather-stream unless separately adjudicated.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestCreationDrawTests.cs`, `source/OpenTPW.Tests/GuestThingListOrderTests.cs`.
1. Hire each staff role with recorded seed and next world word.
2. load staff does not repeat candidate draws.
3. advisor mode-2 creates no spurious draw.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: WALK-010.
- Dependency: GUEST-014.
- Dependency: EXT:weather-stream.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
