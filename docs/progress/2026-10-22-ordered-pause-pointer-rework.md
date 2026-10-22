## Ordered pointer routing: rework after the first review of PATH-017 (October 22, 2026)

A first independent review of the ordered pointer routing (PATH-017) asked for rework. Each point is fixed and covered by a test; PATH-017 stays open.
- **Injected clicks:** once the park routes ordered input, a missing batch is "unknown" and starts no gesture, so the native smokes' hand-built `UiInput.Click` was dropped. Only a frame whose platform batch could not be supplied (`UiInput.OrderedUnavailable`, set by `UiInputSource.Poll`) is now quarantined. Hand-built input is routed as injected edges (`UiPointerRouter.AdmitInjected`): a move, a press for a press edge or for a release with nothing held, then the release.
- **Windows opened after P:** `0x65db25` picks the last-created sibling, so the P window's backdrop covers only what existed when P created it. An Escape menu, confirm box or message box opened afterwards is above it and can be clicked. The 'lolight' backdrop (`0x65dd7b`, flags 1) was checked in tp.exe to be an eligible receiver (flag bit 0x2 clear, byte +4 clear, bit 0x1 set).
- **P and the focus-loss pause create the backdrop:** `0x409350` (the P key row `0x40bf77`, the window's own procedure `0x4a2706`) toggles the window, and the offline deactivation path `0x46b74c` -> `0x4092a0(1)` -> `0x4a2790` creates it, so losing focus offline opens it too (ported: the focus latch creates the overlay). Every resume destroys it (`0x409300` -> `0x4a27c0`). The speed buttons, the numpad and a park loaded paused do not create it, so the `>` button stays clickable after a `||` pause.
- **The camera keeps working under P:** the backdrop takes the click without setting `Level.UiCapturesMouse`, so wheel zoom, right-drag and edge scrolling continue.
- **Stale hover help** is cleared when the pointer rests on the park or a backdrop; **P with ImGui holding the mouse** still toggles the pause; a frame without platform time no longer stamps wall-clock time.
- **Open, not settled:** whether a path or pool drag released over the HUD uses the stored hover cell; noted under PATH-017.
- **Click timing:** the desktop observer reads each SDL event's own timestamp (widened across the 32-bit wrap) and the browser sends the DOM `event.timeStamp`, so one long frame cannot turn a quick click into "not a click".
- **Unmapped SDL buttons:** an SDL button Veldrid cannot map (it files it as Left) is skipped on both sides instead of rejecting the whole batch.
- **Browser presses** send their coordinates and time like releases do.
- **Memberships:** `UiScreen` drops a removed element's generation, and a re-added element takes a new screen-wide one.
- **Globe:** the frame path qualifies a city click with `NotClick`, as the routed path does.
- **Tests:** the pointer tests use test hooks on `Input` and `Level` instead of reflecting into their private fields, and cover injected input, the speed buttons, the numpad, a window pushed after P, the wheel and click timing.
