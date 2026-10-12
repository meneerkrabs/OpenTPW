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
        <a href="https://opentpw.io/docs/">Documentation</a>
    </p>
</p>

![A resumed Jungle park with fixed rides, shops and the bundled Enhanced HUD](website/images/shot-current.png)

Actual browser gameplay after loading an own-format park save, with the bundled Enhanced HUD and icons.

## About this fork

This repository is a fork of [OpenTPW/OpenTPW](https://github.com/OpenTPW/OpenTPW), the project
started by Alex Guthrie and its contributors. Development there had slowed down, so we continued
the work here at a faster pace. We would much rather work together than apart: maintainers and
contributors of the original project are very welcome here, and we are happy to offer our changes
back upstream or to join efforts in whatever way works best. Open an issue or a discussion to talk.

For now our website is **[opentpw.io](https://opentpw.io/)**. Its [status page](https://opentpw.io/docs/status.html) is generated from this repository at every deploy (`tools/site_status.py`), and it has reading options (fonts, contrast, reduced motion).

## What OpenTPW is

OpenTPW runs Theme Park World on modern systems with a new engine. It reads the data of your own
copy of the original game. The engine does not ship the original executable or a playable set of
game files, and nothing from your installation is uploaded when you play in the browser. Releases
include redrawn HD interface art; the optional Clarity pack supplies enhanced replacements as a
separate download ([Legal](#legal)).

**To run OpenTPW you need a full, legal copy of the original game** (an installation or the CD).

The aim is the original game, not a remake. Behaviour is traced from the original wherever
possible, and every place where OpenTPW still has to guess is tagged in the code and listed in
the [fidelity register](https://opentpw.io/docs/FIDELITY-REGISTER.html).

## Getting started

OpenTPW is a **development preview**, but most of the game already works: pick a world in the
original lobby, build a park with the original objects and run it with guests, staff and money.

- **In the browser:** open [play.opentpw.io](https://play.opentpw.io/) (Chrome, Edge, Firefox or
  Safari) and choose your Theme Park World folder. Nothing is installed and your files stay on your
  computer. The browser build is the newest part of OpenTPW: the front end, parks, sound and online
  play run there, and saves and settings stay in the browser between visits; movies and the
  developer panels are desktop-only for now ([web build](https://opentpw.io/docs/WEB.html)).
- **Download:** [pre-release builds](https://github.com/meneerkrabs/OpenTPW/releases) for Windows
  (x64, x86, ARM64), macOS (Apple silicon) and Linux (x64, ARM64). Unpack and start OpenTPW; on the
  first start it asks for your game folder. Follow the [installation guide](https://opentpw.io/install.html)
  for your platform ([releases](https://opentpw.io/docs/RELEASES.html), [setup](https://opentpw.io/docs/SETUP.html)).
- **From source:** install the .NET 10 SDK pinned in `global.json`, then

  ```sh
  bash scripts/run.sh --game-path '/path/to/Theme Park World'
  ```

  or `./scripts/run.ps1` on Windows. See [running](https://opentpw.io/docs/RUNNING.html) for options such as
  `--sandbox`, `--language` and the native macOS libraries.

## Status

What works today, with your own original game data:

- **Start-up as on the CD:** the CD's autorun launcher (Play, View Read-me, Exit) and the original
  start-up movies, Bullfrog logo included ([autorun](https://opentpw.io/docs/AUTORUN.html); `--no-autorun`, `--no-intro`).
- **Front end and HUD:** the original 3D lobby with its islands, menus, the original-layout Game
  Options screen (OpenTPW's own settings sit on a separate page) and the in-game HUD, built from the
  original models, fonts and strings, with bundled Enhanced interface art enabled by default
  ([UI](https://opentpw.io/docs/UI.html)).
- **Languages:** English, Danish, Dutch, French, German and Swedish, and every European language on
  your CD once you choose its folder ([languages](https://opentpw.io/docs/LANGUAGES.html)).
- **Parks:** the four worlds with their original terrain; all 274 original objects can be built
  and run their original ride scripts and animations, and the official bonus objects can be added;
  players build their own roller coasters ([objects](https://opentpw.io/docs/OBJECTS.html), [ride scripts](https://opentpw.io/docs/RSE-VM.html),
  [coasters](https://opentpw.io/docs/COASTERS.html)).
- **Guests, economy and staff:** guests arrive, walk the paths and use rides, shops, sideshows and
  toilets. The 31 root scripts that use ADDHEAD in the fantasy, hallow, jungle and space themes can
  show riders' heads where the model's head nodes resolve; missing node mappings and the original
  attachment rules remain qualified (HEADS-001–005), and coaster and tour cars do not show riders
  yet. Money, loans, wages, research, objectives, golden tickets and keys run on the original
  settings files ([guests](https://opentpw.io/docs/GUESTS.html), [economy](https://opentpw.io/docs/ECONOMY.html)).
- **Park door and rides:** the park door (the entry-price screen's door button, Ctrl-O and Ctrl-C) opens
  and closes the park: admissions stop at the booth, the rides close, and the entrance gate shuts once
  the park is empty ([park door](https://opentpw.io/docs/GATES.html)). A placed queue cell can be clicked to extend its ride's
  queue, and the selected ride can be ridden in a first-person view (E; Escape or a right click leaves
  it). These controls work in OpenTPW; their remaining original-game fidelity limits are documented
  ([guests](https://opentpw.io/docs/GUESTS.html), [UI](https://opentpw.io/docs/UI.html)).
- **Save and resume:** the pause menu's Save writes the whole running park — layout, guests, queues,
  ride scripts, money, staff and world state — in OpenTPW's own format, one slot per theme, and Load
  resumes it exactly where it left off: the same ids, queues and riders, the saved gameplay mode and
  start kind, the park open or closed as saved. This works in the app and in the browser, where the
  save persists between visits. A save is pinned to the game data it was made with: changed
  simulation assets refuse the load with a reason before the running park is touched. Roller coasters
  and prototype rides cannot be saved yet, and the original TPWS/TPWI format stays read-only
  ([park saves](https://opentpw.io/docs/PARK-SAVES.html)).
- **Advisor, music and movies:** the advisor speaks for the traced game events with lip sync, the
  park and the front-end lobby play their original music, rides, the crowd and the interface play
  their original sounds, and all nine movies play. Placed ambients, rain and thunder, ride screams,
  staff walking/idle voices and path-tool feedback are wired. Focused real-data tests verify decoded
  PCM for forced weather and staff events; the native smoke runs did not reach their natural triggers
  ([audio](https://opentpw.io/docs/AUDIO.html), [lip sync](https://opentpw.io/docs/LIPS.html),
  [movies](https://opentpw.io/docs/TGQ-MOVIES.html)).
- **Online again:** accounts, park sharing, visiting, voting, postcards and chat parks, on the
  official server or one you host yourself. A shared park is the park as its owner built it (paths,
  queues, objects and player-built coasters in version 2 packages, with a layout digest checked on
  import). Visits show the published statistics read-only, use the shared ride camera for supported
  rides and allow voting. Park visits do not synchronize simulations or show other players; the
  service and its protocol are OpenTPW implementations ([online](https://opentpw.io/docs/ONLINE.html),
  [server](https://opentpw.io/docs/SERVER.html)).
- **Modern systems:** native builds for Windows, macOS and Linux; any resolution and aspect ratio
  with a HiDPI-aware interface scale; button labels fitted per language; compatibility fixes from
  the community ([compatibility](https://opentpw.io/docs/COMPATIBILITY.html)).
- **Enhanced HUD and optional textures:** fresh settings use **Original world textures** plus the
  **Enhanced HUD and icons included with OpenTPW**. The interface needs no download and has its own
  Original/Enhanced toggle. Choosing **Enhanced (Clarity)** in the options explicitly starts the
  optional world-texture download (about 3.6 GiB for the full pack on desktop); switching textures
  takes effect without a restart. Desktop installs verified GitHub Release archives; the browser
  downloads needed PNGs lazily through jsDelivr from bounded GitHub packages, with a Cloudflare
  fallback and SHA-256 checks. Existing explicit choices are preserved. You can also build a local
  pack from your own copy ([texture packs](https://opentpw.io/docs/TEXTURE-PACKS.html)).
- **Extras:** viewing and exporting the PlayStation 2 disc data (`--export-ps2`, [PS2](https://opentpw.io/docs/PS2.html)).

Not done yet: go-kart and water ride tracks, rider heads on coaster and tour cars, unresolved flat-ride
head attachments, positioned ride sounds (RSE EVENT types 1 and 2), additional staff actions and
their sounds, weather visuals, the visitor click, global lobby effects, the path tool's link-end and
undo sounds, and the exact original rules in places where OpenTPW still uses a documented
approximation. The
[completion plan](https://opentpw.io/docs/COMPLETION-PLAN.html), [feature matrix](https://opentpw.io/docs/FEATURE-MATRIX.html) and
[progress](https://opentpw.io/docs/PROGRESS.html) track what remains.

### File formats

These statuses describe file readers and decoders for the selected original-asset corpus,
separately from gameplay, UI fidelity and compatibility with other editions. ✅ means the observed
decoding is implemented; ⚠️ means decoding or format semantics remain incomplete. Neither symbol
certifies every variant or original-runtime parity.

| Format | Status | Verified support / remaining limit |
|---|---|---|
| Textures ([.WCT](https://opentpw.gu3.me/formats/wct.html)) | ✅ | Decoder loads the selected game's textures; other editions are unverified. |
| Settings ([.SAM](https://opentpw.gu3.me/formats/sam.html)) | ✅ | Parser reads the selected game's settings; gameplay interpretation remains approximate. |
| Sounds ([.SDT](https://opentpw.gu3.me/formats/sdt.html), .MP2) | ⚠️ | SDT containers, MPEG-1/2 Layer I effects and MPEG-2 Layer II speech/music decode (≤1 LSB vs independent reference on selected corpora); sound catalogues, CRC checks and de-emphasis are implemented. Music, UI, routed ride effects, crowd, advisor, placed ambients, screams, rain/thunder, walking/idle staff voices and path lay/refusal are wired. Forced weather/staff PCM regressions pass; natural weather/staff triggers were not reached by the native smoke runs. Positioned RSE EVENT types 1–2, additional staff actions, visitor/global lobby effects and path link-end/undo remain untriggered; weather visuals and original mixer fidelity remain incomplete ([sound](https://opentpw.io/docs/AUDIO.html), [audio evidence](https://opentpw.io/docs/LIPS.html)). |
| Strings ([.BFMU](https://opentpw.gu3.me/formats/bfmu.html), [.BFST](https://opentpw.gu3.me/formats/bfst.html), [.BFUM](https://opentpw.gu3.me/formats/bfum.html)) | ✅ | 21 string tables per language decode and character tables round-trip in six verified languages; other codepages/editions are unverified ([languages](https://opentpw.io/docs/LANGUAGES.html)). |
| Models ([.MD2](https://opentpw.gu3.me/formats/m3d2.html)) | ⚠️ | 2,116/2,118 WAD members parse; the two others (207.201) and the two loose 24.23 arrows are refused by the original loader too. Animation members decode into position/rotation/scale tracks, quantised vertex groups, per-corner texture-coordinate tracks, node-flag toggles, the 12-byte block and texture-frame tracks (all 1,278 checked; unread record bits are exporter metadata); placed objects and the Totem play them. The 12-byte block is shown as the stored mesh (no original reader traced), path parameters and texture frames are not applied, PC executable equivalence is unverified, and rendering fidelity is unverified ([models](https://opentpw.io/docs/MD2-MODELS.html)). |
| Map Data ([.MAP](https://opentpw.gu3.me/formats/map.html)) | ⚠️ | All five TP2M terrain grids parse; grid mapping and five cell bits verified. Remaining flags/header values are opaque; the sound-catalog MAPs are a separate, decoded format ([maps](https://opentpw.io/docs/MAP.html), [sound](https://opentpw.io/docs/AUDIO.html)). |
| Ride Scripts ([.RSE](https://opentpw.gu3.me/formats/rsse.html)) | ⚠️ | All 308 scripts parse and all 84 used opcodes have handlers; 51 route through effect hooks whose game systems/semantics remain incomplete ([VM](https://opentpw.io/docs/RSE-VM.html)). |
| Save Files ([.TPWS](https://opentpw.gu3.me/formats/tpws-ints-lays.html)) | ⚠️ | Read-only: the TPWI container splits into its 19 writer-order blocks and the World thing list (42 things in Easymode) closes at its delimiter. Imported: cell grid, placed objects with price/level/repair, park turn and date, bank, staff and the advisor's pending advice/history; guests, research, the RNG state and the script/particle/sound blocks stay unimported (SAVE-001–010). Original-format saves are never written; other save variants are unverified; OpenTPW parks save in [OpenTPW's own world format](https://opentpw.io/docs/PARK-SAVES.html) instead ([payload](https://opentpw.io/docs/TPWS-PAYLOAD.html), [inventory](https://opentpw.io/docs/reverse/SAVE-plan.html)). |
| Fonts ([.BF4](https://opentpw.gu3.me/formats/bf4.html)) | ✅ | All 33 fonts in each of six verified languages decode (four-bit, RLE, monochrome); original text layout/appearance remains unverified ([fonts](https://opentpw.io/docs/BF4-FONTS.html), [UI](https://opentpw.io/docs/UI.html)). |
| Lip Sync ([.LIP](https://opentpw.gu3.me/formats/lips.html)) | ✅ | Mark lists parse; signed conversion, talking toggles, loaded-LIP start state and random mouth selection are traced to the Mac binary. The advisor speaks by itself for the traced game events (park start, Instant Action, bankruptcy; open/close queue but score below the minimum) with the original mouth choice; its clock is the frame clock and does not pause (ADVISOR-017), and animation/pose remain open ([lip sync](https://opentpw.io/docs/LIPS.html), [binary trace](https://opentpw.io/docs/reverse/APPROX-TRACE.html)). |
| Banner mesh companions ([.MTR](https://opentpw.gu3.me/formats/mtr.html)) | ⚠️ | Selected topology and matrices decode; original runtime purpose remains unknown ([companions](https://opentpw.io/docs/MTR.html)). |
| Video ([.TQI/.TGQ](https://opentpw.gu3.me/formats/tqi.html)) | ⚠️ | All nine movies decode and stream via `--play-movie`; audio is bit-exact, video remains close but not bit-exact (84–95 % identical samples against FFmpeg; the original PC transform and output rounding are traced, its quantiser-matrix builder and FPU precision are still open, and the Mac build holds no TQI decoder (its movies are Sorenson Video), see [movies](https://opentpw.io/docs/TGQ-MOVIES.html)). |
| Theme Park Inc textures (.FSH, EA `SHPI`) | ⚠️ | All 7,283 Theme Park Inc `.fsh` files/WAD members decode to RGBA (`--inspect-fsh`) and the experimental level viewer draws them; Theme Park Inc is not playable, and opaque 24-bit palettes are an approximation ([FSH](https://opentpw.io/docs/FSH.html)). |
| Theme Park Inc level viewer (`[EXT:tpi-viewer]`) | ⚠️ | `--load-original-level arabian\|science\|water` with Theme Park Inc data shows that world (terrain, lake, paths, gate, monorail, bus, guests, sound) with a camera only: no HUD, economy or advisor, and nothing is verified against the original game ([viewer](https://opentpw.io/docs/THEME-PARK-INC.html#theme-park-inc-level-viewer)). |

File format notes are also available on the [OpenTPW formats](https://opentpw.gu3.me/formats/)
website, a work in progress from the original project.

## Research on the original game

Game rules count as original only with evidence: the original data files, the manual, or static
analysis of the original program. The Mac PowerPC build of Theme Park World ships unprotected and
with many named functions, so most rules are traced there with Ghidra; findings, addresses and
open questions are written down in [reverse findings](https://opentpw.io/docs/reverse/FINDINGS.html). The provenance
rules are in the [completion plan](https://opentpw.io/docs/COMPLETION-PLAN.html). Original binaries, decompiled code and
game files never go into this repository.

## Goals

1. **Theme Park World**: the complete original offline game. This is the primary goal; it is in
   progress and not yet complete ([completion plan](https://opentpw.io/docs/COMPLETION-PLAN.html)).
2. **Online play as in the original game**: sharing, visiting and voting on parks, postcards and
   chat parks with live presence, movement and the original chat commands. The original servers are
   gone and their protocol is not reconstructed, so OpenTPW provides its own opt-in, self-hostable
   service ([online](https://opentpw.io/docs/ONLINE.html), [what the original had](https://opentpw.io/docs/reverse/ONLINE-FEATURES.html)).
3. **Theme Park Inc / Sim Coaster**: a separate, later goal. Both games share most file formats and
   closely related ride-script formats ([comparison](https://opentpw.io/docs/THEME-PARK-INC.html)), but Theme Park Inc
   is not playable yet; an experimental level viewer shows its worlds.
4. **Sandbox modes** (an OpenTPW extension, not original behaviour): free building with Theme Park
   World content, Theme Park Inc content, or both. Planned; today's `--sandbox` uses Theme Park
   World content only.

## Contributing

Contributions are very welcome: code, research on the original game, bug reports and testing on
your own edition of the game.

1. Fork this repository and create a branch, for example `yourname/feature-name`.
2. Keep repository text in English, never commit original game files or binaries, and tag every
   behaviour you could not trace to the original as an approximation (see the
   [fidelity register](https://opentpw.io/docs/FIDELITY-REGISTER.html)).
3. Run the tests (`dotnet test source/OpenTPW.Tests`; tests that need original files run when
   `OPENTPW_GAME_PATH` points at your game folder) and open a pull request.

## Legal

OpenTPW is not affiliated with or endorsed by Electronic Arts or Bullfrog Productions. Theme Park
World and Sim Theme Park are trademarks of their respective owners. OpenTPW contains no original
executable code or playable original-game installation; you need your own legal copy of the game
to play. The bundled HD HUD and menu art in `content/hero-art` is AI recreation of the original
icons, made with Google Gemini and fitted to the original layout. The optional Clarity download
contains AI-upscaled and refined replacements derived from original textures; it is distributed
separately from the engine and is not required to play. Local pack-building tools remain available
([texture packs](https://opentpw.io/docs/TEXTURE-PACKS.html)). The engine's MIT licence does not grant rights to the
original game assets or trademarks.

## License

MIT; see [LICENSE.md](LICENSE.md).
