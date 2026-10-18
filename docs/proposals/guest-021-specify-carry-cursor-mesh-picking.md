# Draft proposal: GUEST-021 — Specify carry cursor mesh picking

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Hire/carry cursor uses terrain hit only, so carried sprite can sit behind building; visual anchor differs from footage.

## Source audit
- `source/OpenTPW/Hud/ParkHud.cs:938` (APPROX).
- Current source `source/OpenTPW/Hud/ParkHud.cs:939`: `if ( input.Mouse.X >= 0 && input.Mouse.Y >= 0 )`.

## Missing evidence
Original 0x45D090 mesh picking plus cursor/sprite screen anchors and 81:30 video alignment.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan nearest terrain-or-model ray hit and independently verified screen anchor offset.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Foreground ride mesh wins over terrain.
2. hidden farther mesh loses.
3. no-hit clears cursor.
4. carried sprite aligns with glove at two zooms and rotations.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: Model picking.
- Dependency: HUD cursor owner.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
