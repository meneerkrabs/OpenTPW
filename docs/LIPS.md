# LIPS: advisor lip-sync marks

October 9, 2026. Status: strict `.LIP` reader; mark unit and toggle meaning
**inferred from the decoded speech audio** (not from the original runtime);
`--advisor-say N` renders the original advisor model with a LIP-driven
talking/closed mouth synced to SDL audio playback. The mouth **shape** the original
picks while talking, the advisor's animation/pose and its in-game triggers are not
known. No original data is in the repository.

## Where the data is

The earlier inventory missed these because it searched for `.LIPS` member names.
The actual extension is `.LIP`:

| Location | Contents | SHA-256 |
| --- | --- | --- |
| `Data/global/Speech/lips.wad` (48,129 bytes, DWFB) | 639 uncompressed root members: `sp_001.LIP`–`sp_637.LIP`, `z_error.LIP`, `z_z_ouch1.LIP` | `f86d74c4b4356aeaa0e6ed2feb00e80450a9a643a37c7d108e0c2d65c19cc9e1` |
| `Data/levels/fantasy/Speech/lips/sp_001.LIP` (112 bytes) | loose | `5b9010fbf818327d5de682626787786a869b54206ea8bedd2d188c5bc2cdd1b7` |
| `Data/levels/hallow/Speech/lips/sp_001.LIP` (128 bytes) | loose | `d6be53d0ed0e1f6d2de96983dcc24e62d1980ee72b6042b51e3198551898bec2` |
| `Data/levels/jungle/Speech/lips/sp_001.LIP` (144 bytes) | loose | `f3455533f1c2c3d214484a40c3a0b712cbbb97feb06ee877dd0d860e20c2ce2b` |
| `Data/levels/space/Speech/lips/sp_001.LIP` (80 bytes) | loose | `d7414e592f1b7f5a8e701018375851ed2b8a169e6de9764c041e9592b2702e6a` |

Each `.LIP` sits beside a `speechHD.SDT` that has an MP2 entry with the same stem
(`sp_001.mp2` …). The global SDT has 641 entries; `z_z_ouch2`/`z_z_ouch3` have no LIP.
The retail ISO has another `lips.WAD` plus four level LIPs for each of Danish,
French, German and Swedish (637 members each).

## Layout (observed)

```
repeat n times: u32 LE mark      strictly increasing, never 0xFFFFFFFF
u32 LE 0xFFFFFFFF                terminator, last word only
```

Every one of the 3,207 located files (643 English + 4 × 641 ISO localized)
meets these rules: length is a non-zero multiple of eight bytes. That means `n` is always
odd (1–39 marks in total; English global: 156 files have 1 mark, 185 have 3, max 35).
Some files start at mark 0 (16 English global files plus the jungle and space level files).

## Meaning (inferred from audio, October 9, 2026)

**Marks are microseconds from the start of the paired speech clip. The advisor is
talking from 0 until the first mark; every mark toggles talking/silent.** With the
always-odd count the last mark ends the final utterance; a leading mark 0 means the
clip starts silent. This is a voice-activity timeline, not phonemes or visemes.

Evidence (separate Python/numpy analysis of every clip decoded with ffmpeg; repeated
in C# by `LipSyncTimelineTests` with the new decoder):

- Loudness: in the 638 decodable English global clips, 10 ms windows inside talking
  intervals average 70.2 dB, silent ones 41.9 dB (re 1 LSB; C# test). Per interval
  pair, a silent interval is louder than the following talking one in < 1 %.
  Danish/German/Swedish/French ISO clips give 23–34 dB differences.
- Transitions: at odd-index marks (silent→talking) energy rises in 97 % of 1,291
  cases (+20.6 dB mean, 50 ms before/after); at even-index marks it falls in 85 %.
  Talk onsets lie within 50 ms of a mark in 98.8 % of cases (median −4 ms), offsets
  in 96.3 % (median +7 ms).
- Scale: rescaling marks by 0.95/0.98/1.00/1.02/1.05 gives talking-minus-silent
  differences of 9.6/18.8/**31.5**/27.5/12.4 dB; the peak is exactly at 1 µs per unit
  in all five languages and for the four level `sp_001.LIP` against their level
  speech banks (20–31 dB).
- Fit: the last mark lies inside the clip for 637/638 decodable English global clips
  (exception: `sp_478`, 1.05×; its byte-identical space-level copy fits the
  space speech). Danish `sp_127`/`sp_427` overrun by 3 %/0.3 %.

Each level `sp_001.LIP` is byte-identical to one global member: fantasy = `sp_473`,
hallow = `sp_476`, jungle = `sp_479`, space = `sp_478`. How the original chooses
between global and level copies, and how it maps talking to the five mouth meshes,
are not known (no runtime trace; `strings tp.exe` has no `lip`/`phon`/`viseme`).

## Speech audio decoding

The speech banks are MPEG-2 (LSF) Layer II, 22,050 Hz mono, 48 kbps (640 of 641
global entries; `z_error` is Layer I). Music banks are LSF Layer II stereo; 2,642
sound-effect entries are Layer I (decoded since October 2026, see below). `Mp2Decoder`
(`source/OpenTPW.Files/Formats/Sound/Mp2Decoder.cs`) is a clean-room LSF Layer II
decoder written from the ISO/IEC 11172-3/13818-3 process. Its synthesis window is
the 257 standard Table 3-B.3 coefficients as multiples of 2⁻¹⁶ (values taken from the
locally installed ffmpeg's data table and checked against the standard's
D[1] = −0.000015259, D[256] = 1.144989014). The LSF allocation table's 4-bit row
includes the 7-level class: with it every frame of every corpus stream consumes its
payload to within 23 spare bits, unlike the tested alternatives.

Verification: all 965 corpus Layer II streams that ffmpeg also decodes (speech and
stereo music; same-name duplicates and 1-frame clips ffmpeg rejects excluded) decode to the same length as ffmpeg's `mp2` decoder with a maximum difference of
**1 LSB** (≈81 dB SNR on speech). For Layer II, MPEG-1 and joint stereo are rejected
(no TPW Layer II stream uses them, so their tables would be unverified); free format,
Layer III and MPEG-2.5 are rejected for every layer; CRC words are skipped, not checked. `MP2File.FrameData` slices the
entry at its header-size word; the legacy `SoundData` offset does not fit the
40-byte speech headers.

## Sound-effect decoding (Layer I)

`Mp2Decoder` also decodes MPEG audio Layer I (384 samples per frame), the format of the
sound-effect entries and of speech `z_error`. Layer I has a single allocation scheme
for MPEG-1 and MPEG-2 (LSF), so both versions, all sample rates and all four modes
(including intensity stereo above the mode-extension bound) are accepted; only the
bitrate and sample-rate tables differ between versions. A 4-bit allocation per subband
gives `nb = allocation + 1` bits per sample (allocation 15 is rejected as forbidden);
requantization and synthesis are shared with Layer II.

Verification without the original files: 73 generated Layer I streams (MPEG-1 and
MPEG-2, every sample rate, every mode with random mode extensions, low/mid/high
bitrates, with and without CRC, random allocations, scalefactors, samples and
ancillary bits) decode to the same length as ffmpeg's `mp1` decoder with a maximum
difference of **1 LSB**. `Mp2DecoderTests` pins one such stream against ffmpeg values
and covers silence, DC, intensity-stereo scaling, frame sizes and padding.
`OriginalLayerOneSoundEffectsDecode` decodes every Layer I entry of every `.SDT`
bank below `OPENTPW_GAME_PATH`.

Corpus check on the Mac "Sim Theme Park" data (not the pinned Windows edition, see
THEME-PARK-INC.md): all 2,650 Layer I entries decode (2,641 MPEG-2 22,050 Hz mono,
5 MPEG-1 44,100 Hz mono, 4 MPEG-2 22,050 Hz stereo) to the same length as ffmpeg's
`mp1` decoder, with a maximum difference of **1 LSB** over 63,043,584 samples. The
Windows corpus has not been rerun. Nothing plays sound effects in-game yet (no
sound-effect triggers or mixer).

## Advisor runtime slice

`--advisor-say N` (1–637) takes `global/Speech/speechHD.SDT` and `lips.wad` from the
selected language (`GameLanguage.ResolveDataFile`, overlay first; e.g. `--language German
--language-data <CD extraction>` plays German `sp_001` with German marks 3,272,743 /
3,767,619 / 6,915,192). It adds `Advisor` (`source/OpenTPW/World/Advisor.cs`) to the
park scene: `global/advisor.wad/Advisor.MD2` is drawn in a bottom-left viewport
with its own camera. The model has five co-located mouth meshes (`Mouth - Normal`,
`- Aah`, `- Eee`, `- Ooh`, `- Sss`, textures `Mouth1a`–`e`) and `ShutEye` blink meshes.
Talking shows `Mouth - Aah`, silence `Mouth - Normal`. Using only `Aah` is a
presentation choice: LIP data carries no shape. Body, head, eyes, antennae and hands are
shown; the seven hats, spatula, bow tie and blink meshes are hidden. All nodes are
composed through the shared MD2 hierarchy code (`ModelAnimationPlayer.ComputeRestTransforms`);
with the root `Position Dummy` the model is Y-up facing −Z. Triangle corner order is
reversed for the renderer's clockwise front faces (the advisor does not use the Y/Z
swap other MD2 users rely on, so there is no double flip). The model stays in its bind
pose. All 15 `Advisorm*.MD2` clips decode, but every one has tracks with undecoded
(non-rigid) payload: antennae, eyes, blink meshes and usually hands. So none is played.
`Advisorm13` (330 ticks) is the only clip with tracks on all five mouth meshes. Those
payloads are undecoded, so it is the likeliest original source of the mouth-shape
choice. Mouth switching is a per-frame mesh-visibility choice and can later be driven
by decoded tracks.

`SpeechAudioPlayer` queues the decoded PCM, duplicated to stereo, on the same SDL2
queued output the movie player uses (`SdlMovieAudioOutput`, `SDL_QueueAudio` through
Veldrid's SDL2 loader, no new dependency). Tests drive it with `SimulatedMovieAudioOutput`. The lip-sync position is bytes
consumed from the queue, so it leads the speaker by up to one 1,024-frame buffer
(≈46 ms). Playback starts at the first rendered advisor frame. Without an audio
device, a wall clock drives the mouth and nothing is heard.

On macOS arm64 (Metal) `--smoke-test --advisor-say 1` passed: 4.25 s wall time for
4.23 s of speech, mouth sequence Aah > Normal > Aah > Normal, and 491 changed
pixels in the projected mouth rectangle between the talking/closed captures
`artifacts/native-smoke-advisor-{talking,closed}.png`. With
`SDL_AUDIODRIVER=nosuchdriver`, `sp_473` (27 marks) passed on the wall clock (one
5.4 ms interval is shorter than a frame and is not seen). Original visual fidelity
(lighting, scale, placement, idle animation) is not compared.

## Reader and tests

`LipSyncFile` returns raw `Marks` (input cap 64 KiB; rejects lengths that are not
positive multiples of 8, missing/early terminators and nonincreasing marks; streams
stay open; short nonseekable reads work). `LipSyncTimeline` applies the inferred meaning:
`IsTalking(µs or TimeSpan)` (even number of marks at or before the position) and
`TalkingIntervals`.

Tests: `LipSyncFileTests` (12 synthetic + 5 private corpus), `LipSyncTimelineTests`
(5 synthetic + 1 corpus loudness test), `Mp2DecoderTests` (13 synthetic incl. a unit-DC-gain
check of the window/matrixing + 2 corpus: all 640 Layer II speech clips decode;
`sp_001` samples and RMS match the external decoder ±1), `AdvisorTests` (private test for the German overlay bank needs `OPENTPW_LANGUAGE_DATA`; 9 synthetic
incl. the 60 Hz mouth-change frames 134/169/244 for `sp_001` and the speech clock on a
simulated audio output, plus 3 private tests: model orientation/co-located mouths, no rigid-only
`Advisorm*` clip, and loading `sp_001` through the game file system).
Private tests are inconclusive without `OPENTPW_GAME_PATH`.

## Search method (location)

- Upstream `opentpw-docs@34f357f` `src/formats/lips.md`: title only, `TODO`.
- Loose `Data` walk (801 files) and an independent Python DWFB + RefPack scan of all
  312 WADs (13,394 members): no LIP-shaped data outside `.LIP` members.
- `7z l` of `TPWORLD.ISO` (2,989 entries): 20 `.LIP` loose files and 5 `lips.WAD`.

## Approximation register

Every rule below is tagged `// [APPROX:<id>]` at the listed site. `Advisor` logs each one
once via `Log.Warning` when it is created. Values from original data are tagged `// [DATA:…]`
(asset paths, mouth mesh names, clip range 1–637, SDT header-size word, LSF allocation
table fit). The smoke-test thresholds are test-harness checks, not game rules.

| ID | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| ADVISOR-001 | `source/OpenTPW/World/Advisor.cs:25` | Talking always shows `Mouth - Aah`; Eee/Ooh/Sss unused | Decoded `Advisorm13` mouth-track payloads or a capture of the talking advisor |
| ADVISOR-002 | `source/OpenTPW/World/Advisor.cs:30` | Visible: body, head, eyes, antennae, hands; hats, spatula, bow tie, ShutEye hidden | Original node-visibility rules (dummy attributes 0x401/0x411, Advisorm* tracks) or captures per advisor role |
| ADVISOR-003 | `source/OpenTPW/World/Advisor.cs:74` | Bottom-left square viewport, ⅓ of the short logical screen side (min 64 logical px), 16 logical px margin; mapped to the world target for render scale/HiDPI | Original placement/size captures per resolution |
| ADVISOR-004 | `source/OpenTPW/World/Advisor.cs:58` | Camera at z = −70 facing +Z, 40° FOV, near 1 / far 500 | Original advisor camera/projection (binary or capture) |
| ADVISOR-005 | `source/OpenTPW/World/Advisor.cs:247` | Headlight at camera, light colour 0.6, `test.shader` ambient 0.4 + fog | Original advisor lighting/material captures |
| ADVISOR-006 | `source/OpenTPW/World/Advisor.cs:94` | Bind pose; no `Advisorm*` clip played | Decoded vertex/visibility payloads of the `Advisorm*` tracks |
| ADVISOR-007 | `source/OpenTPW/World/Advisor.cs:118` | Triangle corner order reversed for the renderer's clockwise culling (chosen from this renderer's capture) | Original MD2 front-face convention |
| ADVISOR-008 | `source/OpenTPW/World/Advisor.cs:235` | Speech starts at the first rendered advisor frame | Original advisor trigger timing (binary or trace) |
| ADVISOR-009 | `source/OpenTPW/World/Advisor.cs:183` | Always global `speechHD.SDT` + `lips.wad`; level `sp_001.LIP` never chosen | Original global-vs-level selection (binary or file-access trace) |
| ADVISOR-010 | `source/OpenTPW/Client/SpeechAudioPlayer.cs:34` | Lip-sync clock = PCM consumed from the SDL queue (leads speaker by ≤ one 1,024-frame buffer, ≈46 ms) | Original A/V sync source; latency measurement |
| ADVISOR-011 | `source/OpenTPW/Client/SpeechAudioPlayer.cs:31` | Wall clock drives the mouth without an audio device | Original behaviour without sound hardware |
| ADVISOR-012 | `source/OpenTPW/Client/SpeechAudioPlayer.cs:62` | Mono speech duplicated to both stereo channels | Original speech channel layout/panning |
| ADVISOR-013 | `source/OpenTPW.Files/Public/LipSyncTimeline.cs:53` | Marks = µs; talking from 0, toggle per mark (inferred from audio, see above) | Original runtime LIP consumer (binary or trace) |
| ADVISOR-014 | `source/OpenTPW.Files/Formats/Sound/Mp2Decoder.cs:55` | Synthesis-window values read from ffmpeg's data table; two values checked against ISO, corpus ≤1 LSB | Full comparison with the published ISO/IEC 11172-3 Table 3-B.3 |

## Remaining gates

Original-runtime observation of the mouth shape choice while talking, global vs
level LIP selection, advisor triggers/placement/animation and A/V latency. The undecoded
`Advisorm*` track payloads (vertex animation/visibility) for idle/talk poses and mouth shapes. A Windows-corpus run of the
Layer I decoder (verified on the Mac edition) and in-game sound-effect playback. Capture comparison before claiming original fidelity.
