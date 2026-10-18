# Draft proposal: GUEST-004 — Preserve full-queue refusal target semantics

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Full-room refusal clears AttractionId, losing original stale-target removal happiness consequence.

## Source audit
- `source/OpenTPW/World/Guests/GuestSimulation.cs:306` (APPROX).
- `source/OpenTPW/World/Guests/GuestSimulation.cs:2372` (APPROX).
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:307`: `public void Unregister( IRideVisitorBridge attraction )`.
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:2373`: `if ( result2 is QueueJoinResult.QueueTooLong or QueueJoinResult.NotExcitingEnough or QueueJoinResult.TooExciting )`.

## Missing evidence
Trace Unregister and original 0x4FB360 target handling between refusal and next choice.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify target lifecycle separately from queue membership; distinguish full-room and excitement refusals.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTests.cs`, `source/OpenTPW.Tests/GuestThoughtTests.cs`.
1. Full-room refusal followed by removal applies small loss.
2. subsequent decision clears target before score.
3. excitement refusal still resets stamp/history as original.
4. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
5. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: QUEUE-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
