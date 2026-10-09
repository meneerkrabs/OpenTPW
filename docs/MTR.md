# MTR: ISO-only mesh companion files

October 9, 2026. Status: strict reader; the table is **decoded as mesh topology that is
fully redundant with the paired banner `.MD2`** and the floats as nine 4×4 matrices
(two equal the MD2 node matrix), verified for all 11 files. MTR is not a material format,
adds no geometry the MD2 lacks and is not rendered. There is no evidence the installed
game loads it. No original data is in the repository.

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
| 36 | `T − 36` | u32 table (1,178–4,067 entries): corner/face topology, see below |
| T | 256 | ASCII name, NUL-padded (`s_bkrupt`, `s_congrats`, `s_paused`) |
| T+256 | 4 | u32 1 (always) |
| T+260 | 576 | 144 finite f32 values = nine row-major 4×4 matrices, see below |
| T+836 | 20 | five u32 footer words `F0..F4` |

## Decoded meaning (all 11 files, against the sibling ISO `.MD2`)

Each banner MD2 has one mesh named like the MTR (`s_bkrupt`, `s_congrats`, `s_paused`).
With `C` = its corner count (= `F0`) and `N` = its face count (= `F3`, the u16 at MD2
`0x3E`), the table has exactly `(C − 3) + C + 3N` entries:

| Part | Entries | Meaning (100 % match) |
| --- | --- | --- |
| A | `C − 3` | for corners 3…C−1, the first face that uses the corner (corners 0–2 are face 0) |
| B | `C` | that corner's slot in the face with corner order reversed (MD2 slots 0/1/2 → 0/2/1) |
| P | `3N` | per face, the three MD2 position indices in reversed order (0, 2, 1) |

Checked entry by entry: 6,332 A, 6,365 B and 12,453 P values (with the MD2's
corner→position table). Together with the reversed slot order this looks like an
exporter's source-mesh topology. Since MD2 corners are numbered in first-use order, all
of it can be derived from the MD2.

The 144 floats are nine row-major 4×4 matrices `M0`…`M8`: `M0 = M1` = the MD2 mesh node
matrix exactly (Y/Z-swap rotation plus a translation such as 999.735, −774.461, 0);
`M2 = M3` = `scale(s) · M0` with a uniform `s` (0.66596 bankrupt, 1 congrats, 0.8065 paused);
`M4 = M8` = identity; `M5 = M6` = `s·I`; `M7` = `s·I` plus a translation (e.g. 16.16, 0,
−72.65 for every bankrupt). What `s` and the `M7` offset are used for is not known
(display scale/pivot are plausible, not shown). `M0`/`M1`/`M4`/`M8` match exactly; the
scaled ones match to 1e-3 (they carry ~1e-15 rotation noise).

Footer: `F0` = corners, `F1` = 24, `F2` = 4·`F0` + 24, `F3` = faces, `F4` = 8·`F0` + 24
(= 2·`F2` − 24). These look like byte sizes of 4-byte-per-corner buffers plus a 24-byte
header; not shown.

Refuted/unneeded hypotheses: the table is not per-vertex morph data or vertex indices into a
second geometry. The floats are not 12 4×3 matrices or 48 vec3 keyframes: the 16-float
grouping gives exact affine matrices with `(0,0,0,1)` columns, and the 12-float
grouping does not. Rendering the bankrupt/congrats/paused messages is plain MD2 work:
the MD2 already holds positions, UVs and the `*_grad.tga` material. The
English MD2s are installed under `Data/Language/English`.

## Reader

`MtrFile` (`source/OpenTPW.Files/Public/MtrFile.cs`) exposes `Name`, `Table`,
`TrailerFloats` (144), `Matrices` (9) and `Footer` (5). `DecodeTopology()` returns the
A/B/P parts (rejects a table length that does not fit `F0`/`F3`, slots > 2 and
out-of-order/out-of-range first faces). It enforces exactly the observed structure.
Unknown magic, reserved words, an offset/length mismatch, a bad name slot or non-finite floats
raise `InvalidDataException`. Other values of the fixed 6/1/1/0, 1 and `F1 = 24` words
raise `NotSupportedException`. Input is capped at 1 MiB. Caller-owned streams stay open,
and short nonseekable reads work. The upstream "Material" label is not adopted in the API.

Tests (`MtrFileTests`): 27 synthetic tests (3 for topology/matrices). The private test `OriginalIsoMtrFilesMatchPinnedStructure`
(needs `OPENTPW_MTR_PATH` = directory of ISO-extracted `.mtr`) pins hash, name, table
count and sum, and footer, and checks both relations. `OriginalIsoMtrTopologyAndMatricesMatchPairedMd2`
checks every table entry and `M0`/`M1`/`M4`/`M8` against the sibling `.MD2`
(11/11 files). Two tests check the MD2 `0x3E` value against `OPENTPW_GAME_PATH`. 31/31 pass with both variables set;
otherwise 4 are inconclusive.

## Search method

- Upstream `opentpw-docs@34f357f` `src/formats/mtr.md`: title "Material" only, `TODO`.
- Loose `Data` walk (801 files): no `.mtr`; no other files named `*mtr*`/`*mat*` except fonts/WADs.
- An independent Python DWFB + RefPack scan of all 312 WADs (13,394 members incl. 2,123
  MD2): the magic `AF15592E` is not found anywhere in any member or loose file. No
  member or data file contains `.mtr`.
- `7z l TPWORLD.ISO` (2,989 entries) found the 11 files above. They were extracted only to /tmp.
- `strings tp.exe`: no `mtr`/`material` strings (packed launcher; `TP.ICD` not inspected).

## Remaining gates

Determine whether the shipped game ever opens MTR files (runtime file-access observation);
the installer does not copy them, which suggests exporter leftovers. Find out what `s`/`M7`
mean if a runtime use turns up. Banner rendering belongs to the MD2 renderer and needs no MTR data.
