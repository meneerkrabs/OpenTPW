# Draft proposal: WALK-010 — Specify thing pool identity lifecycle

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Unsupported things take no live slots, imported placeholders never simulate/free, and attraction histories use script ids.

## Source audit
- `source/OpenTPW/World/ThingPool.cs:44` (APPROX).
- Current source `source/OpenTPW/World/ThingPool.cs:51`: `public sealed class ThingPool`.

## Missing evidence
Constructors/destroy callers beyond online code and saved placeholder lifecycle; distinguish runtime and persistent identity.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan explicit identity mapping and unsupported-thing lifecycle policy, not fabricated simulation of missing objects.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/WalkTests.cs`, `source/OpenTPW.Tests/GuestThingListOrderTests.cs`, `source/OpenTPW.Tests/OriginalThingSlotsTests.cs`.
1. Mixed allocation/deferred destroy matches LIFO reuse.
2. history reference resolves correct thing despite differing script id.
3. placeholder not freed without evidence.
4. original load free list stays ascending.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: RIDES-019.
- Dependency: SAVE-002.
- Dependency: SAVE-006.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
