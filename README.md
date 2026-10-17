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

For Theme Park Inc / Sim Coaster, use the [static launch and installation guide](https://opentpw.io/theme-park-inc.html): current desktop/browser steps, upcoming edition chooser availability and experimental limits.

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
  First attraction/shop previews now load substantially faster through bounded LZSS
  copies and reused preview assets; first-use pauses remain ([measurements](https://opentpw.io/docs/BROWSER-PERFORMANCE.html)).
- **Download:** [pre-release builds](https://github.com/meneerkrabs/OpenTPW/releases) for Windows
  (x64, x86, ARM64), macOS (Apple silicon) and Linux (x64, ARM64). Unpack and start OpenTPW; on the
  first start it asks for your game folder. Follow the [installation guide](https://opentpw.io/install.html)
  for your platform ([releases](https://opentpw.io/docs/RELEASES.html), [setup](https://opentpw.io/docs/SETUP.html)).
- **From source:** install the .NET 11 SDK pinned in `global.json`, then

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
  show riders' heads at the nodes the model's head records name, with the traced frame and roll; the
  head size, depth and slot stream remain qualified (HEADS-001, 004, 005), and coaster and tour cars do not show riders
  yet. Money, loans, wages, research, objectives, golden tickets and keys run on the original
  settings files ([guests](https://opentpw.io/docs/GUESTS.html), [economy](https://opentpw.io/docs/ECONOMY.html)). A hired staff member is
  placed with a click on the park, can be picked up and moved from the staff window, and is drawn with the original sprites
  ([staff placement](https://opentpw.io/docs/STAFF-PLACEMENT.html)).
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

Not done yet: the jumps and tunnels of the water and go-kart rides (the water rides and the go-karts of all four themes build, run and save their original tracks,
[phases 1 and 2](https://opentpw.io/docs/TRACKS.html)), rider heads on coaster and tour cars, tour-car sounds (the four tour rides board, fly and release riders, [tours](https://opentpw.io/docs/TOURS.html)), the particle effects of the other
callers (ride smoke, litter; RSE EVENT types 1 and 2 are drawn, [particles](https://opentpw.io/docs/PARTICLES.html)),
additional staff actions (the guards' chase and escort, entertainer shows) and their sounds, the path tool's link-end and undo sounds, and the exact original rules in places where
OpenTPW still uses a documented approximation. The
[completion plan](https://opentpw.io/docs/COMPLETION-PLAN.html), [feature matrix](https://opentpw.io/docs/FEATURE-MATRIX.html) and
[progress](https://opentpw.io/docs/PROGRESS.html) track what remains.

### File formats

The **Decoding** column only says whether the bytes of the selected original-asset corpus are
understood: ✅ means every observed file decodes and every field the original game reads is understood
(fields it is shown to skip, such as the MAP header words, may stay raw); ⚠️ means some bytes or field
meanings that matter are still unknown. Whether the game *uses* a decoded file
the way the original does (triggers, gameplay, rendering, mixing) is listed separately under
"Not yet in the game", because that is engine work rather than a missing decoder. Neither symbol
certifies other editions or original-runtime parity.

| Format | Decoding | What decodes | Still open in the format | Not yet in the game |
|---|---|---|---|---|
| Textures ([.WCT](https://opentpw.gu3.me/formats/wct.html)) | ✅ | Every texture of the selected game, with bounded inflation and the retail BILZ/LZSS quirks | Other editions are unverified | — |
| Settings ([.SAM](https://opentpw.gu3.me/formats/sam.html)) | ✅ | The line grammar of every shipped `.sam` file, loose and in WADs: one-token or quoted values, `#`/`//` comments and `---` blocks; the generic reader agrees with the object reader on all of them | — | How the original uses many keys is approximate |
| Sounds ([.SDT](https://opentpw.gu3.me/formats/sdt.html), .MP2) | ✅ | SDT containers, entry headers (type bits, bit depth, the playback length the game cuts each sound to) and sound catalogues; all audio: MPEG-1/2 Layer I and II in every observed mode (≤1 LSB against an independent decoder), CRC checks and de-emphasis | — (modes no shipped file uses (Layer III, MPEG-2.5, free format, MPEG-2 Layer II joint stereo, are refused) | Some triggers (positioned RSE EVENT types 1–2, staff actions, lobby effects, path link-end/undo) and the original mixer ([sound](https://opentpw.io/docs/AUDIO.html), [audio evidence](https://opentpw.io/docs/LIPS.html)) |
| Strings ([.BFMU](https://opentpw.gu3.me/formats/bfmu.html), [.BFST](https://opentpw.gu3.me/formats/bfst.html), [.BFUM](https://opentpw.gu3.me/formats/bfum.html)) | ✅ | 21 string tables per language; single-byte and two-byte (Polish, Korean) character tables, round-trip tested in six languages and read in nine | Other codepages/editions are unverified ([languages](https://opentpw.io/docs/LANGUAGES.html)) | — |
| Fonts ([.BF4](https://opentpw.gu3.me/formats/bf4.html)) | ✅ | All 33 fonts in six languages, and the Polish, Spanish and Korean (17,270-glyph) fonts (four-bit, RLE, monochrome) | — | Original text layout/appearance is unverified ([fonts](https://opentpw.io/docs/BF4-FONTS.html), [UI](https://opentpw.io/docs/UI.html)) |
| Lip Sync ([.LIP](https://opentpw.gu3.me/formats/lips.html)) | ✅ | Mark lists, signed conversion, talking toggles; checked against all 639 English global clips (638 end inside their audio) | — | Advisor animation/pose and a pausing clock ([lip sync](https://opentpw.io/docs/LIPS.html), [binary trace](https://opentpw.io/docs/reverse/APPROX-TRACE.html)) |
| Models ([.MD2](https://opentpw.gu3.me/formats/m3d2.html)) | ✅ | 2,116/2,118 WAD members (the two 207.201 members and the two loose 24.23 arrows are refused by the original loader too); all 1,278 animation members: position/rotation/scale, quantised vertex groups, the 12-byte pose block, texture-coordinate and texture-frame tracks, node-flag toggles, path parameters and the models' paths (Bézier, linear, Catmull-Rom) | — (slot bits 0x10/0x20 mean wrap, the rest clamp; 0x1/0x2 have no reader; [models](https://opentpw.io/docs/MD2-MODELS.html)) | The terrain's texture turn and mirror and the per-mesh culling boxes are not drawn; PC equivalence and rendering fidelity are unverified |
| Map Data ([.MAP](https://opentpw.gu3.me/formats/map.html)) | ✅ | All five TP2M terrain grids: grid mapping and every cell bit the game reads, as tp.exe's loader reads them (bit 0x04, the five header words and jungle `terrain.map` are never used by it); the sound-catalogue MAPs are a separate, decoded format | — | — |
| Ride Scripts ([.RSE](https://opentpw.gu3.me/formats/rsse.html)) | ⚠️ | All 308 scripts; all 84 used opcodes known with their operand counts | Operand meaning of the 51 opcodes that route to effect hooks is inferred ([VM](https://opentpw.io/docs/RSE-VM.html)) | The game systems behind those hooks |
| Save Files ([.TPWS](https://opentpw.gu3.me/formats/tpws-ints-lays.html)) | ⚠️ | The TPWI container's 19 writer-order blocks; cell grid, placed objects, park turn and date, bank, staff and advisor state | Guests, research, RNG state and the script/particle/sound blocks (SAVE-001–010); one fixture only ([payload](https://opentpw.io/docs/TPWS-PAYLOAD.html), [inventory](https://opentpw.io/docs/reverse/SAVE-plan.html)) | Original-format saves are read, never written; OpenTPW saves [its own format](https://opentpw.io/docs/PARK-SAVES.html) |
| Banner mesh companions ([.MTR](https://opentpw.gu3.me/formats/mtr.html)) | ⚠️ | Topology and the nine matrices of all 11 files | The scale `s`/`M7` offset: no reader of MTR files was found in either build of the game ([companions](https://opentpw.io/docs/MTR.html)) | — (unused by the original) |
| Video ([.TQI/.TGQ](https://opentpw.gu3.me/formats/tqi.html)) | ✅ | All nine movies; audio bit-exact; video through the PC game's own decoder (dequantisation matrix, transposed scan, transform passes, 7-bit samples and colour tables), matching the original routines run in the x86 oracle | — ([movies](https://opentpw.io/docs/TGQ-MOVIES.html)) | Game trigger points; `--play-movie` plays them |
| Theme Park Inc textures (.FSH, EA `SHPI`) | ⚠️ | All 7,283 `.fsh` files/WAD members to RGBA (`--inspect-fsh`) | Transparency of opaque 24-bit palettes is approximated ([FSH](https://opentpw.io/docs/FSH.html)) | — |

The experimental Theme Park Inc native slice (`[EXT:tpi-viewer]`, `--load-original-level
arabian|science|water`) is a game mode, not a format: it builds and runs guests with its own HUD,
terrain, paths, models and sound; economy, staff, campaign, advisor, saves and sharing are disabled,
and no original TPI parity is claimed ([slice](https://opentpw.io/docs/THEME-PARK-INC.html#theme-park-inc-playable-slice)).
The [format backlog](https://opentpw.io/docs/FORMAT-BACKLOG.html) lists each format's open questions.

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
   has an experimental native build/guest slice; its full campaign, economy and saves remain future work.
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
   `python3 tools/fidelity_register.py --check` validates the tags and their declarations;
   `python3 tools/fidelity_register.py --write` generates the register locally as
   `docs/FIDELITY-REGISTER.md`. That file is a build artifact: it is not committed, and the website
   publishes the copy generated from `main`. When an ID is retired, `--write` also records it in
   `tools/fidelity_retired.txt`, which is committed.
3. Describe your change for the progress log in a new file of its own,
   `docs/progress/YYYY-MM-DD-<slug>.md` (date and a short lowercase slug, for example
   `docs/progress/2026-10-16-buy-window-scroll-bar.md`). Its first line is the entry heading
   `## Title (Month D, YYYY)` with the same date; use `###` or deeper inside the entry, and write relative
   links as seen from `docs/progress/` (for example `../TRACKS.md`). Do not
   edit the top of `docs/PROGRESS.md`: it is the frozen history from before this convention.
   `python3 tools/progress.py --check` validates the fragments, and `python3 tools/progress.py`
   prints the assembled log (newest first) that the website publishes as the
   [progress notes](https://opentpw.io/docs/PROGRESS.html). One file per pull request means
   parallel pull requests never conflict over the log.
4. Run the tests (`dotnet test source/OpenTPW.Tests`; tests that need original files run when
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
