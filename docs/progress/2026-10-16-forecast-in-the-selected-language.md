## The weather forecast in the selected language (October 16, 2026)

The forecast banner and its messages ("Forecast: rain tomorrow.") were always English. They are an OpenTPW extension: tp.exe posts no
forecast message, and the shipped string tables hold no weather text (searched again in all 23 English `.str` tables), so there is no
original wording to load. Their words now come from `SupplementaryStrings` like OpenTPW's other own labels, translated for the six
shipped languages (English for any other). The word order follows the language: the Dutch countdown reads "Weerbericht: morgen regen."
and the German one "Vorhersage: in 3 Tagen Regen.". `WeatherForecastPresenter` passes the selected language, and the countdown is
worded again on every step, so a language switch shows at once. The Dutch glyph test covers the new texts in the original fonts; the
Danish, French, German and Swedish folders were not available locally.
