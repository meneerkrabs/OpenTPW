## The with-data test classes put the global file system back (October 24, 2026)

In a run with original data, 31 test classes left their own game-data `FileSystem` behind as the global one. A later test that reads `FileSystem` without setting its own then got that data instead of the null it gets when it runs alone, so it could pass only in the full suite. The no-data suite in CI never saw this: without the data, every setup or test that would replace `FileSystem` stops at `Assert.Inconclusive` first.

- **Fifteen classes put the saved value back only when it was not null.** In a run that starts without a file system, the first of them to run kept its data in place, and every later one saved and restored that leaked value.
  - With the game data (`OPENTPW_GAME_PATH`): `AudioIntegrationTests` (the first in name order), `HeldSoundCorpusTests`, `KeptBodyAssetTests`, `OriginalThingSlotsTests`, `ParkLandOriginalDataTests`, `PoolsTerrainCorpusTests`, `PreviewDepthOrderTests`, `ShopVisitAssetTests` and `WinterPriceMeasureTests`.
  - With Theme Park Inc data (`OPENTPW_TPI_PATH`): `ThemeParkIncFrontEndTests`, `ThemeParkIncLanguageTests`, `ThemeParkIncPlayTests` and `ThemeParkIncViewerTests`.
  - With both: `TpiRidesEndToEndTests` and `TpiRidesShippedModTests`.
- **Sixteen classes replaced it and never put it back.**
  - In `[TestInitialize]`: `BumperTypeCoverageTests`, `FileSystemTests`, `GuestAssetTests`, `GuestMeasurementAssetTests`, `M3GateAssetTests`, `ParticleSpriteAtlasTests`, `PathAssetTests`, `PrototypeRideSaveAssetTests`, `RideCameraTests`, `RidesSweepTests`, `TourAssetTests`, `TrackAddOnDataTests`, `TrackAssetTests` and `TrackPieceClipDataTests`.
  - In two of their tests: `TourEffectTests` and `VehicleEngineVoiceTests`.
  - Fixing only the first group would have left `BumperTypeCoverageTests` as the leaker right after `AudioIntegrationTests`.

**Fix.**
- Each of the 31 classes saves `FileSystem` as the first statement of its `[TestInitialize]`; four classes get one. Its `[TestCleanup]` puts the saved value back unconditionally, null included.
- MSTest 2.2.7 runs `[TestCleanup]` even after a `[TestInitialize]` that stops with `Assert.Inconclusive` or an exception; two throwaway test classes confirmed it. Because the save comes first, that cleanup puts back the value it found.
- `AudioIntegrationTests` and `HeldSoundCorpusTests` also restored `ObjectCatalog.BonusDataRoot` inside the same null check. Its save moves first too, but their cleanup puts it back only when the setup changed it, because its setter also stops the catalog following `OPENTPW_BONUS_DATA`.

**Verified:**
- The with-data suite ran once on this change, with the game data, Theme Park Inc data and the TPWS saves: 11,003 tests, 10,812 passed, 191 skipped, none failed. Its sources were those of this branch after merging main with #818.
- **An uncommitted probe** wrapped every test and logged three things: every change of `FileSystem` with the test frames on the stack, each test's reads of the value it started with, and each test that ended with a different value. All 2,335 changes were undone: every test started with `FileSystem` null and ended with the value it started with.
- **337 test runs read that inherited `FileSystem` in game code, and all passed with null.** Two more tests read it only to check that a CLI export leaves it alone.
  - Almost all of these reads are `ParkHud.CursorArt`; with no file system, the system pointer stays.
  - The rest are `ModContentRoots.FileSystemOrNull`, `ObjectAssets.TextureDirectories` and `Advisor.SayResponseParts`.
  - No skipped test read it.
- **The same 633 tests on main** (c85211222, with the probe): the 32 classes behind those runs, plus `AudioIntegrationTests`.
  - `AudioIntegrationTests` left its game data behind. 291 of the 337 runs read it, and all 337 passed.
  - On this branch they all read null, with the same result: 632 passed, 1 skipped (a language-CD test, skipped in both runs).
- The 31 changed classes alone: with data, 217 of 217 pass. Without data, 42 pass and 175 are skipped, which exercises the cleanup after an inconclusive `[TestInitialize]`.

Still open:
- Nothing stops a new test class from leaving `FileSystem` set again; only a with-data run with such a probe shows it.
- Other global state that tests replace is not covered here. That includes `Log`, and the cleanups of `TpiRidesEndToEndTests`, `TpiRidesShippedModTests` and `ThemeParkIncLanguageTests`, which clear `ObjectCatalog.BonusDataRoot` even when their setup left it alone.
