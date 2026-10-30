## Title caches recover their budgets on cache-hit frames (October 27, 2026)

The title bitmap cache retries its entry and pixel-byte budgets as each frame starts,
not only when composing a new title. Temporary overflow still keeps every title used
in the current or previous frame; once older entries can be dropped, repeated draws
of an existing title no longer prevent obsolete entries from leaving the CPU cache. The newest
bitmap remains cached even if it alone exceeds the byte budget.

Each UI renderer also retires evicted title textures at frame start, independently
of new uploads or whether any titles are drawn. GPU deletion remains deferred through
the renderer's existing deletion queue. The renderers start before the CPU cache's
frame advances, so an eviction is retired at the next renderer frame start.
Synthetic regressions cover byte and entry
overflow, previous-frame protection, hit reuse and GPU-cache retirement scheduling;
they require no original game files or graphics device. Native GPU disposal and live
visual acceptance are not established by these headless tests.
