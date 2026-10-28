## Ride presentation from tp.exe (October 28, 2026)

Ride objects now follow the original's list-pass and render-callback cycle (RIDES-032 narrowed, MD2-900 retired):
an instance that was not called back last frame has its aggregate reset to the stored pose on return
(0x4726D0), and event keys, particle requests and walk nodes read the instance's key table (0x44AB30
refresh points) rather than the live hierarchy (PTCL-900 narrowed, PTCL-909 retired: the HUD windows reach
the screen after the particle pass). The track seat order and joining node (TRACK-026) are read and tagged.

The reset lives in a presentation stash the simulation puts back before its next step, so saves, joins
and picks stay view-independent. Coaster trains hold their two loop sounds and their scream pass
(SAVE-923 narrowed to the saved voice handles): speed crossfade, lift, braking and free state parameters,
occupancy, the one-shot and held screams, following the sound car or the camera car, all presentation and
in no hash. The clip sampler's rotation blend now uses the table sines of 0x4742A0 (COAST-033 narrowed); the
pinned digest of DetMatrix.Slerp changes (D65FD1F808A2F9A0 -> 835A114DD5A79937), world pins do not move.
COAST-026 and COAST-034 are not changed in this round.
