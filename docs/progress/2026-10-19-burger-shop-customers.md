## Burger Shop customers stay in view (October 19, 2026)

A user report: guests disappear in the Jungle Burger Shop and never come out. The simulation loses nobody: with its
original script the shop takes a guest, holds it for one second and lets it off, in all four rotations, on laid access
cells, in the M3 gate's whole park (hundreds of uses in 20 minutes) and in a native jungle run
(`ShopVisitAssetTests`). What differed was the drawing. tp.exe keeps a shop customer's body when the shop's
`UsageInfo.RideHandlesSprite` is 1 (state 16 at `0x50212B`) and does not move it (`0x4FA087`), so the guest stands at
the counter while it is served; OpenTPW hid every served guest, so customers vanished into the hut and popped up
again on the path among the waiting guests. Customers of such shops are now drawn standing at the stand point
(`GuestSimulation.ShowsKeptBody`); shops that delete the body and pools changing rooms still hide them. The M3 gate
gained `ShopFilter` and `ShopRotation`.

Review changes: tp.exe's state-16 entry keeps the body for every object with `RideHandlesSprite` 1, not only shops,
so the sideshows and toilets that set it now keep their guests in view as well. Their scripts were decoded: they move
the kept body only through the walk table (`WALKON`/`WALKOFF`/`WALKGET`, already drawn), and the Small Toilets, Giant
Puzzles, Strength Flower and Ufo Blaster leave it standing at the stand point. A kept body whose walk or bounce entry has
ended stays where it was last placed (`FlatRideBounceRiders.HoldKeptBodies`, the sprite pass `0x542010`), and a guest
standing in place keeps its umbrella drawn. `KeptBodyAssetTests` runs the 17 shipped sideshows and toilets that keep
the body. Presentation only: no pin moved.

The exit walk (October 20, the user's decision to port it here and re-pin). `GUEST-023` is retired, so the PR adds no
approximation. A let-off is now read once per park turn, by the ride update's dismiss (`0x4E1410`). ExitRide
(`0x5014E0`) then puts the guest at `UsageInfo.ExitCellAppearPos` in the exit cell, and state 15 (`0x500900`) walks it
to the cell beyond the exit. A queue cell there, or no route, is a blocked exit: it closes the ride (`0x4DF150`) and
keeps the guest on it until a later ride update finds the way free. `ExitRideTests` covers the exit point, the
reversal, the blocked exit and the cadence. Every pinned run with guests moved; the old and new values and the reasons
are in docs/DETERMINISM.md, "The exit walk".
