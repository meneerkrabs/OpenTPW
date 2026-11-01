# Lobby key panel: static contract

## Scope

The island lobby's key panel as tp.exe keeps it (layout table Lobby `0x757F60`, controls `0x1E0EA` to `0x1E0EE`),
ported in `FrontEnd/FrontEndMenu.KeyPanel.cs` (round 11, package V). The part that needs the lobby advisor is not
ported: see [LOBBY-ADVISOR.md](LOBBY-ADVISOR.md) and ADVISOR-025. No original executable was run and no original media is
included; this is a static reading of tp.exe, not a reference-run or pixel-parity claim.

Build examined: TPW `sp20_nocd/tp.exe`, SHA-256
`5472f97b5eb28f58b5a49f3a15f6f66adffa6b2625a623f503202c9696d1b805`.

## The panel's controls

- EA `0x1E0EA` (`s_key`, (554,170)-(990,437)): the key frame, two frames in ui.wad: 0 gold (`gold_grad`), 1 grey (`grey_grad`).
- EB `0x1E0EB` (`s_X1`, child of EA, (1045,191)-(1405,416)): the cost digit, ten frames: 0 to 4 gold digits 1 to 5, 5 to 9 grey digits 1 to 5.
- EC `0x1E0EC` ((1688,68)-(1963,186)) with ED `0x1E0ED` (`gkey`, four frames, set to frame 3 at creation, `0x4B8D65`) and EE
  `0x1E0EE` (text); EF `0x1E0EF` is the island's text line (`0x4B90F0`, `0x5B12E0`), which OpenTPW's island name label already covers.
- Layout styles (`0x65BD2F` stores the node's second word at control `+0x48`): bit 1 is visible, bit 2 is "disabled" (the virtual `+0x14` of a
  control is `0x65DA8D`, SetEnabled). EA, EB, EC and EF have style 3, ED, EE and the buttons style 1.

## What the original does

- **Creation** (`0x4B8CA0`, from `0x5E19B8` inside the island view's enter `0x5E18D0`, only while the lobby window `[0xF82884] + 4` exists
  and `[0x7CC4B4]` is zero): the panel is created as a child of the lobby window (`0x65FD0E` with the window procedure `0x4B8B70`), EA is set to
  frame 1 and EB to frame 5 (`0x4B8D8B`, `0x4B8DA2`), EC hidden, ED to frame 3, EE gets font slot 2, the stored count `0x7CC4B8` and the effect
  handle `0x7CC4BC` are zeroed, and the full refresh runs (`0x4B8DD5`).
- **Full refresh** (`0x4B9340`): returns at once without the panel window (`0x4B935D`). The selected island is `[[0xF85614] + 4] + 0xC`; its name
  (`+0x30`) finds the record in the player ledger object (`[0xF7C58C]`, built by `0x5AEF50`) through `0x5B09C0`, valid when the theme's
  `data\levels\%s\global.sam` loads (`0x5B11C0`, format string `0x7733B4`). The record's cost (`0x5B11A0`) is the loaded file's `+0x14`, which is
  `Keys.CostToEnter` (Jungle 1, Halloween 1, Fantasy 3, Space 5). The keys are `0x5AF680`: the four global ticket flags, the six local flags of
  every loaded park and the two secret flags, divided by 3, plus the extra keys (`+0x20`).
  - With a record and game type (`0xFB3B7C`) not 2 (Instant Action): EA, EB and EC are shown (and the arrow buttons `0x1E0F0`/`0x1E0F1` enabled). Keys at or
    above the cost: EA frame 0, EB frame cost - 1, and effect `0x61` created at `0x4B960E` when `[0x7CC4BC]` is zero. Below the cost: EA frame 1, EB frame
    cost + 4, the effect ended (`0x51FEB0`, `0x4B9662`). Game type 2 hides EA, EC and disables the arrows (`0x4B9840(0)`).
  - Counter phase (`0x4B96BE`): game type 2 hides EC; otherwise no keys hide EC (`0x4B97F8`), and keys above 0 show EC and EA and write `"%d x"`
    (`0x758194`) into EE. The count is formatted only on that branch.
  - The count is stored in `0x7CC4B8` (`0x4B9816`, from `0x409410`, the same ledger object).
- **Selection step** (`0x4B90F0`, from `0x5E18BD`, `0x5E1B31`, `0x5E1F35`, `0x5E1F95`): for the new selected island's record it repeats only the
  affordability part, comparing the *stored* count `0x7CC4B8` (not the ledger) with the cost: EA frame 0, EB frame cost - 1 and effect `0x61` (once), or EA frame 1,
  EB frame cost + 4 and the effect ended (`0x4B929B`, `0x4B92EB`); game type 2 hides EA; then it writes the island's text into EF.
- **Window procedure** (`0x4B8B70`): message `0x11` (a visibility change that `0x65D90B` sends down the window tree) with a nonzero parameter refreshes the
  panel (`0x4B8C64`); with 0, and message `0x14` (destroy), it clears `[0x7CC4B4]` and ends the effect (`0x4B8EE0(0, 0)`, `0x4B8F9B`). Messages `0x100` from the
  buttons call the island view's virtuals (`0x1E0E8`: `+0x40`, `0x1E0E9`: `+0x20` and `+0x1C`, `0x1E0F0`: `+0x3C`, `0x1E0F1`: `+0x38`). `0x4B8EE0`'s
  show branch (`0x4B8F6E`) has no caller.
- **Callers of the full refresh**: the creation, the window procedure's message `0x11`, and the advisor update's timer (`0x5999CC`).
- Effect `0x61` is created by `0x521E60(0x61, 0xC350, 0, z)` with z from EA's centre row times 75000 / `0x600`, the arithmetic of the enter-park effect
  `0x62` (`0x4B8FD0`, `LobbyEnterEffect`): 14794.

## What OpenTPW does

- The panel is built with the lobby menu and refreshed once then (`RefreshKeyPanel`, the creation's refresh), with the saved Full Simulation key count
  (`PlayerKeySnapshot`, the same snapshot as before); every island change repeats the affordability part with the stored count
  (`KeyPanelSelectionChanged`); removing the lobby screens ends the effect (`UiScreen.Removed`, the message `0x14` path). The cost is
  `Keys.CostToEnter` of the island's `global.sam` (`GameFlow.ReadLobbyThemeEntryCost`), the same field the entry check uses
  (`ParkObjectives`, `0x5E1CC0`). An island without a readable cost has no record: the affordability part is skipped and the counter phase still runs
  (`0x4B9681`). The layout's frames, anchors and ED frame 3 follow the table.
- No usable Full Simulation progress (missing, invalid or another mode's file) shows no panel and no effect: the original always has a ledger object, OpenTPW
  has none before the first progress file (`[EXT:no-player-profiles]`).
- The effect is created in a screen-space particle host of its own (`LobbyKeyPanelEffect.CreateHost`), deleted with the other entities when the lobby is torn down.
- The Instant Action branch (game type 2: EA, EC hidden, arrows disabled) is not reachable: OpenTPW asks the mode on every park entry.

## What is not ported

- The timer refresh: only the lobby advisor starts it, with response 393 (fixed sequence 28, `0x4A6B4F`), see [LOBBY-ADVISOR.md](LOBBY-ADVISOR.md).
  OpenTPW's lobby has no advisor and its ledger does not change while the lobby is up, so a refresh would read the count it already shows.
- The window hide and re-create of the dialogs that hide the lobby window (the player-select dialog `0x4A6580` hides `[0xF82884] + 4` at `0x4A659E` and
  `0x4A6A50` shows it again at `0x4A6A81`); OpenTPW's dialogs draw over the lobby screen, the panel and effect stay up.
- EF's custom park name (`0x5B12E0` with the record's name flag `+0xB8`) and the player profiles.
- Original visual captures: the frame and digit art are the ui.wad frames above, their placement the layout table's.
