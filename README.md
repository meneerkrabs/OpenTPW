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

## Status

OpenTPW is currently in a very early stage of development, and is not yet playable.

### File Formats

- ❌ - Not Implemented
- ⚠️ - Partially Implemented
- ✅ - Implemented

| Format                                                  | Status |
|---------------------------------------------------------|--------|
| Textures ([.WCT](https://opentpw.gu3.me/formats/wct.html))                    | ✅     |
| Settings ([.SAM](https://opentpw.gu3.me/formats/sam.html))                    | ✅     |
| Sounds ([.SDT](https://opentpw.gu3.me/formats/sdt.html), .MP2)         | ⚠️     |
| Strings ([.BFMU](https://opentpw.gu3.me/formats/bfmu.html), [.BFST](https://opentpw.gu3.me/formats/bfst.html), [.BFUM](https://opentpw.gu3.me/formats/bfum.html)) | ✅     |
| Models ([.MD2](https://opentpw.gu3.me/formats/m3d2.html))                      | ⚠️     |
| Map Data ([.MAP](https://opentpw.gu3.me/formats/map.html))                    | ⚠️     |
| Ride Scripts ([.RSE](https://opentpw.gu3.me/formats/rsse.html))                | ⚠️     |
| Save Files ([.TPWS](https://opentpw.gu3.me/formats/tpws-ints-lays.html))                | ⚠️     |
| Fonts ([.BF4](https://opentpw.gu3.me/formats/bf4.html))                      | ⚠️     |
| Lip Sync ([.LIP](https://opentpw.gu3.me/formats/lips.html))                   | ⚠️     |
| Banner mesh companions ([.MTR](https://opentpw.gu3.me/formats/mtr.html))      | ⚠️     |
| Video ([.TQI/.TGQ](https://opentpw.gu3.me/formats/tqi.html))                  | ⚠️     |

### Documentation

Every remaining ⚠️ format now has a bounded, strict CPU reader tested against the
original files, but none is a finished game feature: MD2 parses 2,116/2,118
models and decodes position/rotation/scale animation tracks (tick rate and
vertex animation unverified); MAP reads the 128×128 TP2M terrain grids
(five cell bits and the grid-to-world mapping verified, the rest opaque); RSE parses all 308 scripts and the VM runs them (all 84 used
opcodes handled, 51 of them through an unimplemented-effect hook; the sandbox
Totem runs its original script); the Jungle TPWI payload's cell grid and placed
objects are imported read-only into an original level view
(`--load-original-level`; money, guests and other sections opaque) where the
objects stand as their original models and simulated guests drawn with the
original kid sprites arrive, walk the paths and use the rides, shops, sideshows
and toilets through their scripts (approximated rules, [GUESTS](docs/GUESTS.md));
all 274 original objects (plus the official bonus objects via `--bonus-data`) can
be built and run their original scripts and animations, without sounds or ride
controllers ([objects](docs/OBJECTS.md)); `.LIP` marks
are microsecond talking/silence toggles, inferred from the decoded speech audio. They
drive the original advisor's mouth with SDL audio via `--advisor-say N`; the original
mouth-shape choice is unknown. ISO-only `.MTR` files decode as topology and matrices
redundant with their banner `.MD2` (no material data; runtime use unknown). SDT speech
and music (MPEG-2 Layer II) decode within 1 LSB of an external decoder, but Layer I
sound effects do not decode yet. All nine TGQ movies decode audio bit-exact and video
close to, not bit-identical with, an external reference, without playback. Evidence:
[MD2](docs/MD2-MODELS.md), [MAP](docs/MAP.md), [RSE](docs/RSE-SCRIPTS.md) / [RSE VM](docs/RSE-VM.md),
[TPWS payload](docs/TPWS-PAYLOAD.md), [LIPS](docs/LIPS.md), [MTR](docs/MTR.md),
[TGQ](docs/TGQ-MOVIES.md).

The BF4 CPU decoder handles four-bit, RLE and monochrome glyphs and is tested
against the 33 original fonts of each shipped language. A GPU glyph atlas draws original
strings; the default start is now an original-style front end (the 3D lobby islands, menus,
options) and an in-game HUD built from the original `ui.wad` models and textures, string tables
and fonts in all six languages (Metal readback-verified). Positions of code-placed elements are
approximations and visual fidelity is unverified, so BF4 stays partial (see [UI](docs/UI.md)). Strings decode in all six
verified languages (English, Danish, Dutch, French, German, Swedish) with each language's
own character table; `--language` selects one and `--language-data` reads the
others from the extracted original CD (see [languages](docs/LANGUAGES.md)). See
[BF4 evidence](docs/BF4-FONTS.md), [format backlog](docs/FORMAT-BACKLOG.md),
[completion plan](docs/COMPLETION-PLAN.md) and [progress](docs/PROGRESS.md).
Format checkmarks do not qualify full gameplay or every platform/edition.

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
