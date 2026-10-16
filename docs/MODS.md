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
- park saves record the mods that change the simulation and refuse a mismatched set (below);
- **nothing consumes mod content yet**. Enabling a mod checks it, orders it and computes its digest, but its files
  do not change the game. Content roots and the Extensions and mods screen are later steps.

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

In the last two places the folder must be named after the mod's id. A folder named on the command line or in
`OPENTPW_MOD_DIRS` whose path cannot be used is reported as a refused entry.

Mod folders are treated as untrusted. Only regular files are read: a manifest, `mods.json` or mod file that is a
symbolic link, reparse point, FIFO, device or socket refuses the mod (or, for `mods.json`, falls back to the defaults
with a warning) without being read, and documents are read through a stream that stops one byte past their limit.
The mod folder itself may be a link. More than 20000 files or 2 GiB, or two names that differ only by case or Unicode
normalization (such as `Foo.txt` and `foo.txt`), refuse the mod. Content paths may not use names Windows cannot
hold: a component ending in `.` or a space, or a device name such as `CON`, `NUL`, `COM1` or `LPT1` (with or without
an extension).

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
SHA-256 of one UTF-8 `path NUL sha256 LF` line per file of the mod's folder, the manifest included, with '/' paths in
Unicode normalization form C sorted by ordinal (UTF-16 code unit) order, and lowercase hex file hashes. A copy of a mod
on macOS, Windows or Linux therefore gets the same digest (`ModTests.TheGoldenDigestIsTheSameOnEveryPlatform` pins
one). Operating-system clutter is not part of a mod and is skipped everywhere: `.DS_Store`, AppleDouble `._*` files,
`Thumbs.db` and `desktop.ini`. Park saves record the digest of mods that change the simulation (below).

With no mods and the default extensions nothing changes: the pinned determinism hashes are the same with
`opentpw.online` on or off (`DeterminismTests.TogglingOnlyTheOnlineExtensionKeepsThePinnedDigests`).

## Mods in park saves

A mod **changes the simulation** when it adds objects (`content.objects`) or overrides a file that is not plainly
presentation media (`.fsh`, `.tga`, `.png`, `.jpg`, `.bmp`, `.wav`, `.mp3`, `.ogg`, `.flac`, `.str`, `.bf4`, `.ttf`, `.otf`; any
other type counts as simulation); an extension does when it declares `AffectsSimulation`. A park save records exactly
these (`Mods`: id, version, content digest) and nothing else, so a park saved without them has no `Mods` field and keeps
its bytes. Loading it needs the same mods, versions and content, in the same order; presentation-only mods and the online
extension may differ. A save that needs a mod you do not have refuses with `needs <id> <version>`; the load list and the
Continue entries keep it and add "(requires mod <id> <version>)". Exporting a park to a `.tpwpark` package is refused
while such a mod is active, because packages record no mods yet. The asset manifest keys archives of a mod `mod:<id>:/…`
(`data:` and `bonus:` for the game data and the bonus content). Details: [DETERMINISM.md](DETERMINISM.md) "Mods".

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
