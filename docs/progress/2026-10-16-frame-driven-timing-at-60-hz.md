## Frame-driven timing at a 60 Hz reference (October 16, 2026)

tp.exe's main loop `0x54e360` has no frame limiter: the simulation runs in fixed 31 ms substeps (`0x54f4c4`, `GetTickCount` with a 2 s
catch-up, 500 ms in the lobby at `0x54e768`), and the loop then draws one frame and sends message `0x1e` once per drawn frame
(`0x48a580` → `0x658f6c`). Everything stepped from that message, or otherwise per drawn frame, ran at the player's frame rate;
PCGamingWiki notes that the game "runs too fast and becomes unplayable" at high frame rates and recommends a 60 FPS cap. With the user's
approval OpenTPW steps every such behaviour at a fixed 60 Hz reference (`[EXT:frame-driven-at-60hz]`, `ReferenceFrameClock`):

- **The research fluids** (UI-066 closed): `0x4a9fc0` advances the fluid by 2 × speed on every `0x1e`.
- **The lobby** (UI-940 closed, UI-941 narrowed to the camera wander `0x5e09da`): its effects (`0x5e0470` via `0x5d5c50`, called once
  per drawn frame at `0x54e73a`), the flyers before the island clip picks (`0x5da3a0`, `0x5e11d0`), and the camera's spin, glide, enter
  sequence and fade (`0x5e0490`, `0x5e1210`, `0x5d8cf2`). The lobby's own 31 ms substep runs only `0x520130`.
- **Rain and snow jitter and the bolt flicker** are redrawn per reference frame (`CRain`'s draw `0x57f490`).

Every simulation step was confirmed to be on the 31 ms substep, so the pinned run hash does not move; nothing is saved. Verified with
4657 tests passing without game data, 5107 with it, and all five native smokes. The full enumeration of per-frame behaviour is in
docs/COMPATIBILITY.md ("Frame-driven timing").

Found and not built yet, now registered: the park camera's mouse rotation per frame (`0x42a760`, UI-102), the pause-line hover ramp
(`0x48b2a0` → `0x48b220`, narrowed into UI-013's text), and the edge scroll of the map window's view (`0x5f0b40`, UI-103). The HUD balance's
three-frame redraw cadence (`0x4a0db0`) is noted in docs/COMPATIBILITY.md only (at most 50 ms of delay at 60 Hz).
