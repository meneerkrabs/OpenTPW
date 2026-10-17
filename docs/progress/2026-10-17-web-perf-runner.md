## Browser performance runner follows the current front end (October 17, 2026)

`tools/web-perf/run.mjs` stopped at the Game Mode screen on current main: Full Simulation now needs the tick on that screen and, in a profile
without a progress file, the new-progress consent, where the runner used to click Full Simulation once and wait for the park. The runner now
starts a park with `Page.enterFullSimulation`, which reads the game's own state (`menu` and `hudScreen` from `opentpwPerf.state()`) and clicks
each step once per visit of its screen: Full Simulation then the tick on `gameMode`, the first choice on `newProgress`, until the park's HUD is
up. A fresh frame is waited for between clicks so two clicks never share one frame. A `progressFailed` dialog, a Continue screen (`themeStart`,
which means the profile already holds a saved park) or a stopped game end it with an error naming the state instead of a timeout. The jungle,
switch and revisit scenarios all use it, so there are no fixed numbers of presses left. The usage is unchanged. Checked against a locally
published interpreter build: start, lobby, jungle, buy and digest finished, and the jungle, switch and revisit scenarios ran with `--keep lazy`.
