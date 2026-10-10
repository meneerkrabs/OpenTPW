<p align="center">
    <h1 align="center">
        OpenTPW
    </h1>
    <p align="center">
        An open-source re-implementation of <a href="https://en.wikipedia.org/wiki/Theme_Park_World">Sim Theme Park / Theme Park World</a> (Bullfrog, 1999).
        <br>
        <a href="https://opentpw.io/">Website</a> |
        <a href="https://play.opentpw.io/">Play in the browser</a> |
        <a href="https://github.com/meneerkrabs/OpenTPW/releases">Downloads</a> |
        <a href="docs/">Documentation</a>
    </p>
</p>

![image](https://github.com/user-attachments/assets/be81a5d3-f99c-4f46-8200-7ea5d9a652e8)

## About this fork

This repository is a fork of [OpenTPW/OpenTPW](https://github.com/OpenTPW/OpenTPW), the project
started by Alex Guthrie and its contributors. Development there had slowed down, so we continued
the work here at a faster pace. We would much rather work together than apart: maintainers and
contributors of the original project are very welcome here, and we are happy to offer our changes
back upstream or to join efforts in whatever way works best. Open an issue or a discussion to talk.

For now our website is **[opentpw.io](https://opentpw.io/)**.

## What OpenTPW is

OpenTPW runs Theme Park World on modern systems with a new engine. It reads the data of your own
copy of the original game: no game files are part of this repository, its releases or the
website, and nothing is uploaded when you play in the browser.

**To run OpenTPW you need a full, legal copy of the original game** (an installation or the CD).

The aim is the original game, not a remake. Behaviour is traced from the original wherever
possible, and every place where OpenTPW still has to guess is tagged in the code and listed in
the [fidelity register](docs/FIDELITY-REGISTER.md).

## Getting started

OpenTPW is a **development preview**: it is playable, but far from complete (see [Status](#status)).

- **In the browser:** open [play.opentpw.io](https://play.opentpw.io/) (Chrome, Edge, Firefox or
  Safari) and choose your Theme Park World folder. The browser build runs the front end (the 3D
  lobby, menus and options) and the online features; starting a park, sound, movies and saving
  do not run in the browser yet ([web build](docs/WEB.md)).
- **Download:** [pre-release builds](https://github.com/meneerkrabs/OpenTPW/releases) for Windows
  (x64, x86, ARM64), macOS (Apple silicon) and Linux (x64, ARM64). Unpack and start OpenTPW; on the
  first start it asks for your game folder ([releases](docs/RELEASES.md), [setup](docs/SETUP.md)).
- **From source:** install the .NET 10 SDK pinned in `global.json`, then

  ```sh
  bash scripts/run.sh --game-path '/path/to/Theme Park World'
  ```

  or `./scripts/run.ps1` on Windows. See [running](docs/RUNNING.md) for options such as
  `--sandbox`, `--language` and the native macOS libraries.

## Status

What works today, with original game data:

- **Front end and HUD:** the original-style 3D lobby, menus, options and in-game HUD, built from
  the original models, textures, fonts and strings in six languages ([UI](docs/UI.md),
  [languages](docs/LANGUAGES.md)).
- **Parks:** the four worlds load their original terrain; all 274 original objects can be built
  and run their original ride scripts and animations ([objects](docs/OBJECTS.md),
  [ride scripts](docs/RSE-VM.md)).
- **Guests, economy and staff:** guests arrive, walk the paths and use rides, shops, sideshows and
  toilets; money, loans, wages, research and objectives run on the original settings files. Most
  rules are still registered approximations ([guests](docs/GUESTS.md), [economy](docs/ECONOMY.md)).
- **Advisor, music and movies:** the advisor speaks for the traced game events with lip sync,
  the park plays its original music, and all nine movies decode and play ([audio](docs/AUDIO.md),
  [lip sync](docs/LIPS.md), [movies](docs/TGQ-MOVIES.md)).
- **Online:** park sharing, visiting, voting, postcards and chat through an opt-in OpenTPW server,
  official or self-hosted ([online](docs/ONLINE.md), [server](docs/SERVER.md)).
- **Modern systems:** native builds for Windows, macOS and Linux at any resolution (so far only
  macOS on Apple silicon is verified on real hardware), an optional
  enhanced texture pack generated locally from your own copy ([texture packs](docs/TEXTURE-PACKS.md)),
  and compatibility fixes ([compatibility](docs/COMPATIBILITY.md)).

Not done yet: coaster construction, the full scenario and unlock progression, most ride, ambient and
interface sounds, park play in the browser, and the exact original rules in many places. The [completion plan](docs/COMPLETION-PLAN.md), [feature matrix](docs/FEATURE-MATRIX.md)
and [progress](docs/PROGRESS.md) track what remains.

### File formats

These statuses describe file readers and decoders for the selected original-asset corpus,
separately from gameplay, UI fidelity and compatibility with other editions. ✅ means the observed
decoding is implemented; ⚠️ means decoding or format semantics remain incomplete. Neither symbol
certifies every variant or original-runtime parity.

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

File format notes are also available on the [OpenTPW formats](https://opentpw.gu3.me/formats/)
website, a work in progress from the original project.

## Research on the original game

Game rules count as original only with evidence: the original data files, the manual, or static
analysis of the original program. The Mac PowerPC build of Theme Park World ships unprotected and
with many named functions, so most rules are traced there with Ghidra; findings, addresses and
open questions are written down in [reverse findings](docs/reverse/FINDINGS.md). The provenance
rules are in the [completion plan](docs/COMPLETION-PLAN.md). Original binaries, decompiled code and
game files never go into this repository.

## Goals

1. **Theme Park World**: the complete original offline game. This is the primary goal; it is in
   progress and not yet complete ([completion plan](docs/COMPLETION-PLAN.md)).
2. **Online play as in the original game**: sharing, visiting and voting on parks, postcards and
   chat. The original servers are gone and their protocol is not reconstructed, so OpenTPW provides
   its own opt-in, self-hostable service ([online](docs/ONLINE.md)).
3. **Theme Park Inc / Sim Coaster**: a separate, later goal. Both games share most file formats and
   closely related ride-script formats ([comparison](docs/THEME-PARK-INC.md)), but Theme Park Inc
   is not playable yet.
4. **Sandbox modes** (an OpenTPW extension, not original behaviour): free building with Theme Park
   World content, Theme Park Inc content, or both. Planned; today's `--sandbox` uses Theme Park
   World content only.

## Contributing

Contributions are very welcome: code, research on the original game, bug reports and testing on
your own edition of the game.

1. Fork this repository and create a branch, for example `yourname/feature-name`.
2. Keep repository text in English, never commit original game files or binaries, and tag every
   behaviour you could not trace to the original as an approximation (see the
   [fidelity register](docs/FIDELITY-REGISTER.md)).
3. Run the tests (`dotnet test source/OpenTPW.Tests`; tests that need original files run when
   `OPENTPW_GAME_PATH` points at your game folder) and open a pull request.

## Legal

OpenTPW is not affiliated with or endorsed by Electronic Arts or Bullfrog Productions. Theme Park
World and Sim Theme Park are trademarks of their respective owners. OpenTPW contains no original
game code or assets; you need your own legal copy of the game to play.

## License

MIT; see [LICENSE.md](LICENSE.md).
