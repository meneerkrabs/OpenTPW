# Offline focus-loss pause

## Bounded host behavior

A visible, offline TPW park that has been observed focused pauses its local
simulation when a later frame observes loss of focus. Returning to the window
does not automatically resume it. The pause button is selected while stopped,
but the chosen running speed remains stored.

Explicit actions resume it:
- The pause key restores the stored running speed. If the user had already
  selected Paused, the key keeps its existing Paused-to-Normal toggle.
- A speed button applies its requested speed and clears the focus pause.
- Resume Game or Back in the Escape menu clears the focus pause. Closing that
  menu programmatically, such as after saving, does not implicitly resume it.

The named modal simulation-pause predicate keeps its existing lifetime. Losing
focus while one of those screens is already pausing the park adds no second
pause latch. A modal opened after focus loss still stops the park after an
explicit speed change clears the focus latch. Existing user pause is independent.

Initial lack of focus is not a loss event. Offscreen runs do not participate.
A replacement HUD starts without the old park's focus history. Read-only visits
and logged-in online sessions are excluded; entering an excluded state clears
only the focus latch before the next simulation tick, preserving user speed and
other modal pauses. Co-op sessions are not yet implemented (see COOP.md), and
future host-only pause ownership must be integrated through the session protocol,
not inferred from local focus.

## Scope limits

This is a host integration, not complete UI-096 parity. Existing public source
annotations identify original application deactivation as a pause source. Native
activation/modal ownership, audio/device suspension and unmapped generic message
boxes remain unresolved. The focus latch does not become explicit user
challenge-message timer suspension, and it does not change the separate
`Hud.Paused` predicate. The advisor receives the existing simulation-paused input.

Focus is sampled at the frame boundary; focus changes that begin and end between
two observations are not claimed to be captured. No native event trace,
original executable bytes or game media accompany this implementation.

## Verification

ApplicationPauseTests uses synthetic, asset-free fixtures and real HUD input,
speed controls and Escape-menu callbacks. It covers repeated loss/refocus,
stored speed, explicit user pause, generic versus explicit menu close, named
modal ownership, initial/offscreen observations, online/read-only eligibility
changes, replacement HUD state and challenge-timer independence. It does not
exercise an actual OS focus event or render a full game scene.

Local C# execution is unavailable in the restricted executor because of .NET IPC
restrictions. Exact-head CI execution is required before merge; no local test
pass or native visual comparison is claimed here.
