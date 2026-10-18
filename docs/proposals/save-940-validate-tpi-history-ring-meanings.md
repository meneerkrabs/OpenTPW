# Draft proposal: SAVE-940 — Validate TPI history ring meanings

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Ring structure is traced but names aggregate unclear conditions and available prebuilt histories are empty.

## Source audit
- `source/OpenTPW.Files/Formats/Save/ThemeParkIncAttraction.cs:14` (APPROX).
- Current source `source/OpenTPW.Files/Formats/Save/ThemeParkIncAttraction.cs:15`: `public sealed partial record ThemeParkIncAttraction(`.

## Missing evidence
Populated TPI attraction save and readers of +0x21c/thresholds 0xa01b1c..0xa01b44.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
TPI-only research/adjudication proposal with provisional names; no mandatory TPW runtime changes.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalSaveImportTests.cs`, `source/OpenTPW.Tests/OriginalSaveCorpusTests.cs`.
1. Populated ring values align to period/order.
2. rated/declined/need-failure branches separated by witnessed conditions.
3. unknown meanings remain provisional.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: TPI scope decision.
- Dependency: Populated TPI save.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
