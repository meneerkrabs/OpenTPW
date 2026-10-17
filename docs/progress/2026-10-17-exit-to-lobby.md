## Exit To Lobby no longer fails while the advisor is speaking (October 17, 2026)

Leaving a Full Simulation park within about half a minute of its start (the advisor's level-start welcome speaks for about 28 s in English) showed the "automatic checkpoint failed" dialog instead of the lobby: the checkpoint refuses to capture the advisor
while a response is being spoken (the speech has no snapshot seam), and the exit asked for exactly that capture. The player had to wait for the speech or press Retry later. Found by the browser performance runner's `switch` scenario, whose
Exit To Lobby timed out with the HUD on that dialog.

- **Original.** The park's stop path `0x5996D0`, which the park state machine `0x54E360` and the level exit `0x4A9380` run (two of its six callers), stops the speech (`0x51C0E0`) and kills the advisor (`0x429D60`): leaving a park never waits for a response.
  `AutomaticAdvisor.EndSpeechForExit` now ends the speech silently (no slap) right before the exit's checkpoint capture, so the checkpoint is written and the lobby opens. Manual saves keep refusing while a response is spoken.
- **Runner.** `tools/web-perf/run.mjs` waits three frames after the pause menu appears (its lines are laid out from measured text on the first frames, so an earlier click missed Exit To Lobby and the HUD stayed on `pause`) and
  names the checkpoint failure dialog in its error instead of timing out. Leaving a park now writes its checkpoint, so entering the island again offers Continue: the runner answers that screen with New park
  (`themeStart`, the choice before Back), and when the next island needs keys the fresh profile has not earned (Full Simulation refuses with the progress failure dialog) the switch scenario uses Jungle as its second park and says so in the step name.
  Checked against a locally published interpreter build: the `jungle` and `switch` scenarios finish, with no park alive in the lobby.
