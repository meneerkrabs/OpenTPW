# Draft proposal: SAVE-012 — Adjudicate original restart save semantics

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Restart uses own park-resume JSON rather than original restart.INTS; snapshot-per-start is an intentional extension.

## Source audit
- `source/OpenTPW/Client/GameFlow.Restart.cs:49` (APPROX).
- `source/OpenTPW.Files/Formats/Save/SaveNewParkThings.cs:48` (APPROX).
- Current source `source/OpenTPW/Client/GameFlow.Restart.cs:51`: `internal void TakeRestartSnapshot( Level level, IReadOnlyDictionary<string, string>? verifiedAssets = null, bool overFrames = false )`.
- Current source `source/OpenTPW.Files/Formats/Save/SaveNewParkThings.cs:49`: `public static byte[] AdvisorController( ushort mapChild, ushort mapParent )`.

## Missing evidence
Original state15 write and state10-14 reload capture; supported restart.INTS fixture.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Research compatibility option and scope decision without replacing approved snapshot policy; separate restart source from camera restoration.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Repeated restart reuses same source snapshot.
2. online visit skips original snapshot creation.
3. loaded restart yields consistent starting state.
4. own-format restart remains supported.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: EXT:restart-snapshot-per-start.
- Dependency: SAVE-010.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
