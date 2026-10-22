# Draft proposal: GUEST-015 — Adjudicate multientry entertainer selection

- Classification: **out-of-scope**.
- Mode: Research and scope adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Shipped banks have one performance; hypothetical multientry bank selects member id rather than second world draw.

## Source audit
- `source/OpenTPW/World/Staff/StaffSprites.cs:107` (APPROX).
- Current source `source/OpenTPW/World/Staff/StaffSprites.cs:108`: `public static StaffClip? SelectClip( StaffMember member, bool moving, IReadOnlyList<SpritePerformance>? performances = null, int performanceCount = 0 )`.

## Missing evidence
A real supported multientry bank or explicit extension scope; original draw consumption when n=1.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Research/adjudication-only support boundary for unshipped banks; do not claim missing shipped animation.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. One-entry shipped banks use entry 0.
2. synthetic n=2/4 demonstrates id-versus-draw divergence.
3. no fourth entry at n=4 per original modulo n-1.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: Nonshipped content scope.
- Dependency: DET-015.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
