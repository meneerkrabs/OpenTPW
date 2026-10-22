## Co-op: the browser follows a join recorded on the desktop (October 22, 2026)

The co-op join smoke's join phase now runs in the browser's game too ([COOP.md](../COOP.md), "The browser's join"; `[EXT:online-coop-snapshot-join]`). It follows a join that the native smoke recorded on the desktop. This is C2c-2b-2: the desktop host and the browser guest are shown to run one simulation.

- `tools/web-coop-join.mjs` serves a published page and the game's folder on this machine and opens the page with `?coopSmoke=1` in a fresh, headless, muted Chrome. It copies the recorded join in, runs the adversarial join or the measuring one from the front end, and reads the evidence back.
- In the page, the smoke runs under the page's own loop. Any failure in a frame ends the run with a message, and the game runs on afterwards.
- The browser's run passes in both the interpreter build and the AOT build. Every checkpoint digest, the cut, the final digest, the world hash and the save equal the desktop host's.
- A build's work by section now counts a section that ran even when the browser's coarse clock measured no time for it.
