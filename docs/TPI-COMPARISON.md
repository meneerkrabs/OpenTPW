# Theme Park Inc comparison with TPW

2026-10-09. The inspected TPI retail files share several TPW asset containers,
and the existing readers accept selected files. This does **not** establish
identical engines, gameplay, opcode semantics, or a working TPI target.
Executable engine version remains unverified. No game or installer was run,
and no protection was removed.

The full phase 2 audit below supersedes the initial sample counts. Its
[full-evidence.json](../tools/tpi-compare/full-evidence.json) contains compact
coverage, intersection and diagnostic aggregates. It pins the SHA-256 and
regeneration command of the complete external audit, which retains every
shared payload hash and per-file diagnostic. Partial SDT results and unknown
COS/SHPI schemas remain explicit. The original sampled evidence
is preserved for provenance.

## Full bounded corpus audit

Full mode removed sample limits while retaining a 50,000-file bound, 128 MiB
physical parse bound, 16 MiB member bound, 2 MiB compressed-member bound,
and existing format-reader limits. All 303 TPI and 312 TPW/Patch 2 WADs
passed directory preflight and parsing. Every member was decompressed and
hashed: TPI 15,007; TPW/Patch 2 13,394 each. No WAD member was excluded by a
resource bound in these corpora. Runtime/gameplay compatibility remains
outside this static audit.

| Actual reader outcome | TPW baseline | Official Patch 2 | TPI retail |
| --- | ---: | ---: | ---: |
| WAD archives accepted | 312 | 312 | 303 |
| WAD members hashed | 13,394 | 13,394 | 15,007 |
| Archive MD2 accepted | 2,116 | 2,116 | 1,962 |
| Loose MD2 accepted | 3 | 3 | 16 |
| Archive MD2 rejected | 2 (207.201) | 2 (207.201) | 0 |
| Loose legacy arrows rejected | 2 (24.23) | 2 (24.23) | 2 (24.23) |
| Archive RSE accepted | 308 | 308 | 287 |
| Loose RSE accepted | 0 | 0 | 1 |
| Archive SAM accepted | 345 | 345 | 292 |
| Loose SAM accepted | 55 | 56 | 77 |
| BF4 files accepted | 33 | 37 | 180 |
| TP2M terrain maps accepted | 5 | 5 | 4 |
| Loose sound-catalog MAP rejected by TP2M reader | 62 | 62 | 38 |
| SDT banks accepted without skipped entries | 47 | 47 | 26 |
| SDT banks partially accepted | 0 | 0 | 2 |

Two additional TPW/Patch 2 archive sound-catalog MAP members also fail the
TP2M reader. These are preserved as format distinctions, not terrain errors.
The full inventory distinguishes header/entry acceptance from audio playback
and geometry rendering. BF4 was decoded by the existing `FontFile` reader;
coverage bytes were not stored in Git.

Exact intersections include physical files and decompressed WAD members,
matching by payload SHA-256 even when paths differ. Duplicate occurrences
are counted separately; all matching hashes are recorded in the full artifact.

| Pair | Distinct equal payload hashes | Occurrences before / after | Common logical paths / equal hashes |
| --- | ---: | ---: | ---: |
| TPW → Patch 2 | 12,696 | 14,153 / 14,154 | 14,195 / 14,150 |
| TPW → TPI | 382 | 758 / 501 | 250 / 95 |
| Patch 2 → TPI | 380 | 756 / 499 | 254 / 93 |

These are full bounded asset intersections, not engine-code overlap or proof
that renamed objects have the same role. Manifest SHA-256 values and resource
bounds are in `full-evidence.json` and can be regenerated from the original
inputs without storing them in Git. The 2,745,817-byte full audit is preserved
at `/Users/sander/server/game-assets/theme-park-inc/analysis/tpi-full-evidence.json`;
its exact SHA-256 and removed-array fingerprints are in the compact artifact.

### Unsupported opcodes and partial sound banks

All TPI opcode IDs fall within the current 0..105 enum. TPI uses 72 distinct
IDs, whereas TPW/Patch 2 use 84. However, the reviewed VM source lacks
registered handlers for two TPI-used IDs: WAITABS (45), 34 instructions in
19 scripts; MULT (48), one instruction in `vegetation.RSE`. The full artifact
pins enum/handler source hashes and each affected script hash. A declared
opcode name is not execution support, and Hooked statuses remain dependent
on host effects rather than proving complete TPI behavior.

Arabian `MusicHD.sdt` SHA-256
`193c976c3d07fda05ef507d00ba97867073dda42e51d99d87040f1fee094d66f`
declares 143 entries; the existing reader accepts 103 and skips 40 beginning
at index 103. Water `MusicHD.sdt` SHA-256
`88a29f9b7ef2f129755ddc0e3d55d852024974c55659d900d77442b342d93b36`
declares 116; 101 are accepted and 15 skipped beginning at index 101.
Accepted entry headers are 40 bytes. Rejected offsets fail header/data span
checks. The audit does not decide whether this is an unsupported bank variant,
stale directory data, extraction issue, or damaged source; it must not silently
claim all SDT banks are compatible.

### Settings, training keys, COS, and SHPI structures

The standard SAM comparison retains key names and hashes of normalized
existing-parser value tokens, without source strings or comments. Of the
242 common keys, 214 have equal value-hash sequences and 28 differ; 554 keys
are TPI-only and 120 are TPW-only. All 702 differences are grouped by key
family in the full artifact. Numeric spelling and boolean case are normalized;
multi-value/trailing-token semantics remain outside the existing parser.

All 369 accepted TPI SAM files/members were searched. No `TrackInfo` key or
`TrackInfo.Direction` appears. The only Direction key is lighting-related
`ThemeEngine.DirectionalLightLevel`. Training/experience evidence includes
`StaffAgencies[n].TrainingExperienceGainModifier`,
`AllStaffConstants.MinExperienceForTraining`,
`AllStaffConstants.TrainingExtraExperience`, and grade-specific experience
families. Their file paths, file hashes, key names, and value hashes are
recorded. They establish data presence, not controller direction or staff
training formulas.

All eighty original disc COS files were hashed and inspected. Every first
little-endian u32 is 2; none contains the inspected BILZ, DWFB, RSSEQ, SHPI,
or TP2M tags. The existing save-envelope reader rejects all eighty. These
observations do not turn that word into a version/type or decode track records.

All 7,283 SHPI-bearing files/members (162 physical, 7,121 archive members)
pass the same bounded structural consistency checks: the u32 at offset 4
equals file length, the count/table candidate at offset 8 fits the file, and
the candidate offsets in eight-byte records starting at 16 lie inside the
file beyond the table. The four-byte directory tag is hashed, not dumped.
This qualifies a consistent container-header family, not pixel compression,
palette semantics, or an implemented SHPI/FSH decoder.

### Retail executable and official readme cross-check

The full extracted retail tree has no `Game.exe`/`Game.icd`; existing
`unshield l data1.cab` reports no game-executable entry. The disc-root
`Game.exe` remains the separately identified input. Fix folders are excluded
and no fix executable is substituted for it. Standard version-resource API
results are null; the DRM/wrapper limit on engine version remains conditional.

Disc `Readme.txt` SHA-256
`90f8447c0fd32ebbf851a9ed9bb0fe7f6fde6932fb287ddb1e25d7ba79dfb8cb`
and `ReadMe.htm` SHA-256
`4939e85128250b95792d80d34957b30b5684432321fed8f17c3ffb431f73214e`
contain no version-word or explicit product-engine-version candidate in the
bounded text check. No readme text is checked into Git. Product titles and
system requirements cannot fill the missing engine build/version evidence.

Phase 2 validation: thirteen C# fixture cases and eleven Python tests pass.
Full actual-reader inventories complete without WAD member limit exclusions;
unsupported versions and partial SDT results remain in the report. No new
decoder, dependency, game target, installer/game execution, or protection
removal was introduced.

Metadata and reproducible probes: [tools/tpi-compare](../tools/tpi-compare/README.md),
[evidence.json](../tools/tpi-compare/evidence.json). All original assets remain
outside Git. The result records actual reader calls, their failures, and sample
limits separately; file extensions alone are not evidence of compatibility.

## Metadata prototypes for remaining format questions

The [format evidence](../tools/tpi-compare/format-evidence.json) and
`format_questions.py` retain hashes, offsets, dimensions, and record-boundary
observations. They do not decode images/audio/coasters or change production
loaders. The prototype contracts follow constraints checked against the real
corpus; unsupported meanings remain explicit.

### SHPI bitmap and palette headers

No publisher-authored EA specification was located. A primary implementation
by the format researcher provides an independently documented old-shape
16-byte entry header and format IDs:
[EA Graphics Manager, pinned entry implementation](https://github.com/bartlomiejduda/EA-Graphics-Manager/blob/dce358bc1d34102ea2c72b74210b6ca627a46cc4/src/EA_Image/dir_entry.py),
[palette entry implementation](https://github.com/bartlomiejduda/EA-Graphics-Manager/blob/dce358bc1d34102ea2c72b74210b6ca627a46cc4/src/EA_Image/attachments/palette_entry.py).
This is original implementer evidence, not an official EA specification; no
external decoder/dependency or implementation was installed or copied.

All 7,283 TPI entries agree with that layout: a one-byte format, three-byte
block length, and two-byte width/height in the sixteen-byte header. Format
byte 123 occurs 547 times; byte 251 occurs 6,736 times. Both mask to 123,
documented as indexed eight-bit imagery. Every byte-251 entry's payload
starts with the observed RefPack prefix; none of the byte-123 entries does.
All 547 uncompressed entries have block length exactly `16 + width*height`.
This supports header/size classification without decoding palette indices.

The next header at the image block-length boundary has palette-type
candidates: 4,308 entries use type 36, 2,974 type 42, and one type 45.
The primary implementation describes these as 24-bit, 32-bit, and 16-bit
palette families respectively. Dimensions/count candidates and block lengths
are recorded; alignment, trailing attachments, color ordering, transparency,
and rendered pixel correctness remain unqualified. The unique type-45 example
is water `snowtrac.wad!stexture/icewall1.fsh`, SHA-256
`1a3ecb2f79537ad8d23091fa049e64d98c40ea8aff746ef258a48359f02f6dd0`.
Do not infer a complete FSH decoder from these metadata constraints.

### COS body grouping and original controller boundaries

All eighty coaster files yield sixteen exact body SHA-256 groups after
excluding offsets 0..131, with five language files per group. The four-byte
first word is 2; offsets 4..131 form a fixed 128-byte header region, whose
contents are hashed rather than dumped. Words at offsets 128 and 132 are
zero in the observed corpus. This establishes a useful metadata comparison
boundary and language-independent body equality. It does not establish text
encoding, a serialized track schema, record counts/strides, or thumbnail data.

The rides lane separately proves Mac **memory** contracts: coaster train
records are 128 bytes and car records 96 bytes; car +68 is rider allowance,
+56/+60 are offset/normalized offset floats, and controller +172 is track
length. Train spacing floats are selected from +28/+32/+36. Those proofs
anchor functions `0x3ea74`, `0x3ebe8`, and `0x3ee10`, but no COS load/save
linkage was recovered. These memory strides are therefore not used as COS
file-record strides. TPI's parsed SAM corpus also lacks TrackInfo/Direction
keys that could establish that linkage. The metadata prototype stops at
magic, size, bounded header hashing, and body grouping.

### Music-bank directory/data conflict

Valid indexed prefixes retain the standard forty-byte header: all 103 Arabian
and 101 Water prefix records end exactly at the next declared directory
offset. No alternate prefix record layout is needed to explain those records.
The tail scanner requires a forty-byte header, bounded positive payload, and
plausible bounded name; it stores only name/header hashes and boundaries.

Water's tail contains fifteen candidates with fourteen exact end→next-header
links. The first is at file offset 27,419,825, 7,065 bytes after the first bad
directory offset; later differences vary. This is coherent off-directory
record evidence, not a justified automatic offset repair.

Arabian's tail contains 270 candidates and 268 chain links. More decisively,
247 candidate headers and **246 complete header+audio blocks** are byte-identical
to indexed entries in the original global `speechHD.SDT`. The metadata pins
both file identities, each matched speech index/offset, music offset, and
complete-block SHA-256. This proves directory/payload disagreement inside the
music bytes and distinguishes the failure from merely a new header layout.
It does not settle whether the source or extraction caused the disagreement.
Existing `unshield t` completed 1,566 cabinet-file tests with exit code zero;
that result is not silently promoted to an independent CRC/source-correctness
proof. Both production SDT warnings and loader behavior remain unchanged.

### Asset build label and verification

The extracted retail `Data/Ver.txt` is eighteen bytes, SHA-256
`337d2b461e38d69fa7390397feb325038f80bfa3bcba49e6950e7aeb58dc0897`,
and carries asset build label `Beta_21`. This is a packaged data label, not a
numeric product version or proof of the wrapped executable's engine release.
It supplements the earlier readme/resource-version limits.

Metadata prototype verification adds three Python cases: language-independent
COS bodies, bounded/hashed SDT headers, and exact whole-block comparison
without decoding. All fourteen Python tests and thirteen C# cases pass.
No original arrays, audio/image payloads, names from binary string blobs,
new dependencies, runtime execution, DRM/fix usage, or gameplay decoder is
introduced.

## Sources and identity boundaries

The authorized input was `THEME_PARK_INC_OrigineelCD.zip`, SHA-256
`b2b5399ad812c27c342f6c8edf70f8a8495146786f9a13a4e66768541f4505f4`.
The parent intake checked all 725 ZIP entries' CRCs; expanded size was
617,987,640 bytes. This bundle is **mixed**: it contains retail disc files,
`WIN10FIX+NOCDFIX`, and a serial text file. Fix folders were not used as retail
engine evidence; serial/license contents were not read or printed.

Disc root: `/Users/sander/server/game-assets/theme-park-inc/cd/THEME_PARK`.
Existing `unshield` extracted the original InstallShield cabinets into
`/Users/sander/server/game-assets/theme-park-inc/retail`, yielding 1,566 files.
InstallShield support/engine groups are installer components and must not be
mistaken for the game's engine. The game executable is on the disc root,
separate from the extracted Data group. Coasters on the disc are also separate
from the cabinet payload.

| Retail input | SHA-256 |
| --- | --- |
| `data1.cab` | `3ab8459394c123383e08832c0d52d230e954fe24c8394068d8f6ea77356e4a1a` |
| `data1.hdr` | `ae7005b01a629dbcfbfaeeb87863e87d5df8cf9254a403508d227c2ae37dc6ff` |
| `data2.cab` | `ee056c4e705e480831ebb4b8d92129a835919bff657ddb8d8574d1b6ebc866a8` |
| Disc-root `Game.exe` | `17adfde860f34375ee4986ea2b7100fefaddf8bb4f1f9bb4058fbfa8392fe29d` |

Three TPW baselines remain distinct: the Windows data baseline at
`game-assets/theme-park-world` (801 physical files; no executable in this root),
the official Patch 2 installation at `game-assets/theme-park-world-patch2`
(824 files, including executables), and the Feral PowerPC application/libraries
at `game-assets/mac-feral/bin`. Common physical paths between Windows baseline
and Patch 2 have 780 equal hashes and 21 changed hashes; these corpus counts
are not a replacement for the official patch application manifest.

## Shared, changed, and unverified evidence

| Area | Finding | Status and actual probe |
| --- | --- | --- |
| WAD | TPI and TPW have `DWFB`, version 2, directory/name/member layouts accepted by `WadArchive` | **Shared container family**; twelve archives parsed per corpus; TPI physical count 303, TPW/Patch 2 each 312 |
| Models | Selected TPI `M3D2` files use version major/minor 221/203 and parse as geometry/animation | **Shared supported model family**; TPI fifteen sampled archive members and ten loose models accepted; TPW twenty sampled members and three loose models accepted |
| Legacy arrows | Both contain identical `garrow.MD2`/`rarrow.MD2`, version 24/23 | **Shared unsupported legacy files**; `ModelFile` rejects the version, not a TPI-only regression |
| Terrain maps | Four TPI `TP2M` maps parse as 128×128 with five opaque header values; TPW sampled maps use the same layout | **Shared envelope/layout**; raw cell-value histograms differ; TPW flag meanings are not automatically qualified for TPI |
| Scripts | TPI sampled scripts have the fixed RSSEQ header and pass `RideScriptFile` structural/reference checks | **Shared serialized script family**; eight archive scripts plus loose `vegetation.RSE` accepted; opcode execution/units remain unverified |
| Settings | TPI `Standard.sam` parses 796 entries versus TPW 362 | **Changed schema/content**; 242 shared keys, 120 TPW-only, 554 TPI-only; reader acceptance is syntactic |
| Sound banks | TPI speech SDT declares/parses 1,136 entries versus TPW 641 | **Shared entry layout, changed content**; twelve banks parsed per corpus; no sound payload was decoded or played |
| Images | 162 physical TPI files and twenty-four sampled WAD members begin with `SHPI` | **Changed/unverified image family**; no new SHPI/FSH decoder was written; archive extension counts also show FSH where TPW terrain stores WCT |
| COS | Disc contains eighty `.cos` files; twelve are inspected with the existing save-envelope reader | **Unverified**; all twelve fail its magic check with first u32 value 2; no COS decoder or schema was invented |
| Game engine/version | TPI `Game.exe` exposes PE headers and six visible imports; product/file version API returns null | **Unverified**; wrapper-shaped sections/import surface constrain comparison; linker/image versions are not engine versions |

The probe records 61 equal hashes and 110 changed hashes among 171 common
physical paths in the Windows TPW and extracted TPI corpora. This excludes
renamed assets, languages installed into different groups, and paths absent
from either root. It is not a count of all shared content.

## Concrete proof files and fields

TPI Arabian `terrain.wad`, SHA-256
`5ae4b37ff82ea84754f918245d963c8154f89f4a6b14cecb3bb0a0ec56b6094a`,
has version 2 and 146 entries, all represented by the existing WAD directory
reader. TPW Jungle `terrain.wad`, SHA-256
`6a4443e7793122eb3a804eee3f4f1ebe19324199185b2562f5311d0f331c3300`,
has version 2 and 205 entries. The WAD reader ignores some header semantics;
acceptance does not prove every version/field is understood.

Arabian `terrain.wad!base.map`, SHA-256
`67bcf5bd76e70b1388ac5d8784589340c9d60001a7aa6737346e23fccb987aec`,
passes `MapFile`: Width=128, Height=128, CellCount=16,384. TPW Jungle
`terrain.wad!base.map`, SHA-256
`adbc201acc29e9e81b936dfc9ffcdc403757a920f76c9ef41c435a5417d98364`,
passes the same reader with the same dimensions. Loose sound-catalog `.map`
files in **both** games fail the TP2M reader; they are a different format,
not evidence that TPI terrain maps are broken.

TPI `Data/global/Advisor/advisor.MD2`, SHA-256
`12b3bcdb755eb10b22188d2a53c10b553970e2a51a74c7b89d8b390c92e0fa02`,
parses as M3D2 221.203 geometry: 29 meshes, 37 nodes, 49 textures. The raw
header counts and parsed geometry/animation classification are recorded;
this does not validate rendering or all animation consumers.
The shared legacy arrow identity `garrow.MD2` is
`d5154361929da3a835fee4a47a68ef9c717e18365e670902d423d4e8f480839f`.

`AnimCtrl.RSE` is an exact shared sample across TPW Fantasy and TPI Arabian
`features/end.wad`: SHA-256
`7a05cb5604523b5e7527ce464fd8167133f763390051e9bce3bd0e12e3325e2b`.
It passes structural checks with VariableCount=0, TimeSlice=50,
CodeWordCount=158, and 43 instructions. Similar `bus.RSE` samples differ:
TPW SHA-256 `501b94bf633c461b2c1d4d12932fab54d2b8f14ebe8e406bf5404c1cab223211`
has 124 words/46 instructions; TPI
`e2eed92689dfea7a969d8ee970d3bae93c08ab212748965e3eb261f7ca9c8a1b`
has 100 words/38 instructions. Both have four variables and TimeSlice=50.
No opcode semantics or clock frequency is inferred from these counters.

TPI standard settings SHA-256 is
`8966c8647104feb1878ff3c3d43f3b2ac6dc89856570f9aa5a5a0bf040e6749e`;
TPW/Patch 2 standard settings SHA-256 is
`3d39433641df70dba73e6ce0f4ae576a5a334ed93e61adc1cdd570b751a8bf4b`.
TPI-only keys include `AllStaffConstants.StaffRoomCapacity`,
`AllStaffConstants.MaxExperience`, and `AllStaffConstants.TrainingExtraExperience`.
`SAMParser` keeps one value token per line, so this is key coverage and parse
acceptance rather than complete multi-value schema validation or value parity.

TPI speech bank SHA-256 is
`8454708b4006b04b0bd746248989e694e48e7d8fb722324e820661729ed2a155`;
TPW/Patch 2 bank SHA-256 is
`61e2d6a34c7eecb4569bcd8495c4515227f4404a5c787bd0181e2928f2b79fe8`.
Both declared entry counts equal parsed counts with no skipped entries in
these selected banks. Existing audio object constructors normalize some
rate/bit-depth properties, so those properties are not used as original-field
evidence here.

TPI `terrain.wad!pathtex/an_cnr1.fsh`, SHA-256
`5f4be6b22e6b3569f194625c20ed9fa7eb579fe47f083222e4adb8dd82641620`,
has an observed SHPI signature and 14,704 bytes; image contents were not
decoded. Dutch `Ali Baba Waterbaan.cos`, SHA-256
`999b8299565a22b101509784a6103ca38015a9f2a9c7527c560021155564c4aa`,
has 14,184 bytes and fails `SaveReader.Inspect` at magic 2. Calling that word
a COS version, a save type, or a coaster element count would be speculation.

## Executable and platform limits

Standard PE disk metadata is interpreted using
[Microsoft's PE specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
COFF machine, linker-version, image-version, section, and import fields are
reported separately; none identifies an engine release by itself.

Disc `Game.exe` is 7,502,887 bytes, x86 PE32, linker field 6.0, image field
0.0. It has fifteen section headers including `stxt774`/`stxt371`; its visible
import descriptors expose KERNEL32 and USER32 with six named entries. Neither
MSVC RTTI-name candidates nor plain C-prefixed terminated name candidates
were found by the bounded text probes. This does not prove absence of RTTI
or diagnostics in the underlying wrapped program. No unpacking, execution,
or executable code equivalence claim was attempted.

Other retail disc metadata is kept separate: `IOSYS002.dll` (SHA-256
`8f5fc483def69e2d12e644d9156f2063141641e011d0c54a66fdf36a5eb5e96d`)
does not pass the bounded PE probe. `drvmgt.dll` (SHA-256
`b2ab0d236b379ce73e0ee68974444990dc0bc9fd5854e5a6a12411c7bb622ed1`)
exposes KERNEL32/USER32/ADVAPI32 imports. `secdrv.sys` (SHA-256
`0913892f5e61e92868449331be5a65dc88b1d56a0b9f837665edd18ad0bf8508`)
has an x86 header and an ntoskrnl import. Their presence/imports are packaging
metadata, not a procedure for loading or bypassing them.

Patch 2 `TP.EXE`, SHA-256
`fbff39b6498cdef873695a310a8543ff421176ad24140279485a0f1d4b01298e`,
is a separate 265,734-byte PE with four visible import libraries and SafeDisc
text markers. `TP.ICD`, SHA-256
`f5f727963bcdb1bb5da033a00a4d413c353fdb6833ae5fdc68287d5e27abcedd`,
is 3,734,573 bytes, linker field 5.10, and exposes 204 imports across 21
libraries. It shares some named section families with the TPI image, but
sizes/import surfaces differ. These observations do not qualify either
wrapped code path or establish engine version parity.

Feral `SimThemePark.data`, SHA-256
`04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5`,
passes the existing PowerPC PEF reader: 716 imported symbols, zero exports,
main transition vector in data section 1. Feral `engine_shared.data`, SHA-256
`c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b`,
has 142 imports and 198 exports. CFM/PowerPC metadata and Windows PE metadata
remain independent baselines; symbol counts are not comparable engine sizes
or behavior proofs.

## Verification and handoffs

Nine C# synthetic tests cover signature priority, actual WAD/SAM member
readers, exclusion rules, and rejection bounds. Eight Python tests cover PE
headers/import spans/terminators, malformed inputs, and comparison summaries.
The helper builds with existing repository Common/Files warnings; no new
package is added and shared readers are untouched. Full reproduction commands
are in the tool README.

Unverified inputs are explicit: sampling excludes most archives and large
compressed members; SDT results do not decode audio; SAM acceptance is not a
TPI schema implementation; TPW map-bit meanings and VM opcode effects need
TPI evidence; COS and SHPI remain undecoded; protected game version/runtime
behavior cannot be established by these probes. Independent next lanes could
review the metadata, inventory unsupported opcode/header variants, or qualify
additional existing-reader samples. A TPI target or new decoder needs a
separate task and evidence plan.
