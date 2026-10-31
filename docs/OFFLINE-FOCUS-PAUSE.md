# Focus-loss pause

## Bounded host behavior

A visible TPW park that has been observed focused pauses its local
simulation when a later frame observes loss of focus, as tp.exe's application
deactivation does (`0x46B74C` -> `0x4092A0(1)`, which builds the P window). The
word `[0xF82884]` that the routine tests is 0 in every park (only the front end
sets it), so a logged-in player's own park pauses too. Returning to the window
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
and co-op sessions are excluded (`[EXT:online-coop-lockstep]`: their clock is the
host's); entering an excluded state clears only the focus latch before the next
simulation tick, preserving user speed and other modal pauses. Logged-in
sessions are not excluded.

## Scope limits

The pause itself matches `0x46B74C`. What `[EXT:offline-focus-pause]` still
covers is host plumbing: focus sampled per frame, offscreen runs and an initial
lack of focus not counting as deactivation, and the stopped state shown through
the speed buttons with the chosen speed stored. The mixer's suspension on
deactivation (`0x51BCF0`) is ported with the rest of the pause's sound side
(`GamePauseSound`, docs/AUDIO.md "Pause sound"; UI-096 retired). The focus latch does not become explicit user
challenge-message timer suspension, and it does not change the separate
`Hud.Paused` predicate. The advisor receives the existing simulation-paused input.

Focus is sampled at the frame boundary; focus changes that begin and end between
two observations are not claimed to be captured. No native event trace,
original executable bytes or game media accompany this implementation.

## Verification

ApplicationPauseTests uses synthetic, asset-free fixtures and real HUD input,
speed controls and Escape-menu callbacks. It covers repeated loss/refocus,
stored speed, explicit user pause, generic versus explicit menu close, named
modal ownership, initial/offscreen observations, co-op/read-only eligibility
changes, replacement HUD state and challenge-timer independence. It does not
exercise an actual OS focus event or render a full game scene.

Local C# execution is unavailable in the restricted executor because of .NET IPC
restrictions. Exact-head CI execution is required before merge; no local test
pass or native visual comparison is claimed here.
