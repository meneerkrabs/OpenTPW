# Draft proposal: WALK-005 — Specify structural walk edge inputs

- Classification: **missing**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Blocked lacks track record flag nibble, type9 owner-ride mask2 predicate and underground link byte.

## Source audit
- `source/OpenTPW/World/Guests/WalkSearch.cs:26` (APPROX).
- Current source `source/OpenTPW/World/Guests/WalkSearch.cs:27`: `public static bool Blocked( ParkCellMap map, int x, int y, int direction, int mask )`.

## Missing evidence
TRACK-002 record access, +0xE low-nibble writers and underground link lifecycle.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define edge-test input interface shared by drop/routing; no synthetic assumptions replacing unknown flags.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/WalkTests.cs`, `source/OpenTPW.Tests/GuestThingListOrderTests.cs`, `source/OpenTPW.Tests/OriginalThingSlotsTests.cs`.
1. Each direct/extension track kind tests nibble zero/nonzero.
2. mask2 type9 consults ride owner.
3. underground links selected correctly.
4. byte-edge bounds unchanged.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TRACK-002.
- Dependency: ECON-050.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
