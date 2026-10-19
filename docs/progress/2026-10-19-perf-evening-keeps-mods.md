## The browser harness's evening hook keeps the session's other extensions (October 19, 2026)

`Level.PerfEvening` (the `--evening` option of `tools/web-perf/run.mjs`) activated a fresh mod set that named only
`opentpw.evening`, so every other extension fell back to its default and the session's mods were dropped for the rest of
the measured run. It now switches only the evening extension on the session's own set (`ModSet.WithPresentationExtension`,
which refuses an extension that changes the simulation, so a run never changes what a park save records), and an unknown
mode changes nothing. Review follow-up of #614; `EveningTests.ThePerfHookSwitchesOnlyTheEveningExtensionAndKeepsTheRestOfTheSession`.

The rest of the #614 review: the lamp cache is dropped by day, so it no longer keeps removed objects or a replaced object
list alive between nights; a test covers a removal and a placement between two frames that keep the list's length (only
`ParkObjects.Version` catches it); the docs say so; the progress entry of #614 gives the medians it measured (a quarter to a
half less); and `run.mjs` undoes the evening hold and the extra lamps before its `--zoom` window, so that window measures
the park as it was.
