## The website and README screenshots show the current game (October 18, 2026)

Every in-game image of opentpw.io and the README was taken before the Enhanced HUD was removed, the planar WCT decoder fix, the TPC sprite banks and the sprite depth pull, so they showed redrawn buttons and sprites that looked flat. All of them were re-captured with the current build: native, silent and offscreen, from `~/Downloads/theme park 2`, with the original world textures and the original interface (no texture pack, no redrawn art).

- `shot-queue.webp`, `shot-advisor.webp`, `shot-current.png`, `og.jpg`, `clip-poster.webp` and the two clip files: the jungle park with `--capture-interface --capture-camera`, guests arriving through the gate (entrance fee set to zero for the capture so the park fills), staff from `--debug-staff` for the advisor shot.
- `world-jungle.webp`, `world-fantasy.webp`, `world-space.webp`, `world-halloween.webp`: world-only captures of each gate (`--load-original-level <level> --capture-camera -1,-48,110,-135 --capture-frames 60`; the Halloween level folder is `hallow`).
- `shot-coaster.webp`: `--debug-coaster built`; the coaster now shows its wooden track and a train of cars, so the alt text was updated.
- `shot-buy.webp` and `shot-globe.webp`: the front-end smoke test at 1280x720 (Buy Ride window on Aztec Mayhem, the online globe).
- Captions and alt texts follow the new images: the README image is a native capture rather than a browser one, and the hero image shows guests and staff beside Belly Bounce instead of an Aztec Mayhem queue.

While capturing the globe screen the globe was missing: `Rotation.LookAt` returned an unnormalised quaternion (`(axis, angle)` passed as `x, y, z, w`) when the view direction is exactly opposite to `Forward`, which is the globe camera's start view since it begins at yaw and pitch zero. It now builds the half turn with `Quaternion.CreateFromAxisAngle` (`RotationLookAtTests`).
