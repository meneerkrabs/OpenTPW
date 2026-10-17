## Riders show on Aztec Mayhem and four other rides (October 17, 2026)

Guests who boarded Aztec Mayhem (Azteekse Tekens in Dutch) disappeared: the ride's seats stayed empty. The ride, like The Firepit, Inca Totem, Hover Bot Havoc and
Rock 'n Roll, runs no `ADDHEAD`; its script seats a rider with `WALKON` and the head action 4, which hangs the rider's head on head id u2 (the rider counter) when the
walk-on ends, and `WALKOFF` takes it down (tp.exe `0x00557D80`, `0x005571A0`). OpenTPW only drew heads that `ADDHEAD` had placed, and a boarded rider is not drawn as a body, so
these five rides showed nothing.

- `RideVisitorBridge` now hangs the head for the head action (`RideHeadSpace.Hang`) and takes it down at `WALKOFF`; the walk slot is kept (and saved) for it.
- The head hangs at once, without the walk-on time and the walking body before it (the existing TOUR-011 approximation).
- Pinned by `WalkOnHeadActionTests` and, with the original data, `RideHeadsCorpusTests` (the five rides run with real riders).
- Reviewed against tp.exe again: only the head action 4 reaches the head hang (the walk update `0x00557D80` state 1 and `0x005571A0`); the shipped scripts use the actions 1, 2, 4, 5 and 6 and 5 and 6 draw no head. The walk-on time is read by the same update and gates only the walking body and the head, so TOUR-011 now says exactly that.
- A save in the middle of the ride keeps the heads and the walk slots, and a save without walk slots loads (`HeadsAndWalkSlotsSurviveASaveInTheMiddleOfTheRide`).
