## Lobby camera, locator panels and ride view (October 29, 2026)

- **The lobby and the online globe now use the original's screen matrix.** tp.exe's
  camera method (`0x5d8cb0`) builds the lobby's projection with the park's builder
  (`0x578be0`, near 0.1, far 1000, the angle ISLANDFOV in `[this+4]`), and the matrix
  `0x5781d0` fills once scales every projection, so the visible half height at unit
  depth is 0.5 x tan(angle / 2), not tan(angle / 2). With ISLANDFOV 100 the lobby
  showed twice the vertical extent it should; the globe (60 degrees, set at `0x5dd034`
  in the same camera object) did too. Both cameras use `OriginalProjection` now, the
  lobby with the span selection's farthest corner distance as far plane
  (`0x56bb00` -> `0x578ca0`, as the park) and the globe with 1000. The lobby's rain
  and bolts, whose clip scale already assumed this projection, now sit where the
  scene draws them.
- **UI-091: the locator panels slide.** The staff and visitors panels of Ctrl+S and
  Ctrl+V go through `0x4a2590` into the HUD arm's drawer (`0x4a14f0`): ten 20 ms ticks
  from a panel width and 50 left of the arm's left edge (the staff panel ends at 484,
  the visitors' at 485, both at top 1127, not at their table places), the arm's art
  growing out under the HUD, the retract button shown with the slide out, and a panel
  of the other kind waiting in the arm's slot 2 until the first is away. The radio
  follow has no smoothing of its own: the `0x42b1c0` matrix blend runs only under
  the mode bits `0x3c` of `0x790ab0`, and the camera setter writes only the ride bit.
- **UI-094: the ride view's blend and crowd count follow the code.** The frame time
  of the blend is whole milliseconds plus one, 100 above 200 (`0x42b2a0`, `0x42b2c7`),
  so a stall no longer counts as the full second. The view direction is the camera
  matrix's third row (`0x56b940`), and no writer ever sets the mode bits 8, 0x10 and
  0x20. The crowd sentence's guest count (`0x54f899`) takes the hover cell `0x7b05cc`,
  the cell of the cursor ray the park overview refreshes every frame and the eye views
  keep, not the camera's target.
- **UI-095 narrowed:** the stale claim that the yaw rule fires for no ride is gone
  (the placement writes the exit cell's byte at `0x5292b2`); the remaining residuals
  are divergent saved exit positions and the host camera's own pose.
