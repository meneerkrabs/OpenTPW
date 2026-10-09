# LIPS: advisor lip-sync marks

October 9, 2026. Status: strict CPU reader for `.LIP` files implemented and checked
against every located original file; **advisor animation/synchronization not
implemented and mark semantics not verified**. No original data is in the repository.

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
Some files start at mark 0 (18 English files).

Observed scale, not verified: the last mark divided by 10^6 is ≤ the paired MP2
duration (bitrate × data size; 22,050 Hz MPEG-2 Layer II, mostly 48 kbps) for 642/643 English
files and every French/German/Swedish file. The ratio max is ≈0.998 (e.g. `sp_630`: last mark
18,514,104 vs 18.65 s audio). This is consistent with **microseconds from speech start**.
Each level `sp_001.LIP` is byte-identical to one global member: fantasy = `sp_473`,
hallow = `sp_476`, jungle = `sp_479`, space = `sp_478`. The only English overrun is
global `sp_478` (1.05× its global MP2). Its identical space copy fits the space-level
audio (0.9985×). That suggests these marks were authored for the level speech. Danish
`sp_127`/`sp_427` overrun by 3%/0.3%. An odd count plus 8-byte alignment suggests open/close toggles or
(start, end) intervals with an open-ended final interval. That is a **hypothesis**: there is no
runtime trace, no executable reference, and no upstream description.

## Reader

`LipSyncFile` (`source/OpenTPW.Files/Public/LipSyncFile.cs`) returns `Marks` as raw
`uint` values. It does not convert them to time. Input is capped at 64 KiB
(largest observed file: 160 bytes). It rejects lengths that are not positive
multiples of 8, a missing terminator, an early terminator and marks that do not
strictly increase. Caller-owned streams stay open, and short nonseekable reads work. WAD
access reuses the existing `WadArchive` (members are stored uncompressed).

Tests (`LipSyncFileTests`): 12 synthetic tests always run. Five private tests check the
pinned `lips.wad` hash, all 639 members (3,223 marks in total) and pinned values
(`sp_001` = 2,226,893 / 2,812,380 / 4,058,820; `z_z_ouch1` = 305,804) plus the four level files
(hash, count, first/last). These are inconclusive without `OPENTPW_GAME_PATH`. 17/17 pass with it.

## Search method

- Upstream `opentpw-docs@34f357f` `src/formats/lips.md`: title only, `TODO`.
- Loose `Data` walk (801 files) by extension and name (`*lip*`, `*phn*`, `*lsp*`).
- An independent Python DWFB + RefPack scan of all 312 WADs (13,394 members, 6,804
  RefPack; no decode errors). It checked content shape (u32 strictly increasing +
  `FFFFFFFF`, len % 8 == 0) and keywords `.lip`/`lips` in non-LIP members. No LIP-shaped data
  exists outside `.LIP` members, and no data file (SAM/RSE/TXT/…) names LIP files.
- `7z l` of `TPWORLD.ISO` (2,989 entries): 20 `.LIP` loose files and 5 `lips.WAD`.
- `strings tp.exe`: no `lip`/`phon`/`viseme` strings. The 285 KB launcher appears to be
  packed; `TP.ICD` was not inspected. The runtime load path and selection logic are
  still unknown.

## Remaining gates

The unit and meaning of marks (toggle vs. interval, mouth shape) need original runtime
observation. We also need to know how the game picks the global or level LIP for the same `sp_NNN`.
After that comes advisor mouth animation with audio-clock sync and a comparison against captures.
Do not claim advisor lip-sync until then.
