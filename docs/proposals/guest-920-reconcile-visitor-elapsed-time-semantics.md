# Draft proposal: GUEST-920 — Reconcile visitor elapsed-time semantics

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Runtime elapsed time accumulates seconds rather than integer turn subtraction and roster membership is untraced; imported mArrivalDate is already used.
- Stale exclusion: GuestSimulation.OriginalImport.cs imports valid mArrivalDate into TimeInParkSeconds; no new timestamp decoder is needed.

## Source audit
- `source/OpenTPW/Hud/OverviewRosters.cs:221` (APPROX).
- `source/OpenTPW/World/Guests/GuestSimulation.cs:850` (APPROX).
- Current source `source/OpenTPW/Hud/OverviewRosters.cs:222`: `private static long? Hours( Guest guest, bool refreshed ) => guest.TimeInParkSeconds is { } elapsed`.
- Current source `source/OpenTPW/World/Guests/GuestSimulation.cs:851`: `if ( CountsParkTime( guest ) && guest.TimeInParkSeconds is { } elapsed )`.

## Missing evidence
Find message 0x1c/0x1b senders and compare turn-boundary rounding with render tick accumulation.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Specify integer-turn display and roster population contract; exclude implemented arrival-date restore.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/GuestTimeInParkTests.cs`, `source/OpenTPW.Tests/GuestTimeInParkReviewTests.cs`.
1. Imported valid stamp shows exact saved duration.
2. future stamp handled explicitly.
3. 24/48-hour boundaries match integer formula.
4. arrival/departure roster rows follow traced messages.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: SAVE-002.
- Dependency: HUD rosters.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.
