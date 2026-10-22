# Track line completion: bounded UI-031 slice

This implements one existing documented continuation rule: after a live kart or water
line click produces a valid circuit, exactly Ctrl keeps the line tool armed without
a start when no queue follows. The existing queue transition takes priority.
Mixed Ctrl modifiers and clicks without Ctrl retain the prior end behavior.

The public provenance is the existing track-tool-help annotation in
`Level.Tracks.cs` for UI-031. This document adds no original executable material.

## Ownership and implementation

`Level.Input.ClickCellTool` scopes the click's continuation choice with `finally`.
The HUD's own tool click (`ParkHud.UpdateCellTool`, the click a player makes) goes through it
with the frame's modifier mask, so the continuation applies to that click too
(`TrackAddOnLevelTests.TheHudsToolClickCarriesItsModifiersToTheLinesContinuation`).
`RewalkTrack` still performs the same circuit refresh, animation, add-on and opening
work before it decides the local tool continuation. Only `TrackWalkCause.Line`
qualifies. A headless build, restore, placement or edit does not acquire a persisted
modifier from a previous click.

A restart calls `CellBuildTool.Enter` with the same writer and no start, clearing
its ghost, last commit and undo stacks. The in-progress click detects that reset
and does not append its old segment afterward. The next click chooses a start, and the HUD
posts the advisor's line tool help 204 for that click (docs/TRACKS.md, "Messages"); a click on a
tile the ghost refuses ends the tool instead.

This does not expand the co-op command protocol. `docs/COOP.md` still lists kart
and water tracks among part 3's remaining tools. The modifier is local tool state;
no command payload or shared-world result is conditional on it.

## Acceptance and remaining scope

Synthetic real-level kart tests cover a cut and live mend, exact/mixed modifiers,
queue priority, same-writer reset, the next start click, headless/non-Line calls
after Ctrl, and exception cleanup. These tests require exact-head C# CI; they were
not executed locally. They are not rendered or original-executable parity proof.

UI-031 remains open. This slice does not establish all native station/queue-tail
submodes, invalid-line termination, coaster placement-to-track transitions, or
water-specific rendered behavior. It preserves the existing host queue eligibility
predicate rather than claiming that every native queue gate is mapped.
