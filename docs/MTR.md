# MTR: mesh companion files on the install media

October 9, 2026. Status: strict reader; the table is **decoded as mesh topology that is
fully redundant with the paired banner `.MD2`** and the floats as nine 4×4 matrices
(two equal the MD2 node matrix), verified for all 11 files. MTR is not a material format,
adds no geometry the MD2 lacks and is not rendered. Static searches found no evidence the
game opens it, so it is treated as unused unless contrary evidence appears. No original data
is in the repository.

## Where the data is

No `.mtr` exists in the installed `Data` tree, and none exists as a WAD member. The copies are
11 loose files on the PC install CD (`<Lang>/Meshes/<Lang>/` at the disc root),
and the same 11 files are on the retail `TPWORLD.ISO`, next to localized "banner" meshes.
The paths below are relative to the disc root. The media copies were re-hashed
for this table (11 of 11 files, 9 distinct SHA-256 values, all match); the ISO listing was
not re-run in this pass:

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
`Data/Language/English/*.MD2`. The installed `Data` tree therefore holds the MD2 banners but no
MTR files, while the install media carries both. This is an absence from the installed tree,
not from the media, so it does not by itself show the MTR files are exporter leftovers.

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
`M2 = M3` = `scale(s) · M0` with a uniform `s` (0.66596 bankrupt, 1 congrats, 0.8065 paused; the
translation row is not scaled); `M4 = M8` = identity; `M5 = M6` = `s·I`; `M7` = `s·I` plus a translation (e.g. 16.16, 0,
−72.65 for every English/Danish/Swedish bankrupt). `M0`/`M1`/`M4`/`M8` match exactly; the
scaled ones match to 1e-3 (they carry ~1e-15 rotation noise). What the data proves about `s` and `M7`
is in "Scale and M7" below.

Footer: `F0` = corners, `F1` = 24, `F2` = 4·`F0` + 24, `F3` = faces, `F4` = 8·`F0` + 24
(= 2·`F2` − 24). These look like byte sizes of 4-byte-per-corner buffers plus a 24-byte
header; not shown.

Refuted/unneeded hypotheses: the table is not per-vertex morph data or vertex indices into a
second geometry. The floats are not 12 4×3 matrices or 48 vec3 keyframes: the 16-float
grouping gives exact affine matrices with `(0,0,0,1)` columns, and the 12-float
grouping does not. Rendering the bankrupt/congrats/paused messages is plain MD2 work:
the MD2 already holds positions, UVs and the `*_grad.tga` material. The
English MD2s are installed under `Data/Language/English`.

## Scale and M7 (October 17, 2026, static, 11 files against their paired MD2)

The 3×3 part of `M0` is the exporter's Y/Z swap (row vector `(x, y, z)` becomes `(x, z, −y)`), so `M0` takes mesh-local
vertices to the banner's world position. The MD2 header bounds (0x80) equal the local vertex bounds transformed by `M0` (checked for
all 11), and the banners are screen-sized slabs: bankrupt and paused span exactly 1,700 × 500 and 1,700 × 400 units
(x 174–1,874, y −1,000…−500 and −900…−500), congrats about 1,843 wide.

- **`s` is already baked into the MD2 vertices.** The mesh-local thickness of every bankrupt and paused slab is exactly `30·s`
  (19.979 = 30 × 0.66596 and 24.194 = 30 × 0.80645, all 4 + 3 files that have a thickness check), and `M0` is the *unscaled* node
  matrix. So `M2 = s·M0` is the node as it stood before the scale was baked into the vertices, and `M5 = M6 = s·I` the baked scale itself.
  The unscaled authoring depth was therefore 30 (congrats has `s = 1`: depth 30 in Danish, 30.75 in English, German and Swedish). `s` is
  per banner design, identical in every language (bankrupt 0.66596, paused 0.80645, congrats 1); why those values, e.g. 1/1.24 for
  paused, stays unknown.
- **`M7` is not a function of the shipped MD2.** Its translation divided by `s` is the mesh-local X centre of the vertices
  only in the 4 files whose `M0` translation X is the canonical 999.735 (Danish, English and Swedish bankrupt, Swedish paused; 24.2657);
  in the other 7 it is not (German bankrupt, all congrats, Danish and German paused). Its Y component equals `M0`'s Z (−15 congrats,
  −12.097 paused) in 5 files and is 0 in the rest, German bankrupt included (where `M0` Z is 19.979). The Z component (e.g. −109.086 for the
  Swedish files, −131.527 English and German congrats, −149.538 Danish congrats, −201.4 Danish and German paused, all divided by `s`)
  matches no bounds, centre or `M0` value. The same numbers repeat across languages for unrelated meshes (24.2657 for the Swedish congrats
  as well), so `M7` looks like a stale pivot/object-offset from an earlier state of the artist's scene that was not
  regenerated when the localised MD2s were edited. That reading is an inference from the repeated values, not shown.
- **No placement code exists to confirm any of it.** tp.exe never names the banners (below), so neither the scale nor `M7` has a reader to tie it to.

The relations above (depth, X centre) are pinned by the private test `OriginalIsoMtrScaleIsBakedIntoPairedMd2Geometry`.

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
(needs `OPENTPW_MTR_PATH` = directory of `.mtr` files, ISO-extracted or a copy of the PC CD, read in place) pins hash, name, table
count and sum, and footer, and checks both relations. `OriginalMediaCarriesElevenMtrCopiesOfNineDistinctFiles`
(same variable) requires exactly 11 `.mtr` files whose SHA-256 set is the 9 pinned values.
`OriginalIsoMtrTopologyAndMatricesMatchPairedMd2`
checks every table entry and `M0`/`M1`/`M4`/`M8` against the sibling `.MD2`
(11/11 files). Two tests check the MD2 `0x3E` value against `OPENTPW_GAME_PATH`.
Run in this pass with `OPENTPW_MTR_PATH` set to a copy of the PC CD: 30 pass, 2 inconclusive
(`OPENTPW_GAME_PATH` unset; no installed `Data` tree was readable here). With no variables: 27 pass,
5 inconclusive. The `OPENTPW_GAME_PATH` pair was last run at 31/31 with both variables set, before the new test was added.

## Search method

- Upstream `opentpw-docs@34f357f` `src/formats/mtr.md`: title "Material" only, `TODO`.
- Loose `Data` walk (801 files): no `.mtr`; no other files named `*mtr*`/`*mat*` except fonts/WADs.
- An independent Python DWFB + RefPack scan of all 312 WADs (13,394 members incl. 2,123
  MD2): the magic `AF15592E` is not found anywhere in any member or loose file. No
  member or data file contains `.mtr`.
- `7z l TPWORLD.ISO` (2,989 entries) found the 11 files above. They were extracted only to /tmp.
- `strings tp.exe`: no `mtr`/`material` strings (packed launcher).

### Does the game open MTR? Static negative evidence

Re-run on the read-only inputs (no binary was executed; the byte counts are raw, not decoded):

```
grep -aic '\.mtr' SimThemePark.data libraries/*.data TP.ICD      # case-insensitive literal
python3 -I -c "import sys;d=open(sys.argv[1],'rb').read();print(d.lower().count(b'.mtr'),d.count(bytes.fromhex('af15592e')),d.count(bytes.fromhex('2e5915af')))" FILE
```

The second form counts `.mtr` (case-insensitive), the MTR magic in little-endian (`AF 15 59 2E`)
and in byte-reversed order (`2E 59 15 AF`).

| Input | Identity (SHA-256, prefix) | `.mtr` | magic LE / BE |
| --- | --- | --- | --- |
| Mac `SimThemePark.data` | `04809cd4ccee5433…` | 0 | 0 / 0 |
| Mac `libraries/*.data` (15 files) | identities in `docs/reverse/FINDINGS.md` | 0 each | 0 / 0 each |
| PC `TP.ICD` | `8565135229aeed90…` (3,693,101 bytes) | 0 | 0 / 0 |
| Theme Park Inc `Game.exe` (a different, later game; context only) | `77ea0c41410aad7c…` | 0 | 0 / 0 |

Scope and limits. The Mac set is 16 PowerPC PEF containers. The PC `TP.ICD` is weak evidence:
its code and `.data` are encrypted, so only the plain `.rdata`/`.rsrc` sections can contain a literal.
A literal search misses names built at runtime from a non-literal table, concatenated strings, or
values stored compressed or RefPack-wrapped. The WAD scan above covers WAD members only.
The Mac file-extension list around `SimThemePark.data` offset ≈2,006,300 (`.fpc .ffn … .TPWS .TPWI .LAYS *.md2 *.ESP`)
has no `.mtr` (checked in an earlier pass, not re-run here).

**Conclusion:** no evidence of use; MTR is treated as unused unless contrary evidence appears.

**Decrypted PC executable (October 16, 2026).** The decrypted `tp.exe` (SHA-256 `5472f97b…`, code and data
readable) has 0 occurrences of `.mtr` (ASCII or UTF-16) and 0 of the magic in either byte order, including as a
code immediate; its engine extension table (`.txt .md2 .tps .tpc .fps .fpc .ffn .lan .tgq .tga .adt .wad .emt
.map .sdt .TPWS .TPWI .LAYS .INTS`) and WAD scan filters (`*.md2 *.ESP *.tga *.emt`) have no `.mtr`. This
closes the encrypted-code caveat. No consumer of MTR files was found in either build; a name built at
runtime from non-literal parts remains the only, unsupported, way one could be read.

**Every allowed binary (October 17, 2026).** The same raw byte counts (`.mtr` ASCII/UTF-16, the magic in both orders; also
`mtr` anywhere) over the rest of the shipped binaries, all 0 for `.mtr` and the magic:

| Input | Identity (SHA-256, prefix) | Result |
| --- | --- | --- |
| decrypted `tp.exe` (REA) | `5472f97b5eb2…` | the 3 bare `mtr` hits are a stack-push byte pattern and two `mTrackRideHandle`-style member names |
| PC `TP.exe` launcher, `Ip.exe`, `Setup.exe`, `Autorun.exe`, `clokspl.exe` | `4c4c1bd6beee…`, `b0fc84022365…`, … | nothing |
| `QMixer.dll`, `usp10.dll`, the 7 `wea*r.dll` online resources | `66ca1b8ee084…`, `58863df88dbe…`, … | nothing; no `.md2`, `.tga`, `.wad`, `.sam` file-type literal either, so they hold no data loader (audio mixer, Uniscribe, online front ends) |
| SafeDisc/launcher DLLs `clcd16/32`, `drvmgt`, `dplayerx` | | byte-scanned only, nothing (not analysed) |
| no-CD Theme Park Inc `Game.exe` | `95a2f86a174f…` | nothing, but it has no readable `.md2`/`.tga` literals at all: data is apparently encrypted, so weak |
| Mac `SimThemePark.data` + 15 `libraries/*.data` (PEF) | `04809cd4ccee…` | `mtr` only inside `mTrackRideHandle`-style names; no `.mtr`, no magic |

REA has no PowerPC support, so the PEF containers were searched by bytes only. The DLLs were not opened in REA: with no file-type
literal and no mesh vocabulary a disassembly could only look for a computed name, which the same limitation as for tp.exe covers.

**The banners are not named anywhere either.** tp.exe has no `bankrupt`, `congrats`, `paused`, `s_bkrupt`, `s_congrats` or `s_paused`
string (ASCII or UTF-16, also as 4-byte immediates `bank`/`paus`/`cong`/`s_bk`/`s_pa`/`s_co`); the only near hits are the unrelated advisor events
`Bankrupted`/`Congrat*`. The same holds for `residx.dat` (53 names: `.str`, `.bf4`, the code-page `.dat` files, no `.md2`), every `.str` and
`Advisor.sam` (only the `Bankrupted` event). tp.exe's UI-mesh loader (`0x4891D0`, "Loaded %d UI meshes" at `0x489E54`) only picks
nodes named `!cbut`, `!frame`, `!slider`, `!f_plain` or a non-`!` generic UI node (`0x489060`, skipping `w_small_shadow.md2`) from the UI mesh
set it enumerates, with no language-folder name in it. So no code was found that opens `bankrupt/congrats/paused.MD2` or places the banner;
the MD2 files' use (and therefore any use of the MTR sidecars made for them) is unproven, not merely unread.

## Remaining gates

- Whether the game ever opens MTR at all: no consumer found in any allowed binary (tp.exe, the TPI Game.exe, the Mac PEFs and every
  shipped DLL; the TPI and PEF results are weak). A name built at runtime remains the only way one could still be read.
- Why `s` is 0.66596/0.80645/1 and what `M7` is: no placement code exists to read. The data shows `s` baked into the MD2 and `M7` unrelated to it (above).
- No further work is planned.

Banner rendering belongs to the MD2 renderer and needs no MTR data.
