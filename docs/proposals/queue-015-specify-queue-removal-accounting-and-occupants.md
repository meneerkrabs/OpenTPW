# Draft proposal: QUEUE-015 — Specify queue removal accounting and occupants

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Removal lacks outside-cell charge and corner counters; off-queue guests may be moved where original clears only cells then reevaluates place.

## Source audit
- `source/OpenTPW/World/Guests/QueuePaths.cs:387` (APPROX).
- Current source `source/OpenTPW/World/Guests/QueuePaths.cs:388`: `public static int RemoveFrom( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, bool removeFixed = false )`.

## Missing evidence
Original +0x40 demolition count and +0x20 corner counter lifecycle; called/state-14 exemptions.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify segment accounting and occupancy reevaluation separately; retain labeled one-click/backtrack extensions.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. No-laid-queue demolition charges one outside cell.
2. segment corner counters unwind.
3. state14/called guest not dismissed.
4. guest on resulting path retains position/place.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: QUEUE-014.
- Dependency: QUEUE-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
