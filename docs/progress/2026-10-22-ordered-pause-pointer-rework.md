## Ordered pointer routing: rework after the first review of PATH-017 (October 22, 2026)

A first independent review of the ordered pointer routing (PATH-017) asked for rework. Each point is fixed and covered by a test; PATH-017 stays open.
- **Injected clicks:** once the park routes ordered input, a missing batch is "unknown" and starts no gesture, so the native front-end smoke's `UiInput.Click` was dropped. `GameFlow` now turns injected pointer edges into the batch a real frame would have produced (`UiInput.WithSyntheticOrderedEvents`: a move, then press and release).
- **Windows opened after P:** `0x65db25` picks the last-created sibling, so the P window's backdrop covers only what existed when P created it. An Escape menu, confirm box or message box opened afterwards is above it and can be clicked. The 'lolight' backdrop (`0x65dd7b`, flags 1) was checked in tp.exe to be an eligible receiver (flag bit 0x2 clear, byte +4 clear, bit 0x1 set).
- **Only P creates the backdrop:** the sole caller of the window toggle `0x4a27f0` is the P key thunk `0x48a720`. The speed buttons, the numpad and a park loaded paused no longer create it, so the `>` button stays clickable after a `||` pause.
- **Click timing:** the desktop observer reads each SDL event's own timestamp (widened across the 32-bit wrap) and the browser sends the DOM `event.timeStamp`, so one long frame cannot turn a quick click into "not a click".
- **Unmapped SDL buttons:** an SDL button Veldrid cannot map (it files it as Left) is skipped on both sides instead of rejecting the whole batch.
- **Browser presses** send their coordinates and time like releases do.
- **Memberships:** `UiScreen` drops a removed element's generation, and a re-added element takes a new screen-wide one.
- **Globe:** the frame path qualifies a city click with `NotClick`, as the routed path does.
- **Tests:** the pointer tests use test hooks on `Input` and `Level` instead of reflecting into their private fields, and cover injected input, the speed buttons, the numpad, a window pushed after P, the wheel and click timing.
