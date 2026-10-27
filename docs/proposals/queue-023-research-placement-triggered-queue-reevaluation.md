# Draft proposal: QUEUE-023 — Research placement-triggered queue reevaluation

## Current-source reconciliation (October 27, 2026)

This section supersedes the historical missing-hook premise and October 22
event-source matrix below. Public main
`60c941de233ac45a0d8c7d680aef2269f655b8b6` retires QUEUE-023 in
[`tools/fidelity_retired.txt`](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/tools/fidelity_retired.txt).
[Merged #858](https://github.com/meneerkrabs/OpenTPW/pull/858)
(`2b50a45b23b373d604bbf3603b1c97c6c9a0ccd7`) delivered the queue-tool work.

- [`Level.Objects.cs`](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Level.Objects.cs#L307-L354)
  implements owner reevaluation and `ReevaluatePlacedQueue`'s two calls.
  [Placement calls it after economy binding](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW/World/Level.Objects.cs#L1762-L1765).
  Do not add another placement callback or a speculative global reopen scan.
- The [landed queue-tool summary](https://github.com/meneerkrabs/OpenTPW/pull/858) (`docs/progress/2026-10-26-queue-tool.md`)
  records the caller-specific path clear, Back and demolition routes. A fresh
  placement's queue front links only toward its entrance and does not establish
  the earlier proposed connectivity case. Queue-cell clear is not path-cell
  clear; repeated notifications/stamps must not be blindly deduplicated.
- Existing [`ObjectAccessCellsTests`](https://github.com/meneerkrabs/OpenTPW/blob/60c941de233ac45a0d8c7d680aef2269f655b8b6/source/OpenTPW.Tests/ObjectAccessCellsTests.cs#L129-L209)
  cover two placement stamps, path-clear owner notification, no corresponding
  queue-clear callback, and guest-grid/track clear routes. These tests were
  source-inspected, not executed by this reconciliation.

The next action is review of this superseded proposal's disposition. Separate
ECON-049, ECON-050 and QUEUE-003/-010/-015 residuals are not closed by QUEUE-023's
retirement and must not be folded into a duplicate implementation here.
No original media or executable was inspected, and no C#, browser or native
test was run in this pass. No new test result, runtime change, retirement, merge
or closure is claimed; keep this PR draft pending review of its disposition.

## Historical proposal (superseded where noted above)

- Classification: **approximation**.
- Mode: Evidence-gated implementation design only.
- Baseline: provided main `5ccddcab` source snapshot; source inspected locally on 2026-10-18.
- Status: PLAN ONLY. No runtime implementation, identifier retirement or merge is claimed.

## Current residual
Queue/path edits trigger reopen checks, but placement/removal click hooks and model routine 0x4547C0 are missing.

## Source audit
- `source/OpenTPW/World/Level.Objects.cs:217` (APPROX).
- Current source `source/OpenTPW/World/Level.Objects.cs:218`: `private void ReevaluateQueuedRide( OriginalObjectRuntime runtime )`.

## Missing evidence
Trace 0x52537A/0x5259AE/0x529890/0x52FFEC and model routine effect; prove placement can connect.
- The available original Data corpus contains no executables or additional built-coaster saves.
- Repository reverse-engineering annotations are leads; they are not a newly executed original-game oracle.

## Bounded proposal
Define event-source matrix and idempotent reevaluation contract before adding hooks.
- Keep this identifier scoped to the residual above; link shared interfaces rather than duplicating another owner’s implementation.
- Record unsupported fixtures and unresolved questions explicitly instead of silently treating them as confirmed.

## Acceptance plan
- Existing regression homes to inspect/extend (not executed in this audit): `source/OpenTPW.Tests/QueueEditTests.cs`, `source/OpenTPW.Tests/QueueJoinTests.cs`, `source/OpenTPW.Tests/QueueExitWalkTests.cs`.
1. Placement that connects queue reopens eligible closed ride.
2. broken/condemned/called states remain closed.
3. removal triggers exact pass.
4. model notification does not duplicate queue edits.
5. Retain an evidence manifest with fixture origin/hash, baseline revision, observed trace and expected result; synthetic cases must be labeled synthetic.
6. Re-run the affected existing test suite and compare deterministic state/RNG traces where relevant; these are future checks, not tests run by this proposal.

## Ownership and dependencies
- Owner: proposed simulation/economy maintainer; assignment remains open.
- Dependency: ECON-049.
- Dependency: ECON-050.
- Dependency: ARRIVAL-003.
- GuestSimulation runtime changes must coordinate with the ongoing umbrellas follow-up before implementation.

## Completion boundary
- Deliver the evidence ledger, agreed contract and concrete regression cases described above.
- Keep this proposal draft while required original evidence or scope decisions are unavailable.
- Any later implementation, approximation retirement and merge require separate verified results; this document performs none.


## Public-source scope correction (2026-10-22)

This addendum supersedes the earlier **current residual** and source-line description where they conflict. The original proposal is retained above as historical context. Inspection is pinned to main `16e4213ad5e45ee0d31bb5b15646c9a3a3f39364`; this is source evidence, not a new native trace or a runtime/browser test.

### Already implemented, not a missing model routine

`Level.Objects.cs:241–272` subscribes a successfully registered visitor bridge's `QueueEdited` event to `ReevaluateQueuedRide`. On an eligible closed runtime it calls `runtime.Open()` and `SyncOpenState(runtime)`. `OriginalObjectRuntime.cs:249–256` calls `Hoarding.StartOpenFade()` before setting `VAR_RIDECLOSED` to zero, unless stopped. `ObjectHoardingState.cs:69–79` explicitly documents **0x004547C0**: when a hoarding is present its rate becomes the sink rate, standing flags change from rising to sinking, and its version increments. Without a hoarding it is a no-op. Actual animation proceeds through `Advance`, not through a second queue notification. `Level.cs:816–821` immediately copies the runtime's open state into an existing economy record.

Thus the remaining task is event-source coverage and ordering, not adding an absent hoarding call. This does not certify all native state transitions: the native annotation also mentions `SetState(0)` and clearing `+0x5E`; neither should be declared independently reproduced merely because `Open()` exists.

### Event-source → reevaluation → state/model matrix

In this matrix **N(grid)** means recompute the bridge's queue, then `NotifyQueueEdited(grid)`: stamp the queue-back block, invoke the event, run the level's guarded opening, and synchronize economy if opening is reached. **N()** invokes the event without the block stamp. Registration is required for the level event subscriber; a standalone test bridge does not implicitly acquire it.

| Source / inspected host route | Reevaluation route and order | State/model implication and remaining boundary |
| --- | --- | --- |
| Legacy/headless queue extension: `QueuePaths.TryExtend`, successful continuing write | N(grid) after queue/link mutation (`QueuePaths.cs:120–121`) | Eligible closed ride can open with existing hoarding fade. A successful free-standing write (`!continues`, `:111–114`) only recomputes and returns Ok without notification; refusals also do not reach the notifying tail. |
| Explicit queue-to-path connection: `QueuePaths.Connect`; successful `TryConnect` delegates to it | N(grid), after link/owner writes (`:160–188`) | Connection can trigger opening. A rejected connection does not notify. Mere path adjacency is not this operation. |
| Native queue segment commit / undo via `QueueLineWriter` and `QueueSegmentReducer` | Reducer's `changed` callback runs after passes and removed-cell callback (`QueueSegmentReducer.cs:120,165`); `Level.Objects.cs:797` supplies N(grid) | The `0x52FE50` undo family is not wholly absent: a supported host route already notifies. Unsupported/early-return paths and exact native click multiplicity remain separate evidence questions. |
| Queue disconnect and owned-queue removal: `QueuePaths.DetachFromPath`, `RemoveFrom` removal tail | N(grid) after unlink/clear (`QueuePaths.cs:299–300,412–413`) | Guard may refuse reopening; this routine does **not** close an already-open ride when connectivity is lost. Do not add closing as a QUEUE-023 assumption. |
| Path written over a queue cell, nonzero owner: `ParkPathBuilder.WritePath` | `QueueCellPaved(owner)` after type becomes path, before spending; `Level.Objects.cs:697–703` resolves first matching HasQueue bridge and runs N(grid) | Existing owner-specific event. It occurs before the enclosing path link pass completes; a new generic post-click hook could duplicate it or change ordering. Owner zero/no matching bridge has no level reevaluation through this event. |
| Queue removal whose cached owner is initially unknown: `Level.RemoveQueueCellAt` | Recompute every HasQueue bridge; those whose recompute returns true get N() before retrying owner lookup (`Level.Objects.cs:849–856`). Found-owner removal then uses `QueuePaths.RemoveFrom` | This recovery branch omits the block stamp for its first event and can precede a later stamped event. Preserve observed multiplicity until native evidence selects another contract. A stray unowned clear has no owning bridge event. |
| Ordinary path creation beside a queue, not replacing one | Link writer mutates path/entrance/exit links; type-3 neighbor refresh/sound callback is not `QueueEdited` (`PathLinkWriter.cs:91–128`, `Level.Objects.cs:685–703`) | No general reopen event in this inspected route. Crucially, mode 1 does not invent a new connection to an arbitrary type-3 queue neighbor; its neighbor callback requires an existing backlink. Do not propose adjacency-only reopening. |
| Object placement (including access-cell laying) | `ObjectPlaced` footprint → access `ObjectAccessCells.Lay` → coaster binding → guest registration/subscription (`Level.Objects.cs:158–195,241–246`); no general post-placement N(grid) in `PlaceObject` | New queued objects start closed; hoarding close setup and economy linking occur later in `PlaceObject` (`:1381–1448`). Adding a callback inside access laying would be too early for the new bridge and economy binding and could be followed by hoarding-close setup. |
| Object removal / replacement | Inspected handlers clear footprint/owned queue, unregister visitors; demolished queue may reach `ClearAll` (`Level.Objects.cs:179–195,1134–1158,1511–1524`) | `ClearAll` recomputes before clearing but does not notify afterward (`QueuePaths.cs:354–366`). Removed ride cleanup is not reevaluation of surviving neighboring rides. No global surviving-ride scan in these handlers. Do not reopen deleted objects. |
| Access-path removal / visitor-cell refresh | `AfterAccessCellRemoved` → `RefreshVisitorCells` recomputes affected bridges but does not notify (`Level.Objects.cs:970–1021`) | Updating fallback entrance/exit positions is distinct from queue reopening. Ordinary path removal/backtrack likewise has no general QueueEdited callback in the inspected path-builder route. |
| Import/restore/startup | Footprint/link restoration and registration are separate from `layingAccessCells`; placement-laying gate is false (`Level.Objects.cs:135–196`) | No new gameplay placement hook should run during load/join restoration. #779 specifically changes restore behavior, so preserve its owner boundary. |

### Guard and repeat-call contract

The host reevaluator first returns for `runtime.IsOpen` or when `Gate?.MayOpen(runtime) != true`. The gate derives broken/upgrading state from script/economy, checks maintenance and connectivity and coaster acceptance (`ParkGate.cs:247–255`). Its implementation here does not separately derive a condemned state: retaining the earlier condemned-state acceptance case requires a demonstrated state representation/fixture, not a claim that the prose alone proves it.

The reevaluator additionally rejects a HasQueue ride with no real queue front, a non-accepting coaster, or a Bump controller with a track that is invalid. This real-front check is deliberately stricter than the ECON-049 gate fallback (`ParkGate.cs:265–272`). These guards must be recorded separately rather than replaced with a blanket “connected” predicate.

Opening an already-open ride is suppressed by the level guard, but the full notification is **not idempotent**: `NotifyQueueEdited(grid)` stamps before invoking the guard, and the hoarding operation itself increments Version when present. A future contract must specify exactly when stamps/events occur and which source owns them. It must not blindly deduplicate all repeated events or call `Open` twice. Synthetic tests should assert event count, block-stamp deltas, closed variable, economy open flag and hoarding rate/flags/version independently.

### Meaningful connectivity investigations, not speculative hooks

1. A new object's non-queue entrance or queued ride's exit lays/relinks a path (`ObjectAccessCells.Lay`, lines 36–63). Determine whether this can satisfy the existing back-link predicate for a **surviving** closed ride. Capture complete before/after raw types, owners, cardinal/queue links and chosen affected ride. A neighboring queue without a backlink is a negative control, not a proven connect case.
2. A placement can clear a plain path in its footprint and write new entrance/exit geometry. Record which connections are destroyed versus created and when access links become final. The new object's QueueEdited subscription, hoarding initialization and economy record must exist before any proposed open result can be meaningful.
3. Removal/replacement can change neighboring access links, but removal does not inherently create a path or valid queue. Establish a concrete connectivity-changing case before adding a global reopen pass. Keep removed-owner cleanup separate from surviving-owner notification.
4. Audit native click sites `0x52537A`/`0x5259AE`, placement `0x528A70`, and the previously named `0x529890`/`0x52FFEC` against the already implemented supported reducer undo route. Record selected owner, call order/count, stamp and state/model writes. Repository address comments are leads, not new disassembly or native execution evidence.

### Ownership, acceptance and evidence limits

At this audit #755 (head `8da40644c6684587be0d91996f72388e39d195e1`) and #779 (head `f9bc6155bbd45ee01d5a8d715c17a66b34b62225`) are open drafts; both changed-file lists include `source/OpenTPW/World/Level.Objects.cs`. Coordinate before runtime edits and refresh these mutable statuses before future work. GuestSimulation changes also retain the existing umbrella-owner coordination requirement; this addendum does not assert that either PR modifies that file. ECON-049, ECON-050 and ARRIVAL-003 dependencies remain open in this proposal.

Next bounded work is an evidence-backed fixture and explicit owner/event-order agreement, then focused tests in the regression homes already listed above. Cover successful/rejected/no-op edits, repeated notifications, initially open/closed/stopped runtimes, no model, no real queue, broken/upgrading/maintenance and demonstrated condemned representation, invalid tracks, deletion, and startup/join exclusion. No speculative runtime hook, new implementation, identifier retirement, full native parity, commit execution, test execution, deployment or browser acceptance is claimed by this addendum. Keep #530 draft.

### Reproducible source ledger

All paths below are relative to `source/OpenTPW/` at the pinned main revision above. Git blob IDs were verified against the exact retrieved bytes. They identify source inspected, not test results.

- `World/Level.Objects.cs`: `9c20cdf7500eaf0b6c9920736a5d1661c5772392`.
- `World/Objects/OriginalObjectRuntime.cs`: `b2c03d2f2846ac681910125467ba8514bb9c6cb7`.
- `World/Guests/QueuePaths.cs`: `1e2be30aafa436e02d72f698aaab324ee8326423`.
- `World/Guests/RideVisitorBridge.cs`: `e6fb0f9ed2b205c9b069473e9b2cc97557e91117`.
- `World/Level.OriginalRideState.cs`: `5331ecf86d56174a4f0ff033ed2ca2010694f879`.
- `World/Level.Simulation.cs`: `94298c217e72905c4fd0894b7cec6cb1ce986711`.
- `World/Level.cs`: `126e7fdf888dd8e77719ff6c4419d6d7f2b0444c`.
- `World/Objects/ObjectAccessCells.cs`: `a76189dbea526db85c7c00635b8cfd43a439cadf`.
- `World/Objects/ObjectHoardingState.cs`: `6b12a1c148803d56ca386adbea30dd78d5503718`.
- `World/ParkGate.cs`: `9a699069d6135523c79c58de7bbec7c3180cb1a3`.
- `World/ParkPathBuilder.cs`: `9cdff23ba5b5cbed43af70ada1ca4b6bdc535a53`.
- `World/PathLinkWriter.cs`: `7975a61e9833984681f8de52773e7cd33df2680d`.
- `World/QueueLineWriter.cs`: `1147c48db86c1b3eaee691775a74a33cb1d86cf0`.
- `World/QueueSegmentReducer.cs`: `e90ae98bcd30013897977c062ca74ef9b44e10eb`.
