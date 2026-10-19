## Burger Shop customers stay in view (October 19, 2026)

A user report: guests disappear in the Jungle Burger Shop and never come out. The simulation loses nobody: with its
original script the shop takes a guest, holds it for one second and lets it off, in all four rotations, on laid access
cells, in the M3 gate's whole park (hundreds of uses in 20 minutes) and in a native jungle run
(`ShopVisitAssetTests`). What differed was the drawing. tp.exe keeps a shop customer's body when the shop's
`UsageInfo.RideHandlesSprite` is 1 (state 16 at `0x50212B`) and does not move it (`0x4FA087`), so the guest stands at
the counter while it is served; OpenTPW hid every served guest, so customers vanished into the hut and popped up
again on the path among the waiting guests. Customers of such shops are now drawn standing at the stand point
(`GuestSimulation.StandsAtCounter`); shops that delete the body, rides and pools changing rooms still hide them. The
remaining difference, that tp.exe walks the guest out of its exit cell from `UsageInfo.ExitCellAppearPos` where
OpenTPW sets it down beyond the exit, is registered as `GUEST-023`. The M3 gate gained `ShopFilter` and
`ShopRotation`. Presentation only: no hash or determinism pin changed.

Review changes: the sideshows and toilets that set `RideHandlesSprite` 1 are registered as `GUEST-024` (tp.exe keeps their guests in view too; OpenTPW still hides them until their scripts are decoded), and a customer at a shop counter keeps its umbrella drawn.
