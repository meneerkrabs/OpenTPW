# The CD autorun launcher

October 10, 2026. The original CD opens a 640x480 launcher window (`AUTORUN.INF` runs
`Autorun\Autorun.exe`) with the buttons Play, Install, Uninstall, Re-Install, Tech Support,
View Read-me and Exit. OpenTPW reproduces it as an optional start screen
(`[EXT:autorun]`). Everything below was found by static analysis of the CD files and
`Autorun.exe` (never executed); no original data is stored in the repository.

## Files

`Autorun\` on the CD: `Autorun.exe` (Origin Systems "Gateway" 2.24, a scripted launcher),
`autorun.tre` (the script), `general.tre` (shared art), one `<Language>.tre` per language
(English, Dutch, Italian, Polish on the Benelux CD; English, Danish, French, German,
Swedish on the European CD), `si.img` / `woi.img` (the installer stubs the Play and Install
buttons start). All `.tre` files are TREE archives.

## TREE format

Little endian. Header, 8 x u32: magic `TREE`, version (1; the launcher accepts <= 1),
entry count, descriptor offset, hash node count N (15), hash table offset (0x20),
collision list size (0 in every file), collision list offset (equals the descriptor
offset in practice).

- **Hash table**: N (hash, entry index) pairs followed by one (0, 0) pair, laid out as an
  implicit binary search tree in heap order. Lookup starts at node 0 with step 1: equal
  hash = found; node hash > key: node += step, step *= 2; otherwise node += step + 1,
  step = step * 2 + 1; stop when node >= N. Empty nodes are (0xFFFFFFFF, 0xFFFFFFFF).
  An index of 0xFFFFFFFF means the name is in the collision list (records of
  `[length byte][name][u32 index]`; unobserved, implemented from the launcher's code).
- **Hash of a name** (launcher `trees.c`): the path exactly as the script writes it
  (`.\autorun\play.bmp`, backslashes, leading `.\`), upper-cased; `h = c0 << 8`, then for each
  further character `h += (h >> 4) * c; h += i; i++` (32 bit). All 19 hashes of the
  shipped files were matched to script names this way; names are not stored.
- **Descriptors**, 3 x u32 each (offset, stored size with flags, full size). Equal sizes mean
  stored. Otherwise bit 30 selects headerless RefPack and its absence PKWARE DCL
  ("implode"; the launcher links PKWARE's library, no shipped entry uses it). The size is the
  field masked with 0x3FFFFFFF; `autorun.cfg` also has bit 31 set (meaning unknown).
- **RefPack** here has no `FB 10` header and no size: the stream starts at the first command
  (standard 1/2/3/4-byte copy commands and literal runs, `0xFC..0xFF` stop).

Entries: `general.tre` holds `.\general\backgrnd.bmp` (640x480, the language picker's
backdrop without labels) and nine 224x40 language buttons (`english`, `francais`, `deutsch`,
`italiano`, `espanol`, `svenska`, `nederlands`, `dansk`, `polski`). A language archive holds
`.\autorun\back.bmp` (640x480 with the labels baked in, dim brown), `play`, `install`,
`uninst`, `reinst`, `readme`, `tech`, `quit` (224x40, or 408x40 for the languages with a
wider black column: Polish, Danish, French, German) and sometimes `register` (English) or `song`
(German, a leftover "Dungeon Keeper Song" button). All are 8-bit uncompressed BMPs with one shared
palette; index 255 is black. `autorun.tre` holds one RefPack entry, `autorun.cfg` (a DK2-era script:
`define`s, objects, button actions).

## Layout and behaviour (from `autorun.cfg` and `Autorun.exe`)

- Button bitmaps are placed at x = 0. Rows come from `nvPlayY` 61, `nvInstallY` 103,
  `nvUninstallY` 145, `nvReinstallY` 187, `nvTechbuttonY` 229, `nvReadmeY` 271, `nvQuitY` 313
  (the `PLAYY`/`INSTALLY` defines are unused). Matching each button bitmap against the
  labels of `back.bmp` finds the same rows exactly.
- For every language except English, French and German the script moves Read-me to 229 and
  Exit to 271 and hides Tech Support (six rows); the backdrops of Dutch, Italian, Polish, Danish
  and Swedish have six labels.
- **There is no hover or pressed state.** The buttons are owner-drawn: the decompiled draw
  routine copies the bitmap with `SRCCOPY`, draws a 3D edge only when the script's `border` is
  set (it is `FALSE`) and a focus rectangle (`DrawFocusRect`, inflated by -2) on the focused
  button. A button that is *visible* shows its bright yellow bitmap; a *hidden* one is absent
  and the dim label of the backdrop shows. So dim means unavailable, bright means available.
  The script shows Play/Uninstall/Re-Install only when the game is installed, Install when
  a setup exists and it is not, Read-me when `ReadMe.txt` exists in the language folder,
  Tech Support when `TechSupp.hlp` exists. Cursor, hover sounds and a start sound are not used
  (`STARTWAV`, `MOVIES` are 0).
- Language: a build with `MULTI_LANGUAGE` shows `backgrnd.bmp` with the language buttons
  (rows 108, 148, 188, 228, 268 per CD) until a language is known from the registry;
  `<Language>.tre` is then added and `<language>\` becomes the read-me and setup folder.
  OpenTPW does not show the picker: the game already has a language.
- The window is a captioned 640x480 window titled "Theme Park World", centred.

## In OpenTPW

`AutorunAssets` (art, script rows, read-me lookup), `AutorunView` (CPU composite, focus, clicks, no GPU),
`AutorunScreen` (GPU: the 640x480 image point-sampled at the largest integer scale that fits the window,
centred on black), `AutorunLauncher` (when to show it) in `source/OpenTPW/Client/Autorun`;
`TreArchive` and `TreCompression` in `OpenTPW.Files`.

- Shown before the front end when an `Autorun` folder with `general.tre` exists in the game folder or
  in the CD folder (`--cd-data`, `OPENTPW_CD_DATA` or the CD saved in setup; a `Data` folder works too).
  The archive is the game's language (`GameLanguage.Current`), English when absent.
- Available (bright): **Play** continues to the front end, **View Read-me** opens
  `<CD>/<Language>/ReadMe.txt` (else the CD's `ReadMe.txt`) with the system's default application,
  **Exit** quits. Install, Uninstall, Re-Install and Tech Support stay dim: there is nothing to install
  and no WinHelp viewer. Tab / arrow keys move a dotted focus rectangle, Enter or Space activates; a click
  needs press and release on the same button.
- Skip with `--no-autorun`, `OPENTPW_NO_AUTORUN=1` or `"showAutorun": false` in `setup.json`.
  `--capture-world`, `--sandbox`, `--load-original-level`, `--visit-park` and plain `--smoke-test`
  never show it.
- `--front-end --smoke-test` runs `AutorunSmokeTest` first when the launcher is available: window readback
  must equal the integer-scaled CPU composite (captures `artifacts/native-smoke-<language>-autorun*.png`),
  a click on an unavailable button does nothing, Tab draws the focus rectangle, a click on Play starts the
  normal front-end smoke test.

Not verified against a capture of the original: the focus rectangle's dot phase (`UI-040`) and that
bright buttons keep their pure-black background rectangle (the launcher copies it as it is; the backdrop's
black is (4,4,4)).

Tests: `TreArchiveTests` (synthetic archives, RefPack command forms, the PKWARE reference vector, hashes
of the original names; private CD files with `OPENTPW_GAME_PATH`), `AutorunTests`.
