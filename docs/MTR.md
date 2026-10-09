# MTR: ISO-only mesh companion files

October 9, 2026. Status: strict **structural** reader implemented for all located files.
**Field meanings unknown.** There is no evidence that MTR is a material format or that the
installed game loads it. No original data is in the repository.

## Where the data is

No `.mtr` exists in the installed `Data` tree, and none exists as a WAD member. The only copies are
11 loose files on the retail `TPWORLD.ISO`, next to localized "banner" meshes:

| ISO path | Bytes | SHA-256 |
| --- | --- | --- |
| `English/Meshes/English/bankrupt.mtr` | 10,924 | `ecc7f7eedd85206041afa3ba51e1addae5e62ec1c206e035f64f4f2b4670a5b2` |
| `English/Meshes/English/congrats.mtr` | 17,160 | `6952ffca0e32cf2cf5738e31c47b63af4a3ce6deed4686bab2f612adc644738b` |
| `Danish/…/bankrupt.mtr` = `Swedish/…/bankrupt.mtr` | 10,076 | `6a27ae4733b6a760594db657239f92bef3e7b8c6f0b8b662094eceb0c18c3e7b` |
| `Danish/…/congrats.mtr` | 6,780 | `de0773f79772bf301d0277be31ed8c82707f0b2fc7c123587009e35094d638de` |
| `Danish/…/paused.mtr` = `German/…/paused.mtr` | 6,596 | `17ea1d42ff22f87ed63ece9cc84be6f866d78dcfa1a398acce4823fc6b9d48c1` |
| `German/…/bankrupt.mtr` | 5,604 | `7281eae25edbb8bb2b4c21528a14cb3ee3772cb40189fb0890ac1eefbaf7df2a` |
| `German/…/congrats.mtr` | 14,108 | `93fbf87cf301df996fdbe7c9816b6308e18c62eb58089f81340f465b403db896` |
| `Swedish/…/congrats.mtr` | 14,676 | `715608a5046c11c767763f489444f3a9c310041f2c5b13233ff3c7c67f8503c3` |
| `Swedish/…/paused.mtr` | 7,816 | `7273e32dbe6b8c4d8f99faddb92bfedfa04fda72f9f2edacb307427c47dccd30` |

That is 9 distinct files. French has no MTR. English has no `paused.mtr`, although `paused.MD2` exists.
The English ISO `Meshes/English/*.MD2` files are byte-identical to the installed
`Data/Language/English/*.MD2`. The installer appears to copy the MD2 files but not the MTR files.

## Layout (observed in all 11)

| Offset | Size | Observation |
| --- | --- | --- |
| 0 | 4 | magic `AF 15 59 2E` |
| 4 | 16 | u32 6, 1, 1, 0 (always) |
| 20 | 4 | u32 `T`: trailer offset; `T + 856 == file length` |
| 24 | 12 | zero |
| 36 | `T − 36` | u32 table (1,178–4,067 entries) |
| T | 256 | ASCII name, NUL-padded (`s_bkrupt`, `s_congrats`, `s_paused`) |
| T+256 | 4 | u32 1 (always) |
| T+260 | 576 | 144 finite f32 values (patterns resemble 3×3/4×4 transforms: 1.0 diagonals, repeated ±0.66596) — not interpreted |
| T+836 | 20 | five u32 footer words `F0..F4` |

Relations that hold in all 11 files but have **no assigned meaning**:
- `F1 == 24`; `F4 == 2·F2 − 24`.
- Every table value is `< F3`, and `F3` equals the u16 at `0x3E` of the same-language,
  same-name `.MD2` (English bankrupt 411, congrats 682; Danish 383/227/237; German
  185/547/237; Swedish 383/569/290). The table therefore appears to index MD2
  per-element data.
- The table starts non-decreasing (`1, 2, 2, 2, 3, …`). Its first drop is at index `F0 − 3`.

## Reader

`MtrFile` (`source/OpenTPW.Files/Public/MtrFile.cs`) exposes `Name`, `Table`,
`TrailerFloats` (144) and `Footer` (5). It enforces exactly the observed structure.
Unknown magic, reserved words, an offset/length mismatch, a bad name slot or non-finite floats
raise `InvalidDataException`. Other values of the fixed 6/1/1/0, 1 and `F1 = 24` words
raise `NotSupportedException`. Input is capped at 1 MiB. Caller-owned streams stay open,
and short nonseekable reads work. The upstream "Material" label is not adopted in the API.

Tests (`MtrFileTests`): 24 synthetic tests. The private test `OriginalIsoMtrFilesMatchPinnedStructure`
(needs `OPENTPW_MTR_PATH` = directory of ISO-extracted `.mtr`) pins hash, name, table
count and sum, and footer, and checks both relations. Two tests check the MD2 `0x3E` value against `OPENTPW_GAME_PATH`. 27/27 pass with both variables set;
otherwise 3 are inconclusive.

## Search method

- Upstream `opentpw-docs@34f357f` `src/formats/mtr.md`: title "Material" only, `TODO`.
- Loose `Data` walk (801 files): no `.mtr`; no other files named `*mtr*`/`*mat*` except fonts/WADs.
- An independent Python DWFB + RefPack scan of all 312 WADs (13,394 members incl. 2,123
  MD2): the magic `AF15592E` is not found anywhere in any member or loose file. No
  member or data file contains `.mtr`.
- `7z l TPWORLD.ISO` (2,989 entries) found the 11 files above. They were extracted only to /tmp.
- `strings tp.exe`: no `mtr`/`material` strings (packed launcher; `TP.ICD` not inspected).

## Remaining gates

Determine whether the shipped game ever opens MTR files (runtime file-access observation).
Then work out the table/float/footer semantics against the paired MD2 geometry. Do not wire MTR into
rendering or call it material support before that. The installed `Data` tree contains only the MD2 files for these banners.
