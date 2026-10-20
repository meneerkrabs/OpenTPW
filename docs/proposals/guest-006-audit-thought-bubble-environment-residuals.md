# Draft proposal: GUEST-006 — Audit thought bubble environment residuals

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Toilet voice already invokes event 0x85; bubble camera suppression, fireworks row lifecycle and load reconstruction remain unresolved.
- Stale exclusion: GuestSimulation.cs:910–911 already invokes Voiced with ToiletThoughtVoiceEvent; retain existing voice behavior.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:888` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:889`: `private void UpdateNeedsTurn( Guest guest )`.

## Missing evidence
Writers of camera word bits 0x16; environment save/rebuild; repeated VAR_STATUS=1 lifecycle.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan remaining bubble suppression and environment transitions; explicitly exclude voice reimplementation.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestVoiceTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Voice low-nibble gate still invokes exactly once.
2. each evidenced camera mode suppresses bubble only.
3. repeated show status removes correct row.
4. load does not double-add environment.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: Camera mode ownership.
- Dependency: SAVE-010.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
