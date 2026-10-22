# Draft proposal: GUEST-016 — Specify staff sprite movement timing

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Sprite interval uses observed displacement scaled to turn and moving timeout/smoothing rather than original own step/subturn point.

## Source audit
- `source/OpenTPW/World/Staff/StaffSprites.cs:44` (APPROX).
- Current source `source/OpenTPW/World/Staff/StaffSprites.cs:45`: `public const float FramesPerSecond = 1000f / SpritePlayer.DefaultInterval;`.

## Missing evidence
Trace 0x4F9F00 render interpolation and person's step length fed to animation.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan explicit simulation displacement input and presentation interpolation contract.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Stationary member stops walking clip at evidenced boundary.
2. turn movement yields clamp(trunc(62/v),0,250).
3. identical sim at different render rates has correct frames.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: DET-013.
- Dependency: EXT:fixed-60hz-clock.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
