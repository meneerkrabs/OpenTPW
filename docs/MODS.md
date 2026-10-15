# Extensions and mods

OpenTPW has two kinds of add-ons, both labelled `[EXT:mods]` in the source (`source/OpenTPW/Mods/`):

- **Official extensions** are OpenTPW engine features that live in this repository and carry their own
  `[EXT]`/`[APPROX]` labels. They are compiled in, ship no files and can be switched off. Their ids start with
  `opentpw.`.
- **Mods** are folders of data with an `opentpw-mod.json` manifest. Community mods use ids that start with
  `community.`; `opentpw.` ids are reserved for data-only content shipped with OpenTPW.

Mods are **data only**. OpenTPW never loads assemblies or other code from a mod: .NET has no in-process security
boundary, the browser build is trimmed and compiled ahead of time, and third-party code would break the save and
determinism guarantees. Engine features a mod needs become official extensions instead.

In code the system is called `OpenTPW.Mods` / "extensions"; `OpenTPW.ModKit` is the unrelated asset editor.

## Current state

This is the first step of the mod system:

- manifests are parsed and validated, mod folders are discovered, `mods.json` is read and the run's set of extensions
  and mods is resolved once at startup;
- the only official extension is `opentpw.online` (below);
- **nothing consumes mod content yet**. Enabling a mod checks it, orders it and computes its digest, but its files
  do not change the game. Content roots, the Extensions and mods screen and recording mods in park saves are later
  steps. Until saves record mods, no mod may change the simulation.

Changes to `mods.json` apply at the next start; nothing is swapped while the game runs.

## Manifest

Each mod folder holds `opentpw-mod.json`:

```json
{ "schema": 1, "id": "community.tpi-rides", "name": "Theme Park Inc rides", "version": "1.0.0",
  "category": "community", "openTpw": ">=0.9.0 <0.10.0", "edition": "theme-park-world",
  "author": "Ann", "description": "Optional, up to 1000 characters.",
  "dependencies": [{ "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" }],
  "after": ["community.other-rides"],
  "inputs": [{ "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" }],
  "content": { "overrides": "data/", "objects": "objects.json", "strings": "strings/" } }
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
| `content` | no | Mod-relative paths: `overrides` (a folder of files that replace game files), `objects` (object definitions), `strings` (a folder of text). |

Parsing is strict, like the online documents (`StrictJson`): unknown or duplicate fields, comments, trailing commas
and numbers as strings are refused; the file may be at most 64 KiB; and every content path must stay inside the
mod's folder (no `..`, no absolute paths, no drive letters or `:`, no empty or `.` segments).

### Version ranges

A range is one or more space-separated comparators that must all hold: `>=`, `>`, `<=`, `<` and `=` followed by a
version (a bare version means `=`), or `*` alone for any version. For example `>=0.9.0 <0.10.0`.

Release builds check `openTpw` against their version. A development build without a version (the SDK default
1.0.0) does not check `openTpw` ranges.

## Discovery

Desktop builds look for mod folders in this order; the first folder of an id wins and later ones are reported as
shadowed:

1. `--mod-dir <folder>` (repeatable): one mod folder each, any folder name;
2. `OPENTPW_MOD_DIRS`: mod folders separated by the platform's path separator (`:` or `;`);
3. `<config>/mods/<id>/`, where `<config>` is the configuration folder (`OPENTPW_CONFIG_DIR`, see
   [SETUP.md](SETUP.md));
4. `<program>/content/mods/<id>/`: data-only content shipped with OpenTPW (the only place `opentpw.` ids may come
   from).

In the last two places the folder must be named after the mod's id. Symbolic links inside a mod folder refuse the
mod (the mod folder itself may be a link), as do more than 20000 files or 2 GiB.

The browser build has the official extensions only. Community mods there will come later as zips imported into the
page's private file storage.

## Settings: `mods.json`

The choices live in `<config>/mods.json` (in the browser `/config/mods.json`, which the page keeps):

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
with a warning. Smoke tests (`--smoke-test`) ignore `mods.json` and `<config>/mods`, like the compatibility settings.

`--disable-extension <id>` (repeatable) switches an extension or mod off for one run without changing `mods.json`;
an id that names no extension or installed mod is an error.

## Resolution

At startup, right after the compatibility settings, OpenTPW resolves the run's set once (`ModStartup`, `ModSet.Resolve`):

1. Official extensions are on or off as chosen. Enabled mods are checked: the `openTpw` range, the edition and that
   every input has an existing folder.
2. Every dependency must be installed, enabled and within its range; a refused mod refuses the mods that need it.
3. Two enabled mods whose `overrides` folders hold the same file (compared without case) are **both refused** unless
   one lists the other in `after`. There is no silent "last one wins".
4. Dependency and `after` cycles refuse the mods involved.
5. The rest is ordered: dependencies (and `after`) first, then official before community, then the player's `order`,
   then the ordinal id.

Refused mods and the reasons go to the startup log. Each enabled mod gets a **content digest** (`ContentSha256`): the
SHA-256 of one `path NUL sha256 LF` line per file of the mod's folder, the manifest included, with '/' paths in ordinal
order and lowercase hex file hashes. Later steps will record it in park saves of mods that change the simulation.

With no mods and the default extensions nothing changes: the pinned determinism hashes are the same with
`opentpw.online` on or off (`DeterminismTests.TogglingOnlyTheOnlineExtensionKeepsThePinnedDigests`).

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

`--front-end --smoke-test --disable-extension opentpw.online` runs the front-end smoke test without its online steps
and checks that the globe button is absent.
