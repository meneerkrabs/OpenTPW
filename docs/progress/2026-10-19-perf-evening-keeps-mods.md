## The browser harness's evening hook keeps the session's other extensions (October 19, 2026)

`Level.PerfEvening` (the `--evening` option of `tools/web-perf/run.mjs`) activated a fresh mod set that named only
`opentpw.evening`, so every other extension fell back to its default and the session's mods were dropped for the rest of
the measured run. It now switches only the evening extension on the session's own set (`ModSet.WithPresentationExtension`,
which refuses an extension that changes the simulation, so a run never changes what a park save records), and an unknown
mode changes nothing. Review follow-up of #614; `EveningTests.ThePerfHookSwitchesOnlyTheEveningExtensionAndKeepsTheRestOfTheSession`.
