# Languages

Theme Park World shipped text, fonts, speech and banner meshes in at least six
languages: English, Danish, French, German and Swedish on the European CD, and
Dutch on the Benelux CD. OpenTPW can show the text of any of them. Language
selection is folder-based, so other retail languages should work the same way
once their data is available, but only these six are verified. Original data is never part of this repository.

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
  Placing the CD's banner meshes inside `Language/<Name>` matches the installed
  English layout and is an inference; the installer itself was not run.
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
| Settings (`App.config` / user config) | `Language` | `LanguageDataPath` |

Names are case-insensitive (`german` → German). Without a name: an overlay that
holds exactly one language selects it; otherwise English if `Data/Language/English`
exists, otherwise the alphabetically first installed language. An unknown name is
an error listing the available languages. The startup log prints the choice, e.g.
`Language: German (/…/German/data/Language/German, language overlay)`.

`--language-data` accepts either form:

- a folder of CD language folders (`<dir>/German/data/Language/German`, …), e.g. a
  full extraction of the CD, so one path serves all four languages; or
- one `<Lang>/data` folder (`<dir>/Language/<Name>`); its sibling `Meshes/<Name>`
  is used for banners when present.

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

## What uses the selected language

| Data | Status |
|---|---|
| String tables (`*.str`) | Loaded from the language folder with that folder's own `MBToUni.dat` (`Localization`, sandbox text panel). |
| BF4 fonts (`*.bf4`) | Loaded from the language folder (sandbox text panel). |
| Banner meshes (`bankrupt`/`congrats`/`paused.MD2`) | Resolvable via `GameLanguage.FindFile`; not drawn by the game yet. |
| Speech banks, `lips.WAD`, level `.LIP` | Resolvable via `GameLanguage.ResolveDataFile` (overlay first). `--advisor-say N` plays global `sp_NNN` and its `lips.WAD` mark list from the selected language (see LIPS.md); level LIPs are not used. |

## String table decoding

`.str` (BFST) bytes are 1-based indices into the language's `MBToUni.dat` (BFMU,
uint16 count + UTF-16LE code units). The tables differ: Danish and Swedish have
248 entries, English/French/German 249, with different characters at the same
index. Decoding every language with the English table (the earlier behaviour)
garbled 2,254 of 2,358 Danish and 2,268 of 2,358 Swedish strings and the French
`œ` / German `š` cases. Each string record is `0x01`, a 24-bit little-endian
length, then the bytes; reading one length byte truncated the longest UITEXT
entries (over 255 characters in every language).

## Verification

Synthetic tests (no assets) cover the reader, the 24-bit length, per-folder
character tables, and language resolution (defaults, case-insensitive names and
paths, both overlay forms, mesh and speech fallbacks). With
`OPENTPW_GAME_PATH` (installed English) and `OPENTPW_LANGUAGE_DATA` (extracted
CD folders, plus `Dutch` from the Benelux CD), for each of the six languages:

- all 21 string tables / 2,358 strings decode; record lengths match the file layout;
  the non-ASCII character inventory is pinned;
- sample strings are pinned (UITEXT `GoOnline`: "Go Online", "Gå Online",
  "Se connecter", "Online gehen", "Koppla upp", "On-line gaan"; Dutch "Spanning",
  "Betrouwbaarheid", "Totempaal"; French "d'œuvre"; German
  "Unfuhg Gibsniš"; Swedish strings over 255 characters);
- every character used by any string table has a glyph in all 33 fonts of that
  language, and every UITEXT entry lays out in GAME8AA without fallback glyphs;
- level `sp_001.LIP` files resolve from the overlay and parse; banners resolve.

```sh
OPENTPW_GAME_PATH='/path/to/Theme Park World' OPENTPW_LANGUAGE_DATA=/tmp/tpw-languages \
  dotnet test source/OpenTPW.Tests/OpenTPW.Tests.csproj --filter 'FullyQualifiedName~LanguageTests|FullyQualifiedName~StringTableTests'
bash scripts/run.sh --game-path '/path/to/Theme Park World' --language German --language-data /tmp/tpw-languages --smoke-test
```

The German native smoke test draws "Totemfall", "Spaßfaktor", "Zuverlässigkeit"…
and its readback matches the CPU composite (max difference 0 on Metal).

## Not done

- No original menu/UI screens use the strings yet; only the sandbox panel and
  `Localization.Parse` do. Layout of longer translations in original screens is
  unverified.
- No in-game language switch; the language is chosen at startup.
- Only the `--advisor-say` speech/lip-sync slice exists (no in-game triggers); banner rendering is not implemented.
- `UniToMB.dat` (reverse table, needed for text input) is not read.
- Only the CD in `TPWORLD.ISO` was examined; other editions/patches are unverified.
