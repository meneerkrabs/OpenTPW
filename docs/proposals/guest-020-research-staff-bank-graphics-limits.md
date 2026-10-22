# Draft proposal: GUEST-020 — Research staff bank graphics limits

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Banks returns all fixed folder banks independent of original low/high graphics limit and archive order.

## Source audit
- `source/OpenTPW/World/Staff/StaffSprites.cs:73` (APPROX).
- Current source `source/OpenTPW/World/Staff/StaffSprites.cs:76`: `public static StaffBankSet? Banks( StaffType type, string level )`.

## Missing evidence
Archive enumeration and whether graphics setting changes during park lifecycle.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define load-time bank policy and supported setting mapping before implementation.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Lowest setting limits mechanic to one bank.
2. higher setting permits two.
3. entertainers not capped.
4. bank selection stays stable across save/load.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: GUEST-014.
- Dependency: Graphics settings ownership.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
