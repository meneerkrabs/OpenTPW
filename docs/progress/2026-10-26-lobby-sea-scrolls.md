## The lobby sea scrolls (October 26, 2026)

The sea of the front-end lobby is no longer a still picture. tp.exe plays `Basem1.MD2` on the lobby terrain every frame (`0x44e510` → `0x473c70`, started by the idle restart at the head of `0x473c70`), and its two UV tracks (`Sea_Under` and `Sea`, 200 ticks, one texture repeat towards -U and -V) move both sea meshes; `LobbyScene` now draws the terrain as an animated model and plays that clip in a loop, on the same 30 ticks a second as every other clip.

Research for issue 829 (packages 3c and B): the Jungle terrain's `basem.MD2` (eleven UV tracks on the river and waterfall meshes) is loaded by tp.exe's terrain loader but never played, because the terrain instance is only updated in the lobby state of the main loop. The water of a park stands still in the original, so OpenTPW leaves the park terrain unchanged. The other three themes have no terrain clip. [MD2-MODELS.md](../MD2-MODELS.md) ("Terrain clips") and [COMPATIBILITY.md](../COMPATIBILITY.md) ("Animated terrain water") hold the trace.

`TerrainUvAnimationTests` pins the clips of both archives (tracks, nodes, corner counts, ticks, the scroll per cycle), the loop and its restart, the converted sea parts that scroll, and a synthetic clip of the same shape; the front-end smoke test now requires the sea clip to be playing.
