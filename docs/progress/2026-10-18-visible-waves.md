## The wave machine's waves rise out of the water (October 18, 2026)

A wave machine's waves were a colour effect on a flat water mesh: lighter crests, darker troughs and a ripple in the caustics, too faint to read as waves (reported in Slack: "ik zie geen golven
ontstaan"). The pool water shader (`content/shaders/pool-water.shader`) now has its own vertex stage that raises and lowers the water mesh by the same plane wave the swimmers bob on, up to
`PoolWaves.MaxHeave` (0.105 engine units at height 3, under the 0.12 the water lies below the lowest deck, so a crest never tops the coping). The water tiles are a quarter of a cell, about twelve to a
wave length, so the swell is smooth. The fragment stage shades the slopes against a fixed light over the wave's back (the normal steepened four times for the shading only), adds a glint where that
light reflects to the camera and a band of white water on each crest, broken up by the caustic texture. The swimmers' bob is now the surface's own rise (0.035, 0.07 and 0.105 by height, was 0.03,
0.05 and 0.08), so a swimmer rides the water it is drawn in; a swimmer in an activity (treading water, the jets, a ring, a rope swing's landing) is drawn on the risen water level too (`PoolWaterEffects.SurfaceRise`). Flat water (a resting machine, a pool without one, the strength 0) is drawn exactly as before. Up to eight pools with waves are drawn with
their own wave at once (`PoolRenderer.WaveSlots`, was four). All of this is presentation: the simulation (the happiness, the turn counts) and every determinism pin are unchanged. `[EXT:pools-waves]`.

Checked on the desktop with a silent Jungle capture sequence (a 10 x 7 pool, a wave machine and ten swimmers, `--debug-pool 0,0,10,7 --debug-attractions wavemachine --debug-swimmers 10
--capture-sequence 50,12`): crest bands with white water roll across the pool while the machine runs and the water is flat again in its rest. For the browser, the regenerated
`content/shaders/web/pool-water.json` (`WebShaderTests` with `OPENTPW_WRITE_WEB_SHADERS=1`) was compiled, linked and drawn in a WebGL2 page in Chromium over a grid of water tiles: the surface rises
in bands at strength 1 and stays flat at 0, with no GL error. The browser game itself was not run. New test: `PoolsAttractionsTests.TheWaterSurfaceRisesWithTheWaveAndTheSwimmersRideIt` (the heave
under the deck, the bob equal to the rise, the shader's constants agreeing with `PoolWaves`).

Still open: the machine's settings are stepped by clicking it through four presets (a window with real controls is the next step), and the swell is one plane wave along +x +y (no reflections
off the walls, no wave from the machine's own side).
