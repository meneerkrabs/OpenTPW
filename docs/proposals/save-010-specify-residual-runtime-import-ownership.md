# Draft proposal: SAVE-010 — Specify residual runtime import ownership

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Many decoded world/controller/UI states are not applied; camera core and script/advisor clocks already import; arrival scheduler and world state remain.

## Source audit
- `source/OpenTPW/Economy/OriginalEconomyImport.cs:769` (APPROX).
- Current source `source/OpenTPW/Economy/OriginalEconomyImport.cs:770`: `var engineDumps = save.Layout.Blocks.Where( block => block.Block is SaveBlock.Scripts or SaveBlock.MessageCentre or SaveBlock.TrackRides or SaveBlock.FlyingRides or SaveBlock.Rsse or SaveBlock.Sound ).ToList();`.

## Missing evidence
Per-block consumers/lifecycle for weather/analyser/tags/HQ/UI/world state and camera yaw correction.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Create field-by-field import/derive/unsupported contract with owners; exclude raw sound-engine dumps and coaster work owned by #447.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Lost-world save does not resume normal simulation silently.
2. current arrival handle binds once.
3. advisor attach preserves pending/history.
4. camera correction tested only after evidence.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: ARRIVAL-001.
- Dependency: SAVE-013.
- Dependency: #447 coaster restore.
- Dependency: #438 docs overlap.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
