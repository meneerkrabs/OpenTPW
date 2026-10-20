## Ride-exit cell and direction, UI-095 (October 20, 2026)

The previous camera consumer confused three distinct cells and two distinct bytes.
The yaw switch was already correct; duplicating it would not fix the caller.

### Independently checked native contract

Static reads of TPW's `tp.exe`, without executing it:

- `0x42A340` looks up the cell under the saved park target. Its call at
  `0x42A36D` to `0x536340` requires type 9 (entrance), not any occupied cell.
- It follows that cell's owner word `+0x10` to the owner cell's thing-list head
  `+0x24`, then selects a type-3 thing with `UsageInfo.CannotRide == 0`.
- `0x42A22E` reads that ride's word `+0x38`. This is `mExitPos`, not the footprint
  anchor: serializer `0x4DCA9A` passes `ride + 0x38` with the string at
  `0x75B558`, literally `mExitPos`. `SaveAttraction` already records this layout.
- `0x42A295` calls `0x522850`, which reads cell `+0x0D` (direction/queue link).
  The neighbour getter `0x522770` instead reads `+0x0C`. Masking with `0x55`
  selects exactly one cardinal bit; zero or several cardinal bits retain yaw.
- Placement's switch `0x529164` (tables `0x529D24` and `0x529D04`) sends shape
  type 10 to `0x529239`. The exit's symbol direction is rotated at `0x5292A7`
  and written at `0x5292B2` through `0x5227E0`, which writes `+0x0D`. This is an
  exit producer, not an inference from an entrance neighbour link.

### Bounded host correction

The live entrance's owner identifies the live ride; its last shape exit is the
same exit selected by the placement access-point enumeration. Recenter uses that
exit and `QueueLinkAt`, never the anchor or `LinksAt`. Missing entrances, owners
or exits do not acquire a fabricated fallback. Existing prior-camera restoration
and rideability/deletion checks remain in place.

`WriteFootprint` writes the rotated exit direction for placement and original
import reconstruction. Own park snapshot capture/restore preserves QueueLink
separately from Links. Object removal clears its footprint and the direction.
Synthetic actual-placement fixtures exercise all four rotations through the real
camera consumer, separate entrance/anchor/exit positions, deliberately conflicting
neighbour bits, zero/ambiguous direction, owner absence, import reconstruction,
snapshot restoration and removal. Existing tests cover CannotRide and prior walk
camera identity.

### Remaining boundaries

This does not reconstruct arbitrary native overlapping thing-list order, nor
import a saved `mExitPos` that diverges from the reconstructed shape's exit.
Original import still reconstructs the direction from the shape rather than
reading every saved direction byte. The host park camera's pose/smoothing is not
the original camera implementation. These are explicit residuals, not claims of
full UI-095 parity. No new owner-anchor link writer is needed for this correction.
