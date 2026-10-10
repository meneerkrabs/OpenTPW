# Theme Park Inc (Sim Coaster) compared with Theme Park World

Theme Park Inc (Europe, 2001; Sim Coaster in North America) is Bullfrog's follow-up
to Theme Park World. This note records how far its data and engine match what
OpenTPW reads, measured on October 9, 2026. It is a comparison, not a support claim:
OpenTPW does not load Theme Park Inc. Since October 10, 2026 OpenTPW has a reader for
its `.fsh` textures ([FSH.md](FSH.md)); nothing uses it in the game yet.

## Sources

- **Theme Park Inc:** the Dutch/Benelux retail CD (`Game.exe` PE timestamp
  2001-01-10). The data comes from the InstallShield cabinets `data1.cab`/`data2.cab`
  (1,566 files, 336 MB) via `unshield`. The CD also carries third-party
  no-CD/compatibility patches; they were not used or inspected.
- **Theme Park World:** the Mac "Sim Theme Park" CD (HFS volume, November 2000),
  folder `Theme Park Data/data` (312 MB). This is a different edition from the
  Windows edition the rest of OpenTPW's documentation pins (see below).

Neither set is committed. Every number here comes from OpenTPW's own readers run
over loose files and every WAD member, plus small read-only scripts. The exception is the
[Global Master Save](#global-master-save-gms) layout, which was traced in the Theme Park Inc
executable with Ghidra (static analysis only) and then checked against three original save files.

## Summary

The two games run the same engine family. Archives, models, ride scripts, settings,
fonts, lip-sync, sound banks and the online DLLs are the same formats. The largest
asset change is textures: Theme Park Inc replaced the wavelet `.WCT` textures with
EA `SHPI` (`.fsh`) palette images. Its save header is an extended version of the
TPWS container, and its balance settings grow.

| Area | Theme Park World (Mac data) | Theme Park Inc | OpenTPW reader on Theme Park Inc |
| --- | --- | --- | --- |
| Archives `.wad` (`DWFB`, RefPack) | 327 | 303 | 303/303 open, every member extracts |
| Models `.md2` (version bytes 221) | 2,197 | 1,980 | 1,978/1,980; the same 2 `garrow.MD2` (version 24) fail as in TPW |
| Ride scripts `.rse` (`RSSE`) | 324 | 288 | 288/288 parse; see the VM section |
| Settings `.sam` | 418 | 369 | 369/369 |
| Fonts `.bf4` (`F4FB`) | 32 | 180 | 180/180 |
| Lip sync `.lip` | 641 | 1,136 | 1,136/1,136 |
| Strings `.str` (`BFST`), `BFMU`/`BFUM` tables | 21 | 185 | same magic (not parsed in this run) |
| Sound banks `.sdt` | 48 | 28 | 26/28; the 2 failures are truncated music banks, see below |
| Textures | 8,712 `.wct` | 34 `.wct` (intro only), 7,283 `.fsh`, 2,856 `.tga`, 172 `.png` | `.wct` 34/34; `.fsh` 7,283/7,283 decode to RGBA ([FSH.md](FSH.md)) |
| Terrain/attribute maps `.map` (`TP2M`) | 5 | 4 | 4/4; the other `.map` files are sound catalogs in both games |
| Saves | `Easymode.TPWI` (magic 400) | `prebuilt.TPWS` (magic 500, extended header) | rejected: "version 0; expected 133" |
| Movies | QuickTime `.mov` (Mac edition) | 2 `.tgq` (`SCHl`) | 2/2 |
| Banner companions `.mtr` | none on the Mac CD | 7 | 0/7: header words differ from the TPW variant |

## Ride-script VM: shared formats and opcode lineage

All 288 Theme Park Inc scripts have the same 48-byte header and time slice (50) as
all 324 TPW scripts. Every opcode they use is in OpenTPW's `Opcode` enum with the
same operand count as in TPW, and no unknown opcode occurs. Theme Park Inc uses 72
distinct opcodes; it adds `WAITABS` (45) and `MULT` (48), which no TPW script uses.
It no longer uses `TRIGANIMSPEED`, `LOOPANIM_CH`, `TURBO`, the child-variable
opcodes (66, 67, 69), `GETREMOTEVAR`, the `WALK*FLOAT*` opcodes, `HOUR`/`MIN`/`SEC`
or `SPARK`. So Theme Park Inc scripts would exercise two VM paths that TPW data
never reaches.

`TestScript.wad` ships five RSE **source** files (`test.rss`, `Shop.rss`,
`Feature.rss`, `TestAnim.rss`, `Null.rss`) for Bullfrog's assembler (`#setstack`,
`#setlimbo`, `variable`, `.label`, `#include "\source\game\rsse\code\rsse_scriptdefs.h"`).
They are first-party evidence for the VM:

- The 12 common ride variables are declared in the order of OpenTPW's
  `RideVariables` (`VAR_LETMEON` = 0 … `VAR_PARAM` = 11); custom variables follow from 12.
- `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP` followed by `BRANCH_NV go ; timed out !`, and
  `SUB VAR_TEMP VAR_CAPACITY VAR_TEMP` followed by `BRANCH_Z`/`BRANCH_NV` (shop full),
  match OpenTPW's `SUB dest a b` = a − b.
- `ADD VAR_COUNT -1` followed by `BRANCH_PV runlp` matches the strictly positive
  `BRANCH_PV`.
- Comments name visitor operations: `HUSH VAR_LETMEON ; This guy is now on the ride`,
  `HOP VAR_LETMEOFF` then `; Wait until AI has taken him...`,
  `UNLIMBO VAR_LETMEOFF ; get next person out (if any)`, and `INLIMBO VAR_TEMP` as
  the number of visitors in limbo.
- `LIMBO VAR_LETMEON VAR_DURATION` gives the second `LIMBO` operand, which
  OpenTPW's handler names `unknown`: it is the time spent in limbo.
- `COPY VAR_LETMEON 0 ; Open the gate` and `COPY VAR_LETMEON 1 ; Close the gate`
  give the gate meaning of `VAR_LETMEON` in shop scripts.

These are test scripts, not shipped ride scripts, so they support but do not prove
the semantics of the TPW corpus.

## Differences

- **Textures.** Theme Park Inc stores level, UI and advisor textures as one-image
  EA `SHPI` files: all 7,283 have directory id `G231` and an 8-bit palette image
  (code `0x7B`; 6,736 of them RefPack-compressed as `0xFB`) with a 24-bit (4,308),
  32-bit (2,974) or 16-bit (1) palette. Most are 32×32, 64×64 or 128×128. `.wct`
  remains only in `levels/wIntro`. `FshFile` decodes all of them; see [FSH.md](FSH.md).
- **Saves.** `prebuilt.TPWS` starts with magic 500 (TPWS), like TPW saves, but then
  inserts `01 13 00 00 00` and the ASCII string `Version: Beta_21` before the
  UTF-16 `THE SAVE GAME DATA…` banner. OpenTPW's version-133 offline layout does
  not apply.
- **Global save.** Theme Park Inc keeps the player's progress in a Global Master Save (`.GMS`, with `.GMI` in
  the same extension table), separate from park saves. `ThemeParkIncGlobalSave` reads it; see
  [Global Master Save](#global-master-save-gms) below.
- **Balance settings.** `levels/Standard.sam` has 413 keys against 192 in TPW,
  156 of them shared. The additions are mainly staff simulation
  (`AllStaffConstants.*`: wages, happiness, experience, staff rooms). `Challenges.sam`
  is restructured around missions (`MissionNeededToActivate`, `ScriptIndex`,
  `CashPrize`, `GTPrize`). `sound.sam` and `high.sam` have identical key sets.
- **Themes.** Arabian, water, science and an intro level (`wIntro`) replace jungle,
  fantasy, hallow and space. Each theme folder has the same layout (`rides`,
  `shops`, `features`, `facilities`, `obstacles`, `sky`, `Music`, `Sound`,
  `terrain.wad`, `Standard.sam`, `global.sam`, `Hoardings.sam`, `scape.omp`).
  New top-level folders: `missions`, `PreBuilt`, `Sky`, `extents`.
- **Music banks.** `arabian` and `water` `MusicHD.sdt` are cut off at
  40,787,968 and 31,457,280 bytes (exactly 30 MiB for water). The installer's own
  file table lists these sizes, so the cut is on the CD, not in the extraction.
  Their entry tables list 143 and more tracks than fit; OpenTPW's SDT reader skips
  the 40 and 15 entries past the end and keeps the rest.
- **MTR.** All 7 `.mtr` members start with the word 777590191, unlike the
  6/1/1/0 TPW variant `MtrFile` accepts.

## Shared runtime components

- **Online:** Theme Park Inc ships the same `wea*` online DLL family as TPW
  (`weaauth`, `weachat`, `weacity`, `weamail`, `weanews`, `wearas`, `weaupload`,
  `weavote`, each as `…d`/`…r`), built 1999-09-28 to 2000-01-26. `weachatr.dll`
  (96,256 bytes) and `weauploadr.dll` (70,144 bytes) have the sizes of the patched
  TPW files in OFFICIAL-PATCH-RUNNER.md, but different hashes.
- **Audio:** `QMixer.dll` (built 1999-10-29), as in TPW's import list.
- **Executable:** `Game.exe` (linker 6.0, image base 0x400000) is SafeDisc-wrapped
  (`stxt774`/`stxt371` sections, high-entropy `.text`), like TPW's `TP.ICD`. Its
  section names name the bundled media code: `TQIA_DAT` and `IDCT_DAT` (TGQ/TQI
  video), `LBMPEG_D` (MPEG audio), `UVA_DATA`, `GRPOLY_D`. The executable was not
  decrypted or disassembled.

## Sound banks: Layer I verified on original data

Running the Layer I decoder over the Mac TPW data decodes every Layer I entry:
2,641 MPEG-2 22,050 Hz mono, 5 MPEG-1 44,100 Hz mono and 4 MPEG-2 22,050 Hz
stereo clips (2,650 total), without a failure, and every clip matches ffmpeg's `mp1`
decoder in length and to within 1 LSB. Theme Park Inc's banks hold 3,691
MPEG-2 22,050 Hz mono and 12 MPEG-1 44,100 Hz mono Layer I clips, plus Layer II
speech/music in the same modes as TPW; all decode except the clips past the end
of the two truncated music banks.

## Edition note: Mac Theme Park World

The Mac data differs from the Windows edition OpenTPW pins in its tests. The
differences found so far: QuickTime `.mov` movies instead of `.tgq`; 641 loose
`.lip` files instead of `lips.wad`; TrueType fonts in resource forks (the data
forks of its 17 `.ttf` files are empty); 324 RSE scripts against 308. Asset tests
that pin Windows hashes and counts fail on it for these reasons, which says nothing
about the readers.

## Possible follow-up work

1. ~~`.fsh` (`SHPI`, code `0x7B`) reader.~~ Done (October 10, 2026): `FshFile`
   decodes all 7,283 files and WAD members to RGBA, with `--inspect-fsh`; see
   [FSH.md](FSH.md). Not done: using the textures in the game, and checking them
   against the original game's rendering. Opaque `0x24` palettes are approximation
   COMPAT-014. The next step is loading a Theme Park Inc level in the sandbox
   (MD2 texture names resolved to `.fsh` members).
2. Use the `.rss` evidence in the VM: name `LIMBO`'s second operand as the limbo
   duration and cite the source comments in the opcode handlers.
3. The extended TPWS header (`Version: Beta_21`) and the new `Standard.sam` keys, if
   Theme Park Inc support ever becomes a goal.

## Global Master Save (.GMS)

The player's progress across the worlds lives in a Global Master Save. "GlobaMasterSave" is the name the program
uses in its own log text. It is not a park save. The layout was traced in the Theme Park Inc executable, the scene
noCD build of `Game.exe`, by static analysis only:

- `0x0074C8F0` writes the file;
- `0x0074D110` loads it;
- the shared body is `0x0074D840` with its sub-serializers.

The file is uncompressed and little-endian. The loader rejects it when one of six markers differs.

| Part | Content |
| --- | --- |
| Header | `u32` world (0–2; the writer stores 3 when there is none), `u32` version 12, a `u32` and 8 bytes |
| Card table | 256 × 16 bytes (`0x0074B1F0`): Info.Id, a level 0–2, a flag and an amount. Empty slots have flag `0x7FFFFFFF`. The observed files use 16 slots |
| Words and bytes | including the mission number (object +0x105C; the loader then reads the world's `Miss%02d.sam`) |
| 5 × 48 bytes, `TATS`, 15 words, `AMTA` | untraced |
| Parks | `u32` count; per park a length-prefixed name (`arabian`, `science`, `water`, `wIntro`), then `0x0074FEF0`: two interleaved UTF-16 sign lines (`mSignNameA`/`mSignNameB`, 33 characters), `d_available`, `d_open`, `mParkNumber`, `mNameChanged`, `mAllResearchCompleted`, `mChallengesDone`, `mAllChallengesDone`, `mCurrentTechLevel` |
| `MEHT`, options | `0x0045A130`: four volumes stored as (on, level) word pairs (the last two named SpeechVolume and MovieVolume), AdvisorOn, TutorialOn, TooltipsOn, ConfirmDeleteOn, RMBScrollOn, RMBCancelOn, IsometricOn, AnimateMenus, GameSpeed, mZoomMin, mZoomMax, mFPSClip, MusicEnabled |
| History | `mFirstHourTime`, then 644 × 20 bytes `mHistory[i]` |
| Tail | `0x00592720` (a word and 92 bytes), `DTOT`, the shares game (`0x00620E30`: three company slots, `mNumberOwned`, `mPctCompanySharesOwned`, `mCompanyName`, `mCurrentValue`), `RAHS`, `0x00622D70` (count, bytes, a byte and three words), `MAPS`, the ambient sound tags (`0x005BF470`: count × 16 bytes) |

Three German retail saves read to their last byte with every marker in place. They are kept private and
checked by `ThemeParkIncGlobalSaveTests` when `OPENTPW_TPI_SAVES` names their folder. Sections without a traced
meaning are read and skipped, and nothing is written back.
