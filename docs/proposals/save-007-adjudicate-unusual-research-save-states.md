# Draft proposal: SAVE-007 — Adjudicate unknown research project identities

- Classification: **evidence-only**.
- Mode: Evidence and compatibility adjudication only.
- Baseline: public main `9ab29cac013d488d6c7422971186b2cddfe59321`; source inspected on 2026-10-27.
- Status: open, narrowed to unknown object/project identities. The known-but-unoffered current-project case is implemented and is not reopened here.

## Current residual
The original-save importer drops and reports a lab project whose object the level archives do not define, and a current project identity absent from its category's reconstructed table. The 11 pinned corpus labs contain neither case. Their agreement establishes ordinary-corpus coverage, not the compatibility boundary for missing identities.

Known current projects are preserved exactly as saved, even if the picker would not offer them now. `ParkResearch.SelectAsSaved` and `SavedResearchGroupTests.ASavedCurrentProjectThePickerWouldNotOfferIsKeptAsSaved` retain that behavior and its park-save round-trip. The saved highest groups and ordinary corpus controls/order are also preserved.

## Source audit
- `source/OpenTPW/Economy/OriginalEconomyImport.cs`: `ImportResearch` collects `unknown` and `refused` identities and reports them through `Research not imported`.
- `source/OpenTPW/Economy/ParkResearch.cs`: `SelectAsSaved` preserves known unoffered cursors but rejects identities absent from the requested category.
- `source/OpenTPW.Tests/OriginalSaveImportCorpusTests.cs`: `TheElevenCorpusLabsArePinned` covers the existing 11 ordinary saves, not missing identities.
- This review used public source only. Repository reverse-engineering annotations are evidence leads, not a newly executed original-game oracle.

## Missing evidence
Original continuation when a saved lab project or current cursor references an object/project identity absent from the available archives or reconstructed category, or an explicit decision that such saves are unsupported. Do not assume every such save is modified without fixture evidence.

## Bounded proposal
Evidence-only compatibility matrix and supported-save scope decision for missing identities; preserve precise unknown-project diagnostics. Do not revert known-current-project preservation or change the 11 corpus pins.

## Acceptance plan
1. Classify each missing-identity case from original behavior or an explicit supported-save scope decision.
2. Confirm an unknown InfoId or category/project identity yields a precise diagnostic, without affecting known but unoffered cursors.
3. Keep all 11 known lab tables and the existing saved-cursor regressions unchanged.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Run the affected `OriginalSaveImportTests`, `OriginalSaveImportCorpusTests` and `SavedResearchGroupTests`; these are required integration checks, not tests executed by this source-only proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: original continuation evidence or an explicit missing-identity supported-save scope decision.

## Completion boundary
Keep this proposal draft while the residual evidence or scope decision is unavailable. Any later implementation or approximation retirement requires separate verified results. This correction restores the narrow unresolved classification; it does not alter import behavior or claim a new original-game oracle result.
