# Draft proposal: ECON-059 — Establish shop-cost FPU precision

- Classification: **evidence-only**.
- Mode: Evidence and compatibility adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
ShopGoodsCost rounds each step to float, but original simulation thread precision was never captured.

## Source audit
- `source/OpenTPW/Economy/ShopGoodsCost.cs:18` (APPROX).
- Current source `source/OpenTPW/Economy/ShopGoodsCost.cs:19`: `public static int Of( int costOfGoods, int quality, int ingredientAmount, int specialIngredient )`.

## Missing evidence
Original x87 control word at thread startup, cooperative-level changes, normal/failure render exits.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Evidence-only precision study and boundary vectors; no numeric rewrite until execution context is known.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. Compare 24/53/64-bit intermediate vectors at integer boundaries.
2. include negative ingredient term.
3. report which captured control words each result represents.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: Original executable and runtime capture.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
