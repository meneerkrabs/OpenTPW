## Weather lines only for news (October 18, 2026)

The weather banner and log lines no longer repeat "The weather has changed: dry weather." and "Weather forecast: dry weather in 4 days." every few game
days while the weather stays dry. The simulation follows tp.exe: its weather update (`0x512880`) rolls a forecast every cycle, `DaysBetweenChanges` minus
`DaysOfWarning` days after the last change, whatever the weather is, and applies it `DaysOfWarning` days later. The forecast event's only subscriber, the
weather's own handler (`0x5127a0`), posts nothing, so the original shows no weather line at all. OpenTPW's lines (`[EXT:forecast-presentation]`) announced
every roll and every apply, so in Halloween World (a cycle of 7 days with 4 days of warning, about 5.7 seconds a day at normal speed) dry weather was
announced twice every 40 seconds.

`WeatherForecastState` now posts only news (`[EXT:forecast-cadence]`): a change when the kind of weather changes, and a forecast, with its countdown banner,
when it brings another kind of weather than the current one. An undecided snow-or-rain forecast during rain is still announced. A change and the next
forecast in one update (a cycle no longer than the warning) post both, where the old code dropped the change. The simulation, its random draws and the
saved schedule are unchanged. docs/AUDIO.md "Weather visuals" describes the cadence; the stale "handler not traced" notes are gone.

Tests: `WeatherForecastTests` (`AForecastOrApplyThatKeepsTheKindOfWeatherIsNoNews`, `AChangeAndTheNextForecastInOneStepBothPost`, and the resumed-warning,
no-spam and restore tests with current weather of the kind the cases need).
