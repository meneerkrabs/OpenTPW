# LIPS: advisor lip-sync marks

October 9, 2026. Status: strict `.LIP` reader and selected MPEG Layer I/II decoder.
Mac static evidence establishes signed mark conversion, a pause-aware unscaled
millisecond clock, loaded-LIP talking state, strict deadlines, one toggle per
update and random mouth selection. The bounded original-clock helper is tested;
`--advisor-say N` still renders a manual SDL-synchronized presentation with its
own timeline. Automatic triggers, animation/pose, geometry and audio-device
integration remain incomplete. No original data is in the repository.

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
- Second check (`LipSyncTimelineTests.OriginalLastMarkFitsSpeechDurationInMicroseconds`,
  October 9, 2026): last mark divided by the decoded MP2 duration over the 638 English
  global clips. Median 0.974, shortest talking clip 0.039 (`sp_019`), longest 1.051 (`sp_478`,
  the only overrun). One clip (`sp_146`) has a single 0 mark and ratio 0. A millisecond or
  1/1000 unit would put the median near 1000 or 0.001, so the data supports microseconds.
  The ratio is not 1.0 exactly, so the marks are not an exact end-of-clip value.

Each level `sp_001.LIP` is byte-identical to one global member: fantasy = `sp_473`,
hallow = `sp_476`, jungle = `sp_479`, space = `sp_478`. The later Mac static
consumer trace selects global or level speech from response bank flags and
chooses `random % 5 + 1` among the five mouth nodes after a strict 100 ms
deadline while talking, including the normal mouth. The current manual runtime
uses global speech and Aah/Normal presentation. Cross-edition behavior,
geometry and device-clock integration remain unverified; audio correlation
alone does not establish mouth-shape selection.

## Original-binary evidence

The Mac build's main program (`SimThemePark` data fork, SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`), contains the LIP path
template `:Speech:lips:sp_%03d.lip` at offset 1917026, next to the string `ResponseID %d`
(1917012); the archive name `lips.wad` at 2007032; the mouth mesh name table
`mouth - normal`, `- aah`, `- eee`, `- ooh`, `- sss` (lowercase) from offset 1996907; and the
log string `Advisor sample is %d ms long` at 1917066. These were found by a read-only
`python3 -I` scan of the file. That initial string inventory established paths
and mouth names without their consumer behavior. The later relocation and
control-flow witnesses in [PPC-advisor.md](reverse/PPC-advisor.md) establish
signed marks divided by 1000, the pause-aware unscaled millisecond clock,
loaded-LIP talking state, strict one-toggle-per-update behavior, response bank
selection and random choice among all five mouth nodes. The standalone helper
preserves those rules with explicit clock/random inputs. The advisor runtime
picks the mouth the same way (`AdvisorMouth`: node `rand() % 5 + 1` every 100 ms
of speech, node 1 while silent, from the advisor update at `0x10007434`).
ADVISOR-001 covers the unrecovered node-to-mesh order. Responses use the recovered
global/level bank policy; ADVISOR-009 now only covers the manual `--advisor-say`
path and the unbound descriptors (see "Automatic advice").
Geometry, cross-edition equivalence and device timing remain separate gates.

## Speech audio decoding

The speech banks are MPEG-2 (LSF) Layer II, 22,050 Hz mono, 48 kbps (640 of 641
global entries; `z_error` is Layer I). Music banks are LSF Layer II stereo.
`Mp2Decoder` now dispatches MPEG-1/2 Layer I and MPEG-2 LSF Layer II from the
actual frame header, including sound effects whose bank-member suffix is still
`.mp2`. The Layer I addition is in `Mp2Decoder.Layer1.cs`; both layers share the
existing synthesis/window and signed 16-bit PCM output. Its synthesis window is
the 257 standard Table 3-B.3 coefficients as multiples of 2⁻¹⁶ (values taken from the
locally installed ffmpeg's data table and checked against the standard's
D[1] = −0.000015259, D[256] = 1.144989014). The LSF allocation table's 4-bit row
includes the 7-level class: with it every frame of every corpus stream consumes its
payload to within 23 spare bits, unlike the tested alternatives.

Verification: all 965 corpus Layer II streams that ffmpeg also decodes (speech and
stereo music; same-name duplicates and 1-frame clips ffmpeg rejects excluded) decode to the same length as ffmpeg's `mp2` decoder with a maximum difference of
**1 LSB** (≈81 dB SNR on speech). Layer II still rejects MPEG-1 and joint stereo;
Layer I accepts MPEG-1/2 mono, stereo, dual-channel and intensity joint stereo.
Free format, Layer III and MPEG-2.5 remain unsupported. CRC words are skipped,
not checked; Layer I nonzero de-emphasis is unsupported. Undefined scalefactor
63, forbidden Layer I allocation 15 and reserved header values are rejected. `MP2File.FrameData` slices the
entry at its header-size word; `SoundFile` now reads the validated 40-byte
packed container fields and aligns `SoundData` to that declared offset.


### Layer I decoding and verification (follow-up)

Layer I produces 384 samples per channel from twelve 32-band synthesis slots.
Allocation is four bits: zero means no samples, values 1–14 select widths 2–15,
and 15 is forbidden. Scalefactors are six bits. Intensity stereo shares upper-band
sample codes while retaining separate channel scalefactors; the boundary is
`4 * (modeExtension + 1)`. These are the ISO/IEC 11172-3 Layer I rules, with the
384-sample frame independently specified by
[ITU-R BS.1115](https://www.itu.int/dms_pubrec/itu-r/rec/bs/R-REC-BS.1115-0-199407-S!!PDF-E.pdf).
The primary standard's decoding clauses and tables were retrieved from a
[published ISO text copy](https://previewnorm.com/iec/ISO%20IEC%2011172-3-1993%20PDF.pdf);
its [official ISO catalog record](https://www.iso.org/standard/22412.html) identifies
ISO/IEC 11172-3:1993. No external decoder implementation source was copied.

The identified Mac original also establishes this path: code `0x2de8` supplies
four-bit allocation reads, `0x2fe4` adds one to allocation for sample width,
`0x3114` terminates after twelve synthesis calls, and normal Layer I output-size
setup multiplies channels by 768 PCM bytes. Its centered numerator is
`code + 1 - 2^allocation`; its combined factor is
`float(2^(2 - scalefactor/3) / (2^(allocation+1) - 1))`. All **882** valid
combined-factor floats match the original table exactly when computed in double
before the float conversion. Rounding the scalefactor to float first would
mismatch 105 table entries. Original bitrate/rate tables are also verified.
The [bounded PowerPC witness](reverse/PPC-advisor.md#phase-five-layer-i-runtime-codec)
reproduces these facts without original execution or binary contents in Git.

The baseline Data tree has 2,650 physical Layer I entries across 33 SDT banks,
representing 2,279 unique compressed streams: 2,270 MPEG-2 mono, four MPEG-2
stereo and five MPEG-1 mono. The independent reference run compares all unique
streams plus sixteen generated valid streams and three Layer II regression
streams against installed FFmpeg 8.0.1 (`mp1`/`mp2`, used only as a reference
executable). Across **56,410,752 PCM samples**, the maximum absolute difference
is **1 LSB**. All private Layer I streams have one trailing byte; the comparator
passes the complete-frame prefix to the reference and reports trailing data
explicitly. PCM and original compressed contents stay outside Git; the external
JSON report contains identities, decoded metadata, hashes and error totals.

Generated tests cover silence, offset requantization, low/high allocations,
scalefactor gain, clipping, mono/stereo/dual/joint modes, version/rate tables,
padding/CRC alignment, malformed allocation/scalefactor/header values, truncated
payloads and stream-format changes. Private tests pin `z_error` and decode all
32 global UI entries. With the supplied fixture root, the focused Layer I,
Layer II and LIP tests pass without skips. The addition does not connect game
events, bank/category scheduling, audio-device output or a game clock.

Compatibility boundary: the original Mac factor table has zero in undefined
scalefactor column 63; this strict decoder rejects that input. No such index
occurs in the 3,253,907 parsed private Layer I scalefactors. CRC verification and
de-emphasis remain separate codec work; original runtime/device equivalence is
not claimed by independent PCM agreement.

The parallel .NET 10 branch independently added Layer I coverage, including a
pinned generated stream, intensity stereo, CRC/frame sizes and a full-bank
fixture test. Those tests are retained for the merged decoder. Its earlier
report covered 73 generated streams and all 2,650 Mac-edition physical entries
within 1 LSB over 63,043,584 samples; that report belongs to the earlier
implementation. The selected merged implementation retains the combined factor
arithmetic pinned to all 882 original Mac factors and the separately recorded
56,410,752-sample corpus comparison above.

## Advisor runtime slice

`--advisor-say N` (1–637) takes `global/Speech/speechHD.SDT` and `lips.wad` from the
selected language (`GameLanguage.ResolveDataFile`, overlay first; e.g. `--language German
--language-data <CD extraction>` plays German `sp_001` with German marks 3,272,743 /
3,767,619 / 6,915,192). It adds `Advisor` (`source/OpenTPW/World/Advisor.cs`) to the
park scene: `global/advisor.wad/Advisor.MD2` is drawn in a bottom-left viewport
with its own camera. The model has five co-located mouth meshes (`Mouth - Normal`,
`- Aah`, `- Eee`, `- Ooh`, `- Sss`, textures `Mouth1a`–`e`) and `ShutEye` blink meshes.
Silence shows `Mouth - Normal`; talking shows one of the five mouths, picked at
random every 100 ms as in the original (the LIP data carries no shape). Body, head, eyes, antennae and hands are
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

## Automatic advice

`AutomaticAdvisor` (`source/OpenTPW/Client/AutomaticAdvisor.cs`) runs the original
controller for the game events OpenTPW has. Each identity stays separate: a
`CMsgEvent` ID builds pending **advice** (message ID) with its configured score;
the message's **descriptor** gives the first response and response count; the
**response** (`content/data/advisor-responses.toml`) gives the global or level
**sample** and LIP. Sources: docs/reverse/PPC-advisor.md ("Actual CMsgEvent
producers", "Phase two", "Phase four"); the queue is a port of the reviewed lane
model `OriginalAdvisorScoreQueue` and keeps all 31 of its cases as
`AdvisorScoreQueueTests`.

| Game event | Producer in OpenTPW | Advice (score key) | Responses → sample |
| --- | --- | --- | --- |
| 10 then 0 | Every original-level start (main loop `0x101C2108`/`0x101C2174`) | 10: resets each history's variant, played flag and slaps; the saved tick stays | — |
| 0 | Level start | 0 (`Welcome.Score`) | 1 → **level** bank sample 1, `Speech/lips/sp_001.LIP` |
| 0, game type 2 | Level start in Instant Action (the game-type global, TOC −30136, is the one the player selector `0x1013781C` sets to 2) | 323 (`PrebuiltPark.Score`, tutorial group 1) | 587 → global 606 |
| 2 | `ParkEconomy` bankruptcy (ADVISOR-020) | 106 (`Bankrupted.Score`) | 274/275 → global 424/425 |
| 3 | `ParkEconomy.OpenPark` transition (ADVISOR-020) | 128 (`ParkNowOpen.Score`) | 308/309 → global 342/343 |
| 4 | `ParkEconomy.ClosePark` transition (ADVISOR-020) | 129 (`ParkNowClosed.Score`) | 310/311 → global 344/345 |

`Advisor/Advisor.sam` is read at runtime with the bounded `.sam` reader (≤ 1 MiB):
`MinScoreForConsideration`, the group repeat/once/slap controls and the five
scores. Queue rules: eight records; admission checks tutorial option, repeat
interval in quarter game ticks (`(tick >> 2) − (saved >> 2)`, saved ticks 0–3
skip it, equality passes), once-only, slaps and the pending-duplicate limit,
then takes the first free slot or replaces the earliest weakest record only for a
strictly higher score; selection waits while `now < start + duration`
(unsigned), takes the earliest strictly highest score strictly above the minimum
and the next cyclic variant (so bankruptcy alternates 274, 275); the record is
consumed before playback, and the history is saved and the returned span + 1000
reserved whatever the player returns: the original wrapper `0x1000BA54` fails only
for an invalid record or the missing-descriptor response 614 and keeps the player's
result only as the span (`0x1000BB64`, return 1 at `0x1000BBF0`). A response that
cannot be said therefore reserves 1000. With the Game Options Advisor switch off
the player returns 0 before speaking (`0x10006BB4`–`0x10006BC0`), so advice is
still picked, consumed and recorded silently (ADVISOR-021). With `--mute` the
advisor's speech keeps the wall clock and opens no audio device. Game ticks are the economy's park turns
(`mGameTick`); the sandbox has none, so its ticks stay 0.

With the shipped `Advisor.sam` (minimum 25) the welcome (100,000) plays at once,
the prebuilt-park advice (1,000) follows in Instant Action after the reservation,
bankruptcy (100,000) plays, and park open/close (20) stay pending and never play.
Imported parks are opened during level construction (ECON-031), before the
advisor attaches, so that load-time opening raises no event.

Not wired: the other 346 descriptors, the 135 computed score producers and the
background score scan, research/staff/prank/ride/challenge messages, slaps and
the presentation's deferred speech start, animation sequences and the recovered
`OriginalAdvisorLipDriver` timing (it needs the original pause-aware advisor
clock, which OpenTPW does not reconstruct). The bankruptcy and open/close
producers use the economy's existing conditions; the original producers'
thresholds and preconditions are not traced (ADVISOR-020). Read-only visits of
shared parks (online extension) and the generic sandbox (`--sandbox`, plain
`--smoke-test`, sandbox saves) have no automatic advisor. The pending advice and
history are not saved with the park (ADVISOR-022).

Native check (macOS arm64 Metal, October 10, 2026): `--smoke-test
--load-original-level jungle` passed and logged `Advisor game event 0
(LevelStarted): advice 0 Eligible in slot 0` and `Advisor says response 1
(sample 1, /levels/jungle/Speech/speechHD.SDT): 28,63 s, 35 LIP marks, clock:
game mixer (SDL audio queue)`. Audibility and on-screen appearance were not
inspected.

## Reader and tests

`LipSyncFile` returns raw `Marks` (input cap 64 KiB; rejects lengths that are not
positive multiples of 8, missing/early terminators and nonincreasing marks; streams
stay open; short nonseekable reads work). `LipSyncTimeline` applies the inferred meaning:
`IsTalking(µs or TimeSpan)` (even number of marks at or before the position) and
`TalkingIntervals`.

Tests: `LipSyncFileTests` (12 synthetic + 5 private corpus), `LipSyncTimelineTests`
(5 synthetic + 2 corpus tests: loudness, last mark vs duration), `Mp2DecoderTests` (13 synthetic incl. a unit-DC-gain
check of the window/matrixing + 2 corpus: all 640 Layer II speech clips decode;
`sp_001` samples and RMS match the external decoder ±1), `AdvisorTests` (private test for the German overlay bank needs `OPENTPW_LANGUAGE_DATA`; 9 synthetic
incl. the 60 Hz mouth-change frames 134/169/244 for `sp_001` and the speech clock on a
simulated audio output, plus 3 private tests: model orientation/co-located mouths, no rigid-only
`Advisorm*` clip, and loading `sp_001` through the game file system).
Private tests are inconclusive without `OPENTPW_GAME_PATH`. `Layer1DecoderTests`
adds 27 generated cases and two private checks (`z_error` reference values and
all 32 global UI-bank entries). Before remote integration, the combined Layer I/Layer II/LIP filter passed
49 cases without skips. The merge retains those tests, the parallel branch
codec tests and the fourteen compressed-entry metadata cases; current
verification totals are recorded in [PROGRESS.md](PROGRESS.md).

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
| ADVISOR-001 | `source/OpenTPW/World/Advisor.cs:25` | Mouth nodes 1–5 are Normal, Aah, Eee, Ooh, Sss in that order (the random 100 ms choice itself is traced) | The node-lookup jump table at `0x1019B3DC` or the MD2 node ids |
| ADVISOR-002 | `source/OpenTPW/World/Advisor.cs:30` | Visible: body, head, eyes, antennae, hands; hats, spatula, bow tie, ShutEye hidden | Original node-visibility rules (dummy attributes 0x401/0x411, Advisorm* tracks) or captures per advisor role |
| ADVISOR-003 | `source/OpenTPW/World/Advisor.cs:74` | Bottom-left square viewport, ⅓ of the short logical screen side (min 64 logical px), 16 logical px margin; mapped to the world target for render scale/HiDPI | Original placement/size captures per resolution |
| ADVISOR-004 | `source/OpenTPW/World/Advisor.cs:58` | Camera at z = −70 facing +Z, 40° FOV, near 1 / far 500 | Original advisor camera/projection (binary or capture) |
| ADVISOR-005 | `source/OpenTPW/World/Advisor.cs:247` | Headlight at camera, light colour 0.6, `test.shader` ambient 0.4 + fog | Original advisor lighting/material captures |
| ADVISOR-006 | `source/OpenTPW/World/Advisor.cs:94` | Bind pose; no `Advisorm*` clip played | Decoded vertex/visibility payloads of the `Advisorm*` tracks |
| ADVISOR-007 | `source/OpenTPW/World/Advisor.cs:118` | Triangle corner order reversed for the renderer's clockwise culling (chosen from this renderer's capture) | Original MD2 front-face convention |
| ADVISOR-008 | `source/OpenTPW/World/Advisor.cs:235` | Speech starts at the first rendered advisor frame | Original advisor trigger timing (binary or trace) |
| ADVISOR-009 | `source/OpenTPW/World/Advisor.cs:201` | `--advisor-say` plays global clips by number; responses follow the traced global/level selector (`content/data/advisor-responses.toml`); the controller picks responses only for messages 0, 106, 128, 129 and 323 | The remaining 346 descriptors and their score producers |
| ADVISOR-010 | `source/OpenTPW/Client/SpeechAudioPlayer.cs:34` | Lip-sync clock = PCM consumed from the SDL queue (leads speaker by ≤ one 1,024-frame buffer, ≈46 ms) | Original A/V sync source; latency measurement |
| ADVISOR-011 | `source/OpenTPW/Client/SpeechAudioPlayer.cs:31` | Wall clock drives the mouth without an audio device | Original behaviour without sound hardware |
| ADVISOR-012 | `source/OpenTPW/Client/SpeechAudioPlayer.cs:62` | Mono speech duplicated to both stereo channels | Original speech channel layout/panning |
| ADVISOR-013 | `source/OpenTPW.Files/Public/LipSyncTimeline.cs:54` | Talking from time 0 (unit and per-mark toggle traced: STP-PPC 0x10007434) | Original runtime LIP consumer (binary or trace) |
| ADVISOR-014 | `source/OpenTPW.Files/Formats/Sound/Mp2Decoder.cs:55` | Synthesis-window values read from ffmpeg's data table; two values checked against ISO, corpus ≤1 LSB | Full comparison with the published ISO/IEC 11172-3 Table 3-B.3 |
| ADVISOR-015 | `source/OpenTPW/Client/AutomaticAdvisor.cs:47` | The eligibility check's tutorial byte +53 (`0x10009038`) is the Game Options Tutorial switch (options +0x35, default on in `0x10125B7C`) | The object behind TOC −30268 in the eligibility check |
| ADVISOR-016 | `source/OpenTPW/Client/AutomaticAdvisor.cs:126` | Returned playback span = speech length + 200 + 300 + 1000 ms; the queue adds another 1000 | Decoded advisor sequence and ending-clip durations |
| ADVISOR-017 | `source/OpenTPW/Client/AutomaticAdvisor.cs:13` | Controller clock = wall-clock ms since the automatic advisor started, one controller update per frame, not paused with the game | The advisor clock's offset/freeze/compensation and the update cadence |
| ADVISOR-018 | `source/OpenTPW/Client/AutomaticAdvisor.cs:64` | The automatic advisor is drawn only while a response plays and only inside a level; leaving the level stops it | Entry/exit animation and idle visibility |
| ADVISOR-019 | `source/OpenTPW/World/AdvisorController.cs:94` | `GeneralAdvisor.MinTimeAnyMessage` (5) and `MinTimeSameMessage` (120) are loaded but not applied | Reads of balance fields +24/+28 |
| ADVISOR-020 | `source/OpenTPW/Client/AutomaticAdvisor.cs:76` | Events 2/3/4 come from the economy's bankruptcy (six months in the red) and park open/close transitions | The producers' threshold and preconditions (`0x100CC464`, `0x10108EE4`) |
| ADVISOR-021 | `source/OpenTPW/Client/AutomaticAdvisor.cs:104` | The response player's options byte +0x34 (`0x10006BB4`) is the Game Options Advisor switch; off, advice is picked, consumed and recorded silently | The object behind TOC −30268 (data `0x120A14`) and its +0x34 writer (same question as ADVISOR-015) |
| ADVISOR-022 | `source/OpenTPW/Client/AutomaticAdvisor.cs:58` | Pending advice and message history are not saved or loaded with the park | Whether the original park save writes the controller's pending records (serializer `0x1000BC10`) and history, and where |

## Remaining gates

Original-runtime observation of the mouth shape choice while talking, the
remaining advisor triggers and score producers, placement/animation and A/V latency. The undecoded
`Advisorm*` track payloads (vertex animation/visibility) for idle/talk poses and mouth shapes. Original sound-bank/category scheduling,
codec CRC/de-emphasis behavior and device latency still need verification. Layer I
codec support is implemented; automatic sound events are not wired by that change.
Capture comparison remains necessary before claiming original fidelity.
