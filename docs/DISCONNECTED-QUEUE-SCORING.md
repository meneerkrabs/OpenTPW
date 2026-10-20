# Disconnected typed queue scoring

The bounded `GUEST-002` change separates the faithful attraction score from the
major decision's route request. After the sole connecting path is removed, a
queue built with `QueuePaths.TryExtend` and `TryConnect` still has a real back
cell of raw type 3. It supplies the same distance, queue and ride facts as before;
`JoinCell` is null and the public `AttractionView.HasJoinCell` stays false.
A newly built typed queue that has never been connected qualifies by the same
predicate; a separate producer test covers that case.

`ScoreAttraction`, `ViewOf` and the major candidate loop share one predicate.
Existing connected fallbacks remain eligible. A disconnected candidate requires
all of the following: the policy opts in, `QueueCells` is nonempty, and the actual
last cell is raw `ParkCellType.Queue`. No coordinate is invented for missing,
unknown, entrance/exit or other untyped fallback backs.

The score and comparison still precede counts and routing. A newer reachable
candidate can win, then an older higher-scoring disconnected candidate can make
a failed route request. The chosen attraction remains the earlier reachable
winner, while navigation retains the later failed destination and stuck state.
`PeekChoice` uses the observational route search and does not make that mutation.

## Policy and script contract

`IGuestDecisions.ScoresDisconnectedQueues` is additive and defaults to false.
`OriginalGuestDecisions` opts in; `ScriptGuestDecisions` forwards its inner
policy's choice. `SmarterGuestDecisions` and existing custom implementers keep
the host's zero score, skip and identity-only view for disconnected candidates.
This preserves their existing eligibility and does not change the smarter
policy's directly callable fallback to the original formula when a supplied
view has `HasJoinCell=false`.

A real script hook over the faithful policy receives actual scoring facts,
`hasJoinCell=false` and the faithful score as `prev`. A script over the smarter
policy is not called for this candidate. No script key is renamed or repurposed.
Wrappers that want their base policy's new eligibility must forward the capability.

## Authored regression coverage

- `GuestTests.TheScoreMeasuresTheGuestAgainstTheBackOfTheQueueNotTheJoinCell`
  retains its exact three-cell, six-queued, back `(2,3)`, guest `(0,1)` oracle.
  Removing path `(3,3)` preserves all view facts except the truthful join flag,
  and the expected score remains 19. Its queued IDs are formula-fixture entries;
  it is not a valid whole-guest save fixture.
- `DisconnectedQueueScoringTests` exercises the real queue producer, full view
  preservation and history exclusion, two-candidate ordering, the failed route,
  repeated observational peeks, the actual state-6 target assignment, missing and
  untyped fallback controls, and the faithful/smarter/script/custom-policy matrix.
  The state-6 fixture deliberately stages an eligible turn boundary, a known
  guest position, an old decision stamp and a generator state selecting the
  major branch, outside the needs phase. It asserts score ordering immediately
  before the actual update; it does not claim a naturally wandering trajectory.
- The script test constructs a real temporary mod and runtime. It checks view
  fields and `prev`, hook call counts, and absence of hook failures.
- The JSON test uses `ParkResumeJson` for the guest snapshot and both ride VMs,
  validates and restores them into a fresh fixture, then compares guest/bridge
  bytes, VM bytes, both guest-system hashes and the world generator through 180
  ticks. The identical typed map is rebuilt by the fixture; this does not test
  layout serialization, imported original saves or extension-state restoration.

These C# regressions were authored but not run in the local authoring environment.
Python fidelity/progress checks and diff validation cannot establish compilation,
runtime results, native behavior, full-suite hashes or original-game equivalence.

## Remaining scope

`GUEST-002` remains open. The minor-decision target/candidate filters, smarter
planning/alternative/booth filters, unknown fallback enrichment, original-save
back reconstruction and the broader `QUEUE-003` geometry/placement work are
unchanged. No approximation is retired. No movement, queue admission or
persistence schema is changed by this bounded scoring fix.
