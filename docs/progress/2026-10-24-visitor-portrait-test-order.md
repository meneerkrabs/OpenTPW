## The visitor portrait test passes alone again, and the lamp test no longer leaks its file system (October 24, 2026)

`VisitorLivePortraitTests.SingletonCloseReopenAndLowerScreenDrawDoNotReuseStaleDelegatesOrRequireUiUpdates` failed whenever its class ran alone (`--filter "FullyQualifiedName~VisitorLivePortraitTests"`) on main (a33f217f4, 73813b2b8 and a2c8300ac), while the full no-data suite in CI passed. The test threw a `NullReferenceException` in `UiImages.Resolve` while it drew the HUD stack.

- **Cause:** the fixture has no UI models, so the visitor window's buttons fall back to the `purple_button` art. `UiContext.ResolveTexture` defaults to `UiImages.Resolve`, which lists `/ui/textures` through the global game-data `FileSystem`, and that is null when the class runs alone.
- **Why the suite passed:**
  - `ObjectLightTests.Cleanup` put the saved file system back only when it was not null. After `TheExampleLampLoadsThroughTheModPipelineWithItsLightAndSettingsAndItsScriptDrivesTheLight`, `FileSystem` stayed the lamp test's stand-in game folder, which the same cleanup then deletes.
  - Every later class that saves and restores `FileSystem` put that value back.
  - In the portrait test, listing `/ui/textures` in the deleted folder threw a `DirectoryNotFoundException`. `UiImages.Resolve` catches that, so `purple_button` resolved to nothing. The empty listing then stayed in the shared listing cache until something reset it.
- **How it was found:**
  - One instrumented run of a broad filter logged every change of `FileSystem` with the test on the stack, and the failing test's inputs. The filter held 1848 tests: the classes that assign `FileSystem` by its short name, plus the portrait class.
  - At the portrait test, `FileSystem` was the lamp test's folder, and `/ui/textures` was not cached yet. The lamp test made the only change from null to a file system that nothing undid.
  - A two-test filter, the lamp test plus the portrait test, passes; the portrait test alone fails. In these runs the classes ran in name order, which MSTest does not promise.
- **Fix:**
  - The portrait tests' UI context resolves no image (`ResolveTexture = _ => null`), as the other synthetic HUD drawing tests do. The test no longer reads the global file system or fills the shared listing cache, and its assertions are unchanged.
  - `ObjectLightTests` saves the file system before anything in its setup can fail, and its cleanup puts it back, null included.

Verified:
- The portrait class passes alone (74 tests), and so does the failing test on its own.
- `ObjectLightTests` passes alone: 21 passed, 1 skipped without game data.
- A broad filter of 2856 tests passes: 2359 passed, 497 skipped, none failed. It holds every class that assigns `FileSystem`, the portrait class, and the 75 classes after `ObjectLightTests` (in name order) that use a UI context, the park HUD or the game flow. Those that set no file system of their own now see none, as they would alone.
- An instrumented run of that filter found no class leaving `FileSystem` set.

Still open:
- Fifteen classes still restore `FileSystem` only when the saved value is not null. They replace it only when original data is present, so the no-data suite never sees them. In a run with that data, the first of them leaves the real data as `FileSystem` for the tests after it.
  - With the game data (`OPENTPW_GAME_PATH`): `AudioIntegrationTests`, `HeldSoundCorpusTests`, `KeptBodyAssetTests`, `OriginalThingSlotsTests`, `ParkLandOriginalDataTests`, `PoolsTerrainCorpusTests`, `PreviewDepthOrderTests`, `ShopVisitAssetTests` and `WinterPriceMeasureTests`.
  - With Theme Park Inc data (`OPENTPW_TPI_PATH`): `ThemeParkIncFrontEndTests`, `ThemeParkIncLanguageTests`, `ThemeParkIncPlayTests` and `ThemeParkIncViewerTests`.
  - With both: `TpiRidesEndToEndTests` and `TpiRidesShippedModTests`.
