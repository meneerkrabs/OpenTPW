# LIPS: advisor lip-sync marks

October 9, 2026; updated October 14, 2026 (fixed sequences, model hiding, door events) and October 17, 2026 (node bits cleared at every clip end). Status: strict raw `.LIP` reader/writer and selected MPEG Layer I/II decoder.
Mac static evidence establishes signed mark conversion, a pause-aware unscaled
millisecond clock, loaded-LIP talking state, strict deadlines, one toggle per
update and random mouth selection. The bounded original-clock helper is tested;
The advisor now speaks by itself for the traced game events (see "Automatic
advice"); `--advisor-say N` still plays a single clip on demand. Its clock stands
still while a pausing screen is open (the pause menu and its windows, the map window, the postcard), as the original's pause-aware clock does (every owner of the game pause `0x4092A0`: those, the offline message boxes, the P key and the application's deactivation; ADVISOR-017, retired); the clip sequences, the pose and the mouth are ported as read from tp.exe (see "Sequences and timing", "Clip timeline" and "Costumes"); the remaining
triggers and the shared `rand()` stream remain incomplete. No original data is in the repository.

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

- Loudness: in all 639 English global clips (`z_error`, the one Layer I clip, included now that the
  decoder reads Layer I), 10 ms windows inside talking
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
- Fit: the last mark lies inside the clip for 638/639 English global clips
  (exception: `sp_478`, 1.05×; its byte-identical space-level copy fits the
  space speech). Danish `sp_127`/`sp_427` overrun by 3 %/0.3 %.
- Second check (`LipSyncTimelineTests.OriginalLastMarkFitsSpeechDurationInMicroseconds`,
  October 9, 2026; `z_error` included since October 15): last mark divided by the decoded duration over the 639 English
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
The node-to-mesh order is traced too: the node lookup `0x1019B394` finds nodes 1–5 by the names "mouth - normal", "aah", "eee", "ooh" and "sss". Responses use the recovered
global/level bank policy; ADVISOR-009 now only covers the manual `--advisor-say`
path and the unbound descriptors (see "Automatic advice").
Geometry, cross-edition equivalence and device timing remain separate gates.

## Speech audio decoding

The speech banks are MPEG-2 (LSF) Layer II, 22,050 Hz mono, 48 kbps (640 of 641
global entries; `z_error` is Layer I). Music banks are LSF Layer II stereo.
`Mp2Decoder` now dispatches MPEG-1/2 Layer I and Layer II (MPEG-1 joint included) from the
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
**1 LSB** (≈81 dB SNR on speech). Layer II now accepts MPEG-1 mono/stereo/dual
through standard allocation tables (the separate synthetic verification below),
and MPEG-1 intensity joint stereo (the follow-up below); MPEG-2 Layer II joint remains refused;
Layer I accepts MPEG-1/2 mono, stereo, dual-channel and intensity joint stereo.
Free format, Layer III and MPEG-2.5 remain unsupported. CRC check words are
verified when protection_bit is 0 (mismatches are counted, not concealed); a
nonzero emphasis indication decodes through a de-emphasis shelf (see the
follow-up below). Undefined scalefactor
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
occurs in the 3,253,907 parsed private Layer I scalefactors. CRC verification
and de-emphasis are implemented below; original runtime/device equivalence is
not claimed by independent PCM agreement.

The parallel .NET 10 branch independently added Layer I coverage, including a
pinned generated stream, intensity stereo, CRC/frame sizes and a full-bank
fixture test. Those tests are retained for the merged decoder. Its earlier
report covered 73 generated streams and all 2,650 Mac-edition physical entries
within 1 LSB over 63,043,584 samples; that report belongs to the earlier
implementation. The selected merged implementation retains the combined factor
arithmetic pinned to all 882 original Mac factors and the separately recorded
56,410,752-sample corpus comparison above.

The final incomplete supported frame is reported as `TrailingBytes` without
changing the completed prefix's format/PCM/counters. Completion is checked after
header validation and before format continuity; a complete rate/channel/layer/
emphasis change remains unsupported, and malformed/unsupported trailing headers
are still refused. `MpegFrameCompletionBoundaryTests` covers these boundaries in
synchronous and cooperative decoding with synthetic Layer I/LSF tails.

Independent controls also retain a nonzero, CRC-protected, emphasized two-frame prefix when a supported incomplete frame precedes later bytes. All remaining bytes stay trailing; the decoder does not resynchronize past the partial frame. First-partial input yields no decoded frame/metadata, short final header fragments request no extra cooperative batch, and complete format changes or malformed/unsupported four-byte headers still refuse. The selected WAV exporter keeps its stricter trailing-byte rule and refuses a supported partial tail before creating an output directory. These are synthetic host decoder boundaries, not original-player recovery parity.
The earlier Layer I switch fixture changed its header to advertise 208 bytes but
kept only 136 bytes; it now uses a complete Layer II frame for the unchanged refusal
assertion, while the former 136-byte clone has an explicit partial-tail regression.

### MPEG-1 nonjoint Layer II (October 14 follow-up)

The decoder now accepts mono, stereo and dual-channel MPEG-1 Layer II at
44.1/48/32 kHz, using the bitrate/mode combinations and A/B/C/D allocation tables
from [ISO/IEC 11172-3](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf)
§2.4.2.3 and Annex B.2a-d. The table is selected per frame from per-channel bitrate
and sample rate. Low-table code 15 uses 32767 levels and 15 bits; no library/table
code was vendored. Header-derived SDT metadata and the existing selected WAV
export inherit this support. MPEG-2 Layer II joint, free format, MPEG-2.5 and Layer III
remain unsupported; Layer I and the existing LSF maps stay unchanged.

Only header/profile selection and Layer II table limits changed. SCFSI, grouping,
requantization, synthesis/window, PCM rounding/clamp, emphasis, cooperative batches
and count-and-continue mismatch policy reuse the current decoder. CRC counts the
frame's transmitted allocation/SCFSI fields through the corrected shared rule.
This does not establish original PPC/x86 audio behavior or full MPEG conformance.

`Mpeg1LayerTwoTests` checks every legal header, table/row endpoints, padding,
ignored nonjoint mode extension, grouped/scalefactor errors, SCFSI/channel
independence, per-frame table transitions, CRC, truncation and actual SDT metadata.
Unclipped negative-quarter DC distinguishes low-table 15-bit code 15 from an
incorrect 16-bit mapping. `Mpeg1LayerTwoWavExportTests` uses the real exporter.
Fifteen pinned independently generated reference excerpts add transient/channel
and quantizer checks without a temporary directory or external decoder dependency.
See [MPEG1-LAYER2-VERIFICATION.md](MPEG1-LAYER2-VERIFICATION.md) for complete
reference provenance, hashes, the measured one-LSB difference and its limits.
Original corpus counts above are earlier verification; no game assets were opened
or original corpus rerun for this generic format addition.

### CRC verification and de-emphasis (follow-up)

October 11, 2026: CRC verification and de-emphasis were added. October 14:
protected-span coverage for supported nonjoint MPEG-2 LSF Layer II was corrected
from the final [ISO text](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf),
§2.4.1.6 and Annex B Table B.5. No external decoder implementation was copied.

**CRC**: the existing CRC-16 polynomial (0x8005), all-ones start, MSB-first order
and header bits 16–31 remain. Layer II then protects all transmitted allocation
fields and only the SCFSI fields actually present for nonzero channel/subband
allocations. It excludes scalefactors, sample codes and ancillary bits. The current
LSF table has 75 allocation bits per channel; add two bits for every allocated
channel/subband. One allocated band per channel therefore protects 77 mono or
154 stereo bits; zero allocations protect 75/150, all thirty protect 135/270.
The old fixed maximum counted nonexistent SCFSI fields and included unprotected
payload bits. Its generated tests mirrored that maximum; they were not an
independent oracle for coverage. The original corpus inventory below was not
rerun for this correction.

Layer I's existing 128/256-bit coverage—including its joint-mode behavior—is
unchanged and is not newly qualified here. This correction covered nonjoint Layer II;
the later MPEG-1 joint follow-up also counts actual transmitted fields. MPEG-1
nonjoint frames use the same actual-field CRC rule below. The stored checkword, CrcChecks/CrcFailures and
count-and-continue mismatch policy (AUDIO-014) remain; no muting, playback or save
policy changes. The WAV exporter still refuses failed checks and now accepts
correctly protected Layer II entries through the existing decoder.

`LayerTwoCrcSpanTests` emits explicit protected fields and builds its checksum
without production ProtectedBits: zero/one/all allocations, unequal dual channels,
protected header/allocation/SCFSI flips versus excluded valid scalefactor/ancillary
flips, incomplete frames, unchanged PCM and cooperative decoding.
`LayerTwoCrcWavExportTests` checks valid export PCM/source identity and failed-CRC
refusal before output. The checksum builder also matches the published CRC-16/MPEG-2
check value; no original assets, reference decoder or native execution was used.
These are supported-format correctness controls, not full MPEG conformance or
original audio parity claims. Existing synthesis/de-emphasis qualifications remain.

**De-emphasis**: emphasis '01' (50/15 µs) and '11' (CCITT J.17) decode through a
first-order shelving filter after synthesis, per channel, at the stream's sample
rate; '10' is reserved and rejected. 50/15 µs inverts the broadcast
pre-emphasis (zero 15 µs, pole 50 µs; high-frequency shelf 15/50 = −10.46 dB);
J.17 inverts the published insertion-loss curve
10·log₁₀((75+(ω/3000)²)/(1+(ω/3000)²)) dB (zero 1/(3000·√75) s, pole 1/3000 s;
−18.75 dB shelf at high frequencies after the round trip's absolute pad cancels),
both realized at unity DC gain via the bilinear transform (AUDIO-013; the
standards give only the analogue characteristic, not a digital realization).

**Corpus inventory** (every SDT entry of the available editions, frames walked
with the decoder's header rules; `OriginalCorpusCarriesNoCrcWordAndNoEmphasis`,
skips without `OPENTPW_GAME_PATH`):

| Edition root | Banks | MPEG frames | protection_bit = 0 | CRC failures | emphasis ≠ 00 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `theme-park-world` (PC 1999) | 47 | 416,072 | 0 | — | 0 |
| `theme-park-world-patch2` | 47 | 416,072 | 0 | — | 0 |
| `theme-park-world-nl` (Dutch speech/music) | 5 | 89,476 | 0 | — | 0 |
| `mac-feral` (HFS data forks) | 47 | 417,681 | 0 | — | 0 |
| `theme-park-world-bonus` | 0 | — | — | — | — |

1,339,301 frames in total, every one with protection_bit = 1 and emphasis '00';
no non-MPEG entries. The Mac banks were read read-only with `hfsutils`
(`hcopy -r` of the data forks; the HFS image stayed unchanged and was unmounted
afterwards). Emphasis and CRC are therefore exercised only by the generated
tests (`Mp2ErrorProtectionTests`: the CRC-16/MPEG-2 catalogue check value, valid
and corrupted words per layer, shelf coefficients against the published curves,
per-channel filtering, mid-stream emphasis change rejection, and the corpus
inventory).

### Playback length (entry +32)

Every SDT entry header's u32 at +32 is the length the game plays, in bytes of 16-bit PCM (all channels).
tp.exe's MPEG sound object (constructor 0x6C82C0) copies it when type byte +27 has bit 0x04 (the type
is a bit field: 0x20 MPEG, 0x10 another codec, 0x04 length present, 0x01 stereo; 36/37 = 0x24/0x25), and
its fill routine 0x6C83C0 delivers at most that many decoded bytes before the end/loop handler, so the
decoder's tail is never heard. +26 is the bit depth (16 plays 16-bit, anything else 8-bit, 0x6C6F40).
All 5,674 entries (Data and the three language folders) have the bit, and a length of whole sample frames
no longer than the decoded audio: about one frame plus the encoder delay shorter (Layer I mono 558–991
samples, Layer II mono mostly 672–1,824, stereo 725–1,729; 212 Layer II entries are cut much earlier).
`SdtEntryHeader.PlaybackBytes` and `Mp2Audio.TrimTo` apply it: sound events and advisor speech play exactly
that length (sp_001: 92,320 of 93,312 decoded samples). The decoder and `--export-sdt-wav` keep the full
decoded PCM.

## Advisor runtime slice

`--advisor-say N` (1–637) takes `global/Speech/speechHD.SDT` and `lips.wad` from the
selected language (`GameLanguage.ResolveDataFile`, overlay first; e.g. `--language German
--language-data <CD extraction>` plays German `sp_001` with German marks 3,272,743 /
3,767,619 / 6,915,192). It adds `Advisor` (`source/OpenTPW/World/Advisor.cs`) to the
park scene: `global/advisor.wad/Advisor.MD2` is drawn in the bottom-right of the screen
without a camera. In the Windows v2.0 executable (`tp.exe`, docs/COMPLETION-PLAN.md) the
advisor setup `0x00429BA0` gives the model the translation (0.6, −0.6, 0.2) and scales its root
node by (0.015 × 0.75, 0.001, 0.015); `SetLocalMatrix` (`0x0057A260`) flips y; and the mesh draw
`0x0057A750` skips the camera, projection and screen matrix for the advisor (model flag 0x200,
set by `0x0045BED0`). So the model reaches the transform stage in screen space, x right and y
down, with its centre at (0.6, 0.6), height 0.015 per model unit and width 0.75 of that (the 4:3
screen's height/width), and the depth squeezed to 0.001. OpenTPW reads that space as −1..1 across
the screen (ADVISOR-023): the advisor's box is x 0.2..1.0, y −1.0..−0.2 in clip space, about
x 420–595 and y 302–460 of a 640×480 screen for the model itself. In the park it is rendered into
an image and drawn after the HUD, as the original draws it over the scene; outside 4:3 the width
scale follows the output's aspect (an extension, `[EXT:advisor-aspect]`). The advisor's parallel light (26.67, −26.67, 800)
from the same setup shines into the screen. The model has five co-located mouth meshes (`Mouth - Normal`,
`- Aah`, `- Eee`, `- Ooh`, `- Sss`, textures `Mouth1a`–`e`) and `ShutEye` blink meshes.
Silence shows `Mouth - Normal`; talking shows one of the five mouths, picked at
random every 100 ms as in the original (the LIP data carries no shape). Body, head, eyes, antennae and hands are
shown without a costume; the seven hats, spatula, bow tie and blink meshes start hidden and each response puts on its own costume
("Costumes" below). All nodes are
composed through the shared MD2 hierarchy code (`ModelAnimationPlayer.ComputeRestTransforms`);
with the root `Position Dummy` the model is Y-up facing −Z. Drawn without a camera,
nothing mirrors it, and the MD2 corner order is already clockwise for the renderer's
front faces. The advisor material culls the back faces (front faces clockwise on the screen), as the original's engine
does in software: the Direct3D device sets D3DCULL_NONE once at device init (`0x566957`) and nothing else writes the cull mode,
and neither the mesh path (`0x57A750`, `0x57AA10`, `0x574110`–`0x574660`) nor the submit `0x581CD0` (whose vertex transform,
`[0x87B1D8]` vtable +8: `0x579250`, `0x5F4730`, `0x5F4F81`, applies the matrix only) tests faces, but the submit's queue
(`[0x8BCBB8]`, filled only by `0x581CD0`, whose only caller is `0x57A530`) is flushed by the scene draw (`0x576A00` →
`0x582170` → `0x581DB0`), and its primitive builder `0x580530` keeps a triangle only when the determinant of its corners'
x, y and w is positive (`0x581320`, its only caller), after the advisor's own view `diag(1, −1, 1, 1)` (`0x57A260`) and
the viewport `0x579690`, which scales y by the positive half height (`0x582F70`): the screen's y runs down, so a positive
determinant means the corners run clockwise on the screen. Until October 20, 2026 OpenTPW drew the advisor double-sided, having missed that test;
`AdvisorTests.TheOriginalsFaceTestKeepsTheAdvisorsFacesTowardsTheViewer` checks that the kept faces are the ones whose stored
normals face the viewer in every advisor model (ADVISOR-007 stays retired). The bind pose is the base of the clips
("Clip timeline" below). All 15 `Advisorm*.MD2` clips decode completely: TRS tracks, quantised vertex
groups and node-flag toggles (plus the never-read metadata bit 0x2000); the level
advisors add texture-coordinate tracks and one clip with path parameters
(docs/reverse/ANIM-plan.md §10). The original starts animation id n as
`Advisor<level>m<n>.MD2` on channel 0 at speed 1.0 on clock B
(`Advisor.AnimationClipName`). The clip choice of a generated sequence (`0x598B20`) and the fixed rows (`0x598BF0`, table `0x76DC18`) are traced and
built by `AdvisorSequences` (see "Sequences and timing" below) and played (see "Clip timeline"), vertex and texture-coordinate tracks included. The clips' node lists set node bit
0x10 on the hats, spatula and bow tie, the hidden set of the base model. That bit is the clip-toggled word of the mesh-node record
(+0, set at `0x472E75`, cleared at `0x472401`); the mesh draw's own bit 0x10 (`0x57A797`) is a different word, a render-node deferral
marker (the draw defers a node of a model with flag 0x200000 to the list at `0x8BB220` and the drain `0x579C80` sets and clears it); the
toggled bit is read by the model draw walk `0x57A530`, which skips a node's own mesh when its word has any of 0x8210 (`0x57A5E3`). The fixed-sequence entries also carry accessory bits `0x7F0000`, stored at
`0xF79660`, which the selector `0x598CA0` tests before it shows or hides nodes by dummy attribute (`0x429F60`, `0x429EE0`); see "Costumes".
`Advisorm13` (330 ticks) is the only clip with tracks on all five mouth meshes, the
likeliest original source of the mouth-shape choice.

`SpeechAudioPlayer` queues the decoded PCM, duplicated to stereo, on the same SDL2
queued output the movie player uses (`SdlMovieAudioOutput`, `SDL_QueueAudio` through
Veldrid's SDL2 loader, no new dependency). Tests drive it with `SimulatedMovieAudioOutput`. The lip-sync position is bytes
consumed from the queue, so it leads the speaker by up to one 1,024-frame buffer
(≈46 ms). The automatic advisor starts its speech on the advisor clock as the original does (see "Sequences
and timing"); the manual `--advisor-say` / `--advisor-response` tools start at the first rendered advisor
frame. Without an audio device, a wall clock drives the mouth and nothing is heard (`[EXT:advisor-no-device-speech]`; the rule is
fully traced: the original's mouth runs on the advisor clock, never on audio, and without a sound system `0x51BFC0` returns 0 and
`0x51C570` returns its preset 1, so the player builds the sequence from 1 + 200 + 300 = 501 ms; an optional strict-parity change would
use length 1 and no mouth).

### Costumes (ADVISOR-002 retired, October 14, 2026)

Every response carries two accessory selectors, fields `+0x14` and `+0x18` of its 32-byte record in the Windows table `0x768FB8`
(`content/data/advisor-costumes.toml`, written by `tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py`). The update `0x598DC0`
hands them to the selector `0x598CA0` right after a clip starts (`0x599800` runs it once the channel's queue reads the idle set 12),
after the clip's node list has hidden every accessory: a selector is the dummy-attribute value of the mesh node to show.
The lookup `0x44B220` takes the first attribute record whose type has bit `0x400` and whose value matches; record `k` belongs to node
(u16 at model header `+0x46`) + `k`, so the global model's records name the antennae (20, 19), the right hand (21), the spatula (14),
the left hand (22) and the hats: 5 Hardhat, 6 Security Hat, 7 Handyman Hat, 8 Kiosk Hat, 9 Ticket Hat, 10 Research Hat, 11 Bowler Hat
(with 12, the Bowler_BTie), 13 Fast Food Hat (with 14, the spatula). A hat hides the antennae (selectors 1, 2, 4, 5, 6, 7, 9, 10, 11, 13;
not the kiosk hat), selector 0 shows them, the spatula hides the right hand. Selector −1 is the level's theme costume:
`ThemeAdvisorCostumes[0..3].AddOns` of `levels/<level>/Standard.sam` are shown and `.SubOffs` hidden (`0x4080E0` copies them to
`0xF797A4`/`0xF797B4`; jungle add-on 4 is the pith helmet, hallow 3, 16, 17 the axe and its two antennae halves, space 2 the helmet, fantasy 1
the helm). Responses of model 1 draw the level's `Advisor_<XXX>.MD2`; the other 473 the global model. The shipped table: 77 responses hard
hat, 47 security, 48 handyman, 8 kiosk, 34 ticket, 58 research, 32 bowler, 18 fast food, 129 theme costume, 159 none (help text, and the
welcome-like level responses). A fixed sequence's accessory bit `0x10000` (the clips 19 and 20 of sequences 17 and 29) hides the costume,
the bug head (`0xF79698`, the mesh `0x44B2E0` finds by the name "bug head") and the mouth (the mouth update `0x599B09`-`0x599BDE` shows
none); the bits `0x20000`–`0x200000` arrange screen-space ticket effects in effect group 0 (`0x598EAA`), and `0x40000`
alone also plays `cat_ui` `0xC5` (`0x598F1C`); see "Screen effects" below. The node bits live on the model instance, and every clip end clears them all: the sampler `0x4735D0` starts the idle set 12 at a clip's
end (`0x473778`-`0x473783`, flags 0), `0x472F60` then calls `0x472310` for the clip that ended, and with the advisor's model flag 8 (`0x4621C9`,
load flags `0x50120`) `0x472310` clears node bit `0x10` on every node of the model (`0x472345`-`0x472398`); the Kill path `0x429D60` reaches the same
clear, a freshly loaded model is all shown (the node type words of all five advisor models carry no `0x10`), and the response player starts a clip
only while the channel reads the idle set (`0x599A23`), so no clip-to-clip transition exists. Each clip therefore starts from an all-shown model and
hides only its own node list (`0x472D70`), then the selectors, the bug head and the toggles act: the ticket advice (sequence 29: clips 17, 18, 19, 20)
leaves the held Ticket shown in clips 17 and 18 (their lists omit it) until clip 18's toggle hides it at tick 38 and shows Ticket2, the close-up clip 19
lists (hides) the body, eyes, hands and mouths and hides Ticket2 at tick 39, and clip 20 shows Ticket2 again until its toggle at tick 60
(`AdvisorAccessories.SequenceHiddenNodes` gives the state at a clip's start, the toggles the rest). Nothing persists between responses (ADVISOR-026 retired
October 17, 2026). `AdvisorAccessories`, tested without a GPU
(`AdvisorHelpTests`). Video evidence: `[EXT-EVIDENCE:video tpw-nl 0:42]` and `29:01` (park intros in the theme hat: the safari helmet in
Lost Kingdom), `78:57` (the axe in Halloween), `14:00` (yellow hard hat on a breakdown), `9:47` (research hair), `48:26` (cleaner cap),
`60:40` (police cap), `6:49` (bowler on the ticket price), `55:08` (fast-food visor on shop value), `74:10` (no hat on help).
OpenTPW's capture of the jungle welcome shows the pith helmet.

### Captions (ECON-030, October 16, 2026)

When the advisor chooses a response to say, tp.exe (`0x0059B620`, from the player of the next response `0x0059A550`) asks the speech (`0x00599050`, which
answers 0 with the advisor switched off) and then, either way, posts a category-24 message carrying the response's tag (field `+0x1C` of its 32-byte row,
`0x005997B0`; `0x17F` has no text) to the tag system (`0x00509BF0`). The tag system finds the tag's record in the table at `0x0075ECA8` (`0x0050AF40`, by the id in
field `+0`), fills the `TAG_SYSTEM.str` entry in field `+0xC` with the message's words (`0x00509E10`) and posts the text to the message tube. The bank's warnings are
such captions: the money messages 102 to 106 (responses 266 to 275) show the entries 123 to 127 ("If you stay in the red for six months...", "the banks have closed
your park..."), so the advice 106 of the bank's message type 2 puts entry 127 in the tube. `content/data/advisor-captions.toml` (written by
`tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py`) lists the 353 responses with a caption; `AutomaticAdvisor.PostCaption` hands the entry to the park HUD
(`ParkHud.PostAdvisorCaption`). A caption with parameter slots (36 responses) is not posted: its arguments are the advice record's words, which OpenTPW does not keep
(GUEST-007). The money warnings 102 to 105 still have no producer (ADVISOR-009: their scores are computations of the months in the red).

### Clip timeline (ADVISOR-018 and the clip draw, October 14, 2026)

A sequence's clips play one after the other from the request, each for its own length (trailer word 2 × 1000 / 30 ms; the budget table
zeroes clip 14's entry, not its playing: `AdvisorClipDurations.PlayOf`). The node tracks move the model: clip 14 lifts the body and head
from below the screen edge (z −73 to −21) over its 30 ticks, clip 15 sinks them (−21 to −74) over 20; the blink toggles
(`NodeFlagToggles`: a positive entry shows the node, zero or negative hides it) swap `Right/Left Eye` and `ShutEye R/L` (the toggled meshes are hidden by the mesh-node bit's reader in the draw walk, `0x57A5E3`, which tests `0x8210`). The vertex tracks (antennae, hands, eyes) and the level
advisors' texture-coordinate tracks (the eyes of clip 16) play as an object's do: the advisor's model slots are drawn by the shared instance
draw `0x473C70(instance, 1, 0)` from `0x429DF0`, which runs the channel sampler `0x4735D0` -> `0x471860` for each animation channel (vertex pass in set mode, the
models' header flag 4 being clear, and texture-coordinate pass `0x4745C0`); `AdvisorRig.Write` samples them per part. No advisor clip has a
12-byte vertex block. The sampler hides the model when the last clip ends
(`0x4735D0`), `Advisor.DrawnSpanMilliseconds` = the sum of the played lengths. A new response restarts at clip 14, so an interrupted
advisor disappears in the frame the next one starts and rises again. Video check (`[EXT-EVIDENCE:video tpw-nl 32:07]`, `41:58`, `58:38`,
`61:34`): the model appears at the request, speech starts 0.6–1.0 s later (the 800 ms delayed start), the last word ends and the advisor is
gone 0.8–1.0 s later (2.2 s for one longer outro); the played sum puts the end 0.4–0.9 s after the last word for generated sequences
(capture noise ±0.2 s), where the budget sum alone would end 1 s earlier, so the 1000 ms of clip 14 belong to the timeline. Interruption: see
"Contextual help". The 0.25 s of visible rise and sink in the video is the part of the 1 s and 0.67 s clips in which the model is on screen.

### Contextual help (ADVISOR-009, October 14, 2026)

About half of all speech in the video is help text for a window or tool. In `tp.exe` it is a message family: descriptors of the
tutorial group (group 1; 82 descriptors, 81 of them with a response in the table, since 291 names the absent response 551) whose score callback `0x59E8C0` returns the Advisor.sam score `HelpMessage.Score` (`+0x328`, 1000), marked to
skip the background scan, posted by the UI code (`content/data/advisor-help-messages.toml`; the descriptor table `0x76E300` is filled at
start by the straight-line initializer `0x5A0A50`, which the script replays). A window builder posts its message with `0x486B00`
(build `0x59B590`, queue `0x59A940`) and its close handler withdraws it with `0x486B40` → `0x59AA70`: pending records of the message are
dropped and, when it is the speaking message (`+0x16EC`, busy test on `+0xD8/+0xDC`), the speech is cut (`0x5994E0`: stop, kill the
advisor, slap sound), its played flag is cleared, its slap count rises by one and the reservation ends so the next advice starts at once.
`SetMode` (`0x52F200`) withdraws ten tool/mode help messages whenever the mode changes. Everything else is the normal queue: event advice
that is speaking is not cut off (help waits for it), and a help message is said once per park (group 1: once, `DiscardAfterSlaps` 3), unless
it is cut off, then it comes again until the third cut discards it. Video: the buy-window help plays at 2:25, 2:44 and 4:16 (three
starts, each cut by the next window), again three times in the next park, and the ticket-price help at 29:32, 31:16, 35:57 and 81:06, 82:26,
85:28; so the repeats are the slap rule, not a cool-down timer. The HUD windows with a traced message (eight at first, then research and the object windows below): buy 188, hire staff 189, all
attractions 191, loans 195 (full simulation only), finance 197, park information 199, all visitors 286, ticket price 303 and research 200 (builder `0x4AA480`, withdrawn by its procedure `0x4A96B0`), identified by the
title strings their builders load (`AdvisorUiHelp`). The object windows of a placed object add three builders: a ride (`0x4AF980`) posts 297 when the truncated state of repair (the float at the object's `+0x44`) is 1 to 20 and 207 otherwise, a shop (`0x4B0E30`) 209, a sideshow (`0x4B2430`) 208, and the close handlers withdraw them (a ride both); `ParkHud` reports the info arm as a window under a name that carries the kind, the repair choice at the time it is shown and the opening (`ObjectInfoHelp`). `ParkHud` reports screens opening and closing to the host, `AutomaticAdvisor.OnWindowOpened`/
`OnWindowClosed` post and withdraw. Panels and tools post
their help the same way (October 17, 2026): the message panel posts 284 when a tube message is opened (`0x48D9C0`) and withdraws it when the panel closes
(`0x48E4BF`); the map window 304 (`0x5F0B40`); the camera panel 305 (`0x498BB0`); the locator panels 198 (staff, `0x4B3F60`) and 287 (visitors, `0x4B3F60` with a
visitor and `0x4B4280`), both withdrawn by the panel's close message (`0x4B37D5`); the coaster tool's panel posts 289 (move), 293 (build), 294 (loop, 295 for a flume
coaster) and 292 (demolish) on the button press and withdraws all five when it goes (`0x4982A0`), and the tool's click handler 205 after every click (`0x52644C`); the
path tool posts 184 for its first accepted click and 214 for the third click after it (counter `0x8186DC`, in either path or queue mode), every way into the queue tool
203 (`SetMode(3)`, `0x52F4B0`) and 337 when a click ends it without joining a path (`0x527200`, the link flag `0x816D60`); the kart and water line tool posts 204 for the click
that stores the line start it had none of (`0x526D41`, a jump into the queue tool's post `0x527629`), which happens only after a line closed its circuit with exactly Ctrl held and no
placement's queue start pending (`0x8186C0`), and no mode change withdraws it; the buy list posts 201 for a chosen ride while no
ride is placed and 206 otherwise, 346 for Buy Land (tool `0x39`) and 345 for Clear Land (tool `0x3A`) (`0x4AC5D0`); the patrol button posts 193 once the employee window
closed (`0x4B5B50`) and the window's refresh 212 for an employee with an own patrol area (`0x4B5AB0`, withdrawn with 192); the object windows are chosen by
`Info.WhichUIType` (`0x486920`): rides 207 or 297, shops 209, sideshows 208, a feature that provides relief 213 (the truncated state's low byte below 25) or 299
(`0x497990`), one that chills you out 300 (`0x4B5290`), any other feature 298 (`0x499EB0`), a ride-type add-on of `AddOn.UpgradeType` 2 301 (`0x4B6AE0`) and a fixed item
none. `ParkHud` reports a change of the interface mode (the tool it runs) to the host, which withdraws the ten mode help messages, and holds a tool's help until
that report so the withdrawal comes first, as in `SetMode`; a help posted in the frame its tool ended is dropped, the original's `SetMode(0)` having withdrawn it
after the post (the line tool's 204, which no `SetMode` withdraws, is posted at once and kept). Help with no producer here: 285 (the All Staff window `0x496620`), 308 (the address book `0x4A8140`), 307 (the postcard window `0x4A8C00` posts only
outside game type 1, and OpenTPW's postcard runs only online) and 35 ids that no post names (204 was counted with them until its push was found ahead of the jump above). A cut-off while the speech sound still plays (the update
`0x599902` drops the handle once it ends) plays one of the three slaps of the global `cat_speech` bank, `0x27F` + `rand() % 3`
(`0x5995A4`; OpenTPW draws from a local presentation generator). Every tutorial (group 1) record offered to the queue is first copied to
controller `+0x16D0` (`0x59A940`), admitted or not; withdrawing that message clears the copy (`0x59AA7B`-`0x59AAC8`), and F1 in a help
window (the default window procedure `0x488BA0`, key `0x70` without modifiers) offers it again at `HelpMessage.ContextHelpScore` with its
once-only and slap checks overridden (`0x59AB50`). The help descriptors themselves score `HelpMessage.Score` (`+0x328`, `0x59E8C0`). The
copy is the save's "current record" and rides in OpenTPW's park save; `+0x16E8` (`mRequestedLastTutorial`) has no writer besides the
save/load and its clear, so the one update it skips only follows an original save that held it (reported under SAVE-010).

### Sequences and timing

October 14, 2026, from the decrypted Windows `tp.exe` (`[BIN:TPW-EXE:...]` citations at the sites):

- **Start.** The audio starts when the response is requested and the LIP clock base is request − 200 ms
  (`0x599050`). A sequence that opens with clip 14 (every generated sequence and the fixed sequences 21–27;
  only 16, 17 and 29 do not) stops that audio at once and restarts it when the advisor clock reaches
  request + 800 ms (equality counts, update `0x599880`); the LIP base moves there too. The talking flag is set
  to 1 at LIP load (`0x59930B`), cleared on the delayed-start path (`0x59943E`) and set again when the speech
  starts (`0x599987`); each mark then flips it once the clock is strictly past it, one flip per update
  (ADVISOR-013 retired). `AdvisorSpeechStart` holds these numbers; `Advisor.Update` starts the audio per frame.
- **Generated sequences** (`0x598B20`; 550 of the 610 responses): clip 14, then each next clip is
  `rand() % 10 + 1` (the C runtime's `rand`, a stream of the advisor's own seeded with 1; one draw per clip choice,
  including the ignored first), and a drawn clip longer than the remaining budget is replaced by the first of
  clips 1–10 that is at least as long as the budget; this repeats until the budget is below 1 or 19 clips are queued,
  and clip 15 ends it. The budget is the sample length + 200 + 300 ms. A clip's length is trailer word 2 × 1000 / 30
  as unsigned integer arithmetic (`0x474070`), from the level advisor's clips when loaded, else the global
  advisor's (`0x5989C0`; the entry of clip 14 is forced to 0). `AdvisorSequences.Generate`, tested without a GPU.
- **Span.** The response player returns the sequence sum + 1000 + the length of clip 15 (`0x5994AC`); a generated
  sequence already contains clip 15, so it counts twice.
- **Fixed sequences** (60 of the 610 responses; `AdvisorFixedSequences`, builder `0x598BF0`). The response row's sequence field
  (`0x768FB8`, 32-byte rows, field +0xC; −1 is generated) minus 16 selects a 0x50-byte row of the table `0x76DC18`, whose dword
  entries run to a 0. An entry with bit 0x400000 is a timer pair (flag, milliseconds): it sets the timer `0xF79778` to the clip sum so
  far plus the milliseconds and adds no clip. Any other entry is a clip (`entry & 0xFF80FFFF`; bits `0x7F0000` go to `0xF79660`) whose length
  is the clip table's. If the first clip is not 14 the immediate-start word `0xF79694` is cleared (audio starts at the request: sequences
  16, 17 and 29). Ten rows are in use: 16 (clip 16), 17 and 29 (clips 17, 17, 17, 17, 18, 19, 20; 29 has a 1000 ms timer before clip 20),
  21 to 23 and 25 to 27 (clip 14, two or three body clips, clip 15) and 28 (a 17000 ms timer only); rows 18–20 and 24 are empty. A
  sum of 0 with a nonzero budget falls back to the generator with the same budget, so 28 is generated (and keeps its timer), and so is
  any row whose clips are missing from the archives: clips 16–20 exist only in the level advisors. The span is then the same sum + 1000
  + the length of clip 15. When the advisor clock reaches the timer (`0x599991`) the update calls the particle effect `0x521E60` (87,
  90000, 0, 7000), plays sound `0xC6` (global `cat_ui`, flat) and calls `0x4B9340`; OpenTPW starts the Key effect (template 87,
  screen-space: the top right of the screen) and plays the sound once per response (the deadline minus the clock as a signed
  32-bit difference, not positive). `0x4B9340` refreshes the lobby's key panel and returns at once while that window
  (`0x7CC4B4`, created by `0x4B8CA0` from the lobby) does not exist, so in the park it does nothing; OpenTPW's lobby shows the saved key count
  only as a snapshot taken when the menu is built, without that refresh (ADVISOR-025). Without any clip archive every length is 0 and
  the original returns 1000; OpenTPW keeps speech + 200 + 300 + 1000 for such a partial install (`[EXT:advisor-no-clips]`).
- **Shown.** The model is shown when any animation other than 12 starts (`0x4732A0` → `0x45BD30`). "Kill advisor"
  (`0x429D60`: animation 12 with the immediate flag, then unlink) hides it at once; it is called by the stop path `0x5996D0` (six
  callers, level exit among them) and the slap path `0x5994E0`, and the normal end of advice (`0x599880`) does not call it. But the
  animation sampler `0x4735D0` hides it at the end of the last clip (ADVISOR-018, settled October 14, 2026, instruction level): at a clip
  end (frame past its length, `0x473743`–`0x47374E`) with nothing queued (`[channel+0x24] == 12`, `0x47375D`) and model flag 8
  (`0x473769`–`0x473776`) it starts the idle set 12 (`0x472F60`) and, when the object is shown (`0x45BDC0`), hides it with `0x45BDD0`
  (scene unlink `0x576540`, clears bit 0x19; with model flag 0x4000 it calls `0x45BE80` instead). The advisor model has flag 8: it is
  loaded with flags `0x50120` (`0x429C9D` → `0x461F10`, which ORs 8 into the template at `0x4621C9` when load flag 0x100 is set;
  `0x4557C0` copies it into the model). The update starts the next clip only while the channel reads the idle set 12 (`0x599A23`), and
  `0x4732A0` shows the model again (`0x4732C6`–`0x4732DD`); after the last clip nothing starts, so the model stays hidden until the next
  response. The earlier round's statement that only Kill hides it was true of `0x599880` itself, not of the sampler. OpenTPW therefore
  draws the model until the sequence's clips have played out since the request (`Advisor.DrawnSpanMilliseconds`: each clip for its own
  length, clip 14's 1000 ms included, which the budget table leaves out) and clears `ModelShown` there. `Advisor.Kill` is called when the level is left. A fixed row's clips can sum to less than a long speech (rows 21 to 27), so the model then hides while the speech still plays; that
  follows the trace and a capture should confirm it. Not checked:
  the model-flag 0x4000 branch and the frame order between the update and the sampler.
- **Without sound** (`0x802BC8`/`0x802BD4` clear; set at `0x51B660` only when sound init succeeds) the sample is 1 ms
  long (`0x51C570`), there is no sound handle (`0x51BFC0`) and the advice ends with a sequence built from a 501 ms budget.
- **Balance.** Nothing reads fields +0x18/+0x1C (`MinTimeAnyMessage`/`MinTimeSameMessage`) of the `Advisor.sam` object
  (static `0xFB3540`, built by `0x59BEA0`); `+0x20` (`MinScore`) is read at `0x59B6C3` (absence result, medium-high
  confidence; ADVISOR-019 retired). The synthesis window table at `0x70BD08` (544 floats, −0.5·D[32·tap+row]) equals the
  decoder's (ADVISOR-014 retired).
- **Events 2/3/4.** Bankruptcy (event 2, `0x4D0370` at the bank's month-end message type 0xC via `0x4D02D0`): balance < 0 (`0x4D04DE`),
  previous balance (+0x11C) < 0 (`0x4D04E4`–`0x4D04EC`) and `__alldiv(0x4F88B0(world+0x2A0, turn entered red +0x120), 2.592·10^13) ≥ 6`
  (`0x4D053B`–`0x4D054F`) post event 2 through `0x40F630(2)` every month while it holds; the turn entered red (+0x120) is set when the balance
  first goes below zero (`0x4D041A`–`0x4D0432`); it does not close the park. `0x4F88B0` gives the park time since the stamp: the unsigned 32-bit turn difference × the calendar rate (15000) / 4 × 10^7, in 100 ns units. Events 3/4 are posted by the
  door routine `0x519EF0` on every press of the open/close button (control `0x4F3B1`) and of the Ctrl-O/Ctrl-C shortcuts (`0x40C410` calls
  it), also when the park is already in that state (event 3 at `0x51A033`, event 4 at `0x51A1C6`, both reached from either state). OpenTPW
  raises 3/4 the same way, from the HUD's door button and shortcuts (`HudHost.ParkDoorPressed` → `AutomaticAdvisor.OnParkDoorPressed`),
  not from the economy's open/close transition. Event 2 comes from the economy's month end the same way (`ParkEventKind.BankruptcyMessage`, every month while the condition holds); the bank's own handler of the message loses the park once (ECON-030).

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
| 2 | `ParkEconomy`'s bank month (`BankruptcyMessage`), every month end more than five months in the red | 106 (`Bankrupted.Score`) | 274/275 → global 424/425 |
| 3 | Every press of the open button or Ctrl-O (`ParkHud`), also when the park is already open | 128 (`ParkNowOpen.Score`) | 308/309 → global 342/343 |
| 4 | Every press of the close button or Ctrl-C (`ParkHud`), also when the park is already closed | 129 (`ParkNowClosed.Score`) | 310/311 → global 344/345 |
| golden ticket won (`ParkEventKind.GoldenTicketWon`) | `AutomaticAdvisor.Challenges.cs`, after the economy's ticket check | 215, 216 or 217 for a local ticket, 220-222 global, 225-227 the secret camera ticket (`GeneralWonGoldenTicket.Score`, group 2) by the key rule of the player's ledger, with the ticket's type as the response; with exactly one ticket in all, 311 and 315 follow (`WonFirstGoldenTicket.Score`, `GoldTicketCluesForRemainder.Score`) | responses 434-439, 446-451, 458-463 (215-217, one per ticket type); global and secret messages have four and two |
| challenge offered, won, lost | `ParkObjectives` events (`ChallengeOffered`, `ChallengeCompleted`, `ChallengeFailed`) | 270 for an offer (344, a tutorial help line, for the first offer of the park), 272 for the win of the last part of a chain, 273 for a loss (`Challenge.Score`; full simulation only) | responses 520-522, 526-528, 529-531 (three each) |
| prank message (type 0xE) | A prankster's prank (`GuestSimulation.Pranked`; the bus `0x40fb10` delivers it at once). The message's kind word is the prankster's id modulo 3 (tp.exe `0x4ff2f8`), not the prank | 125, 126 or 127 for kind 0, 1, 2 (`PrankSbomb.Score`, `PrankLitter.Score`, `PrankBalloon.Score`, optional in the file; `0x59b060`) | 302/303, 304/305, 306/307 → global 336–341 |

`Advisor/Advisor.sam` is read at runtime with the bounded `.sam` reader (≤ 1 MiB):
`MinScoreForConsideration`, the group repeat/once/slap controls and the eight
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
still picked, consumed and recorded silently (`0x10006BB4`, `0x1000BBF0`). With `--mute` the
advisor's speech keeps the wall clock and opens no audio device. Game ticks are the economy's park turns
(`mGameTick`); the sandbox has none, so its ticks stay 0.

With the shipped `Advisor.sam` (minimum 25) the welcome (100,000) plays at once,
the prebuilt-park advice (1,000) follows in Instant Action after the reservation,
bankruptcy (100,000) plays, and park open/close (20) stay pending and never play.
Imported parks saved open are opened during level construction, before the
advisor attaches, so that load-time opening raises no event; a park started without
a save stays closed until the player opens it (tp.exe `0x51562d`).

Contextual help ("Contextual help" below) adds 81 help messages (of the 82 descriptors with the help score; 291 names the absent response 551) and eight HUD windows' producers. The producer of every one of the 351 messages (scan, event, help or none), its inputs and its OpenTPW status are in docs/reverse/ADVISOR-SCAN.md and `content/data/advisor-producers.toml`. The background score scan is wired (`AdvisorScan`, docs/reverse/ADVISOR-SCAN.md "The scan"): once per park turn it scores one scanned descriptor and queues what is above the minimum; the money batch is its first producer (advice 100-105 and 107, ported from `0x59E8D0`-`0x59EB80`; 102 and 101 in the full simulation only), and the golden-ticket advice 234-237 is defined. The park-opening, gate, congratulation, tutorial and golden ticket hint callbacks are the next scan producers (advice 1, 2, 124, 140, 156, 157, 185, 274-276, 278-282, 314, 316 and 317, `AdvisorChallengeCallbacks`: the arrival group sizes behind 124, 140 and 281 are the advisor's own record of the last five groups, `GuestSimulation.ArrivalGroupStarted`), and game code posts the ticket wins and the challenge lines (the two table rows above). Not wired: the other 9 computed
score producers (coverage, prank, queue link and ride fear inputs OpenTPW lacks), prank messages and help that are not listed above (the challenge warning, advice 271, is wired), the tool-start help and the recovered
`OriginalAdvisorLipDriver` timing (it needs the original pause-aware advisor
clock, which OpenTPW does not reconstruct). The bankruptcy producer is the bank's monthly guard (`0x4D0370`, `ParkEventKind.BankruptcyMessage`). Read-only visits of
shared parks (online extension) and the generic sandbox (`--sandbox`, plain
`--smoke-test`, sandbox saves) have no automatic advisor.

The scan's queues, prices and research batches (`AdvisorQueuesCallbacks`, `source/OpenTPW/World/AdvisorScanQueues.cs`) add 31 producers: the researcher lines (advice 15-17, ported from `0x59CEA0`-`0x59CFE0`), the research-more and upgrade
lines (61, 63, 64, 72, 76, 80), the ticket price lines (91, 92: the mean of perception ring 0, which the booth's verdict `0x004FF5B0` now fills with the `PeepInfo.OpinionFor*` bytes) and the shop value and satisfaction lines (238-257,
`0x59FB60`-`0x5A0290`; the ten "poor" lines compute a negative score for the shipped multipliers and never pass, as in the original). The six research-more and upgrade lines belong to the full simulation only (descriptor game-mode selector 0).

A park started from an original save (Instant Action, `--load-original-level`)
first takes the save's advisor controller (thing model 11, serializer
`0x82e0`; docs/reverse/SAVE-plan.md): its eight pending records (`0xbc10`:
message, score, response number and once-only override; records with the valid
byte clear are free slots, including the uninitialized `0xCD` ones) keep their
slots, and the 351 history entries (saved tick, variant, played byte, slap
count, indexed by message ID) replace the controller's history. Then the level
start raises events 10 and 0 as before. Records and non-initial history of
messages without a descriptor (an ID past the table's 351 records) are dropped and counted in the log line; every
descriptor of table `0x76E300` is bound (`content/data/advisor-descriptors.toml`, ADVISOR-022 retired), so the pending
advice and history of any message are kept, with the saved score, although most messages have no producer yet (ADVISOR-009)
(Easymode: the one valid record, ParkNowOpen with score 20, and Welcome's history are imported). OpenTPW's own park save
(`GameFlow.SaveCurrentPark` → `ParkResumeSaveFile`) does store the controller: the queue slots, the history, the reservation and the
advisor clock (`AutomaticAdvisorSnapshot`); it refuses to save while speech is active.

Native check (macOS arm64 Metal, October 10, 2026): `--smoke-test
--load-original-level jungle` passed and logged `Advisor game event 0
(LevelStarted): advice 0 Eligible in slot 0` and `Advisor says response 1
(sample 1, /levels/jungle/Speech/speechHD.SDT): 28,63 s, 35 LIP marks, clock:
game mixer (SDL audio queue)`. Audibility and on-screen appearance were not
inspected.

### Restored advice (EXT, October 17, 2026)

The official extension `opentpw.restored-advice` (on by default, `AffectsSimulation` false; [MODS.md](MODS.md)) says advisor messages that exist in the data but that tp.exe v2.0 never posts (the 103 "none" rows of [reverse/ADVISOR-SCAN.md](reverse/ADVISOR-SCAN.md)); it restores 42 of them (thirteen lines stood down because the original's own ticket advice, 215-227, first-offer line, 344, challenge offer and win lines, 270 and 272, and kart and water line tool help, 204, are now ported), each trigger labelled
`[EXT:restored-advice-<topic>]`. It adds no data and no queue: a restored message is pending advice like any other, with its descriptor's group, its original response rows, its `Advisor.sam` score where its descriptor's callback names a key and
otherwise a documented constant, and it goes through the same admission rules (tutorial option, once-only, repeat interval, pending-duplicate limit, game mode, `MinScoreForConsideration`). Three entry points, all gated on the extension at the moment they run:

- the **scan hook**: a scan-zero row's callback (`0x005D60C0`) is replaced by the extension's own while it is on (`AdvisorScan.ScoreOf` asks `RestoredAdviceScan.CallbackFor`); the scan's cursor, row order and eligibility check are the original's;
- the **poll and events** of `RestoredAdviceDriver`: one state-driven rule per scan call for the rows the scan skips, and the park events, arrival vehicles and online screens that post the rest;
- the **help hooks**: windows (`RestoredAdvice.WindowHelp`), tools and the message tube post tutorial hints through `HudHost.RestoredHelp`; `AutomaticAdvisor.PostHelp` drops a restored help message while the extension is off.

The extension keeps no saved state. The pending records and history it causes are in the advisor's own snapshot, which has the same shape with the extension on or off and loads either way. `AdvisorRestoredAdviceTests` fires each trigger through the real queue in a constructed park state and checks that off is silent.

## Raw LIP authoring

`LipSyncFile.Encode(IReadOnlyList<uint>)` emits the documented raw little-endian
marks and final terminator. `LipSyncFile.Write(Stream, IReadOnlyList<uint>)`
encodes and validates everything before writing at the caller's current stream
position. Both enforce the reader's odd, nonzero mark count, strict increase,
reserved terminator and 64 KiB bound. A leading zero and values through
`uint.MaxValue - 1` remain raw values, without signed conversion or clamping.
Pass a decoded file's `Marks` to encode it again or supply your own list.

The writer leaves the stream open and does not seek, truncate or flush it;
nonseekable output is supported. Invalid marks leave its bytes and position
unchanged. I/O failures propagate and may leave partial output: the caller owns
file replacement, rollback and durability. The input list must remain stable
during encoding. No compressed representation, phoneme/pixel data, mouth-shape
generation, timing conversion or native authoring compatibility is claimed.
`LipSyncWriterTests` pins canonical bytes, raw read-back and current timeline
interval/event equivalence, leading zero, the reader's maximum size, malformed
counts/values, nonseekable ownership and failure behavior using synthetic data.
Original-data, native advisor/audio and GPU qualification remain separate gates.

Independent writer controls also cover index-only lists at the minimum and
maximum count without enumeration, current-position writes preserving prefix
and trailing bytes, late input-access failure before the first output write,
and an output that writes a prefix before throwing. Reader and current managed
timeline events roundtrip unchanged; partial I/O output remains caller-owned.

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
simulated audio output, plus 3 private tests: model orientation/co-located mouths, every
`Advisor*m*` clip decoding and playing against its model, and loading `sp_001` through the game file system).
Private tests are inconclusive without `OPENTPW_GAME_PATH`. `Layer1DecoderTests`
adds 27 generated cases and two private checks (`z_error` reference values and
all 32 global UI-bank entries). `Mp2ErrorProtectionTests` adds the CRC and
de-emphasis cases of the follow-up above (11 generated + 1 corpus inventory).
Before remote integration, the combined Layer I/Layer II/LIP filter passed
49 cases without skips. The merge retains those tests, the parallel branch
codec tests and the fourteen compressed-entry metadata cases; current
verification totals are recorded in [PROGRESS.md](PROGRESS.md).

## Search method (location)

- Upstream `opentpw-docs@34f357f` `src/formats/lips.md`: title only, `TODO`.
- Loose `Data` walk (801 files) and an independent Python DWFB + RefPack scan of all
  312 WADs (13,394 members): no LIP-shaped data outside `.LIP` members.
- `7z l` of `TPWORLD.ISO` (2,989 entries): 20 `.LIP` loose files and 5 `lips.WAD`.

## Screen effects

The ticket and key advice draws screen-space particles (`ParticleScreenSpace`, `AdvisorEffectGroups`). A template with byte
`+5` set is a screen-space one: its native x and z place it on the screen and its y is ignored. The draw list (`0x51F01A`)
turns the emitter-scaled position into `((px - 0xC35) << 4) / 49` and `((pz << 4) - 0x927C) / 37` (integer, truncating), so
native (50000, 37500) is the screen centre and one screen unit is about 50000 native x or 37500 native z; the quad builder
(`0x57C620`, flag `0x40000`) uses that / 1024 as the vertex position without the view transform, in the screen space the
advisor is drawn in (−1..1, x right, y down), with the half-height (size >> 6) / 2048 and the quad turned about its centre in
1/256-turn steps (`0x591430`). Every shipped screen-space template (Button, GoldenTicket, GoldenTicket2, Key, Key2, the message
tags, KeySparkle, KeyPuff, CongratSparkle, Test2D) has z 0 and blend flags `0x2004`; OpenTPW draws them without depth.

The advisor keeps three effect groups of up to five emitters (`0xF86900`, 0x44 bytes each). `0x5ED5A0` sizes a group (new
emitters start at the native origin, surplus ones end), `0x5ED660` moves its centre from the last target (or jumps) to a
point over a time on the advisor clock, `0x5ED700` sets its orbit radii and turns, and every frame `0x5ED770` places emitter
i of n at the angle `frac(i / n + t * turns) * 512` of the 512-entry table: (cx + rx sin, cy − ry cos), the centre following t
unclamped and the radii t clamped to 1. A fixed sequence's clip start applies its accessory bits to group 0 (`0x598EAA`, with the
response active): `0x20000` one GoldenTicket jumping to (0.34, 0.62) with radii (0.01, 0.2), one turn; `0x40000` two
GoldenTicket2 moving to the centre with radii (0.6, 0.4), two turns, and `cat_ui` `0xC5`; `0x80000` three GoldenTicket2, three
turns; `0x100000` two fresh GoldenTickets flying to (0.8, −0.8) as the radii shrink to 0; `0x200000` none; each move lasts the
started clip's table length. The response's start, its end (speech done and the clip sum passed, `0x59992D`) and the kill
empty group 0. The sequence timer adds the Key effect at (90000, 0, 7000), screen (0.8, −0.8).

## Approximation register

Every rule below is tagged `// [APPROX:<id>]` at the listed site. `Advisor` logs each one
once via `Log.Warning` when it is created. Values from original data are tagged `// [DATA:…]`
(asset paths, mouth mesh names, clip range 1–637, SDT header-size word, LSF allocation
table fit). The smoke-test thresholds are test-harness checks, not game rules.

| ID | Site | Current value / rule | Evidence needed |
| --- | --- | --- | --- |
| ADVISOR-005 | `source/OpenTPW/World/Advisor.cs` | The advisor is lit per vertex like the original (`content/shaders/advisor.shader`: ambient 0.15 grey, `0x573FD0`, plus the directional light of colour 1.0, `0x429BA0`, clamped per channel; `0x574660` → `0x5741B0`); no fog is drawn | Which lobby screen each frontend lighting state is and which show the advisor: the ambient words `0x879DD8`-`0x879DE4` are (alpha 1.0, R, G, B), 0.15 grey by default; `0x5D8B50` and `0x5D8D70` set them black, `0x5D9230(1, rgb)` a colour (`0x202020` from `0x5DCFE0`, `0x444444` at `0x5E13A9`, `0x5E1924`, `0x5E2445`); and whether the original fogs the advisor model at all |
| ADVISOR-009 | `source/OpenTPW/World/Advisor.cs` | `--advisor-say` plays global clips by number and starts them at its first rendered frame; responses follow the traced global/level selector; the controller says messages 0, 33-57 (staff strike messages), 66, 68, 69 and 258 (the ride breakdown lines), 70 (a mechanic on his way to a broken ride), 93-97 (research done), 106, 125-129, 133-136 (new research groups), 167 (all research done: Instant Action, and the full simulation once the player's records show every park complete, `0x5B0C40`), 215-217, 220-222 and 225-227 (golden ticket wins) with 311 and 315 (the first ticket), 270, 272 and 273 (challenge offers, wins and losses; 344 at the first offer), 271 (the challenge system's short-time warning), 323 and the help messages of the HUD windows, panels and tools OpenTPW has (see "Contextual help": buy, hire staff, all attractions, loans, finance, park information, all visitors, ticket price, research, employee, visitor and map windows, the object windows, the message, camera, locator and coaster panels, the path, queue, kart and water line, patrol, build, Buy Land and Clear Land tools and the hire's Place tool); all 351 descriptors of table `0x76E300` are bound as data (`content/data/advisor-descriptors.toml`, with the scan flag) | The producers of the other messages: of the 351 descriptors 195 are posted by game code (`scan = 0`: the 82 help messages and the event and prank messages) and 155 are scored by the background scan `0x59C680` (one scanned descriptor per update, in table order), 21 of them score 0 (`0x5D60C0`) and 134 compute their score from the park's state through 85 park-statistics helpers (`0x4C7BB0`-`0x4D0A00`, `0x519450`-`0x5195D0`: counts and averages over the thing list by type, per-ride 30-sample history windows, litter, staff happiness) and constants of `Advisor.sam` (the object's fields from `+0xA0` in the order of the schema `0x72E124`); the scan is ported (`AdvisorScan`) with all 134 computed callbacks (the money batch and the rides, cleanliness, guest mood, staff, queues, prices, research and golden ticket batches, and the last nine, 12, 19, 21, 22, 27, 98, 113, 141 and 264, over the guards' chase counters, the patrol coverage, the first unconnected queue and the mean ride excitement: `AdvisorInputsCallbacks`, `docs/reverse/ADVISOR-SCAN.md`); game code posts no advice beyond the ported ones (218-219, 223-224 and 228-233 have no producer in tp.exe); help messages without a window or tool here: 285 (the All Staff window `0x496620`), 308 (the address book `0x4A8140`) and 307 (the postcard window `0x4A8C00`, posted only outside game type 1: OpenTPW's postcard is online only), and 35 help ids that no post names (186, 187, 190, 194, 196, 202, 210, 211, 266, 283, 288, 290, 296, 306, 322, 324-336, 338-344; 204, once among them, is pushed at `0x526D41` ahead of a jump into the queue tool's post `0x527629`: the kart and water line tool's help) |
| ADVISOR-025 | `source/OpenTPW/World/AdvisorSequence.cs` | A fixed sequence's timer (sequences 28 and 29) starts the screen-space Key effect (87 at native (90000, 0, 7000)) and plays `cat_ui` `0xC6`; its call `0x4B9340` does nothing in a park and refreshes only the island lobby's key panel (window `0x7CC4B4`, layout Lobby `0x757F60`), which OpenTPW's lobby shows only as a snapshot taken when the menu is built (it shows the key group `0x1E0EC` for a positive key count outside Instant Action, formats "%d x" into `0x1E0EE` from the player ledger object `0xF7C58C`, and keeps particle `0x61` running while the count meets the selected record's threshold; OpenTPW has no live refresh, no EA/EB art and no effect `0x61` there); screen-space particles are drawn in the world pass, under the HUD and the advisor image | Whether the lobby says sequences 28 and 29 and the player ledger the panel reads (UI-051); the order of the original's particle pass against the HUD and the advisor |

Retired October 14, 2026 (traced in tp.exe, citations at the sites): ADVISOR-008 (audio start), ADVISOR-013 (talking
flag), ADVISOR-014 (synthesis window), ADVISOR-019 (balance fields +0x18/+0x1C are not read), ADVISOR-016 (the ten fixed
rows of table `0x76DC18`; the timer effect's lobby refresh is ADVISOR-025) and ADVISOR-018 (the sampler hides the model at the end of the
last clip). ADVISOR-027 (the cut-off slap, the last tutorial record and its F1 replay) retired on October 15, 2026. ADVISOR-026 (the node bits: every clip end clears them) retired on October 17, 2026. ADVISOR-012 (the speech's left and right levels) retired on October 28, 2026: every event of `cat_speechSFX.map` (the global one and each level's) carries the any-distance flag `0x200`, so the library places the voice flat (`0x6BC51D`, `QSWaveMixSetPosition` with the integers 0, 1, 0), QMixer centres it at distance 9.2e-5 with `SetPan(0)` (`0x18007ABD`, `0x18009DC0`, `0x18005982`) and the mono samples play at the same level on both channels, which `SpeechAudioPlayer.ToStereo` reproduces (see docs/AUDIO.md, AUDIO-018). ADVISOR-002 (costumes) and ADVISOR-023 (the −1..1 screen space, confirmed by the video: the body centre at 0.80 of the
frame in both axes, `[EXT-EVIDENCE:video tpw-nl 1:50]`) retired on October 14, 2026 (video round). ADVISOR-011 is an extension (`[EXT:advisor-no-device-speech]`), as is ADVISOR-024 (`[EXT:advisor-aspect]`).

## Remaining gates

Original-runtime observation of the mouth shape choice while talking, the
remaining advisor triggers and score producers and A/V latency. The sequence and mouth
draws use the advisor's own `rand()` streams, not the C library's shared one (the original interleaves every caller). Original sound-bank/category scheduling
and device latency still need verification. Layer I
codec support is implemented; automatic sound events are not wired by that change.
The codec's CRC mismatch policy and de-emphasis realization are documented
approximations (AUDIO-013/014): no shipped frame exercises either path, so the
original player's concealment behaviour and filter realization remain unknown.
Capture comparison remains necessary before claiming original fidelity.

### MPEG-1 Layer II intensity joint stereo

The version3 Layer II mode1 header now routes through the existing A/B/C/D tables.
The effective shared-region boundary is `min((mode_extension+1)*4,sblimit)`.
Allocation and sample triplets above that boundary are transmitted once; SCFSI
and scalefactors still belong to each channel. The existing requantized sample is
scaled separately for left/right and fed to the unchanged synthesis/rounding path.
CRC includes the actual shared allocation once and both nonzero channel SCFSI
fields. MPEG-2 LSF joint remains unsupported; no Layer I coverage policy changed.

This follows [ISO/IEC 11172-3](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf)
§2.4.1.6/2.4.2.3/2.4.3.3 and Annex B.5; the effective boundary clamp is corroborated
by [official FFmpeg Layer II source](https://www.ffmpeg.org/doxygen/9.0/mpegaudiodec__template_8c_source.html).
Only numeric and field-order facts are used, with no vendor implementation copied.

A dedicated independent field writer emits both channel factors and explicit
protected fields; mathematical silence and independently encoded ordinary-stereo
packets protect all boundaries, tables, shared/independent channels, ordered
triplets, SCFSI patterns and CRC spans. Actual SDT metadata/WAV readback, source
identity, strict output refusal and cooperative cancellation are also tested.
The separate [independent joint reference measurement](MPEG1-JOINT-REFERENCES.md)
measured a maximum 1 LSB difference across 172 original synthetic four-frame joint
vectors plus 3 low-row endpoint vectors; 43 representative packets and independent
PCM excerpts are permanent regressions. This joint envelope was measured separately
from the nonjoint 231-vector result. No original assets, native player or full MPEG
conformance are qualified.
