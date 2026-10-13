# Own-format save/weather roundtrip audit

Audit base: `3a47751a749afe2126a54693f9822a72ebd3f233` (2026-10-13).
**ACCEPT within the synthetic subsystem scope; no new production defect found.**
This audit adds missing full-envelope and file coverage without changing weather
rules, save versions, restore ordering, UI, help or prototype behavior.

The prior direct weather-snapshot tests already cover the schedule, random stream,
drops, lightning/thunder clocks and settings-dependent validation. Relevant saved
history also records the completed forecast-day restoration fix and the accepted
coaster/prototype save reviews. Their earlier original-data and GPU evidence is
historical evidence, not a fresh qualification performed by this audit.

`ParkResumeWeatherRoundtripTests` uses real `FixedStepClock`, `GuestSimulation`,
`ParkEconomy`, `ParkWeather`, cell-map and script-world capture/restore APIs. Its
empty-object park has a live seeded visitor and explicit synthetic weather keys.
The strict `ParkResumeSaveFile` codec and real `.otpw` file save/load carry the
whole envelope into fresh runtime modules. Tests compare complete recaptured
serialized envelopes, including weather and diagnostics; the gameplay digest
alone would not prove weather preservation because it excludes weather state.

Five new cases cover:

- An unstarted weather object and a fractional fixed-clock remainder. Restore
  does not advance the random stream early; the next real simulation step starts
  both timelines identically and advances the clock, economy and guest counters.
- Rain and snow storms with a pending forecast, due thunder and live lightning
  clocks. Actual file save/load preserves the pause setting, clock remainder,
  dropped-time diagnostic, all weather fields, queues and random states.
- Pause leaves the recaptured simulation state unchanged. After resume, each rain
  and snow timeline remains identical over 300 irregular frame intervals using
  pause, normal and fast clock scales. Due thunder is retrieved once on each side,
  and subsequent weather turns and simulation counters continue advancing.
- An omitted optional weather member stays absent until a real simulation step,
  preserving the existing lazy initialization behavior. This cold fixture does
  not claim reconstruction of missing historical weather.
- Missing strike lists and nonfinite bolt coordinates fail strict serialization.
  A structurally readable drop count above the destination settings' cap is
  refused by the real weather restore without changing that weather instance.
  Structural file validation and destination-settings validation remain distinct.

Verification: **44 synthetic/related cases passed; two original-data cases
skipped** with `OPENTPW_GAME_PATH` unset. The full solution Release build succeeded
with zero errors (141 warnings). The new-file style/whitespace checks, CRLF-aware
diff check, fidelity register check and its eleven Python tests passed. No original
asset files, binaries or GPU captures were opened or executed.

Limits: the fixture uses a synthetic manifest and no placed objects, scripts,
coasters or prototype. It exercises the strict file path and fresh subsystem
restores, not an asset-backed `Level` constructor, destination catalog/asset
manifest qualification, rejected-level transaction, or renderer/presenter/audio
attachment. Those integration paths and original weather parity remain deferred
until their required assets and capture environment are available. In particular,
the branch-local older prototype/coaster refusals are not changed by this audit;
their accepted save work is integrated separately.

```sh
env -u OPENTPW_GAME_PATH dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj \
  --filter 'FullyQualifiedName~ParkResumeWeatherRoundtripTests|FullyQualifiedName~ParkWeatherSnapshotTests|FullyQualifiedName~ParkWeatherTests|FullyQualifiedName~ParkResumeSaveFileTests|FullyQualifiedName~ParkResumeEconomyTests|FullyQualifiedName~ParkResumeValidationTests|FullyQualifiedName~FixedStepClockTests'
dotnet build source/OpenTPW.sln -c Release --no-restore
```
