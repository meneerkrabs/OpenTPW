## Weather and particles as tp.exe projects them (October 27, 2026)

Package R9-E. The rain, snow and lightning now reach the screen the way the original's code puts them there, and five register entries are closed or narrowed.

- **AUDIO-900 retired:** the rain's 0.3 parallax factor belongs to the camera-mode bits `0x16` of `0x790AB0`: the terrain camcorder (bit 2, OpenTPW's free walk) and the ride follow (bit 4); no instruction sets `0x10`.
- **AUDIO-901 retired:** rain and snow are the original's clip vertices (`0x57F1F0`, `0x57F490`), drawn from their screen position `x / w`, `y / w` with the display aspect word, so sizes and spread match at any window size (about half the former flake size). The depth value is written in the scene's depth space.
- **AUDIO-902 retired:** the bolt ribbon is the emitter `0x57DD10` (mitred joins, clip-space width `1.3 / (depth + 1)`, the projection `0x578D20`, the quad order); the walk's emit calls were checked against the original's walk on four seeds.
- **AUDIO-904 retired** (user decision after the measurements, docs/AUDIO.md "Near-plane measurements"): the original near-plane behaviour is the only one; `[EXT:weather-near-plane]` and the `OPENTPW_WEATHER_NEAR` switch are gone, the weather depth stays in the scene's depth space.
- **PTCL-904 retired:** nothing rescales a frame's float +8, so textured quads are the frame's aspect times the display's width over its height wide; screen-space quads follow the original's factors.
- **PTCL-900 narrowed:** the refresh gate is read (instance registered in the scene, table with a matrix key). **PTCL-909 narrowed:** the particle pass lies in the only scene view before the advisor's model; the HUD windows' place is not read.

Reference numbers for the tests come from running tp.exe's own instructions offline; the game was never launched. The determinism hash is unchanged (presentation only, no simulation stream is drawn).
