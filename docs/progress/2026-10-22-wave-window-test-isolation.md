## Wave machine window tests no longer depend on the test order (October 22, 2026)

The two wave machine window tests (`PoolsAttractionsLevelTests.TheWaveMachineWindowSetsOnOffHeightAndIntervalAndCancelLeavesThemAsTheyWere` and `TheWaveMachineWindowsChoicesFitItAndAreNamedInEveryLanguage`) passed alone but failed in a broad filtered run on main (4bfd2d255): `ParkHud.OpenWaveSettings` returned without opening the window, so the first test saw the HUD screen on top and the second threw a `NullReferenceException`.
- **Cause:** `NativeWidgetTests.FieldKeepsFocusOnHoverAndTabMovesBetweenFields` and `TextFieldTypesUpToItsLimitAndSubmitsOnEnter` focus a text field, so the screen's update sets the static `Input.TextEntryActive`. Only the game's frame clears it, and those tests left it set. The window's gate (`ParkHud.WaveSettingsAllowed`) stays shut while text is typed. The save-and-restore cleanups of the HUD tests that run in between (`ObjectRayPickTests`, `ParkFinaleTests`, `ParkHudHelpTests`, `HudToolClick`) faithfully put the leaked value back.
- **How it was found:** one instrumented run of the broad filter did two things. It logged every change of the flag with the test on the stack, and it dumped each part of the window's gate when the window stayed shut. Every part was open except the text-entry one, and the last unreset change came from the `NativeWidgetTests` test. A three-test filter reproduces it: the leaking test plus the two wave tests, since classes run in name order.
- **Fix:**
  - `NativeWidgetTests` clears the flag in a `[TestCleanup]`, as `ChatPanelTests` already does.
  - The wave tests' gate helper (`WindowGates`, formerly `ParkCamera`) sets the keyboard state the window needs, as it already did for the camera, and restores both.
  - Each change alone fixes the three-test reproduction.

Verified:
- The broad filter (4155 tests) passes: 3886 passed, 269 skipped, none failed. Before the fix, two failed.
- Each wave test passes alone.
- The `NativeWidgetTests` class passes alone.
- An instrumented sweep of the 827 tests in the classes that type into text fields (chat, online, employee, postcard, park name, shop price, mods, options, front end, original UI) found no other test leaving the flag set.
