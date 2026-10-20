## Coaster HUD cost visibility (October 18, 2026)

The coaster panel's HUD cost now stays hidden in idle height, rotation, tilt, stack and demolition modes, and appears during a successful height drag. Build/loop/move retain their existing cost display. Static panel callers and the height handler distinguish the original mode classes and bit lifecycle; arithmetic, spending and the separate cursor cost are unchanged. UI-047 remains open for the host mode-stack/fallback qualifications.

Synthetic HUD and panel regressions cover the mode matrix, failed/successful press, zero-cost active drag, release, cancellation, mode change and non-height drags. No browser or original-runtime result is claimed.
