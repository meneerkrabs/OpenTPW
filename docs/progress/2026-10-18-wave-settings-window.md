## The wave machine gets a settings window (October 18, 2026)

A wave machine's settings (waves on or off, the height and the rest between two runs) could only be stepped through four presets by clicking the selected machine again. The machine now has a
settings window of its own (`ParkHud.OpenWaveSettings`, `[EXT:pools-waves]`). The object window of a selected wave machine, a Features object that the game shows in the shop table, has a
**Wave settings** button in the row where a shop has its quality slider and price box, which a wave machine does not show; that button, or a second click on the machine that is already selected,
opens the window. It is OpenTPW's own size (`UiDialogs.CenteredWindow`) in the original's `w_dialog` frame, with the original's `b_ltoggle` toggles (they play `cat_ui` 0xBD, as the original's
radio groups do): waves on or off, the height (low, medium, high) and the interval (short, regular, long: a rest of 40, 100 or 200 turns), with the machine's current settings selected. The choices
edit the window's copy; the tick writes them to the machine through the new `Level.SetPoolWaves`, and the cross, Escape or a right click leave the machine as it was. The window does not open in a
read-only visit, in the chat mode or over another window. Its seven new texts are in all eleven languages; the window is titled with the machine's own name. `Level.CyclePoolWaves` keeps stepping the presets for scripted use, now on top of
`Level.PoolWavesOf`/`SetPoolWaves`. The settings live where they did (the flags of the machine's access cell), so saves, the validator and every determinism pin are unchanged.

Checked in the game with silent Jungle interface captures (`--debug-pool 0,0,8,6 --debug-attractions wavemachine --debug-pool-lead 190 --debug-hud waves`, and `waves:select` for the object
window alone; `waves` is a new smoke-only `--debug-hud` action): the button sits in the object window's settings row, and the window shows the three rows of toggles inside its frame with the
default (on, medium, regular) selected. New tests in `PoolsAttractionsTests`: every choice, the tick, the cross, the window only for a wave machine, every control inside the window frame, and the
texts in every language.
