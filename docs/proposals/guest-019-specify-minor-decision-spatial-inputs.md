# Draft proposal: GUEST-019 — Specify minor-decision spatial inputs

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
MinorDecision indexes rides by entrance and routes to back cell, while original uses thing cell/access destination; major destination missing.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:2414` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:2415`: `internal void MinorDecision( Guest guest )`.

## Missing evidence
Thing-position cell and +0x36 writer; major target consumer and route comparison.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Separate scoring window position, route metric endpoint and final destination fields.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Ride thing inside window but entrance outside is considered correctly.
2. route comparison uses target access cell.
3. twelfth state-10 update only.
4. new destination does not overwrite major target prematurely.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: GUEST-002.
- Dependency: GUEST-004.
- Dependency: GUEST-005.
- Dependency: WALK-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## Source-only reconciliation (2026-10-27)

The preceding plan is preserved as the 2026-10-18 baseline, not a statement that the implementation is still missing. At reviewed main `eb8e3d2018eeeb726b5143caf51f75ce9f0681e1`, `docs/GUESTS.md:1216` and `tools/fidelity_retired.txt:179` record GUEST-019 as retired by merged [PR #876, guest destinations](https://github.com/meneerkrabs/OpenTPW/pull/876) (merge `9ab29cac013d488d6c7422971186b2cddfe59321`). The missing spatial/major-target pieces were added in `d7ad9b11a3bcdcb02a0acd77185c639687110ab7`; the retirement was recorded in `871eb4b6861f268a206702939cf8e7234f2ddbf8`, with review corrections included in that merge.

- Current source at that main: `source/OpenTPW/World/Guests/GuestSimulation.cs:2540-2545,2573-2588,2610-2618` contains the twelfth-update call, position-cell candidate window, routes measured from the target's access cell, access-cell destination and saved old target. Lines 3486-3501 consume `SavedMajorAttractionId` after the exit walk.
- `source/OpenTPW/World/Level.Objects.cs:450-473` supplies the placed object's position and first-entrance access cells. The null/entrance fallback for bridges without placed objects remains explicit in `source/OpenTPW/World/Guests/IRideVisitorBridge.cs:209-216`; this note does not widen that boundary or take over #527's access-cell scope.
- Synthetic regression source exists in `source/OpenTPW.Tests/GuestDestinationTests.cs:25-207` for the position-cell decoy, access-cell detour, major-target promotion/removal and snapshot handling. These tests were inspected, not executed here; their presence is not a fresh pass of the original acceptance plan.

Proposed disposition: treat the missing-implementation premise as superseded; do not duplicate the merged work. Preserve the original evidence, acceptance and completion gates and draft status pending a separate disposition review. This source-only reconciliation supplies no new original-game trace, fixture qualification, runtime/GPU/native acceptance result or fresh retirement decision, and changes no gameplay, register or progress fragment.
