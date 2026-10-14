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
| Gate and ride signs show no text (rides now show their name, see below); launch problems around the fonts the game unpacks to `Data/tpwfnt` and installs (the original installs them with `AddFontResourceA` and draws with GDI) | **Done.** The 17 TrueType fonts are read in memory from `Data/fonts.wad` by an own parser/rasterizer; nothing is unpacked or installed. Byte-identical to the `tpwfnt` files (pinned SHA-256). Park name drawn on the jungle gate. |
| Movies and level music missing unless copied from the disc | **Done.** `--cd-data <dir>` mounts the extracted CD (or its `Data` folder) as a read-only, case-insensitive fallback; missing movies/music are listed at startup. |
| Anisotropic filtering forced to 16x through a DirectDraw wrapper | **Done** as the Enhanced preset (16x); original presets use their own filter setting. |
| `high.sam` edited for a better look | **Done** as the Enhanced preset (documented values below); the original files are never modified. |
| Resolution forced/limited (`Data/Resolution.sam`, hex-edited executables), instability above 1024x768 | Display slice: free resolutions, HiDPI and upscaling ([UPSCALING-DESIGN.md](UPSCALING-DESIGN.md)). |
| Random crashes tied to graphics settings; DirectDraw problems on modern Windows (wrapper DLLs) | Not applicable: Veldrid renderer (Metal/D3D11/Vulkan). |
| White textures in large parks (texture-memory estimate constant patched in the executable) | Not applicable to the original constant; OpenTPW uploads every texture. Revisit when the original texture budget is reproduced. |
| Sound fixes via a newer QMixer DLL; MIDI SoundFont driver DLL | Not applicable (own audio output, advisor/audio slice). |
| Text library (`usp10.dll`) workaround for missing sign text | Not applicable: no Windows text stack is used. |
| Sim Theme Park (TPI) mangles non-ASCII characters in prebuilt coaster names | Robustness: `UniToMB.dat` reader + codec round-trips every string of all six languages; unrepresentable characters become `?` (`[EXT:text-encode-helper]`: tp.exe never names the codec, see below). TPI itself is not targeted yet. |
| TPI music archives for the second and third park are corrupt | Robustness: a damaged SDT entry is skipped and logged instead of failing the whole bank. All 47 TPW banks parse with no skipped entry. |
| North American bonus content; two TPI bonus rides fixed for non-English versions | Rides slice (`--bonus-data`, sharing the `DataRoots` overlay below). |

Issues found by OpenTPW itself while implementing this slice:

| Finding | Handling |
| --- | --- |
| `levels/space/rides/megacost.wad/megacost.sgn` text slot 1 asks for "EggIt Italic" / `EGGII___.TTF`, which `fonts.wad` does not ship (the only unresolved font of 168 slot references) | Fix `sign-font-substitution`: in-memory data correction keyed by the file's SHA-256 (`E1CCFB45…`) pointing it at `EGGITAOE.TTF`. Off in Original (line not drawn). |
| The old UI enumeration mapped `UnsentPostcardsWarning` to 473 instead of 472 | The frontend correction maps all IDs to shipped entries; the internal-name fallback remains for genuinely absent entries. |
| French `UITEXT.str[457]` (CashDollar), and French/German `[448]` (Dollar), are empty | The generic fallback wrapper reports English fallback; the original-style UI preserves blank currency prefixes. Verified with all six language tables. |
| None of the 17 fonts has `€` | Reported as a missing glyph (.notdef), never crashes. |

## In-world TrueType text

Formats and evidence:

- `Data/fonts.wad`: 17 refpack-compressed members (`BIGLA___.TTF` … `YOUNIA__.TTF`), all
  TrueType glyf fonts, unitsPerEm 2048, Windows Unicode cmap (format 4, 307 code points),
  kern format 0. `TrueTypeFont` reads head/hhea/maxp/hmtx/loca/glyf/cmap (+ name, OS/2,
  kern); `TrueTypeRasterizer` flattens quadratic contours (implied on-curve points,
  composite glyphs) and fills with non-zero winding and coverage anti-aliasing.
- `*.sgn` (84 members: gates and sign1 features, ride WADs, `lobby.wad`), read by
  `SignFile`: the identified Mac reader proves a 17-byte packed header (version,
  flag, extra-image byte, two style selectors), then two 392-byte font / 44-byte
  effect pairs. Fonts store a 64-byte face, 260-byte TTF name, two i32 fields and
  60-byte `LOGFONTA`. Effects store a mask word, eight floats and two stored bounds
  words; the native compositor overwrites those bounds from measured text. Float
  parameters 2..4 are shared base, diffuse and white-specular coefficients, not RGB.
  Nonzero selectors add 20-byte paint records. Two native bitmap headers store
  width, height and bytes per pixel; all 84 files have two 16x128x4 source images
  (8,192 bytes each). The 23 extra images are version-101 wavelet payloads: four
  float dequantization scales and two BILZ chunks are decoded to native ARGB by
  `SignWaveletDecoder`. Unknown styles and image formats are preserved with diagnostics; invalid
  spans and excessive allocation products are rejected. Corrected 17-byte font offsets and the existing paint API remain compatible
  with hash-keyed corrections; raw effect words remain available. Use
  `Effects`, `Paints` and `SourceImages` for native metadata. See
  [the native sign evidence](reverse/PPC-ui.md#native-sign-records-and-surface-inputs).
  The wavelet fourth channel is native alpha; the two 16x128 images supply relief
  text RGB, with coverage taken from the generated glyph mask. An absent extra image
  (for example Belly Bounce) therefore does not establish an invisible original board.
  OpenTPW puts an opaque contrast-selected backing under each face (COMPAT-030) while
  the downstream source of original board coverage remains untraced.
  The gate's stored paint is black over a dark decoded stone board; OpenTPW uses
  light stone text and upward-facing sign normals there for legibility (`[EXT:gate-paint-legibility]`,
  an OpenTPW presentation decision), pending original-platform pixels.
- Sign models have texture slots `sign1` (left half) and `sign2` (right half); the shared
  `sign1.wct`/`sign2.wct` are 128x128 placeholders reading "SIGN1"/"SIGN2", i.e. the game
  renders these textures at runtime. The binary imports `CreateFontIndirectA`,
  `AddFontResourceA`, `CreateDIBSection` and `SetTextColor`.
- `global.sam` `ParkName.GateObjectId` names the gate object (1601/2601/3601/4601); its
  `Info.DontApplyOffset 1` makes `gates.MD2` coordinates level coordinates.
- `OBJECT_NAMES.str` starts with ride names split into two entries ("Temple"/"Of Gloom",
  "Sun"/"God"). Which text a sign gets is established from the Mac binary: the object
  loader passes only the id of the object's info record, and the sign builder
  (`[BIN:STP-PPC:0x1018E254]`) takes that record's single name string (the OBJECT_NAMES entries joined
  by a space, `0x1011A08C`; a first entry ending in '-' is joined without space and dash) and
  splits it at the space nearest the middle: probing `len/2`, `len/2-1`, `len/2+1`, ... for U+0020,
  characters before it are text slot 0, after it text slot 1, no space gives one line. The
  entry boundary and this rule agree for the ride names. Material names are matched as
  `sign1.tga`/`sign2.tga`; the builder also accepts two explicit texts. Only rides ship an
  `.sgn` (shops, sideshows and features have none and no sign faces); the original first
  looks for `<archive>:<theme>_<object>.sgn` and then `<archive>:<object>.sgn`, the shipped
  archives only contain the second form.
- `ObjectSigns` (`World/Objects`) composes the sign per archive and text (cached): the object
  renderer (`OriginalObject`) binds the rendered halves to the `sign1`/`sign2` slots, and
  the buy-window preview (`PreviewIcon`) draws the same textures. The gate binds its
  composed park-name halves to the fixed model and its raised sign faces. The name is the
  current language's display name (approximation COMPAT-016); characters outside a font's
  cmap are drawn as .notdef with a diagnostic. The native compositor still layers the
  material effects for the three signs (`hallow/c_scat`, `fantasy/jelly`, `space/zob`)
  whose paint styles are both zero.

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
corrections). `background` remains for API compatibility; the ride-sign compositor clears
to transparent. `SignSurfaceComposer` decodes and alpha-blits the optional 256x128 board,
layers paint and the tiled 16x128 material strips in native order, then returns two
128x128 textures. CPU parts (`SignTextLayout`, `SignSurfaceComposer`) are GPU-free and tested.
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
original presets keep scale 1). Default preset: the original's per-machine choice (traced: Low below 65 MB or 301 MHz, Medium below 192 MB or 450 MHz, otherwise High), with the processor clock taken as at least 450 MHz (`[EXT:graphics-preset-cpu]`). Settings live in
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
| COMPAT-001 | Sign text | Ride signs use two 128x128 destinations and a 512-wide text raster reduced to a 256-wide mask; lower DIB rows and GPU UV orientation remain unverified | native texture pixels and GPU UV consumer |
| COMPAT-002 | Sign text | lfWidth applied as a horizontal scale with the Win32 rule (average character width = lfWidth) and fractional advances; the width search itself is traced (`0x100A9F9C`: no margin, height unchanged, bisection over lfWidth) | captures of long names (Mac GDI width rounding) |
| COMPAT-003 | Sign text | Ride signs apply paint RGBA, material strips and coefficients with native integer layer blending; GDI coverage, morphology, Gaussian filtering, source-height resampling and float rounding remain approximate | native masks and original-platform pixels |
| COMPAT-004 | Sign text | Gate signs now compose their decoded .sgn boards; gate-specific pixels have not been compared with the original | original gate capture and board/text placement comparison |
| COMPAT-006 | Sign text | No pair kerning: tp.exe has no TextOut/DrawText/ExtTextOut imports, sign text goes through Uniscribe (`0x5EAB70`: ScriptStringAnalyse flag 0x80, ScriptStringGetLogicalWidths, the percent scaling, a second ScriptStringAnalyse with the game's advance array `0xF85F00`, ScriptStringOut) and no kerning API is called | whether usp10's natural widths include kerning |
| COMPAT-007 | Sign text | Gate shows the THEMENAMES theme name until a save supplies a park name | save park-name field, capture |
| COMPAT-009 | Sign text | 16 sub-scanline unhinted linear coverage instead of GDI ANTIALIASED_QUALITY | captures of original sign text |
| COMPAT-016 | Sign text | Ride sign text = the object's display name (OBJECT_NAMES entries bound by English name, else the .sam name), split at the middle space | record field selecting the name entries; capture of a shop/sideshow sign |
| COMPAT-018 | Sign text | Ride boards are decoded and alpha-blitted; exact wavelet pixels, GPU row orientation and original-platform appearance are not yet certified | native bitmap pixel oracle and ride-sign capture |
| COMPAT-030 | Sign text | Opaque dark or cream backing under every sign face; native compositor clears transparent, but downstream board coverage is untraced | original sign material/render path and original-platform gate and ride captures |
| COMPAT-010 | Graphics | TEXTUREFILTERING 0/1/2 → Veldrid point / linear+point-mip / linear+linear-mip; MIPMAP 0 → mip 0 only | binary render states or captures per detail level |
| COMPAT-014 | Theme Park Inc textures | FSH palette 0x24 has no alpha and decodes opaque; no colour key (see [FSH.md](FSH.md)) | Theme Park Inc texture upload code or captures |
| COMPAT-015 | Theme Park Inc textures | FSH palette 0x2D (one file) read as A1R5G5B5, bit 15 = opaque | Theme Park Inc palette conversion code or a capture |
| COMPAT-019 | Theme Park Inc lake | Theme Park Inc's lake is mission obstacle 1703 (`lake.MD2`, drained by a gardener's Water Drainage in mission 1) with the drained state 1704 (`dlake.MD2`); the viewer always draws `dlake.MD2` and ignores the obstacle state and the `idle`/`lakem` animations | Theme Park Inc's obstacle code or a capture ([THEME-PARK-INC.md](THEME-PARK-INC.md)) |

Extensions by design (not approximations): `[EXT:COMPAT-GFX-ENHANCED]` Enhanced preset
values, `[EXT:COMPAT-GFX-ANISOTROPY]` anisotropy degree, `[EXT:COMPAT-GFX-VIEWDISTANCE]`
view-distance scale, `[EXT:COMPAT-FIX <id>]` each fix. Pre-existing OpenTPW values outside
this slice (e.g. the fog formula in `test.shader`) are not listed here.

October 14, 2026 (tp.exe): COMPAT-005 is retired: the `.sgn` slot field at file offset +324 (memory slot base + 8,
loader `0x5EC3A0`) scales each character's width as width × percent / 100, truncated (`0x5EAD01`–`0x5EAD2C` and in the
font-size fit loop); `SignTextLayout.ScaledAdvance` applies it (the original's integer natural widths are not
reproduced, COMPAT-002; at 100 percent the advance is left as it is). The baseline hashes of the 71 of 75 composed signs whose percent is not 100 were re-pinned for it.
COMPAT-017's calls are traced: the object text's NAME/SIGNA/SIGNB are parsed at `0x413600`, SIGNA/SIGNB are copied into each placed
object as two 32-character lines (`0x413020`/`0x4130B0` at `0x4DB6F9`/`0x4DB70D`), and the sign rebuild `0x468500` (types with flag 0x8000000)
reads line 1 through `0x4E2860`, which strips a trailing "--", and line 2 through `0x4E2920`; `BonusNames` now does both.
October 14, 2026, second round (tp.exe and Theme Park Inc data): COMPAT-017 is retired with a code change. The sign lines come from
the selected language's file only (no English fallback); a file without SIGNA or SIGNB shows "MISSING STRING", which the original sets
for NAME, SIGNA, SIGNB and +0x500 before it parses (`0x413600`, `0x41360D`–`0x41361D`); without a language file the definition's text
pointers stay NULL (`0x412C65`; the sign code's handling of a NULL line is untraced and OpenTPW draws the display name); the "both empty, keep the .sam name" rule is gone. The line-1 fallback `0x78A374` is a
never-written .bss buffer, the empty string, so an empty line 1 stays empty (`0x4E2860`). Both getters (`0x413020`, `0x4130B0`) read the
OBJECT_NAMES row (table `0x7488D8`, +4 and +8) when the id is listed, and otherwise def+0x4F8 (SIGNA) and def+0x4FC (SIGNB).
COMPAT-011 (the localized-string fallback order), COMPAT-012 (the UniToMB encode rule: the original converts wide text with
`WideCharToMultiByte`, `0x424DB0`, and `Encode` has no runtime caller) and COMPAT-031 (the gate paint) are OpenTPW decisions or helpers now:
`[EXT:locale-fallback]`, `[EXT:text-encode-helper]` and `[EXT:gate-paint-legibility]`. The original opens one language folder
(`Data\Language\<selected>`, `0x419710`) and has no English-then-identifier chain; the missing-id result of the table lookup (`0x6ACB70`) is untraced.
COMPAT-019 is narrowed: Theme Park Inc's `Data/Objects.txt` lists 1703 "New Science Lake Obstacle" and 1704 "New Science Drained Lake
Obstacle", `levels/science/obstacles/` holds `lake.wad` and `dlake.wad`, `terrain.wad` holds dlake, dlakem, lake, lakem1 and lakem2, and
`missions/miss01.sam` sets `Obstacle.Type 1703 # lake`, cleared by one gardener with ability 10 ("Water Drainage"); so "dlake" is the
drained lake, not a finished one (the viewer's drawing is unchanged). COMPAT-006 (kerning) stays open but its answer is in the shipped
usp10.dll: tp.exe imports only ScriptStringAnalyse, ScriptStringGetLogicalWidths, ScriptStringOut, ScriptString_pSize and ScriptStringFree
from USP10 and no kerning or ABC-width call from GDI32. COMPAT-001, -003, -004 and -018 need pixel captures; COMPAT-014 and -015 need
Theme Park Inc's own FSH loader.
COMPAT-008 and COMPAT-013 are OpenTPW design decisions: `[EXT:gate-sign-depth-lift]` (the gate sign faces' depth lift) and
`[EXT:graphics-preset-cpu]` (the processor clock taken as 450 MHz or faster).

## Not done

- Ride signs now compose optional decoded boards, native-order paint and relief layers,
  and two 128x128 destinations. GDI text coverage, nonzero mask morphology, Gaussian
  filtering and original-platform pixel parity remain open (COMPAT-001, COMPAT-003,
  COMPAT-016, COMPAT-018).
- Most detail options have no renderer feature to drive (table above).
- The overlay does not merge directory listings; `GameLanguage` keeps its own language overlay.
- Only TPW (not TPI/Sim Theme Park) data was examined.
