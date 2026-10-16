## The lobby's enter-park particle effect (October 16, 2026)

The enter-park routine of tp.exe (`0x4b8fd0`, called from `0x5e1e30` through the lobby's vtable slot `0x702f04`) does more than play
`cat_globallobbysfx` 4: before the sound it starts the screen-space particle effect `0x62` over the lobby's key panel through the
particle creator `0x521e60` (the earlier reading of `0x521e60` as a global sound was wrong). OpenTPW's particle system only ran in the
3D park world; the lobby now has a screen-space particle host and starts the effect where and when the original does.

- **Position:** `0x521e60(0x62, 0xC350, 0, z)` with z = ((bottom - top) >> 1 + top) * 75000 / `0x600` of the key panel control `0x1E0EA`
  (layout table Lobby, rows 170..437 of the 1536-row canvas): native (50000, 0, 14794), the horizontal centre of the screen and screen y
  -613/1024, over the panel. The control's animation offset (`+0xF8..+0xFE`, `0x65D42E`) is taken off first, so it is the table rectangle.
- **When:** at the tick of the game-mode dialog, before the sound; the whole block (effect and sound) is skipped in Instant Action
  (game mode 2) and needs the lobby window (`0x7CC4B4`).
- **Host and lifetime:** `ParticleRendererModule.CreateScreenRenderer` builds a `ParticleRenderer` without a level (the original's pool is
  global and drawn by whichever scene is up); `GameFlow` starts the effect in it and the host is deleted with the lobby's entities when the
  park loads (the post-load pass `0x5200B0` kills screen-space emitters). Template `0x62` runs 61 ticks of 31 ms (about 1.9 s) on the
  shipped file and draws 898 sprites.
- **Presentation only:** the effect draws from the particle pool's own random stream; the world and guest streams are untouched, the
  pinned run hash and saves are unchanged. The particle clock is the pool's own 31 ms tick, as for the park's particles.
- **Tests:** `LobbyEnterEffectTests` (position, z scale, Instant Action skip, the menu's tick, template `0x62` on the shipped file) and a
  front-end smoke step that starts the effect and requires screen-space draws.

No APPROX id covers this behaviour (UI-942 was closed as `EXT:online-globe-audio` earlier today, for the online globe's music only).
