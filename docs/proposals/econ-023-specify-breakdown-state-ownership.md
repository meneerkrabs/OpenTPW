# Draft proposal: ECON-023 — Specify breakdown state ownership

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Hosted breakdown state spans visitor bridge and economy flag with rising-edge synchronization; headless immediate repair is intentional.

## Source audit
- `source/OpenTPW/Economy/ParkEconomy.cs:838` (APPROX).
- Current source `source/OpenTPW/Economy/ParkEconomy.cs:839`: `if ( item.ScriptHosted )`.

## Missing evidence
Trace break/repair/condemn transitions and save boundaries to establish whether split ownership changes behavior.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Produce invariant and transition table before proposing one authoritative hosted state; explicitly preserve headless extension.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/ParkCalendarTurnTests.cs`, `source/OpenTPW.Tests/ShopGoodsCostTests.cs`.
1. VAR_BREAKSTAT precedes script VAR_BROKEN.
2. mechanic waits for clear before repaired state.
3. condemned state cannot look repaired.
4. headless tests retain immediate contract.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: QUEUE visitor bridge.
- Dependency: EXT:headless-economy-breakdown.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## Bounded implementation (2026-10-20)

Restores the supported TPW original-save retained broken/condemned visitor-bridge state after script/coaster attachment, only for a unique matching saved/economy/runtime owner, closed saved/runtime state and an available script. It uses the economy's existing gauge normalization, does not replay a breakdown event, and never rewrites script words. Unsupported cases report a reason without mutation. Synthetic regression cases cover ownership, unsupported states, idempotence, script preservation and queue drainage. This is not original VM continuation: ECON-023 remains unresolved and the broader ownership transition ledger and original oracle are still outstanding.
