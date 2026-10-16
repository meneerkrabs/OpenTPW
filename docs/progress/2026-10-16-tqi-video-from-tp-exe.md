## Movie video through tp.exe's own TQI decoder (October 16, 2026)

Format round 3. The TQI video decoder now reproduces the PC movie player instead of an FFmpeg-fitted IDCT:

- **Matrix** `0x6710C0`: each MPEG-1 weight times `(float)((107 − q) / 16)` times 2^16, truncated, times the Q13 AAN
  prescale with half-up rounding; `M[0] = 2^16`.
- **Block decoder** `0x6747D0`: coefficients go to the transposed scan position `0xFBB5B0` and use the matrix entry
  there, so 21 asymmetric weights apply to the transposed frequency.
- **Transform passes** `0x67673C` / `0x676965`: integer AAN with doubled Q31 rotations, the odd part in double precision
  rounded half to even by the 1.5 · 2^52 trick, a zero shortcut in the first pass, and `>> 17` with no bias, so samples
  are 7-bit.
- **Colour** `0x670350`: the packed three-field tables and the channel tables at the library's default brightness 100.
  Neutral white is (241, 239, 241), so the original never shows full white.

Verification is in the x86 oracle, **X26**:
- `tpw-exe-tqi-matrix` agrees for every quantiser.
- `tpw-exe-tqi-transform-pass` agrees on 500 coefficient rows.
- Outside the suite, the original block decoder was run per macroblock over 51 frames sampled from all nine movies. It
  matched in every sample but one, a half-way rounding where Unicorn's x87 is not IEEE-exact.

No colour field leaves its table over the 1.06 billion pixels of the nine movies. `TqiFrame` now holds the 7-bit samples;
the pinned plane hashes, the synthetic decode tests and the PNG export expectations moved with it.

Still assumed: the FPU precision movies run at (53-bit; 64-bit would differ only in rare ties) and the display surface's
packing. Docs: TGQ-MOVIES.md, FORMAT-BACKLOG.md, README format table, ORACLE.md.
