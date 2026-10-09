# Format implementation priorities

User-directed focus on missing/partial formats, October 9, 2026. Parsing, decoded
content, engine integration, platform execution and original fidelity are separate
gates. Do not promote a container reader to a playable game feature.

| Format | Verified starting point / delivered slice | Next gate |
| --- | --- | --- |
| BF4 | New bounded CPU decoder: all 33 English fonts / 8,217 entries, three encodings and pinned samples | GPU text/atlas integration, actual string spacing/AA and locale/platform fidelity |
| MAP | Empty MapFile; metadata inventory locates 7 MAP members in terrain WADs | Extract named terrain fixtures, establish record schema with tests; do not confuse terrain data with loose sound-catalog MAPs |
| MD2 | Original Totem buffers and material texture lookup work in sandbox; broad semantics unverified | Inventory model variants/hierarchy/animation/materials and exercise bounded malformed records and selected golden models |
| RSE | 308 WAD members identified, original VM incomplete | Opcode/call/event inventory, golden scripts and original runtime traces; no invented ride logic |
| TPWS/TPWI | Bounded offline BILZ reader and hashed Easymode TPWI; payload opaque | Actual original TPWS fixture, decoded payload schema and known-state read-only importer |
| MTR | No standalone MTR fixture found in loose Data files or the inspected WAD member names; upstream doc is TODO | Locate actual material representation/fixture before designing a parser; don't treat current model materials as proof of MTR support |
| LIPS | No standalone LIPS fixture found in the same inventory; upstream doc is TODO | Locate packed/embedded lipsync data and document timelines/phonemes before implementing advisor synchronization |
| TQI/TGQ | Nine loose `Data/Movies/*.tgq` videos found; no decoder | Verify chunk/container identities, then TQI video and EA audio decoder/playback/reference tests; container listing is not video support |

Read-only WAD member-name inventory: 312 DWFB archives, 2,118 `.MD2` members,
308 `.RSE`, 7 `.MAP`; no `.MTR`, `.LIPS` or `.TQI` member names observed.
Absence from these bounded locations does not prove an edition lacks embedded
data, differently named files or formats stored outside WADs.

Selected terrain paths: `levels/jungle/terrain.wad` contains `base.map` and
`terrain.map`; their directory metadata declares 16,464 decoded bytes each.
Compression/data semantics are not verified by that directory observation.
`levels/hallow/terrain.wad/base.map` has the same declared decoded size.

Preserve efficient order: finish narrow verifiable slices, keep synthetic tests
public and original fixtures private, and reuse proven existing decoders/runtime
boundaries. No large renderer/VM rewrite, new decoder dependency or SDK upgrade
without a separate justified decision. Continue MAP/MD2/RSE/save semantics while
deferring claims for unlocated LIPS/MTR fixtures, not silently dropping them.

Primary upstream source is accessible via the docs repository even when the old
website challenges browsers:
https://github.com/OpenTPW/opentpw-docs/tree/34f357fabc8a6aa064c76260c714dca8b875b148/src/formats
Its MAP page describes a sound map and is TODO; its TQI page identifies movies
as TQI video/EA audio in TGQ containers. Verify actual files instead of assuming
that extension names describe one uniform schema.
