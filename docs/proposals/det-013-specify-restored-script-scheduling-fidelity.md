# Draft proposal: DET-013 — Specify restored script scheduling fidelity

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Script run state is imported but object animations, child relationships, object nodes and walk/bounce tables remain incomplete; host ticks differ.

## Source audit
- `source/OpenTPW/VM/RideScriptWorld.cs:70` (APPROX).
- Current source `source/OpenTPW/VM/RideScriptWorld.cs:71`: `public WorldRng World => world;`.

## Missing evidence
Original captures of TRIGWAITANIM, live child scripts, node lists and effects between 31ms substeps.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

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
