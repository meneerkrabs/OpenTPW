# MPEG-1 nonjoint Layer II verification

October 14, 2026. Generic format support, not original game decoder or full MPEG
conformance qualification. Production uses independently written header/profile
selection from [ISO/IEC 11172-3](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf)
§2.4.2.3 and Annex B.2a-d. Existing quantizer/scalefactor/synthesis/rounding and
emphasis algorithms are reused; no external codec source, library or binary is
vendored. The low C/D allocation15 entry is 32767 levels/15bits (repo class16),
corroborated by the [official FFmpeg table](https://www.ffmpeg.org/doxygen/9.0/mpegaudiodata_8c_source.html).
PL_MPEG's different65535 endpoint was not adopted.

## Independent generated reference

A separate reviewer generated 231 synthetic four-frame MPEG-1 streams from the
standard fields, without the production decoder or test builder. They cover
A/B/C/D, 44.1/48/32kHz, mono/stereo/dual channels, grouped and ungrouped classes,
SCFSI0..3, nonuniform ordered triplets, last subbands and unclipped C/D allocation15
quarter-scale DC. No game assets or original executable input was used.

The already installed Homebrew8.0.1_4 FFmpeg8.0.1 Mach-O arm64 executable's
fixed-integer `mp2` decoder produced signed16 little-endian interleaved PCM with
no requested rate/channel conversion. Representative command:

```sh
ffmpeg -hide_banner -nostdin -v info -f mp3 -c:a mp2 -i generated.mp2 \
  -map 0:a:0 -c:a pcm_s16le -f s16le reference.s16le
```

Executable SHA-256:
`0a96da2735695308d964e25fa6f4a0db2e9d24031390360f4c5ff96a4f8938e5`.
Its installed build reports GPLv3-or-later (`--enable-gpl --enable-version3`).
Only its generated PCM output is recorded; no GPL/LGPL implementation is copied,
no tool/binary is redistributed, and no native codec dependency is added.
Independent generator hashes:

| Generator | SHA-256 |
|---|---|
| generate.py | 1c46b2c096495ca1b42dba6878d98ef007bf4c4d3c4e02b8e77a72eb23db1521 |
| supplement.py | 6506856ec0b0b1a7d6a33076cad05f718b90b09e4253a5d1fc8103a7780eb333 |
| quarter_scale.py | 3ac8eb8ae7145702654b2240412b003fa7b2185303376db57daa26a8fd1e6e37 |

The author separately verified every source/PCM hash and actual header rate,
channel count and four-frame length, then compared every sample of all231 full
reference vectors against the managed decoder. **Measured maximum difference:
1 LSB** on this arm64 run. Silence was exact. Analytical steady DC checks use the
existing2LSB envelope; unclipped code8191/scalefactor3 in the32767-level class
represents -16384/32767×32768, so a wrong16bit mapping cannot hide behind clipping.

This is a measured envelope for these generated signals, not a promise over all
bitstreams/platforms and not PPC/x86 parity, device timing, original assets or
full ISO11172-4 conformance. Existing ADVISOR-014/AUDIO-013/014 qualifications remain.

## Permanent tests without external tooling

`Mpeg1LayerTwoReferenceTests` embeds fifteen selected first-frame byte strings,
repeats each four times and checks the complete source SHA-256/rate/channels/frame
count. Twenty-five sample-frame positions per channel span startup, all SCFSI
parts and steady output. Pinned PCM excerpts must stay within the observed1LSB
fixed-integer-versus-managed envelope. These tests require no temporary files,
FFmpeg installation, private assets or external fixture generator.

The complete reference PCM hashes below identify the independently produced
vectors from which the excerpts came; the permanent test checks excerpts, not a
bit-identical full managed-output hash. Source hashes cover four complete frames.
The author-only all231/full-vector measurement and manifests are retained in the
review evidence, not loaded by the test suite.

| Fixture | Source SHA-256 | Reference PCM SHA-256 |
|---|---|---|
| A-mode3-class1 | 4c53caf6e392c1d860acb9ce4dc96bc0dc4a51497638e643d9561f8223cf8eb0 | 5bf2e700ca1aaab9e080b6e9cbf5656cd86f9cde24a24fa42b0ed96a60a80724 |
| A-mode0-class2-ordered | 791155cde3d54d80b086e54abdc7906a0e521352a1ee918dd3d7a9bbf966d442 | bace2a5b27074ff88a7881a2ef9619ee2ad31746668e14b9af73402ff366e9d4 |
| A-mode2-class3-ordered | cdf8c55d831072669ec61ffd83956848eaa8c0ea89160281b0962d2b8bc59121 | 06ecf7eb0cacbacdea4252f983440a9dd3a4dbaef13457baf6dcf59f8fb5a723 |
| B-mode3-class4-ordered | b49b32dfebd7d9b8f883b6015ca6baa39e798af0e1a2927744682a5a954d988f | b4ea37c18d53eaf766d530d69745886b8cd1f691d4e906b1dc3d32e50a38be3e |
| B-mode0-class5-ordered | ab4c68b0faa6deb6ead4185575055f6fbe52f7575472fe98bdbce5a3fd654d34 | 438eda47fa69b819a3e6a49b53b32f8ab5b2c53d456dbf756dba56134b95f9cb |
| B-mode2-lastband | fcf790e9b97fd2ba704c23857bd1e6c218c3f3eba16662db19eb13e1e288b39d | 9938d933437974ad35aedc5c20ccecbdfed41f363a827dfb90237c1b46b3457d |
| A-mode3-scfsi0 | a00343ff79cc2c703a7019ddcbc0a0dc2979275e8582f322b299cdf6252856cd | e8026fb695c2f8a0067ac915ce43661c6e441b9996a7940bf91be54f01aa691a |
| B-mode0-scfsi1 | 2b20612f64219a46d63ff3d2cb0a3a080c0a67843967d78b9c1fbf6931e1dad9 | 4a900853fadd8c17d96915abaa38e964c99f84e84f7b75275a69d6b8f81616fd |
| D-mode2-scfsi3 | 888ca8a93a91654c11ae32dd9aae06044c05376806f804b1806ab4583eb8d6eb | 198a4d4fcb71c2f5d4f8d23009d490506b6a8142ce8e43f53cf6d99fe8d239c0 |
| C-mode3-class4-ordered | 336d1430b7a07a94543b47c92d87732d70a3cb3f58065b4d00b0a630b0eeecd6 | ce80ccaa88de4b944b4d81a94fc39fc723bfee58e8b5ff722d233a39bc9f86e7 |
| C-mode0-class16-ordered | e51bde2b0cdb0fc8794947b71a85480eca1b5fc2af5ff2e93b16c3571c4fd388 | ee2edc2307c9064217adf0298b5b1a41d08196cc20a94b288539e0d33194c904 |
| D-mode2-class16-ordered | d3b9fcb0d21d91502aa29fa5800b00be6e1db6836afef1ce5acfdf6c1b517296 | ee2edc2307c9064217adf0298b5b1a41d08196cc20a94b288539e0d33194c904 |
| C-rate44100-mode3-code15-negative-quarter | 62862229c03e70705138ba753c06c24100f9f9b967c1e33e70eb6f75ee0670aa | b3c3a3d0ffbc60f9f02285487e2aedfa35b10e77ef94238a4e5f194918c093fe |
| C-rate48000-mode0-code15-negative-quarter | 6eeb29b6e96c8e3d45ba15c475d9f03577e20d9a0a3524592c0ff47f47921802 | 7ef404745659ae9d29757ff58761a7a7dc4a8c006834615a45440e959dcdca48 |
| D-rate32000-mode2-code15-negative-quarter | 2e6a0adf88af5228092595ca78b0d0e6ac5db80b28de3ce73606a3cd47a7befd | 7ef404745659ae9d29757ff58761a7a7dc4a8c006834615a45440e959dcdca48 |

`Mpeg1LayerTwoTests` separately uses an independent standard-field encoder and
mathematical silence/DC controls: every legal header and illegal mode/bitrate
pair, row and subband boundaries, padding/ignored nonjoint mode extensions,
SCFSI/grouping/channel independence, current-frame table transitions, corrected
CRC coverage and excluded scalefactor bits, truncation and first-complete-header
SDT metadata. `Mpeg1LayerTwoWavExportTests` runs the existing selected-entry writer.
Layer I (including its existing joint mode), LSF maps/PCM, cooperative batching
and mismatch policy retain their previous behavior. Layer II joint, freeformat,
MPEG2.5 and LayerIII remain explicitly unsupported.
