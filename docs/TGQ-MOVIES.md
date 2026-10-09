# TGQ movies: decoding evidence and playback

October 9, 2026. Status: CPU container/audio/video decoding for the nine local
`Data/Movies/*.tgq` files, plus streaming playback (`--play-movie <name>`)
with audio, A/V sync and GPU presentation, and the start-up movie sequence
(`bf`, then a trailer by day of the month) before the front end. Audio PCM matches an external FFmpeg
8.0.1 oracle bit-exactly (over the samples FFmpeg emits); video planes are
**close but not bit-exact** (84–95 % identical samples, 56–61 dB). No in-game
The triggers come from the Mac PowerPC build (see "Where the original plays
movies"); the PC `.tgq` flow is assumed to match. The original player's colour
conversion and end-of-movie behaviour are not verified. No dependency or movie
data is added.

Code: `source/OpenTPW.Files/Formats/Video/TgqMovieFile.cs`,
`source/OpenTPW.Files/Formats/Video/TqiDecoder.cs`,
`source/OpenTPW.Files/Formats/Video/TgqAudioReader.cs`,
`source/OpenTPW.Files/Formats/Sound/EaXaAdpcmDecoder.cs`, playback in
`source/OpenTPW/Client/Movie/`; tests: `source/OpenTPW.Tests/TgqMovieFileTests.cs`,
`source/OpenTPW.Tests/MoviePlaybackTests.cs`.

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

## Playback

`bash scripts/run.sh --game-path <install> --play-movie bf` plays
`Data/Movies/bf.tgq` (name or `name.tgq`, case-insensitive, no paths) in the
game window and exits when it ends; any new key press or mouse click skips.
`--mute` plays without audio; `--headless` runs the decode-and-clock simulation
with a simulated real-time audio device at 60 updates/s and prints statistics
and a hash of the presented planes (no window, GPU or audio device);
`--smoke-test` is the native GPU check below.

- **Streaming / memory**: the compressed file stays in memory (≤ 64 MiB limit;
  largest movie 25 MiB); video frames are decoded on demand, one at a time;
  audio is decoded one SCDl block at a time (`TgqAudioReader`) and kept at most
  0.25 s + 1,024 frames ahead of the device.
- **Audio**: SDL2's push queue (`SDL_QueueAudio`) through the SDL2 library
  Veldrid already loads (`Sdl2Native.LoadFunction`), s16 stereo at the movie
  rate, SDL converting to the device format. The game had no audio backend
  before this (NAudio is only used by the Windows-only ModKit MP2 preview). If
  SDL audio cannot open, playback logs a warning and runs without audio.
- **Clock**: with audio, movie time is the device's played position minus its
  buffer latency; because the position moves a device buffer (~46 ms) at a
  time, elapsed time fills in between steps, capped at one buffer and never
  running backwards. Without audio (or after a shorter soundtrack drains) the
  existing 60 Hz `FixedStepClock` advances it, so hitches longer than 16 ticks
  lose time instead of jumping ahead.
- **Frame policy**: the frame due at `floor(time × 30)` is decoded; frames
  passed in between are dropped without decoding (every TQI frame is
  intra-coded); otherwise the current frame is held. Playback ends at the later
  of the last video frame and the end of the audio, holding the last frame:
  `plan.tgq` shows its final frame for ~12.9 s while its audio finishes. The
  original player's behaviour here is unknown.
- **Presentation**: CPU BT.601 full-range conversion to RGBA, uploaded to a
  320×352 texture and drawn with the existing fullscreen-triangle blit shader
  into a centred viewport at the original's display aspect 640:352 (the 320×352
  frames are shown at twice their width, as the original's movie box does; see
  below), letter/pillarboxed in other windows.

Measured: headless `bf` and `plan` at 60 Hz with audio decode every frame
(255, 1,138) with no drops and end within one update after the audio; at 12 Hz
`bf` drops > 100 frames but keeps audio time. A real run of `bf` with SDL audio
(macOS, Metal) showed all 255 frames with 0 drops and ended at 8.84 s (audio
8.83 s); before smoothing the stepped audio position it dropped 71.

Native check: `--play-movie bf --smoke-test` (audio off for determinism) plays
to frame 60, reads the resolved framebuffer back at frames ≥ 15 and ≥ 60 and
requires the mean RGB inside the movie rectangle to match the CPU-converted
frame within 6/255 per channel, black bars outside it and a changed picture
between the two reads; it saves `artifacts/native-movie-bf-*.png`.

## Where the original plays movies

Source: static analysis of the Mac PowerPC build (`SimThemePark`, QuickTime
`.mov` files; Ghidra, never executed). The PC `.tgq` flow is **inferred** from
the shared game code, not proven. Addresses are function entry points of the
main code fragment; nothing here is decompiled text.

### Mechanism (proven)

- One movie player routine at `0x1009B1DC` (name, target surface, x/y): it
  resolves the file, opens it with QuickTime, binds it to the game surface,
  sets the movie box, prerolls, sets the movie volume, runs a few
  `MoviesTask` steps and starts it. Companions: `0x1009B404` (task + is-done),
  `0x1009B45C` (stop and dispose), `0x1009B4D8` (`EnterMovies`, called once
  from the start-up routine `0x101C0B28`), `0x1009B4FC` (shutdown).
- This player has **exactly two callers** in the whole fragment (no data
  references to it): `0x101C0DF0` (the logo) and `0x101C0F40` (the trailer).
  Both are called from one place each, the start-up states of the main state
  machine `0x101C1208`. No other code (theme entry, golden tickets, rides,
  advisor) starts a movie. Indirect calls cannot be excluded statically, but no
  movie string or QuickTime import is referenced outside this player.
- Both callers first look the file up (`0x100036B0`); a missing movie is skipped
  silently and the state still advances.

### Start-up sequence (proven)

Main state machine (`0x101C1208`, 16-entry jump table): start-up routine
`0x101C0B28` sets state 4 when the start-up flag (bit `0x200` of the global
flag word) is set, else state 9. State 4 continues to 5, which is the logo.

| State | Does |
|---|---|
| 5 | if a skip input is down: go to 7; else play `Data:Movies:bf.mov` and go to 6 |
| 6 | pump the movie; when done or a skip input is down: stop it, go to 7 |
| 7 | if a skip input is down: go to 1 (front-end loading); else play the day trailer, go to 8 |
| 8 | pump; when done or skip: stop it, go to 1 |
| 1 | splash plus normal loading (game data, lobby), then on to the lobby |
| 9 | the same loading **without** movies (flag clear), ending in the running state |

- **Logo**: `bf` at every start-up (`0x101C0DF0`).
- **Trailer** (`0x101C0F40`): `localtime()` day of the month, modulo 8, indexes
  the eight other movies in the alphabetical order of the path table (pool
  entries `+0x57…+0xE5` of the data block behind TOC slot `-0x3880`):

| day % 8 | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| movie | bub | buc | grav | jug | mir | plan | roc | roll |

  The choice is deterministic by date (days 8, 16, 24 play `bub`; days 7, 15, 23,
  31 `roll`), not random. All nine names are the `Data:Movies:<name>.mov` strings;
  `bf` sits at pool offset `+0x44`.
- **Skip** (proven): the skip inputs are Esc, Space (virtual-key style codes
  `0x1B`, `0x20`) and the mouse button (global at `0x101EDD34`, polled through
  `0x10171DA0`), sampled by level, not by edge, both before a movie starts and
  while it plays. A button still held when the next movie is due therefore skips
  that movie too: a normal click usually ends the whole intro.
- **Every start**: no first-run or "seen" flag is read on this path. Whether it
  plays is the start-up flag, set by default in the defaults routine
  `0x1004CAD0` (called from `0x1004CE40` just before the start-up routine) and
  changeable by the graphics settings block field `+0x2C` (`0x1004C2BC`, values
  0 clear, 1 set) or the developer file named "debug options" (`0x1004BFE4`,
  resource byte 3). **Inferred**: the settings field behind `+0x2C` is not
  identified, so whether the player can switch the intro off in a menu is
  unknown; the shipped default plays it.
- **Movie volume** (proven): the player sets the QuickTime movie volume from a
  global written only by `0x1009B4AC`, called from the sound-options apply
  routine `0x10126460`: movie sound off gives 0, otherwise the Movie volume
  percentage `p` (0…100) becomes `p × 1023 / 100`, then `× 256 / 1023`, i.e.
  linear 0…256 (QuickTime full volume). That routine runs from the early
  program start-up (`0x100002DC` → `0x10000D54` → `0x10125B7C`), before the
  start-up states (the order is **inferred** from the call chain). OpenTPW maps
  its 0…10 Movie volume option linearly onto 0…1.
- **Picture box** (proven): the movie is placed at y = screen height × 64/480
  and sized to the screen width by screen height × 352/480, i.e. 640×352 in a
  640×480 screen, with black above and below. The `.tgq` frames are 320×352, so
  the original shows them horizontally doubled (non-square pixels); OpenTPW does
  the same inside a letterboxed window. (The original stretches to the
  window width; OpenTPW keeps the 640:352 aspect, see APPROX UI-035.)
- Corroboration from `sound.sam` (`DefaultVolume.MOVIE 100`) and the options
  string "Movie volume:" in the data directory.

### Not in the data directory

No data file names a trigger (the 18,734 files, WAD and SDT archives, and all
English `.str` tables were searched), which is why the executable was needed.

### Wired in OpenTPW

`IntroPlaylist` / `IntroSequence`: every normal start plays `bf`, then the day
trailer, then opens the front end. Esc, Space or a mouse button skips (input held
when the window appears is ignored until released, APPROX UI-035; held input
also skips the next movie as above). A missing or unreadable movie is skipped.
`--no-intro` or `OPENTPW_NO_INTRO=1` goes straight to the front end; smoke
tests never play it; `--mute` silences it.

## Remaining gates

Bit-exact IDCT (only if original-player captures prove it matters), colour
matrix/range against original-player captures, what the original does after
the last frame (`plan.tgq` audio tail), whether the PC build uses the same
logo-plus-day-trailer sequence and `.tgq` names (PC executable not analysed),
and which user-facing setting, if any, controls the start-up flag. None of
these are verified.

## Sources

- OpenTPW docs `c077cc9a12aaaac93caac52673ab3080f631fbd9`, `src/formats/tqi.md`.
- multimedia.cx wiki: Electronic Arts TQI (rev 15985), Electronic Arts TGQ
  (rev 15826), Electronic Arts SCxl (rev 15794).
- ISO/IEC 11172-2 (MPEG-1 video) VLC tables B.12–B.14 and default intra matrix.
- FFmpeg 8.0.1 `ffprobe`/`ffmpeg` binaries used only as an external oracle for
  counts, PCM and plane comparison; no FFmpeg source consulted or copied, no
  original game binary executed.
