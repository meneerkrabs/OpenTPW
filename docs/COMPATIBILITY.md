# Compatibility: known issues, profile and detail presets

OpenTPW's answer to the problems players hit when running the original Theme Park World
on modern systems. The default is **pure Original**: no fix is active, the detail preset
values come from the original `low/med/high.sam`, and every value that is not taken from
original data is tagged `[APPROX:COMPAT-NNN]` in code and listed in the
[approximation register](#approximation-register). User-facing deviations by design
(the Enhanced preset, opt-in fixes) are tagged `[EXT:...]`.

## Known issues reported by the community

Source: the published notes of the community fix pack HyperJeanJean/TPW-TPI-Fixes
(GitHub). The pack has no licence; nothing from it is copied, only the reported problems
are listed (paraphrased) and answered by OpenTPW's own implementation. Several issues
belong to the original Windows executable and do not exist in a reimplementation.

| Reported issue (original game) | OpenTPW status |
| --- | --- |
| Gate and ride signs show no text; launch problems around the fonts the game unpacks to `Data/tpwfnt` and installs (the original installs them with `AddFontResourceA` and draws with GDI) | **Done.** The 17 TrueType fonts are read in memory from `Data/fonts.wad` by an own parser/rasterizer; nothing is unpacked or installed. Byte-identical to the `tpwfnt` files (pinned SHA-256). Park name drawn on the jungle gate. |
| Movies and level music missing unless copied from the disc | **Done.** `--cd-data <dir>` mounts the extracted CD (or its `Data` folder) as a read-only, case-insensitive fallback; missing movies/music are listed at startup. |
| Anisotropic filtering forced to 16x through a DirectDraw wrapper | **Done** as the Enhanced preset (16x); original presets use their own filter setting. |
| `high.sam` edited for a better look | **Done** as the Enhanced preset (documented values below); the original files are never modified. |
| Resolution forced/limited (`Data/Resolution.sam`, hex-edited executables), instability above 1024x768 | Display slice: free resolutions, HiDPI and upscaling ([UPSCALING-DESIGN.md](UPSCALING-DESIGN.md)). |
| Random crashes tied to graphics settings; DirectDraw problems on modern Windows (wrapper DLLs) | Not applicable: Veldrid renderer (Metal/D3D11/Vulkan). |
| White textures in large parks (texture-memory estimate constant patched in the executable) | Not applicable to the original constant; OpenTPW uploads every texture. Revisit when the original texture budget is reproduced. |
| Sound fixes via a newer QMixer DLL; MIDI SoundFont driver DLL | Not applicable (own audio output, advisor/audio slice). |
| Text library (`usp10.dll`) workaround for missing sign text | Not applicable: no Windows text stack is used. |
| Sim Theme Park (TPI) mangles non-ASCII characters in prebuilt coaster names | Robustness: `UniToMB.dat` reader + codec round-trips every string of all six languages; unrepresentable characters become `?` (APPROX COMPAT-012). TPI itself is not targeted yet. |
| TPI music archives for the second and third park are corrupt | Robustness: a damaged SDT entry is skipped and logged instead of failing the whole bank. All 47 TPW banks parse with no skipped entry. |
| North American bonus content; two TPI bonus rides fixed for non-English versions | Rides slice (`--bonus-data`, sharing the `DataRoots` overlay below). |

Issues found by OpenTPW itself while implementing this slice:

| Finding | Handling |
| --- | --- |
| `levels/space/rides/megacost.wad/megacost.sgn` text slot 1 asks for "EggIt Italic" / `EGGII___.TTF`, which `fonts.wad` does not ship (the only unresolved font of 168 slot references) | Fix `sign-font-substitution`: in-memory data correction keyed by the file's SHA-256 (`E1CCFB45…`) pointing it at `EGGITAOE.TTF`. Off in Original (line not drawn). |
| `UIStrings.UnsentPostcardsWarning` (473) lies past the end of `UITEXT.str` in every language; `Localization.Parse` crashed on it | Shows its internal name, logged once. |
| French `UITEXT.str[457]` (NewVisitor) is empty | Falls back to the English text, logged once. |
| None of the 17 fonts has `€` | Reported as a missing glyph (.notdef), never crashes. |

## In-world TrueType text

Formats and evidence:

- `Data/fonts.wad`: 17 refpack-compressed members (`BIGLA___.TTF` … `YOUNIA__.TTF`), all
  TrueType glyf fonts, unitsPerEm 2048, Windows Unicode cmap (format 4, 307 code points),
  kern format 0. `TrueTypeFont` reads head/hhea/maxp/hmtx/loca/glyf/cmap (+ name, OS/2,
  kern); `TrueTypeRasterizer` flattens quadratic contours (implied on-curve points,
  composite glyphs) and fills with non-zero winding and coverage anti-aliasing.
- `*.sgn` (84 members: gates and sign1 features, ride WADs, `lobby.wad`), read by
  `SignFile`: u32 version (100/101), u32, u8 (extra image present), u32, then two
  436-byte text slots: u32 style id, 64-byte face name, 260-byte TTF file name, two i32
  (85..141, and a vertical offset), a Win32 `LOGFONTA` (height, width, weight, charset 1,
  OUT_TT_PRECIS, ANTIALIASED_QUALITY, face name equal to the slot's), u32, eight floats,
  u32. The rest (two 12-byte headers `16, 128, 4` each followed by 8,192 bytes of 32-bit
  pixels, and for flag 1 an extra block with a `BILZ` compressed image) is kept raw.
- Sign models have texture slots `sign1` (left half) and `sign2` (right half); the shared
  `sign1.wct`/`sign2.wct` are 128x128 placeholders reading "SIGN1"/"SIGN2", i.e. the game
  renders these textures at runtime. The binary imports `CreateFontIndirectA`,
  `AddFontResourceA`, `CreateDIBSection` and `SetTextColor`.
- `global.sam` `ParkName.GateObjectId` names the gate object (1601/2601/3601/4601); its
  `Info.DontApplyOffset 1` makes `gates.MD2` coordinates level coordinates.
- `OBJECT_NAMES.str` starts with ride names split into two entries ("Temple"/"Of Gloom",
  "Sun"/"God"), matching two text slots. The object-to-entry mapping is not established
  (`Info.RideTypeStringIndex` is a ride type shared by all themes, not this index).

Which font the original uses where (slot 0 = first line, slot 1 = second line):

| Sign | Slot 0 font, em height | Slot 1 font, em height |
| --- | --- | --- |
| Jungle gate | Young Itch AOE (`YOUNIA__.TTF`), 144 px, offset -25 | Clunker AOE (`CLUNA___.TTF`), 119 px, offset 97 |
| Halloween gate | Haunt AOE (`HAUNTAOE.TTF`), 112 px, offset 32 | LinusPlay AOE (`LINUPA__.TTF`), 90 px, offset 112 |
| Space gate | Gargamel Smurf AOE (`GARGSA__.TTF`), 180 px, offset -22 | Gargamel Smurf AOE, 137 px, offset 121 |
| Fantasy gate | Big Limbo AOE (`BIGLA___.TTF`), 144 px, offset 15 | Krelesanta AOE (`KRELA___.TTF`), 111 px, offset 128 |
| All 84 signs, slot 0 | Big Limbo 11, Clunker 10, EggIt 5, Gargamel Smurf 11, Haunt 12, Intruder 5, Krelesanta 7, LinusPlay 8, Tannarin 6, Young Itch 9 | |

API for the rides/frontend slices (`SignTextRenderer`): `RenderText(text, font, emPixels)`
→ texture, `CreateQuad(texture, worldHeight)` → quad model, `RenderSign(sgn, lines,
background)` → `sign1`/`sign2` textures, `LoadSignFile(archivePath)` (applies enabled data
corrections). CPU parts (`SignTextLayout`, `SignCanvas`) are GPU-free and tested.
Demonstration: `--load-original-level jungle` draws "Lost Kingdom" in Young Itch AOE on
the gate's `sign1`/`sign2` faces (only those faces; the gate model belongs to the object
renderer). The native smoke test requires non-empty sign text.

## Media fallback (`--cd-data`)

`--cd-data <dir>` (or `OPENTPW_CD_DATA`) accepts an extracted CD root containing `Data`
(any case) or a `Data` folder. `DataRoots` resolves every path segment case-insensitively,
stops at `.wad`/`.sdt` archives, and prefers the install; the overlay only fills gaps.
The game file system consults it for missing files (`OpenRead`, `FileExists`, `GetSize`;
listings stay those of the install) and the movie library searches its `Movies` folder.
At startup the nine movies and five music banks are checked; each missing one is logged
("the movie will be skipped" / "the music will be silent"). Nothing is copied.
`DataRoots`/`DataOverlayRoot` with `DataOverlayRole.Bonus` is the shared abstraction for
the rides slice's `--bonus-data`.

## Detail presets (`IGraphicsSettings`)

Original values ([DATA:Data/low.sam, med.sam, high.sam]):

| Option | Low | Medium | High | Enhanced [EXT] | OpenTPW |
| --- | --- | --- | --- | --- | --- |
| TEXTUREQUALITY | 1 | 2 | 3 | 3 | not applied (full resolution always) |
| TEXTUREFILTERING (0 point, 1 bilinear, 2 trilinear, 3 anisotropic) | 0 | 1 | 2 | 3 (16x) | **applied** to world samplers |
| PROCEDURALTEXTURING | 0 | 2 | 2 | 3 | not applied |
| PROCEDURALTEXTURECACHE | 0 | 1 | 1 | 1 | not applied |
| ANIMATINGTEXTURES | 0 | 1 | 1 | 1 | not applied |
| SKYQUALITY (layers) | 1 | 2 | 4 | 4 | not applied |
| SKYDETAIL / SKYSHADOW | 0/0 | 1/0 | 1/0 | 1/0 | not applied |
| MESHSHADOW / SPRITESHADOW | 0/0 | 0/1 | 1/2 | 1/3 | not applied |
| TRIPLEBUFFER | 0 | 0 | 1 | 1 | swapchain-managed |
| FOG (0 vertex, 1 depth) | 1 | 1 | 1 | 1 | depth fog always |
| MIPMAP | 1 | 1 | 1 | 1 | **applied** |
| RENDER32 / TEXTURE32 | 0/0 | 0/0 | 0/0 | 1/1 | always 32-bit |
| BUMPMAPPING / LIGHTING / FORCELOWALPHAREF | 0/0/0 | 0/1/0 | 0/1/0 | 0/1/0 | not applied |
| FIRSTPERSONVIEWDISTANCE (0..3) | 0 | 1 | 2 | 3 | stored (no first-person view yet) |
| COASTERSMOOTHNESS (1..8) / COASTERTRACKDETAIL | 1/0 | 4/1 | 6/1 | 8/1 | exposed for the rides slice |
| PARTICLEDENSITY / TOTALPARTICLES (simulation) | 500/1000 | 1000/3200 | 1500/2000 | 1500/2000 (2000 with `enhanced-game-options`) | exposed |
| NUMKIDS (0→4, 1→6, 2→8, 3→max; simulation) | 0 | 2 | 2 | 2 (3 with `enhanced-game-options`) | exposed for the guests slice |
| WEATHER / LOBBYOBJECTS | 0/10 | 1/70 | 1/100 | 1/100 | exposed |

The Enhanced preset also stretches OpenTPW's fog distance by 2 ([EXT] view distance; the
original presets keep scale 1). Default preset: High (APPROX COMPAT-013). Settings live in
`graphics.json` next to `display.json`; `--graphics-preset low|medium|high|enhanced`,
`--save-graphics-settings`. Filtering/mipmap changes reach the samplers at the next start
(`RestartRequired`); the view distance applies immediately. Custom presets keep the
simulation options at the original High values unless `enhanced-game-options` is on.
`GraphicsSettingsService.SimulationDeviations()` lists deviating simulation options.

## Compatibility profile

| Profile | Fixes |
| --- | --- |
| Original (default) | none |
| Recommended | presentation-only fixes: `sign-font-substitution` |
| Custom | per fix (`--compat-fix <id>[=on|off]`, `compatibility.json`) |

| Fix id (stable) | Kind | In Recommended | Effect |
| --- | --- | --- | --- |
| `sign-font-substitution` | presentation | yes | hash-keyed in-memory correction of `megacost.sgn` (unshipped font) |
| `enhanced-game-options` | simulation | no | Enhanced/Custom presets may raise NUMKIDS/PARTICLEDENSITY |

`--compat-profile original|recommended|custom`, `--save-compat-settings`. Startup logs the
profile, every active fix, simulation-affecting fixes and every approximation.

Save metadata (the economy slice owns the save format): store
`CompatibilityRuntime.Flags.ToMetadata()`, e.g.
`opentpw-compat/1;profile=Custom;fixes=enhanced-game-options;simulation=enhanced-game-options`,
and read it back with `CompatibilityFlags.ParseMetadata` (unknown ids from newer versions
are kept and treated as simulation-affecting). `IsOriginalSimulation` tells whether a save
was played with original simulation rules.

Data corrections (`DataCorrections`): each entry names a fix id, a data-relative path
(`.wad` archives addressed without extension) and the SHA-256 of the original bytes; it
applies only when the fix is enabled and the hash matches, in memory, never on disk.

## Robustness defaults

- Missing localized strings (`LocalizedStringTable`, used by `Localization`): the language's
  entry, else English, else the internal name; strings blank in every language stay blank;
  each fallback is logged once. Unknown `#Key` markers are shown instead of crashing.
- SDT banks: entries whose offset, header or data size leave the bank are skipped and
  logged (`SdtArchive.SkippedEntries`); lookups of a skipped entry raise `FileNotFoundException`.
- `UniToMB.dat` (`BFUMReader`, `GameTextCodec`): magic `BFUM`, then ranges of (u16 first,
  u16 filler, u16 last, u16 filler, u16 indices); English/French/German hold
  U+0009..U+00FF plus U+0007 and their extra letter (’, œ, š); Danish/Dutch/Swedish
  U+0007 and U+0009..U+00FF. Filler entries map code points the language lacks, so only
  mappings that round-trip through `MBToUni.dat` are used. Every codepage character and
  every string of all 21 tables round-trips in all six languages.

## Tests

`TrueTypeFontTests` (synthetic fonts: metrics, cmap, kerning, exact coverage, non-zero
winding, implied curves, composites, malformed data; original: 17 fonts, file hashes,
glyph metrics and raster hashes, gate line hash), `SignFileTests` (synthetic + all 84
`.sgn`, gate fonts, character coverage of OBJECT_NAMES/THEMENAMES in six languages ×
17 fonts), `CompatibilityTests` (presets, settings, profile, metadata, data corrections,
synthetic minimal install with a CD overlay, missing strings, SDT damage, 47 original
banks), `TextCodecTests` (UniToMB in six languages). Optional cross-check of the WAD fonts
against loose files: `OPENTPW_TPWFNT_PATH=<tpwfnt folder>`.

## Approximation register

| Id | Area | Assumption | Evidence needed |
| --- | --- | --- | --- |
| COMPAT-001 | Sign text | Canvas 512x256 texels (two 256x256 halves for sign1/sign2); original texture size unknown | binary DIB size or a sign texture capture |
| COMPAT-002 | Sign text | Lines centred horizontally and shrunk to fit the width minus 8 texels | original placement / captures of long names |
| COMPAT-003 | Sign text | `.sgn` slot floats 2..4 read as RGB text colour (clamped) | binary use of the floats or a capture |
| COMPAT-004 | Sign text | Flat dark board behind gate text; `.sgn` pixel blocks and BILZ image not decoded | decoding of the `.sgn` remainder |
| COMPAT-005 | Sign text | The 85..141 slot field (read as horizontal scale) is not applied | binary use of the field |
| COMPAT-006 | Sign text | No pair kerning (GDI TextOut default) | binary text-output call site |
| COMPAT-007 | Sign text | Gate shows the THEMENAMES theme name until a save supplies a park name | save park-name field, capture |
| COMPAT-008 | Sign text | Sign faces lifted 0.05 engine units along their normal | none once the gate model draws runtime textures |
| COMPAT-009 | Sign text | 16 sub-scanline unhinted linear coverage instead of GDI ANTIALIASED_QUALITY | captures of original sign text |
| COMPAT-010 | Graphics | TEXTUREFILTERING 0/1/2 → Veldrid point / linear+point-mip / linear+linear-mip; MIPMAP 0 → mip 0 only | binary render states or captures per detail level |
| COMPAT-011 | Localization | Missing string → English → internal name | original behaviour for missing strings |
| COMPAT-012 | Text input | Unrepresentable characters → `?` | binary text-input handling |
| COMPAT-013 | Graphics | High is the default detail preset | installer/registry default |

Extensions by design (not approximations): `[EXT:COMPAT-GFX-ENHANCED]` Enhanced preset
values, `[EXT:COMPAT-GFX-ANISOTROPY]` anisotropy degree, `[EXT:COMPAT-GFX-VIEWDISTANCE]`
view-distance scale, `[EXT:COMPAT-FIX <id>]` each fix. Pre-existing OpenTPW values outside
this slice (e.g. the fog formula in `test.shader`) are not listed here.

## Not done

- Sign texture size, text placement, colour and the `.sgn` pixel/image blocks are not
  decoded; which `OBJECT_NAMES` pair belongs to which ride is unknown, so ride signs are
  not drawn yet (API ready for the rides slice).
- Most detail options have no renderer feature to drive (table above).
- The overlay does not merge directory listings; `GameLanguage` keeps its own language overlay.
- Only TPW (not TPI/Sim Theme Park) data was examined.
