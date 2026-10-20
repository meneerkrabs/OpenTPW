# Queue removal: preserving the overflow thought

This is a bounded QUEUE-015 correction, not a completion of queue removal fidelity.

The existing queue-edit re-evaluation gives an Angry thought to guests whose id is divisible by three, whose list position is beyond the shortened queue's room, and whose state is 11, 12 or 13. The called guest and state 14 are excluded. That decision is established by the existing public [X14 oracle account](reverse/ORACLE.md), with stubbed list-position and dismissal calls; it is not evidence for a complete original queue undo.

The Level queue tool's ordinary undo already relocates guests standing on emptied queue cells to the nearest path and dismisses them immediately. That removes their membership before the later re-evaluation can supply its thought. Level now passes the resolved edited ride to that set-down operation. It snapshots eligible ids before any guest is moved or unlinked, then supplies Angry through the existing dismissal. An earlier in-room guest forced off a removed cell cannot shift a later guest out of eligibility.

This carries the thought through OpenTPW's existing relocation policy. It does not reorder re-evaluation, change position selection, add a dismissal or refund, or change called/state-14 forced set-down behavior. None still means no new thought, preserving an existing thought. Only the exact registered edited ride supplies eligibility; another ride, a stray cell without an owner, and the public set-down entry without edit context keep their prior behavior.

## Regression coverage authored

`QueueRemovalCommandTests` uses an asset-free real Level with actual economy-bound objects. Its action is the queue tool's `Undo()`, recorded as an `UndoLineCommand` and executed through the production writer and removal callback.

- A naturally joined and settled queue exercises the missing immediate thought.
- Explicitly synthetic membership cases isolate all eligible states, called/state-14 exclusions, retained capacity, nondivisible ids, and an earlier forced removal that shifts the target's live position from four to three.
- Other-ride, stray-cell, missing-membership and legacy public-entry controls preserve their previous thought behavior.
- Assertions cover the existing relocation, once-only events and happiness step, queue/admission counters, refund and cell metadata, RNG state, command refusal, and twin replay through subsequent simulation ticks.

The queue geometry is synthetic on the real fixture objects. These are not captures of native occupant movement. No original media is needed. The fixture was source-reviewed but has not been compiled or executed in the current environment; an authorized exact-head C# run remains required.

QUEUE-015 remains open for the original cleared-cell occupant policy, outside-cell demolition accounting and remaining corner/count lifecycle evidence. The separate QUEUE-014 conversion failure-state and persistence gate is unaffected.
