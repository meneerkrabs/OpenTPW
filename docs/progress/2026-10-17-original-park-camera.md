## The park camera is the original's (October 17, 2026)

The park view now uses tp.exe's camera instead of OpenTPW's own tuned pose (UI-101 retired, UI-094 narrowed):

- **Pose** (`0x42b1c0`): one zoom value from 70 to 130 (110 at Home) sets the pitch (`(1 - t) x 45 + t x 65` degrees, `t = (zoom - 70) / 60`)
  and the eye (zoom x cos/sin pitch behind and above the target, plus the ground height under the target smoothed by half each frame).
  The view looks along the pitch, not at the target. Yaw 0 looks along +Y; Home returns to the entrance centre (475, 175, cells 47/48, 17).
  Wheel steps are 10, the zoom keys 0.05 a millisecond, the rotate keys 0.002 radians a millisecond, and the pan speed the original's
  settled 0.02 x sqrt(zoom) a millisecond. The camera panel's view button cycles 70, 110 and 130.
- **Projection** (`0x578be0`, `0x56b790`): one projection for the park, ride and walk views. The visible half height at unit depth is
  0.5 x tan(45 degrees) = 0.5 (the pick frustum; the matrix of `0x578BE0` has tangent 1, but `0x5781D0` multiplies the view-projection by a screen matrix of 2 x the aspect word and -2, which halves the visible range) and the width follows the window (0.75 aspect at 4:3). A
  native capture of Lost Kingdom at the Home pose matches the PC video's first park frame (gate, booths and path edges within a few pixels
  at 1024 x 768); wider windows keep the height and show more width.
- **Still open under UI-107:** the fog range (the video shows no fog at the top of the Home view, which a 135-150 range would fog), the
  `0xC000` gate of `0x567AD0` and the moons. UI-930 keeps its circle test; the aspect word is height over width.
- Not ported: the pan ramp, the yaw easing, the velocity lean, and the camera block of saves.
