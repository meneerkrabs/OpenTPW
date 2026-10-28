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
  composite glyphs) and fills with non-zero winding; coverage is the GDI grayscale model of COMPAT-003 (see "Sign text antialias model" below).
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
  The composed surface starts transparent, as in the native compositor (`0x100AC21C`): a PC v2.0
  video shows wood and sky between the letters of Belly Bounce, Azteekse Tekens and Wilde Aap, so
  the faces are drawn with alpha and there is no backing (COMPAT-030 retired). Paint masks are
  shifted by the record's offsets, which gives Wilde Aap's drop shadow.
  The gate shows the park name on two lines with the stored paint, a 60 % black shadow offset
  by (-1, 1) under dark relief stone text, as in the video; the earlier cream paint
  (`gate-paint-legibility`, formerly COMPAT-031) was contradicted by it and is gone, and so are
  the upward-facing sign normals it came with: with them the relief text rendered white. With the
  overlay's normals converted to engine axes like its positions (model X, Y, Z become X, Z, Y) the
  gate's front face is dark stone with grey-blue relief letters and a black shadow, as in the video;
  the back of the gate shows no text in a capture from behind.
  The park name is the gate's two sign lines (`ParkNameLines`), as tp.exe keeps them on the
  placed gate: they start as the first and second OBJECT_NAMES text of the gate's Info.Id row
  (`0x4DB6F9`; jungle "LOST" / "Kingdom", Dutch "Verloren" / "Koninkrijk"), the park name button
  of the entry price window opens the rename dialog with two 14-character fields (`0x4990F0`),
  OK stores them and redraws the gate (`0x4993E0`), and the world save carries them in its
  optional `ParkName` part (at most 32 characters a line, no control characters; older saves
  start with the default lines and draw them as the rebuild after loading does, `0x468500`).
  The composed halves are straight alpha: the layers blend into a surface that started at ARGB 0, so
  partial-alpha texels held colour already scaled by their alpha, which the engine's alpha blend
  would scale again (dark fringes, a shadow far too dark); the colour is divided back and
  transparent texels take their neighbours' colour so mip levels do not average towards black
  (`[EXT:sign-straight-alpha]`; the native consumer of the texels is untraced).
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
  object's info-record name in the current language (`SignName`: the OBJECT_NAMES entries of
  its Info.Id row, empty for an id outside tp.exe's table `0x7488D8`, which composes the
  board alone, as `0x467A80` does); characters outside a font's
  cmap are drawn as .notdef with a diagnostic. The native compositor still layers the
  material effects for the three signs (`hallow/c_scat`, `fantasy/jelly`, `space/zob`)
  whose paint styles are both zero.
- The lobby's island and gate models are built with two explicit lines, the theme gate's OBJECT_NAMES
  row (`0x409B10` per theme, `0x5DFA60` passes them to both models), so the four lobby sign models
  (`Jun_isle`, `Hal_isle`, `Fan_gate`, `Spa_gate`) show the lines a new park's gate starts with, in the
  player's language (English "LOST"/"Kingdom", "HALLOWEEN"/"World", "WONDER"/"Land", "SPACE"/"Zone"); `LobbySigns` draws them with the model's own
  `lobby/terrain/<model>.sgn`. No track piece, vehicle, coaster car, pylon or tour car ships a sign
  face (`LobbySignCorpusTests`).

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

### Sign text antialias model (COMPAT-003)

`[EXT:sign-text-antialias]` (16 linear sub-scanlines) is gone; the sign text coverage is `TrueTypeRasterizer` with `CoverageModel.GdiGray4`
(October 17, 2026). Each pixel takes 4x4 point samples (sample centres at (k + 0.5) / 4 in both directions, spans half-open, non-zero
winding), the count 0..16 is the level, and the byte written into the text DIB is `(level * 255 + 8) / 16`. `CoverageModel.Linear` (the
old exact coverage) stays available and is what the scanline-window tests pin. The model is deterministic and never reads the host's fonts.

What the sources document (Windows 2000/XP era GDI is the choice: the game shipped in 1999/2000 and its Redist carries DirectX 6.1):

- **Levels:** GetGlyphOutline returns grayscale bitmaps as GGO_GRAY2/4/8_BITMAP with 5, 17 and 65 levels (value ranges 0-4, 0-16, 0-64; "n^2+1 levels of gray").
  https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getglyphoutlinea The page states level counts only, no sampling positions, no byte mapping and no link to
  lfQuality. **Inferred (not documented):** that those levels are n x n point samples, and that ANTIALIASED_QUALITY text uses the 17-level (4x4) form; the 4x4 factor is this model's inference.
- **Method and hinting:** Microsoft's font blog describes font smoothing (the Windows 2000 default) as applying the hints (grid-fitting) to the outline, overscaling it, then downsampling the
  bitmap; it gives no overscale factor. https://learn.microsoft.com/en-us/archive/blogs/fontblog/the-gasp-table The GetGlyphOutline page separately says the returned outline is
  "for a grid-fitted glyph" (same URL as under Levels).
- **gasp:** version 0 knows GRIDFIT (0x1) and DOGRAY (0x2) per rangeMaxPPEM; "if font smoothing is enabled, then the first two bit flags are used", and the table is
  absent-tolerant (the rasterizer applies its defaults, which the specification does not list). https://learn.microsoft.com/en-us/typography/opentype/spec/gasp
  LOGFONT says ANTIALIASED_QUALITY applies "if the font supports it and the size of the font is not too small or too large" without numbers.
  https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-logfonta
- **Gamma:** SPI_GETFONTSMOOTHINGCONTRAST (default 1400) is documented for ClearType, not for grayscale smoothing, and Windows 2000 does not support it;
  no grayscale gamma is documented, so none is applied. https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfoa
- **Windows 98 differs (display requirement only):** the archived FontQuality page says that on Windows 95 Plus!/98/Me antialiased text needs a display above 8-bit colour (a single-plane,
  non-palette device) and a TrueType font selected into a screen DC before use in a DIB section, or no antialiasing occurs. That is a condition on the display and the DC, not on the DIB depth.
  FreeType's table lists its grayscale / bilevel interpreter v35 (MS rasterizer 1.7) for "GDI (Win 98/2000)", so it does not separate those two versions, and v35 honours only gasp
  version 0 bits (the table has none here). https://learn.microsoft.com/en-us/previous-versions/windows/desktop/bb322856(v=vs.85) and
  https://freetype.org/freetype2/docs/reference/ft2-properties.html (FreeType warns the mapping may not be exact).
  Choosing the Windows 2000/XP behaviour is an inference, not a documented fact: the game's 8-bit DIB drawing smoothed text (the 2x2 reduction `0x5EAFC3` presumes gray levels) shows that
  smoothing happened on the machine that produced the data, which the 98 condition allows at a high-colour display mode as well.
- **Not used:** the Mac PowerPC build's QuickDraw smoothing (16 gray levels from 4x4 supersampling, Apple TN1149) and the PS2 build's baked signs; neither settles the PC result.

What the 17 shipped fonts say (read from `fonts.wad`; the signs draw them at 65 to 256 ppem, the 2x text DIB):

- No font has a `gasp` table, so no per-ppem switch exists: smoothing is on at every sign size (also below 9 or above 255 ppem, where the model has no cutoff:
  the lower and upper limits of "too small or too large" are undocumented; the signs never reach them).
- All 17 are hinted: each has `fpgm` (20 bytes), `prep` (62 to 172 bytes, a CVT cut-in table by ppem), `cvt ` and glyph programs. `prep` also issues
  `SCANCTRL 0x1FF` and `SCANTYPE 1` (dropout control on at every size up to 255 ppem). OpenTPW has no TrueType bytecode interpreter, so the outlines are sampled unhinted
  and the dropout rule is not applied; at 65 to 256 ppem (32 to 128 px in the final mask) the strokes are many samples wide and only sub-quarter-pixel tips can drop out.
- Estimated effect of the missing hinting (not measured): grid-fitting moves a stem edge by at most about half a pixel of the 2x text DIB, i.e. about a quarter of a mask pixel after the 2x2
  reduction, and at 65 to 256 ppem that is 0.2 to 0.8 percent of the em; stems keep their shape. A TrueType bytecode interpreter (fpgm/prep/cvt, about 200 opcodes, the GDI v35 rounding and
  dropout rules) is not worth building without a GDI capture to check it against: it could not be shown to match, and the licence rule applies (no FreeType code is copied; a clean-room
  implementation from the public TrueType instruction-set specification would be needed).
- `hdmx` covers 9 to 24 ppem only, outside the sizes the signs use.

Still open (kept in `APPROX:COMPAT-003`): that GDI really samples 4x4 (Microsoft states no factor for ANTIALIASED_QUALITY; 4x4 is the GGO_GRAY4 form), the sample positions and
edge rule, the hinted outline, the dropout control, the level to byte mapping on the 8-bit DIB (palette and nearest-colour matching) and the Windows version the game ran on. Evidence
needed: a captured text DIB or the GDI rasterizer of the Windows version the game ran on. Measured effect against the previous coverage on 40 corpus masks: total ink within 0.2 percent, at most 22 levels difference per mask byte, no thin stroke lost.

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
| PROCEDURALTEXTURING (0 off, 1 16, 2 32, 3 64 texels) | 0 | 2 | 2 | 3 | **applied** at the next level load: each visible terrain cell gets its own N × N texture from the theme's `base.lnd` (docs/LND.md), and the skirt cells of placed rides and shops get a composite of their floor art over that texture (docs/OBJECTS.md, Floors); the lobby stays static |
| PROCEDURALTEXTURECACHE (0, 1, 2 → 1500, 2000, 2500 cells) | 0 | 1 | 1 | 2 | **applied** at the next level load: the size of the visible-cell cache |
| ANIMATINGTEXTURES | 0 | 1 | 1 | 1 | **applied** to the object texture-frame tracks (0 = each slot shows its first frame, tp.exe `0x57A420`) |
| SKYQUALITY (layers) | 1 | 2 | 4 | 4 | **applied** to the sky's scrolling cloud layers (0 draws no sky) |
| SKYDETAIL / SKYSHADOW | 0/0 | 1/0 | 1/0 | 1/0 | not applied |
| MESHSHADOW / SPRITESHADOW | 0/0 | 0/1 | 1/2 | 1/3 | not applied |
| TRIPLEBUFFER | 0 | 0 | 1 | 1 | swapchain-managed |
| FOG (0 vertex, 1 depth) | 1 | 1 | 1 | 1 | depth fog always |
| MIPMAP | 1 | 1 | 1 | 1 | **applied** |
| RENDER32 / TEXTURE32 | 0/0 | 0/0 | 0/0 | 1/1 | always 32-bit |
| BUMPMAPPING / LIGHTING / FORCELOWALPHAREF | 0/0/0 | 0/1/0 | 0/1/0 | 0/1/0 | BUMPMAPPING and FORCELOWALPHAREF not applied; LIGHTING **applied** at the next level load, to the terrain: 0 draws it unlit in the constant white of tp.exe `0x57453A` (no sun, ambient or point light) and without the darkening of the land outside the park (docs/PATHS.md); the evening extension still darkens that white at dusk and night |
| FIRSTPERSONVIEWDISTANCE (0..3) | 0 | 1 | 2 | 3 | **applied**: the ray distance of the visible-cell spans (200, 325, 450, 700 original units) and, in the ride and free walk views only (the original has fog only in its first-person views, none in the park overview, the lobby or the globe), the fog's start and end (50-60, 100-110, 135-150, 230-250), linear in the projection's depth (docs/LND.md "Fog and far plane", UI-107); also sets the extension's wide-zoom far end |
| COASTERSMOOTHNESS (1..8) / COASTERTRACKDETAIL | 1/0 | 4/1 | 6/1 | 8/1 | exposed for the rides slice |
| PARTICLEDENSITY / TOTALPARTICLES (simulation) | 500/1000 | 1000/3200 | 1500/2000 | 1500/2000 (2000 with `enhanced-game-options`) | density applied to the particle pool at park start; TOTALPARTICLES unread, as in tp.exe |
| NUMKIDS (0→4, 1→6, 2→8, 3→max; simulation) | 0 | 2 | 2 | 2 (3 with `enhanced-game-options`) | exposed for the guests slice |
| WEATHER | 0 | 1 | 1 | 1 | exposed |
| LOBBYOBJECTS (%) | 10 | 70 | 100 | 100 | **applied** to the share of each lobby flying-mesh group that is spawned (`0x5d9db0`, at least 1) |

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

## Frame-driven timing (`[EXT:frame-driven-at-60hz]`)

tp.exe's main loop (`0x54e360`) has no frame limiter: it calls no `Sleep` and no timer wait. `GetTickCount` only drives the fixed 31 ms
simulation substeps (`0x54f4c4`, a catch-up of at most 2 s in a park and 500 ms in the lobby, `0x54e768`); after them it draws one frame, and
the UI sends message `0x1e` to every window once per drawn frame (`0x48a580` → `0x658f6c` → `0x658f97`, which also recurses over the child windows
through `0x658d9f`). Whatever the original steps from `0x1e`, or from the per-frame lobby update (`0x5d5c50`, at `0x54e73a`), therefore ran at the
speed of the PC's frame rate. PCGamingWiki's page for Theme Park World reports that the game "runs too fast and becomes unplayable" at high frame
rates and advises limiting the frame rate to 60 FPS. OpenTPW steps each frame-driven original behaviour at a fixed 60 Hz reference rate
(`ReferenceFrameClock`), whatever its own frame rate; this is presentation only, so it never moves the determinism pin. This replaces the
guessed rates of `APPROX:UI-066` (research fluids, 30 ticks a second) and `APPROX:UI-940` (lobby effects, 30 ticks a second), both retired, and
the fixed-step part of `APPROX:UI-941` (the islands stepped at 30 a second while the flyers ran per frame).

What steps per rendered frame in tp.exe, and what OpenTPW does (READ at instruction level unless noted):

| Behaviour | tp.exe | OpenTPW |
| --- | --- | --- |
| Research fluids (64-frame bubbling, fill and drain) | window proc `0x4a9fc0`, message `0x1e`: position += 2 × speed (16.16) | `ResearchFluid.StepFrames` at 60 Hz (built) |
| Lobby island effects: sky ease, rain count, lightning, island one-shots | islands component vf2 `0x5e0470` (vtable `0x702cb0`, slot 2), called by `0x5d5c50` once per drawn frame; the 31 ms substep of the lobby (`0x54e789`) runs only `0x520130` | `LobbyEffects` at 60 Hz (built) |
| Lobby flying meshes and island clip picks | flying-mesh vf2 `0x5da3a0`, then `0x5e0470` → `0x5e11d0`, in registration order, one frame | `LobbyScene.StepFrame` per reference frame, flyers before islands (built, also for the online city lobby's islands); clips and wings animate by real time |
| Lobby camera: spin, glide shares 0.1 and 0.2, enter sequence, fade eighth | `0x5e0470` (state machine `0x5e0490`), `0x5e1210`, `0x5d8cf2`, step = frame ms (at most 500) × 0.01 | `LobbyCameraMode` per reference frame (built) |
| Rain and snow: per-flake jitter and the bolts' flicker | `CRain` draw `0x57f490`, redrawn on every draw, paused or not | redrawn per reference frame (built); the scroll and bolt age stay by real time |
| Park camera: mouse rotation | `0x42a760` message `0x1e` while the middle-button mode bit `0x790aac & 2` is set: yaw += (X × 2048 / width − 1024) / 2048 per frame (up to ±0.5 rad a frame) | built (`ParkCameraMode`): the middle button pressed over the free view turns the yaw by X / width − 0.5 radians per 60 Hz reference frame, the sign of the Left arrow's turn; the arrow keys keep OpenTPW's own tuning |
| Pause-menu line hover ramp | `0x48b2a0` → `0x48b220`: message `0x10001` (read as the pointer entering) starts eight frames from grey 0x21 to white in steps of 0x20, then idle; `0x10002` (leaving) restores (0, 175, 190) | built (`MenuLineRamp`); OpenTPW's focus stands for the pointer entering (`[EXT:pause-menu-hover-focus]`) |
| HUD balance text and sign | `0x4a0db0`: every third message `0x1e` calls `0x4a0ab0`, which redraws only when the balance changed | live every frame, up to 50 ms earlier at 60 Hz (noted, not built) |
| Edge scroll of the view built by `0x5f0b40` (from `0x4a0840`) | `0x5f1130` / `0x5f18d0`: (pointer − 0.99) × 250 and (0.01 − pointer) × 250 view units a frame in the 1 % edge band (`0x5f35b0`) | built in the map window (`ParkHud.Map.cs`): with the Pushscroll option the centre moves by those cells per 60 Hz reference frame, clamped to the map; the Right button option (`0x78d910` set) turns it off |
| Online globe | `0x5d5c50` → `0x5dc860` once per frame (not a Ghidra function; called through a vtable) | continuous time at 30 ticks a second (`[EXT:online-globe-tick]`), unchanged |
| 3-frame delay before a deferred action | `0x4a9350`: `0x7cc150` counts down once per frame from 3 (set at `0x4a9413`), then `0x4a9180` runs | not needed: no blocking step to hide |

Message `0x1e` handlers that only re-read state, so any frame rate gives the same result: research effort gauge `0x4a96b0`, HUD counters `0x4a0e30`
(`0x4a0fb0`), locator `0x4b3790`, employee window `0x4b5cb0`, text refresh `0x4bc660`, chat poll `0x4c56e0` → `0x4c4bb0` and the list forwarding
`0x665c35` → `0x6650d4`.

Not frame-driven (real time or substep, nothing to match): the timer list polled by `0x6622d2` / `0x661fe5` (elapsed milliseconds with catch-up: the arm drawer
`0x4a14f0`, 20 ms steps), scripted window moves `0x65e9a4` (through the default window proc `0x65f6d1` → `0x65c357`) and press timers `0x668dd9` (milliseconds), the preview-object windows
`0x4af600`, `0x4b0b30`, `0x4b2150`, `0x497640` and `0x4b7720` (message `0x1e` → `0x4864f0` / `0x486610` → `0x468e50`, whose turn is per millisecond), the park camera `0x42b1c0` (frame milliseconds clamped
to 1..200), scrolling textures `0x435a30` and thing animation clocks `0x557ab0` (clock reads), the render interpolation fractions `0x519060`, `0x55ce60`,
`0x518f90` (time since the last substep), and every simulation step on the 31 ms substep
(`0x520130`, `0x546c80`, `0x5516b0`, `0x55abf0`, `0x475360`, `0x516380`, `0x55a470`, `0x55ab50`).

## World geometry translucency

The terrain meshes and placed objects follow the original's translucent pass: faces whose texture has an alpha plane with a non-zero alpha byte (the river water, the harbour's glass roofs, leaves) are drawn after all
other geometry, farthest first, and tested at 0x10/255; cut-out textures (alpha byte 0) are tested at 0xF0/255; every consumer of the world shader shares this path (terrain, heightfield, paths, objects, coaster, track, tour, pool parts and the lobby). The classification reads the texture's own header, so the low-detail copies (no alpha plane) stay opaque, as in
the original ([MD2-MODELS.md](MD2-MODELS.md), "The translucent pass of world models"). This answers the community report of a multicoloured river and a glass roof that hid what stood behind it (issue 829).
Open: the original's deferred particle batches are not merged into the sorted list (PTCL-905); the reverse sort 0x5828F0 and the
alpha-reference index of devices that fail the 0x567270 probe are not reproduced.

## Animated terrain water

In tp.exe the terrain instance plays its `base` clip every rendered frame, in the lobby and in a park: the lobby's `Basem1.MD2` scrolls the two sea meshes one texture repeat per 200 ticks, and the Jungle
terrain's `basem.MD2` (eleven UV tracks, 100 ticks, +1 U per cycle) scrolls the river and the waterfall (a render callback, `0x54e2b0` → `0x44e510`, runs it in a park). The clock is the game's scaled clock, so
the water stands still while the park is paused and follows the game speed. OpenTPW plays both on the scene clock ([MD2-MODELS.md](MD2-MODELS.md), "Terrain clips"; issue 829). The harbour sea is static in
the data and the other two park themes have no terrain clip.

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
| COMPAT-002 | Sign text | lfWidth applied as a horizontal scale with the Win32 rule (average character width = lfWidth); the width search itself is traced (`0x100A9F9C`: no margin, height unchanged, bisection over lfWidth). The Mac GDI layer's width rounding is not reproduced | captures of long names (Mac GDI width rounding) |
| COMPAT-003 | Sign text | Ride signs apply paint RGBA, material strips and coefficients with native integer layer blending. Each paint mask is shaped as `0x5EBF10` does: offset copy, erosion or dilation by the mask word (+4), separable Gaussian by the parameter (+8) (Wilde Aap's drop shadow, the gate's shadow, Azteekse Tekens' outline). Each relief is shaded as `0x5EB820` does: a shape is built from the glyph coverage (the effect's mask word at +0 erodes the binary ink one step at a time and every removed pixel keeps a smoothstep level, `0x5EB270` with `0x5EA1F0`; the parameter at +8 blurs it with a square Gaussian where the glyphs have coverage, `0x5EB3E0`), the normal comes from the shape's next-byte and next-row differences read without clamping (`0x5EB8A0`), and the colour is the strip colour times parameter 2 plus the diffuse and specular terms of parameters 3 to 5, scaled by 255, clamped and truncated. The strip resampler is `0x5E80D0` (two bucket averages in 16.16 fixed point, matched); it has no upsampling branch (a requested height above the strip divides by a zero bucket count, `0x5E8364`), so the nearest-row upsampling of a shorter strip is `[EXT:sign-relief-upsample]` (user approved, October 16, 2026). The blurs (`0x5EA8A0`, `0x5EB3E0`), the bevel levels (`0x5EB270`) and the relief arithmetic (`0x5EB8A0`) run instruction for instruction on emulated x87 registers at the single precision of tp.exe's thread (the CRT start-up sets 53 bits, `0x6804D0`; DDSCL_FPUSETUP at `0x563914` and `0x563B92` makes the shipped `D3DIM.DLL` CreateDevice set 24 bits, which the render function `0x576EC0` never changes; the 75 corpus signs differ between 24, 53 and 64 bits, so the precision matters). Open: the glyph coverage. tp.exe draws each line with GDI (the sign's LOGFONT, lfQuality 4 = ANTIALIASED_QUALITY, grayscale smoothing through Uniscribe ScriptStringOut) onto an 8-bit gray DIB twice the mask's size. OpenTPW models those gray levels with the documented Windows 2000/XP grayscale smoothing (`TrueTypeRasterizer`, `CoverageModel.GdiGray4`): 4x4 point samples per pixel, i.e. the 17 levels of GGO_GRAY4 (GetGlyphOutline documents GGO_GRAY2/4/8 as 5/17/65 levels), written as `(level * 255 + 8) / 16`, with no gamma (none is documented for grayscale smoothing; the contrast setting is ClearType only) and no gasp switch (none of the 17 shipped fonts has a `gasp` table). Documented: the level counts, grayscale smoothing as overscale and downsample of a grid-fitted outline, gasp semantics, the absence of a grayscale gamma. Unverified against a GDI capture: that ANTIALIASED_QUALITY uses exactly 4x4 sampling (inferred from GGO_GRAY4; Microsoft states no factor), the sample positions and edge rule, the hinted outline (the fonts carry hinting programs, and OpenTPW has no TrueType bytecode interpreter, so no hinting is applied), the dropout control the fonts request (prep SCANCTRL 0x1FF, SCANTYPE 1; not applied), the level to byte mapping on the 8-bit DIB (its palette and GDI's colour matching), and the Windows version the game ran on | a captured original sign texture (the text DIB or the composed halves), or the GDI rasterizer of the Windows version the game ran on |
| COMPAT-004 | Sign text | The gate is composed from its decoded board with the park name as two lines and the stored paint (a 60 % black shadow under dark relief stone text, as in the video); board placement and relief strength are compared with a 720p capture only | native-resolution gate capture |
| COMPAT-007 | Sign text | The park name is the gate's two sign lines: default from the gate's OBJECT_NAMES row (`0x4DB6F9`), renamed in the entry price window's dialog (`0x4990F0`, OK `0x4993E0`), saved in the world save. Its secret name ("Kevin the" / "Cosmic Cow", obfuscated at `0x751A54`/`0x751A3C` through the pointers `0x751A34`/`0x751A38`) turns the cheat mode on (`0x4053C0(1)`, the `CHTS` byte and the twelve Alt keys of the cheat key context `0x78733C`; built, [CHEATS.md](CHEATS.md)). Not built: the OK handler's copies to the online park record (`0x409410`, `0x5B0940`/`0x5B0970`, unless GameType is 1) and the message of type `0x15` that `0x4053C0` posts (`0x40FB10`). The gate lines are the attraction record's `mNameA`/`mNameB` (thing `+0x3C8`/`+0x40A`); the shipped Easymode seed keeps the default lines, while `SavedGateName` imports valid lines from a user-supplied original save through `SaveAttraction.NameLine1/2` | what the listeners of message type `0x15` do (runtime subscriptions of the message centre `0x788D3C`); exact original-save import equivalence against a representative corpus |
| COMPAT-018 | Sign text | Ride boards are decoded and alpha-blitted; tp.exe compares the BILZ chunk tag at `0x6193D0` (the inflate step), and everything after it is ported instruction for instruction at the main thread's x87 precision (24 bits once the Direct3D device exists, which precedes the codec's registration at `0x564B20` and every board decode): the header scale multiplies each dequantised coefficient (`0x592B80`, inlined in `0x592E60`), the D4 coefficients are the 53 bit static initialisers `0x591600..0x591690` stored as float32, the row inverse `0x592C50` and the column passes `0x5933A0` (colour) and `0x592E60` (alpha) add in the original's operand order, the colour conversion `0x5935A0` forms the chroma terms once per 2x2 block and rounds each sum through the 1.5 x 2^23 magic number and a 512-entry clamp table, and the alpha conversion is the clamp and truncation at `0x593301`; the header floats feed the planes in the order Y, Cb, Cr, alpha (the PC board reader `0x5E7990`, called by the sign reader `0x5EC3A0`, passes the four floats in file order to the codec's `0x592000` - vtable `0x7026B8` slot 3 through the thunk `0x5987B0`, which runs the colour decode `0x592730` or the alpha decode `0x592E60` by a flag argument - once for colour with the first three as the Y, Cb and Cr scales, `0x5935A0` reading plane 1 as luma and planes 2 and 3 as the 1.772 and 1.402 chroma, and once for alpha with the fourth); a constant opaque alpha plane decodes to 254 on even and 255 on odd rows, which is the float32 rounding of the original's D4 taps (their sums differ in the last bit and `0x593301` truncates), not an orientation fault; open: the GPU row orientation, and original-platform board pixels | the GPU row orientation and a ride-sign capture |
| COMPAT-033 | Sign text | A Hangul sign (the Korean CD names `gulim.ttc` in every sign) is laid out as Latin: usp10's itemizer (`0x65112330`) gives U+AC00..U+D7A3 script 12, whose routine row (`0x65108E74`) is the generic shaper `0x6511325C` and placer `0x65113458` (one glyph per UTF-16 unit by the cmap, per-glyph advances summed, no jamo composition or kerning), and tp.exe passes UTF-16 with flag `0x80` only (no `SSA_FALLBACK`, `0x5EACE0`). usp10 takes each advance from GDI as an integer pixel width (`gmCellIncX`, `0x6510EB3E`) and GDI draws Gulim hinted; OpenTPW scales the `hmtx` advance and rasterizes the unhinted outline. | a capture of an original Korean sign |
| COMPAT-034 | Sign text | A sign whose text holds Hebrew (the gate's park name in the Hebrew language) is laid out in logical order. No font of `fonts.wad` has a Hebrew glyph, so each letter is a `.notdef` box in both games, but tp.exe hands the line to usp10's `ScriptStringAnalyse` (`0x5EACE0`, flag `0x80`), which resolves bidi levels, and `ScriptStringOut` draws visual order, so the boxes of a Hebrew name's words trade places there. Whether usp10's Hebrew engine draws boxes at all for a font without the script is not read; the interface text's own right-to-left pass (`0x6ae4b0`) does not apply to signs (docs/LANGUAGES.md, "Hebrew"). The localisation pack has no `fonts.wad`; whether the Hebrew CD shipped Hebrew sign fonts, as the Korean CD ships Gulim, is unknown. | a capture of an original Hebrew gate sign, or the Hebrew CD's `fonts.wad` |
| COMPAT-010 | Graphics | tp.exe: the state decoder `0x567620` sets `MAGFILTER` point for state bit 0x20000 (mesh record bit 0x8000000, `0x5817D8`, which no shipped mesh has), `MIPFILTER` none for 0x40000 (texture flag 0x100000, `0x581841`) and wrap with a linear `MINFILTER` (else clamp with point) for 0x400000 (slot flags 0x10/0x20, `0x575E9C`); the option `[0x785898]` is SPRITESHADOW, not a filter: its engine flags 0x20000/0x40000 (`0x44DAA0`) only select scene-pass and sprite-flush modes (`0x576D10`, `0x58A7A1`, `0x58AA54`). The preset filter is TEXTUREFILTERING `[0x785878]` (bits 0x18 of `DAT_008BD550`, `0x44D8F0`, `0x566840`). OpenTPW's Veldrid sampler modes stand in for these states ([EXT:texture-filter-samplers]) and wraps or clamps each MD2 slot (docs/MD2-MODELS.md, "Texture slots"); the per-face bits are not applied and a clamped slot keeps the preset's MINFILTER rather than point | the registration that binds option names to the variables (the table position is inferred); which faces carry mesh flag 0x8000000 and texture flag 0x100000 |

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
reads line 1 through `0x4E2860`, which drops the last dash of a trailing "--" (it terminates the copy at length - 1), and line 2 through `0x4E2920`; `BonusNames` now does both.
October 14, 2026, second round (tp.exe and Theme Park Inc data): COMPAT-017 is retired with a code change. The sign lines come from
the selected language's file only (no English fallback); a file without SIGNA or SIGNB shows "MISSING STRING", which the original sets
for NAME, SIGNA, SIGNB and +0x500 before it parses (`0x413600`, `0x41360D`–`0x41361D`); without a language file the definition's text
pointers stay NULL (`0x412C65`; the sign code's handling of a NULL line is untraced and OpenTPW draws the display name); the "both empty, keep the .sam name" rule is gone. The line-1 fallback `0x78A374` is a
never-written .bss buffer, the empty string, so an empty line 1 stays empty (`0x4E2860`). Both getters (`0x413020`, `0x4130B0`) read the
OBJECT_NAMES row (table `0x7488D8`, +4 and +8) when the id is listed, and otherwise def+0x4F8 (SIGNA) and def+0x4FC (SIGNB).
COMPAT-011 (the localized-string fallback order) and COMPAT-012 (the UniToMB encode rule: the original converts wide text with
`WideCharToMultiByte`, `0x424DB0`, and `Encode` has no runtime caller) are OpenTPW decisions or helpers now:
`[EXT:locale-fallback]` and `[EXT:text-encode-helper]` (COMPAT-031, the cream gate paint, was an OpenTPW decision too and is contradicted by the video, see COMPAT-004). The original opens one language folder
(`Data\Language\<selected>`, `0x419710`) and has no English-then-identifier chain; the missing-id result of the table lookup (`0x6ACB70`) is untraced.
COMPAT-019 is an extension (`[EXT:tpi-lake-drained]`, `APPROX:COMPAT-019` retired: the viewer runs no missions, so it has no obstacle state to follow): Theme Park Inc's `Data/Objects.txt` lists 1703 "New Science Lake Obstacle" and 1704 "New Science Drained Lake
Obstacle", `levels/science/obstacles/` holds `lake.wad` and `dlake.wad`, `terrain.wad` holds dlake, dlakem, lake, lakem1 and lakem2, and
`missions/miss01.sam` sets `Obstacle.Type 1703 # lake`, cleared by one gardener with ability 10 ("Water Drainage"); so "dlake" is the
drained lake, not a finished one. COMPAT-006 (kerning) is retired (`APPROX:COMPAT-006`, October 17, 2026): tp.exe imports only ScriptStringAnalyse, ScriptStringGetLogicalWidths, ScriptStringOut, ScriptString_pSize and ScriptStringFree
from USP10 and no kerning or ABC-width call from GDI32, and the shipped usp10.dll (1999, SHA-256 `58863df8...06d8`) places a Latin run (`ScriptPlace` `0x6510CEDE`, the generic routine `0x65113458` that every Latin entry of the script table at `0x65108E74` names) by summing per-glyph advances from its width cache (`0x6510F241`, filled by `0x6510EF97` through GDI width calls): no kern table is ever requested (`GetFontData` has six call sites, for `JSTF` `0x65110255`, `maxp` `0x65110478`, `cmap` `0x65110C95`, `OS/2` `0x65111022`, `GSUB` `0x65114CA9` and `GSUB`/`GPOS`/`GDEF` `0x65124809`, the last three kinds of layout call belonging to the script entries `0x65124049..0x651249C9` and not to the Latin ones; the image holds no `kern` tag, as bytes or reversed, and imports no `GetKerningPairs`; the flagged fallback `0x6511308F` sums `GetTextExtentExPointW` extents), so sign text is not pair-kerned, as OpenTPW draws it (`[BIN:USP10-DLL:0x6510CEDE ScriptPlace]`). A later Windows copy of Uniscribe loaded in place of the shipped one is not among the files and is not read.
COMPAT-001 is retired (`APPROX:COMPAT-001`, October 17, 2026): the text DIB is read at `0x5EAB70` (an 8-bit gray DIB twice the mask's size, top-down, text colour white on black; each mask pixel is the sum of the four bytes of its 2x2 block shifted right by 2, `0x5EAFC3..0x5EAFE5`), and the surface is composed 256 rows high with two 256x256 masks, of which `0x5ECB40` packs only the top 128 rows (`0x5ECF2C`), so the rows below 128 now feed the blur, morphology, shape and relief neighbours and the relief's measured text extent as they do natively (`[EXT:sign-text-mask-width]` is gone: the 256-wide mask is the original's). The baselines of the 75 corpus signs moved for this; see the progress note of October 17, 2026. COMPAT-018 needs a ride-sign capture, COMPAT-003 and -004 native-resolution ones; the 0x2D palette (`[EXT:fsh-0x2d-palette]`, `APPROX:COMPAT-015` retired) has no reading in
Theme Park Inc's FSH loader to match, so its A1R5G5B5 decode is OpenTPW's choice.
COMPAT-008 and COMPAT-013 are OpenTPW design decisions: `[EXT:gate-sign-depth-lift]` (the gate sign faces' depth lift) and
`[EXT:graphics-preset-cpu]` (the processor clock taken as 450 MHz or faster).
October 16, 2026 (user approved): COMPAT-009 is retired: `APPROX:COMPAT-009` became `[EXT:sign-text-antialias]` (16 sub-scanline coverage instead of GDI ANTIALIASED_QUALITY).
Parts of three more IDs became extensions and the IDs were narrowed (COMPAT-001 once too, `[EXT:sign-text-mask-width]`, and COMPAT-003 once too, `[EXT:sign-coverage-precision]`, both retired October 17, 2026): `[EXT:sign-fractional-advance]`
(COMPAT-002), `[EXT:texture-filter-samplers]` (COMPAT-010, the Veldrid sampler choice). `[EXT:sign-face-alpha-blend]` is removed and COMPAT-032 retired (October 28, 2026): the composed sign faces take the original's own state, alpha type 0x200 (`0x575C60`, `0x575DAB`).

## Not done

- Ride signs now compose optional decoded boards, native-order paint and relief layers,
  and two 128x128 destinations, with paint masks shaped as tp.exe does (offset, morphology,
  Gaussian) and relief shapes as tp.exe does. The glyph coverage (GDI gray levels) and original-platform pixel
  parity remain open (COMPAT-003, COMPAT-018).
- Most detail options have no renderer feature to drive (table above).
- The overlay does not merge directory listings; `GameLanguage` keeps its own language overlay.
- Only TPW (not TPI/Sim Theme Park) data was examined.
