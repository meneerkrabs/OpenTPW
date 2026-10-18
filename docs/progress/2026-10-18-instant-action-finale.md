## Instant Action research-finale lifecycle (October 18, 2026)

Completing all research in a writable Theme Park World Instant Action park now creates its
fixed `End` object. The existing advisor still posts advice 167. Repeated notifications reuse
an existing object, including one restored with a layout; binding a loaded economy never
creates a finale merely because its research is already complete. Missing definitions do
not substitute another object or charge the park.

The looping finale needs an exit: creating it alone would leave its music duck and the bank's
winning-sequence guard active indefinitely. A bounded full-screen input owner hides the HUD,
ends tools and blocks park/camera controls while the object runs. Escape without modifiers or
an enabled right click after more than two seconds destroys it through normal object cleanup.
An OpenTPW **Resume Game** button provides a visible, localized exit. The dismissal frame does
not reach the park tools; object removal releases its script, held sounds, particles and music
duck, and the next bankruptcy message is no longer suppressed.

This is a partial RIDES-019 implementation. Full Simulation's player-wide research prerequisite,
the original winning-camera follow and panel, and the post-dismissal profile and dialog changes
remain open. A resumed End receives the bounded dismissal screen, not restored original UI state.
See [the implementation boundary](../reverse/INSTANT-ACTION-FINALE.md).

Asset-free tests cover the mode/edition/visit gates, name matching, missing definitions,
duplicates, origin and seed allocation, restored objects, economy rebinding, bankruptcy,
script/audio/particle cleanup, deferred thing destruction, and HUD input ownership/dismissal.
The local .NET test command could not run because the SDK was unavailable; executable test
results must come from the exact-head CI run. No original game files are included.
