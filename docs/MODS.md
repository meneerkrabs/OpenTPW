# Extensions and mods

OpenTPW has two kinds of add-ons, both labelled `[EXT:mods]` in the source (`source/OpenTPW/Mods/`):

- **Official extensions** are OpenTPW engine features that live in this repository and carry their own
  `[EXT]`/`[APPROX]` labels. They are compiled in, ship no files and can be switched off. Their ids start with
  `opentpw.`.
- **Mods** are folders of data with an `opentpw-mod.json` manifest. Community mods use ids that start with
  `community.`; `opentpw.` ids are reserved for data-only content shipped with OpenTPW. The pools extension's own content is a catalog root of the id `opentpw.pools` (`content/pools`, Info.Ids 65500 to 65519 reserved; [POOLS.md](POOLS.md#content-extpools-content)), listed only while that extension is on.

Mods are **data, plus sandboxed TPW Script**. A mod may ship data files, and scripts (see
[SCRIPT-MODS.md](SCRIPT-MODS.md)) in *TPW Script*, a small deterministic language with its own interpreter inside
OpenTPW. OpenTPW **never loads native code or assemblies from a mod**: .NET has no in-process security boundary, the browser
build is trimmed and compiled ahead of time (so there is nothing to load), and third-party code would break the save and
determinism guarantees. A script cannot reach files, the network, the clock or any host object: it only sees what a hook
passes it, under a step budget, and it can only change the simulation through a typed hook result. Engine features a mod
needs that scripts cannot express become official extensions instead.

New to modding? Start with the [modding guide](MODDING-GUIDE.md): folder layout, a minimal manifest, where to put a mod, and examples.

In code the system is called `OpenTPW.Mods` / "extensions"; `OpenTPW.ModKit` is the unrelated asset editor.

## Current state

- manifests are parsed and validated, mod folders are discovered, `mods.json` is read and the run's set of extensions
  and mods is resolved once at startup;
- the official extensions are `opentpw.online`, `opentpw.cross-edition-objects`, `opentpw.smarter-guests`, `opentpw.guest-insights` and `opentpw.restored-advice` (below), `opentpw.winter` (on by default, recorded in a save only while the park has a winter kiosk, a glühwein stall or a guest with a warm item; [WINTER.md](WINTER.md)), `opentpw.winter-weather` (off by default, recorded in every save while on: December's rain and thunderstorms are snow in the jungle and fantasy parks; [WINTER.md](WINTER.md#simulation-phase-w3)), `opentpw.winter-always` (off by default, recorded in every save while on: every month rolls December's weather and its rain and thunderstorms are snow, and the winter shops sell at their winter demand all year; [WINTER.md](WINTER.md#always-winter-extwinter-always)), `opentpw.evening` (on by default: a day and night cycle with lamps that light at dusk, presentation only, and a buildable torch per theme; recorded in a save only while the park has a torch; [EVENING.md](EVENING.md)), `opentpw.winter-snow` (presentation only, on by default, not in a Theme Park Inc run: snow settles on the ground, roofs and objects while it snows; [WINTER.md](WINTER.md)), `opentpw.pools-guest-art` (presentation only, on by default: with the pools extension on, land guests use OpenTPW's own 2x kids, off shows the original guests; [POOLS.md](POOLS.md)), `opentpw.rain-mood`, off by default (not in a Theme Park Inc run; [UMBRELLAS.md](UMBRELLAS.md#the-rain-mood-rule-phase-u4): guests lose or gain happiness, thirst and hunger in rain, storm and snow once per qualifying day, and an open umbrella softens the loss; recorded in every save while it is on), `opentpw.umbrellas`, on by default (not in a Theme Park Inc run; [UMBRELLAS.md](UMBRELLAS.md): the brolly shops and the umbrella rules, recorded in a save only while the park has a shop or an umbrella), and `opentpw.pools`, on by default (not in a Theme Park Inc run), in which the player paints
  pool water cells (see [POOLS.md](POOLS.md)); a park save records `opentpw.pools` only when the park has pool cells;
- park saves record the mods that change the simulation and refuse a mismatched set (below);
- the **Extensions and mods screen** (below) edits `mods.json`;
- **content roots** are live on the desktop: an enabled mod's `overrides`, `strings` and `objects` change what the game
  reads (see [Content](#content)). **Script mods run on the desktop**: their four hooks are wired into the guest simulation (script mods phase 4, [SCRIPT-MODS.md](SCRIPT-MODS.md)), and the shipped example `opentpw.example.patient-guests` works. The browser build runs the same data mods shipped with the page (see
  [Mods in the browser](WEB.md#mods-in-the-browser)) but does not run script mods yet (the AOT check, phase 5, is still to do);
- **Theme Park Inc attractions in Theme Park World** work end to end through a data mod and the cross-edition objects
  extension: they are listed, priced, researched, bought, placed, run and saved. OpenTPW ships that mod itself,
  `opentpw.tpi-rides`, off by default, on the desktop and in the browser (see
  [The shipped Theme Park Inc rides mod](#the-shipped-theme-park-inc-rides-mod); to write your own, see
  [Making a Theme Park Inc rides mod](#making-a-theme-park-inc-rides-mod)).

Official extensions switched on or off apply at the next park start or load, without a restart; community mods, their inputs and a few extensions need the next start (see
[When a change applies](#when-a-change-applies)). Nothing is swapped while a park simulates.

## Defaults of the official extensions

| Extension | On by default | Selectable | Changes the simulation | What it is |
| --- | --- | --- | --- | --- |
| `opentpw.online` | yes | yes | no | [Online play](ONLINE.md); no network request until the player acts |
| `opentpw.pools` | **yes** | yes | yes (recorded in a save only while the park has pool cells or a changing room) | [Swimming pools](POOLS.md); not in a Theme Park Inc run |
| `opentpw.pools-guest-art` | yes | yes | no | OpenTPW's own 2x kid and head sprites for land guests while pools is on; switch it off for the original guests |
| `opentpw.umbrellas` | **yes** | yes | yes (recorded in a save only while the park has a brolly shop or a guest with an umbrella) | [Umbrellas](UMBRELLAS.md): a brolly shop per theme and guests who carry an open umbrella in the rain; not in a Theme Park Inc run; simulation only until its art lands |
| `opentpw.rain-mood` | no | yes | yes (recorded in every save while it is on) | [Rain mood](UMBRELLAS.md#the-rain-mood-rule-phase-u4): guests lose or gain happiness, thirst and hunger in rain, storm and snow, once per qualifying day; an open umbrella softens the loss; not in a Theme Park Inc run |
| `opentpw.winter` | **yes** | yes | yes (recorded in a save only while the park has a winter kiosk, a glühwein stall or a guest with a warm item) | [Winter](WINTER.md#simulation-phase-w3): a winter kiosk (hat or earmuffs, then mittens; sells all year, demand by season and snow) and a glühwein stall (a drinks shop with no drunk effects) per theme; not in a Theme Park Inc run; the items are drawn from phase W4 |
| `opentpw.winter-weather` | no | yes | yes (recorded in every save while it is on) | [Wintry Decembers](WINTER.md#simulation-phase-w3): a December weather roll in the jungle and fantasy parks is snow instead of a thunderstorm; what umbrellas and the rain mood read changes with it; not in a Theme Park Inc run |
| `opentpw.winter-always` | no | yes | yes (recorded in every save while it is on) | [Always winter](WINTER.md#always-winter-extwinter-always): every month rolls December's weather and its rain and thunderstorms are snow (jungle, fantasy; Halloween snows by its own band; space is left alone), and the winter kiosk and the glühwein stall sell at their winter demand all year; implies Wintry Decembers; not in a Theme Park Inc run |
| `opentpw.restored-advice` | yes | yes | no | advisor lines in the shipped data that the original never plays |
| `opentpw.evening` | **yes** | yes | yes (recorded in a save only while the park has a torch) | [Evening and night](EVENING.md): a day and night cycle on play time with lamps that light at dusk (presentation only), and a [torch](EVENING.md#torches) per theme in the Features tab |
| `opentpw.winter-snow` | **yes** | yes | no | [Settling snow](WINTER.md#settling-snow-extwinter-snow): snow settles on ground, roofs and objects while it snows and melts afterwards, paths turn to slush; presentation only; not in a Theme Park Inc run |
| `opentpw.guest-insights` | no | yes | no | refusal counters in the park information window |
| `opentpw.cross-edition-objects` | no | yes | yes | engine support for mods that bring Theme Park Inc attractions into Theme Park World |
| `opentpw.smarter-guests` | no | **no, not yet** | yes | guests that decide better; selectable once phase 6 shows an effect |
| `opentpw.cheats` | no | yes | yes (recorded in a save only once the park has used one of its own cheats) | [Cheats](CHEATS.md): the Alt+C cheats window (arrivals by vehicle, the weather, a chosen golden ticket, money and research, a ride's vehicle limit); Theme Park World parks only. The original's own cheat mode (the secret park name and the Alt keys) is not part of it |

Mods are off until switched on. Switch an extension off (or a mod on) for one run with `--disable-extension <id>` and `--enable-mod <id>`, or
permanently on the Extensions and mods screen or in `mods.json` ([below](#settings-modsjson)).

## Quick start: your first data mod

The [modding guide](MODDING-GUIDE.md) walks through a first mod: the folder layout, a minimal manifest, where to put the folder on
Windows, macOS and Linux, switching it on, a data override and a script.

## The Extensions and mods screen

Options > OpenTPW > **Extensions and mods** (`UI/Original/Options/ModsScreen.cs`, `[EXT:mods-ui]`) is the in-game way to edit
`mods.json`. It uses the original options frame (`f_screen`) with two `f_optpanel` columns, **OpenTPW extensions** and
**Community mods**, each a scrolling list. The selected row of a column is switched on or off with that column's original
`b_on` switch (or by activating the row). Each row shows the name, the version and a status:

| Status | Meaning |
| --- | --- |
| On / Off | Switched on and accepted, or switched off. |
| Incompatible | For another OpenTPW version or the other game edition, or `objects` in a Theme Park Inc run. |
| Missing input folder | An `inputs` folder is not chosen or no longer exists, or the `objects.json` reads from an input that is not a game Data folder. |
| Conflict | Another enabled mod provides the same file or object archive, or reserves an overlapping `Info.Id` range, and neither lists the other in `after`. |
| Needs another mod | A dependency is missing, off, refused or out of range, or is part of a cycle. |
| Cannot be used | The folder has no valid manifest, is shadowed by the same id elsewhere, its files cannot be read, or its `objects.json` or string tables are invalid. |

The statuses are not a second rule set: the screen resolves the real `ModSet` with the pending choices (away from the
game's thread) and words the refusal it gets (`ModRefusal.Kind`). The details pane under the lists shows the selected
row's author, version, description, content digest (once the mod is on and accepted), dependencies, folder and the reason
it is refused. A mod folder that cannot be read has no switch.

A mod with an `original-data` input shows an input row below the details: the chosen folder, **Change...** (the platform
folder dialog of the Game files screen, or a typed path where there is none), **Enter path...**, **Remove** and, for a
mod with several inputs, **Next input**. A chosen folder is inspected by the same helper process as Game files, and
`GameEditions.Detect` (which uses `GameEditions.HasKnownStandardSam` for the known retail Theme Park Inc `Standard.sam`, else the
folder structure) must find the edition the input asks for; the result is shown ("Theme Park Inc data found (known retail
Standard.sam)", "This folder holds Theme Park World data, not Theme Park Inc", "No game data folder found here"). A folder of
the wrong edition is not saved. Saved folders go to `inputs` in `mods.json`.

**OK** writes `mods.json` (only when a choice changed) and says when the choices apply; **Cancel** and Escape discard the edits. When only official extensions changed, they apply without a
restart (`[EXT:extensions-apply-at-load]`): from the lobby at once, so the next park you start or load uses them ("The changes apply to the next park you start or load. No restart is needed."), and inside a
park when you leave it or load another park ("The changes apply when you leave this park or load another one."). A change to a community mod or its input, and the cases in
[When a change applies](#when-a-change-applies), show the restart notice instead. The screen's own texts are in the eleven interface languages (`OpenTpwText`, `SupplementaryStrings`). The name and description of five extensions (smarter guests, guest insights, pool guest art, restored advice, evening and night) are translated too; the other extensions (online, cross-edition objects, pools) and every mod show the name and description from their code or manifest, which are English. In the
browser build the community list holds the mods shipped with the page, and an input is the copy of your own game files that the
page keeps in the browser: **Change...** opens the browser's folder dialog (a typed path or **Remove** would mean nothing there), and
the copy is replaced only before a mod reads it (see [Mods in the browser](WEB.md#mods-in-the-browser)).

## Manifest

Each mod folder holds `opentpw-mod.json`:

```json
{ "schema": 1, "id": "community.tpi-rides", "name": "Theme Park Inc rides", "version": "1.0.0",
  "category": "community", "openTpw": ">=0.8.0", "edition": "theme-park-world",
  "author": "Ann", "description": "Optional, up to 1000 characters.",
  "dependencies": [{ "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" }],
  "after": ["community.other-rides"],
  "inputs": [{ "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" }],
  "scriptApi": ">=1.0.0 <2.0.0",
  "content": { "overrides": "data/", "objects": "objects.json", "strings": "strings/",
               "scripts": { "files": ["scripts/main.tps"], "hooks": { "leaveQueue": "leaveQueue" } } } }
```

| Field | Required | Meaning |
| --- | --- | --- |
| `schema` | yes | Always `1`. |
| `id` | yes | `opentpw.` or `community.` followed by lowercase letters, digits, `.`, `-` and `_`; at most 64 characters. |
| `name` | yes | Display name, at most 80 characters. |
| `version` | yes | `MAJOR.MINOR.PATCH`, optionally `-prerelease` (sorted before the release). |
| `category` | yes | `official` for `opentpw.` ids, `community` for `community.` ids. |
| `openTpw` | no | The OpenTPW versions the mod works with (a range, below). |
| `edition` | no | `theme-park-world` or `theme-park-inc`; the mod is refused for the other edition. |
| `author`, `description` | no | Shown in the mods screen later. |
| `dependencies` | no | Up to 32 mods or extensions this mod needs, each with an optional version range (default `*`). |
| `after` | no | Mods this one is ordered after when both are enabled; also lets its overrides win over theirs. |
| `inputs` | no | Up to 16 folders the player supplies; `kind` is `original-data` (another original game's install). |
| `scriptApi` | with `scripts` | The script API versions the mod's scripts target (a range, below); required when `content.scripts` is present and refused otherwise. The mod is refused when this build's API version (`ScriptLimits.ScriptApiVersion`, currently `1.0.0`) is outside it. |
| `content` | no | Mod-relative paths: `overrides` (a folder of files that replace game files), `objects` (object definitions), `strings` (a folder of string tables), `scripts` (script files and hooks). See [Content](#content). |

Parsing is strict, like the online documents (`StrictJson`): unknown or duplicate fields, comments, trailing commas
and numbers as strings are refused; the file may be at most 64 KiB; and every content path must stay inside the
mod's folder (no `..`, no absolute paths, no drive letters or `:`, no empty or `.` segments).

### Version ranges

A range is one or more space-separated comparators that must all hold: `>=`, `>`, `<=`, `<` and `=` followed by a
version (a bare version means `=`), or `*` alone for any version. For example `>=0.8.0 <1.0.0`.

Release builds check `openTpw` against their version (`ModStartup.OpenTpwVersion`, the assembly's informational version, for example `0.8.13`):
a mod whose range does not contain it is refused as incompatible. Use a range that includes the releases you tested, such as `>=0.8.0`;
a range that starts above the current release refuses the mod on every build released so far. A development build without a version (the SDK default
1.0.0) does not check `openTpw` ranges.

## Discovery

Desktop builds look for mod folders in this order; the first folder of an id wins and later ones are reported as
shadowed:

1. `--mod-dir <folder>` (repeatable): one mod folder each, any folder name;
2. `OPENTPW_MOD_DIRS`: mod folders separated by the platform's path separator (`:` or `;`);
3. `<config>/mods/<id>/`, where `<config>` is the configuration folder (`OPENTPW_CONFIG_DIR`, see
   [SETUP.md](SETUP.md));
4. `<program>/content/mods/<id>/`: data-only content shipped with OpenTPW (the only place `opentpw.` ids may come
   from). The repository's `content/mods/` folder is copied to the build output and to every release like the rest of `content/`
   (`OpenTPW.csproj`), and embedded in the browser build (`OpenTPW.Web.csproj`). A mod there is listed in the screen like any other and is
   **off by default**.

In the last two places the folder must be named after the mod's id. A folder named on the command line or in
`OPENTPW_MOD_DIRS` whose path cannot be used is reported as a refused entry.

Mod folders are treated as untrusted. Only regular files are read: a manifest, `mods.json` or mod file that is a
symbolic link, reparse point, FIFO, device or socket refuses the mod (or, for `mods.json`, falls back to the defaults
with a warning) without being read, and documents are read through a stream that stops one byte past their limit.
The mod folder itself may be a link. More than 20000 files or 2 GiB, or two names that differ only by case or Unicode
normalization (such as `Foo.txt` and `foo.txt`), refuse the mod. Content paths may not use names Windows cannot
hold: a component ending in `.` or a space, or a device name such as `CON`, `NUL`, `COM1` or `LPT1` (with or without
an extension).

The browser build discovers the program folder only: the mods shipped with the page are embedded and written below it at start
(`content/mods/<id>/`, like the other content). It has no folders for community mods; those would come later as zips imported into the
page's private file storage.

## Settings: `mods.json`

The choices live in `<config>/mods.json` (in the browser `/config/mods.json`, which the page mirrors into the browser's IndexedDB and
puts back before the game starts):

```json
{
  "enabled": { "opentpw.online": false, "community.tpi-rides": true },
  "order": ["community.tpi-rides"],
  "inputs": { "community.tpi-rides": { "tpi": "/games/Theme Park Inc" } }
}
```

- `enabled`: on or off per id. Official extensions without an entry use their default; mods without an entry are off.
- `order`: the player's order of mods, used where dependencies leave a choice.
- `inputs`: the folder chosen for each input of a mod.

A missing file means the defaults. A file that cannot be read, is larger than 256 KiB or is not valid strict JSON adds
a startup warning and the defaults are used; invalid entries in an otherwise valid file are dropped one by one, each
with a warning. Smoke tests (`--smoke-test`, and the co-op join smoke `--coop-join-smoke`) ignore `mods.json` and
`<config>/mods`, like the compatibility and graphics settings.

`--disable-extension <id>` (repeatable) switches an extension or mod off for one run without changing `mods.json`;
an id that names no extension or installed mod is an error. `--enable-mod <id>` (repeatable) switches an extension or mod on, and
`--mod-input <mod id>:<input id>=<folder>` (repeatable) chooses an input folder, for one run each (also in smoke tests, which otherwise
ignore `mods.json`); a malformed `--mod-input` or an unknown id is an error.

## Content

`ModContentRoots.Activate` installs the enabled mods' content once at startup, after the set is resolved. With no files-based
mod enabled nothing is installed and every lookup is the game's own (the pinned determinism digests do not change).
Mods are consulted in load order, later over earlier; since two mods that supply the same thing are refused unless one
is ordered after the other, the winner is always the one that said so.

### `overrides`

Loose files below the `overrides` folder replace the game file at the same data-relative path, whatever the case:
`data/levels/jungle/sharetex/leaf.wct` replaces `/levels/jungle/sharetex/leaf.wct`, and
`data/levels/jungle/rides/totem/totem.sam` replaces that member of the archive `totem.wad`. Textures, sounds, UI art
and any other file read through the game's file system work the same way. A file the game lacks is added. Reads, sizes
and `FileExists` see the replacement; directory listings stay the game's, so an override cannot add an object archive
(use `objects`). The game's folder is never written.

### `strings`

`strings/<Language>/<NAME>.str` (for example `strings/German/UITEXT.str`) replaces that language's string table
completely; it is also an override of `/Language/<Language>/<NAME>.str`. Any other file below `strings` refuses the mod.
Tables must have the same layout as the original's. Two mods supplying the same table of a language conflict like two
overrides do.

### `objects`

`objects.json` (at most 256 KiB, `StrictJson` style: unknown or duplicate fields, comments and trailing commas are
refused) adds object archives to the theme catalogs:

```json
{ "schema": 1, "idRange": { "first": 20000, "last": 20099 },
  "objects": [
    { "archive": "objects/zoom", "theme": "jungle", "category": "rides", "infoId": 20001,
      "names": { "English": "Zoom", "German": "Sause" } },
    { "archive": "levels/jungle/rides/bumper", "input": "tpi", "theme": "jungle", "category": "rides", "infoId": 20002,
      "names": { "English": "Bumper Cars" } } ] }
```

New objects made from scratch (models, textures, scripts) are authored with the content kit ([CONTENT-KIT.md](CONTENT-KIT.md)).

- `idRange`: the Info.Ids the mod reserves, between 5000 and 65535, at most 4096 of them (one rule for every mod, see
  [Cross-edition objects](#cross-edition-objects-opentpwcross-edition-objects)). Every `infoId` lies in
  it and is used once. Layout records and saves join on the Info.Id, so a mod keeps an object's id stable across
  versions. The archive's own `Info.Id` is replaced by `infoId`. Two mods whose ranges overlap are refused.
- `archive`: the archive's path without `.wad` (a plain folder works too), in the mod's folder, or, with `input`, in the
  data folder of that input (the folder the player chose in `mods.json`, or the `Data` folder inside it). The path may
  not leave its folder (the rules of the manifest's content paths).
- `research` (optional, a top-level field of the file, default `true`): `false` makes every object of the file available from the start while its mod is on. Such objects are never research projects: they are in no research table, never in a
  save's research set, do not count in the group percentages and do not hold back the golden ticket for "everything researched and built". The official pools extension's `objects.json` sets it (its changing rooms), so a park that
  uses none of them has the same research tables and saved bytes with the extension on or off. Community mods leave it out and are researched like the game's own objects.
- The asset hashes of a save: a save of a park that places an object of the official pools extension holds that object's files (`mod:opentpw.pools:` keys) and a load requires them to hash the same; the files of objects the park does not place
  are ignored in both directions. Changing the files of a placed object changes the saves that place it; adding objects to the extension does not ([POOLS.md](POOLS.md#the-extension-and-the-save-rule)).
- `theme`: `jungle`, `hallow`, `space` or `fantasy`. `category`: `rides`, `shops`, `sideshow`, `features` or `upgrades`.
- `names`: the object's name per language; the selected language, then English, then the archive's `.sam` name.
- The archive must hold one `.sam` with an `Info.Id` and the main model, like the game's own. The category defaults
  (`Rides.sam`, ...) are layered under it from the **game data**, as for the official bonus objects. An archive whose
  name the catalog already lists (the original keeps the first), a missing archive and a broken one are skipped with a
  warning, never silently. Two mods that add an archive of the same name to one theme and category are refused.
- Only for Theme Park World parks. `input` must name an input of the manifest; a folder that holds no Data content
  refuses the mod. Nothing from another game is copied: the archive is read from where the player keeps it.

Mod objects come after the game's and the official bonus objects in each category, in mod order. Each content root has
its own file system (with WAD and SDT handlers); the catalog cache key includes the mod set's digest, so a different set
never reuses a catalog.

In a park's asset manifest the members of a mod's archives, its own and those read from an input, are keyed
`mod:<id>:/<path>` (`ContentRoot.Mod(id)`); game data stays `data:` and official bonus archives `bonus:`. The path is
relative to the archive's file system, so an input archive keeps its game path (`/levels/jungle/rides/bumper/...`) and
cannot collide with the mod's own, because one theme and category never lists two archives of one name.

### `scripts`

`content.scripts` has `files` (1 to 8 mod-relative script files, same path rules as the other content, no duplicates) and `hooks` (1 or
more entries from a known hook name to the function that implements it). Only the hook names of script API v1 are accepted
(`scoreAttraction`, `joinQueue`, `leaveQueue`, `wanderGoal`); the game runs them through the guest decision seam ([SCRIPT-MODS.md](SCRIPT-MODS.md#hook-api-v1)).
At resolve time every script file is compiled and checked against the script limits, and each hook must name a function that exactly one
file defines, with the hook's parameter count. A compile error or a failed check refuses the mod with the file, line and message. The
files, the caps (8 files, 256 KiB of source in all, 64 KiB each), safe mode and the rule that the executed bytes are the hashed bytes
are in [SCRIPT-MODS.md](SCRIPT-MODS.md#loading-and-the-manifest). A mod with scripts changes the simulation (next sections), and the mods
screen says "Contains scripts" for it. The shipped example is `opentpw.example.patient-guests` (off by default; not in the web build).

A second shipped example is a **data mod with a light**, `opentpw.example.lamp-lights` (off by default; not in the web build): a lamp post object whose model carries a light dummy and whose RSE script pulses, flickers and
colour-cycles the light with the `ENABLELIGHT`, `DISABLELIGHT`, `SETLIGHT` and `COLOURLIGHT` opcodes. To add lights to your own object, give its model light dummies (the content kit writes them from glTF point lights or
`ModelBuilder.AddLight`), tune them with the `Lights[n].*` keys of the object's `.sam` and drive them from the object's script; the lights are presentation only and are not part of a save's simulation state
([LIGHTS.md](LIGHTS.md), [CONTENT-KIT.md](CONTENT-KIT.md#lights)).

## Resolution

At startup, right after the compatibility settings, OpenTPW resolves the run's set once (`ModStartup`, `ModSet.Resolve`):

1. Official extensions are on or off as chosen. Enabled mods are checked: the `openTpw` range, the edition and that
   every input has an existing folder, and the `scriptApi` range contains this build's script API version. In safe mode
   ([SCRIPT-MODS.md](SCRIPT-MODS.md#safe-mode)) a mod with scripts is refused here without reading its files.
2. Every dependency must be installed, enabled and within its range; a refused mod refuses the mods that need it.
3. Two enabled mods that supply the same file (in `overrides` or `strings`, compared without case), the same object
   archive name in a theme and category, or overlapping Info.Id ranges are **both refused**; for files, unless one lists
   the other in `after`. There is no silent "last one wins".
4. Dependency and `after` cycles refuse the mods involved.
5. The rest is ordered: dependencies (and `after`) first, then official before community, then the player's `order`,
   then the ordinal id.

Refused mods and the reasons go to the startup log. Each enabled mod gets a **content digest** (`ContentSha256`): the
SHA-256 of one UTF-8 `path NUL sha256 LF` line per file of the mod's folder, the manifest included, with '/' paths in
Unicode normalization form C sorted by ordinal (UTF-16 code unit) order, and lowercase hex file hashes. A copy of a mod
on macOS, Windows or Linux therefore gets the same digest (`ModTests.TheGoldenDigestIsTheSameOnEveryPlatform` pins
one). Operating-system clutter is not part of a mod and is skipped everywhere: `.DS_Store`, AppleDouble `._*` files,
`Thumbs.db` and `desktop.ini`. Park saves record the digest of mods that change the simulation (below).

With no mods and the default extensions nothing changes: the pinned determinism hashes are the same with
`opentpw.online` on or off (`DeterminismTests.TogglingOnlyTheOnlineExtensionKeepsThePinnedDigests`).

## Mods in park saves

A mod **changes the simulation** when it adds objects (`content.objects`), has scripts with a hook that is not UI-only (all v1 hooks
change the simulation, so every mod with scripts does), or overrides a file that is not plainly
presentation media (`.fsh`, `.tga`, `.png`, `.jpg`, `.bmp`, `.wav`, `.mp3`, `.ogg`, `.flac`, `.str`, `.bf4`, `.ttf`, `.otf`; any
other type counts as simulation); an extension does when it declares `AffectsSimulation`. A park save records exactly
these (`Mods`: id, version, content digest) and nothing else, so a park saved without them has no `Mods` field and keeps
its bytes. Loading it needs the same mods, versions and content, in the same order; presentation-only mods and the online
extension may differ. A save that needs a mod you do not have refuses with `needs <id> <version>`; the load list and the
Continue entries keep it and add "(requires mod <id> <version>)". Exporting or publishing a park as a `.tpwpark` package is refused
while a **community** mod is active, because packages record no community mods yet; OpenTPW's own extensions do not refuse it (below, "Parks that use extensions"). Some extensions are recorded **only while the park uses them** (`ParkResumeMods.UsedOnlyExtensionIds`): `opentpw.pools` (pool cells or a changing room), `opentpw.umbrellas`, `opentpw.winter`, `opentpw.cheats` and `opentpw.evening` (a torch), so a park
that uses none of them saves, loads and exports identically with them on or off ([POOLS.md](POOLS.md), [EVENING.md](EVENING.md#torches)). The asset manifest keys archives of a mod `mod:<id>:/…`
(`data:` and `bonus:` for the game data and the bonus content). Details: [DETERMINISM.md](DETERMINISM.md) "Mods".

## When a change applies

`[EXT:extensions-apply-at-load]` (user-approved 2026-10-19, "Ook ontbrekende extensie aanzetten"). The run's extensions and mods are resolved at startup. An official extension switched on or off afterwards
(on the Extensions and mods screen, or by the load dialog below) becomes active **without a restart**, at a moment when no park simulates: at once in the lobby, else when the running park is left or
another park is loaded or started (a Restart Park reloads the same park and keeps its set; a park started from inside a park while a choice waits is built in one go, so the running park never
simulates with the new set). `ModStartup.ApplyExtensions` makes the switched set active: the feature gates read it at once, the official
extensions' objects (pools, umbrellas, winter, the evening torches) are listed in the theme catalogs again (`ModContentRoots.Activate`; the catalog cache is keyed by those roots), and everything else that depends on an
extension reads the set when a park is built: the guest simulation's rules and decision policy, the weather, the pools tool, slides and HUD rows, the guest sprites (`PoolGuestSprites`, the umbrella
and warm-item renderers), the evening lamps and the snow. In the browser the page fetches the pools sprite sets when the extension comes on after the start. The set's order is the one a new start with
the same choices resolves (`ModSet.WithExtensions`), so a park saved after a switch loads after the next start.

These still apply at the **next start** (in the browser, after a page reload), and the screen says so:

- **Community mods**: switching one on or off, or changing its input folder. A data mod overlays the original game's files, string tables and object catalogs, and its files are read and hashed at
  startup; assets, strings and catalogs already loaded from the old overlay would stay in use, so a live change could mix the two. Script mods are read and compiled at startup, behind the safe-mode
  marker ([SCRIPT-MODS.md](SCRIPT-MODS.md)).
- **The online extension** (`opentpw.online`): its online folders and command-line tools are set up at startup and the lobby's Go Online button is built with the front end.
- **An extension switched for the run on the command line** (`--disable-extension`, `--enable-mod`): the command line wins for that run.
- **Switching off an extension an enabled community mod needs**, and a community mod that was refused because an extension it needs was off: the mods are resolved again only at a start.

A change that combines an extension with any of these applies wholly at the next start.

## Loading a park with other extensions

`[EXT:save-extension-switch]` (user-approved 2026-10-19, "Ook ontbrekende extensie aanzetten"). Solo play is not blocked by rule options: when an own park save (the Load Park list, Continue, the
automatic checkpoint or autosave) differs from the active official extensions in a way a player may switch, the load asks first instead of refusing. The question is the original confirm dialog with one
line per kind of switch, in the eleven interface languages:

- **Rule-only extensions**, in either direction: "This park was played without Rain mood. Continue with Rain mood?" or "This park was played with Cold mood. Continue without Cold mood?", and "This park
  was played with another version of Smarter guests. Continue with this version?". The park loads under the active set, so its next save records the active set. Rule-only are the extensions that ship no
  content and that a park does not need for its objects or state, verified one by one: `opentpw.rain-mood`, `opentpw.winter-weather`, `opentpw.winter-cold`, `opentpw.winter-always` and
  `opentpw.smarter-guests` (`ParkExtensionSwitch.RuleOnlyExtensionIds`). The weather rules leave only per-guest day marks that every build reads with the rule on or off; the smarter guests' own saved
  state starts fresh when the extension comes on, is dropped when it goes off (the guest identity script mods share is kept) and starts fresh in another version (the guests keep their identity
  and traits). A switchable save is not listed as "requires mod" in the load list and the Continue entries; the load asks instead.
- **Content extensions the park uses** that are off now: "This park uses Pools, which is off. Turn Pools on and load the park?". The tick's tooltip is "Turn Pools on and load". These are the extensions a
  save records only while the park uses them (`ParkResumeMods.UsedOnlyExtensionIds`): `opentpw.pools`, `opentpw.umbrellas`, `opentpw.winter`, `opentpw.cheats` and `opentpw.evening` (a park with a torch). Confirming switches the extension on for
  this run at once (`[EXT:extensions-apply-at-load]`: the feature gates and the extension's objects in the theme catalogs; everything else that depends on an extension is read when a park is built) and
  writes it to `mods.json` like the Extensions and mods screen, keeping every other choice there; in the browser the page then fetches the pools sprite sets it skipped at the start. A load that fails
  switches the extension back and writes nothing.

The X goes back without loading. One line says the park continues with the extensions that are on now: **a switched park is a new run from the switch point**, not a replay of the saved one
([DETERMINISM.md](DETERMINISM.md#switched-parks)).

Everything else stays strict, with the reasons above: a community mod that is missing or has another version or content, a script mod in safe mode, an extension switched off for the run with
`--disable-extension`, a content extension in another version, the cross-edition objects extension (it changes which objects a park's catalog holds; it only matters with a community mod, which is
strict anyway), the order of the mods, and any load that mixes one of those with a switch. A Theme Park Inc run never asks (it records none of these extensions and loads no own park saves). Replays and
checks never switch: only the player's confirmation in the dialog does, so `--park-resume-smoke`, the tests, the online server and co-op, which compare the mod set, and the original-format export are
unchanged.

## Parks that use extensions

`[EXT:mods]` A `.tpwpark` package, and so a published park, may use OpenTPW's **official** extensions. Official means built in and shipped with every build (`OfficialExtensions.All`, ids `opentpw.*`); anything else is a community
mod. `ParkSharing.ModsOf` splits the simulation-changing mods the park would record (the same list a save records: a used-only extension only while the park uses it) into the two.

- **Publishing and exporting** refuse only community mods ("This park records mods (community.x 1.0.0) and cannot be exported or published yet"), exactly as before, and name only those mods. The package's manifest
  records the **content** extensions the park uses as `extensions` (id and version, for example `opentpw.winter 1.0.0`): `opentpw.pools`, `opentpw.umbrellas`, `opentpw.winter` and `opentpw.evening`, the
  extensions whose objects and art a visit draws (`ParkVisitExtensions.RequiredIds`). The rule extensions (rain mood, the winter rules, smarter guests, cross-edition objects) and the cheats extension change how a park
  plays, not how it looks, so they are not recorded: a park that uses only those is still a plain version 2 package, and an extension a later build adds cannot block a visit. A park that uses no content extension has no
  such member and stays format version 2, byte-compatible with every older reader. A package with the member is format version 3. An older build cannot read it: its strict manifest reader rejects the unknown
  member, so an older client or server says the manifest is malformed ('extensions' could not be mapped). A build with this change reads the version before the strict parse and says "Park package version N is not
  supported" for every later version. When a publish is refused that way by an older server, the client says "This server must be updated before it accepts parks that use OpenTPW extensions"
  (docs/ONLINE.md, docs/SERVER.md).
- **Visiting** (a downloaded park, Import file, `--visit-park`) looks at those content extensions. A visit never runs without a content extension its park uses:
  - on: the visit goes ahead (another version of the extension is not a difference for a visit);
  - off and the player may switch it: the confirm dialog of a park load asks "This park uses Winter, which is off. Turn Winter on and visit the park?" with the tick "Turn Winter on and visit", in the eleven
    interface languages. Confirming switches the extension on as a load does (`[EXT:extensions-apply-at-load]`: this run at once, or when the running park ends, and `mods.json`); the X goes back without a visit
    and without a change;
  - off and it cannot be switched (`--disable-extension` for this run, an edition that does not have it, an extension this build does not know): a message names it and the visit does not start. In a Theme Park Inc
    run the pools, umbrellas and winter extensions are gated off; the evening extension has no such gate and can be switched on;
  - `--visit-park` has no dialog: it refuses with the extension and the option that switches it on for the run (`--enable-mod opentpw.winter`).

  Co-op is separate and strict: a shared park's host and guests must run the same mod set (`CoopLink.CurrentIdentity`), as before; a park file's `extensions` play no part there.
- **Why a menu line is greyed**: the online menu's Publish park line and the pause menu's Publish Park line say why on hover (no park open, a park you only visit, a shared park's guest copy, community mods in the
  park). Hover help needs a pointer and Popup help on, and a disabled line cannot be focused with the keyboard or a gamepad, so the online menu's status line also names the reason while it has nothing else to
  say. The pause menu has the hover help only.

## Smarter guests and guest insights (`opentpw.smarter-guests`, `opentpw.guest-insights`)

Two official extensions for the park visitors ([SMARTER-GUESTS.md](SMARTER-GUESTS.md) has the plan, the architecture and the measurement): guest insights at version 1.0.0 and smarter guests at version 1.6.0. **Both can be switched on** on the Extensions and mods screen, in
`mods.json` or with `--enable-mod`, and both are off by default. Guest insights has its first feature (phase 1). Smarter guests was released as opt-in in 1.6.0, once its rule set (phases 2 to 6b) had a
measurable effect (less crowding, less litter) and its CPU cost per guest-turn met the gate of at most 1.10 times the original guests' (SMARTER-GUESTS.md, "Cost and release").

- **`opentpw.smarter-guests`** changes the simulation (`AffectsSimulation`), so a park saved with it records it (id, version, no files) and loads only with it on, in the same version; a park saved without it
  loads only with it off. **The extension's version is the algorithm identity**: every release that changes how guests decide bumps it, and a park saved by another version is refused with
  `needs opentpw.smarter-guests <saved version>, not <current>`. In 1.0.0 the guests still decided exactly as the original (the policy delegated); what the extension added is the seam (`IGuestDecisions`),
  a birth id for every guest and its own park state, saved in an optional `SmarterGuests` section of the park save (since 1.1.0 also the price-stall runs, the attractions' throughput estimates and, in 1.4.0 and 1.5.0, the guests' meal times and complaint times). A build from before that section refuses such a save. Switching it needs a
  restart like every extension, and a save made without it is refused while it is on.
  `FeatureGates.SmarterGuests` is read once when the guest simulation is built. `[EXT:smarter-guests]`.
- **`opentpw.guest-insights`** only reads the park (`AffectsSimulation` false) and is listed on the Extensions and mods screen (`PlayerSelectable`). Switched on, the park information window gains a column of
  **refusal-reason counters** in the place of the original's line-graph toggles: how many times guests turned an attraction down because it was too expensive (the price refusal at a shop or sideshow stand), the queue was too long
  (full, or at its limit), it was too scary, too boring, or the ride was broken or closed (a queue head sent away). The counters are a passive observer of the guest simulation (`GuestRefusalCounters`), start at zero for
  every park built or loaded, are never saved, never hashed and take no random draw, so saves, pins and replays are byte for byte those of the extension being off. Never recorded in saves, so it can be switched freely
  (with a restart like every extension). The complaints summary is still to come. `[EXT:guest-insights]`.

Their names and descriptions exist in the nine interface languages for the screen. Switching either on or off changes none of the pinned digests of the off path
(`DeterminismTests`); with smarter guests on, `DeterminismTests.SmarterGuestsFixedRunHash` pins the world hash, which then includes the extension's state.

## Restored advice (`opentpw.restored-advice`)

An official extension that gives the advisor the lines the original game recorded but never says: tp.exe v2.0 scores 21 of its scanned messages with a constant-zero stub and nothing posts 82 more. The extension restores **42** of those 103 messages, by trigger: security cover gaps, long queues, blocked ride exits, riders stuck in a
broken or condemned ride, low balances, land running out, buses, seaplanes and Ferries arriving, award news for the global golden tickets, upgrades without a mechanic, a few tutorial hints and the lines of the online screens. The messages, triggers, scores and the messages skipped on purpose are in
[reverse/ADVISOR-SCAN.md](reverse/ADVISOR-SCAN.md) "Restored advice" (thirteen lines of the first 55, the golden ticket wins, the first challenge offer, the double-or-nothing lines 319 and 320 and the track ride hint 204, stood down because the original's own ticket advice 215-227, first-offer line 344, challenge lines 270 and 272 and kart and water line tool help 204 are now ported); the mechanism (one scan hook, one poll, a driver for events) is in [LIPS.md](LIPS.md) "Restored advice".

- **On by default**, `PlayerSelectable` (the Extensions and mods screen lists it with its name and description in the nine languages; a restart applies a change like every extension), version 1.0.0.
- **It never changes the simulation** (`AffectsSimulation` false), which is what lets it be on by default: it only reads park state and posts advice through the advisor's queue, the advisor is outside the world state hash, and it keeps no state a save would carry. A park save
  records no mod for it, so a save made with it loads with it off and the other way round (the advisor's saved state has the same shape either way; a pending or said restored message loads wherever all 351 descriptors exist, which is every build). The pins of `DeterminismTests` do not move.
  `AdvisorRestoredAdviceTests` checks the hash, the mod records and the save in both directions. `[EXT:restored-advice]`.
- **A save made with it on may carry restored advice**: up to eight pending restored records, and the history of the restored messages said, are in the advisor's own saved state. They load with the extension off, where a pending record still plays once (harmless: it is an ordinary descriptor's advice), and nothing else of the extension is saved. Its once-per-park and long-repeat rules read that same history.
- **Off means silent**: with `opentpw.restored-advice` disabled in `mods.json` or by `--disable-extension opentpw.restored-advice` the scan rows score 0 as in tp.exe and no restored event, poll or help is posted. Every trigger is labelled `[EXT:restored-advice-<topic>]` where it is coded.

## The online extension (`opentpw.online`)

[Online](ONLINE.md) is an official extension, on by default (it makes no network request until the player acts).
When it is switched off (`"opentpw.online": false` in `mods.json`, or `--disable-extension opentpw.online`):

- the lobby has no globe button (`b_worldview`) and its Escape menu no Go Online line; the park's pause menu has no
  Go Online line, and its Publish Park line stays disabled; the postcard shortcuts and the camera panel's postcard
  button do nothing (`[EXT:extension-online-gate]`: the original always offered the globe);
- `GameFlow.Online` throws, so no online session, chat overlay or online folder is ever created, and the developer
  online panel is not shown;
- `--export-park`, `--import-park`, `--visit-park` and `--online-dir` refuse with "OpenTPW online extension is
  disabled";
- sharing features read `FeatureGates.Sharing` (the edition supports sharing and the extension is on).

To check the switched-off extension in the real game, run the front-end smoke test with it off (the repository has
no script or CI job that runs native smokes):

```sh
bash scripts/run.sh --game-path '/path/to/Theme Park World' --front-end --smoke-test --disable-extension opentpw.online
```

It skips the online steps and checks that the globe button and the pause menu's Go Online line are absent and that
Publish Park is a disabled line.

The browser page has no control for the extension yet; a `/config/mods.json` saved in the page's storage is honoured
at the next start. The page's own scripts make no request to an online server at startup (only the game's online
client does, after the player acts), so there is no page-level fetch to check when the extension is off.

## Cross-edition objects (`opentpw.cross-edition-objects`)

An official extension, **off by default**, that lets a data mod bring attractions of another original game (Theme Park
Inc) into Theme Park World. Nothing of Theme Park Inc ships with OpenTPW: the player supplies the install as a mod
input (`"inputs": [{ "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" }]`), and a mod that needs the
extension lists it under `dependencies` (a mod refuses to run while the extension is off). Because objects it brings
change the park, the extension is marked as changing the simulation (`AffectsSimulation`): while it is on, park saves record
it (id, version, no files) and a park saved with it loads only with it on, and one saved without it only with it off
([Mods in park saves](#mods-in-park-saves)). It is `[EXT:cross-edition-objects]`.

What the extension provides in the engine:

- **Reserved `Info.Id`s.** Theme Park World and Theme Park Inc number their objects from the same space: 168 of the 202
  distinct Theme Park Inc ids are also Theme Park World ids, and 145 of those name other objects (id 1100 is the Belly
  Bounce in one and the Bellows in the other). The original keeps a definition whose id is
  taken (it stays listed) but every lookup by id returns the first one in load order (RIDES-024), so a second object with
  a taken id would be unreachable. A mod therefore reserves a range of ids in the `idRange` of its `objects.json` and gives
  each archive an id from it; `objects.json` is refused when an id lies outside the range or is used twice, and `ModSet`
  refuses two mods with overlapping ranges.

  **One rule for every mod's id range**, whether or not the mod uses this extension (`CrossEditionObjects.RangeProblem`,
  applied by `ModObjectsFile`): the range lies in 5000 to 65535 and holds at most 4096 ids. It lies above every original id
  (Theme Park World reaches 4605, Theme Park Inc 4500, both measured against the installs by tests), so no mod id can be
  taken by an original object, and inside 16 bits, the width of a placed object's Info.Id (`mId`) in the original park saves,
  so every mod object stays representable in an original-format save. The range is declared once, in `objects.json`; the
  manifest has no second field. (The content roots first allowed 10000 to 1000000000 and at most 1000 ids; every range that
  ended below 65536 stays valid.) Nothing in OpenTPW's saves or tables assumes ids below 5000: its tables are keyed by
  the Info.Id as an integer, and the only fixed ids are the original ones.

  **The ranges OpenTPW's own content holds** (a community mod picks a range outside them):

  | Ids | Holder |
  | --- | --- |
  | 40000 to 40499 | the Theme Park Inc rides mod (`opentpw.tpi-rides`) |
  | 63900 to 63999 | the evening extension's torches ([EVENING.md](EVENING.md#torches)) |
  | 64000 to 64009 | the example lamp mod (`opentpw.example.lamp-lights`) |
  | 64010 to 64499 | the winter extension's shops ([WINTER.md](WINTER.md)) |
  | 64500 to 64999 | the umbrellas extension's brolly shops ([UMBRELLAS.md](UMBRELLAS.md)) |
  | 65000 to 65535 | the pools extension's objects ([POOLS.md](POOLS.md)) |
- **Sound banks per content root.** A Theme Park Inc ride script's `EVENT` ids belong to Theme Park Inc's own sound
  catalogues (its global `cat_rides` holds 114 events against Theme Park World's 25), so an object from a registered root
  (`ContentSoundRoots.Register`) looks its events up in that root: global categories in the root's `global/sound`, level
  categories in `levels/<theme of the object's archive>/Sound`, and nothing falls back to the running game's categories (the
  original has no second folder to try either). Every Theme Park Inc ride and ambient bank resolves in the Theme Park Inc
  root. Fixed-id sounds (screams, repair sounds) still use the running game's categories.
- **Names in the selected language.** The objects a mod brings from a Theme Park Inc install take their names from that install's own
  `OBJECT_NAMES.str` in the selected language (matched by the object's own `Info.Name`, which `objects.json`'s English name replaces), as the
  Theme Park Inc slice names its objects; a name the table lacks, and a language the install does not have (it has Dutch, English, French, German
  and Swedish), keep the `objects.json` name ([LANGUAGES.md](LANGUAGES.md#theme-park-inc)).
- **The two Theme Park Inc RSE opcodes** `WAITABS` (45) and `MULT` (48) need no new support: the VM already runs them, and
  they are not Theme Park Inc only. Theme Park Inc's `Game.exe` and `tp.exe` have the same two handlers (same operand
  decoding and the same effect; see `[BIN:TPI-EXE:0x6a8b7e]` and `[BIN:TPI-EXE:0x6a9108]`).

Missing economy keys: with the Theme Park World category defaults (`Rides.sam`, `Shops.sam`, ...) under a Theme Park Inc
object file, as for official bonus archives, every key that all Theme Park World objects of that category carry is present
in each of the 183 Theme Park Inc archives that map onto the four Theme Park World themes, so no per-key default is needed
for Theme Park World's economy. Theme Park Inc's `Upgrades[n].RunningCost` has no Theme Park World counterpart and is
ignored; its `Research.Group` values reach 6, and the research table already treats groups past `ResearchTech` as
threshold 0, as the original's zero-filled block does.

Loading the archives themselves is the content roots' job (`objects.json` with an `input`). When the extension is on, the
engine registers each input that the manifest declares as `theme-park-inc` **and** that `GameEditions.Detect` recognises as
Theme Park Inc data (`ModContentRoots`) as a sound root, as a root of shared textures and as a source of economy data, as the next
section describes; an input folder that is not Theme Park Inc data gets a warning and no sound root, and with the extension off no
input is registered, so every object keeps the running game's banks.

## The shipped Theme Park Inc rides mod

OpenTPW ships the rides mod of the next section as ready-made content: `content/mods/opentpw.tpi-rides/` holds the manifest and the
`objects.json`, two text files of about 30 KB with no Theme Park Inc bytes. It is a data mod with an `opentpw.` id (reserved for content
shipped with OpenTPW, found in the program folder only), depends on the cross-edition objects extension and asks for one `original-data`
input, `tpi`, with `"edition": "theme-park-inc"`. Nothing is on by default: not the extension, not the mod.

- **What it lists.** 183 objects, joined to the Theme Park World theme of their world (arabian to the jungle park, science to space, water to
  fantasy), by their own category: every archive of the install's `rides`, `shops`, `sideshow`, `features` and `upgrades` folders that the
  parks can load (182 of the 217; the other 35 are the skipped names described below), and the science litter bin of the `Facilities` folder. Ids
  run from 40001 inside the reserved range 40000 to 40499; the first four (Turban Bounce, Giant Robot Swing, the litter bin and Desert Drinks)
  keep their ids, and a new archive takes the next free one. The English names are "Turban Bounce" and its three siblings, and "<World> <archive
  name>" for the rest (for example "Arabian apcoast"): they are derived from the install's own file names, never copied from its text.
- **Where it comes from.** The tests read these files themselves (`TpiRidesFixture`), so the shipped mod and the tests cannot drift. Without
  any install, `TheShippedModHasLfLineEndingsAndAFixedContentDigest` pins the mod's content digest to a golden value, so any change to the
  files is deliberate (a new digest and a new mod version together). A park save records that digest, so the files must be the same bytes on every
  platform: `.gitattributes` pins `content/mods/** text eol=lf`, and the test refuses a CR. With the install,
  `TpiRidesShippedModTests` compares the list with the archives your install really loads, and with `OPENTPW_WRITE_TPI_RIDES=1` rewrites
  `objects.json` from it (existing ids stay, new archives are appended). Run it with `OPENTPW_GAME_PATH` and `OPENTPW_TPI_PATH` set.
- **Turning it on, desktop.** Open Options > OpenTPW > **Extensions and mods**, switch on *Cross-edition objects* and *Theme Park Inc rides*,
  choose your Theme Park Inc install (or its `Data` folder) as the input (the screen checks it), press OK and restart; or for one run:
  `--enable-mod opentpw.cross-edition-objects --enable-mod opentpw.tpi-rides --mod-input 'opentpw.tpi-rides:tpi=/games/Theme Park Inc'`.
  Start a Theme Park World park: the objects are in the buy lists of their theme, after the game's own.
- **Turning it on, browser.** Choose your Theme Park Inc folder on the setup page (*Theme Park Inc files for mods*) before or after the Theme
  Park World folder, then switch the extension and the mod on in the same screen and reload. Only the files the mod reads are copied (about 77
  MB of the install's 400 MB) and they stay in the browser's storage. Details and limits: [WEB.md](WEB.md#mods-in-the-browser).

## Making a Theme Park Inc rides mod

The mod above is made this way; a mod of your own works the same, with a `community.` id.

A mod that brings Theme Park Inc attractions into Theme Park World is two small text files. It copies nothing: the player's own
Theme Park Inc install is the mod's input and the archives are read where they are.

1. Make a folder named after the mod's id, for example `community.tpi-rides`, in `<config>/mods/` (or anywhere, with `--mod-dir`).
2. Write `opentpw-mod.json` in it. The mod needs the cross-edition objects extension, one `original-data` input for the install
   and an `objects.json`:

   ```json
   { "schema": 1, "id": "community.tpi-rides", "name": "Theme Park Inc rides", "version": "1.0.0", "category": "community",
     "edition": "theme-park-world",
     "dependencies": [ { "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" } ],
     "inputs": [ { "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" } ],
     "content": { "objects": "objects.json" } }
   ```

3. Write `objects.json` next to it. Each entry names one archive of the install (the path below its `Data` folder, without `.wad`), the
   Theme Park World theme and category it joins, an `infoId` from the range the mod reserves, and its names:

   ```json
   { "schema": 1, "idRange": { "first": 40000, "last": 40099 }, "objects": [
     { "archive": "levels/arabian/rides/tbounce", "input": "tpi", "theme": "jungle", "category": "rides", "infoId": 40001,
       "names": { "English": "Turban Bounce", "German": "Turban-Huepfer" } },
     { "archive": "levels/science/rides/robot", "input": "tpi", "theme": "space", "category": "rides", "infoId": 40002,
       "names": { "English": "Giant Robot Swing" } },
     { "archive": "levels/science/Facilities/litbin", "input": "tpi", "theme": "space", "category": "features", "infoId": 40003,
       "names": { "English": "Science Litter Bin" } },
     { "archive": "levels/arabian/shops/drinks", "input": "tpi", "theme": "jungle", "category": "shops", "infoId": 40004,
       "names": { "English": "Desert Drinks" } } ] }
   ```

   - **Which archives.** Theme Park Inc keeps its objects below `Data/levels/<world>/` in `rides`, `shops`, `sideshow`, `features`,
     `upgrades` and `Facilities` (litter bins, toilets, staff rooms); `archive` may name any of them, and `category` says where the object
     is listed in Theme Park World (the litter bin above is a feature). Its worlds are `arabian`, `science` and `water`; the example
     puts them into the `jungle`, `space` and `fantasy` parks.
   - **Which ids.** Pick a range of at most 4096 ids in 5000 to 65535 (see [Cross-edition objects](#cross-edition-objects-opentpwcross-edition-objects))
     and keep each object's id when you update the mod: saves and layouts join on it.
   - **What an archive brings.** The `.sam` with the model, its textures, its animations and its RSE script, and the object's own
     price, research cost and group. Theme Park World's category defaults (`Rides.sam`, ...) fill in what the archive does not say, as for the
     official bonus objects. The name in `names` replaces the archive's `.sam` name in the build lists, the HUD and the economy.
   - **What does not load.** An archive whose name the park already lists (the game's own `gates`, `bus`, `end`, `tvsim`, ...; the original
     keeps the first), an archive with no `.sam` that declares an `Info.Id` (`ptract`) and a broken archive are skipped with a warning in the log.
     The example mod's scale is fine: all 217 archives of the install's three worlds can be listed (the test `EveryArchiveOfTheInstallLoads...`
     does), 182 of them load, and the 35 others are the skipped names above.
4. Turn it on. In the **Extensions and mods** screen switch on *Cross-edition objects* and the mod, then choose the install as the
   `tpi` input (the screen checks that the folder holds Theme Park Inc data); or edit `mods.json`:

   ```json
   { "enabled": { "opentpw.cross-edition-objects": true, "community.tpi-rides": true },
     "inputs": { "community.tpi-rides": { "tpi": "/games/Theme Park Inc" } } }
   ```

   or for one run: `--enable-mod opentpw.cross-edition-objects --enable-mod community.tpi-rides --mod-input 'community.tpi-rides:tpi=/games/Theme Park Inc'`
   (`--mod-input <mod id>:<input id>=<folder>`; both options are repeatable and change nothing on disk). Start a Theme Park World
   park: the mod's objects are in the buy lists of their theme, after the game's own.

What the engine does with such an object (all `[EXT:cross-edition-objects]` unless it says otherwise):

- **Catalogue and economy.** The object joins its theme's object catalogue (`ObjectCatalog`) and the economy catalogue
  (`EconomyObjectCatalog`) with the layers a game object has (the theme's category `.sam`, the archive's `.sam`, `Easy_` in easy mode) and
  the `Info.Id` and English name of `objects.json`. It is priced from `Upgrades[0].CostOfUpgrade`, researched like any other (a ride with
  a research cost waits in the lab), listed in the buy window's tab of its `Info.WhichUIType`, bought through `ParkEconomy.TryBuild`,
  placed through the normal build path (`Level.PlaceObject`, the build tool, the queue tool), linked to the guests and sold or scrapped
  like a game object.
- **Textures.** The archive's own `textures` folder, then the shared textures of the **world the archive was authored in**
  (`levels/science/sharetex` of the install, not the park theme's), both read from the install; `.fsh` and `.tga` textures are decoded
  as well as `.wct`.
- **Script and sounds.** The RSE script runs as in Theme Park Inc, including the two Theme Park Inc only opcodes (`WAITABS`, `MULT`). Its
  `EVENT` ids are looked up in the install's own sound categories (`ContentSoundRoots`), never in the running game's. A litter bin's
  trigger sets the script variable at index 0 (as the original does), which Theme Park Inc's bin calls `VAR_USE`. Variables only Theme
  Park Inc's own engine writes stay 0: [APPROX:RIDES-045] (docs/OBJECTS.md).
- **Saves.** The extension and the mod are recorded in a park save with their versions and content digests, the asset manifest pins the
  install's members the park uses (`mod:community.tpi-rides:/levels/arabian/rides/tbounce/...`), and loading without them (or with another
  version of the mod) is refused with `needs community.tpi-rides 1.0.0`; the load list keeps the park and says which mod it needs. A different
  Theme Park Inc install with other bytes is refused by the manifest hashes.

What is not verified: Theme Park Inc's track rides (coasters, water and kart tracks) and tours in a Theme Park World park, the
sound-driven synchronisation of TPI scripts with its engine events, and the TPI scripts' variables above.

### Checking a TPI rides mod

- Tests (`source/OpenTPW.Tests/TpiRidesWiringTests.cs`, `TpiRidesShippedModTests.cs` and `ModsInBrowserTests.cs`, always;
  `TpiRidesEndToEndTests.cs` and the install comparison of `TpiRidesShippedModTests`, over the real install): the shipped mod
  (`content/mods/opentpw.tpi-rides`, copied beside the test binaries, no Theme Park Inc bytes) is checked with `OPENTPW_TPI_PATH` (an install
  or its `Data` folder) next to `OPENTPW_GAME_PATH`: it is discovered as program content and off by default, every object it lists loads into its
  theme, and then catalogue, economy, buy list, textures, sound routes, a placed object's script (the litter bin runs `WAITABS`), the M3
  gate's guests boarding a Theme Park Inc ride in the jungle and in the space park, and the save records. Without the variables they are
  inconclusive.
- Native smokes: `OPENTPW_TPI_PATH=... bash scripts/smoke-tpi-rides.sh '/path/to/Theme Park World'` switches the shipped mod and its
  extension on for the sandbox smoke (it builds the mod's objects), and the park-resume smoke pair with `OPENTPW_SMOKE_MODS=1`, whose
  fixture park is built from the mod's Turban Bounce through the HUD build menu and queue tool, saved and restored.
