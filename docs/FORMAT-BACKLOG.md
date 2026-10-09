# Format implementation priorities

User-directed focus on missing/partial formats, October 9, 2026. Parsing, decoded
content, engine integration, platform execution and original fidelity are separate
gates. Do not promote a container reader to a playable game feature.

| Format | Verified starting point / delivered slice | Next gate |
| --- | --- | --- |
| BF4 | Bounded CPU decoder: all 33 English fonts / 8,217 entries, three encodings and pinned samples; deterministic atlas, metric layout and GPU text panel with Metal readback; original-style front end, options and HUD draw UITEXT/UIHELPTEXT in all six languages with SMALL/MED/BIG tiers, readback-verified ([UI.md](UI.md)) | Original screen positions, AA/gamma and spacing versus original captures, D3D11/Vulkan runs |
| MAP | `MapFile`: TP2M header/`MAP `/`END ` chunks, 128×128 raw cells, 5 pinned terrain fixtures; grid-to-world mapping and five cell bits (`MapCellFlags`: blocked, water, initial path, entrance area, fixed walkway) verified against MD2 geometry/heightfield in four themes, `Standard.sam` fixed items and the Easymode save ([MAP.md](MAP.md)); 62 loose + 2 WAD sound-catalog `.map` files distinguished and rejected | Bit 0x04, the five opaque header values, purpose of jungle `terrain.map`, original build rules; sound-catalog schema separately |
| MD2 | Bounded 221.203 reader: 2,116/2,118 corpus members parse (838 geometry with verified mesh/texture/hierarchy layout, 1,278 animation members with decoded position/rotation/scale tracks); parent-relative node matrices (UI models: 2048×1536 screen space, children are state frames, [UI.md](UI.md)); sandbox Totem plays `totemm1.MD2`; 2 version 207.201 members rejected; pinned golden models and Totem samples ([MD2-MODELS.md](MD2-MODELS.md)) | Animation tick rate, 4,082 records with undecoded payload kinds (vertex animation, per-frame scalars, events), node list, rotation flag bits, texture/material flag bits, 207.201, terrain heightfield cell-word low bits (0x6C block otherwise decoded) |
| ESP/FPC/TPC (sprites) | `SpriteBankFile` (255-entry BGRA palette, signed-RLE rows, hotspot origins) and `SpriteAnimationFile` (20 direction-major slots): all 121 `esprites.wad` members decode; the 8 kid sets draw guests ([GUESTS.md](GUESTS.md)) | Meaning of the two u16 frame words and ESP flag bytes, FPC vs TPC selection, slot names; heads, thoughts, staff, litter, particles in game |
| RSE | `RideScriptFile` + static analysis: all 308 scripts parse, 84 opcodes used, none unknown, operand counts match upstream; 4 pinned scripts ([RSE-SCRIPTS.md](RSE-SCRIPTS.md)). `RideVM` executes them: 33 opcodes implemented, 51 hooked (4,065 of 11,915 instructions route game effects to an unimplemented-effect hook); corpus runs fault-free; Totem trace pinned ([RSE-VM.md](RSE-VM.md)) | Confirm slicing/time units/CRIT_LOCK and inferred operand semantics against original-runtime evidence; implement the hooked game systems (visitor opcodes now run against real guests for the Totem through `RideVisitorBridge`, [GUESTS.md](GUESTS.md); TOUR/BUMP/COAST guest handling and WALK*FLOAT remain; animation playback beyond the Totem's ANIM_Main, sound/EVENT mapping, objects, TOUR/BUMP/COAST, park clock) |
| TPWS/TPWI | Bounded BILZ reader; 17 unique section markers; per-cell grid in the prefix (MAP byte, path, connections, occupancy) and SYSG placed-object records decoded for the only fixture (jungle `Easymode.TPWI`); read-only `OriginalParkImport` of paths and objects ([TPWS-PAYLOAD.md](TPWS-PAYLOAD.md)); loan-offer and challenge tables located and cross-checked with the easy balance ([ECONOMY.md](ECONOMY.md)) | A second fixture (actual TPWS/INTS/LAYS); header/tail, remaining record fields, other sections; money/time/guest state with known-state saves |
| MTR | Only 11 ISO-only `Meshes/<lang>/*.mtr` files (9 distinct), magic `AF15592E`; structural `MtrFile` ([MTR.md](MTR.md)); fixtures via `OPENTPW_MTR_PATH` | Establish whether the game opens MTR at all; relate table/float data to the paired MD2 before any rendering use |
| LIPS | `.LIP` (not `.LIPS`): 639 members in `global/Speech/lips.wad` + 4 level files, strictly increasing u32 marks ending `FFFFFFFF`; `LipSyncFile` ([LIPS.md](LIPS.md)) | Confirm units (likely µs) and toggle meaning, then audio-synced advisor mouth animation |
| TQI/TGQ | `TgqMovieFile`: all nine movies parse to EOF; EA ADPCM audio bit-exact vs external reference; TQI video decodes every frame (integer IDCT, 56–61 dB, not bit-exact); `--play-movie` streaming playback with audio clock, drop/hold policy and GPU presentation ([TGQ-MOVIES.md](TGQ-MOVIES.md)) | Bit-exact IDCT, original colour/pixel aspect, end-of-movie behaviour, game trigger points |

Read-only WAD member-name inventory: 312 DWFB archives, 2,118 `.MD2` members,
308 `.RSE`, 7 `.MAP` (5 terrain, 2 sound catalogs); no `.LIPS` names, but 639
`.LIP` members in `lips.wad`; MTR exists only on the ISO; no `.TQI` members
(movies are loose `.tgq` files).
Absence from these bounded locations does not prove an edition lacks embedded
data, differently named files or formats stored outside WADs.

Selected terrain paths: `levels/jungle/terrain.wad` contains `base.map` and
`terrain.map`; their directory metadata declares 16,464 decoded bytes each.
Their TP2M chunk structure and five cell bits are verified (see MAP.md).
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
