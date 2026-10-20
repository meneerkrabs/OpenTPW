# Draft proposal: SAVE-006 — Specify attraction metadata and claims restore

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Unclaimed saved staff assignments are discarded; non-gate signs use defaults and raw runtime references are rebuilt.

## Source audit
- `source/OpenTPW/Economy/OriginalEconomyImport.cs:391` (APPROX).
- Current source `source/OpenTPW/Economy/OriginalEconomyImport.cs:393`: `foreach ( var held in save.Attractions.Where( item => item.AssignedStaffMember != 0 && !resumedClaims.Contains( item.Id ) ) )`.

## Missing evidence
Saved attraction/staff graph, orphan-claim expiry and sign customizations across supported saves.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify safe persistent fields versus rebuilt handles and delayed reference binding; never restore raw engine pointers.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/OriginalStaffImportTests.cs`.
1. Custom attraction names survive import.
2. active claim keeps saved turn.
3. orphan claim expires at original >100 threshold if evidenced.
4. no stale mesh/script handle dereference.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed save/import maintainer; assignment remains open.
- Dependency: SAVE-002.
- Dependency: ECON-022.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
