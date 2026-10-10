# FSH (EA `SHPI`) textures: Theme Park Inc reader

October 10, 2026. Status: `FshFile` decodes every `.fsh` file and WAD member of the
Theme Park Inc (TPI) retail data to RGBA on the CPU. Nothing in OpenTPW loads
these textures yet; TPI is not playable. No visual comparison with the original
game was made. No asset is included in the repository.

This is step 1 toward offering TPI in the launcher. Theme Park World does not use
`.fsh`; it stores textures as `.wct` (see [THEME-PARK-INC.md](THEME-PARK-INC.md)).

## Corpus

The TPI `Data` folder from the Dutch/Benelux retail CD (InstallShield cabinets
extracted with `unshield`; see THEME-PARK-INC.md), read in place. Every member of
all 303 WADs was enumerated with OpenTPW's own `WadArchive`.

| | Count |
| --- | --- |
| `.fsh` files and members | **7,283** (162 loose, 7,121 in WADs) |
| WAD members starting with `SHPI` but not named `.fsh`, or the reverse | 0 |
| Header id `G231`, one image | 7,283 |
| Image code `0x7B` (raw 8-bit indices) | 547 |
| Image code `0xFB` (`0x7B` + `0x80`: RefPack-compressed indices) | 6,736 |
| Palette `0x24` (24-bit) | 4,308, all with 256 entries |
| Palette `0x2A` (32-bit with alpha) | 2,974 (414 with 256 entries, 2,560 with fewer) |
| Palette `0x2D` (16-bit) | 1: water `snowtrac.wad` `stexture/icewall1.fsh` |
| Name record `0x70` present | 6,649 (634 without) |
| Other attached records (`0x6F` comment, etc.) | 0 |
| Image sizes | 26 distinct; 32×32 (3,001), 128×128 (2,009) and 64×64 (1,840) are most common; the largest are 384×344 and 250×250 |

The older note in THEME-PARK-INC.md that all files have "record code `0x7B`" is
true only after masking off the compression flag (format byte 251 = `0xFB` in
[TPI-COMPARISON.md](TPI-COMPARISON.md)).

## Layout established from the corpus

All values are little-endian unless stated otherwise.

- **Header (16 bytes):** `SHPI`, u32 total size (equal to the file length in all
  7,283), u32 image count, four-character directory id (`G231`).
- **Directory:** per image a four-character tag and a u32 offset. In 7,101 files
  the tag is the first four characters of the file name (ignoring case); in the
  other 182 it is not. In all 7,283 the 88 bytes between the directory and the
  image hold `Buy ERTS` followed by zeros, and the image starts at offset 112. The
  reader accepts any gap and does not interpret it.
- **Image record (16-byte header):** code byte, 24-bit offset from this record to
  the next attached record (0 = last), u16 width, u16 height and four u16 words
  (EA's centre and position fields). The four words are 0 in all 7,283 images.
- **Pixels:** width × height palette indices, row-major from the top row (the
  decoded images are upright; see Evidence). Raw `0x7B` blocks are exactly
  width × height bytes; all 547 have a pixel count that is a multiple of 16.
- **Compression (`0xFB`):** RefPack with the 5-byte header `10 FB` + 24-bit
  big-endian decoded size (the only header form in the corpus). The decoded size
  is the pixel count **rounded up to a multiple of 16**: for the 135 images whose
  pixel count is not a multiple of 16 (sizes 22×22, 26×26, 30×30, 38×38 and
  250×250) it is 12 bytes more than width × height. The rows are not padded (the extra bytes are fewer than
  one row); only the end is. The compressed block is followed by up to 15
  alignment bytes before the next record; these are not always zero (36 of the
  162 loose files) and are ignored.
- **Palette record:** directly after the image, code `0x24`, `0x2A` or `0x2D`,
  then u16 entry count, u16 height (1) and four more words (in the 162 loose files
  checked: the entry count again, then zeros). Entries follow, then alignment
  bytes up to the next record.
  - `0x24`: 3 bytes per entry, **R, G, B**.
  - `0x2A`: 4 bytes per entry, **B, G, R, A**.
  - `0x2D`: u16 per entry, read as A1 R5 G5 B5 (see Approximations).
  No pixel uses an index beyond the palette's entry count.
- **Name record `0x70`:** a NUL-terminated name. In the loose files it follows
  the palette and is the last record (next offset 0), bounded by the end of the
  file. The reader accepts the palette and name in either order.

## Evidence for colour order and alpha

- **Thumbnails.** 70 loose TPI textures ship next to a small TGA of the same name
  (8×8 or 16×16 pixels). The
  asset test decodes the TGA with StbImageSharp, box-filters the decoded FSH to
  its size and compares (mean absolute error per channel, 0–255):

  | Palette / TGA | Images | R | G | B | A | R/B swapped |
  | --- | --- | --- | --- | --- | --- | --- |
  | `0x2A` / 32-bit | 23 | 1.7 | 1.9 | 1.1 | 1.2 | 100.7 |
  | `0x24` / 24-bit | 43 | 7.5 | 10.0 | 7.7 | – | 92.0 |
  | `0x24` / 32-bit | 4 | 8.1 | 8.1 | 8.1 | 255 | 8.1 |

  This fixes the byte order of both common palettes and shows that the `0x2A`
  alpha byte is the texture's alpha. The four `0x24` images with 32-bit
  thumbnails are two badge textures, each shipped twice; their TGA alpha is 0
  everywhere (see COMPAT-014).
- **Duplicate textures.** Many objects ship a texture twice, in `stexture/` and
  `textures/`, often at different sizes and with different palette codes. For the
  12 such pairs whose `0x2A` copy is mostly opaque (mean alpha ≥ 200), the mean
  colours of the `0x24` and `0x2A` copies differ by less than 2 per channel, and
  swapping red and blue in one copy is worse in all 12 (by more than 5 levels in
  11; the twelfth is nearly grey).
- **`0x2D`.** The only file decodes to a mean colour of (134, 197, 221), next to
  (134, 194, 217) and (107, 188, 216) for `icewall3` and `icewall2` (`0x24`) in the same
  archive. All 256 entries have bit 15 set.
- **Visual check.** A few loose textures (award ribbon, Mutant eye, tiara, bear
  badge, golden ticket with the "theme PARK Inc." logo, snow, flower, TP logo)
  were viewed after decoding: upright, readable and with plausible colours. This
  is a check of the decoder, not of how the game shows them.

## Approximations

- `[APPROX:COMPAT-014]` `0x24` palettes store no alpha, so their 4,308 images
  decode fully opaque. No colour key or transparent index is applied. Nothing in
  the data proves this; the four 32-bit thumbnails above even have alpha 0, and
  some objects ship an opaque `0x24` copy of a texture whose `0x2A` copy is
  fully transparent (e.g. `fruitb.wad` `grape.fsh`). Evidence needed: TPI's
  texture upload code or captures.
- `[APPROX:COMPAT-015]` The single `0x2D` palette is read as A1R5G5B5 with bit 15
  meaning opaque. The colour order is supported only by the mean-colour match
  above; the alpha bit has no effect on this file.

## Reader

`FshFile` (`source/OpenTPW.Files/Public/FshFile.cs`) follows the other bounded
readers: input capped at 16 MiB and read through non-seekable or short-read
streams without closing them; at most 256 images, 4096 × 4096 per image and
16,777,216 pixels in total, all checked before any pixel allocation. Every record
is bounded by its next-record offset, the next image or the end of the file. The
RefPack body is decoded by the existing strict `TreCompression.Refpack`, which
rejects truncated commands, back-references before the start and output beyond the
declared size. Size mismatches, out-of-range offsets and indices, a missing,
duplicate or truncated palette, an unterminated name and duplicate directory
offsets throw `InvalidDataException`. Image codes other than `0x7B`/`0xFB`, palette
codes other than `0x24`/`0x2A`/`0x2D`, other attached records (including `0x6F`)
and other RefPack header flags throw `NotSupportedException`. The reader supports
several images per file, although the corpus only has one.

`FshImage` exposes the tag, size, raw position words, compression flag, palette
code and entry count, the indices, the palette as RGBA, the name and the decoded
RGBA pixels.

Inspection (no game path needed):

```sh
dotnet source/OpenTPW/bin/Debug/net10.0/OpenTPW.dll --inspect-fsh '/path/to/TPI/Data/levels/water/rides/snowtrac.wad!stexture/icewall1.fsh'
```

A plain path reads a loose file; `archive.wad!member/path` reads a WAD member
(case-insensitive). It prints the header, each image's size, codes and palette,
and the SHA-256 of the decoded RGBA.

## Tests

`FshFileTests` builds files in the test: raw and compressed images, all three
palette codes, padded RefPack sizes with non-zero trailing bytes, back-references,
several images, short non-seekable reads, and rejection of bad magic, size and
count, directory and next-record offsets, truncated pixel, palette and name
records, oversize input, dimensions and aggregate pixels, unsupported image,
palette and attached record codes, duplicate records, palette index overflow and
bad RefPack streams.

The asset tests need `OPENTPW_TPI_PATH` set to a TPI installation or its `Data`
folder, read in place. Without it they are inconclusive (not passes). They check
the corpus counts above, the thumbnail and duplicate-texture comparisons, and
five fixtures pinned by file and decoded-RGBA SHA-256 (hashes only):

| Fixture | Palette | Size | Code | File SHA-256 | RGBA SHA-256 |
| --- | --- | --- | --- | --- | --- |
| `generic/shadow/alphkid.fsh` | `0x2A` | 64×64 | `0xFB` | `3d9aa0ad6957cc5558066418de7bdd8061fa77ce7d1588aa3fb1400996822122` | `d9adb81b8d57c9a4f34c4ea0592e6f47b04e9eae4736a6eabacccddfbe907bd9` |
| `levels/water/rides/snowtrac.wad` `stexture/icewall1.fsh` | `0x2D` | 32×32 | `0xFB` | `1a3ecb2f79537ad8d23091fa049e64d98c40ea8aff746ef258a48359f02f6dd0` | `aefb6b56c12487f023746a8b057edda235ac5ce59189af41c50bea2b8eb57b4e` |
| `ui.wad` `stexture/tb_camera.fsh` (padded) | `0x2A` | 38×38 | `0xFB` | `1bad3bed574e7f823d0166e70a17d89f6c9f11a51b819f3b4395734bf302957d` | `75d874e1d3fc656e115c7626e796ac8b94a948963f5d4244be277434f5c3d9e7` |
| `levels/arabian/Sharetex.wad` `an_g07.fsh` | `0x24` | 128×128 | `0x7B` | `6246087bd4c5803d728d5b2c1dbe4b13a8d5d6e84862a7d3effd5ea9cfc1a057` | `35175b6940013fd3c99f1494e5741d1f839c6d7735f0637a99919fc4ca409baa` |
| `global/Advisor/textures/Mutant_Eye.fsh` | `0x24` | 64×64 | `0xFB` | `3ee747db7a0f9bb0a80a2c3ffd3ac0591782e884b8511c07c202c6222b0eb91c` | `1f0bd89426b4cf12b72316f1b1c3967821355c70d6e4ecb806219111dfaf3b1a` |

The `alphkid` and `Mutant_Eye` RGBA hashes were also
produced by a separate throwaway Python decoder written for this check.

## Not verified

- How TPI uses or draws these textures: filtering, mipmaps, blending, alpha test,
  or whether `0x24` textures get a colour key (COMPAT-014).
- The meaning of the directory gap (`Buy ERTS`), the four position words and the
  repeated palette entry count; the reader keeps them raw or ignores them.
- Which of the `stexture/` and `textures/` copies the game loads, and when.
- FSH variants outside this corpus (other EA image codes, multi-image banks,
  other RefPack headers) are rejected, not supported.

## Next step

Step 2 is loading a TPI level in the sandbox: resolving the textures named by
TPI's MD2 models to `.fsh` members (TPW resolves them to `.wct`), uploading
`FshImage.Rgba` through the existing texture path, and then the TPI-specific
data (the extended TPWS save header, the new `Standard.sam` keys). See the
follow-up list in THEME-PARK-INC.md.

## Sources

- Format facts are from the corpus above. The record layout and palette code
  names agree with the EA Graphics Manager implementation cited in
  TPI-COMPARISON.md (bartlomiejduda/EA-Graphics-Manager at
  `dce358bc1d34102ea2c72b74210b6ca627a46cc4`, `dir_entry.py` and
  `palette_entry.py`); no code from it was copied or run.
- RefPack command set: the existing `TreCompression.Refpack` (docs/AUTORUN.md).
- No original executable was run or inspected for this work, and the third-party
  no-CD patches on the TPI CD were not used.
