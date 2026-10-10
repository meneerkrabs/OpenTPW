# Review TPI-FSH-V: Theme Park Inc SHPI reader (commit 425169c)

October 10, 2026. Independent review of `425169c` ("Decode Theme Park Inc's SHPI
textures…", parent `6c58be0`), merged for testing onto fork main `f51e874`. The
merge is clean. Scope: `FshFile`, `--inspect-fsh`, `OPENTPW_TPI_PATH`,
docs/FSH.md, COMPAT-014/015 and the new tests. This review says nothing about how
Theme Park Inc (TPI) draws these textures. It makes no claim that TPI is playable
or that decoded colours match the original game's output.

Corpus: TPI retail `Data` extracted from the Dutch/Benelux CD's cabinets, read in
place. Nothing from the CD was executed. The CD's third-party no-CD folder was
neither opened nor used.

## Merge blockers

None.

## Verdicts

| Check | Verdict |
| --- | --- |
| Independent decoder vs `FshFile`, RGBA SHA-256 | **Confirmed**: 7,283 of 7,283 match, 0 mismatches (every file, so above the 200-file sample asked for) |
| Corpus counts in the commit, README, FSH.md, THEME-PARK-INC.md, FORMAT-BACKLOG.md | **Confirmed**, re-derived with this review's own DWFB walker and RefPack decoder |
| RefPack padding to 16 enforced only where the data shows it | **Confirmed** |
| Bounds, caps, truncation, non-seekable input, overflow | **Confirmed**: no unexpected exception type in about 204k mutated or truncated inputs |
| Rejection of variants not in the corpus | **Confirmed**, with the documented lenient points below |
| Bounded claims; COMPAT-014/015 wording | **Confirmed**, with two wording nits (N1, N2) |
| Build, tests, register, evidence runner, CRLF, git objects | **Confirmed** (see Build and tests) |

## 1. Independent decoder

`tools/ppc-analysis/lanes/review/test_tpi_fsh_v1.py` contains a minimal DWFB
directory walker, a RefPack decoder and an SHPI decoder, all written for this
review. Unlike `FshFile`, this decoder is permissive: it accepts any record order
and any attached record, and it does not enforce the padding rule. If an
assumption in `FshFile` were wrong, it would therefore show up here as a count
or hash difference instead of being hidden. FshFile's output came from a scratch
console harness outside the repository. The harness walked `WadArchive` and
wrote one JSON line per file with the RGBA SHA-256 of each image.
`OPENTPW_FSH_DUMP=<that file>` makes the test compare the two, file by file.

- 7,283 files compared (162 loose, 7,121 WAD members), including every palette
  code (0x24 4,308, 0x2A 2,974, 0x2D 1), both image codes (raw 547, RefPack
  6,736) and all 135 padded images. Result: **0 mismatches**. The two decoders
  produced identical sets of member keys.
- To show the comparison is not vacuous, one hash in the dump was corrupted; the
  test then failed and named that file (`global/Advisor/textures/Fastfood_Badge.fsh`).
- The five pinned fixture hashes in FSH.md are reproduced by this decoder alone,
  without the dump.
- `--inspect-fsh` (Release build) printed the pinned hashes for `icewall1` (WAD,
  0x2D), `tb_camera` (WAD, padded; looked up as `STEXTURE\tb_camera.fsh` to
  check case and separator handling) and `alphkid` (loose). A missing member,
  a rejected file and a missing argument all exit with code 1.

## 2. Corpus counts (re-derived)

All of the following match the commit. They are asserted in the
`Corpus` tests, which need `OPENTPW_TPI_PATH`.

| Fact | Count |
| --- | --- |
| `.fsh` files and members | 7,283 = 162 loose + 7,121 in 303 WADs |
| SHPI members not named `.fsh`, or the reverse | 0 |
| Header id `G231`, one image; header size equals the length | 7,283 |
| Image code 0x7B / 0xFB | 547 / 6,736 |
| Palette 0x24 (all 256 entries) / 0x2A (414 with 256, 2,560 with fewer) / 0x2D | 4,308 / 2,974 / 1 (`snowtrac.wad!stexture/icewall1.fsh`) |
| Palette height field | 1 everywhere |
| Name 0x70 present | 6,649 |
| Attached record orders | (0x24, 0x70) 3,824; (0x2A, 0x70) 2,824; (0x24) 484; (0x2A) 150; (0x2D, 0x70) 1. No other code |
| Four position words zero | 7,283 |
| Highest index below the palette's entry count | 7,283 |
| Image at offset 112 after the `Buy ERTS` gap | 7,283 |
| Tag equal to the first four characters of the file name | 7,101 |
| Distinct sizes; most common; largest | 26; 32×32 3,001, 128×128 2,009, 64×64 1,840; 384×344 and 250×250 |
| Loose `.fsh` with a TGA thumbnail of the same name | 70 (0x2A/32-bit 23, 0x24/24-bit 43, 0x24/32-bit 4) |
| 0x2D entries with bit 15 set | 256 of 256 |

Not re-derived: the thumbnail and duplicate-texture error figures (MAE table,
"12 pairs") and the "36 of 162 loose files with non-zero alignment bytes". These
are covered only by the author's asset tests, which pass here (FshFileTests
19/19 with `OPENTPW_TPI_PATH`).

## 3. Robustness

Scratch harness, five corpus files: `alphkid` (loose, 0x2A, RefPack),
`icewall1` (0x2D), `tb_camera` (padded), `an_g07` (raw 0x24) and `Mutant_Eye`
(loose, 0x24, RefPack).

- **Truncation at every length** (not just at record boundaries), with the
  header size left as is and also patched to the new length (51,424 inputs). Every unpatched
  prefix is rejected with `InvalidDataException`. Patched prefixes are rejected
  too, except for 2 to 6 lengths per file that only cut bytes after the name's
  NUL terminator; these decode to the same RGBA, which is correct. A cut exactly
  at the end of the image record gives `NotSupportedException` ("no palette
  record") instead of `InvalidDataException`. This is harmless, and recorded as
  N3.
- **Single-byte mutation fuzz**: six values at every offset, 152,505 inputs.
  Results were `InvalidData`, `NotSupported` or a successful decode only, with
  **0 unexpected exception types**. The successful decodes come from mutated
  pixel, palette, gap, id and tag bytes, which the reader does not interpret.
- **Non-seekable stream** that returns one byte per `Read`: same result as a
  `MemoryStream` for every corpus sample and every probe below.
- **Caps and overflow probes**:
  - Rejected: image count `0xFFFFFFFF`, directory offset `0xFFFFFFFF`, next
    offset `0xFFFFFF`, 65535×65535, 4097×16, palette count `0xFFFF`, a palette
    index past the entry count.
  - 4096×4095 RefPack is accepted, with about 250 MB peak RSS for four decodes
    in one process.
  - 4096×4096 cannot be expressed. Its padded size 2^24 does not fit the 24-bit
    RefPack size of the only accepted header (`10 FB`), and a raw 4096×4096
    image exceeds the 16 MiB input cap. The 16,777,216-pixel cap therefore
    cannot be reached by a single image. This is harmless.
- **Padding rule**:
  - 9 pixels stored with size 16: accepted.
  - 9 pixels stored with size 9: rejected.
  - 16 pixels stored with size 32: rejected.
  - Raw blocks are never required to be padded; all 547 corpus raw blocks have
    exactly w×h bytes and w×h divisible by 16.
  - All 135 corpus images with padding have exactly 12 extra bytes, fewer than
    one row.

  The rule therefore applies only to RefPack and only as the corpus shows it.
- **Unseen variants**: image code 0x7D, RefPack flags 0x11 and attached 0x6F are
  rejected with `NotSupportedException`. The reader accepts some inputs the
  corpus never contains, and FSH.md says so:
  - several images per file;
  - a zero-image file (`SHPI`, size 16, count 0), which decodes to no images;
  - any id other than `G231`;
  - any directory gap;
  - a name record before the palette.

  None of these are blockers.

## 4. Claims and wording

The README row, FSH.md, THEME-PARK-INC.md and FORMAT-BACKLOG.md say "decode to
RGBA", "nothing uses them in the game" and "Theme Park Inc is not playable".
None of them claims visual fidelity, and all their counts agree with section 2.
COMPAT-014 correctly says the opaque, no-colour-key choice is unproven.
COMPAT-015 correctly limits the A1R5G5B5 reading to one file whose entries all
have bit 15 set.

Nits (not blocking):

- **N1**: ApproximationRegister COMPAT-014 says "four grey badges ship 32-bit TGA
  thumbnails". The data shows four files but only two textures (`Badge_TPLogo`
  and `Fastfood_Badge`, each byte-identical in `stexture/` and `textures/`).
  FSH.md says this correctly. "Grey" was not checked by this review.
  Suggested wording: "two badge textures (each shipped twice) have 32-bit TGA
  thumbnails whose alpha is 0 everywhere".
- **N2**: The `--inspect-fsh` footer always cites COMPAT-014, even for a 0x2A or
  0x2D image. For `icewall1` it should cite COMPAT-015. Suggested fix: print the
  approximation for the palette codes actually present.
- **N3**: A file truncated exactly at the end of its image record reports
  `NotSupportedException` ("no palette record") instead of
  `InvalidDataException`.

## 5. Build and tests

Run in a scratch clone with `f51e874` merged with `425169c`
(merge `572422a`, scratch only, not pushed).

- SDK 10.0.401 (`/Users/sander/.local/share/opentpw-dotnet10/dotnet`), Release
  build of OpenTPW.Tests: 0 errors. No warnings in the FSH files.
- Full OpenTPW.Tests without assets: **864 passed, 240 skipped, 0 failed**.
- Full OpenTPW.Tests with `OPENTPW_TPI_PATH`: **872 passed, 232 skipped, 0
  failed**. The 8 tests that skip without assets now run.
- `FshFileTests` with `OPENTPW_TPI_PATH`: 19/19.
- `tools/fidelity_register.py --check`: complete, 140 unresolved unique APPROX
  IDs. `test_fidelity_register.py`: 8/8.
- Evidence runner (Python): all suites pass. The review lane, with this file:
  209 ran, 49 skipped, 0 failed.
  - With `--dotnet`, 7 net8.0 lane harnesses fail as `missing-runtime` because
    this SDK-only install has no net8 runtime. That is environmental: `425169c`
    changes no lane harness.
  - The runner neither strips nor registers `OPENTPW_TPI_PATH`, so
    `--require-fixtures` does not account for it. This is outside this commit.
- CRLF: `git diff --stat` and `git diff --ignore-cr-at-eol --stat` are identical
  (12 files, +1140/−12). `Game.cs` stays CRLF (555 of 555 lines). The new `.cs`
  files are CRLF, matching their neighbours, and the docs stay LF.
- Git objects: the largest new blob is FIDELITY-REGISTER.md (105 KB). No asset
  bytes were found. The only corpus-derived literal is the 8-byte `Buy ERTS` gap
  marker in the synthetic builder and the census test.
- Python paths: the commit adds no Python. This review's test builds corpus keys
  with `Path.as_posix()`.

## Not tested

- How the original game renders, filters or keys these textures.
- Other TPI or Sim Coaster editions, and SHPI files from other EA games.
- The thumbnail MAE figures and the 12-pair duplicate comparison, except through
  the author's own passing tests.

## Resolution on main

- N1: COMPAT-014 now says "two badge textures, each shipped twice".
- N2: the `--inspect-fsh` footer cites COMPAT-014 for 0x24 and COMPAT-015 for 0x2D.
- N3: kept. A missing palette record is deliberately reported as an unsupported variant (`NotSupportedException`, pinned by the synthetic tests); a file truncated exactly after its image record cannot be told apart from that variant.
