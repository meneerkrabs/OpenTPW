## The lobby's lightning flash is drawn under the bolts and the rain (October 24, 2026)

Part of #829 (Halloween island flicker). On a strike the front end inverts the picture for one frame; the bolt and the rain now stay bright on that frame, as in the original.

- **Original:** the lightning draw (`0x57ee30`, reached from the rain render `0x57ff60`, called by the scene `0x576a00` at `0x576dab`) draws the full-screen quad first (`0x57ed50`, state `0x9A1`: source blend INVDESTCOLOR, destination ZERO), then the bolts (`0x57ee4d`); the raindrops follow (after `0x57ffec`).
- **Before:** OpenTPW drew every entity, including the lobby's weather renderer, and the invert quad after them, so the bolt and the rain came out dark.
- **Now:** the other entities, then the flash quad, then the weather renderer (`GameFlow.RenderLobbyLayers`). The strike rate and the one-frame duration are unchanged.
- `docs/AUDIO.md` no longer says the flash is an opaque white frame.
- A test pins the order.
