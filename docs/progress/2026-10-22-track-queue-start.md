## The placement's queue start for the kart and water line tools (October 22, 2026)

A kart or water ride the player has just placed goes on into its queue tool once its circuit closes, as in tp.exe. Any later close of the circuit is decided by the click's modifiers, and help 204 is reached with the shipped rides. OpenTPW used to open the queue tool on every close of a ride with a queue, and every shipped kart and water ride has one. A line click whose ghost has a refused tile now ends the line tool and lays nothing, also when the line has a start ([TRACKS.md](../TRACKS.md), "Messages"). This closes the gap left open by the help 204 port of the same day.

- **The queue start.** A placement of a type with an entrance stores the record of its queue's start cell in `0x8186C0` (`0x52A050`, the cell in `0x7639B8`/`0x7639BC`). Four placement sites set it: `0x524DD7`, `0x525269`, `0x52544E` and the Move tool's `0x5258AC`. When a line closes the circuit, the handler tests it (`0x526F95`). While it is pending, the queue tool takes over at the stored cell, whatever the click held, and the handover zeroes it (`0x527056`). With none pending, exactly Ctrl keeps the line tool without a start (`0x527146`) and any other modifier word ends it. Other code zeroes it too:
  - the build tool's and the Move tool's click, before they place (`0x524B53`, `0x5253F3`);
  - the Move tool's pick-up, a click in mode 0x33 (`0x525E5A`);
  - a path or queue commit, after its LayLine (`0x527483`);
  - the queue tool that follows any other ride's placement (`0x5252EA`), and the coaster tool's own handover to it (`0x526680`).

  The cross-reference list of `0x8186C0` has no other writer.
- **The port.** `Level.EnterPlacedTrackTool` opens the track tool of a placed kart or water ride and leaves the ride's queue start pending. `ParkHud.FinishPlacement` calls it. `Level.ContinueTrackTool` hands a line's close to the pending ride's queue tool and takes the start. With none pending, the close goes to the exactly-Ctrl rule. The clicks above drop the start:
  - `Level.ClickBuildTool`, the build and Move tools' click, now its own method;
  - `Level.BeginMove`;
  - a path or queue commit in `Level.ClickCellTool` (a click on a refused ghost commits nothing and keeps the start);
  - `Level.FinishCoasterTool`.

  The start is local tool state of the issuer, never a command, saved or hashed. It may belong to another ride than the circuit that closes, as in the original. A start whose ride was sold since is dropped: an OpenTPW guard, since its queue tool needs the ride.
- **The started-line refusal.** The kart and water line modes preview the line from the start (`0x52452E`: `0x53AAC0` with mode | 0x100). The preview walks the line tile by tile and stops at the first tile the ghost routine `0x5370E0` refuses: its chooser `0x539760` returns 1, and its money test at `0x539F95` counts too. The refused branch sets the latch (`0x537A1B` → `0x527940(1)`). The click handler tests that latch before the mode dispatch whether or not a start is set (`0x524A63`), and the mode table sends 0xB and 0x10 to `SetMode(0, 0)` (`0x5266A3`). `CellBuildTool.Click` therefore ends the line tool and lays nothing for a refused tile anywhere on the line, as it already did for the path and queue tools and for the line tool's first click. Before, it laid the line up to the refused tile, or up to the money.
- **What players notice.** Closing a kart or water track through the ride window's track button no longer opens the queue tool, and neither does a close after a build click, a Move pick-up or a queue line since the ride's placement. Exactly Ctrl keeps the line tool there, any other click ends it, and the kept tool's next click gives the advisor's help 204. Right after the placement nothing changes: the close goes on into the queue tool, Ctrl or not. A line that crosses a blocked tile, or that the park cannot pay for in full, is not built at all, and the line tool ends with the refusal cue. The README and the website now say 236 of the 248 messages are spoken.
- **Documentation.**
  - UI-031's text (`UiApproximations.cs` and the docs/UI.md row) gives the line tools' handover with the pending start.
  - UI-031 also notes that the coaster tool's click hands over to the queue tool only while a start is pending (`0x52656E`) and otherwise ends the tool (`0x5266BC`). OpenTPW's coaster flow differs, as UI-031 already records. No new APPROX ID.
  - `docs/TRACKS.md` ("Messages"), `docs/TRACK-LINE-CONTINUATION.md`, `docs/LIPS.md` and ADVISOR-SCAN.md drop the "not reached yet" notes.
- **Tests.**
  - `TrackAddOnLevelTests` (the new `TrackAddOnLevelTests.QueueStart.cs`), on the asset-free kart level:
    - a placed ride's close hands over to its queue whatever the close held (no modifier, exactly Ctrl, Ctrl+Shift), and the handover uses the start up;
    - with no start pending, a ride with a queue keeps the line tool for exactly Ctrl and ends it otherwise;
    - a build click (a refused placement included), a queue commit and the Move tool's pick-up each drop the start, and a refused queue click keeps it;
    - a line with a start across a queue cell, or across more tiles than the park can pay for, lays nothing, charges nothing, issues no line command and ends the tool.
  - `TheClickThatGivesTheKeptLineToolItsStartPostsHelp204` also runs for a ride with a queue.
  - `AudioRegisterRound2Tests.ARefusedTrackToolClickThroughTheHudPlaysTheRefusalCueAndNoTrackClick` also covers a line with a start: no track click 0xCE, and the refusal cue 0xAF on release.
  - `AFollowingQueueWinsOverExactlyCtrl` is replaced: the track button's line tool has no queue start pending.
  - Each part was taken out once to check that a test catches it. Without the start set at placement, its use at the handover, any one of the three drops, the refused-click exception, or the started-line refusal, its own tests fail. With the old rule, which follows the queue on every close, eight tests fail.
