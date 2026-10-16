## Park objects fade into the level's fog colour (October 16, 2026)

Distant terrain, objects, water and kid sprites faded into one fixed blue (`0.301, 0.84, 1`, and `0.28, 0.88, 1` in the old 3d shader),
so the hallow and space parks faded into a blue haze against their own night and orange skies. The engine has one fog colour, and
tp.exe read it from the level's Standard.sam (UI-107):

- **The colour** `ThemeEngine.FogColour` (jungle and fantasy `0x48D8F8`, hallow `0x112244`, space `0xF88430`) is copied by `0x4080E0`
  into the engine fog colour `[0x8BD47C]`, which the viewport clear (`0x5737AA`), the sky (`0x583F0C`, `0x58438C`) and the fogged
  vertices (`0x5747D0`, `0x576B41` hands it to the device) all use. OpenTPW now passes it to every shader that fogs (`test`, `water`,
  `sprite`, `3d`) as `g_vFogColor` in the object uniform buffer (`RenderFog`, set from the level before each world pass; the lobby uses
  its clear colour). The precompiled web shaders (`content/shaders/web`) are regenerated.
- **The range** (read, not applied): the fog is linear in view depth (`D3DRS_FOGTABLEMODE` 3, no range fog, `0x567AD0`; the vertex
  path `0x5747D0` blends the same way) with start and end from FIRSTPERSONVIEWDISTANCE 0-3 (`0x44D8F0` -> `0x583280`, far plane `0x8BD4C0`):
  50-60, 100-110, 135-150 and 230-250 original units (0.2 engine units each); the table fog is switched off altogether when the render
  flags `[0x8BCBC8] & 0xC000` are set (`0x567AD0`). The per-primitive state
  bit `0x100000` switches `D3DRS_FOGENABLE` (`0x567620`). OpenTPW keeps its own distance curve, because its park camera is not the original's
  (UI-101) and the literal range, seen from the default camera, fogs the whole park out; the high-preset captures confirmed that.
- Checked with offscreen native captures of the jungle, hallow and space parks from the overview camera: the far scenery fades into cyan,
  night blue and orange respectively.

Still open under UI-107: the moon billboards, the render-flag variants of the clear and the sky, the original fog range with the
original camera, and which passes set the fog bit. Presentation only; the simulation is untouched.
