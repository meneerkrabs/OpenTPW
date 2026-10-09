# Format implementation priorities

User-directed focus on missing/partial formats, October 9, 2026. Parsing, decoded
content, engine integration, platform execution and original fidelity are separate
gates. Do not promote a container reader to a playable game feature.

| Format | Verified starting point / delivered slice | Next gate |
| --- | --- | --- |
| BF4 | Bounded CPU decoder: all 33 English fonts / 8,217 entries, three encodings and pinned samples | GPU text/atlas integration, actual string spacing/AA and locale/platform fidelity |
| MAP | `MapFile`: TP2M header/`MAP `/`END ` chunks, 128×128 raw cells, 5 pinned terrain fixtures ([MAP.md](MAP.md)); 62 loose + 2 WAD sound-catalog `.map` files distinguished and rejected | Cell semantics, the five opaque header values, `base.map` vs `terrain.map`, grid-to-world mapping; sound-catalog schema separately |
| MD2 | Bounded 221.203 reader: 2,116/2,118 corpus members parse (838 geometry with verified mesh/texture/hierarchy layout, 1,278 animation containers with opaque payload); 2 version 207.201 members rejected; 4 pinned golden models ([MD2-MODELS.md](MD2-MODELS.md)) | Animation tracks, matrix space, texture/material flag bits, stored normals in the renderer, 207.201, terrain 0x6C block; untextured `Spa_isle` material lookup in the lobby renderer |
| RSE | `RideScriptFile` + static analysis: all 308 scripts parse, 84 opcodes used, none unknown, operand counts match upstream; 4 pinned scripts ([RSE-SCRIPTS.md](RSE-SCRIPTS.md)). `RideVM` executes them: 33 opcodes implemented, 51 hooked (4,065 of 11,915 instructions route game effects to an unimplemented-effect hook); corpus runs fault-free; Totem trace pinned ([RSE-VM.md](RSE-VM.md)) | Confirm slicing/time units/CRIT_LOCK and inferred operand semantics against original-runtime evidence; implement the hooked game systems (visitors, animation playback, sound/EVENT mapping, objects, TOUR/BUMP/COAST, park clock) |
| TPWS/TPWI | Bounded BILZ reader; `SavePayloadLayout` locates 17 unique section markers in the only fixture (jungle `Easymode.TPWI`, also the only one on the ISO); contents opaque ([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md)) | A second fixture (actual TPWS/INTS/LAYS), per-section and prefix schema with known-state saves, then a read-only importer |
| MTR | Only 11 ISO-only `Meshes/<lang>/*.mtr` files (9 distinct), magic `AF15592E`; structural `MtrFile` ([MTR.md](MTR.md)); fixtures via `OPENTPW_MTR_PATH` | Establish whether the game opens MTR at all; relate table/float data to the paired MD2 before any rendering use |
| LIPS | `.LIP` (not `.LIPS`): 639 members in `global/Speech/lips.wad` + 4 level files, strictly increasing u32 marks ending `FFFFFFFF`; `LipSyncFile` ([LIPS.md](LIPS.md)) | Confirm units (likely µs) and toggle meaning, then audio-synced advisor mouth animation |
| TQI/TGQ | `TgqMovieFile`: all nine movies parse to EOF; EA ADPCM audio bit-exact vs external reference; TQI video decodes every frame (≈55 dB, not bit-exact) ([TGQ-MOVIES.md](TGQ-MOVIES.md)) | Original IDCT/colour fidelity, playback clock/A-V sync, GPU presentation, game trigger points |

Read-only WAD member-name inventory: 312 DWFB archives, 2,118 `.MD2` members,
308 `.RSE`, 7 `.MAP` (5 terrain, 2 sound catalogs); no `.LIPS` names, but 639
`.LIP` members in `lips.wad`; MTR exists only on the ISO; no `.TQI` members
(movies are loose `.tgq` files).
Absence from these bounded locations does not prove an edition lacks embedded
data, differently named files or formats stored outside WADs.

Selected terrain paths: `levels/jungle/terrain.wad` contains `base.map` and
`terrain.map`; their directory metadata declares 16,464 decoded bytes each.
Their TP2M chunk structure is now verified; cell semantics are not (see MAP.md).
`levels/hallow/terrain.wad/base.map` has the same declared decoded size.

Preserve efficient order: finish narrow verifiable slices, keep synthetic tests
public and original fixtures private, and reuse proven existing decoders/runtime
boundaries. No large renderer/VM rewrite, new decoder dependency or SDK upgrade
without a separate justified decision. LIP and MTR fixtures are now located;
their readers are structural only and make no gameplay or rendering claims.

Primary upstream source is accessible via the docs repository even when the old
website challenges browsers:
https://github.com/OpenTPW/opentpw-docs/tree/34f357fabc8a6aa064c76260c714dca8b875b148/src/formats
Its MAP page describes a sound map and is TODO; its TQI page identifies movies
as TQI video/EA audio in TGQ containers. Verify actual files instead of assuming
that extension names describe one uniform schema.
