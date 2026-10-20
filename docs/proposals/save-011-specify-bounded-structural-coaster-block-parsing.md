# Draft proposal: SAVE-011 — Specify bounded structural coaster block parsing

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Coaster-containing payload split still searches delimiters and rejects ambiguous/repeated tag bytes unlike original structural reader.

## Source audit
- `source/OpenTPW.Files/Formats/Save/SavePayloadLayout.cs:154` (APPROX).
- Current source `source/OpenTPW.Files/Formats/Save/SavePayloadLayout.cs:155`: `var search = new DelimiterSearch( payload, start, profile );`.

## Missing evidence
Original block end widths and real built-coaster saves containing embedded tag bytes.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan structural boundary selection using decoded type-dependent train/car dimensions; preserve size/count hardening.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Embedded COAS-like bytes inside data do not split block.
2. truncated sections reject deterministically.
3. tag mismatch after exact end rejects.
4. synthetic TPW fallback remains explicitly labeled.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: SAVE-920.
- Dependency: #447 coaster decoder.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
