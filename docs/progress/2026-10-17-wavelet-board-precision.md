## Sign boards decode at the original's x87 precision, instruction for instruction (October 17, 2026)

- **Where the wavelet decode runs (verdict: 24 bits).** The decoder object is built by `0x5916C0` through `0x5985C0`, from two places only: the engine init `0x572780` -> `0x564B20`
  (registers the codec, `[0x879A68]`) and the sign generator's own instance (`0x5EBFC0`, called from `0x467A80`). In `0x44DF20` the display init `0x5632B0` and the Direct3D start
  (`0x5836D0` -> `0x572C40` -> `0x573080`) both come before `0x572780`, and neither function has another caller, so no decode can run before the device exists and every decode runs
  at the 24 bits of #291 (the same verdict, caveat and thread argument). The decoder methods are the vtable at `0x7026B8` (`0x5987B0` -> `0x592000`, which runs the colour decode `0x592730` or the alpha decode `0x592E60`
  by a flag argument, one call per kind); no thread entry point reaches them.
- **The one 53-bit part: the D4 coefficients.** `0x591600`, `0x591630`, `0x591660` and `0x591690` are C++ static initialisers (table at `0x72BD70`, inside the range
  `0x727000..0x72E0E8` that `_cinit` `0x67F8F0` walks after the FP hook `0x67A5F0` -> `0x6804D0` set 53 bits), so they run before WinMain: `((sqrt 3 - c) * 0.25) / sqrt 2` with
  every operation rounded at 53 bits and the quotient stored as a float32. `SignWaveletDecoder.D4Coefficients` computes them on `Ext` at 53 bits; the old closed form
  differed in operation order.
- **What was read and ported.** The header scale multiplies each dequantised coefficient (`0x592B80` and the copy inlined in `0x592E60`: table float times scale, `fstp dword`), it does
  not enter the filter (the decoder used to fold it into the row pass's D4 taps; this closes the "where the scale enters" question of COMPAT-018). The 1-D inverse `0x592C50`
  has five forms: the steady software-pipelined loop, its last pair (a different wave order), the wrap pair 0, and a short-row loop for rows under six samples; the column passes
  `0x5933A0` (colour) and `0x592E60` (alpha) are copies of it along the rows (the last column of each row pair takes the last-pair wave, and the two wrap loops add the smooth products in
  different orders). They were derived with a small symbolic x87 stack interpreter over the disassembly, so the operand order is the binary's, not a transcription. The colour conversion
  `0x5935A0` works per 2x2 block: the chroma terms `Cb*1.772 + magic`, `Cr*1.402 + magic` and `(magic - Cb*0.344136) - Cr*0.714136` are formed once on the register stack, each luma is added
  to them (a second rounding; the old decoder rounded the whole sum once) and the float's low 9 bits index the clamp table. The alpha conversion (`0x593301`) compares with 0.0 and 255.0
  and truncates (`_ftol`).
- **Registers.** At 24 bits an x87 register rounds like a float32, so the default path runs on floats (`SingleReg`) and the same formulas run on emulated registers (`ExtReg`, `Ext`)
  for any other precision; a test pins that the two give identical bytes on synthetic data and on original boards at 24 bits, and that 53 bits moves pixels. An independent double
  reference of the D4 inverse confirms the structure (rows, columns, wrap, scale), within one level.
- **Effect.** On the 23 original boards the alpha plane moves on at most 8 000 pixels (one level; most boards not at all) and the colour channels on 12 000 to 26 000 of 98 304 components,
  by one level, as the COMPAT-018 note predicted. Rendered boards (gates, ride boards, signs) show no artefacts at the wrap rows and columns. **Re-pins:** 28 left/right half hashes of the 14 signs
  with a wavelet board in the sign baseline TSV and the five `RidesRound2` table-named signs; the cause is in the baseline's header.
- **Header-to-plane order (settled in review).** The PC board reader `0x5E7990` (called by the sign reader `0x5EC3A0`) reads the six header dwords (four floats, two chunk lengths)
  and calls the codec's `0x592000` through the vtable twice: once with flag 1 for the colour planes (the first three floats as the Y, Cb and Cr scales: `0x592730` passes them to the
  three dequantisations in plane order, and `0x5935A0` reads plane 1 as luma, plane 2 as the 1.772 chroma and plane 3 as the 1.402 chroma) and once with flag 0 for alpha (the fourth float).
  That is the Mac loader's order Y, Cb, Cr, alpha, which the decoder already used.
- **The 255/254 alpha rows are the original's arithmetic.** A fully opaque board (the gates, the sign boards) has a constant alpha plane, and it decodes to 254 on even rows and 255 on odd
  rows. The D4 taps are float32, `H0 + H2` and `H1 + H3` differ in the last bit, so the final column pass gives 254.99998 on the even rows, which `0x593301` truncates to 254. An independent
  float32 reimplementation of the verified formulas reproduces every alpha byte of two boards, and a constant plane gives the same pattern at every side, so it is not an orientation
  fault of the decoder (`TheOpaqueAlphaPlaneDecodesToTheOriginalsRowPattern` pins it); it is invisible in the blend.
- **COMPAT-018 narrowed.** The arithmetic and the plane order are now the original's; open are the GPU row orientation and original-platform board pixels.
