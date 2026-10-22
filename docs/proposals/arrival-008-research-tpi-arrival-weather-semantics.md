# Draft proposal: ARRIVAL-008 — Research TPI arrival weather semantics

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
TPI arrivals have percentage math but no weather state or mode-3 suppression source.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:803` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:804`: `var state = ArrivalWeatherState?.Invoke() ?? 0;`.

## Missing evidence
Game.exe weather +0x70 writers and object behind 0xA06558; actual rain/storm/snow/heatwave captures.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Prepare a TPI-only evidence matrix and scope decision; do not impose TPI weather on TPW.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ArrivalVehicleTests.cs`, `source/OpenTPW.Tests/ArrivalWalkTests.cs`.
1. Map all four weather states to percentages.
2. mode 3 has evidenced zero-arrival case.
3. TPW 0.8/1.2 factors remain outside proposed change.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: TPI scope decision.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
