# Draft proposal: SAVE-944 — Validate TPI person save layouts

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Guest/staff/gardener layouts are loader-derived/emulated without real save witnesses; many word meanings unknown.

## Source audit
- `source/OpenTPW.Files/Formats/Save/ThemeParkIncBodyLayout.cs:58` (APPROX).
- `source/OpenTPW.Files/Formats/Save/ThemeParkIncSaveWorld.cs:262` (APPROX).
- Current source `source/OpenTPW.Files/Formats/Save/ThemeParkIncBodyLayout.cs:59`: `public sealed partial class ThemeParkIncBodyLayout`.
- Current source `source/OpenTPW.Files/Formats/Save/ThemeParkIncSaveWorld.cs:263`: `public sealed class ThemeParkIncSaveWorld`.

## Missing evidence
Actual TPI person saves across roles/ring counts and loader evidence for field types.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
TPI-only research and parser validation plan; keep bounds checks rather than emulate memory corruption.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Witness guest619-byte body.
2. all staff684-696 plus rings align.
3. gardener next-record boundary exact.
4. negative/overcapacity ring counts reject safely.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: TPI scope decision.
- Dependency: Real TPI person saves.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
