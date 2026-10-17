## Sign text coverage follows the documented GDI grayscale model (October 17, 2026)

- **What changed:** `TrueTypeRasterizer` measures coverage with `CoverageModel.GdiGray4` by default: 4x4 point samples per pixel (the 17 levels of GGO_GRAY4), written as
  `(level * 255 + 8) / 16`, no gamma, no hinting. `[EXT:sign-text-antialias]` (16 linear sub-scanlines) is gone; `CoverageModel.Linear` stays for the scanline-window tests.
- **Sources (Windows 2000/XP behaviour chosen, Windows 98 differs and is documented):** GetGlyphOutline (GGO_GRAY2/4/8 = 5/17/65 levels), the Microsoft font blog on gasp
  (grid-fit, overscale, downsample), the OpenType `gasp` specification, LOGFONT, SystemParametersInfo (the contrast setting is ClearType only), the archived FontQuality page and
  FreeType's interpreter table; URLs in docs/COMPATIBILITY.md, "Sign text antialias model (COMPAT-003)".
- **Shipped fonts:** none of the 17 has `gasp`; all carry hinting programs (`fpgm`, `prep` with `SCANCTRL 0x1FF` / `SCANTYPE 1`, `cvt `) that OpenTPW cannot run (no bytecode interpreter), so the
  outlines are sampled unhinted at the 65 to 256 ppem the signs use.
- **Still open (`APPROX:COMPAT-003`):** the 4x4 factor itself (inferred from GGO_GRAY4), sample positions, hinting, dropout control, the level to byte mapping on the 8-bit DIB and the Windows
  version; no GDI capture exists. Nothing here claims exactness.
- **Effect:** total ink within 0.2 percent of the previous coverage, at most 22 levels difference per mask byte, no thin stroke lost (checked on 40 corpus masks and zoomed crops).
- **Baselines:** 150 half hashes and 75 text pixel counts of `Font/sign-hash-baseline-v0713-dc3b1aef.tsv` and the five `RidesRound2Tests` signs re-pinned (causes in the TSV header and
  docs/DETERMINISM.md); no line, `.sgn` hash, world hash or save moved.
- **Other pins:** the 'A' hashes of the 17 fonts and the gate name line hash in `TrueTypeFontTests` (original data) moved with the coverage and were re-pinned.
