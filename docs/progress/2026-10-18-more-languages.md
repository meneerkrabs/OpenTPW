## US English, Hebrew, Italian and the fan Russian from the localisation pack (October 18, 2026)

The ModDB localisation pack (`--language-data` on its root, one of its folders or that folder's `data`) adds three verified languages
and a fan translation (docs/LANGUAGES.md). **US English** (tp.exe's `American`, the pack's `USEnglish`) has its own speech (the US
voice), Init pictures, postcard logo and `ui.wad`, whose Sim Theme Park logo now replaces the game's: a language's own `ui.wad`
stands in for `data/ui.wad` (`LanguageInterfaceArchive`, `[EXT:language-settings]`): tp.exe reads only the fixed `data\ui.wad` (`0x747c84`), so this
models an install the language's data folder was copied over. Korean from the Korean CD's own `Data` folder now uses that CD's `ui.wad` too. **Italian** is verified like the others. **Hebrew** is drawn right to left as tp.exe draws it, read statically: the
tables stay in logical order, the text library reverses each line that holds Hebrew and turns Latin words back (`0x6ae4b0`, no bidi
algorithm), the two interface text helpers swap left and right alignment (`0x492750`, `0x4929f0`), and typed Latin letters become the
Hebrew letters of the same keys (`0x46b460`). The **fan Russian** (Windows-1251 bytes in the Latin table, drawn by its own fonts) loads
as "Russian (fan translation)" ([EXT:fan-translations]). tp.exe's language table (`0x4193d0`, registry switch `0x5c0d00`) is
documented with all sixteen entries; Japanese, both Chinese and Thai have no data. The Init and postcard files resolve per language
(`0x587380`), but OpenTPW draws no loading screen yet. OpenTPW's own labels are translated into Italian and Hebrew. A Hebrew gate sign
shows boxes, the shipped sign fonts having no Hebrew glyph ([APPROX:COMPAT-034]). Tests: `LanguagePackTests` without data,
`LanguagePackDataTests` with `OPENTPW_LANGUAGE_PACK`; the front-end smoke passes in Hebrew, US English and Italian.
