# First-run setup

OpenTPW needs the files of the original Theme Park World and ships none of them.
Players no longer have to pass `--game-path`: on the first start OpenTPW looks for
the game itself. Ordinary interactive startup shows the desktop edition starter when autorun is enabled; explicit command-line paths and tool/game modes retain direct startup.

## How the game folder is chosen

An ordinary desktop start (Finder or Explorer, no arguments) goes straight in with the
saved choice, as before the edition choice existed. In this order; the first that applies wins:

1. `--game-path <folder>` on the command line.
2. `OPENTPW_GAME_PATH`.
3. The saved choice in `setup.json` (below): for Theme Park World the saved `gamePath`,
   which must still pass inspection as Theme Park World (a folder holding Theme Park Inc
   data opens the window instead, as do the old setting and detected folders); for the experimental Theme Park Inc the saved folder
   and world, which start that world directly.
4. The folder in the old application setting (`LegacySettings.GamePath`, read from the
   old settings file only, whose default is `C:\Program Files (x86)\Bullfrog\Theme Park World`).
5. Automatic detection (`InstallationFinder`): the folder of the OpenTPW executable
   and its parent; on Windows the `Bullfrog Productions Ltd` and `Electronic Arts`
   registry keys (every string value that names an existing folder, because the
   installer's value name is not known) and the usual Program Files folders; Wine and
   CrossOver prefixes on macOS and Linux; mounted discs (`/Volumes`, `/media`,
   `/run/media`, `/mnt`, Windows drives) holding `TP.ICD`, `TP.exe` or a `Data` folder.

A Theme Park World start then shows the CD autorun screen as before. The setup window
opens only:

- on a first start, when none of the above resolves a usable folder;
- when the saved Theme Park Inc folder or world is missing or invalid (it asks again
  instead of stopping with an error), which includes a re-inspection that times out;
- with `--setup`, which refuses with a message, before any window, when combined with an
  explicit tool mode (`--sandbox`, `--front-end`, `--load-original-level`, `--m3-gate`, ...).
  If the re-inspection after the choice fails or times out, the window stays open with the
  typed folder and the reason instead of stopping.

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
usability does not certify edition compatibility. The experimental Theme Park Inc level
viewer (`--load-original-level`, [THEME-PARK-INC.md](THEME-PARK-INC.md#theme-park-inc-playable-slice))
has its own, wider edition check that also accepts edited Theme Park Inc folders; it never
changes the legacy one-argument inspector. The explicit experimental desktop selection uses the same positive edition evidence, rejects mixed folders and requires an available supported world. It does not enable TPW normal play or the TPW game-files settings path for TPI. The inspector warns, without
refusing, when:

- the language folder lacks `bankrupt.MD2`, `congrats.MD2`, `paused.MD2` or
  `swears.txt`, which the installer copies from the CD's language folders (some banners
  or the chat word filter may then be missing);
- there is no `Movies` folder (the CD can be added for movies and music);
- the folder is read-only, such as a mounted CD or (on Windows, where OpenTPW runs
  without administrator rights) an installation under Program Files: saves then go to a
  `save` folder in the user configuration directory instead of `<game>/save`. Saves an
  earlier OpenTPW wrote in `<game>/save` are copied there once, without replacing files.

## The setup window

Pages: welcome (with the folders detection found, each with a **Use** button), game
folder (typed path, **Browse…** and drag-and-drop onto the window, with live results
of the inspection), and a summary. Closing the window or choosing **Quit** exits without
saving. The window draws with ImGui because no original art or font is readable before
the game folder is known.

The welcome page offers **Theme Park World** and **Theme Park Inc / Sim Coaster (Experimental)**. The experimental choice requires positively identified TPI files and an explicit available **arabian**, **science** or **water** world; Polar and Director are not supported world names. Start re-inspects the chosen folder before remembering it or launching. A changed folder invalidates its inspection, and cancellation does not change settings.

`setup.json` keeps the existing TPW `gamePath`, CD and bonus paths, and adds `selectedEdition` (0 = TPW, 1 = TPI), `themeParkIncPath` and `themeParkIncWorld`. Old files default to TPW. The TPI choice never overwrites the TPW path and never borrows its CD. `--setup` opens the starter explicitly; an ordinary start does not (see [How the game folder is chosen](#how-the-game-folder-is-chosen)). A remembered explicit TPI choice is revalidated and launches its selected world directly, or opens the starter when its folder or world is gone. Explicit CLI/environment paths and gameplay/tool modes take precedence over that remembered choice. In particular, the existing headless `--m3-gate` and `--m4-gate` commands neither open the starter nor acquire a remembered experimental world. Combining either with `--setup` refuses before a window or configuration change; missing TPW data also refuses instead of falling through to the wizard.

TPI Start feeds the existing `--load-original-level` experimental flow rather than the TPW lobby. It has the existing capability limits: no TPI economy, campaign, native saves, advisor or original interface. No original `autorun.exe` or installer is executed, and no TPI autorun artwork is required. Folder inspection is not proof that every world asset decodes successfully; normal runtime validation still applies. Synthetic CPU tests cover selection, migration, stale results and the actual no-window saved-selection resolver. Native starter drawing, GPU gameplay and original-edition parity remain unqualified.

## Changing the folders in game

**Options > Game files** is an original-style screen (BF4 fonts, original window and
button art, labels in the six supported languages) that shows the game folder, the
optional CD for music and movies and the optional bonus content, changes them with the
platform's folder dialog and removes the CD or the bonus content. The CD is stored in
`setup.json` and passed to the game as `--cd-data` unless that option or `OPENTPW_CD_DATA`
is given. The game reads its data at start-up, so
changes apply after a restart. `--setup` still opens the first-run window. On desktop an **Edition...** button
(the browser has no Game files screen) switches between Theme Park World and the experimental Theme Park Inc: the
Theme Park Inc folder and world are checked with the same edition-aware inspection as the setup window, stored in
`setup.json`, and used from the next start (the screen says so).

The folder dialog is the platform's own: `osascript` (`choose folder`) on macOS,
`zenity` or `kdialog` on Linux, and the Windows `IFileOpenDialog` folder picker.

**Bonus content** is the third row. It shows the bonus folder the game uses (`None`
when there is none) and takes the official "Bonus content" folder (the one with `levels`
in it; `Theme-Park-World-Bonus-Stuff.zip` holds 35 bonus WADs). The folder dialog only
selects folders, so a `.zip` is given by its path in the field that opens where there is
no folder dialog; with a dialog, extract the zip and choose the folder. A folder or zip
outside the configuration directory is imported: only the `levels/<theme>/<category>/_name_N.wad`
files are copied into `<config>/bonus/levels/<theme>/<category>/`, and nothing is written
into the game folder. The new copy replaces the previous import only when all of it
succeeded; archive entries that point outside the target (`..` or absolute paths) are
refused, and a zip or folder without bonus WADs is reported and leaves things as they
were. **Remove** stores an empty `bonusPath`, which turns the bonus objects off even if
the imported copy exists; the files are not deleted. The new folder applies after a restart.

At start-up the bonus root is taken from `--bonus-data`, then `OPENTPW_BONUS_DATA`, then
the saved `bonusPath`, then `<config>/bonus` when it has a `levels` folder. The startup log
names the source (OBJECTS.md).

Tool and test modes never open the window: `--smoke-test`, `--validate-assets`,
`--inspect-model`, `--inspect-rides`, `--headless`, `--export-park` and
`--import-park` fail as before when no folder is known, and `OPENTPW_NO_SETUP=1` does
the same for any start.

## Stored settings

`setup.json` sits beside `display.json` in the user configuration directory
(`OPENTPW_CONFIG_DIR`, else `~/Library/Application Support/OpenTPW` on macOS,
`~/.config/OpenTPW` on Linux, and `%APPDATA%\OpenTPW` on Windows; Linux also honours
`XDG_CONFIG_HOME`):

```json
{
  "gamePath": "/home/alice/Games/Theme Park World",
  "cdPath": "/Volumes/THEME_PARK",
  "bonusPath": "/home/alice/OpenTPW/config/bonus"
}
```

`bonusPath` is optional: a file without it (from an older release) means no bonus folder
was chosen. An empty string means the bonus content was removed.

An unreadable file is ignored with a warning and the setup asks again.

The same directory holds a `cache` folder of things the game can make again: `cache/shaders` keeps the compiled shaders between runs
([ENGINE-PERFORMANCE.md](ENGINE-PERFORMANCE.md), "Kept SPIR-V"). Deleting it only makes the next start compile them again.

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

The desktop starter precedence controls also exercise the actual resolver with
invalid remembered edition/world values, both gate flags, and command-line then
environment overrides. Explicit tools leave those saved fields unchanged; a
conflicting `--setup` request refuses before malformed configuration or an
override can act. These are synthetic CPU resolver/argument controls, not native
wizard rendering or headless gameplay qualification. Controlled empty discovery
is injected only in tests; production retains the existing bounded search.
