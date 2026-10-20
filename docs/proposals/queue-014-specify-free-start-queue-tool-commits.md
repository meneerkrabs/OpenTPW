# Draft proposal: QUEUE-014 — Specify free-start queue tool commits

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Tool starts from ride front/back and avoids path overwrite rather than arbitrary valid clicked start/free-standing owned segments.

## Source audit
- `source/OpenTPW/World/Guests/QueuePaths.cs:34` (APPROX).
- Current source `source/OpenTPW/World/Guests/QueuePaths.cs:35`: `public static QueueBuildResult CheckExtend( GuestPathGrid grid, RideVisitorBridge ride, int x, int y, Func<int, int, bool>? isBlocked = null, Func<int, int, bool>? givesWay = null ) =>`.

## Missing evidence
Remaining 0x532FC0 commit passes and 0x5367A0 owner-based refund.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Plan queue-tool gesture state and cell transaction semantics; keep preview side-effect free.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. Valid isolated click starts segment with owner but no link.
2. path overwrite obeys NoModify and final-cell rule.
3. other ride queue rejected.
4. dominant-axis segment commits exact links.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: QUEUE-003.
- Dependency: QUEUE-012.
- Dependency: QUEUE-015.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.

## October 20 bounded implementation follow-up

The historical plan above is retained for scope and ancestry. Its plan-only status
is superseded for the secondary-empty queue-segment domain by the
[ordered-pass contract](../reverse/QUEUE-014-segment-contract.md), with explicit
command/replay semantics and strict own-save/TPI residue persistence. The code
uses per-cell debit and preserves failure residue, not transaction rollback.
Independent source review passed; executable tests and current-head CI remain
separate gates. No full QUEUE-014 retirement is claimed. Free-start UI, secondary
layers, original-save divergent entry positions, counter wrap and full guest
presentation timing remain unsupported as detailed in that contract.
