## Theme Park Inc: the advisor on screen (November 26, 2026)

The client side of Theme Park Inc's advisor port ([TPI-ADVISOR.md](../reverse/TPI-ADVISOR.md) §4.4). While the advisor speaks, its model now stands over the bottom right of the screen, as in the original.

- **The model and its place.** A speech shows the model its response names: the level's advisor for the forecasts and the letters (on Science the bug with the science helmet, holding the forecast letter), the global one otherwise. It stands where Theme Park Inc's model slots put it (`0x464E7F`: further right than Theme Park World's advisor), over the experimental HUD.
- **Clips and mouth.** The model plays the clips of the sequence the simulation chose, each for its own length, and starts no clip after the speech has ended (`0x717067`). Its mouth is the one the simulation's per-frame advisor drew. Each clip start applies Theme Park Inc's own update (`0x715ED0`) and accessory table (`0x715CF0`), which differ from Theme Park World's; the level's theme costume comes from its `Standard.sam`.
- **Shown and hidden.** A camera flight, accessory `0x29A` or a stop hides the advisor, as in the original (`0x7163C3`, `0x4650B0`); after its last clip the model goes.
- **Only a reader.** The view reads the simulation and never writes to it: a test plays a forecast with and without the client's reads and compares the saves. A drawn frame shows the advisor at its own point of the world tick, so the clips move smoothly between ticks. A model that cannot be read is logged once and not drawn; the models load at the first speech that shows them, also in the browser.
- **Theme Park World's advisor** shares the drawing code, which moved into `Advisor.DrawRig` and `AdvisorImageTarget` unchanged.
- **Smoke and capture.** The Science smoke posts a rain forecast and requires the level advisor on screen with clip 25 and a mouth, then gone. `--debug-tpi-forecast` now waits for the arrival flight to end, since a flight hides the advisor's head.
- **Review follow-ups of the playback phase.** TPI-ADVISOR.md lists the co-op prerequisite on speech data; a test shows the world generator ends the same with and without speech data; the response rows are read-only; the save test of the first phase's advisor shape uses the save's own JSON options.

Labels: no new approximation and no new extension label. `[EXT:tpi-advisor-tick-clock]` covers the drawn frame's point of the tick, `[EXT:tpi-text-hud]` the advisor drawn over the text HUD, `[EXT:advisor-aspect]` the width outside 4:3, and `[EXT:tpi-capability-gates]` the award ribbons' texture swap and the clip-bit screen effects, which no ported response uses. No new `rand()` is reached.

The original game was not launched, and no binary, data or screenshot of original art is included.
