# Languages

Theme Park World shipped text, fonts, speech and banner meshes in at least nine
languages: English, Danish, French, German and Swedish on the European CD, Dutch (and
Polish and Italian) on the Benelux CD, Spanish on the Spanish CD, and Korean on the EA Slim
Pack CD. OpenTPW can show the text of English, Danish, Dutch, French, German, Korean, Polish,
Spanish and Swedish. Language selection is folder-based, so other retail languages should work the
same way once their data is available, but only these nine are verified (Italian loads, but it is not
among them). Original data is never part of this repository.

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
whose `MBToUni.dat` OpenTPW can read are offered (all nine verified ones, Polish and Korean through the two-byte
layout below, and Italian, which loads but is not among the verified languages).
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
- The CDs examined are `TPWORLD.ISO` (European), the Benelux/Polish CD, `Theme Park World_1.iso` (Spanish), `TPWorld-KR.iso` (Korean EA Slim Pack) and the extracted Theme Park Inc CD; other editions/patches are unverified.
