## Sign text raster, relief shape and Uniscribe kerning read (October 17, 2026)

- **COMPAT-006 retired (usp10.dll, now allowed for static analysis):** the shipped Uniscribe places a Latin run by summing per-glyph
  advances from its width cache (`ScriptPlace` `0x6510CEDE`, the generic routine `0x65113458`, the cache `0x6510F241`) and never asks
  for a `kern` table (`GetFontData` has six call sites, for `JSTF`, `maxp`, `cmap`, `OS/2`, `GSUB` and `GSUB`/`GPOS`/`GDEF`, none for `kern`; no `kern` tag, no `GetKerningPairs` import). Sign text is not
  pair-kerned, as OpenTPW draws it. The register tool learns the `BIN:USP10-DLL` label. A later Windows copy of Uniscribe is not among
  the files and is not read.
- **COMPAT-001 retired (`0x5EAB70`, `0x5ECB40`):** the sign line is drawn on an 8-bit gray DIB twice the mask's size and reduced by a
  2x2 box, the four bytes summed and shifted right by 2 (this port halved twice with truncation). The surface, both glyph masks and the
  paint shaping are 256 rows high; only the top 128 rows are packed into the textures, so the rows below feed the morphology, the
  blur, the relief neighbours and the relief's measured text extent as they do natively. `[EXT:sign-text-mask-width]` is gone.
- **COMPAT-003 narrowed (`0x5EB820`, `0x5EB270`, `0x5EA1F0`, `0x5EB3E0`, `0x5EB8A0`):** the relief is not lit from the raw coverage. Each
  effect builds a shape first: the binary ink is eroded once per count of the record's mask word (every removed pixel keeps a smoothstep
  level, the core stays 255), then blurred by the record's first parameter with a square Gaussian where the glyphs have coverage. The
  normals come from that shape, read without a clamp at the row ends; the colour sum follows the original's order. The signs now show
  the bevelled, glossy letters of the original instead of flat-edged ones. Open: the x87 precision the relief arithmetic ran at.
- **Hash baselines:** every left/right half hash of the 75 corpus signs moved twice (the raster reduction and 256 rows, then the shape)
  and the text pixel counts of 8 signs moved once (the reduction); `Font/sign-hash-baseline-v0713-dc3b1aef.tsv` and
  `RidesRound2Tests` are re-pinned with the causes in the baseline's header. No line, `.sgn` hash, determinism pin or save changed.
- **Awaiting the user:** whether the remaining COMPAT-003 part (the relief's float arithmetic at an unknown x87 precision, computed in
  double precision with float stores) should become an extension like the double-precision blur sums.
