# Modding guide

A short start for people who want to make an OpenTPW mod. It covers the folder layout, a minimal manifest, where to
put the folder, how to switch the mod on, a data override and a script. The full references are
[MODS.md](MODS.md) (manifest, content, resolution, saves) and [SCRIPT-MODS.md](SCRIPT-MODS.md) (TPW Script).

## What a mod is

OpenTPW has two kinds of add-ons. **Official extensions** are OpenTPW features that are compiled in and can be switched
off (for example pools, which is on by default, see [POOLS.md](POOLS.md)). **Mods** are folders of data and,
optionally, scripts. Community mods use ids that start with `community.`.

A mod can:

- replace game files (**overrides**: textures, sounds, interface art and so on),
- replace a language's string tables (**strings**),
- add object archives to the theme catalogs (**objects**),
- change guest decisions with a script written in **TPW Script** (**scripts**).

It never ships native code or .NET assemblies; OpenTPW does not load them (see [What is not possible](#what-is-not-possible)).
Mods contain no original game files either: you write your own data, or use files you are entitled to share.

## Folder layout

A mod is one folder with a manifest named `opentpw-mod.json`. Everything else is optional and referenced by relative
paths from the manifest:

```text
community.my-mod/
  opentpw-mod.json
  data/             overrides: files that replace game files at the same path
  strings/          string tables, strings/<Language>/<NAME>.str
  objects.json      object definitions (see MODS.md, "objects")
  scripts/main.tps  TPW Script
```

The folder must be named after the mod id when it lives in the configuration or program folder (below).

## A minimal manifest

```json
{ "schema": 1,
  "id": "community.my-mod",
  "name": "My first mod",
  "version": "1.0.0",
  "category": "community" }
```

`schema` is always `1`. `id` starts with `community.` and uses lowercase letters, digits, `.`, `-` and `_`. `version` is
`MAJOR.MINOR.PATCH`. `category` is `community` for `community.` ids. Parsing is strict: unknown or duplicate fields,
comments and trailing commas are refused, and every content path must stay inside the mod folder. All fields are listed in
[MODS.md](MODS.md#manifest).

## Where to put the folder

Put the folder in the `mods` folder of the OpenTPW configuration directory, named after the id:

| Platform | Folder |
| --- | --- |
| macOS | `~/Library/Application Support/OpenTPW/mods/community.my-mod/` |
| Linux | `~/.config/OpenTPW/mods/community.my-mod/` (`$XDG_CONFIG_HOME/OpenTPW` when set) |
| Windows | `%APPDATA%\OpenTPW\mods\community.my-mod\` |

`OPENTPW_CONFIG_DIR` replaces the configuration directory ([SETUP.md](SETUP.md)). For development you can also load a
folder with any name for one run: `--mod-dir <folder>` (repeatable), or list folders in `OPENTPW_MOD_DIRS`. The first
folder with an id wins; a second one is reported as shadowed. The browser build has no folder for community mods yet; it
runs only the mods shipped with the page.

## Switch it on

Mods are **off until you switch them on**. Start OpenTPW and open Options, then OpenTPW, then **Extensions and mods**. Your mod is in
the **Community mods** column with a status. Select it, switch it on and press OK. The change applies at the next start.
A status other than On or Off tells you why the mod cannot be used (for example a manifest error or a conflict with another mod); the details
pane shows the reason. For one run you can use `--enable-mod community.my-mod` instead. The choice is stored in `mods.json` in the configuration
folder ([MODS.md](MODS.md#settings-modsjson)).

## An example data override

Files below the `overrides` folder (the manifest's `content.overrides`) replace the game file at the same data-relative path,
whatever the case. This mod replaces one jungle texture:

```json
{ "schema": 1, "id": "community.my-leaf", "name": "My leaf", "version": "1.0.0", "category": "community",
  "content": { "overrides": "data/" } }
```

```text
community.my-leaf/
  opentpw-mod.json
  data/levels/jungle/sharetex/leaf.wct
```

`data/levels/jungle/sharetex/leaf.wct` now replaces `/levels/jungle/sharetex/leaf.wct`. A file inside an archive works the
same way (`data/levels/jungle/rides/totem/totem.sam` replaces that member of `totem.wad`). A file the game lacks is added,
but an override cannot add an object archive; use `objects.json` for that. Two enabled mods that supply the same file are
both refused unless one lists the other in `after`.

Strings work like this: `strings/German/UITEXT.str` replaces that language's table completely, so it must have the same
layout as the original. Objects are described in [MODS.md](MODS.md#objects); a complete worked example is the shipped
Theme Park Inc rides mod ([MODS.md](MODS.md#the-shipped-theme-park-inc-rides-mod)).

## A tiny TPW Script example

Scripts are written in TPW Script, a small deterministic language with Lua-like syntax ([SCRIPT-MODS.md](SCRIPT-MODS.md)). A mod with scripts
needs `scriptApi` and a `content.scripts` entry that maps hook names to functions:

```json
{ "schema": 1, "id": "community.patient-queue", "name": "Patient queue", "version": "1.0.0", "category": "community",
  "scriptApi": ">=1.0.0 <2.0.0",
  "content": { "scripts": { "files": [ "scripts/main.tps" ], "hooks": { "leaveQueue": "leaveQueue" } } } }
```

`scripts/main.tps`, taken from the working example `opentpw.example.patient-guests` (the whole mod ships in `content/mods/`):

```lua
local patience = 600       -- park turns a guest waits in a queue before it gives up (the game itself never loses patience)

-- Counts an event in the mod's saved `state`.
function count(name)
  if has(state, name) then
    state[name] = state[name] + 1
  else
    state[name] = 1
  end
end

-- Called after the game's own leave tests; `prev` is true when the game lets the guest leave (it never does).
function leaveQueue(guest, attraction, prev)
  if prev then
    return true
  end
  if attraction.waitedTurns > patience then
    count("gaveUp")
    return true
  end
  return false
end
```

A hook receives the answer the game (and earlier mods) would give as `prev` and returns the answer to use. `state` is the only
place a script keeps data, and it is saved with the park. The four hooks of API 1 are `scoreAttraction`, `joinQueue`,
`leaveQueue` and `wanderGoal`; their arguments are listed in [SCRIPT-MODS.md](SCRIPT-MODS.md#hook-api-v1). A script that fails or runs out of its step budget returns
`prev`, and is switched off after repeated failures. A compile error refuses the mod and the screen shows the file, line and message.
Script mods run on the desktop; the browser build does not run them yet. If a script mod breaks start-up, OpenTPW starts the next time in safe mode with all
script mods off ([SCRIPT-MODS.md](SCRIPT-MODS.md#safe-mode), or `--safe-mode`).

## Saves

A mod that **changes the simulation** is recorded in every park save with its id, version and content digest. That covers mods
with objects, mods with scripts and mods that override a file that is not plainly presentation media (`.fsh`, `.tga`, `.png`, `.jpg`,
`.bmp`, `.wav`, `.mp3`, `.ogg`, `.flac`, `.str`, `.bf4`, `.ttf`, `.otf`; any other type, such as `.wct`, counts as simulation). A save made with such a
mod loads only with the same mod, version and content, and refuses with `needs <id> <version>` otherwise, so a changed script or data file
makes older parks of that mod refuse to load until you restore it. A park saved without such mods has no mod record and keeps its bytes.
Presentation-only mods may differ between saving and loading. The content digest is the same on every platform. Script state, failure counters and the
disabled flag are saved too, so a loaded park continues exactly ([MODS.md](MODS.md#mods-in-park-saves), [SCRIPT-MODS.md](SCRIPT-MODS.md#saves-and-versioning)).

## What is not possible

- **No native code and no assemblies.** .NET has no in-process security boundary, the browser build is compiled ahead of time, and third-party code would
  break saves and determinism.
- **No Lua or other engine.** TPW Script has Lua-like syntax but is OpenTPW's own interpreter: all numbers are integers, there are no closures, no
  `pcall`, no libraries, no clock, files or network, and the step budget cannot be caught. That is what keeps the simulation deterministic and saveable
  ([why](SCRIPT-MODS.md#why-our-own-language)).
- **Scripts can only answer the four guest hooks of API 1.** They cannot add new UI, objects, rules or windows, and they cannot change anything except through
  a hook result. Read-only event hooks, UI-only hooks and a `--test-mod` command are planned but not built. Features scripts cannot express become
  official extensions.
- **No mods in the browser from outside the page.** The browser runs the mods shipped with the page; importing community mods there is not built.
- **Changes apply at the next start.** Nothing is swapped while the game runs.
- **Mods that conflict are refused**, not merged: two mods that supply the same file, object archive or `Info.Id` range are both refused unless one is ordered after the other.
