# LND: the procedural terrain texturer's input (`base.lnd`)

October 25, 2026. Status: format decoded (all four themes parse to exactly the file size), the texturer ported and byte-exact against tp.exe's build routine, and the ground drawn with it
through the original's visible-cell cache (issue #829, package E): the options PROCEDURALTEXTURING and PROCEDURALTEXTURECACHE are applied at the next level load ("Drawing" below).
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
    **The terrain pass draws alpha 0**: the cell textures have no alpha test in the original (the state word of a cell texture, 0x1256 from `0x590C40`, has bit 0x400 clear, so `0x567620` leaves ALPHATESTENABLE and ALPHABLENDENABLE off); OpenTPW's terrain shader does discard low alpha, so
    `BuildRgba` writes `[R, G, B, 255]` directly (the same bytes as `Build` with the alpha byte last and 255; `TheRgbaWriterIsTheOriginalsBytesWithAlpha255`) and the pass is unchanged.
    The 16-bit paths are not ported: OpenTPW renders in 32 bits (`0x562F70`, read: for the 16-bit formats it converts the A8R8G8B8 buffer with its own channel shifts, which the 32-bit case, a plain copy, does not need).

With a floor descriptor (floor art over the terrain, rides and shops) the routine continues past this head; that part is not ported yet (package F).

### Orientation

A texel column runs along +x and a buffer row along +z inside the cell (x, z). The mean absolute colour difference across the cell edges of a 16 × 12 jungle patch at N = 32 is 13.7, against 12.7 for one texel
inside a cell; mirrored or turned cells give 20.7 to 28.4 (`NeighbouringCellsContinueAcrossTheirEdges` re-measures the first pair). The seam test corroborates this orientation, and the drawn ground ("Drawing" below) shows no seams at the cell edges with it: the corner table of `0x56F670` gives the cell corner (x, z) the texture's (0, 0) and (x, z + 1) its (0, 1), which is this orientation.

## Code

- `LndFile`: strict bounded reader (16 MiB limit, version 3, grid four bytes per sample, sets in the order 64, 32, 16 with 13 images of the roles above, each image larger than N,
  no bytes after the last image).
- `ProceduralTerrainTexturer`: one instance per theme file and N; `Build(x, z, destination)` writes N × N × 4 bytes and returns false for the grey fallback; `BuildArgb` writes the cache layout and
  `BuildRgba` the texels the renderer needs (alpha 255, no second pass). Pure CPU, deterministic, no allocation per cell (three N × N weight planes and the five ramp tables belong to the instance,
  so an instance is not thread-safe). The window offsets of a texel row are worked out once per row. Columns up to 95 are built (column 95 from the flat grid reads, below), rows 0 to 84; `CanBuild` says which.
- `ProceduralTextureCache`: the 128 × 128 key table and ring of nodes of `0x572110` / `0x572440`, ported move for move; the slots, builds and ring order are pinned against the original's code (`ProceduralTextureCacheGolden`, below).
- `TerrainVisibleSpans`: the span selection of `0x56BB00` (the four corner rays, their ground points, the hull, the clip and the rasteriser) in single precision, the original's arithmetic operation by operation (`TerrainVisibleSpansGolden`, below).
- `ProceduralTerrainSettings`: the option mapping and the memory clamp of `0x572110`. `ProceduralTerrainRectangle`: the cells that take a procedural texture, read from the level's `Standard.sam`.
- `ProceduralTerrainAtlas`: the texture pages that hold the cache's textures. `ProceduralTerrain`: the per frame work (the cells the spans list, cache, builds, the mesh), owned by `OriginalTerrain`.

## Verification

- **x86 oracle (X27, docs/reverse/ORACLE.md section 7).** The ramp `0x55E710`, the weight plane `0x55E780` and the build routine `0x55F780` run in the Unicorn sandbox on synthetic levels:
  every ramp pair of the edge values and 500 random pairs (600), 500 weight planes at N = 16, 32 and 64, and 48 cells of synthetic levels that give each run mode (C, B, A, BA, CB, CA, CBA),
  random masks and invalid cells. The Python reading (`tools/ppc-analysis/oracle_x86/terrain_reading.py`) agrees on every input; a reading without the quirk differs.
- **Real cells.** `tools/ppc-analysis/oracle_x86/terrain_golden.py` runs the original build routine on the shipped `base.lnd` of the four themes for a seeded sample of cells per size (the four
  corners, 30 random cells at N = 16, 16 at 32, 8 at 64 and two invalid cells, 297 in all, with the three space cells whose validity bytes are odd) and prints the SHA-256 of each result as `ProceduralTerrainGolden.cs`. The C# texturer reproduces all 297 byte for byte.
  The research that preceded this port also compared its Python model with the emulated routine on 33,820 cells (every cell of all four themes at N = 16, plus 300 at N = 32 and 80 at N = 64 per theme) with no mismatch.
- **Synthetic levels, in CI.** `terrain_synthetic_golden.py` builds 27 levels from an LCG and fixed formulas (`terrain_reading.synthetic_level`; `SyntheticTerrain` in the tests is the same generator): each of the seven run modes plus random masks at N = 16, 32 and 64, gradient weights, random ramps, and an invalid-cell level per size, three cells each (81 cells: two inside the grid and the column after its last cell). It runs tp.exe's routine on them and commits the hashes (`ProceduralTerrainSyntheticGolden.cs`); `ProceduralTerrainSyntheticTests` reproduces all 81 without game data, so CI checks the weight interpolation, general ramps, window offsets, N = 64 and the flat reads of the last column.
- **The visible-cell spans** (`terrain_spans.py`, `terrain_spans_golden.py`, `TerrainVisibleSpansTests`). `0x56BB00` with the ray `0x56B940`, the hull `0x56BD60`, the clip `0x56C5A0` (`0x56D590`, `0x56D520`), the rasteriser `0x56D600`, the ray rotation `0x578D20` and `__ftol` runs in the sandbox with the single precision control word (DDSCL_FPUSETUP, ECON-059) and the fog call `0x578CA0` and the move `0x67BB90` stubbed: the Python reading gives the same rows, the same 128 start and 128 end bytes and the same largest corner distance on 406 inputs (random cameras with a yaw, a pitch from 10 degrees up to straight down, a roll, a mirrored matrix, a field of view, an aspect, heights, a maximum distance and a field, plus scenes for the hull branches they rarely reach: a vanishing width, a ray with no height, a hull outside the rectangle, every ray beyond the half distance), every branch of the hull covered. `TerrainVisibleSpans` reproduces 66 of them (`TerrainVisibleSpansGolden.cs`) bit for bit in CI without game data.
- **The cache lookup** (`terrain_cache.py`, `terrain_cache_golden.py`, `ProceduralTextureCacheTests`). `0x572440` runs in the sandbox with the build `0x55F780` and the page and device calls (`0x58DC50`, `0x58C9F0`, `0x590D40`, `0x590DA0`, `0x590F90`) stubbed, on 100 random sequences of 3 to 16 frames over caches of 3 to 12 nodes (frames of up to the capacity plus 3 cells, some hits marked dirty): the slot of each key, the cells the original builds, the ring from the head and the key table after each frame agree with the Python reading, which `ProceduralTextureCache` reproduces on 48 sequences (`ProceduralTextureCacheGolden.cs`).
- **Not original bytes.** The tests keep hashes and parameters only; no image or data byte is committed.

## Drawing (issue #829, package E)

`OriginalTerrain` draws the ground of a level with the procedural textures when the options ask for it and the level has the data (`TryAddProceduralGround`); otherwise nothing changes.

- **Options, applied at the next level load** (`ProceduralTerrainSettings`; `[BIN:TPW-EXE:0x0044D926 option apply]`, `0x0044D998`, `0x00457AC5`, `0x00572110`). PROCEDURALTEXTURING 0, 1, 2, 3 give off, N = 16, 32, 64 (the presets: Low 0, Medium and High 2, Enhanced 3); PROCEDURALTEXTURECACHE 0, 1, 2 give a cache of 1500, 2000, 2500 cell textures
  (Low 0, Medium and High 1). The memory clamp of `0x572110`: with less than 48 MB of RAM every N is 16, with less than 64 MB a 64 becomes 32. OpenTPW takes the RAM figure the preset default reads (the browser assumes 4096 MB, so the clamp never applies there)
  and has no figure for the video card's texture memory, which the original tests as well. The lobby stays static (`0x5D84A0` asks for no procedural textures). A level without `base.lnd` (Theme Park Inc) stays static.
  The original ORs the size bits (`[0x79D86C]` bits 1, 2 and 4, read in the order 16, 32, 64 by `0x457AC5`) into one word and never clears them in the option apply, so within one session an option lowered and raised again keeps the smaller size; OpenTPW reads the option as it stands at each level load.
- **The cells** (`[BIN:TPW-EXE:0x00457CF5 rectangle]`, `0x56F090`, `0x56F380`). The cells with a surface inside the procedural rectangle take a procedural texture, and the frame lists the cells of the visible spans (next bullet): (XStart, YStart) to (XStart + max(0, ProceduralTexturingXOffset) + Width, YStart + max(0, YOffset) + Height), inclusive,
  from the level's `MapInfo.Heightfield*` and `ThemeEngine.ProceduralTexturing*Offset` keys (both are 0, 0, 95, 84 and -1, -1 in every shipped `Standard.sam`, so the rectangle is cells 0 to 95 by 0 to 84, the whole heightfield). The cells outside it keep their static ground texture
  (`OriginalTerrain` keeps a static part for them; there are none in the shipped levels). Cells the spans do not list are not drawn, as in the original. **Column 95** is inside the rectangle and past the 95 cells of the weight grid: the original builds it from the grid's flat reads (the last samples are the first of the next sample row), which the oracle
  confirms on the shipped jungle file (`TheColumnAfterTheWeightGridIsBuiltLikeTheOriginalBuildsIt`) and on the synthetic levels.
- **The visible cells** (`TerrainVisibleSpans`; `[BIN:TPW-EXE:0x0056BB00 visible spans]`, `0x56B940`, `0x56BD60`, `0x56C5A0`, `0x56D600`). Each frame `0x56BB00` puts four rays on the ground and rasterises the hull of the camera and the four points into a start and an end column per row (128 signed bytes each);
  `0x56F090` lists the cells of the rows from the first to below the last row and of the columns from the start to below the end of each row, row by row. The rays are four corner rays like the pick frustum's (`0x56B790`), but with the factor 1 where the pick frustum multiplies the half height by 0.5 (`0x56B7BD`): (-w, h, 1), (w, h, 1), (w, -h, 1), (-w, -h, 1) with h = tan(field of view x pi / 360) and w = h / the aspect word `0x8BCBCC` (height over width), turned by the camera's matrix (right, up, forward, `0x578D20`) and starting at its position (`[0x87AFE8] + 0x30`). The field of view is `[0x87AF58]`, the 90 degrees of the park camera (`0x42A990` to `0x578BE0`; tangent 1) that the span selection keeps whatever the screen shows: OpenTPW's camera is the narrower one that reproduces the video's picture (about 53 degrees, tangent 0.5), so the rays reach twice as far to each side as the screen does and the list is about four times the screen's area. A ray's ground point (`0x56B940`) is where it meets the field's lowest and highest height
  (`[+0x30]`, `[+0x34]` of the level object): a parameter that is negative or beyond half the maximum distance (`[0x8BD4C0]`, which FIRSTPERSONVIEWDISTANCE sets to 200, 325, 450 or 700 original units, `0x44D8F0`) becomes that half, and the point is at the single parameter or the mean of the two. The hull is clipped to the field (the origin to the origin plus the cell counts times the cell size, less one cell on the far sides)
  and scaled to cells before the rasteriser: a falling edge lowers a row's start, a rising one raises its end, an end being the edge plus 0.999999 in single precision, which rounds up to the next whole cell from a value of 32 on (the single nearest to v + 0.999999 is v + 1 only for v of 32 or more; below, the spacing of 2 micro-units is smaller than the margin), so a view that reaches the far edge of a field 32 or more cells wide lists its last column and row, which a smaller field does not. The farthest of the four ground points from the camera (at least 10) is handed to `0x578CA0` (the fog and far plane object `0x87B110`);
  `TerrainVisibleSpans.FarthestCorner` keeps it and the far plane of the park view is that distance (see "Fog and far plane"). The second path `0x56EA90` runs while `[0x790AB0] & 0x16` is set (`0x576A9F`): the two camera modes below (bits 0x2 and 0x4), which switch the textures off; bit 0x10 is tested there but no code that sets it was found, so no mode with bit 0x10 is known to keep the textures on while it runs. OpenTPW draws those modes with the static ground. The same path sets the far plane (see "Fog and far plane"). OpenTPW feeds the span selection with its own camera and the original's values: the position and the axes of `Camera.ViewMatrix` turned back to the field's space (height up), the aspect word from the projection (M11 / M22), 90 degrees (tangent 1; OpenTPW's camera shows a half height of 0.5, so the list is about four times the screen's area, as in the original), the heightfield header's stored lowest and highest height (`+0x20`, `+0x24`, truncated: `0x56E6C0` to `0x56B830` copy them into the landscape object's `+0x30` and `+0x34`) and the ray distance `[0x8BD4C0]` of FIRSTPERSONVIEWDISTANCE (200, 325, 450 or 700, `0x44DBB0`), with no other factor. Nothing narrows the rays or raises the distance for a larger view: a list that outgrows the cache or the 2500 cells of a frame behaves as the original's does (slots are taken twice in one frame, cells past the cap are not listed).
- **The cache** (`ProceduralTextureCache`, 1500, 2000 or 2500 slots): each frame the listed cells, row by row, are looked up, (`0x56F090` builds the list and calls `0x572440` at `0x56F2D0` only while bit 0x80000000 or 0x40000000 of `[0x8BD508]` is set, `0x56F0A2`; the frame function `0x576EC0` sets 0x80000000 when the 64 bytes of the camera matrix differ from the copy at `0x87A680` (`0x5773BE` to `0x5773E1`, only while the flags are clear), with more than one viewport (`0x5773FB`) and always with bit 0x40 (`0x576ECB`), keeps the flag word of the frame's start and restores it at `0x577326`, so a bit set during the frame lasts for it only, while the scene-change setters `0x576574`, `0x576603`, `0x5767CA`, `0x473D9E`, `0x477177`, `0x477FAE` and `0x4786A7`, which set it outside the frame, leave it set: nothing was found that clears it, so after the level's first scene change the list is looked up in every frame. `TerrainListGate` is that logic; an all-hit pass reorders the ring, which is why the lookup frequency matters); a cell that is in the cache keeps its slot and its pixels, a cell that is not takes the least recently used slot and has its texture built
  (the original also rebuilds a hit whose cell word has the dirty bit 0x100, which only the floor art of package F sets). At most 2500 cells are listed per frame (`0x56F090`); a view of more cells than the cache holds takes slots twice in one frame and shows the later cell's texture in both, as the original does.
- **The pages** (`ProceduralTerrainAtlas`; `[BIN:TPW-EXE:0x0058F190 page packer]`, `0x00590350 page placement`). The original packs its small textures into shared pages and takes each texture's coordinates from the texel centre of its first to that of its last texel, `(x + 0.5) / page width`
  to `(x + N - 0.5) / page width` (scale 1, `0x58D170`), so tiles need no gutter and a tile's edge texel is sampled exactly. OpenTPW packs the cache's tiles edge to edge into RGBA pages of at most 2048 texels a side (N = 32 with 2000 cells: one page of 8 MB; N = 64 with 2500 cells: three pages, 41 MB, the
  original's own 25 to 40 MB for N = 64), one dynamic mesh with four vertices per cell in view, rewritten when the cells or their textures change. No mip levels: the original's page surfaces have one level (`0x58B0B0`: no DDSD_MIPMAPCOUNT, caps TEXTURE | SYSTEMMEMORY).
  A cell's corner (x, z) takes the tile's first texel centre, (x + 1, z + 1) the last; u runs along x and v along z, and the turn and mirror bits of the cell word are ignored (`0x46DED0`).
- **Sampling.** The terrain pass of the original uses the state word 0x1256 (`0x590C40`): addresses clamped, MAGFILTER linear, MINFILTER point and the mip filter of the device, no alpha test and no blending (`0x567620`, `0x566840`). OpenTPW draws the pages through the same sampler as the static ground (the graphics
  preset's filter, wrap addressing with the half texel inset): that is the difference [APPROX:COMPAT-010] already records for clamped slots (a clamped slot keeps the preset's MINFILTER instead of point); the pages have no mip levels, so trilinear and anisotropic filtering act on level 0.
- **Build time.** Every listed cell is built before the frame draws, on the desktop and in the browser alike, as the original does (the first frame of a level builds the whole first view at once, with no per frame budget). The hole answers of the pools extension (`[EXT:pools-render]`) are kept per hole version, so a panning frame asks for each cell once.
  Measured (Apple silicon, Release, N = 32): 0.08 ms a cell, 60 µs of it the texturer; the jungle's Home view lists 898 cells (37 ms on the first frame; the span selection's rays are twice as wide as the screen), 1044 at zoom 130, about 1750 at 70 after a turn, and the cap of 2500 at 300, where the 2000 slot cache of the High preset is outgrown and every frame builds 2000 textures (about 70 ms; the original's own behaviour with its wide zoom flag, docs/ENGINE-PERFORMANCE.md).
- **Camera modes** (`[BIN:TPW-EXE:0x0042AB20 camera mode switch]`; plan OPEN-1). Two camera modes switch the procedural textures off and leaving them switches them on: 0x42AB20 takes the mode bits 0x2 (the terrain camcorder, left through `0x42AE70`; docs/INSTANT-ACTION-FINALE.md) and 0x4 (the follow of a ride, `0x42A560`, reached from `0x4E15B0`, which takes the id of an object whose record has flag 0x400000), remembers
  that the textures were on in bit 0x200 of `[0x790AB0]` and calls `0x457A40` (off: clears 0x4000 of `[0x7A1A8C]`, `0x46DED0(0)` restores the turn and mirror bits) on entering and `0x457A00` on leaving, which turns the texturing on again only when `[0x78D8D8]` is 1 and otherwise clears the bits 0x6000. The names are inferred from what the code does, not from a string in the executable. OpenTPW draws the static ground in the ride camera and the free walk camera.
- **Alpha.** Cell textures are drawn with alpha 255 (`BuildRgba`); the terrain shader is not changed. [translucent terrain meshes, #834, keep their own pass.]

## Fog and far plane (UI-107)

FIRSTPERSONVIEWDISTANCE is read only by the option apply `0x44D8F0` (the part from `0x44DBB0`); OpenTPW applies it at once. What tp.exe does with it, from the code:

- **The option** (`[0x7858B4]`, the 17th word of the option table at `0x785874` counting TEXTUREQUALITY as 0; the shipped Low, Medium and High files give 0, 1 and 2): `0x44DBB0` to `0x44DC23` store the ray distance `[0x8BD4C0]` (the span selection's maximum distance) and call `0x583280(start, end)`, which keeps `[0x8BD490]` = start, `[0x8BD494]` = end, `[0x8BD498]` = 1 / (end - start) and the squares:

  | Option | Ray distance `[0x8BD4C0]` | Fog start `[0x8BD490]` | Fog end `[0x8BD494]` |
  | ---: | ---: | ---: | ---: |
  | 0 | 200 (`0x43480000`) | 50 (`0x42480000`) | 60 (`0x42700000`) |
  | 1 | 325 (`0x43A28000`) | 100 (`0x42C80000`) | 110 (`0x42DC0000`) |
  | 2 | 450 (`0x43E10000`) | 135 (`0x43070000`) | 150 (`0x43160000`) |
  | 3 | 700 (`0x442F0000`) | 230 (`0x43660000`) | 250 (`0x437A0000`) |

  (original units; any other value leaves the earlier ones). The option FOG at `[0x7858A0]` (0 or 1) only clears bits 0x6000 of `[0x8BCBC8]`; LIGHTING (`[0x7858B8]`) sets or clears 0x1800.
- **To the device, every frame.** The view's frame function `0x576A00` runs the span selection (`0x56BB00`; or `0x56EA90` while `[0x790AB0] & 0x16`, `0x576A9F`) and then calls the virtual at +0x18 of the object `0x879AB0` with (`[0x8BD490]`, `[0x8BD494]`, `[0x8BD47C]`) (`0x576B4D` to `0x576B55`; the thunk `0x569D10` jumps to `0x567AD0`, `SetFog`). `SetFog` keeps the colour (`0x5832E0`), sets `[0x8BD498]`, and sets the render states 0x30 (range fog) to 0, 0x22 (fog colour) to the colour and, unless a bit of 0xC000 is set in `[0x8BCBC8]`, 0x23 (table mode) to 3 (linear), 0x24 (start) and 0x25 (end) to the raw floats; with 0xC000 set the table mode is 0 and the fog is per vertex instead (`0x5747D0` and `0x574960`, from `0x565590`).
  The bits 0xC000 are set only by the device probe (`0x566840`: `0x566AF1` sets 0x8004000 for vendor 0x1002 device 0x4742, `0x566B35` sets 0x4000 for S3 0x5333 device 0x8A22 and ATI 0x5246 and 0x4C42), a workaround for old devices; OpenTPW does not apply it.
- **The fog colour** `[0x8BD47C]` is the level's `ThemeEngine.FogColour` (`0x4080E0`), also the viewport clear and the sky's haze. The lobby (`0x5D8B50`) saves the flags, colour, start and end (`this + 0x34` to `+0x40`), sets the colour 0xFF44DDFF, start 50 (`0x42480000`) and end 300 (`0x43960000`) and restores them on leaving; the front-end screens (`0x5E1421`, `0x5E199A`, `0x5E24BC` through `0x5D96A0`) set colour 0xFF44DDFF, start 50 and end 250.
- **What the fog measures.** The projection `0x578BE0(near, far, fov)` has x and y scaled by cos(fov / 2), z by sin(fov / 2) / (1 - near / far), and w = sin(fov / 2) x the view depth (`[0xB]`; with bit 0x200 of `[0x8BCBC8]` w = 1 instead, and no setter of that bit was found in the park views). The vertex projection `0x579690` stores 1 / w as the rhw and the software fog `0x5747D0` compares the same depth with start and end, so the table fog is taken to measure w: sin(45 degrees) = 0.7071 x the view depth at the park camera's 90 degrees. Which of w and z the device uses for table fog is not read; the fog values are in world units, which only the w reading explains, and it agrees with the PC video (the High preset's 135 to 150 is not reached at the top of the Home view, whose w is about 112).
- **The far plane.** The camera init and the frame setup build the projection with near 0.1, far 1000 and 90 degrees (`0x42AA53`, `0x44E277`, `0x54E874`); the park view's span selection then hands its farthest corner distance (at least 10) to `0x578CA0` every frame (`0x56BD3E`), which rebuilds the projection with it (`0x578BE0(near, d, fov)`) and the depth mapping (`0x583220`). The ride and walk views' `0x56EA90` hands it 1000 first (`0x56EAC0`) and, after it has selected its cells, the greatest depth of their boxes, at least 100 (`[this + 0x1C]`, grown by `0x56E7E0` over the boxes `0x561BC0` classes, `0x56ED79`); the lobby builds near 0.1, far 1000 and its own field of view (`0x5D8CB0`). Nothing past the far plane is drawn: objects, paths and rides are cut, the sky has no depth.
- **The zoom flag changes nothing here.** The inputs of `SetFog` are written only by the option apply, the lobby and front-end screens and the level load, and the far plane's only by the callers above, whose argument comes from the span selection or the cell boxes of `0x56EA90` (both from the camera's pose through the view matrix) or is the constant 1000. The camera's wide zoom clamp (the flag `0x42A520` toggles, 40 to 300 at `0x42C621` to `0x42C6A1`; the bit 0x200 of `[0x790AB0]` is a different thing: it remembers that the textures were on, `0x42AC8D`, `0x42ACF4`) changes only the eye through `0x42B1C0`, so with the flag the same formulas give the fog and the far plane for the same inputs.

OpenTPW (`RenderFog`, `Camera.ParkFarPlane`, `TerrainViewSpans`): the shaders fog linearly in the projection's depth (`gl_FragCoord.w`), `start` and `end` being the option's values times 0.2 world units over 0.7071 (the lobby's in its own units over sin(ISLANDFOV / 2)), multiplied by the graphics preset's view distance scale (`[EXT:COMPAT-GFX-VIEWDISTANCE]`, 1 in the original presets and 2 in Enhanced); the park view's far plane is the span selection's farthest corner distance times 0.2, the ride and walk views' far plane (the second bullet's `0x56EA90` value, not ported) and the lobby's keep OpenTPW's own (10000 world units). The ground ends in the fog colour as in the original; the edge of the span list is the original's too.

What that shows, for OpenTPW's camera (identical to the original's, `UI.md` UI-101; the ground at height 0 in the centre column, the depth in original units x 0.7071, `w`):

| Zoom | 40 | 70 | 90 | 110 (Home) | 130 | 150 | 170 | 200 | 230 | 300 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| w at the top of the screen | 98.9 | 99.0 | 105.3 | 112.5 | 119.9 | 127.1 | 134.0 | 148.9 | 171.2 | 223.3 |
| w at the bottom | 16.5 | 33.0 | 45.6 | 59.4 | 74.5 | 91.0 | 109.0 | 134.7 | 154.9 | 202.0 |

| Setting (start to end) | Fog reaches the screen's top | The whole screen is fog (bottom edge at the end) | Home (110) |
| --- | --- | --- | --- |
| 0 (50 to 60) | already at 40: the top is fog at every zoom | from about 96 the bottom starts to fade, from 111 the view is all fog | all fog but the bottom rows |
| 1 (100 to 110) | starts at about 73, complete from about 103 | bottom starts at about 160, all fog from 171 | the top 10 % is fog |
| 2 (135 to 150) | starts at about 173, complete from about 201 | bottom starts at about 200, all fog from 223 | no fog (the PC video) |
| 3 (230 to 250) | never within 40 to 300 (w 223 at 300) | never | no fog |

At the High preset's setting 2 the ground therefore starts to vanish at the top of the screen at a zoom value of about 173 and the whole view is fog colour from about 223, and at 300 the view is all fog; at setting 3 nothing fades. The extra range the wide zoom adds is thus not usable at settings 0 to 2 in the original either.

## Open

- **The camera modes' second span path** (`0x56EA90`, used while the textures are suspended) is read and not ported: those modes draw the static ground. The cells a view lists beyond the 2500 per frame (`0x56F090`) are not drawn, as in the original; the shipped views stay far below that.
- **The mip filter of the terrain pass** (the object field behind `0x567620`'s MIPFILTER) and the writer of the option into it: not traced, and without mip levels in the pages it makes no difference to OpenTPW's picture.
- **The camera modes' names** (above), and whether a menu, a pause or loading suspends the textures: no suspending code was found for them.
- **The "not allowed" check** (plan OPEN-5): which device check sets `[0x8BD550]` bit `0x10000000` ("Procedural textures not allowed") is not identified; OpenTPW never refuses.
- **The 16-bit paths** (plan OPEN-6, `0x562F70` for 565, 1555 and 4444 pages) are read, not run, and not ported.
- **The floor art tail** of `0x55F780` (rides and shops) and the dirty-bit rebuilds of `0x46DF50`: package F.
