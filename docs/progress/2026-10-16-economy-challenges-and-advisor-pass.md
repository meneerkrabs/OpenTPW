## Economy pass: challenge census, mechanic advice and the challenge time-short event (October 16, 2026)

A pass over the economy approximations, read in tp.exe via REA:

- **Retired:** ECON-014, ECON-020 (the analyser constructor stamps are BIN-tagged in `World/ParkLitter.cs`, covered by
  `ALoadedBlockCacheCarriesTheConstructorsStampZero`) and ECON-055 (carried-object challenge filter). ECON-001 is closed as
  `[EXT:fixed-catch-up-cap]` (user-approved EXT). The retirements are listed in `tools/fidelity_retired.txt`.
- **ECON-022** (partly built, narrowed): the mechanics' advice message 70, queued newest guard first. Still open: the `+0x36`
  access-cell writer, the entertainer animation `0x4D46D0`, the guard escort to the booth gate cell, the original-save list-chain
  import and the stranded stamp.
- **ECON-026 and ECON-030** (partly built, narrowed): the guest-census cache with analyser age tests (`CachedGuestCensus`,
  `CrowdPhaseTurn`), and `KartBestCrossings` / `TrackRideSections` for challenge types 15 and 21.
- **ECON-058** (partly built, narrowed): the challenge time-short event (message 271) goes through the advisor and the HUD captions.
- **User-approved EXT conversions:** ECON-054 is closed as `[EXT:headless-economy-dispatch]`; the headless part of ECON-023 is
  `[EXT:headless-economy-breakdown]` (its routines were already read); ECON-022 is split, its headless handyman dispatch being
  `[EXT:headless-economy-dispatch]`. These three exist only in the test and tool economy without a map. ECON-023 stays open for the
  split ride state word (the bridge's and the economy's halves, joined by the level), which real parks have too: by user decision it is not an EXT.
- **ECON-017, ECON-059** narrowed.
- **SAVE-005** text no longer cites the retired ECON-055.

Saves: `AnalyserCaches.GuestCensus` and `CrowdPhaseTurn` are new optional fields validated on load; old saves still load.
The pinned run hash is unchanged. Verified with the full test suite with and without game data and the native smokes.

Still open: the arrival target's age-20 census is not wired to the shared cache; the PC advisor response table's 36 parameterised
responses have no captions yet; the tag table (`0x75ECA8`) and entry indices differ (tag 38 is entry 41).
