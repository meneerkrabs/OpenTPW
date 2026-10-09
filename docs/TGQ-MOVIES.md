# TGQ movies: container, EA-XA audio and TQI video CPU decoding evidence

October 9, 2026. Status: CPU container/audio/video decoding implemented for the
nine local `Data/Movies/*.tgq` files. Audio PCM matches an external FFmpeg 8.0.1
oracle bit-exactly (over the samples FFmpeg emits); video planes are **close but
not bit-exact** to that oracle. No playback, A/V sync, renderer or game
integration exists. The original player's colour conversion is not verified.
No dependency or movie data is added to the repository.

Code: `source/OpenTPW.Files/Formats/Video/TgqMovieFile.cs`,
`source/OpenTPW.Files/Formats/Video/TqiDecoder.cs`,
`source/OpenTPW.Files/Formats/Sound/EaXaAdpcmDecoder.cs`;
tests: `source/OpenTPW.Tests/TgqMovieFileTests.cs`.

## Container

Little-endian EA chunks: ASCII FourCC, u32 size including the 8-byte preamble.
All nine files parse exactly to EOF. Observed and enforced order: `SCHl` first
(once), then interleaved `pIQT` video and `SCDl` audio, one 4-byte `SCCl`
(audio block count, before the first `SCDl`, must equal the `SCDl` count),
empty `SCEl` last with nothing after it. Any other FourCC (including the
documented but unobserved `SCLl`) is rejected as unsupported.

| File | Bytes | pIQT | SCDl (=SCCl) | Audio samples/channel | Audio s | Video s @30 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `bf.tgq` | 2,462,896 | 255 | 265 | 194,815 | 8.83 | 8.50 |
| `bub.tgq` | 20,099,276 | 1,174 | 1,174 | 862,999 | 39.14 | 39.13 |
| `buc.tgq` | 19,926,420 | 1,141 | 1,142 | 839,443 | 38.07 | 38.03 |
| `grav.tgq` | 20,301,804 | 1,212 | 1,213 | 891,701 | 40.44 | 40.40 |
| `jug.tgq` | 25,925,296 | 1,150 | 1,151 | 846,094 | 38.37 | 38.33 |
| `mir.tgq` | 15,862,512 | 1,360 | 1,361 | 1,000,591 | 45.38 | 45.33 |
| `plan.tgq` | 14,828,744 | 1,138 | 1,525 | 1,120,542 | 50.82 | 37.93 |
| `roc.tgq` | 14,301,112 | 990 | 991 | 728,347 | 33.03 | 33.00 |
| `roll.tgq` | 17,553,568 | 992 | 993 | 729,817 | 33.10 | 33.07 |

Totals: 9,412 video frames, 9,815 audio blocks. Every file has exactly one
`SCHl`, `SCCl` and `SCEl`. `plan.tgq` audio continues ~12.9 s past its last
video frame; playback behaviour after the last frame is unknown.

### SCHl audio header

Identical in all files except the sample count: `PT`, platform u16 0 (PC),
then the patch substream (tag, length byte, big-endian value; `0xFC`–`0xFE`
carry no data, `0xFF` ends; `0xFF` length means a 4-byte BE length follows):

`00:02  06:65  1B:1E  FD  85:<samples>  82:02  83:07  8A:00000000  FF`

- `0x1B` = 30: frame rate per the SCxl page. The multimedia.cx TQI page says
  15 fps; the corpus contradicts it (8 of 9 files: audio and 30 fps video
  durations agree within 0.34 s; FFprobe also reports 30 fps).
- `0x82` = 2 channels, `0x83` = 7 (EA-XA ADPCM), `0x85` = per-channel
  sample count. No `0x84` (rate) tag: the documented default 22,050 Hz applies;
  no `0x80` (revision) tag.
- `0x00`=2, `0x06`=0x65 have no verified meaning; `0x8A` is documented as
  data-less but here carries a 4-byte zero value. Tags are retained raw.

## Audio: EA-XA ADPCM, stereo

Each `SCDl` payload: u32 sample frames, then per channel int16 current and
int16 previous predictor state (left then right), then 30-byte frames: byte 0
= predictor indexes (left high nibble, right low), byte 1 = shifts (`nibble +
8`), 28 bytes each holding one left (high) and one right (low) 4-bit sample.
`sample = ((nibble << 28) >> shift + cur*c1 + prev*c2 + 0x80) >> 8`, clamped,
with `(c1, c2)` = `(0,0) (240,0) (460,-208) (392,-220)`. The predictor state is
reset by every block header. The final frame of the final block is partial
(e.g. `bf.tgq` 775 = 27×28 + 19); after the encoded frames, 0 or 2 zero
alignment bytes are observed. Stricter than observed, the decoder rejects
predictor indexes > 3, nonzero padding or ≥ 4 slack bytes.

Verification: per file, the sum of block sample counts equals the header
`0x85` value, and decoded PCM equals FFmpeg's `adpcm_ea` output on every sample
FFmpeg emits. FFmpeg drops the partial last frame (3–25 samples per file and
logs "invalid number of samples in packet"); OpenTPW keeps the header count.

## Video: TQI

`pIQT` payload: u16 width, u16 height, u8 quantizer, 3 unknown bytes. All
9,412 frames: 320×352, quantizer 99, unknown bytes `14 16 03` (the wiki says
"always zero"; meaning unknown, retained as `UnknownBytes`). Every frame is
intra-coded.

Bitstream (verified against the corpus and the oracle): MSB-first bits read
from little-endian 32-bit words. Macroblocks in raster order, each Y0 Y1 Y2 Y3
Cb Cr with no macroblock header. Each block is an MPEG-1 intra block:
`dct_dc_size_luminance/chrominance` VLC (ISO/IEC 11172-2 B.12/B.13, sizes 0–8),
DC differential with per-component predictors starting at 0, then Table B.14
run/level codes + sign, MPEG-1 escape (6-bit run, 8-bit level, 0x00/0x80
extension), end-of-block `10`, standard zigzag. The wiki claims a different
run/level table; the standard MPEG-1 values are what match the oracle (PSNR
~55 dB versus ~21–40 dB for wrong scan variants during investigation).

Dequantisation: `F[0] = dc × 8`, `F[i] = level × W[i] × (107.5 − q) × 0.625 / 8`
with `W` the MPEG-1 default intra matrix (wiki "base_table2"); in integer form
`qscale = (215 − 2q) × 5` and `F[i] = level × W[i] × qscale / 128`. DC is in
pixel units (no +128 offset). Every frame ends within its final 32-bit word
with zero padding bits; the decoder requires that.

### IDCT: integer variants measured against the oracle

Oracle: FFmpeg 8.0.1 `tqi`, `-pix_fmt yuv420p`, run locally only; no FFmpeg
or other third-party decoder source was consulted or copied. Each candidate
was implemented from the published algorithms and compared sample-by-sample
(scratch harness outside the repository):

| Candidate | `bf` identical / PSNR | `plan` identical / PSNR |
| --- | ---: | ---: |
| Orthonormal float IDCT, round half up (previous decoder) | 84.9 % / 56.16 dB | 82.3 % / 55.48 dB |
| IJG-style AAN IDCT (float, prescaled) | ≈ float | — |
| MPEG reference Chen–Wang 11-bit IDCT (4 dequant roundings) | 80.4–82.2 % / ≤55.6 dB (2 frames) | — |
| IJG-style integer AAN, best of ~4,000 precision/order/rounding variants | 94.6 % / 60.8 dB (1 frame) | — |
| **Transposed AAN (adjoint of the AAN forward DCT), integer, selected** | **94.6 % / 60.70 dB** | **90.4 % / 58.22 dB** |

Selected arithmetic (`TqiDecoder.InverseTransform`): AAN prescale folded into
the dequantisation table, `floor(round(2^17·s(u)·s(v)) × W × qscale / 2^18)`
(6 fraction bits; `s(0) = 1/(2√2)`, `s(k) = 1/(4 cos(kπ/16))`); the
transposed AAN flowgraph with 12-bit rotation constants (2896, 2217, 5352,
1567) and floor products; columns first with a 1-bit floor shift, then rows;
output `(x + 8) >> 5`, clipped. Precision (6–9 bits tested), pass order, the
mid shift and the quarter-LSB bias were chosen by search; constant precision
(9–12 bits) barely matters. The quarter-LSB bias is unexplained: the oracle
rounds slightly downward relative to round-half-up in every variant tried.

All 9,412 frames (this decoder vs the oracle):

| File | Identical samples | Mean PSNR | Worst frame | Max abs diff |
| --- | ---: | ---: | ---: | ---: |
| `bf` | 94.6 % | 60.70 dB | 57.98 dB | 4 |
| `bub` | 88.2 % | 57.30 dB | 55.69 dB | 4 |
| `buc` | 87.0 % | 56.90 dB | 54.53 dB | 4 |
| `grav` | 87.2 % | 57.00 dB | 55.02 dB | 4 |
| `jug` | 83.9 % | 55.95 dB | 54.53 dB | 4 |
| `mir` | 90.9 % | 58.47 dB | 56.04 dB | 4 |
| `plan` | 90.4 % | 58.22 dB | 56.49 dB | 4 |
| `roc` | 90.0 % | 58.06 dB | 56.13 dB | 4 |
| `roll` | 87.7 % | 57.13 dB | 54.70 dB | 4 |

(Previous float decoder over `bf`, `bub`, `mir`, `plan`, `roll`: 79–85 %
identical, 54.6–56.2 dB, max difference 5.) **Still not bit-exact.** Evidence
of the remaining gap: for blocks with one AC coefficient the oracle output is a
deterministic function of the level, so the residual is IDCT rounding, not
bitstream or dequantisation. Of the `plan` blocks holding one horizontal or
vertical frequency-1 coefficient, 93,921 of 95,345 (20 of 29 distinct levels)
reproduce exactly, and 6,332 of 6,334 frequency-2 blocks; the misses are ±1 in
one or two columns. Frequency-1 patterns over all levels fit neither IJG-AAN
nor transposed-AAN arithmetic with any tested precision, product rounding
(floor, ceil, nearest, toward zero) or bias, so the original uses a different
factorisation or rounding order. The synthetic test pins two of those oracle
patterns.

Colour: `TqiFrame.ToRgb24()` and the GPU path use full-range BT.601 with
nearest-neighbour chroma. Supporting evidence (oracle planes, every fifth frame
of `bf`/`plan`): luma clips at both ends (Y = 0: 0.46 %/0.87 %, Y = 255:
0.43 %/0.73 %) and 1.1 %/2.2 % of luma is below 16, which limited-range video
would not show. Chroma stays within 16–240 for > 99.9 % of samples. The
original player's conversion is still **not verified** against captures.

### Pinned hashes (SHA-256)

Files:

| File | SHA-256 |
| --- | --- |
| `bf.tgq` | `174723130d5f3f03da58c26f6b162b886267f9ba694792df6bc534847704a32d` |
| `bub.tgq` | `338764e9d40e3e9b49cf8356ea62c4bea6e6701a79d74fcedd8fbb6f9683e5e1` |
| `buc.tgq` | `50f751e8195dbe6784037a29776b46480b75ccae542bf1ee77373dd3bbff6a44` |
| `grav.tgq` | `fe6531e19fa0a9aea2c9ea88428898e11fba91034515818076e036061e85ebfd` |
| `jug.tgq` | `acbde3241eb1268f525c4e50af2fcefbfef057cb0583e8831c2d63bafa2d4e69` |
| `mir.tgq` | `ffbceb6864ddda516922aceaf4ed2c55e463e663d49230ea03a2e14e4bea55a7` |
| `plan.tgq` | `9ac41b21b05527b76be611bcb6951d05f461092a20906fa732701e864abd0ff1` |
| `roc.tgq` | `0906c4862f9fb594b60b9c11afd7f657fc97de82d60ef3cc4d7cd210d404e72d` |
| `roll.tgq` | `2bb0194447ae9dce24d6e15c7d9c53177fff1356e85bb6d8902dc68a69cb3fe0` |

Decoded PCM (interleaved s16le) — `bf` `9f7b0e0d…647c8`, `bub` `bd5cf5a5…3255f`,
`plan` `91839600…fadcf`; all nine are pinned in full in the tests.

Decoded planes (Y‖Cb‖Cr, this decoder; not FFmpeg's bytes):

| Frame | SHA-256 |
| --- | --- |
| `bf.tgq` #0 | `fa16bdec3febbcbed042110427c6e53fe48d338ed51d218fc2cfdd814633ff08` |
| `bf.tgq` #127 | `7199c61776b43c297cd8219e9d8501e43c201bb46417bea705a97eefc1cc5993` |
| `bub.tgq` #0 (identical first frame in the other 7 non-`bf` files) | `83fe6707db6c43d97d195b1cb5347e7ccd92cfbbbb7dac13fd5c303754c108f5` |
| `bub.tgq` #587 | `1fd72a9ee1f98ae101b2cb1e0ed3caadb014bf64bf5fc582bd9070f381a4e1f5` |
| `plan.tgq` #569 | `7d9a9672f871c15cfc849644a55a850403875f5f6c7f130d48774c60e4a53031` |

## Limits and strictness

Input ≤ 64 MiB, chunk ≤ 1 MiB, ≤ 65,536 chunks, ≤ 16 Mi samples/channel,
frame dimensions multiples of 16 up to 1024, quantizer ≤ 107. Bitstream words
must be whole; invalid VLCs, DC sizes > 8, more than 63 AC positions, forbidden
escape levels, truncated bitstreams, whole unused trailing words and nonzero
padding bits are rejected. Only PC/stereo/EA-XA (no revision tag or revision 1)
audio decodes; other variants throw `NotSupportedException`. Caller streams stay
open; nonseekable short reads are supported. Asset tests are inconclusive (not
passing) without `OPENTPW_GAME_PATH`; the all-frames test takes ~1 min in Debug.

## Remaining gates

Bit-exact IDCT (only if original-player captures prove it matters), colour
matrix/range against original-player captures, playback clock/A-V sync
(including `plan.tgq`'s audio tail), GPU upload/presentation and where the game
triggers each movie. None of these are verified.

## Sources

- OpenTPW docs `c077cc9a12aaaac93caac52673ab3080f631fbd9`, `src/formats/tqi.md`.
- multimedia.cx wiki: Electronic Arts TQI (rev 15985), Electronic Arts TGQ
  (rev 15826), Electronic Arts SCxl (rev 15794).
- ISO/IEC 11172-2 (MPEG-1 video) VLC tables B.12–B.14 and default intra matrix.
- FFmpeg 8.0.1 `ffprobe`/`ffmpeg` binaries used only as an external oracle for
  counts, PCM and plane comparison; no FFmpeg source consulted or copied, no
  original game binary executed.
