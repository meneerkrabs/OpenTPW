# Draft proposal: DET-013 — Specify restored script scheduling fidelity

- Classification: **approximation**.
- Mode: Evidence-gated partial implementation; ordinary-child graphs only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: DRAFT PARTIAL IMPLEMENTATION. Ordinary-child import is implemented with synthetic regression coverage; compilation/runtime CI is pending. No identifier retirement or full original fidelity is claimed.

## Current residual
Script run state is imported but object animations, child relationships, object nodes and walk/bounce tables remain incomplete; host ticks differ.

## Source audit
- `source/OpenTPW/VM/RideScriptWorld.cs:70` (APPROX).
- Current source `source/OpenTPW/VM/RideScriptWorld.cs:71`: `public WorldRng World => world;`.

## Missing evidence
Original captures of TRIGWAITANIM, live child scripts, node lists and effects between 31ms substeps.
- Authorized local static TPW executable evidence now establishes loader and ordinary/sound child link semantics; no original saved-child runtime fixture or executed original-game oracle was added.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## October 20 bounded implementation

[Ordinary-child import contract](det-013-ordinary-child-import.md) records the independently reviewed implementation scope. Exact embedded code/string identity, full folder matching, reciprocal normal children and saved scheduling state are prepared before a whole-block commit. Any sound edge or malformed/unresolved graph refuses unchanged. Optional own-save list order preserves non-ID scheduler order; old snapshots retain their legacy behavior.

Sound ownership differs from ordinary parent identity in the executable and is not mapped speculatively. NAME, object animation/list and node/bounce/walk table state remain unrestored. TRIGWAITANIM keeps its existing retrigger gate; acceptance item 1 below remains open. Tests are synthetic, and runtime CI has not yet passed.

## Bounded proposal
Define state ownership and replay checkpoints; distinguish approved explicit-seed and 60Hz extensions from missing restore state.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestCreationDrawTests.cs`, `source/OpenTPW.Tests/GuestThingListOrderTests.cs`.
1. Resume TRIGWAITANIM without retrigger after animation restore.
2. child script is neither duplicated nor lost.
3. ordered RNG/effects trace matches at substep boundary.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: SAVE-013.
- Dependency: #437 WAIT4ANIM overlap.
- Dependency: EXT:fixed-60hz-clock.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
