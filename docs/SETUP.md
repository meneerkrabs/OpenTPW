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
mounted CD can qualify: the CD's own `Data` folder is complete game data. A known
negative identity rejects the supplied Theme Park Inc / Sim Coaster retail
`levels/Standard.sam` (SHA-256 `8966c8647104feb1878ff3c3d43f3b2ac6dc89856570f9aa5a5a0bf040e6749e`).
Unknown or modified editions are not classified from generic folders; structural
usability does not certify edition compatibility. The inspector warns, without
refusing, when:

- the language folder lacks `bankrupt.MD2`, `congrats.MD2`, `paused.MD2` or
  `swears.txt`, which the installer copies from the CD's language folders (some banners
  or the chat word filter may then be missing);
- there is no `Movies` folder (the CD can be added for movies and music);
- the folder is read-only, such as a mounted CD: saves then go to a `save` folder in
  the user configuration directory instead of `<game>/save`.

## The setup window

Pages: welcome (with the folders detection found, each with a **Use** button), game
folder (typed path, **Browse…** and drag-and-drop onto the window, with live results
of the inspection), and a summary. Closing the window or choosing **Quit** exits without
saving. The window draws with ImGui because no original art or font is readable before
the game folder is known; it asks only for that folder.

## Changing the folders in game

**Options > Game files** is an original-style screen (BF4 fonts, original window and
button art, labels in the six supported languages) that shows the game folder and the
optional CD for music and movies, changes either with the platform's folder dialog and
removes the CD. The CD is stored in `setup.json` and passed to the game as `--cd-data`
unless that option or `OPENTPW_CD_DATA` is given. The game reads its data at start-up, so
changes apply after a restart. `--setup` still opens the first-run window.

The folder dialog is the platform's own: `osascript` (`choose folder`) on macOS,
`zenity` or `kdialog` on Linux, and the Windows `IFileOpenDialog` folder picker.

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

## Bounded detection and folder validation

Saved-folder validation, automatic discovery and wizard inspection run in
separate child processes, with a three-second request deadline and cancellation.
A mounted drive can block inside the OS even after cancellation; an automatic
scan cannot freeze the setup window or prevent manual validation of a local
folder. At most one automatic search and one inspection can be outstanding.
A user-controlled picker has its own single child slot; lookup and native
dialogs run there instead of probing Linux PATH on the UI thread. Closing the
wizard cancels it and terminates its owned process tree. If no native dialog
tool is installed, the editable folder field remains available.

When a deadline expires the child is killed, and its slot remains reserved until
it actually exits. This prevents repeated requests from accumulating stalled
children. If a manual inspection itself stalls in the kernel, later manual
requests time out until that child exits; the window remains responsive. No
mount is changed or disconnected.

The wizard opens before its optional scan and updates results asynchronously.
Changing a field cancels stale validation; the result applies only to the
current field request. UI scale follows both logical and drawable changes,
including transitions where drawable size stays fixed. The setup window has a
520×420 logical minimum so Quit, Back and Next remain usable.

The live-host discovery test is opt-in (`OPENTPW_INSTALLATION_SCAN_TESTS=1`) and
uses a one-second bounded child; synthetic candidate, timeout, cancellation and
manual-recovery cases run without mounted-game assumptions. The identified
unsupported-edition fixture test uses `OPENTPW_TPI_GAME_PATH`.

The original-style Options > Game files screen reuses these bounded operations.
Game and CD paths can always be typed directly, including when Linux has no
native dialog helper. Closing the screen or disposing GameFlow cancels its
pending inspection and picker work.
