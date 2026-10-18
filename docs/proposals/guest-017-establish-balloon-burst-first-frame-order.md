# Draft proposal: GUEST-017 — Establish balloon burst first-frame order

- Classification: **evidence-only**.
- Mode: Evidence and compatibility adjudication only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Burst frame is drawn immediately; original script appears to start on next tick but same-frame scheduling is unverified.

## Source audit
- `source/OpenTPW/World/Guests/BalloonRenderer.cs:140` (APPROX).
- Current source `source/OpenTPW/World/Guests/BalloonRenderer.cs:141`: `var view = Camera.ViewMatrix;`.

## Missing evidence
Capture script creation relative to 0x475360 sprite pass and render frame.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Write timing trace and proposed activation boundary; leave existing bob order and fade formula intact.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Burst before versus after sprite pass gives captured first frame.
2. 13 steps at 62ms fade 250 by 20.
3. multiple balloons retain thing-list bob phases.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: DET-013.
- Dependency: SAVE-013.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
