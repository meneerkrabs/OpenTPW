# The end-of-park finale

The finale is what the original plays when a park has nothing left to research: the fixed
object `End` (its script `End.RSE`) is built at the park's fixed-item origin, the camera rides
its camera seat, the HUD disappears and the language's "congratulations" banner squeezes, rises,
spins and stretches over the park until the player leaves with Escape (or a late right click).
OpenTPW now plays it as the original does, in Instant Action and in the Full Simulation. The
addresses below are those of the Windows `tp.exe` v2.0 (`TPW-EXE`).

## When it starts

The lab's completion-message handler (`0x503D40`) checks the remaining research after the
completed item and group notifications. When nothing is left:

1. It marks the current park's record in the player's file complete (`0x5B0CA0`, call at
   `0x503E94`), in **every** game type, before the game type is tested.
2. Game type 2 (Instant Action) posts advisor message `0xA7` (167) and starts the finale
   (`0x516B00`). The call to `0x409CD0` constructs the advice's 16-bit handle argument; it
   clears no flag.
3. Game type 0 (Full Simulation) does the same only when `0x5B0C40` returns 1: every park's
   record has its research complete (`0x503EF7`).
4. Any other game type (1) returns without either.

`0x503D40` runs once per completion message, so a park opened with its research already
complete never starts the finale.

### The player's record list (`0x5B0C40`)

The player object (`0xF7C58C`, constructor `0x5AEF50`, one per player of the original's four)
keeps a map from theme name to a 0xBC-byte "global save info" record (`+0x3C`). The player file
(`0x5AFF50`, loaded by `0x5AFD70`, written by `0x5AFC60`) stores every record through `0x5B0D70`:
six bytes, four (byte, int) pairs, 33 pairs of sign names (`mSignNameA`/`mSignNameB`),
`mNameChanged` (`+0xB8`) and `mAllResearchCompleted` (`+0xB9`, string `0x773320`).

- Choosing a player (`0x5C83B0`) loads its file and then makes sure every theme of the theme
  list (`0x786B90`: the level folders `jungle`, `hallow`, `fantasy`, `space`) has a record
  (`0x5B09C0`; a missing one is created by `0x5B0A70` with the byte 0). Entering a theme makes its
  record the current one (`+0x48`, `0x5B08A0`).
- `0x5B0CA0` sets the current record's byte to 1. Nothing ever clears it.
- `0x5B0C40` walks the map and returns 0 at the first record whose byte is 0, else 1.
- The file is written with the park saves (`0x5AC780` → `0x5C8A10`).

So the Full Simulation's finale needs the research of all four parks completed by the same
player, in any game type; the park whose research has just completed counts, its byte being set
just before the test.

**OpenTPW.** There are no player profiles (`[EXT:no-player-profiles]`), so the list belongs to
the one player and lives in the own progress file (`opentpw-player-progress.json`) beside the
golden tickets and keys: an optional `ResearchCompleted` array of the canonical theme names
(`PlayerTicketLedger.ResearchCompleted`). Older files load with no park recorded; a file without
any recorded park does not write the field. `Level.ResearchRecords` connects a park to it:

- a Full Simulation park through its progress owner (`PlayerProgressRuntimeOwner.RecordResearchCompleted`,
  saved at once, also in a theme whose tickets cannot be earned, since `0x5B0CA0` checks no ticket
  setting);
- an Instant Action park, which keeps no tickets or keys, by marking the record in the progress
  file directly when one exists. Without a progress file there is no player to record against.
- A park in a co-op session neither records nor reads (`[EXT:online-coop-snapshot-join]`), so every
  peer's park takes the same decision. Read-only visits and Theme Park Inc never start the flow
  (Theme Park Inc's twin of `0x5B0CA0`, `0x74FBA0` in its no-CD `Game.exe`, is reached only
  through an incremental-link thunk nothing calls, and its executable has no finale strings).

The advisor posts 167 under the same test (`Level.ResearchCompletionStartsFinale`).

## The finale object

`0x516B00` finds the case-insensitive definition name `End`, constructs it at the fixed-item
origin with the common constructor `0x4DB090` (call `0x516B8B`), which counts the type, stamps its
first-purchase turn and allocates its thing slot, and stores its thing handle (`0x7CF4F4`). OpenTPW
builds it through `ParkObjects.Add`: an uncharged economy record (`Level.BindFixedItemMadeInPlay`;
End's price is 0) and a slot in the park's thing pool, in both game types. `0x516C00` answers
whether the handle is nonzero; the bank handler uses it to suppress bankruptcy while End lives.
A catalogue without End starts nothing (the original logs "Could not find the %s feature" and
would go on with a null thing).

## The winning screen (`0x481B60` → `0x48AE70`)

1. The previous camera mode ends (`0x42AE70` for the terrain camcorder, `0x42A190` for a ride).
2. **The winning camera.** `0x4E15B0(0)` on End is the ride window's Ride it! routine:
   `0x42A560` looks up camera seat 1 (`0x44B220(model, 0x1000, 1)`) and enters the seat camera
   (mode 4). Every shipped `End.MD2` has it: the first dummy attribute, type `0x1031`, value 1,
   node 38. The camera then follows the seat as End's animation moves it, with the ride view's
   free look (`Level.EnterWinningView`, `RideCameraMode`); its look ignores the window over the
   view (`0x42B8C7` reads the pointer against the park view's centre). Entering it shows the eye
   camera's static (`0x54FAF7`) like any ride. A model without the seat leaves the camera alone.
3. The key tables are disabled (`0x40CFA0`), an open object window is closed, and the HUD and
   camcorder windows are hidden (message 6).
4. A full-screen window `0x7472` gets the layout table `0x74FAF0`: one window `0x321` at
   (102,536)-(1946,940) drawing the model `s_congrats`. That model is not in `ui.wad`: it is the
   language folder's `congrats.MD2` (texture `cong_grad`), which the UI start-up `0x4893C0`
   registers with the `ui.wad` models, beside `bankrupt.MD2` (`s_bkrupt`) and `paused.MD2`
   (`s_paused`). OpenTPW's registry does the same (`UiModels`, `GameLanguage.EnumerateMeshes`,
   reloaded when the language changes); the layout generator resolves the name from the language
   folder.
5. The banner is disabled (`0x65DA8D(0)`; its model has only the root frame) and animated with
   the table `0x74FCF8` at 35 ms (`0x65E72D`, stepped by `0x65E9A4`, `UiWindowAnimation`): six
   steps narrowing it by 16 at each side, four lifting it by 4, eight setting its angle to 512 ×
   1..8 (4096 a turn), four lowering it by 4, six widening it back, then a jump to the start. A step
   runs when strictly more than 35 ms have passed since the last; the jump takes a step of its own.
   The angle turns the model clockwise on the screen (`0x476C50`: a rigid turn in the 4:3 view)
   about the pivot `0x65F345` was given: half the window's right and bottom edges, (973, 470),
   not its middle.
6. The handler `0x48A970` gets the focus. The window has no button.

The simulation keeps running under it (End's looping script ducks the music, holds its sounds and
runs its particles). In OpenTPW the screen owns all input until it is dismissed, including Pause
and the park-door shortcuts.

## Leaving it (`0x48A970`)

- Escape (key `0x1B` with modifier bits `0xFF0000` clear, `0x48A99E`) or, with `RMBCancelOn`, a
  right click more than 2000 ms after the window's creation (`0x48AADB`, the stamp taken on message
  `0x15`). Nothing else.
- Both call `0x516C10`: End is destroyed (`0x50B780`), its handle cleared and the seat camera left
  (`0x42A190`). The script's destructor clears its music duck (`0x558513`).
- The HUD is shown again, the camcorder window stays hidden, the key tables are enabled
  (`0x40CF60`), the winning flag `0x7C24D0` is cleared and the focus returns (`0x4862A0`).
- In game type 2 only, the handler then opens the generic message box `0x47F020` (table
  `0x74F920`: the `wdialog` window, white centred wrapped text, three button slots from the bottom)
  with UITEXT 471 ("Congratulations! Now you've completed Instant Action mode…", OpenTPW's
  `CongratulationsInstantAction`) and the cross as its only button, without an action. The box
  pauses the park until it closes (`0x47F26C`), as OpenTPW's `winningMessage` screen does.
- The dismissal writes nothing to the player's file: the record was written at the completion.

In OpenTPW the exit is a `DismissFinaleCommand` (`[EXT:online-coop-commands]`); an End removed in
another way (a co-op peer's dismissal) closes the screen and leaves the camera without the message.

## Resuming a park with End

The original never saves the winning handle: `0x7CF4F4` is written only by `0x516B00`,
`0x516C10` and a start-up initialiser, and while the screen is open the key tables are off and the
only window takes Escape itself, so no original save is made during the finale. OpenTPW's own
saves can be (for example its automatic checkpoints). A park restored with a live End enters the
same winning screen as a new finale (camera seat, banner, input ownership and bank guard), not a
substitute; it creates no second End and replays no completion.

## Tests

`ParkFinaleTests` (the object, the records' gate in both game types, the winning screen and its
exits, the message box and its pause), `PlayerResearchRecordTests` (the record list and its file),
`UiWindowAnimationTests` (the table, its timing and the clockwise turn about the pivot) and, with
the game data, `ParkFinaleCorpusTests` (End's seat in every theme, the banner meshes in the UI
registry). The layout tables `WinningScreen` and `MessageBox` are generated by
`tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py`, which now takes the language folder.
