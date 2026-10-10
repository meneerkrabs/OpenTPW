<p align="center">
    <h1 align="center">
        OpenTPW
    </h1>
    <p align="center">
        OpenTPW is an open-source re-implementation of <a href="https://en.wikipedia.org/wiki/Theme_Park_World">Sim Theme Park / Theme Park World</a>.
        <br>
        <a href="https://opentpw.org/">Website</a> |
        <a href="https://docs.opentpw.org/">Documentation</a>
    </p>
</p>

![image](https://github.com/user-attachments/assets/be81a5d3-f99c-4f46-8200-7ea5d9a652e8)

## About

OpenTPW is a re-implementation of Theme Park World, requiring an installation the original game and its assets in order to run. OpenTPW aims to re-create the same experience as the original game. While OpenTPW was initially created as it is quite difficult to get Sim Theme Park to run on modern hardware and on a modern operating system, it also aims to somewhat re-introduce the original online aspect of the game - the servers of which have since been shut down.

**In order to run OpenTPW, you must have a full legal copy of any version of the original game.**

## Goals

These are goals, not current features; [Status](#status) below says what works today.

1. **Theme Park World**: the complete original offline game. This is the primary goal;
   it is in progress and not yet complete ([completion plan](docs/COMPLETION-PLAN.md)).
2. **Online play as in the original game**: sharing, visiting and voting on parks,
   postcards and chat. The original servers are gone and their protocol is not
   reconstructed, so OpenTPW provides its own opt-in, self-hostable service. Partly
   implemented as an extension (accounts, park publish/search/download/visit,
   voting, postcards, chat); see [online](docs/ONLINE.md).
3. **Theme Park Inc / Sim Coaster**: a separate, later goal. Both games share most file
   formats and closely related ride-script formats ([comparison](docs/THEME-PARK-INC.md)), but
   its `.fsh` textures, extended saves and missions are not read yet. Not supported yet.
4. **Sandbox modes** (an OpenTPW extension, not original behaviour): free building with
   Theme Park World content, Theme Park Inc content, or both combined. Planned; today's
   `--sandbox` uses Theme Park World content only.

## Status

OpenTPW has a working preview: an original-style front end, options and park HUD,
original terrain and objects, scripted rides and simulated guests. Gameplay rules
and visual fidelity are still approximations; this is not a complete recreation
of the original game (see [progress](docs/PROGRESS.md)).

### File Formats

These statuses describe file readers and decoders for the selected original-asset
corpus, separately from gameplay, UI fidelity and compatibility with other editions.
✅ means the observed decoding is implemented; ⚠️ means decoding or format semantics
remain incomplete. Neither symbol certifies every variant or original-runtime parity.

| Format | Status | Verified support / remaining limit |
|---|---|---|
| Textures ([.WCT](https://opentpw.gu3.me/formats/wct.html)) | ✅ | Decoder loads the selected game's textures; other editions are unverified. |
| Settings ([.SAM](https://opentpw.gu3.me/formats/sam.html)) | ✅ | Parser reads the selected game's settings; gameplay interpretation remains approximate. |
| Sounds ([.SDT](https://opentpw.gu3.me/formats/sdt.html), .MP2) | ⚠️ | SDT containers, MPEG-1/2 Layer I effects and MPEG-2 Layer II speech/music decode (≤1 LSB vs independent reference on selected corpora); the `cat_*SFX.map`/`cat_*BANK.map` sound catalogues are read and the park plays its original music (busier sections as guests arrive), the park view click and the advisor through one mixer; ambient, ride and most UI sounds are not triggered yet, and CRC verification and de-emphasis remain incomplete ([sound](docs/AUDIO.md), [audio evidence](docs/LIPS.md)). |
| Strings ([.BFMU](https://opentpw.gu3.me/formats/bfmu.html), [.BFST](https://opentpw.gu3.me/formats/bfst.html), [.BFUM](https://opentpw.gu3.me/formats/bfum.html)) | ✅ | 21 string tables per language decode and character tables round-trip in six verified languages; other codepages/editions are unverified ([languages](docs/LANGUAGES.md)). |
| Models ([.MD2](https://opentpw.gu3.me/formats/m3d2.html)) | ⚠️ | 2,116/2,118 members parse; position/rotation/scale tracks decode. Two older-version members and vertex/other animation payloads remain unsupported ([models](docs/MD2-MODELS.md)). |
| Map Data ([.MAP](https://opentpw.gu3.me/formats/map.html)) | ⚠️ | All five TP2M terrain grids parse; grid mapping and five cell bits verified. Remaining flags/header values are opaque; the sound-catalog MAPs are a separate, decoded format ([maps](docs/MAP.md), [sound](docs/AUDIO.md)). |
| Ride Scripts ([.RSE](https://opentpw.gu3.me/formats/rsse.html)) | ⚠️ | All 308 scripts parse and all 84 used opcodes have handlers; 51 route through effect hooks whose game systems/semantics remain incomplete ([VM](docs/RSE-VM.md)). |
| Save Files ([.TPWS](https://opentpw.gu3.me/formats/tpws-ints-lays.html)) | ⚠️ | Selected TPWI container, cell grid and placed objects import; most state payloads and other save variants remain unverified/unsupported ([payload](docs/TPWS-PAYLOAD.md)). |
| Fonts ([.BF4](https://opentpw.gu3.me/formats/bf4.html)) | ✅ | All 33 fonts in each of six verified languages decode (four-bit, RLE, monochrome); original text layout/appearance remains unverified ([fonts](docs/BF4-FONTS.md), [UI](docs/UI.md)). |
| Lip Sync ([.LIP](https://opentpw.gu3.me/formats/lips.html)) | ✅ | Mark lists parse; signed conversion, talking toggles, loaded-LIP start state and random mouth selection are traced to the Mac binary. The bounded original-clock helper is tested; the manual SDL presentation still uses its own timeline ([lip sync](docs/LIPS.md), [binary trace](docs/reverse/APPROX-TRACE.md)). |
| Banner mesh companions ([.MTR](https://opentpw.gu3.me/formats/mtr.html)) | ⚠️ | Selected topology and matrices decode; original runtime purpose remains unknown ([companions](docs/MTR.md)). |
| Video ([.TQI/.TGQ](https://opentpw.gu3.me/formats/tqi.html)) | ⚠️ | All nine movies decode and stream via `--play-movie`; audio is bit-exact, video remains close but not bit-exact ([movies](docs/TGQ-MOVIES.md)). |
| Theme Park Inc textures (.FSH, EA `SHPI`) | ⚠️ | All 7,283 Theme Park Inc `.fsh` files/WAD members decode to RGBA (`--inspect-fsh`); nothing uses them in the game, Theme Park Inc is not playable, and opaque 24-bit palettes are an approximation ([FSH](docs/FSH.md)). |

### Documentation

The remaining ⚠️ formats have bounded CPU readers tested against selected
original files, with runtime integration at different stages: MD2 parses 2,116/2,118
models and decodes position/rotation/scale animation tracks (tick rate and
vertex animation unverified); MAP reads the 128×128 TP2M terrain grids
(five cell bits and the grid-to-world mapping verified, the rest opaque); RSE parses all 308 scripts and the VM runs them (all 84 used
opcodes handled, 51 of them through effect hooks with incomplete game semantics; the sandbox
Totem runs its original script); the Jungle TPWI payload's cell grid and placed
objects are imported read-only into an original level view
(`--load-original-level`; money, guests and other sections opaque) where the
objects stand as their original models and simulated guests drawn with the
original kid sprites arrive, walk the paths and use the rides, shops, sideshows
and toilets through their scripts (approximated rules, [GUESTS](docs/GUESTS.md));
all 274 original objects (plus the official bonus objects via `--bonus-data`) can
be built and run their original scripts and animations, without sounds or ride
controllers ([objects](docs/OBJECTS.md)); `.LIP` marks
are microsecond talking/silence marks, traced through the Mac advisor's signed
conversion to a pause-aware millisecond clock. In a park the advisor now speaks
automatically for the traced game events: level start says the welcome from the
level's own speech bank (and, in Instant Action, the prebuilt-park advice), and
bankruptcy and park open/close are queued through the original's scored
eight-slot queue with `Advisor.sam` scores (park open/close score 20, below the
minimum 25, so they never play). Every other advice message is not wired yet;
`--no-advisor` turns it off and `--advisor-say N` / `--advisor-response N` still
play one clip by hand. While talking the mouth is one of five shapes chosen at
random every 100 ms, as in the original (`AdvisorMouth`); pose, animation,
placement and timing remain registered approximations ([LIPS](docs/LIPS.md)).
ISO-only `.MTR` files decode as topology and matrices redundant with their banner
`.MD2` (no material data; runtime use unknown). SDT speech, music and Layer I
effects decode within 1 LSB of independent references on selected corpora;
event scheduling, CRC verification and de-emphasis remain incomplete. All nine
TGQ movies decode audio bit-exact and video
close to, not bit-identical with, an external reference; `--play-movie` streams
the decoded video and audio. Evidence:
[MD2](docs/MD2-MODELS.md), [MAP](docs/MAP.md), [RSE](docs/RSE-SCRIPTS.md) / [RSE VM](docs/RSE-VM.md),
[TPWS payload](docs/TPWS-PAYLOAD.md), [LIPS](docs/LIPS.md), [MTR](docs/MTR.md),
[TGQ](docs/TGQ-MOVIES.md).

The BF4 CPU decoder handles four-bit, RLE and monochrome glyphs and is tested
against the 33 original fonts of each of the six verified languages. A GPU glyph atlas draws original
strings; the default start is now an original-style front end (the 3D lobby islands, menus,
options) and an in-game HUD built from the original `ui.wad` models and textures, string tables
and fonts in all six languages (Metal readback-verified). Positions of code-placed elements are
approximations and visual fidelity is unverified (see [UI](docs/UI.md)); this is
separate from BF4 decoding completeness for the selected corpus. Strings decode in all six
verified languages (English, Danish, Dutch, French, German, Swedish) with each language's
own character table; `--language` selects one and `--language-data` reads the
others from the extracted original CD (see [languages](docs/LANGUAGES.md)). See
[BF4 evidence](docs/BF4-FONTS.md), [format backlog](docs/FORMAT-BACKLOG.md),
[completion plan](docs/COMPLETION-PLAN.md) and [progress](docs/PROGRESS.md).
Format checkmarks do not qualify full gameplay or every platform/edition.

The recovered PowerPC rules, independent reviews and bounded reference helpers are
recorded in [reverse findings](docs/reverse/FINDINGS.md). [TPI comparison](docs/TPI-COMPARISON.md)
measures shared data and parser behavior; it does not establish engine equivalence.

Compatibility ([docs](docs/COMPATIBILITY.md)): the 17 sign fonts are read in memory from
`fonts.wad` by an own TrueType rasterizer (the jungle gate shows the park name in the
font and size from its `.sgn`); `--cd-data` fills in missing movies/music from the
extracted CD; Low/Medium/High detail presets come from the original `.sam` files plus an
Enhanced extension; an Original (default) / Recommended / Custom fix profile, missing-string
fallback, `UniToMB.dat` text encoding and damaged-SDT tolerance. Approximations are tagged
and listed in its approximation register.

File format information is available at the [OpenTPW formats](https://opentpw.gu3.me/formats/) website. Keep in mind that this information is a work-in-progress, and therefore might not be of incredible detail - however, upon completion, it still aims to be as useful, detailed, and as in-depth as possible.

## Contributing

Contributions to this project are greatly appreciated; please follow these steps in order to submit your contribution to the project:

1. Fork the [Original Project](https://github.com/ThemeParkWorld/OpenTPW)
2. Create a branch under the name `YourName/FeatureName`
3. Once you've made all the changes you need to make, go ahead and submit a Pull Request.

## License

This project is licensed under the MIT license; a copy of this license is available at [LICENSE.md](https://github.com/ThemeParkWorld/OpenTPW/blob/main/LICENSE.md).
