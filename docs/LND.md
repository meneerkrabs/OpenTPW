# LND: the procedural terrain texturer's input (`base.lnd`)

October 25, 2026. Status: format decoded (all four themes parse to exactly the file size), the texturer ported and byte-exact against tp.exe's build routine.
Not yet drawn by the game: wiring the textures into the terrain renderer, the option settings and the shader are later steps of issue #829 (package E, steps E4 to E7).
No original data is in the repository; the tests that read it run with `OPENTPW_GAME_PATH` and keep only hashes.

`base.lnd` is the one file the original's option PROCEDURALTEXTURING reads. It sits in each theme's terrain archive,
`levels/<theme>/terrain.wad` (OpenTPW path `/levels/<theme>/terrain/base.lnd`), and is 2.2 to 2.4 MB. The MAP, HMP and scape files play no part in the texturer.
With the option on, tp.exe builds a separate N × N texture for every visible terrain cell (N = 16, 32 or 64 from the option) from this file instead of drawing one
of the six static `*_bas1..6` ground textures per cell. OpenTPW's reader is `LndFile` (`OpenTPW.Files`), its texturer `ProceduralTerrainTexturer` (`OpenTPW`).

## Layout

All integers are little-endian. The parse must end exactly at the end of the file; `LndFile` refuses anything else.

```
u8      version            3 (the loader 0x560FF0 rejects other versions)
u8[10]  parameters         five (centre, width) byte pairs, below
u32     W                  weight grid width in samples   (384 in every shipped file)
u32     H                  weight grid height in samples  (344)
u32     bytes per sample   4
u8[W*H*4] grid             per sample: [valid, w1, w2, w3], row-major
3 x set:                   the sets for N = 64, 32, 16, in that order
  u32   N                  64, 32 or 16
  u8    set index          0, 1 or 2
  13 x image:
    u32 width, u32 height, u32 bytes per pixel
    u8[width*height*bytes] pixels, rows top to bottom
```

Images 0 to 5 are colour images, 4 bytes per pixel `[0, R, G, B]` (the first byte is 0 and never read). Images 6 to 12 are byte maps, 1 byte per pixel.
The cell grid is `(W - 1) / 4` by `(H - 1) / 4` = 95 by 85 cells with four weight samples per cell along each axis. The heightfield is 96 by 85 cells:
its column 95 lies outside the weight grid (open, below).

### Parameters

The ten bytes are five (centre, width) pairs, each the input of one ramp table (below):

| Bytes | Ramp | Role |
|---|---|---|
| 0, 1 | layer A | first layer (dirt in the jungle) |
| 2, 3 | layer B | second layer (grass in the jungle) |
| 4, 5 | mask 2 | where B shows over A |
| 6, 7 | layer C | third layer (foliage and flowers in the jungle) |
| 8, 9 | mask 1 | where C shows over the rest |

| Theme | A | B | mask 2 | C | mask 1 | file bytes | SHA-256 |
|---|---|---|---|---|---|---|---|
| jungle | 48, 40 | 40, 90 | 68, 40 | 97, 30 | 55, 50 | 2,417,116 | `cc50e741ee3b1531dcb8af149dac0e7eb00ab1402d0c8ac94e37c3a7d21fe9e6` |
| fantasy | 28, 50 | 40, 12 | 52, 40 | 33, 30 | 42, 43 | 2,196,802 | `b0ccf2889e3c647612548f31e705d207311540f876d9975d844b0ddd1b09759a` |
| hallow | 18, 10 | 40, 30 | 60, 40 | 47, 10 | 115, 60 | 2,447,361 | `f4b5f299a0bbab1795d6a61f96c27bb1670dc04518254144f09e1fb5e93d2907` |
| space | 30, 0 | 40, 50 | 25, 50 | 47, 50 | 45, 60 | 2,241,690 | `f29d803973107ae6256a74a3cf2a4e6c61a1a193a7f59d91a71df3142f2106bf` |

### Images

File order within a set, and the role each takes in the build:

| Images | Role |
|---|---|
| 0, 1 | layer A colours (c0, c1) |
| 2, 3 | layer B colours |
| 4, 5 | layer C colours |
| 6, 7, 8 | detail maps of layers A, B, C |
| 9, 10 | mask 2 maps |
| 11, 12 | mask 1 maps |

Every image of a set is larger than N and tiles seamlessly with the period (width - N, height - N): its last N columns and rows repeat the first ones, so a window of N × N
texels never leaves the image. Set 2 (N = 16) is set 0 at about a quarter of the scale. The image sizes of every theme and set are pinned in `ProceduralTerrainCorpusTests`.

The grid's first byte is 0xFF where the sample is valid. Space also has other values (1 to 9, 11 to 13, 16, 17, 20, 25, 27, 28, 30; cells (42, 12), (43, 12) and (44, 12) have them among their inner samples, and the original draws those cells grey); only 0xFF counts as valid.

## The build

`[BIN:TPW-EXE:0x0055F780]` builds one cell's texture. Input: N, the cell (x, z) with z the heightfield row. The file's rows run the other way: `row = 84 - z`.

1. **Validity.** The validity bytes of the four samples (4x + 1, 4 row + 1), (4x + 2, 4 row + 1), (4x + 1, 4 row + 2) and (4x + 2, 4 row + 2) must all be 0xFF. Otherwise the whole
   texture is the grey pixel `[0, 0x7F, 0x7F, 0x7F]`. Every non-hole heightfield cell inside the grid is valid in all four themes; some holes are marked valid too
   (`ProceduralTerrainCorpusTests`), and a hole is never drawn.
2. **Ramp tables** (`0x0055E710`). `h = (w + 1) / 2`; entry i is 0 below `c - h`, 255 above `c + h` and `255 * (i - (c - h)) / (w + 1)` (truncating) between.
3. **Weight planes** (`0x0055E780`, for the weights w1, w2, w3 = grid bytes 1, 2, 3). The cell is split into 4 × 4 sub-blocks of n = N / 4 texels, s = log2 n. The sub-block
   (i, j) takes the four grid samples v00 at (4x + i, 4 row + j), v10 to the right, v01 below and v11 diagonal, and interpolates in 16.16 fixed point by forward differencing:
   `start = v00 << 16`, `xs = (v10 - v00) << (16 - s)`, `rs = (v01 - v00) << (16 - s)`, `dd = ((v11 - v01) << (16 - s)) - xs`. Per row, `acc = start`; the texel is `(acc >> 16) & 0xFF` and
   `acc += xs` per texel; after the row `start += rs` and `xs += dd >> s` (arithmetic shift). The plane is N × N bytes, not flipped.
4. **Windows.** For an image of width w and height h the cell reads the window at `ox = (N * x) % (w - N)`, `oy = (N * row) % (h - N)`: texel (X, Y) is image pixel (ox + X, oy + Y).
   Point sampling, no rotation, no mirroring.
5. **Layers.** `L(c0, c1, detail, T) = c1 + (((c0 - c1) * T[(detail * w1) >> 8]) >> 8)` per channel, the shift arithmetic, the result truncated to a byte (`0x0055EE10`).
   A uses images 0, 1 with detail 6 and ramp A, B uses 2, 3, 7 and ramp B, C uses 4, 5, 8 and ramp C.
6. **Masks.** `m1 = max(T1[(w3 * img12) >> 8], T1[(img11 * w3) >> 8])` and `m2 = max(T2[(w2 * img10) >> 8], T2[(img9 * w2) >> 8])`, with T1 and T2 the mask 1 and mask 2 ramps.
7. **Runs.** The decisions are taken per run of 16 texels along a row, aligned at X = 0, 16, ...:
   - all m1 = 255: C.
   - otherwise m2 is computed. With every m1 = 0: all m2 = 255 gives B, all m2 = 0 gives A, else `A + (((B - A) * m2) >> 8)` (BA).
   - with some m1 other than 0: all m2 = 255 gives `B + (((C - B) * m1) >> 8)` (CB), all m2 = 0 gives the same with A (CA), else `BA` first (stored as a byte) and then
     `BA + (((C - BA) * m1) >> 8)` (CBA, `0x0055EAA0`).
8. **The three layer quirk.** `0x0055EAA0` never advances its weight 1 pointer: in a CBA run all 16 texels use the weight w1 of the run's first texel, not their own. Without this, 4 of the
   first 18 cells the research tried differed from the original; with it none do.
9. **Rows** are written bottom-up: source row Y goes to buffer row N - 1 - Y.
10. **Pixels.** The routine writes `[0, R, G, B]`. The cache's 32-bit texture reverses each pixel's four bytes (`0x005725BC`), which is A8R8G8B8 with alpha 0 (`BuildArgb`).
    **A renderer must write alpha 255**: OpenTPW's terrain shader discards low alpha, and the original's terrain pass is deduced to have no alpha test (its cache textures carry alpha 0). That is a deduction: the render state of `0x56F670` is not traced (an E6 research question). Writing alpha is renderer work (step E6), not done in the texturer.
    The 16-bit paths are not ported: OpenTPW renders in 32 bits.

With a floor descriptor (floor art over the terrain, rides and shops) the routine continues past this head; that part is not ported yet (package F).

### Orientation

A texel column runs along +x and a buffer row along +z inside the cell (x, z). The mean absolute colour difference across the cell edges of a 16 × 12 jungle patch at N = 32 is 13.7, against 12.7 for one texel
inside a cell; mirrored or turned cells give 20.7 to 28.4 (`NeighbouringCellsContinueAcrossTheirEdges` re-measures the first pair). The seam test corroborates this orientation; it does not prove it (see Open).

## Code

- `LndFile`: strict bounded reader (16 MiB limit, version 3, grid four bytes per sample, sets in the order 64, 32, 16 with 13 images of the roles above, each image larger than N,
  no bytes after the last image).
- `ProceduralTerrainTexturer`: one instance per theme file and N; `Build(x, z, destination)` writes N × N × 4 bytes and returns false for the grey fallback; `BuildArgb` writes the cache layout.
  Pure CPU, deterministic, no allocation per cell (three N × N weight planes and the five ramp tables belong to the instance, so an instance is not thread-safe).
  Cells outside the 95 × 85 grid are refused.

## Verification

- **x86 oracle (X27, docs/reverse/ORACLE.md section 7).** The ramp `0x55E710`, the weight plane `0x55E780` and the build routine `0x55F780` run in the Unicorn sandbox on synthetic levels:
  every ramp pair of the edge values and 500 random pairs (600), 500 weight planes at N = 16, 32 and 64, and 48 cells of synthetic levels that give each run mode (C, B, A, BA, CB, CA, CBA),
  random masks and invalid cells. The Python reading (`tools/ppc-analysis/oracle_x86/terrain_reading.py`) agrees on every input; a reading without the quirk differs.
- **Real cells.** `tools/ppc-analysis/oracle_x86/terrain_golden.py` runs the original build routine on the shipped `base.lnd` of the four themes for a seeded sample of cells per size (the four
  corners, 30 random cells at N = 16, 16 at 32, 8 at 64 and two invalid cells, 297 in all, with the three space cells whose validity bytes are odd) and prints the SHA-256 of each result as `ProceduralTerrainGolden.cs`. The C# texturer reproduces all 297 byte for byte.
  The research that preceded this port also compared its Python model with the emulated routine on 33,820 cells (every cell of all four themes at N = 16, plus 300 at N = 32 and 80 at N = 64 per theme) with no mismatch.
- **Synthetic levels, in CI.** `terrain_synthetic_golden.py` builds 27 levels from an LCG and fixed formulas (`terrain_reading.synthetic_level`; `SyntheticTerrain` in the tests is the same generator): each of the seven run modes plus random masks at N = 16, 32 and 64, gradient weights, random ramps, and an invalid-cell level per size, two cells each (54 cells). It runs tp.exe's routine on them and commits the hashes (`ProceduralTerrainSyntheticGolden.cs`); `ProceduralTerrainSyntheticTests` reproduces all 54 without game data, so CI checks the weight interpolation, general ramps, window offsets and N = 64.
- **Not original bytes.** The tests keep hashes and parameters only; no image or data byte is committed.

## Open

- **Heightfield column 95.** The heightfield has 96 columns, the weight grid 95. Column 95 is non-hole in every theme but outside the file's cells (the routine would read the next row's samples). Whether it is textured
  procedurally or keeps its static slot texture is not settled (the texturer refuses x = 95).
- **Which cells are built.** The original builds only the cells inside a "procedural rectangle" (landscape `+0x234..+0x240`, set by `0x56DF70` from `0x457CF5`); its extent is not fully traced.
- **Texture pages, mipmaps and the edge address mode** of the original's cache textures (`0x590A80`, `0x58C9F0`) are not traced.
- **UV mapping in the draw path.** The orientation above rests on data (seams); how `0x56F380` / `0x56F4F0` and the page UV rect map the texture onto the cell is not traced.
- **Camera modes that suspend procedural textures** (plan OPEN-1): `0x457A40` turns them off for some camera modes (`[0x790AB0]` bits 2 and 4); which modes is not known.
- **Texture budget for N = 64** (plan OPEN-4): the user chose the original's visible-cell cache (1500, 2000 or 2500 textures) over a full atlas; the 64 to 32 clamp of `0x572110` is part of that port.
- **The "not allowed" check** (plan OPEN-5): which device check sets `[0x8BD550]` bit `0x10000000` ("Procedural textures not allowed") is not identified.
- **Wiring.** The visible-cell cache of 1500, 2000 or 2500 textures, the option mapping and the renderer are not part of this change.
