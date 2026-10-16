# Format implementation priorities

State of October 16, 2026 (the October 9 list it replaces had fallen behind the code). Parsing,
decoded content, field meaning, engine integration, platform execution and original fidelity are
separate gates: a format whose bytes all decode can still be missing from the game, and that is
engine work, not a decoder gap. The README's format table follows the same split.

| Format | Decoded | Open in the format | Open outside the format |
| --- | --- | --- | --- |
| WCT | Every texture of the selected game; bounded inflation, checked coefficients, exact BILZ output and checksum, checked LZSS input with the retail quirks (990 header-inclusive sizes, an odd final byte) ([WCT-ROBUSTNESS.md](WCT-ROBUSTNESS.md)) | Other editions | — |
| SAM | `SettingsFile` reads the shipped line grammar (one-token or quoted value, `#`/`//` comments, `---` blocks skipped, a bare key is no setting) from any stream, and agrees with `ObjectSettingsFile` on every shipped `.sam`, loose and in WADs; `SamDocument` keeps multi-value balance lines ([ECONOMY.md](ECONOMY.md)) | — | Many keys' gameplay use is approximate |
| SDT / MP2 | Containers, `SoundCatalog`, MPEG-1/2 Layer I and II in every shipped mode (≤1 LSB against ffmpeg; exact Layer I entry counts and ffmpeg-pinned clips), CRC verification, de-emphasis ([LIPS.md](LIPS.md), [AUDIO.md](AUDIO.md)) | None for shipped data: +32 is the playback length the game cuts each sound to (`SdtEntryHeader.PlaybackBytes`, tp.exe 0x6C82C0); the second codec class (type bit 0x10), Layer III, MPEG-2.5, free format and MPEG-2 Layer II joint stereo are unused | Untriggered events and the original mixer ([AUDIO.md](AUDIO.md)) |
| BF4 | All 33 fonts × six languages, three encodings ([UI.md](UI.md)) | — | Original screen positions, AA/gamma and spacing |
| LIP | `LipSyncFile` (read and write) and `LipSyncTimeline`: µs marks, talking from 0, toggling per mark, checked against all 639 English global clips ([LIPS.md](LIPS.md)) | — | Advisor animation/pose, a pausing clock |
| MD2 | 2,116/2,118 members (the other two, 207.201, and the two loose 24.23 arrows are refused by the original loader too); all 1,278 animation members: position/rotation/scale, quantised vertex groups, the 12-byte pose block, texture-coordinate and texture-frame tracks, node-flag toggles, node lists, path parameters (flag 0x200/0x400, `ModelPathParameters`) and the geometry models' paths (`ModelPath`, header 0x40/0xAC), which placed objects follow ([MD2-MODELS.md](MD2-MODELS.md)) | Texture flag bits 0x1/0x2 (no reader found) and the exact render state of 0x10/0x20; node bit 0x8000's setter (node bit 0x10 hides a mesh, the group-0 box culls it, heightfield bits give the diagonal, texture turn and mirror: MD2-MODELS.md) | Game-speed scaling of the scene clock; the terrain texture turn/mirror and per-mesh culling; PC equivalence and rendering fidelity |
| MAP | Five TP2M terrain grids, grid mapping, five cell bits (`MapCellFlags`), `MapFile.Encode`; the sound-catalogue `.map` files are decoded separately ([MAP.md](MAP.md)) | None: tp.exe's loader skips the five header words, never opens `terrain.map`, and copies bit 0x04 to a tile flag nothing reads (MAP.md) | Build rules: the original's tile set-up ignores `Blocked` (0x01), OpenTPW refuses it |
| RSE | All 308 scripts, all 84 used opcodes with operand counts; `RideVM` runs the corpus fault-free ([RSE-SCRIPTS.md](RSE-SCRIPTS.md), [RSE-VM.md](RSE-VM.md)) | The inferred operand meaning of the 51 hooked opcodes; slicing, time units and CRIT_LOCK against original-runtime evidence | The game systems behind the effect hooks |
| TPWS / TPWI | The 19 writer-order blocks, the World thing list, cell grid, placed objects, park turn and date, bank, staff and advisor state ([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md), [SAVE-CONTAINER.md](SAVE-CONTAINER.md)) | Guests, research, RNG state, the script/particle/sound blocks and the RSSE object records (SAVE-001–010); a second fixture | Writing original-format saves (OpenTPW writes [its own format](PARK-SAVES.md)) |
| ESP/FPC/TPC | `SpriteBankFile`, `SpriteAnimationFile`: all 121 `esprites.wad` members ([GUESTS.md](GUESTS.md)) | The bytes of ESP slots 16–19 and slot names (frame words = pixels per unit, 0x10C picks the FPC, 0x10D unused: GUESTS.md) | Heads, thoughts, staff, litter and particle sprites in the game |
| MTR | Topology and nine matrices of all 11 ISO-only files ([MTR.md](MTR.md)) | The scale `s`/`M7` offset; no reader of MTR files was found in either build (MTR.md) | — |
| TQI / TGQ | All nine movies; EA ADPCM audio bit-exact; video 84–95 % identical samples against FFmpeg ([TGQ-MOVIES.md](TGQ-MOVIES.md)) | Reproducing the located PC decoder (matrix builder 0x6710C0, transposed zigzag, half-even odd part, 7-bit output, colour tables) for bit-exact video (TGQ-MOVIES.md) | Game trigger points |
| FSH (Theme Park Inc) | All 7,283 `.fsh` files/WAD members to RGBA ([FSH.md](FSH.md)) | `0x24` palette transparency (COMPAT-014) | Comparison with the original game's rendering |
| WAD / Refpack | Directories with bounded ranges ([WAD-ROBUSTNESS.md](WAD-ROBUSTNESS.md)); Refpack decodes every compressed member exactly as the legacy decoder, malformed input included (`RefpackTests`) | None: the original allocates the declared size and checks nothing; it equals the decoded length in every shipped member (WAD-ROBUSTNESS.md) | — |

Read-only WAD member-name inventory: 312 DWFB archives, 2,118 `.MD2` members,
308 `.RSE`, 7 `.MAP` (5 terrain, 2 sound catalogs); no `.LIPS` names, but 639
`.LIP` members in `lips.wad`; MTR exists only on the ISO; no `.TQI` members
(movies are loose `.tgq` files).
Absence from these bounded locations does not prove an edition lacks embedded
data, differently named files or formats stored outside WADs.

Preserve efficient order: finish narrow verifiable slices, keep synthetic tests
public and original fixtures private, and reuse proven existing decoders/runtime
boundaries. No large renderer/VM rewrite, new decoder dependency or SDK upgrade
without a separate justified decision. A stricter Refpack or WAD size check would
change a tested contract, so it needs a caller that requires it and corpus evidence first.

Primary upstream source is accessible via the docs repository even when the old
website challenges browsers:
https://github.com/OpenTPW/opentpw-docs/tree/34f357fabc8a6aa064c76260c714dca8b875b148/src/formats
Its MAP page describes a sound map and is TODO; its TQI page identifies movies
as TQI video/EA audio in TGQ containers. Verify actual files instead of assuming
that extension names describe one uniform schema.
