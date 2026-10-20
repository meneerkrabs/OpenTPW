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
  A synthetic busy script slot holds the arriving guest in state 13 at the
  stand point; the test then clears that slot and closes the runtime before a
  guest-only subturn. It checks that neither the park turn nor the last admission
  turn advances, with an open control that reaches state 14 and writes the offer.

These are host regression tests, not original-game execution, imported-save
queue reconstruction, or a native timing-equivalence result. They require the
C# test runner; source validation alone does not establish that they pass.

## Remaining closure scope

`GUEST-011` remains open: close-time pending-call/loading-slot withdrawal is
still separate from this guard. Closing still withdraws the pending guest at
the next queue-head dismissal. This change does not clear an already-written
offer, alter the close/withdraw protocol, or implement the entrance-gate wait
state. No approximation ID is retired.
