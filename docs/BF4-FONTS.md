# BF4 fonts: CPU decoding and rendering evidence

October 9, 2026. Status: CPU decoder implemented for the selected corpus; GPU atlas
text renders original strings in original-style menus and HUD on Metal. README
marks the selected-corpus decoder implemented; original palette/alpha mapping,
layout and visual fidelity remain **unverified**. No new
dependency or font asset is included in the repository.

`FontFile` reads the `F4FB` header, its little-endian offset table and 24-byte
glyph records. The upstream format note omits four encoding/reserved bytes in
each record; actual glyph dimensions start at record offset 16, not 12.
Header width/height are retained as hints, not trusted as glyph bounds.
Glyph records retain character, signed placement offsets/advance, encoding,
dimensions and decoded coverage samples 0–15 in continuous row-major order.
No original palette/alpha mapping is implied; see Rendering for OpenTPW's choice.

Encodings:
- 0: packed four-bit samples, high nibble first, continuous across odd-width rows.
- 1: nibble RLE; nonzero literal, zero/count/value run, zero/zero terminator.
- 2: continuous high-bit-first monochrome samples mapped to coverage 0 or 15.

The selected corpus contains RLE glyphs with one zero alignment sample beyond
an odd pixel count, or one omitted final zero sample. These bounded cases are
handled explicitly and regression-tested; longer overflow/early termination and
nonzero alignment are rejected. This is observed corpus behavior, not blanket
acceptance of arbitrary incomplete glyphs. Unknown encodings fail explicitly.
Multiple space records are preserved in original table order, not overwritten
in a codepoint dictionary. Each selected font has 58 space records.

Input is capped at 16 MiB; at most 4096 records, 1,048,576 pixels per glyph and
16,777,216 total decoded pixels. Bounds are checked before pixel allocation;
offset aliases also count toward the total decoded budget. Encoded spans and
declared decoded packed sizes must agree with file/dimensions. Caller-owned
streams stay open and nonseekable short reads are supported. Reserved fields
and unused encoded padding are not assigned invented meanings.

## Private corpus

All 33 local `Data/Language/English/*.bf4` files decode all 8,217 table entries.
Encoding inventory: 4,373 raw-four-bit, 2,326 RLE and 1,518 monochrome entries.
Counts describe this extraction only; they do not certify other locales/editions.
Three fixtures pin file identity and independently calculated `A` coverage hashes:

| Fixture | File SHA-256 | A size/advance | Coverage SHA-256 (0–15 samples) |
| --- | --- | --- | --- |
| `GAME8.bf4` | `a52c765c2c652f20eea95dd25f19fe595300b7e6b89e88abcf526889e226c8ea` | 6×8 / 7 | `0447875b4a01045703c3b76dbbd3c10a282faee3c139f204929368e906951547` |
| `GAME8AA.bf4` | `15eb42cd95898a7cd87f7b4e15b4fabdfb0f321c092be257df13a23f71dd3fed` | 7×8 / 8 | `116f1f31dcb27af9dbef89ed5669592008ea01ef66735ed7797a9406e48f79aa` |
| `SESHMED.bf4` | `429db5d5503e725e6783d49bd9524dd8cb41ae030ece16ed2a85c77cddd0a6a2` | 12×19 / 12 | `d79cdee8540308a668fa2734b742bad0082fab889838efea043f665ccfcff08f` |

Synthetic tests cover all encodings, odd rows, signed metrics, empty fonts/glyphs,
short reads, invalid magic/offsets/sizes/flags, truncated commands, expansion/input/
aggregate limits, terminators, zero padding and implicit final zero. Asset tests
are inconclusive without the selected private corpus, not successful asset passes.

## Rendering

`FontAtlas` packs the first record of each character (192 per English font; the 58
identical space slots collapse to one) on shelves ordered by descending height,
descending width, ascending character, with one transparent padding pixel. Width is
the smallest power of two from 64 that keeps the packed height within it; height is
the next power of two. Coverage maps linearly to alpha (`value × 17`, 15 → 255).
The original blend is now known from the Mac binary (see *Original-binary evidence*):
it is linear in coverage with no gamma curve, so `× 17` matches it except for rounding
and the darkening bias described there. No original palette is evidenced. All 33 fonts pack without overlap and every region equals its decoded
coverage; GAME8AA (128×128) and SESHMED (256×256) atlas hashes are pinned.

`TextLayout` advances the pen by each record's advance and places bitmaps at
pen + offset X and line top + offset Y. Line height is the header height hint:
across the corpus the lowest glyph bottom (offset Y + height) is within one pixel
of it, and glyph tops can be negative (accents above the line box). Interpreting
offsets relative to the line top is inferred from these extents; using the header
byte as line height and the record advance as pen step matches the original code
(see *Original-binary evidence*). The fallback to the lowest glyph bottom when the
header byte is 0 is OpenTPW policy (the original returns 0). No kerning is applied: records contain no
pair data. `\n`, `\r` and `\r\n` break lines (UITEXT uses `\n`); optional greedy
wrapping at spaces and `?` fallback for missing characters are OpenTPW policy.
Every UITEXT entry lays out in GAME8AA without fallback. Pinned layouts: UITEXT
`Excitement` (GAME8AA, 58 px), OBJECT_NAMES `Totem` (SESHMED, 53 px) and the quit
confirmation wrapped at 120 px.

`TextRenderer` uploads atlases as R8 textures, point-samples one texel per
framebuffer pixel at integer positions and alpha-blends vertex color by coverage.
`SandboxTextOverlay` draws a top-right panel in the park sandbox: OBJECT_NAMES
`Totem` in SESHMED and UITEXT ride labels/`No upgrades have` in GAME8AA. Panel
placement, colors and backing rectangle are not an original screen. The native
smoke test reads the resolved frame back on Metal and requires the panel to match
a CPU composite of the same quads (observed: 2,224 ink pixels, maximum channel
difference 0) and writes `artifacts/native-smoke-text.png`. Visual check of that
capture: upright, legible glyphs with expected baselines and descenders.

## Original-binary evidence

October 9, 2026. Static analysis (Ghidra, decompiler plus disassembly) of the Mac
PowerPC library `ltms_shared` (data fork SHA-256
`2b0f7ac92c1f8b67761d271dd5832fca1bd19a8dd8d494b7b89b6ffa693e6ee8`, part of the corpus
in [reverse/FINDINGS.md](reverse/FINDINGS.md)). Its exports carry their C++ names, so
each finding is tied to a named function; nothing was executed and no code is copied here.

- **Font metrics.** `TbIRLE4bitFont::GetVerticalSpacing()` returns header byte 5 (OpenTPW's
  `HeaderHeightHint`), or 0 without a font. `GetCharSize()` returns (u16 at record +0x16,
  that same header byte): the record advance OpenTPW already uses, and the line height.
- **Glyph encodings.** `Tb4BitFontRenderMethod::DrawChar` expands run-length nybble glyphs
  and 1-bit glyphs (bit → coverage 0 or 15) into one 4-bit buffer before drawing.
- **Render parameters.** `TbFontRenderParams` holds a colour mode (pre-coloured, recolour,
  textured) and a write mode (alpha-write or blended). The constructor selects **blended**.
  Each `Use…` setter fails when the font lacks the matching capability bit (1 pre-coloured,
  2 recolour, 4 textured, 8 blended).
- **Blend (one-colour, blended; `TbOneColour4BitFontRenderMethod::DrawCharTo8888`).** Per
  R, G and B lane, with c the 4-bit coverage: if the colour alpha A is not 255, first
  c = ⌊A·c / 255⌋; then `out = dst + (c · (colour − dst)) / 15` in **unsigned** 32-bit
  arithmetic (the divide is a multiply-high by 0x88888889, checked in the disassembly).
  Destination alpha is left unchanged; A = 0 draws nothing. Consequences: no gamma; a
  lighter colour over a darker pixel gives the truncated linear blend; a darker colour
  over a lighter pixel wraps and lands **about 17 above** the linear result per lane (black,
  full coverage, over white gives 17,17,17). This is derived from the code, not yet
  seen in a capture.
- **Pixel formats.** Separate named blitters exist for 8888, BGRA, 565, 555, 4444 and a
  generic path, plus no-blend and textured variants. Only the 8888 path has been read so
  far; the 16-bit paths, which the Mac build most likely used, still need the same check.

## PC text library: placement and blitting (tp.exe)

October 17, 2026. Static analysis of tp.exe (`[BIN:TPW-EXE:...]`), the PC twin of the Mac library above. Nothing was executed.

- **Glyph position.** The common draw routine (`0x6b4aa0`) places a glyph bitmap at pen + (signed byte at record +0x14, signed byte at +0x15)
  with the pen y at the line's top, clips it to the surface (`0x6b5340`) and draws nothing for character 0x20 (advance only). This
  confirms OpenTPW's reading (offsets relative to the line top, advance as pen step); line height is the font's vertical spacing (`0x6ae930`
  stores it at +0xbc), and no kerning pass exists.
- **Block and line placement** (`0x6aeb10`, `0x6ae550`; `TextPlacement`). The text block is lines x line height tall and the widest
  line wide (a line's width is its advance sum without the spaces dropped where it wrapped, `0x6aec80`). Vertically it is centred as
  `(bottom - top - block height) / 2 + top` in signed division, also when it is taller than the box, or bottom aligned as
  `bottom - block height`; horizontally the block is centred as `(right - left - block width) / 2 + left` or right aligned. Inside
  the block every line moves by `(block width - line width) >> 1` (centred) or by the whole difference (right aligned). OpenTPW's
  `UiContext.DrawText` follows this since the October 17, 2026 round; before, a centred or right aligned multi-line text kept
  all lines left aligned inside the block, and a block taller than its box was put at the box's top.
- **Measure** (`0x4929f0` through `0x6ae1c0`). It returns the block box described above (OpenTPW's layout width and height); with its wrap flag
  clear the layout box is 0x4000 wide so nothing wraps. A language flag (`0x41a850`, object `0x78b530`) swaps left and right alignment
  (`0x4925a0`, `0x4929f0`), and `0x6ae410` finds Hebrew letters (U+05B1..U+05EA) for a right-to-left line pass (`0x6ae4b0`); no shipped
  language here uses either.
- **Two-byte text** (`0x6ae930`). A byte below the table's lead threshold (table byte 8) is glyph index byte - 1; a lead byte at or above it
  takes the next byte as well: index = threshold + (lead - 1 - threshold) * 255 + (trail - 1). The index feeds the glyph metrics and
  the table of code points (`+0xc`), whose entry is the character the line breaker classifies (`0x6aeab0`).
- **Surface and blend.** A text control draws into a bitmap in the display mode's pixel format (`0x4925d0`: bit depth and channel
  masks from the 32-byte mode record at `0x8bd558`; `0x48f830` clears it to zero, puts the text library in mode 0 (`0x6ad790`), draws once with
  colour white (255,255,255,255) unless the control keeps the font's colours, then sends the bitmap through the sprite pass). The glyph draw
  reaches the canvas class by the table at `0x782d38` (library mode, destination type, source kind): an RGB bitmap takes the class at `0xfb1ef0`
  (vtable `0x70a1a8`), whose generic slot `0x6b4aa0` expands the glyph to a 0-15 coverage buffer and picks the per-format blitter by the
  destination's channel masks (`0x6b4f11`-`0x6b52b7`). Its 8-8-8-8 blitter (`0x6b24b0`) writes, for every sample of coverage `c` above 0, the
  pixel (alpha = `c` x 17, the colour's RGB) and leaves the rest of the cleared bitmap untouched; the 4-4-4-4 blitter (`0x6b21f0`) writes the alpha nibble `c`;
  a colour alpha below 0xff (the title shadow's 0x80) scales the coverage first, `c x alpha / 255` rounded down. The blending
  blitters of class `0x70a188` (5-5-5 `0x6b0760`, 4-4-4-4 `0x6b1130`, 8-8-8-8 `0x6b15f0`, blending `dst + ((src - dst) * c) / 15` and keeping the
  destination's alpha) are the library's mode 1 (set by `0x6ad760`, used by the list text builder `0x492c40`), not the mode 0 these controls use. The texture format index `[0x8bd554]` is 2 (4-4-4-4) or 3
  (8-8-8-8) by option bit `0x400000` of `[0x7a1a8c]` (table `0x7015f8`: 0 = 5-6-5, 1 = 1-5-5-5, 2 = 4-4-4-4, 3 = 8-8-8-8). The control is a
  sprite object (record at control +0x54, colour at record +0x24 = control +0x78) drawn by the sprite draw `0x58a010` as pre-transformed
  quads of the fixed state word `0x7060556` (`0x58a26c`): alpha blend SRCALPHA/INVSRCALPHA, alpha test at `0x10`, depth test and write, stage 0
  colour = texture MODULATE diffuse (the control colour), point filters (`0x567620`, `0x566840`). The screen result is the control colour blended with
  coverage `c / 15`, and a black control colour gives exact black text (`UI-037` retired). The title control (constructor `0x491920`, draw `0x491ab0`) draws a grey shadow copy under its string in the same bitmap; OpenTPW ports it (`UiBatch.AddTitleText`) and draws no shadow under plain text.
- **Control text box** (`0x48f830`). Horizontal alignment byte +0xd8 (0 left, 1 centre, 2 right) and vertical byte +0xd9 (0 top, 1 centre, 2 bottom;
  constructor default centre) select the library's modes; margins +0xde and +0xe0 are in 2048 x 1536 units scaled by the screen
  size words `0xfaa5c4` and `0xfaa5c0`; without the wrap byte (+0xda) the box is 0x4000 wide. The bitmap's quad is placed in canvas
  units as `pixel * 2048 / screen width` (`0x48fbbd..0x48fc08`), so original text positions are integers in the original screen mode's pixels.
  OpenTPW's controls use their table rectangles without these margins (no control builder's margin values were traced).

## Remaining gates and sources

Next: use fonts in actual original UI screens/dialogs; compare captures of actual
strings, baseline/spacing/AA/colors and supported locales against the original.
Run rendering on D3D11 and Vulkan (shader cross-compilation is tested for HLSL and
GLSL only). Parsing and rendering samples do not qualify the original UI or the
original game's audiovisual behavior.

Research sources (format references, no third-party decoder source copied):
- OpenTPW docs `34f357fabc8a6aa064c76260c714dca8b875b148`:
  https://github.com/OpenTPW/opentpw-docs/blob/34f357fabc8a6aa064c76260c714dca8b875b148/src/formats/bf4.md
- OpenKeeper `1d9a1c713a033bb2850e25e977ddfdd13113f77a`, BF4 reader/encoding definitions:
  https://github.com/tonihele/OpenKeeper/tree/1d9a1c713a033bb2850e25e977ddfdd13113f77a/src/main/java/toniarts/openkeeper/tools/convert/bf4
- Local selected original files and a separate bounded Python coverage calculation;
  no original runtime execution or external decoder binary used as an oracle.
