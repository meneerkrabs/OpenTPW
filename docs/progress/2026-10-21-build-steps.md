## Smaller park build steps, the lobby's enter-park button and the park door (October 21, 2026)

The browser's park start no longer has build steps of 70-85 ms (published AOT build, Jungle, medians of three runs).

The largest construction step fell from 79 to 34 ms. The frames over 33 ms from the click until 5 s after the park is ready fell from 8 to 2: the entering frame (68 to 56 ms) and the step that reads the advisor's model (about 35 ms). The park is ready about 150 ms later, because the build now spans 63 frames instead of 40.

The CPU profile named the long steps:
- the economy: the whole object catalogue was read inside it, because the official extensions count as mods;
- the gate sign's composition;
- the ride script handler table, compiled at once on the first object's script;
- the guests' and staff's sprite sets and renderers;
- the warm-up of the economy capture.

Each now runs in smaller steps, with the same work in the same order:
- the economy per object archive;
- the sign per mask, effect and 16 relief rows;
- the handler table a few handlers at a time;
- the guests, staff and terrain per sprite set, renderer, mesh and texture;
- the capture's parts one by one.

A step known to be long starts a frame of its own. First calls of the entering frame are made in the build where they change nothing: the blueprint markers, the player progress file's serializer, the sliced serializer and the advisor shader's program. The HUD's first update fell from 15 to 5 ms.

The lobby's enter-park button: in a session that starts with saved parks, the first click read them with the world-save codec's metadata still unmade. That took one frame of about 0.40 s before the theme start showed. The lobby's idle frames now make that metadata in 4 ms steps, and the first click takes about 0.13 s. Later clicks take 0.05-0.07 s, as before.

The park door: the first opening has no hitch (worst frame about 10 ms). The wait for visitors after it is the original's arrival schedule: the first group is due at turn 604 of the park's clock, counted from the park's start, not from the door. In the measured run the first guest came 146 s after the click.

The harness has new `door` and `lobbyEnter` scenarios and a `--cold-mark-ms` option, and reports the cold start's frames over 33 ms. Tests compare the economy, the signs and the opcode table built in steps with the same built at once. The front-end smoke also compares the gate sign's pixels of the park built over frames. Details in docs/BROWSER-PERFORMANCE.md, "Build steps".
