# Draft proposal: QUEUE-012 — Reconcile queue terrain validation

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
QueueTerrainAllows rejects terrain/seed occupancy beyond original live map type and ownership rules.

## Source audit
- `source/OpenTPW/World/Level.Objects.cs:1058` (APPROX).
- Current source `source/OpenTPW/World/Level.Objects.cs:1060`: `private bool IsQueueBlocked( int x, int y ) =>`.

## Missing evidence
Compare map init/seed occupancy with validator 0x535670 for water/blocked/walkway type-0 cells.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define queue validation from live cell/owner state while retaining explicit pool/slide extensions.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. Owned type-0 water-origin cell follows original rule.
2. unowned always rejects.
3. lower-priority object demolished only on commit.
4. preview and commit agree.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: ECON-050.
- Dependency: EXT:pools-slides.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
