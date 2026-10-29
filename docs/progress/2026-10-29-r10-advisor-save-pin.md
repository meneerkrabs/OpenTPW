## The excitement cache entry no longer changes saves without it (October 29, 2026)

#908 added the optional `Excitement` entry to the analyser caches of the park save, but wrote `"Excitement": null` for every park, which changed the serialized bytes of every save and the economy digest pinned by `BuildStepsTests.TheEconomyMadeInStepsIsTheEconomyMadeAtOnce` (FullSimulation `D2F1349AA44D6545` became `7876695A107A2FE7`). Stepped and at-once economies stayed equal; the only difference was that line (stripping it gives back `D2F1349AA44D6545`).

- **Fix.** The entry is skipped while null (`JsonIgnoreCondition.WhenWritingNull`, like `AnalyserChase`), so a park that never read the excitement writes the bytes it wrote before and no pin moves. The pin keeps its old value; a park that has read it still saves and restores the cache.
