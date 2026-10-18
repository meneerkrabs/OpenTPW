## Winter W1: settling snow, `opentpw.winter-snow` (October 18, 2026)

Phase W1 of the [winter plan](../WINTER.md). `opentpw.winter-snow` 1.0.0 makes snow settle on the park's ground, roofs and objects while it snows and melt afterwards; the paths turn to greyish slush so they stay readable and cliffs stay bare.
It is **presentation only** (`AffectsSimulation` false: no mod-set record, no hash part, no draw from any random stream), **on by default** (the user's decision), and listed on the Extensions and mods screen in all eleven interface languages.

- **Cover**: `SnowCover` rises with `ParkWeather.CurrentDrops` while it snows (1/35 of the drop share a park turn), melts by 1/70 a dry turn (three times faster in rain), is stepped once per park turn from the weather (new `ParkWeather.TurnStepped`) so it freezes when paused, and is eased per frame for drawing.
- **Shader**: test.shader gets `g_flSnowCover` in the last free float of the uniform block (byte 268, `_padding6`; the block stays 272 bytes, the point lights still start at 272) with procedural value-noise patches, a slope term and a slush look for negative values; `content/shaders/web/test.json` is regenerated and the GLSL ES 3.00 links in Chrome's WebGL2 (uniform block 784 bytes). `ModelEntity.SnowSurface` gives terrain, objects, track and coaster pieces 1, the path layer and queue models -0.35, everything else 0.
  **Cover 0 is byte-identical**: a hallow capture with the extension on and no snow equals the one with it off (`cmp`).
- **Save**: optional unhashed `SnowCover` in the park-resume envelope, written only above 0 while the extension is on, validated on load; old saves load, and a park without snow saves the same bytes.
- **Determinism**: no pin moves. World state hash at park tick 4000 of the hallow park (`--perf-run 9000 --perf-speed 1 --perf-arrivals 3 --perf-hash-tick 4000`): HASHES
- **Debug hooks**: `--debug-snow-cover <0..1>` (`[EXT:debug-snow-cover]`); `--debug-weather 8` already forces hallow snow.
- Tests: `SnowCoverTests` (accumulate, melt, rain melt, clamps, pause and speed through the weather's turns, easing, save field and validation, the uniform slot at byte 268, shader declarations, web JSON, registration, the mods screen in every language, the economy hash).
- Not covered (documented in [WINTER.md](../WINTER.md#settling-snow-extwinter-snow)): sprites, lakes, pools, the sky; ground under a roof whitens too. Wintry Decembers stays W3.
