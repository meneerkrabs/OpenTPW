# Draft proposal: ECON-059 — Establish shop-cost FPU precision

- Classification: **evidence-only**.
- Mode: Evidence and compatibility adjudication only.
- Baseline: proposal head `83e74e5042b3cfa217dc5314e9b4e794fde23bc6`, reconciled against main `0910d275638548f12ebfa41915bb71e64f091119` on 2026-10-30.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
The hardware-path precision premise is resolved in current main: the shipped D3DIM CreateDevice sets the economy thread's control word to `0x7F`, so the current single-precision steps remain appropriate. ECON-059 is narrowed to the software renderer path (`0x8BD524 = 4`), which creates no D3DIM device and is documented as retaining the C runtime's 53-bit precision. OpenTPW does not model that path's precision; its control word or discriminating sale-output evidence remains required. This is reconciliation of repository evidence, not a new original-runtime capture or a full-parity claim.

## Source audit
- [`ShopGoodsCost.cs`](https://github.com/meneerkrabs/OpenTPW/blob/0910d275638548f12ebfa41915bb71e64f091119/source/OpenTPW/Economy/ShopGoodsCost.cs): hardware control-word and renderer restore annotations; ECON-059 explicitly identifies the software-path residual. `Of` and `MultiplyAsSingle` retain staged binary32 rounding and exact unsigned-integer multiplication before nearest-even rounding.
- [`ECONOMY.md`](https://github.com/meneerkrabs/OpenTPW/blob/0910d275638548f12ebfa41915bb71e64f091119/docs/ECONOMY.md), ECON-059 and its narrowed scope: hardware `0x7F` is established; no additional oracle target is needed for that path. The software renderer can be selected explicitly or after failed hardware initialization.
- [`ShopGoodsCostTests.cs`](https://github.com/meneerkrabs/OpenTPW/blob/0910d275638548f12ebfa41915bb71e64f091119/source/OpenTPW.Tests/ShopGoodsCostTests.cs) and [`ShopGoodsCostPrecisionTests.cs`](https://github.com/meneerkrabs/OpenTPW/blob/0910d275638548f12ebfa41915bb71e64f091119/source/OpenTPW.Tests/ShopGoodsCostPrecisionTests.cs): inspected, not executed in this reconciliation. Existing synthetic coverage includes the `200, 51, 50, 2 -> 201` boundary, negative/sign-flipped ingredient terms, masked bytes, clamp transitions, nearest-even ties, unsigned-cost boundaries, signed wrapping and exact-integer multiplication discriminators. These protect the current host contract, not software-renderer parity.

## Missing evidence
Software-path control-word or discriminating sale-output evidence, including explicit software selection and fallback after failed hardware initialization. Record the actual execution path and relevant startup, cooperative-level and render-exit context; do not reintroduce an unresolved hardware-path premise.
- No original executable, original game media or runtime capture was accessed for this reconciliation.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Evidence-only software-path precision study and boundary vectors; preserve the established hardware contract and make no numeric rewrite until software execution context is verified.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to extend only when evidence justifies it: `source/OpenTPW.Tests/ShopGoodsCostTests.cs` and `source/OpenTPW.Tests/ShopGoodsCostPrecisionTests.cs`.
1. Compare labeled 24/53/64-bit intermediate vectors at integer boundaries, retaining the routine's single-precision stores; do not treat synthetic vectors as captured execution.
2. Include negative ingredient terms and preserve existing hardware-contract boundary regressions.
3. Report the renderer path, initialization/fallback context and captured control word or discriminating sale output represented by each native result.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: verified software-renderer execution evidence; no original executable or capture is supplied by this documentation change.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required software-path evidence or scope decisions are unavailable; hardware resolution alone does not retire ECON-059.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
