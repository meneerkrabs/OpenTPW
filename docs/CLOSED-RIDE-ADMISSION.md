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

## Close-time withdrawal

tp.exe's close `0x004DF300` ("Object %d: Closing...") writes the open word
`+0x68` = 0, zeroes the loading slot `+0x6C` ("person being loaded was %d
(about to zeroise)"), writes `VAR_RIDECLOSED` = 1 and calls the model routine
`0x00454550(1)`; it touches no guest, call flag or queue word. Its callers are
the park door `0x00519EF0`, the coaster tool's entry `0x00447520` and the ride
window's close `0x0048CCF0`; `SetState` 1, 2 and 4 and the repair tail's
re-close clear `+0x6C` the same way. `OriginalObjectRuntime.Close` now clears
the bridge's pending visitor at once (`RideVisitorBridge.ClearLoadingSlot`),
unless the guest has already written its offer (AdmitPerson clears `+0x6C`
when it writes `VAR_LETMEON`, and the close leaves `VAR_LETMEON` alone).

What follows is the original's:

- A called guest still standing at place 0 with its call flag set, whose ride
  no longer names it, ends its state-11 update there ("Person been admitted to
  ride %d but ride %d not ready to admit!", `0x005001A4`) until the ride calls
  it again or the closed ride's update dismisses it.
- A guest on its way to the stand point is refused there while the ride is
  closed and walks back to its place; once the ride has opened again, the
  next ride update finds nobody pending and calls its standing head again.
- A guest that arrives after the ride has reopened is admitted at once:
  AdmitPerson only logs a loading slot that does not name the presenting guest
  ("admitting wrong person", `0x004E0976`), then clears it and writes
  `VAR_LETMEON`. OpenTPW calls only a standing queue head, so the presenting
  guest is always the pending visitor or, after a close or a busy refusal,
  nobody.

`ClosedRideAdmissionTests` covers the three cases, closing and reopening
before the ride's next update. `GUEST-011` is retired: its remaining text (the
call withdrawn only at the next update, and a booth wait without a state of its
own, which state 3 `0x004FF7F0` now is) no longer describes OpenTPW.
