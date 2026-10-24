## The browser's park view no longer scrolls before the pointer first moves (October 24, 2026)

In the browser, a page that had seen no pointer event read the pointer at (0, 0). That is the canvas's top-left pixel, inside the park camera's edge band, so with the default scroll mode (Pushscroll) the park view scrolled left and forward until the pointer first moved over the canvas. The co-op live acceptance's page runs (C2d-5b) found it: their driver never moved the pointer, and the view ran from the park's middle to its edge within seconds. That driver now rests the pointer at the canvas centre.

**Reproduced first.** The page was published from main (5c7d30a4f, interpreter build) and served with the game folder by `tools/web-perf`'s server. Chrome 155 ran headless and muted, with a 1280×813 viewport. Jungle Full Simulation was entered through the harness's own clicks. A read-only camera probe was added to the page for this check only and is not part of the change.

| Phase | main: pointer the game read → target moved | this change |
| --- | --- | --- |
| No pointer event at all, 8 s | (0, 0) → **168.6 engine units**, to the park's extent clamp at its top-left corner | (-1, -1) → **0** |
| Pointer in the bottom-right edge band, 3 s | 126.5 | 100.8 (stopped by the clamp) |
| Pointer at the centre, 4 s | 0 | 0 |
| Pointer moved out of the canvas (the canvas got its `mouseleave`), 8 s | (-1, -1) → 0 | (-1, -1) → 0 |
| Pointer at the centre, W held for 1 s | 41.9 | 42.0 |
| A real pointer on the top-left pixel, 3 s | 104.5 (stopped by the clamp) | 126.6 |

The suspected second case did not reproduce. A pointer that leaves the canvas does not scroll the view: `opentpw-gl.js` reports a leave as `m -1 -1`, and `ParkCameraMode.Step` already ignores a pointer outside the window before it reaches the edge band. Only the "no position seen yet" state was wrong.

**Fix.** `CanvasInputSnapshot` (`OpenTPW.Web.Veldrid/Input.cs`) now starts with the pointer at `NoPointer`, (-1, -1). That is the page's own report of a pointer that left the canvas, so both states without a pointer read the same, and every consumer already handled that value. It is labelled `[EXT:browser-no-pointer]`. The original ran fullscreen, where the pointer cannot leave the screen, so this is a browser choice, not ported behaviour. The desktop is unchanged: it keeps SDL's positions.

What else changes in a page before its first pointer event:
- The camera keys rest too. The camera takes no input of any kind while the pointer is outside the window (`GuardsSuppressInputAndFocusLossRearmsDrag`), as it already did after a leave. Before this change the keys did work in that state, but they fought the drift: S held for a second against the edge band's forward push moved the view 0 units.
- The ride view's free look looks straight ahead instead of toward the top-left corner: `RideCameraMode` has its own check for a pointer outside the window.
- The game's own pointer is no longer drawn in the top-left corner. The screenshots of the check show the view still at the Lost Kingdom gate after 8 s, and no pointer.
- `tools/web-perf` never moves the pointer, so its park windows measured the view drifting to the park's corner, which in Jungle is bare grass. The view now stays where the park opens. One interpreter build of each side, Jungle, a 20 s steady window: main thread busy 15.5% → 21.2%, frame p95 2.8 → 4.5 ms. [BROWSER-PERFORMANCE.md](../BROWSER-PERFORMANCE.md) notes that park windows from before and after this change are not comparable.

**Tests.**
- `CameraControlTests.NoPointerNeverPushesButAPointerInTheEdgeBandDoes`: no pointer, (-1, -1), never pushes, not even over a UI that leaves the edge band to the camera. A pointer in the band, the top-left pixel included, pushes at the pan speed, and a pointer just inside the band does not.
- `CanvasInputSnapshotTests.APageHasNoPointerUntilItsFirstPointerEventAndAfterALeave`: the browser parser's pointer, fed to the park camera frame by frame. With no pointer event yet, with key events only, after a leave and in a later frame without events, the view stays put. The top-left pixel and the right band scroll it.
- Three mutations were caught: the snapshot starting at (0, 0) again, the camera's guard ignoring negative positions, and an edge band that skips the top-left pixel.
- The camera, input, ride and free-walk tests pass: 84 passed and 2 skipped (with-data ride camera tests).

Still open:
- With no pointer over the canvas the camera keys rest, in a page that never saw the pointer as after a leave. Letting them work there would be a further browser choice, for both states.
