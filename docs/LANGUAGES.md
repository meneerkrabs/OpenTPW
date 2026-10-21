# Languages

Theme Park World shipped text, fonts, speech and banner meshes in at least nine
languages: English, Danish, French, German and Swedish on the European CD, Dutch (and
Polish and Italian) on the Benelux CD, Spanish on the Spanish CD, and Korean on the EA Slim
Pack CD; US English (Sim Theme Park) and Hebrew are known from the localisation pack (below).
OpenTPW verifies twelve: English, US English, Danish, Dutch, French, German, Hebrew, Italian, Korean,
Polish, Spanish and Swedish. A fan-made Russian translation loads too and is labelled as one; it is
not an official release. tp.exe's language table also names Japanese, Traditional and Simplified
Chinese and Thai, for which no data is available ("The original's language table"). Language
selection is folder-based. Original data is never part of this repository.

## Where the data lives

- A retail install contains one language: `Data/Language/<Name>/` (string tables
  `*.str`, BF4 fonts `*.bf4`, `MBToUni.dat`/`UniToMB.dat`, banner meshes `*.MD2`)
  plus speech under `Data/global/Speech` and `Data/levels/<level>/Speech`.
- The CD carries every non-English language as an overlay that mirrors `data`:
  `<Lang>/data/Language/<lang>/`, `<Lang>/data/global/speech/` and
  `<Lang>/data/levels/<level>/speech/` (speech banks, `lips.WAD`, `lips/sp_001.LIP`),
  and banner meshes in `<Lang>/Meshes/<Lang>/` (`bankrupt`, `congrats`, `paused`,
  `.MD2` plus `.mtr` except French). The CD's case is inconsistent (`danish`,
  `french`, `speech` vs installed `Speech`, `lips.WAD`, German `sp_001.lip`,
  `French/meshes/french`), so OpenTPW matches overlay paths case-insensitively.
  A game folder copied from the CD rather than installed has no banners in
  `Data/Language/<Lang>`; OpenTPW then uses the `<Lang>/Meshes/<Lang>` folder beside `Data`.
  Placing the CD's banner meshes inside `Language/<Name>` matches the installed
  English layout and is an inference; the installer itself was not run.
- The Spanish CD and the Korean EA Slim Pack CD are single-language CDs with a different layout: a
  complete `Data` folder on the CD root (not an overlay tree) whose `Data/Language` holds English
  next to the CD's own language (`spanish`, `korean`), the CD's own speech (`Data/global/Speech`,
  `Data/levels/<level>/Speech`, 637 lip-sync files in `lips.WAD`), and a folder named after the language
  beside `Data` (`Spanish/` with `Setup`, `Meshes/Spanish/` and `Filter/Spanish/`; `Korean/` likewise, with
  `meshes/korean` and `filter/korean` in lower case). Their `Autorun` folder holds `spanish.tre` or
  `Korean.tre` next to `autorun.tre` and `general.tre`, and the root holds Windows locale files named by
  locale identifier (`00000c0a.016` Spanish; the Korean CD carries `00000404`, `00000406`, `00000407`,
  `0000040c`, `00000412`, `0000041d`, `00000809` and `00001004`, so it names languages the installer
  knows, not the CD's own). Both discs were read as data only; the SafeDisc `Game.exe`, the installers and
  patches were never opened. `GameEditions.Detect` reports the CD's language from these marks
  (`CdLanguage`: the one non-generic autorun cabinet, else the language folder beside `Data`, else the
  only non-English locale file).
- The Benelux CD uses the same overlay layout for Dutch: `Dutch/data/language/Dutch/`
  (21 `.str`, 33 `.bf4`, `MBToUni.dat`, `UniToMB.dat`, `residx.dat`),
  `Dutch/data/global/speech/` (`speechHD.SDT`, `lips.WAD`), level speech with
  `lips/`, `Dutch/Meshes/Dutch/` (three banner `.MD2`, no `.mtr`) and
  `Dutch/Filter/Dutch/`. Its readme is dated 22 October 1999.

## Selecting a language

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --language German --language-data /path/to/extracted-cd
```

| Source (highest first) | Language | Overlay |
|---|---|---|
| Command line | `--language <name>` | `--language-data <dir>` |
| Environment | `OPENTPW_LANGUAGE` | `OPENTPW_LANGUAGE_DATA` |
| `language.json` in the OpenTPW config folder (beside `display.json`), written by Options | `Language` | `LanguageDataPath` |
| Old .NET user settings (read only when `language.json` has no choice) | `Language` | `LanguageDataPath` |

Names are case-insensitive (`german` → German). Without a name: an overlay that
holds exactly one language selects it; otherwise English if `Data/Language/English`
exists, otherwise the alphabetically first installed language. An unknown name is
an error listing the available languages. The startup log prints the choice, e.g.
`Language: German (/…/German/data/Language/German, language overlay)`.

`--language-data` accepts these forms:

- a folder of CD language folders (`<dir>/German/data/Language/German`, …), e.g. a
  full extraction of the CD, so one path serves all four languages; or
- one `<Lang>/data` folder (`<dir>/Language/<Name>`); its sibling `Meshes/<Name>`
  is used for banners when present; or
- the root of a single-language CD (`<dir>/Data/Language/spanish` beside `<dir>/Spanish`), or its `Data`
  folder: the CD's language is offered (speech from that `Data`, banners from `<dir>/<Lang>/Meshes/<Lang>`)
  and the English that stays in the CD's `Data` is not (it is the installed game's own).

A game folder that is such a CD (`--game-path /path/to/spanish-cd`, with `Data` and the `Spanish` folder on the
root) starts in the CD's own language when no language is chosen; English stays in the list. The installation
check lists both.

Without `--language-data`, the CD folder (`--cd-data`, `OPENTPW_CD_DATA`, or the CD chosen
under Options → Game files) serves as the language overlay when it holds more languages than
the installed game, so its languages appear in the options' language list.
A game folder copied from the CD rather than installed (with `Data` beside the CD's
`<Lang>` folders) counts as such a CD folder without being chosen separately. Only languages
whose `MBToUni.dat` OpenTPW can read are offered (all twelve verified ones, Polish, Korean and Hebrew through the two-byte
layout below, and the fan Russian).
Desktop and browser choose their start-up language with the same code (`LanguageStartup`); the
browser reads `/config/language.json`, falls back to the default with a warning when the saved
language is missing, and copies the CD languages' text and banners, and the chosen language's speech
(docs/WEB.md, "Languages").

The advisor takes both its global speech bank and `lips.wad` and the level's own `speech/speechHD.SDT`
(the Welcome line, response 1, is a level response) and `lips` from the current language, overlay first,
each time a line is said. Nothing is cached, so a live switch changes the voice at the next line, on the
desktop as in the browser. Until this was found the level bank was always the installed language's.

Two editions of `UITEXT.str` exist. The installed game examined here has 473 entries; the European
CD's languages (its English included) and the Mac version have 474, with an extra message at entry
207 ("You cannot load this park, as it uses new rides which you have not yet downloaded") that moves
every later entry up by one. OpenTPW numbers UI strings by the 473-entry edition (`UIStrings`), so
`GameLanguage.LoadStrings` drops entry 207 from a 474-entry UITEXT; without that, every string from
207 on (Game Options, the option labels, Yes/No, the resolutions, …) showed its neighbour's text.
The other 20 tables agree between the editions; `CHAT_COMMANDS.str` only appends six entries.

The overlay is read-only and is never copied into the game folder or the
repository. Files missing from the overlay fall back to the installed data.

### Extracting the overlay from the CD image

Any extractor works; with 7-Zip, outside the repository:

```sh
mkdir -p /tmp/tpw-languages
7z x -o/tmp/tpw-languages TPWORLD.ISO 'Danish/data' 'Danish/Meshes' 'French/data' 'French/meshes' \
  'German/data' 'German/Meshes' 'Swedish/data' 'Swedish/Meshes'
```

About 110 MB. Only data files are extracted; the CD's installers are not needed
and must not be run.

## Switching languages

[EXT:live-language] Options → OpenTPW → *Language* switches when the options are accepted, without a
restart. In the front end the language, its string tables and its fonts are loaded once every
dialog is closed (a display change may still wait for confirmation), the lobby and the menu are
rebuilt, and the options open again in the new language; a language that fails to load leaves the
old one in place. In a park the choice waits: the options say it is used when the park is left,
leaving the park (to the lobby or into another park) applies it, and choosing the current language
again takes it back. The full CD overlay stays available, so every CD language remains in the list.
Caches of language data (UITEXT through `Localization`, the online strings, the chat font, object
names in the object catalogue) follow `GameLanguage.Version` and reload after a switch; an advisor
line already playing finishes in the old language. The choice is saved in `language.json`, so it
also survives a new release unpacked in another folder (the old user settings file lived in a
folder that depends on the program's location). The front-end smoke test switches to the second
available language and back, and switches inside a park, checking the captured text each time.

## What uses the selected language

| Data | Status |
|---|---|
| String tables (`*.str`) | Loaded from the language folder with that folder's own `MBToUni.dat` (front end, options, HUD, popup help, sandbox text panel; see UI.md). |
| `KEYBOARD.str` | The 21 shortcut letters of the language (Dutch buy K, hire A, research O), applied to the HUD shortcuts when the language changes (`Input.ApplyKeyboardStrings`, tp.exe `0x40cb80`); a plain letter that is a camera-pan key (W, A, S, D) keeps its default. In the Polish and Korean tables even an ASCII letter is a two-byte code (the letters sit behind the lead bytes), so those files hold two-byte entries, which decode to the same 21 letters and punctuation marks (`BHIASVFLTGRCSVPHCO,.P`) as every other language. |
| BF4 fonts (`*.bf4`) | Loaded from the language folder (UI screens in SMALL/MED/BIG tiers, sandbox text panel). The Korean fonts hold 17,270 glyph records each (every Hangul syllable, KS X 1001 hanja, symbols), see "Korean". |
| Sign fonts (`fonts.wad`) | The 17 TrueType fonts every CD ships; the Korean CD adds `gulim.ttc` (Gulim, GulimChe, Dotum, DotumChe), which its sign files name instead of a `.ttf`, see "Korean signs". |
| Sign text (`OBJECT_NAMES.str`) | The park gates (their row, such as English 85/86 "LOST"/"Kingdom") and every object sign in the language; `--export-signs <language> <dir>` writes all of them as PNG with an index (RUNNING.md, "Sign textures"). |
| Banner meshes (`bankrupt`/`congrats`/`paused.MD2`) | Resolvable via `GameLanguage.FindFile`; not drawn by the game yet. |
| Speech banks, `lips.WAD`, level `.LIP` | Resolvable via `GameLanguage.ResolveDataFile` (overlay first). `--advisor-say N` plays global `sp_NNN` and its `lips.WAD` mark list from the selected language (see LIPS.md); level LIPs are not used. |

## String table decoding

`.str` (BFST) bytes are 1-based indices into the language's `MBToUni.dat` (BFMU,
uint16 count + UTF-16LE code units). The tables differ: Danish, Spanish and Swedish have
248 entries, English/French/German 249, with different characters at the same
index (Spanish is the Latin-1 set, with no typographic apostrophe). Decoding every language with the English table (the earlier behaviour)
garbled 2,254 of 2,358 Danish and 2,268 of 2,358 Swedish strings and the French
`œ` / German `š` cases. Each string record is `0x01`, a 24-bit little-endian
length, then the bytes; reading one length byte truncated the longest UITEXT
entries (over 255 characters in every language).

### Two-byte tables (Polish and Korean)

The Polish and Korean `MBToUni.dat` files use another header and a two-byte code. Header: `BFMU`, a uint16
**lead-byte count** (0 in the single-byte tables), then two bytes: the **last byte** (0xFF) and the **trail count of
the last lead byte**. For the single-byte tables the same two bytes read as the uint16 character count. The lead bytes are
the top `count` byte values (0xFF down to 0xFF - count + 1, taken in ascending order); a byte below them is a single
character (index = byte), and a lead byte `L` is followed by a trail byte `T` (1..255) and means the index
`(first lead - 1) + (L - first lead) * 255 + T`. The last lead byte has only its trail count of trails, which gives the character count:

| Language | Lead bytes | Single bytes | Trails of the last lead | Characters |
|---|---|---|---|---|
| Polish | 1 (0xFF) | 254 | 75 | 329 |
| Korean | 67 (0xBD..0xFF) | 188 | 252 | 17,270 |

The record length of a string counts bytes, not characters (a Korean string of five Hangul syllables is ten bytes long). The
characters are in descending code order. In the Polish table the Polish letters (`ą ć ę ł ń ó ś ź ż` and their capitals) are single
bytes, and the ASCII letters and most punctuation sit behind the lead byte; in the Korean table the single bytes are digits,
a few symbols and the full-width characters, and the lead bytes cover hanja, KS X 1001 symbols, Hangul (11,172 syllables
from U+D7A3 down) and the ASCII letters (`A` is index 17,225). `UniToMB.dat` agrees: its entries hold the code as the lead byte
(low byte) and trail byte (high byte) for every two-byte character, which cross-checks the layout for all 17,270 Korean entries
(`TheKoreanTableIsTheSixtySevenLeadByteLayout`). tp.exe's loader (`0x6ada90`, magic check `BFMU`/`BFMH` at `0x6adac6`) derives the
single-byte count as `255 - leads` and the table size as `255 * (last - 1 + 0x101) - 254 * (255 - leads) + last trails` in 16-bit arithmetic; its string
decoder (`0x6adc80`) maps a byte below the first lead to `byte - 1` and a lead `L` plus trail `T` to `255 * (L - 1 + 0x101) - 254 * (255 - leads) + T` (zero-based, modulo 65536),
which is this layout's `(first lead - 1) + (L - first lead) * 255 + T` minus one, so the formula agrees with the original on the Polish and Korean tables. Two small differences:
tp.exe's size is one below the character count (its range check rejects the table's last entry, U+0001 in both tables) and it does not reject trail byte 0;
OpenTPW keeps every entry and rejects trail 0. The data agrees (every string table of both languages decodes without an unknown code). Korean text decodes to 697 distinct Hangul syllables; Polish to its 16 letters.

## Languages added from the Spanish, Korean and Polish CDs

**Spanish** (Spanish CD, single-byte table of 248 characters): the 21 string tables (2,358 entries, UITEXT with 473 entries), the 33 fonts, `KEYBOARD.str`,
speech and lip-sync (`lips.WAD`, 637 lines) and banners all read like Danish's. The non-ASCII characters are `¡ º ¿ Á Í Ó á é í ñ ó ú`.
**Polish** (Benelux/Polish CD, `Polish/data` overlay): the same 21 tables and 33 fonts; the three GAMEBOLD and the two MATISSE fonts
have 262 glyphs, the others 329. **Korean** (EA Slim Pack CD): 21 tables with 2,365 entries (UITEXT has 474), 32 fonts, 637 lip-sync lines.
The Korean language folder lacks `GAMEBOLD9/10/12` and `MATISSE18/36` and adds the four `DATE*` fonts; the SESH fonts, the CASH fonts, `CONSOLE6`
and `COURIER8` carry only Latin glyphs (229 or 384 of their 17,270 records), and the DATE fonts draw only Latin, Greek and Cyrillic.
`[EXT:ui-font-korean-bold]`, `[EXT:ui-font-korean-heading]` and `[EXT:ui-font-korean-date]` make `UiFonts` use the label font where a role
font is missing or draws fewer than half the glyphs of the label font (a Hangul date such as `1년 1월` in the HUD, a heading), instead of a blank line.
The language row shows a language's own name (`한국어`, `Polski`, `Español`) only when the font in use draws it, else its English name
(`SupplementaryStrings.DisplayName`).

The 17,270-glyph fonts pack into 2048 or 4096 pixel atlases (`FontFile.MaximumGlyphs` is 20,000 now); the biggest ones (the MENU fonts, 10 million
coverage pixels) fill a 4096 x 4096 atlas.

### Korean signs

Every `.sgn` of the Korean CD (80 files, 160 text slots) names `gulim.ttc` as its font file in 159 slots, with the face `굴림` (Gulim) or, in a few,
`굴림체` (GulimChe) as the code page 949 bytes of its LOGFONT (`B1 BC B8 B2`; `SignFile` reads face names as Latin-1); the 160th slot names
`gargsai_.ttf`. The Korean `fonts.wad` holds that TrueType
collection (12.7 MB: Gulim, GulimChe, Dotum, DotumChe, 40,087 glyphs and 20,608 cmap entries each, a format 4 BMP cmap), and
`TrueTypeFont.ParseCollection`/`SignFontLibrary.Find(file, face)` pick the member by face name (the English or Korean family name, or the
code page 949 bytes of the Korean one; an unknown face keeps the first member). `SignFile` accepts a `.ttc` file name next to `.ttf`.
Sign text is laid out as for Latin (what tp.exe and the shipped `usp10.dll` say, read statically):

- tp.exe's sign function (`0x5EAB70`, caller `0x5ECB40`) passes a UTF-16 string to `ScriptStringAnalyse` (`0x5EACE0`, `0x5EAD66`, `0x5EAE03`, `0x5EAE89`),
  all four calls with `dwFlags` 0x80 (`SSA_GLYPHS`) and nothing else: no `SSA_FALLBACK` (0x20), so a character the sign's font lacks is drawn from that font only. The function
  calls no `MultiByteToWideChar`, `IsDBCSLeadByte` or `CharNext` and has no per-byte loop: two-byte codes never reach it. The
  widths are scaled by the slot percent as for Latin (`0x51EB851F` multiply, i.e. divide by 100, truncated).
- usp10's itemizer (`0x65112330`) classifies every UTF-16 unit through a two-level table (`0x65107AF8`): all of U+AC00..U+D7A3 (Hangul), Han,
  Kana and Hangul jamo are class 76 and script id 12, whose row of the routine table `0x65108E74` is the generic one (shape `0x6511325C`, place
  `0x65113458`) that scripts 0 to 3, 5, 7 to 11 and 19 also use. That shaper gives one glyph per UTF-16 unit through the font's own format 4 cmap,
  with no jamo composition, OpenType lookup or kerning; the placement sums per-glyph advances from the width cache (`0x6510F241`). The script-in-font check
  (`0x6510F1D2`, `USP_E_SCRIPT_NOT_IN_FONT`) cannot fail for this row. Latin letters are script 50 and place the same way.
  The one difference to OpenTPW is the advance source: usp10 asks GDI for integer pixel advances (`GetGlyphOutlineA` with `GGO_METRICS`, `gmCellIncX`,
  `0x6510EB3E`), where OpenTPW scales the `hmtx` advance. That is the width rounding of `APPROX:COMPAT-002` for Latin and now also for Hangul with Gulim's hinting
  (`APPROX:COMPAT-033`). A missing glyph uses the cmap's default (glyph 0, .notdef here), as for the other fonts.

## The original's language table

tp.exe selects its language from the registry value `Language` (a DWORD under
`HKLM\Software\Bullfrog Productions Ltd\Theme Park World`, default `0x809`) through a switch at `0x5c0d00`: each known value sets one
bit, the lowest set bit indexes a table of sixteen names (`0x4193d0`, one copy per translation unit), and that name is the folder
`Data\Language\<Name>` (`0x419710` opens it) and the suffix of the loading-screen pictures. Any other value stores `0x809` (English).
`GameLanguage.OriginalLanguageTable` holds the table; the save container's language byte is a separate, shorter list (`SaveContainerFields`).

| Index | Name (folder) | `Language` value | OpenTPW | Data |
|---|---|---|---|---|
| 0 | English | `0x809` | verified | European CD, every CD's English, pack |
| 1 | French | `0x40C` | verified | European CD, pack |
| 2 | German | `0x07` | verified | European CD, pack |
| 3 | Spanish | `0x0A` | verified | Spanish CD, pack |
| 4 | Swedish | `0x1D` | verified | European CD, pack |
| 5 | Italian | `0x10` | verified | Benelux CD (`Italian/data`), pack |
| 6 | Dutch | `0x13` | verified | Benelux CD, pack |
| 7 | Polish | `0x15` | verified | Benelux/Polish CD, pack |
| 8 | Danish | `0x06` | verified | European CD, pack |
| 9 | Japanese | `0x11` | no data | not available |
| 10 | TraditionalChinese | `0x404` | no data | not available (the pack has only a `.reg` file) |
| 11 | SimplifiedChinese | `0x804` | no data | not available |
| 12 | Thai | `0x41E` | no data | not available |
| 13 | Korean | `0x412` | verified | Korean EA Slim Pack CD, pack |
| 14 | Hebrew | `0x40D` | verified (right to left) | pack |
| 15 | American | `0x409` | verified (US English) | pack (`USEnglish`) |

Some values are full language ids (`0x809`, `0x40C`, `0x412`), others primary language ids (`0x07` German, `0x10` Italian); the pack's `.reg`
files use exactly these. The full Italian id `0x410` or a Russian id would select English. The fan Russian has no entry: it replaces
the American folder and is selected with `0x409`. The language word is also read by the online code (`0x41a830`; untouched here) and by
`0x41a850`, which tests for Hebrew (`0x40D`).

## The localisation pack

The ModDB "Sim Theme Park World Localization Files" pack holds one folder per language with a `data` folder to copy over the game's
and a `.reg` file that sets `Language`: `English`, `USEnglish`, `Danish`, `Dutch`, `French`, `German`, `Hebrew`, `Italian`, `Korean`,
`Polish`, `Russian`, `Spanish`, `Swedish`. Each `data` holds `Language/<Name>` (US English and Russian in `language/american` and
`language/American`), `Init/<width>/` pictures (not for Korean), `ui.wad`, `postcard/tpwlogo.tga` and the global and per-level speech.
Read it as data only; nothing in it is run (the German folder contains an `ip.exe`).

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --language Hebrew --language-data /path/to/tpw-language-pack
```

`--language-data` takes the pack's root (every language), one of its folders (`.../tpw-language-pack/USEnglish`) or that folder's
`data`. A pack folder whose language folder has another name is named by its language folder (`USEnglish` → `American`, also accepted
as `--language USEnglish` or `"English (US)"`), except a fan translation, which keeps its own name ([EXT:fan-translations]): the
`Russian` folder is the language `Russian`, whose files are the `American` folder's. The options list it as "Russian (fan
translation)" in every interface language. A game folder into which the Russian data was copied cannot be told from US English and
shows as US English.

### Files beside the text: ui.wad, Init and postcard

tp.exe reads these from fixed paths below its one data folder: `data\ui.wad` with `data\ui`, `data\language.wad` and
`data\Postcard(.wad)` (the folder table at `0x747c84`), and the loading screen's pictures `data\init\<width>\splash_<Name>.tga`,
`legal_<Name>.tga`, `welcome_<Name>.tga` and `welcome.tga` (`0x587380`, called by the start-up routine `0x54dcf0` and the main state
machine `0x54e360` with the table name of `0x41a820`; the width folder is the screen width when it is 400, 512, 640, 800 or 1024, else
640). The splash shows first at start-up, then `welcome.tga` with the legal notice; later loading screens show `welcome.tga` with
`welcome_<Name>`; a missing picture is simply not shown. tp.exe has no per-language lookup for any of these: a language's own copies are
used only because its data was copied over the game's, as the localisation pack's readme instructs.

| Source | `ui.wad` | `Init` | `postcard` |
|---|---|---|---|
| Benelux and Spanish CDs | one, the European one | `Data/Init` holds Splash, Legal and Welcome pictures for nine languages and `Welcome_Hebrew`; the overlays (`Dutch/data`) have none | one |
| Korean CD | its own: another logo, an IME list frame (`f_imelist.md2`) | `Welcome.tga` only | the European one |
| Localisation pack | per language; US English's differs only in `tpw_logo.wct` (Sim Theme Park, both texture sizes), the others are the European one | per language, named with the table name (`Splash_American`); Hebrew has only `Welcome_Hebrew`, Korean none | per language; US English's logo differs |

OpenTPW layers the language's data over the game's as before (overlay first, then the game): `GameLanguage.ResolveInitImage` and
`ResolvePostcardFile` resolve these files, and a language whose overlay has its own `ui.wad` (`OwnInterfaceArchive`) replaces the
game's for every `/ui/...` read (`LanguageInterfaceArchive`, `BaseFileSystem.ArchiveRedirect`), at start-up and on a live switch
(interface images reload). That swap is OpenTPW's (`[EXT:language-settings]`): it models an install the language's data was copied over.
It also applies to Korean loaded from the Korean CD's own `Data` folder, which now uses that CD's `ui.wad` (its own logo, the IME list
frame `f_imelist.md2` and its own `qickload.txt`), as a Korean install would; the Spanish CD's `ui.wad` is the European one. In US English the lobby's and the player screen's logo is Sim Theme Park's. OpenTPW draws no loading
screen and does not compose the original postcard picture, so the Init and postcard files are resolved but not shown yet.

### US English

Tables, fonts and fonts' glyphs as the UK English (21 tables, 2,365 entries, 248-character table; the only non-ASCII character used is
`é`), with the US speech bank (Terry McGovern; the UK voice is Lewis Macleod), its own ui.wad, Init pictures and postcard logo. It is
offered as "English (US)" and uses the English OpenTPW labels.

### Italian

Italian (Benelux CD and pack, identical language folders): 21 tables, 2,365 entries, the 248-character table, non-ASCII `° à è é ì ò ù`,
37 fonts with a glyph for every used character, 637 lip-sync lines. `KEYBOARD.str` differs from the other languages in two letters
(`BHIASVFLTGRCSVCHKO,.P`). OpenTPW's own labels are translated (no native speaker has reviewed them).

### Hebrew

Hebrew is right to left. What tp.exe does, read statically:

- **Table.** `MBToUni.dat` is the one-lead-byte layout of the Polish table (254 single bytes, 75 trails of lead `0xFF`, 329 characters):
  U+0001..U+00FF plus the rest of the code page 1255 repertoire (the Hebrew points and letters, ₪, the direction marks LRM and RLM and
  the Windows punctuation). The codes are Bullfrog's indices, not Windows-1255 bytes; the Hebrew letters are single bytes, the ASCII
  capitals sit behind the lead byte. `UniToMB.dat` round-trips every string.
- **Order.** The string tables keep Hebrew in logical order (`UITEXT` "Go Online" is `התחבר לרשת`, first letter first), with Latin words
  in their own order (`DirectX`).
- **Drawing.** The text library turns every laid-out line that holds a character U+05B1..U+05EA into visual order (`0x6ae4b0`, called
  for each line by the only drawing loop `0x6ae550`): it reverses the line (without the spaces a wrap consumed), then reverses back each
  word that has no Hebrew letter (a word is a run of characters other than space and comma, `0x6ae3d0`, `0x6ae390`), and a Hebrew word
  that now starts with a parenthesis has it swapped with its partner inside the word (`0x6ae300`). There is no bidi algorithm: words are
  not regrouped, so two Latin words in a Hebrew line trade places ("Direct Draw" shows as "Draw Direct"), and a parenthesis around
  several words is not mirrored. `TextVisualOrder` ports this and `TextLayout` applies it to every line, in any language (only Hebrew
  triggers it). usp10 plays no part in interface text.
- **Alignment.** Both interface text helpers (`0x492750`, `0x4929f0`; other widget draws call the library's draw `0x6ae0f0` and measure `0x6ae1c0` directly, at `0x48fbb0`, `0x491e1a`, `0x491e84` and `0x491680`, and keep an alignment swapped by `0x4925a0`) swap left and right
  alignment when `0x41a850` reports Hebrew; `0x4925a0` does the same for an alignment a caller keeps. `UiContext.DrawText`/`DrawTextAt`
  do this (`UiContext.RightToLeft`), so labels that are left aligned elsewhere stand at the right, as the cash in the HUD.
- **Keyboard.** The window procedure maps each typed character from 0x20 up through a fixed table (`0x46b460`, called at `0x46b8c5`):
  the lower-case Latin letters and `' , . / ; ` ` ` become the Hebrew letters on the same keys of the Israeli layout (`t` aleph,
  `c` bet). `HebrewKeyboard` ports it for typed text. The interface library's edit fields also swap the arrow keys and stop placing the
  caret by mouse when the flag at `0xfaa56c` is set (`0x667a72`, `0x668269`, `0x6668e9`); OpenTPW's text fields have no caret to move,
  so this is not ported.
- **Fonts.** The Hebrew folder has 32 fonts: no GAMEBOLD9/10/12 and no MATISSE18/36 (as Korean), and the CASH, DATE, SESH, CONSOLE6 and
  COURIER8 fonts have no Hebrew letter. The GAME, MENU, TITLE and POSTCARD fonts have the letters but not the accented Latin letters of the
  table (so "Français" shows as "French" in the language list). `UiFonts` gives a heading or date font without Hebrew letters the label
  font's role, as it does for Korean ([EXT:ui-font-korean-heading], [EXT:ui-font-korean-date]).
- **Signs.** Sign text goes through usp10 (`ScriptStringAnalyse`, which resolves bidi), not through the pass above. None of the 17 fonts of
  `fonts.wad` has a Hebrew glyph, so a Hebrew gate name is a row of `.notdef` boxes, in logical order in OpenTPW ([APPROX:COMPAT-034]).
- **Init.** The pack has only `Welcome_Hebrew.tga`, so tp.exe shows no splash and no legal notice in Hebrew.

OpenTPW's own labels are translated into Hebrew with ASCII punctuation only and without words that mix Hebrew and Latin letters (the
pass above would reverse the Latin part), and multi-word Latin names are joined with no-break spaces (`Theme Park Inc`) so they keep
their order. No native speaker has reviewed them.

### Russian (fan translation)

The pack's Russian replaces the American folder: the same 248-character Latin table as US English, but its strings are Windows-1251
bytes, so they decode to Latin-1 look-alikes (`Ïîòåðÿíîå Êîðîëåâñòâî` for `Потеряное Королевство`), and its fonts draw Cyrillic shapes
at those Latin positions. That is how the original shows it too, and OpenTPW draws it the same way with no new code: the interface table
`UITEXT` is untranslated (English), the names, objects, staff states and messages are Russian; the speech bank and `Welcome.tga` are its
own. Sign fonts and OpenTPW's own labels have no such Cyrillic positions, so signs show the Latin look-alikes and the labels stay English.
Text in logs and saves is the Latin form.

## Theme Park Inc

Theme Park Inc ships its own language folders (`Data/Language/{Dutch,English,French,German,Swedish}`: 37 string tables and the fonts each, 3,775 entries per language,
single-byte `MBToUni.dat` tables of 248 or 250 characters), which the extracted CD has next to `_Support_<Language>_String_Tables` folders (InstallShield
string-table pointers for the installer, not game text). They go through the same `GameLanguage` path: `FindLanguages`, `Readable` and `Resolve` list the five, the
default is English, and `--language Dutch` (or `OPENTPW_LANGUAGE`, or the saved `language.json`) selects one for the experimental slice, which has no options screen. What follows the choice:

- **Interface.** The slice's text HUD (`ThemeParkIncHud`, `[EXT:tpi-ui]`) reads its labels, tool help, build window, pause menu and layout dialogs from `OpenTpwText` (35 `Tpi*` keys, nine
  languages each) instead of English literals; English is unchanged. In Dutch the buttons read Bouwen..., Verwijderen, Draaien, Annuleren, Snelheid x1 and Menu.
- **Attraction names.** The slice's object names come from the language's own `OBJECT_NAMES.str` (`ObjectNames`, matched by the English name, the Theme Park Inc binding rule being untraced).
- **Cross-edition mod.** With the extension on and a mod input registered as Theme Park Inc data, the objects the mod adds take their names from that install's `OBJECT_NAMES.str`
  in the selected language (`ObjectNames.ApplyThemeParkIncNames`, matched by the object's own `Info.Name`, which objects.json's English name replaces in `SettingsName`); a name the table
  lacks, or a language the install does not have (Spanish, Korean, Polish, Danish: English then), keeps what objects.json gives.
- **Advisor text.** The slice runs no advisor (`EditionCapabilities.Advisor` is false), so there is no advisor text to translate; the speech banks of Theme Park Inc are not read.

## Verification

Synthetic tests (no assets) cover the reader, the 24-bit length, per-folder
character tables, and language resolution (defaults, case-insensitive names and
paths, both overlay forms, mesh and speech fallbacks). With
`OPENTPW_GAME_PATH` (installed English) and `OPENTPW_LANGUAGE_DATA` (extracted
CD folders, plus `Dutch` from the Benelux CD), for each of the six European and Benelux languages:

- all 21 string tables / 2,358 strings decode; record lengths match the file layout;
  the non-ASCII character inventory is pinned;
- sample strings are pinned (UITEXT `GoOnline`: "Go Online", "Gå Online",
  "Se connecter", "Online gehen", "Koppla upp", "On-line gaan"; Dutch "Spanning",
  "Betrouwbaarheid", "Totempaal"; French "d'œuvre"; German
  "Unfuhg Gibsniš"; Swedish strings over 255 characters);
- every character used by any string table has a glyph in all 33 fonts of that
  language, and every UITEXT entry lays out in GAME8AA without fallback glyphs;
- level `sp_001.LIP` files resolve from the overlay and parse; banners resolve.

Spanish, Korean and Polish have their own data tests (`NewLanguageDataTests`), gated on `OPENTPW_ES_PATH` (the Spanish CD's root), `OPENTPW_KR_PATH` (the
Korean CD's root) and `OPENTPW_PL_PATH` (a folder that holds `Polish/data`, such as the Benelux CD's root); `OPENTPW_GAME_PATH` adds the overlay checks. They pin: all 21 tables decode with no unknown
code (2,358 / 2,365 / 2,358 entries; the character inventories), sample strings, the 21 KEYBOARD shortcuts, `UniToMB.dat` round trips of every string, the
Korean and Polish table layouts, the glyphs of every used character in every font, `UiFonts` in all tiers, speech, lip-sync and banners, edition detection from
the autorun cabinets, both overlay forms, and for Korean the sign fonts (gulim.ttc, all signs' faces) and a sign with Hangul text. Synthetic tests
(`MultiByteLanguageTests`) cover the lead/trail layout, malformed codes, the two-byte codec, CD folder detection and the TrueType collection without any data.
`LanguagePackDataTests` (`OPENTPW_LANGUAGE_PACK`, the pack's root; with `OPENTPW_GAME_PATH` the pack goes over that game) cover US English,
Hebrew, Italian and the fan Russian: all 21 tables decode (2,365 entries, the character inventories), sample strings, the KEYBOARD
letters, `UniToMB.dat` round trips, the Hebrew table's layout and repertoire, the fonts' glyphs, `UiFonts` in every tier, the language
list in all thirteen pack languages, Hebrew drawn right to left and right aligned, US English's ui.wad, Init pictures and postcard
(and its own voice), the Init names, speech and lip-sync; `LanguagePackTests` cover the language table, the right-to-left pass, the
alignment swap, the Hebrew keyboard, the Init paths, the pack's folder names and a language's own ui.wad without any data.
`ThemeParkIncLanguageTests` (`OPENTPW_TPI_PATH`) decode all 37 tables in all five Theme Park Inc languages and pin Dutch, French, German and Swedish strings and names.

```sh
OPENTPW_GAME_PATH='/path/to/Theme Park World' OPENTPW_LANGUAGE_DATA=/tmp/tpw-languages \
  dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter 'FullyQualifiedName~LanguageTests|FullyQualifiedName~StringTableTests'
bash scripts/run.sh --game-path '/path/to/Theme Park World' --language German --language-data /tmp/tpw-languages --smoke-test
```

The German native smoke test draws "Totemfall", "Spaßfaktor", "Zuverlässigkeit"…
and its readback matches the CPU composite (max difference 0 on Metal). The front-end smoke runs in Spanish
(`--game-path <Spanish CD> --language Spanish --front-end --smoke-test`) and in Korean the same way: the lobby menu, options, mods, game
files, online screens, HUD, buy window, finance, research and staff windows are drawn and read back from the GPU in Hangul (no boxes), and the language switch inside the smoke goes to the other language and back.
`--load-original-level science --language Dutch --capture-world out.png --capture-interface` writes a Theme Park Inc capture in Dutch.

## Not done

- Original-style screens use the strings (UI.md); their layout remains an approximation. Supplementary labels come from the OpenTPW translated table.
- The language switches live in the front end; inside a park it waits until the park is left (see "Switching languages").
- The `--advisor-say` speech/lip-sync slice works; automatic gameplay triggers and banner rendering remain unimplemented.
- `UniToMB.dat` is read and round-trip tested in all nine languages (COMPATIBILITY.md); text-input UI integration remains incomplete, and typing Hangul needs an input method that
  this code does not have (the codec encodes Hangul, but no UI uses it).
- Korean signs: the Gulim glyph advances and coverage are OpenTPW's (`APPROX:COMPAT-033`); the embedded bitmap strikes and the TrueType instructions of Gulim are not used, and no Korean capture of the original is available.
- The browser build is not verified with the Spanish and Korean CD layouts or the Korean fonts' size (docs/WEB.md).
- The account e-mails of the online server (`EmailTexts`) and the OpenTPW labels were translated for the new languages, but no native speaker has reviewed the Spanish, Korean, Polish or Theme Park Inc HUD translations; the PR lists the uncertain ones.
- Theme Park Inc's string tables beyond the object names and the OpenTPW HUD (its UITEXT numbering is its own; the original advisor and mission texts) are not used by the slice.
- OpenTPW draws no loading screen (the `Init` pictures) and does not compose the original postcard picture; both resolve per language.
- Italian and Hebrew have OpenTPW labels but the online labels (`OnlineStrings`) stay English for them, US English and the fan Russian.
- The Hebrew edit-field behaviour (arrow keys, caret) and a Hebrew sign font are not available; Hebrew signs draw boxes (`APPROX:COMPAT-034`).
- The browser build is not verified with the localisation pack.
- The CDs examined are `TPWORLD.ISO` (European), the Benelux/Polish CD, the localisation pack, `Theme Park World_1.iso` (Spanish), `TPWorld-KR.iso` (Korean EA Slim Pack) and the extracted Theme Park Inc CD; other editions/patches are unverified.
