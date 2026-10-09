# BF4 fonts: CPU decoding and rendering evidence

October 9, 2026. Status: CPU decoder implemented for the selected corpus; GPU atlas
text renders original strings in a sandbox panel on Metal. Original UI screens and
original visual fidelity are **not verified**; README status stays partial. No new
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
That mapping is an OpenTPW choice: no original palette, gamma or blend mode is
evidenced. All 33 fonts pack without overlap and every region equals its decoded
coverage; GAME8AA (128×128) and SESHMED (256×256) atlas hashes are pinned.

`TextLayout` advances the pen by each record's advance and places bitmaps at
pen + offset X and line top + offset Y. Line height is the header height hint:
across the corpus the lowest glyph bottom (offset Y + height) is within one pixel
of it, and glyph tops can be negative (accents above the line box). Interpreting
offsets relative to the line top, and the hint as line height, is inferred from
these extents, not from original code. No kerning is applied: records contain no
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
