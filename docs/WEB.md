# OpenTPW in the browser

Status, October 10, 2026: the **front end, parks and sound run in the browser** over WebGL2 and WebAudio. The lobby with
its islands, the camera glide between them, the original UI (buttons, BF4 fonts, popup help)
and the options screen draw as on the desktop, and mouse and keyboard work. **Online play
works** against an OpenTPW server, which can also host the page itself (docs/SERVER.md):
register, log in and chat were checked in the browser. Starting a park works too: the Lost
Kingdom ran with its rides, guests, economy and build menu at about 97 frames per second in
the interpreter. Sound plays through WebAudio, and saves, options, online files and settings
stay in the browser between visits. Movies and the developer panels are not in the browser yet
(see the end of this page).

Nothing from the game is shipped or uploaded: the player chooses where their own game data
comes from (a CD image on archive.org, another address, a local CD image or a game folder; see
"Game data sources" below), and the page copies the files needed for the selected start into
the runtime's memory.

## Building and running

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -o out/web
```

Serve `out/web/wwwroot` over http (WebAssembly needs a server, not `file://`), open
`index.html` and choose the game folder (the one that holds `Data`). No extra .NET workload
is needed: this build runs .NET in interpreter mode. The published page (the server image) is
compiled ahead of time, which runs the game about twice as fast for about 5 MB more download
(docs/BROWSER-PERFORMANCE.md); for that build install the workload once and add the property:

```sh
dotnet workload install wasm-tools
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:RunAOTCompilation=true -o out/web
```

The browser build can also run on CoreCLR instead of Mono (`-p:UseMonoRuntime=false`, .NET 11 only, no workload needed). It
works, with the same texture and simulation hashes, but in .NET 11 it is interpreter-only and roughly 10 to 30 times slower than
the Mono AOT build. `node tools/web-coreclr/r2r.mjs` compiles a finished CoreCLR publish ahead of time with crossgen2 (about 38 fps
instead of 8 in Jungle, a 150 MB download after decompression); it is still about 2 to 7 times slower than Mono AOT. Both are opt-in
experiments, not what the published page uses (docs/BROWSER-PERFORMANCE.md, "CoreCLR on WebAssembly"). Delete
`source/OpenTPW.Web/obj` and `bin` when switching between runtimes, or the next publish fails to link.

The cross-runtime determinism check (docs/DETERMINISM.md, "Browser and native") publishes the page with the asset-free pin tests
compiled in and runs them in headless, muted Chrome. Delete `obj` and `bin` between an AOT and an interpreter publish here too: an
AOT publish strips the method bodies that the interpreter needs.

```sh
dotnet publish source/OpenTPW.Web/OpenTPW.Web.csproj -c Release -p:RunAOTCompilation=true -p:OpenTpwDeterminismPins=true -o out/web-pins
node tools/web-determinism/run.mjs out/web-pins/wwwroot
```

An OpenTPW server can serve the published `wwwroot` itself, beside its API
(`OpenTPW:WebClientDirectory`, docs/SERVER.md). The page then offers its own address as the
online server, and everything runs over one HTTPS address.

For local testing without the folder dialog, put a `files.txt` (one path per line, such as
`Data/ui.wad`) next to a copy or link of the game's `Data` folder, serve both from the same
server and open `index.html?data=<that folder's URL>`. A line may give the URL to fetch after
a tab, for servers that only serve known file types (the OpenTPW server does). Serve real
files, not symbolic links: ASP.NET reports a link's own size.

## Measurements (Apple Silicon, Chromium, local server)

| Check | Result |
| --- | --- |
| Published framework payload, Brotli-compressed | 11.8 MB for an interpreter build, 16.5 MB compiled ahead of time (the published page; 13 October); both include 7.7 MB of compressed, predecoded HD interface art (removed on October 18, 2026, so current builds are smaller) |
| Game files copied at start | everything in `Data` except movies, start-up pictures and the theme folders below `levels` (with the speech and global sound banks) |
| Game files copied when a park starts | its theme folder, 122 files and 39 MB for the Lost Kingdom, most of it music |
| MP2 decoding in the interpreter | 60 to 130 times faster than real time; a 17.5 s music segment takes about 280 ms, a short stutter on the page's only thread |
| Front end frame rate | 119 frames per second (the display rate) at 2048×1536 pixels, interpreter mode |

## How it works

### Two projects

- **`source/OpenTPW.Web`** compiles the game's own source files (OpenTPW, OpenTPW.Common,
  OpenTPW.Files) for `browser-wasm`. Files that only make sense on the desktop are left out
  or replaced by browser versions in `Browser/`:

  | Desktop file | In the browser |
  | --- | --- |
  | `Render/ShaderCompiler.cs` (native SPIR-V compiler) | `Browser/ShaderCompiler.cs` reads the precompiled shaders |
  | `Common/Client/SdlDisplay.cs` (SDL display queries) | `Browser/SdlDisplay.cs`: canvas size times the device pixel ratio |
  | ImGui developer panels, the editor, SDL audio | `Browser/DeveloperPanels.cs`, `Browser/DesktopOnly.cs`: silent stand-ins |
  | `Program.cs`, `Client/Game.cs` (command line, setup window) | `Program.cs`: `AddFile`, `Start` and `Frame` for the page |

  The legacy `Settings.Designer.cs`, `Client/GameDir.cs`, `Client/LegacySettings.cs` and unused
  `UI/Cursor.cs` are also excluded. The browser's `Browser/LegacySettings.cs` always returns no
  legacy game path, so shared audio and game-files callers use their existing fallback without
  loading `System.Configuration.ConfigurationManager`. `GameDir.GetPath` was only called by that
  old cursor; the active park cursor uses `ParkHud.Cursor.cs`. Desktop setup keeps its own settings.

  Canonical Unicode normalization remains in .NET: object-name matching uses FormD to remove
  accents and punctuation, and mod path keys use FormC. These forms work in browser .NET; unlike
  the online NFKC identity rule, they must not fold compatibility characters such as ligatures.
  `tools/web-postcard-smoke.mjs` exercises both exports in the published WASM runtime, twice and
  offline after boot, with synthetic object names and no original assets. It checks the settings
  boundary, split-name matching, mod path keys, postcard identity and unchanged persistent files.
  This is focused platform coverage, not a claim that every shared desktop API is browser-safe.

  The desktop code itself only skips two things in the browser: watching shader files and
  colouring console output. The browser has no `graphics.json`, so it uses the default Original
  world textures; a locally built texture pack does not exist there (docs/TEXTURE-PACKS.md).

  Until October 18, 2026 the browser build also embedded a predecoded copy of the redrawn HD interface art
  (about 7.7 MB compressed; see the measurements above). That art and its build-time generator were removed.

- **`source/OpenTPW.Web.Veldrid`** implements the Veldrid API the game uses over WebGL2. The
  description structs and enums have Veldrid's names, fields and values (the enums are
  generated from the Veldrid assemblies), so game code compiles unchanged. Textures, buffers,
  framebuffers, samplers and pipelines live in `wwwroot/opentpw-gl.js` behind integer
  handles. A `CommandList` records draws and buffer updates as integers and bytes and replays
  them in JavaScript at `SubmitCommands`, one interop call per submit, so updates keep their
  order relative to draws as in Veldrid. The canvas plays the SDL window: `Sdl2Window`
  turns its mouse, wheel and keyboard events into an `InputSnapshot`. A page with no pointer
  over the canvas, before its first pointer event as after the pointer left it, reports the
  pointer at (-1, -1), outside the window (`[EXT:browser-no-pointer]`): the park camera then
  takes no input, so its edge band does not scroll the view (nor do its keys), and the ride
  view's free look looks straight ahead. The original ran fullscreen, where the pointer cannot
  leave the screen; the desktop keeps SDL's positions.

`Renderer.Run` is split into `Start`, `Frame` and `Stop`. The desktop keeps looping over
`Frame`, and the page calls it from `requestAnimationFrame`.

A Veldrid call added on the desktop that the replacement lacks breaks the browser build at
compile time instead of failing at run time. `tools/AssemblySurface` lists what a compiled
assembly uses from another package:

```sh
dotnet run --project tools/AssemblySurface -- path/to/OpenTPW.dll Veldrid
```

### Game files on demand

The setup screen also offers an explicit **experimental Theme Park Inc** start in Arabian,
Science or Water. Choose the extracted installation folder containing `Data`, then choose
the theme before importing or pressing Play. That theme's complete browser-supported file
set is copied at startup. (The desktop build can open a Personnel File start screen instead of asking for the
zone: with `--front-end`, or a plain start with an explicit Theme Park Inc folder; the setup wizard still asks for a zone. See [THEME-PARK-INC.md](THEME-PARK-INC.md#theme-park-inc-front-end); the browser keeps
this up-front choice in phase 1, because the on-demand copy of a zone's files chosen later is
built for Theme Park World's lobby only.) The existing edition detector checks its identity or structure,
and an edition/theme mismatch refuses before renderer creation. Before deferral,
the complete source list retains the native detector's actual theme-directory witnesses.
A structural Theme Park Inc list refuses the default Theme Park World choice immediately.
Only observed directory names are preserved under the actual copied Data root before
detection; no marker files, media bytes or loaded-theme flags are created for them.
This also preserves mixed-source classification and the unchanged Standard.sam identity
precedence. Up to seven real witness filenames may remain as metadata in the existing
cache manifest when their media payload is excluded; they stay unread, unstored and
unhashed. Secondary planning and cache reopening retain those full-source witnesses. No Theme Park World lobby
is opened for Theme Park Inc. The browser calls the same `GameFlow.StartLevel` route as
desktop `--load-original-level`, including its existing shipped-layout eligibility.
The experimental HUD and capability policy are the desktop slice's: building, guests and money (cash, loans, costs), no
staff, research, challenges, missions, original game save writing, tickets or sharing. OpenTPW's own
layout checkpoint is separate from an original game save.

A failed setup import does not mark the selected theme loaded. Before a retry, only the
runtime-owned `/game` staging tree and import readiness/speech tracking are reset; kept
original files, `/save`, `/online` and `/config` are retained. Play requires every selected
theme entry to be kept; missing or size-mismatched kept data still goes through the existing
folder recovery reader. Already verified individual files can remain kept after a partial
import, but that does not make the theme ready. Once a renderer has been created, choosing
another installation or retrying startup requires a page reload, including after a later
startup exception; this route does not dispose/recreate an active world.

`BrowserStartupTests`, `test_web_startup.mjs` and the originals-store tests cover synthetic
edition refusal, staging isolation, explicit dispatch, complete theme import, retry and
cache reopening. Independent review also executes the actual page module and registered
folder handler with synthetic DOM and runtime-export stubs, covering selected-theme dispatch,
partial-copy retry, refusal before cache work and the unchanged TPW lobby route. The browser
project compiles this route. These are CPU and handle-stub
controls: no fresh original-data, actual browser gameplay, WebGL rendering or original-game
parity qualification is claimed. The existing deferred Theme Park World park-copy failure
behavior is outside this initial Theme Park Inc import path.

The page copies the files the front end needs when the player chooses the folder, and keeps
the theme folders below `levels` (about 10 MB each without sound) for later. Starting, loading
or visiting a park goes through `GameFlow.QueueLevel`, which waits until
`GameFlow.LevelDataReady` says the level is there; the browser answers by asking the page to
copy that theme's folder and shows *Loading the park* while the lobby keeps running. On the
desktop `LevelDataReady` is null and parks start at once. Each theme's saved park
(`Easymode.TPWI`) is copied at start, so the Load list is complete.

### Languages

A game folder copied from the CD keeps the other languages beside `Data`
(`Dutch/data/Language/Dutch`, `Dutch/Meshes/Dutch`, likewise Italian, Polish). For every
top-level folder that holds its own `data/Language/<same name>` (detected by structure, not a
fixed list) the page copies those two trees at once: about 1 to 3 MB per language, so the
Options -> OpenTPW -> Language toggle can list and switch them. Polish is offered now that its two-byte
character table is read (docs/LANGUAGES.md). The Spanish and Korean CDs keep their language in their own
complete `Data` folder (a `Spanish` or `Korean` folder beside it): a copy of such a CD as the game folder has
that language already; their use as an overlay beside an installed game, and the browser cost of the Korean fonts
(32 files of 0.5 to 1.8 MB each), are not measured in the browser build.

The advisor's voice is copied when the language is chosen, like a theme folder: the language's
`data/global/speech` (`speechHD.SDT`, 27.5 MB on the real Dutch folder, and `lips.WAD`) and each
level's `speech` folder (0.6 MB), all together so bank and lip marks never mismatch.
`GameFlow.LanguageDataReady` (the counterpart of `LevelDataReady`) asks the page through
`Page.RequestLanguage`; the page shows *Loading the language* and calls `Program.LanguageLoaded`.
A switch in the options waits for it (the choice stays pending, the text changes together with
the voice), a park does not start before the current language's speech is in, and a saved
language is requested at start-up. The cost of one language is that download (the local copy of
27.5 MB plus a short-lived second copy while it is written, about 56 MB more JavaScript heap at
the peak in Chrome), kept in memory for the visit. The installed language needs no request.

A language counts as loaded only when every one of its files is in memory. When a read or a
write fails (a file the page cannot read, a 404 in the `?data=` test mode), the page logs the
error, shows *Could not load the language speech* for ten seconds and reports
`Program.LanguageFailed`; the language stays unready. Text and speech must stay consistent, so
the game does not apply the switch: the pending choice is dropped with a warning (the options
row shows the current language again) and the current language, text and voice, stays. Nothing
waits forever, and a park queued meanwhile starts at once. Choosing the language again copies
again, and only the files that are still missing (the page remembers what it wrote). A pending
choice also holds a park that is started right after it (the park waits for the speech, then the
transition applies the language before the park is built), so the park is entered in the chosen
language. A saved language whose speech cannot be copied at start-up is not used: the game
starts in the default language (the saved choice stays in `language.json`, so the next visit
tries again).

At start the game reads the saved choice from `/config/language.json` (kept in browser storage
like the other settings), offers the CD languages with `LanguageStartup` (the code the desktop
uses) and, when the saved language is no longer in the folder, warns and uses the default.

### Sound

`Browser/WebAudioOutput.cs` answers to the desktop's `SdlMovieAudioOutput` name, so the game's
mixer, speech and movie code use it unchanged. It queues 16-bit stereo PCM to an AudioWorklet
(`opentpw-audio-worklet.js`) that plays it and reports the frames it has taken; that count is
the clock for the mixer and the advisor's lip sync. Browsers start audio only after a click or
key press on the page.

### Game data sources

**OpenTPW ships no game data.** The start screen offers four ways to bring one's own copy of Theme
Park World, and nothing is fetched or read before the player clicks:

- **Load from archive.org** (the visible default, one click). The item
  [`tpworld_202508`](https://archive.org/details/tpworld_202508), "Theme Park World (PC
  ITA/ENG/DUT/POL)", is a **third-party upload** that the player chooses to load; OpenTPW does not host,
  mirror or vouch for it. The page streams the item's CD image once (`TPWORLD.bin`, 712,674,816
  bytes) and keeps only the game's data files (935 files, about 322 MB: everything
  `opentpw-startup.js` plans to copy, for English, Dutch, Italian and Polish). Afterwards they stay in the
  browser (the next visit shows **Play**; no download). **Only data files are read.** The upload
  also contains a patched executable; the page never requests, reads or mentions it, and no
  executable from any image is read (the image's other files are skipped, and the CD's own
  programs are not planned for copying).
- **Another address.** The address of a `.zip`, `.iso` or `.bin`; the same pipeline. The server must
  answer cross-origin requests (CORS), and a page served by an OpenTPW server only connects to archive.org
  (its content security policy, docs/SERVER.md), so other addresses work on a copy of the page served
  without that policy; otherwise download the file and choose it. `?source=<address>` on the page's own address fills the box
  and opens the alternatives, without starting anything.
- **A local CD image**: an `.iso`, a `.bin` (select its `.cue` with it: a leading audio track is
  refused) or a `.zip` holding one, chosen with the button or dropped on the page. It is read in
  place and never uploaded.
- **The game folder** (an installed game or the CD's contents), as before.

#### The pipeline (`opentpw-cdimage.js`)

The image is never held in memory and never stored whole. Bytes stream in (a `fetch` body, or a
`Blob` slice) and pass through four steps, each tested with synthetic fixtures
(`tools/test_web_cdimage.mjs`):

1. **ZIP.** A local ZIP and a ZIP address whose server answers ranges (and allows the `Range`
   header cross-origin) are read from the central directory, so only the image's own bytes are
   requested; any other member (the upload's patched executable among them) is never requested.
   Otherwise one streamed request is parsed local header by local header: other members are skipped by
   their compressed size, never inflated, and the image is inflated with `DecompressionStream('deflate-raw')`.
   Stored and deflate are supported; ZIP64 and streamed sizes (flag bit 3) are refused with advice.
2. **Sectors.** The layout comes from the first bytes: a raw dump starts with the 12-byte sync
   pattern, and its mode byte gives the user data's offset (Mode 1: 16, Mode 2 form 1: 24); anything
   else is a plain 2048-byte ISO. A raw sector without sync is not data (copy-protected and unreadable
   sectors are dumped like that, outside the files) and is only an error for a wanted file that lies on it.
3. **ISO 9660.** The directory tree is read as soon as its sectors have arrived (the first
   few hundred sectors; the page buffers at most 32 MB while looking), preferring the Joliet tree (mixed-case
   long names; the plain names are upper case) and dropping `;1` version suffixes. The same
   `plan()` that chooses files from a folder chooses the files to keep, so the result is exactly what a
   folder would have given.
4. **Keeping.** `begin` writes the manifest (same format, so the next visit needs nothing else), then each
   wanted file is handed to the store the moment its last sector has passed (at most one file, 32 MB
   at most, is in memory). The stream stops after the last wanted file. A local `.iso`/`.bin` is read at
   random and touches only the wanted sectors; a ranged address skips long stretches (8 MB or more) of unwanted data (movies)
   with a `Range` request, and a server that ignores ranges is simply read through.

The page asks for storage that survives pressure (`navigator.storage.persist()`) and checks
`navigator.storage.estimate()` against the files' size before writing. **Cancel** stops at once
and keeps what is written; a failed or cancelled load can be started again and skips every file that is
already kept (the data itself is fetched again: the default source cannot resume, see below).
Errors are told apart: a network or CORS failure, an HTTP status, a full disk or quota, and an image
problem (not a CD image, no `Data` folder, truncated, damaged sectors). It needs writable OPFS streams
(`createWritable`) and, for a ZIP, `DecompressionStream`; a browser without them gets an explanation and the
folder choice. Only Chrome was exercised end to end (the other engines are covered by the unit tests' synthetic
streams only), and dropping a file on the page is not part of the smoke check.

#### What the archive.org item is (measured on October 27, 2026)

- `TPWORLD.zip` is 587,693,074 bytes with three members: a directory with a patched executable (deflate,
  1.5 MB), `TPWORLD.bin` (deflate, 712,674,816 bytes) and `TPWORLD.cue` (deflate, 316 bytes). The cue (Aaru
  5.3.2.0) lists one track, `MODE1/2352`: 303,008 raw sectors, no audio track; the user data is bytes 16 to 2063 of each.
- The disc is plain ISO 9660 with a Joliet tree, 2,651 files. `Data` is the English game (its language text
  and speech are in `Data/Language/English` and `Data/global/Speech`); `Dutch`, `Italian` and `Polish` hold
  their own `data/language/<language>`, `data/global/speech` and `data/levels/<theme>/speech` folders,
  the layout the page already reads from an installed game or CD folder. There are no installer archives
  to unpack for the game data (the `Setup` folders hold installers that are not read). The game picks the
  language itself (Options, as with a folder); the page keeps all four languages' text and speech.
- 843 raw sectors (LBA 809 to 10436, in front of the first file) have no sync pattern: protection data
  outside every file, ignored as described above. All 935 kept files are byte-identical (SHA-256) to an
  existing copy of the same CD.
- `https://archive.org/download/tpworld_202508/TPWORLD.zip` redirects to a `dnNNN.ca.archive.org` host
  that answers `Range` (206) **without any CORS header**, so a page on another origin cannot read it.
  The per-member address `.../TPWORLD.zip/TPWORLD.bin` redirects to `view_archive.php` on an `iaNNN.us` host, which
  sends `access-control-allow-origin` (the caller's origin) but ignores `Range`, has no `Content-Length` and streams the
  extracted member (about 7 MB/s in a measurement, so about 100 s for the 713 MB image). That is what the page uses; the progress bar
  therefore knows the size from the item (a constant in `main.js`), and the host behind the redirect changes between requests
  (nothing about it is cached).
- A load that is interrupted starts the download again (the files it already wrote are kept and not written twice).

The end-to-end check, `tools/web-archive-smoke.mjs`, serves a published page and a local copy of an image on two
origins (with or without ranges and CORS), loads it in muted headless Chrome with a fresh profile, enters the Jungle
park from the kept files, and then starts a second visit from Play with no request to the image server. Measured on
October 27, 2026 with the real `TPWORLD.zip` (and its `TPWORLD.bin`): ZIP with ranges, 9 s, three ranged requests, none inside the
executable's bytes; ZIP without ranges, 7 s, one request; the file chooser, 6 s; raw BIN without ranges, 9 s; raw BIN with
ranges, 8 s and three ranged jumps over the unwanted stretches (a server without ranges costs one cancelled probe). All kept 935 of 935 files (322,175,733 bytes). `--probe-archive`
checks the real archive.org stream in the page (CORS, redirect, progress) and cancels after 15 MB.

### Game files kept in the browser

The page can keep the player's own game files in the browser's origin-private file system
(OPFS, `opentpw-originals.js`), so a later visit starts with **Play** instead of choosing the
folder. The files stay on that device; OPFS is never uploaded and no other site can read it. The
setup screen chooses how much is kept, and remembers the choice:

- **what the parks use** (the default): the files the start copies, and each theme folder and
  language speech once the game has read it;
- **all at once**: when the folder is chosen, every file the page uses (Data without movies and
  start-up pictures, plus the other languages' text, banners and speech; about 300 MB for an English
  CD with three other languages), before the game starts;
- **no**: nothing; every visit asks for the folder, as before.

A manifest lists every file the page uses with its size and SHA-256, kept or not, so a later visit
rebuilds the file list; a park whose theme was not kept yet asks for the folder once more and keeps
it then. That folder is accepted only when it has the file, every kept file has the same size in it
and up to eight kept files have the same bytes (SHA-256); otherwise it is refused with a message and
can be chosen again. A folder
chosen on the setup screen is read directly and every kept file it meets is compared by its SHA-256;
any difference, whenever it shows, means another installation: everything kept is removed at once
(so files of two installations never mix, even if the start then fails) and the folder's start files
are kept again; in the "all at once" mode a chosen folder is read completely, so every kept file is
compared. Each start attempt drops reads and writes of earlier ones. Limits: in the lazy mode files
the visit never reads are not compared, a folder asked for again is checked by sizes and eight hashes
rather than every byte, and a read that overlaps a replacement may simply not be kept. Writing waits until the game has started (the start's files wait in memory until then)
and runs in the background with at most 64 MB waiting afterwards (one larger file can exceed it);
the manifest is written every 25 files and whenever the queue empties. A page closed before that
keeps less, and the next visit asks for the folder for what is missing. Only one tab keeps files (a Web
Lock); a second tab plays without them. A game that stops on an error leaves its tab open on the message, so that tab writes the list of
what it kept and gives the lock back at once; a restart in a new tab then starts from the kept files instead of asking for the folder again. **Remove kept files** deletes them (saves stay). Browsers
without writable OPFS streams, or whose storage does not open, keep nothing and show no choice.

Content hashing succeeds before a kept file is changed. Replacing a previously kept path first
persists it as missing, then writes its new bytes and makes it available after close succeeds;
the next manifest flush publishes the new hash. A failed replacement can therefore temporarily
make that path unavailable until it is kept again, rather than expose new bytes under an old
persisted hash. Initial lazy writes retain their existing checkpoint schedule. Failed manifest
flushes remain retryable. [Cache recovery checks](BROWSER-ORIGINAL-CACHE-RECOVERY.md) use synthetic
storage; they do not establish atomic multi-file OPFS transactions or real-browser behavior.

### Mods in the browser

The browser runs the same data mods as the desktop (docs/MODS.md; never code). The mods OpenTPW ships (`content/mods/`, today
`opentpw.tpi-rides`, the Theme Park Inc rides mod) are embedded in the page like the other content: `OpenTPW.Web.csproj` adds
`content/mods/**` as resources, `Program.WriteContent` writes them below the program folder at start, and `ModStartup.InitializeBrowser`
runs the usual discovery over that folder (`ModSource.Program`, the only source of `opentpw.` ids) before it resolves the set with the
choices of `/config/mods.json`. The browser has no folders for community mods. The mods are off until switched on, exactly as on the desktop.

- **Settings.** `/config/mods.json` is one of the files the page keeps (the `/config` folder is mirrored into IndexedDB every five seconds
  and when the page is hidden, and put back before the game starts), so the choices of the **Extensions and mods** screen
  (Options > OpenTPW) survive a reload. Official extensions switched there apply to the next park started or loaded, as on the desktop (the page then fetches the pools sprite sets if
  pools came on); a change to a mod or its input applies at the next start, which in the browser means a reload ([MODS.md](MODS.md#when-a-change-applies)).
- **The `tpi` input.** A mod input is a folder of the player's own files on the desktop. The browser holds the files of one original game
  beside the Theme Park World files in `/game`: the page copies them into the runtime's memory below `/inputs/theme-park-inc/Data/...`
  (`Program.AddInputFile`, validated by `BrowserModInputs`: only `Data/...` paths without `..`, at most 20000 files and 256 MB) and a mod's
  `original-data` input with `"edition": "theme-park-inc"` is satisfied by that tree when `mods.json` names no other folder. Every mod
  that asks for Theme Park Inc data shares that one copy, and the check of the folder is the desktop's (`GameEditions.Detect`: the known
  `Standard.sam` or the folder structure). Theme Park World data cannot be a mod input in the browser (it is already the game), and a typed
  folder path means nothing there.
- **Choosing the folder.** The setup page has a second folder button, *Theme Park Inc files for mods*, before or after the Theme Park World
  folder (`opentpw-inputs.js`). It takes the install or its `Data` folder and copies only what the mod reads: the object archives of the
  three worlds (`rides`, `shops`, `sideshow`, `features`, `upgrades` and `facilities`), `sharetex.wad` and `ssharete.wad`, the worlds' `Sound`
  folders, `global/sound`, `Ver.txt` and `levels/Standard.sam` (the identity of the install), 324 files and 74 MB of the retail install's 400
  MB; music, terrain, speech, movies and the interface stay where they are. A folder is accepted only as a complete Theme Park Inc install: `Ver.txt`, per world a
  ride archive and the shared textures, and the global sounds must be there. Theme Park World data, a mixed folder, or a folder that lacks any
  of that is refused with a message; the game repeats the check on the copy (`BrowserModInputs.Publish`, with `GameEditions.Detect`). The Extensions and mods screen's **Change...** button opens the same dialog from inside the game (the page clicks its hidden file input; browsers
  only open the dialog shortly after a player's click, which has not been tried against a real dialog). While the game runs the copy is
  slow (about four files a second in the interpreter build, because the busy page serves each file between frames), so the setup page is the
  better place; the copy goes to a staging tree and becomes the live one (with a completion marker) only when every file is in and the check
  passed, so a mod never sees a half-copied install; a failed or refused copy removes the staged and live trees, **Play** waits for a
  copy in progress, and every outcome, refusals included, tells the game's screen exactly once. A copy that an active mod already reads is never replaced during a run: the screen says to reload the page first
  (`BrowserModInputs.LockInUse`), since the catalogs read their archives lazily.
- **Storage.** With *Keep the game files in this browser* on (the default, and when the browser has writable OPFS), the chosen files are kept in a
  second origin-private store, `opentpw-input-theme-park-inc`, with its own manifest (`openOriginalStore(root, { name })`): the Theme Park World
  files and the Theme Park Inc files never share a manifest, so choosing a Theme Park Inc folder never clears the kept Theme Park World
  files and the other way round. A later visit restores the complete set into memory before the mods are resolved: every file must be listed as kept and
  match its stored SHA-256 before anything is copied, and a store that is incomplete, has an invalid manifest or fails a check is cleared
  (never half used, and no longer shown as kept), so **Play** starts the mod without asking for either folder again. The Theme Park World and Theme
  Park Inc stores together hold about 100 MB for the start files plus what the parks read, and 74 MB are in the runtime's memory for the visit.
  With *no*, or a browser that cannot keep files, the Theme Park Inc folder must be chosen on the setup page every visit before the game starts.
  The setup page's *Remove* deletes the kept Theme Park Inc files (and, before the game starts, the copy in memory); *Remove kept files* removes
  only the Theme Park World files.
- **Limits.** One Theme Park Inc copy per browser origin; one tab keeps files (the same Web Lock as for the game files); the Theme Park Inc
  files count against the browser's storage quota; memory-limited devices pay for the 74 MB twice while the files are copied. The copied files are
  the retail install's: another edition of Theme Park Inc data would need its own allowlist (`opentpw-inputs.js`).

`tools/test_web_inputs.mjs` covers the page side with synthetic files and a fake origin-private file system (what is chosen, replacing an
install, keeping, restoring a complete set only); `ModsInBrowserTests` (desktop test run of the shared game code) covers discovery of
the shipped mod, the defaults and the lock of the browser inputs, and the Extensions and mods screen in browser mode with the page played by the test.
Checked once by hand in Chrome (DevTools, synthetic folder objects instead of the dialog) against a published interpreter build served from a local server: the Theme Park Inc files copied from the
real install (324 files, 74 MB) before and during the game, kept and restored after a reload, the mod listed and saved by the screen (`mods.json`
with `/inputs/theme-park-inc`), and the Jungle park's buy list showing the mod's Arabian objects (18 rides, 7 shops, 6 sideshows, 13 features) with the mod on.
The real folder dialog, the screen's drawing and gameplay with the mod's objects placed were not checked in the browser.

### The pools sprite sets

The pools extension's 2x guest and swimwear sprites (`content/pools/esprites`, 8.84 MB) are not in the startup download. The build embeds only the pinned manifest `content/pools/esprites.manifest.json`; the files are static files of the site below `pools-sprites/` (the web project links them into `wwwroot` as `Content`; the server image copies `content/pools`), and `wwwroot/opentpw-pool-sprites.js` fetches them in the background after the game has started while the extension is on. `Program.AddPoolSpriteFile` stores a file in the in-memory file system only when its path, size and SHA-256 are the pinned ones; a set is used only when complete, and offline or a 404 leaves the fallbacks (details: [POOLS.md](POOLS.md#content-extpools-content)). The files are not part of the saves or the options storage, and the browser's HTTP cache keeps them between visits.

### Saves and settings

The display choices the browser honours (render scale, interface scale and the frame rate limit)
are kept in `/config/display.json` like the desktop's; the canvas decides the size, so a stored
window size or mode has no effect.

The game writes saves and options to `/save`, online files to `/online` and display and graphics
settings to `/config` (`OPENTPW_CONFIG_DIR`). `opentpw-storage.js` mirrors those folders into
IndexedDB every five seconds and when the page is hidden or closed, and puts them back before the
game starts. Whole-park saves (`opentpw-<theme>-world.json`, docs/PARK-SAVES.md) live in `/save`
too: a park saved in the browser is still on the Load screen after a page or runtime reload, and
resuming it asks for the game folder to be chosen again, because the original files are not kept.
The periodic autosave (`[EXT:periodic-autosave]`, every 5 minutes of play, on by default) writes
`opentpw-<theme>-autosave-world.json` into the same folder, so the mirror carries it to IndexedDB with
the next five-second sync, and a crashed or closed tab offers "Continue autosave" in the theme menu on the
next visit once the game folder is chosen again. The page's only thread does the work: the capture is one
short frame and the JSON is serialized in slices of 2 ms per frame (`SlicedJsonSerializer`), the write goes
inline into the in-memory folder. A browser killed inside the five-second window loses the autosave of that
window (the previous one stays); the page mirrors again when it is hidden, which is when a tab is usually
closed. Native tests compile and run the same code path; a real browser run of the autosave is not done here.
The restart snapshot and its `.pending.json` recovery marker also live in `/save` and are mirrored together by the same IndexedDB transaction. While restart JSON is being serialized, a marker prevents an earlier park's restart file from being treated as the new entry state. Once the final JSON exists, its digest in the marker permits recovery after replacement even if marker cleanup was interrupted (see [PARK-SAVES.md](PARK-SAVES.md#restart-snapshot-persistence)). Browser copy/hash work runs in bounded 64 KiB steps after serialization; final writes remain in-memory. An abrupt page/process loss before a completed storage sync can still lose that window's changes. This does not add a separate durable write or a browser worker.

The player's game files are not uploaded.

### Shaders

All eleven shaders in `content/shaders` cross-compile to GLSL ES 3.00 through the existing
SPIR-V path (`ShaderTests.NativeShadersCrossCompileToWebGl2`), and every generated pair also
compiles and links in a real WebGL2 context. The native SPIR-V compiler does not run in the
browser, so the shaders are stored precompiled in `content/shaders/web/<name>.json` (vertex
and fragment source, vertex elements, resource layouts with their names), embedded in the
browser build. `WebShaderTests` fails when those files no longer match the shader sources;
after changing a shader, regenerate them with

```sh
OPENTPW_NATIVE_SHADER_TESTS=1 OPENTPW_WRITE_WEB_SHADERS=1 dotnet test source/OpenTPW.Tests -r osx-arm64 --filter WebShaderTests
```

CI does not run the native shader tests yet, so this check is local for now.

GLSL ES 3.00 has no `layout(binding = …)`. Pipelines therefore bind by position, as on Metal
and Direct3D, and look up each resource by its name from the shader's reflection: uniform
blocks and texture units are numbered in layout order, and a set's sampler applies to all
of that set's textures (SPIR-V-Cross names each combined sampler after its texture).

### Coordinates

OpenTPW renders with Metal and Direct3D conventions (depth 0..1, row 0 at the top). The web
shaders move depth to -1..1 (`fixClipSpaceZ`) and negate Y (`invertVertexOutputY`), so
everything WebGL renders lands in memory with row 0 at the top, as on the desktop.
Viewports, scissor rectangles, `gl_FragCoord` and texture uploads then need no conversion.
Two things change instead: the negated Y reverses the winding, so the browser swaps the
front face for culling, and the frame is flipped once when it is copied to the canvas
(`present` in `opentpw-gl.js`; every render target, the swapchain included, is an offscreen
framebuffer).

### Differences from WebGL2

- **BGRA**: WebGL2 has no BGRA8. `B8_G8_R8_A8_UNorm` is only used for render targets, so the
  browser stores it as RGBA8 (uploads to such a texture are swizzled anyway).
- **Multisampling**: the 4x world target is a multisampled renderbuffer, resolved into the
  sampled texture with `blitFramebuffer`.
- **Base vertex**: WebGL2 cannot offset indices; `DrawIndexed` offsets the attribute pointers.
- **`Map`/`Unmap` and `CopyTexture`**: only used to read back smoke-test and capture images on
  the desktop; the browser throws `NotSupportedException`.
- **Clicks and key taps within one frame**: the game reads button and key state once per
  frame. A press that starts and ends within one browser frame stays down for that frame and
  is released in the next. Held keys and buttons are released when the canvas loses focus.

### Pitfalls found

- **Trimming removes reflection targets.** The file system creates archive handlers by
  reflection; the browser project roots its own assembly (`TrimmerRootAssembly`), and turns
  reflection-based `System.Text.Json` back on for the settings files.
- **`dotnet.run()` exits the runtime** when `Main` returns; `runMain()` keeps the exports
  callable.
- **`Console.ForegroundColor` throws** in the browser.
- **The browser's HTTP responses only support asynchronous reads**; the online client reads
  them with `ReadAsync`.
- **Browsers cannot set WebSocket headers**, so the chat sends its session token as the first
  frame instead of an `Authorization` header (docs/SERVER.md).
- **The window asks for its size before the device exists**, so the page's canvas is looked up
  when `opentpw-gl.js` loads.

## What the browser build still lacks

- **Movies:** not copied or played yet; sound is in place (see Sound below).
- **Decoding off the frame:** music segments decode on the page's only thread; decoding them in
  pieces across frames would remove the short stutter (the AOT build decodes textures about four
  times faster; the music stutter itself has not been measured again).
- **Developer panels:** they need ImGui, which is a native library.
- **Typing without a keyboard event**: text arrives through key presses on the canvas, so
  input methods and on-screen keyboards that only insert text do not reach the game yet.

## Background: what the game uses from Veldrid

Measured before the replacement was written, from the member references in the compiled
`OpenTPW.dll` and `OpenTPW.Common.dll`:

| Package | References | Notes |
| --- | --- | --- |
| Veldrid | 186 (about 60 types) | 9 `ResourceFactory.Create*` kinds, 22 `CommandList` and 16 `GraphicsDevice` members, plus description structs and enums |
| Veldrid.SDL2 | 31 | window and display queries; input types (`InputSnapshot`, `Key`, `KeyEvent`, `MouseEvent`) |
| Veldrid.SPIRV | 16 | shader compilation and reflection, replaced by the precompiled files |
| Veldrid.ImGui | 6 | developer panels only |

A build of all game sources for `browser-wasm` without Veldrid, SDL2 or ImGui failed in 45
files, almost all of them on Veldrid types; outside Veldrid only ImGui and SDL were missing.
An OpenTPW device interface implemented twice would have rewritten about 40 desktop files,
so the browser got its own Veldrid instead.

The first spike (same day) ran only the file readers in the browser: map, terrain, a ride
model and a texture parsed unchanged in about 430 ms, from a 2.4 MB download.

## Performance

See [browser performance](BROWSER-PERFORMANCE.md) for measured CPU/frame costs,
render binding reuse, cooperative audio decoding and remaining park-load stalls. Its harness
(`tools/web-perf/run.mjs`) opens the page with `?perf=1`, which records the time of every
`Program.Frame` and exposes the measurement hooks of `Program.Perf.cs`; without it the page
behaves as before.

The co-op join smoke's browser runs (`tools/web-coop-join.mjs`, [COOP.md](COOP.md), "The browser's
join" and "The browser's host") open the page with `?coopSmoke=1` instead: the same harness object, but the game's co-op
smoke mode (`Program.CoopJoinSmokeTest.cs`) rather than the performance mode, whose hooks change parks
outside the commands, which a shared park refuses. Both switches work only on a loopback address, and
not together. The smoke's files go to `/coop-smoke`, which the page does not keep.

The co-op link check (`tools/web-coop-link.mjs`, [COOP.md](COOP.md), "The browser's link to a real
server") opens two pages of the real server with `?coopLinkSmoke=1`: the page then starts no game, reads
no game file and claims none of its stores, and exposes only the check's exports
(`Program.CoopLinkSmoke.cs`), which drive the game's own online client against the server that serves
the page. It too works only on a loopback address, and never with `?perf=1` or `?coopSmoke=1`; its
online folder is `/coop-link`, never the player's.
