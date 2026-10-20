# Draft proposal: SAVE-013 — Specify sprite and particle state restore

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Sprite program counter/wakes are not applied; particles omit flags/groups/templates and effector matching relies on unchecked indirect writes.

## Source audit
- `source/OpenTPW/World/Particles/SaveParticleState.cs:17` (APPROX).
- Current source `source/OpenTPW/World/Particles/SaveParticleState.cs:20`: `private const int EffectorMatchFrom = 0x30, EffectorMatchTo = 0x44;`.

## Missing evidence
Live sprite-script save and effector fixture; full template-byte writer audit including base-register writes.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define restoreable presentation state and unsupported capability diagnostics; do not infer fidelity from empty corpus cases.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Saved walking sprite resumes phase.
2. modified emitter template does not silently become file default.
3. ambiguous effector identity rejects.
4. screen-space Button emitter hidden after load.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: DET-013.
- Dependency: SpritePlayer/particle owners.
- Dependency: #437 limited overlap.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
