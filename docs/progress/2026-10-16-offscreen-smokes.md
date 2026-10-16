## Native smokes render offscreen (October 16, 2026)

The native smokes no longer open a visible Theme Park World window:

- **Offscreen mode**: `--smoke-test`, `--tpi-smoke-test` and `--park-resume-smoke` (also `--offscreen` / `OPENTPW_OFFSCREEN=1` for any run;
  `OPENTPW_OFFSCREEN=0` or `--no-offscreen` opts out) create the SDL window hidden, never show it and render every frame into an offscreen
  `Framebuffer` (a colour texture in the swapchain's format and, like the swapchain, no depth, so every pipeline stays compatible) of the size the window would have had. The swapchain is never presented,
  so macOS drawable throttling of hidden windows cannot time a smoke out, no focus is stolen and a locked screen no longer fails the
  fullscreen-toggle step (`CGSSessionScreenIsLocked`).
- **Pacing**: nothing paces an unpresented target, so offscreen runs without a frame cap are paced like a 60 Hz display (`--no-vsync` unpaces them); otherwise
  wall-clock-timed work spans far fewer frames than the smokes' frame budgets allow.
- **One present target**: the renderer, the overlay pipelines' output description, the output capture and the smoke's size checks go through
  `PresentTarget` (`SwapchainPresentTarget` or `OffscreenPresentTarget`) instead of the swapchain directly.
- **Window state**: an offscreen `Window` keeps a virtual logical size (windowed size, or the desktop size for borderless) instead of
  asking SDL, and reports itself unfocused (as the window of a visible smoke is; a focused one would edge-scroll the park camera from the pointer's corner position and drift the screenshots). The sandbox smoke's fullscreen toggle therefore verifies the logical window change and logs that
  the OS-level fullscreen transition is skipped; run it with `OPENTPW_OFFSCREEN=0` to cover that part. Normal game runs stay visible.
