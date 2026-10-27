# Draft proposal: SAVE-007 — Adjudicate unusual research save states

- Classification: **evidence-only**.
- Mode: Evidence and compatibility adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: superseded: SAVE-007 was retired in fidelity round 9 (package R9-C) with the 11 corpus labs pinned; see docs/ECONOMY.md.

## Current residual
Importer drops unknown projects and current projects no longer offered; known corpus controls/order already agree.

## Source audit
- `source/OpenTPW/Economy/OriginalEconomyImport.cs:749` (APPROX).
- Current source `source/OpenTPW/Economy/OriginalEconomyImport.cs:750`: `var dropped = new List<string>();`.

## Missing evidence
Modified or otherwise valid save with closed-group current project; original continuation behavior and missing-asset semantics.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Evidence-only compatibility matrix and scope decision for modified saves; preserve strict unknown-asset diagnostics.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Closed-group saved current project is classified from original behavior.
2. unknown InfoId yields precise diagnostic.
3. all 11 known lab tables stay unchanged.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: Modified-save scope decision.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
