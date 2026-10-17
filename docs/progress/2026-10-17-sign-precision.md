## Sign blur, bevel and relief run at the original's x87 precision (October 17, 2026)

- **Control word read (verdict: 24 bits).** tp.exe's CRT start-up sets 53 bits (`0x6804D0`: `_controlfp(_PC_53, _MCW_PC)` through `_cinit` `0x67F8F0`).
  The display init (WinMain `0x45A960` -> `0x44E080` -> `0x563460`, before the main loop that composes every sign on the same thread) calls
  DirectDraw `SetCooperativeLevel` with DDSCL_FPUSETUP and no FPUPRESERVE (`0x563914`: `0x851`, `0x563B92`: `0x808`) and starts Direct3D (`0x573080`).
  The DirectX 6.1 DLLs shipped in the game's Redist folder, read statically, set the rest: `DDRAW.DLL` turns `0x800` into a local flag
  (`0xBAABE771`) and `D3DIM.DLL` CreateDevice (`0x56664F87`) then sets CW = (CW & 0xFCFF | 0x3F) & 0xF3FF (24 bits, all exceptions masked) and never restores it.
  The only later writer, the render function `0x576EC0` (`0x591220`, `0x591240`), forces 24 bits for the frame and restores the word it found; its
  PC=64 restore (`0x591250`) needs bit 1 of `[0x8BD508]`, which no instruction sets. The sign generator is not reached from that function.
  `_ftol` (`0x67A830`) and the CRT `pow` (`0x67C5F2`, keeps the caller's precision) preserve the precision bits.
  Review, from the same DLLs: the device class `[+0xF58]` is chosen in CreateDevice (`0x56664DD8..0x56664EEA`; constructors `0x56665000` class 0, `0x566651AA` class 1,
  `0x566651DB` class 2). A non-HAL device is class 2 unless the registry values `DisableDP` or `DisableDP2` (`HKLM\Software\Microsoft\Direct3D`) are set; a HAL device is class 2
  only when the driver provides DrawPrimitives2 (`D3DHAL_CALLBACKS3`, global `+0x638`, field `+0x14`), otherwise class 1 or 0, which leave the control word at the CRT's 53 bits.
  tp.exe references only the HAL device GUID (`0x572C8C`), so the 24-bit verdict holds for DirectX 6 class drivers (and any later runtime), and a machine with a DirectX 5 style
  HAL driver would have composed signs at 53 bits. No thread other than WinMain's reaches the sign generator in the direct call graph; no instruction sets bit 1 of `[0x8BD508]`
  (the log flag word: its stores only touch bits 0, 6, 8, 24, 25, 27, 28, 30 and 31, and its file dump `c:\tcache.log` only writes it).
- **The arithmetic follows the instructions.** `Ext` emulates x87 registers (64-bit mantissa, rounding of FADD, FSUB, FMUL, FDIV and FSQRT at the precision
  control, exact loads, float and double stores; integer-only apart from sine, cosine, 2^x - 1 and log2, which the x87 does not round at the precision control either).
  The Gaussian kernels (`0x5EA8A0`, `0x5EB3E0`: the tap sequence with FRNDINT, F2XM1, FSCALE, stored as doubles, normalised by the register-width sum), their
  convolutions (accumulator in a register, `_ftol` truncation), the bevel levels (`0x5EB270`) and the relief (`0x5EB8A0`: light and half vectors, normals, dots,
  `pow` as FYL2X + FRNDINT + F2XM1 + FADDP + FSCALE, colour sums stored as floats, the clamp `0x5EBE50`) are ported in the original's operation order, so
  details such as `u * (u * u)`, `(ay² + az²) + ax²` and the unrounded `s * ay * 0.5` are as in the binary.
- **The precision matters.** Composed at 24, 53 and 64 bits the 75 corpus signs differ (53 vs 24: 77 of 80 composed signs differ, by up to 228 in a byte, because
  truncated sums sit on integer boundaries), so matching the thread's precision is what reproduces the original. At 53 bits 69 of the 75 frozen halves
  equal the previous double-precision baseline, which checks the port.
- **`[EXT:sign-coverage-precision]` retired.** Its blur-precision half is the above. Its glyph-coverage half is now an open `APPROX:COMPAT-003`: tp.exe draws each line
  with GDI (LOGFONT `lfQuality` 4 = ANTIALIASED_QUALITY, grayscale smoothing, through Uniscribe `ScriptStringOut`) onto an 8-bit gray DIB twice the mask's size, from the fonts in
  `fonts.wad`. The gray levels GDI's rasterizer writes there (level count, oversampling, hinting, gamma, the Windows version the game ran on) are in neither the game files nor
  tp.exe, so OpenTPW's own coverage (`[EXT:sign-text-antialias]`, user approved October 16) stands in; a captured original sign texture would close it.
- **Register tool** learns `BIN:D3DIM-DLL` and `BIN:DDRAW-DLL`. **Re-pins:** 72 of 75 frozen sign half pairs and the five `RidesRound2Tests` signs (no line, text pixel count
  or `.sgn` hash moved); the cause is in the baseline's header.
- **Seen on the way:** COMPAT-018 (`SignWaveletDecoder`) still lists the x87 precision at the wavelet conversion as open; the same verdict (24 bits at that call) would apply
  there but its arithmetic was not ported here.
