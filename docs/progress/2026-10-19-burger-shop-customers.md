## Burger Shop customers stay in view (October 19, 2026)

A user report: guests disappear in the Jungle Burger Shop and never come out. The simulation loses nobody: with its
original script the shop takes a guest, holds it for one second and lets it off, in all four rotations, on laid access
cells, in the M3 gate's whole park (hundreds of uses in 20 minutes) and in a native jungle run
(`ShopVisitAssetTests`). What differed was the drawing. tp.exe keeps a shop customer's body when the shop's
`UsageInfo.RideHandlesSprite` is 1 (state 16 at `0x50212B`) and does not move it (`0x4FA087`), so the guest stands at
the counter while it is served; OpenTPW hid every served guest, so customers vanished into the hut and popped up
again on the path among the waiting guests. Customers of such shops are now drawn standing at the stand point
(`GuestSimulation.ShowsKeptBody`); shops that delete the body and pools changing rooms still hide them. The
remaining difference, that tp.exe walks the guest out of its exit cell from `UsageInfo.ExitCellAppearPos` where
OpenTPW sets it down beyond the exit, is registered as `GUEST-023`. The M3 gate gained `ShopFilter` and
`ShopRotation`. Presentation only: no hash or determinism pin changed.

Review changes: tp.exe's state-16 entry keeps the body for every object with `RideHandlesSprite` 1, not only shops,
so the sideshows and toilets that set it now keep their guests in view as well. Their scripts were decoded: they move
the kept body only through the walk table (`WALKON`/`WALKOFF`/`WALKGET`, already drawn), and the Small Toilets, Giant
Puzzles, Strength Flower and Ufo Blaster leave it standing at the stand point. A kept body whose walk or bounce entry has
ended stays where it was last placed (`FlatRideBounceRiders.HoldKeptBodies`, the sprite pass `0x542010`), and a guest
standing in place keeps its umbrella drawn. `KeptBodyAssetTests` runs the 17 shipped sideshows and toilets that keep
the body. Presentation only: no pin moved.
