# First-run setup

OpenTPW needs the files of the original Theme Park World and ships none of them.
Players no longer have to pass `--game-path`: on the first start OpenTPW looks for
the game itself and, when it finds nothing, opens a small setup window.

## How the game folder is chosen

In this order; the first that applies wins:

1. `--game-path <folder>` on the command line.
2. `OPENTPW_GAME_PATH`.
3. The folder saved by an earlier setup (`setup.json`, below).
4. The folder in the old application setting (`Settings.Default.GamePath`, whose
   default is `C:\Program Files (x86)\Bullfrog\Theme Park World`).
5. Automatic detection (`InstallationFinder`): the folder of the OpenTPW executable
   and its parent; on Windows the `Bullfrog Productions Ltd` and `Electronic Arts`
   registry keys (every string value that names an existing folder, because the
   installer's value name is not known) and the usual Program Files folders; Wine and
   CrossOver prefixes on macOS and Linux; mounted discs (`/Volumes`, `/media`,
   `/run/media`, `/mnt`, Windows drives) holding `TP.ICD`, `TP.exe` or a `Data` folder.
6. The setup window.

The first two are developer overrides and are used as given, so a wrong path still
fails with the old message. Every other folder must pass `GameInstallation.Inspect`,
which also corrects a selected `Data` folder to its parent. A folder found by
detection or the old setting is saved, so detection does not run again.

## What counts as game files

A folder is usable when it has a `Data` folder (any spelling) with `levels`, `global`
and at least one `Language/<name>` folder. An installed copy, a copy of the CD and the
mounted CD all qualify: the CD's own `Data` folder is complete game data. The inspector
warns, without refusing, when:

- the language folder lacks `bankrupt.MD2`, `congrats.MD2`, `paused.MD2` or
  `swears.txt`, which the installer copies from the CD's language folders (some banners
  or the chat word filter may then be missing);
- there is no `Movies` folder (the CD can be added for movies and music);
- the folder is read-only, such as a mounted CD: saves then go to a `save` folder in
  the user configuration directory instead of `<game>/save`.

## The setup window

Pages: welcome (with the folders detection found, each with a **Use** button), game
folder (typed path, **Browse…** and drag-and-drop onto the window, with live results
of the inspection), an optional CD for music and movies (stored and passed to the game
as `--cd-data` unless that option or `OPENTPW_CD_DATA` is given), and a summary.
Closing the window or choosing **Quit** exits without saving.

**Browse…** uses the platform's own dialog: `osascript` (`choose folder`) on macOS,
`zenity` or `kdialog` on Linux, and the Windows `IFileOpenDialog` folder picker. The
window has no game fonts yet, so it draws with ImGui. Start OpenTPW with `--setup` to
open it again and change the folders.

Tool and test modes never open the window: `--smoke-test`, `--validate-assets`,
`--inspect-model`, `--inspect-rides`, `--headless`, `--export-park` and
`--import-park` fail as before when no folder is known, and `OPENTPW_NO_SETUP=1` does
the same for any start.

## Stored settings

`setup.json` sits beside `display.json` in the user configuration directory
(`OPENTPW_CONFIG_DIR`, else `~/.config/OpenTPW` on macOS/Linux and `%APPDATA%\OpenTPW`
on Windows):

```json
{
  "gamePath": "/home/alice/Games/Theme Park World",
  "cdPath": "/Volumes/THEME_PARK"
}
```

An unreadable file is ignored with a warning and the setup asks again.

## Background and limits

The flow follows OpenRCT2's first start (search known places, then ask, then
remember). OpenRCT2 is GPLv3 and OpenTPW is MIT, so only the idea was used; no code
was copied. OpenRCT2's GOG and Steam installer paths have no counterpart here; copying
the CD into a user folder is not offered yet (the CD works directly).

Tested on macOS (Apple Silicon). The Windows folder dialog, the Windows registry and
drive search, and the Linux `zenity`/`kdialog` dialogs are compile- and unit-tested
only.
