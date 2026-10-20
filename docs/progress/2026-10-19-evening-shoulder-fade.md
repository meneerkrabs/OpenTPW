## The evening shoulder fades in with the dusk (October 19, 2026)

The soft highlight shoulder of the evening extension (`test.shader`) switched on whole on the first frame below full
daylight, so a lit surface dropped in one frame (a fully lit 0.8 texture from 1.0 to 0.58) twice per day cycle, and
already compressed bright unlit surfaces in the middle of the dusk. It is now weighted by the ambient that has fallen away,
`clamp((1 - g_flAmbientLevel) / 0.65, 0, 1)`: nothing on the first dusk frame, the whole shoulder at full night (ambient
0.35), with the uniform block unchanged. `EveningTests.TheShoulderFadesInWithTheDuskSoALitSurfaceNeverJumps` checks day,
the first dusk frame, full night and every 0.01 step between; `content/shaders/web/test.json` is regenerated. Review
follow-up of #632: EVENING.md now says the pool spray and the smoke, dust and steam particles take the sprite tint too, and
the lamp test allows the taller bound only for the underwater light.
