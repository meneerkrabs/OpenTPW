# Closed-ride stand-point admission

`RideVisitorBridge.PresentForBoarding` now rechecks the owner's open word and
retained broken/condemned state before offering the called guest to the script.
Closing after the call but before the guest's presentation therefore cannot
write a new `VAR_LETMEON` offer in the interval before the next ride update.
This applies the closed-admission behavior already described by `GUEST-011`.

The guard uses the owner's open predicate, not the public choice predicate:
coaster/track acceptance is deliberately absent from stand-point admission.
It reads `IsBrokenDown`, not the script's unobserved `VAR_BROKEN` flag. Existing
offer consumption remains in `HostStep`, before the closed ride drains its
next queue head.

## Synthetic host coverage

`ClosedRideAdmissionTests` adds an asset-free runtime with a generated model
and idle script. It covers:

- Actual `OriginalObjectRuntime.Close()` followed by repeated presentation,
  with no ride update in between and no bridge/script mutation on refusal.
- Retained states 1 and 4 with `VAR_BROKEN` clear, including a separately
  supplied open predicate so the retained-state guard is exercised independently.
- Normal open admission, a false coaster/track choice predicate, and an
  unobserved script breakdown flag.
- An already-written offer, its existing next-update dismissal, and a consumed
  offer that still boards before the next head is dismissed after closing.
- A real `GuestSimulation` queue walk using the actual runtime's bridge.
  The runtime closes while the called guest walks to the stand point; only
  guest subturns run until it arrives. It checks that the last admission turn
  does not advance and that the refusal leaves the bridge untouched, with an
  open control that reaches state 14 and writes the offer.

## Refused presentation

tp.exe's state 13 (`0x005006B0`) leaves on every outcome of its arrival: the
price test `0x004FDE50` runs once, then AdmitPerson (`0x004E0900`). A refusal
(`0x005007E7`) calls `0x00501160`, which routes the guest back to its queue
place (state 12); when that fails ("Couldn't rejoin FOQ even!") the guest is
unlinked, dismissed with no thought and set to state 6 (`0x00500824..0x00500865`).
`GuestSimulation.RejoinQueuePlace` ports that tail with the existing
`MoveToQueuePosition` route and `DismissFromQueue`, so a refused guest no longer
retries the presentation (and the price test) every tick until the ride's next
head dismissal.

AdmitPerson's closed test (state 1, open word 0, state 4) returns before it
touches the ride. Past it, AdmitPerson clears the person being loaded (`+0x6C`)
before reading `VAR_LETMEON`, so a busy `VAR_LETMEON` (`0x004E09EC`) leaves the
ride with no pending visitor and the guest is called again once it stands at
position 0. `PresentForBoarding` ports that clear for the busy-slot refusal.

`ClosedRideAdmissionTests` covers the closed refusal's walk back to state 12 and
state 11 and the ride's later head dismissal, a refused guest whose route back
fails (state 6), the busy-slot refusal and its later call, a closed priced shop
whose price test records once for the arrival, and the runtime opener leaving the
retained broken state as `SetState(0)` does.

These are host regression tests, not original-game execution, imported-save
queue reconstruction, or a native timing-equivalence result. They require the
C# test runner; source validation alone does not establish that they pass.

## Remaining closure scope

`GUEST-011` remains open: close-time pending-call/loading-slot withdrawal is
still separate from this guard. Closing still withdraws the pending guest at
the next queue-head dismissal. This change does not clear an already-written
offer, alter the close/withdraw protocol, or implement the entrance-gate wait
state. No approximation ID is retired.

Until that withdrawal is ported, a guest refused at a closed ride walks back
to its place still named as the ride's pending visitor (tp.exe's close
`0x004DF300` would already have cleared `+0x6C`). The ride's next update
dismisses it as the closed queue's head; a ride reopened before that update
keeps the stale call, so its admission calls nobody until that guest leaves
the queue through one of its own state-11 exits.
