## A guest builds pools in the shared park (October 26, 2026)

A granted guest in a shared park can now use the pools: paint, erase and the lazy river, bridges, and slide towers with their slides ([COOP.md](../COOP.md), "A guest's pools"; `[EXT:online-coop-commands]`, `[EXT:online-coop-permissions]`). This is C3b-3, after the Move tool of C3b-2.

- **A guest's strokes go in batches.** One request per cell would outrun the session's limits, so the cells a stroke crosses go to the host a batch at a time. Each cell is written as the host's own drag over it would write it. Until a batch is back, its cells show as the stroke's ghost.
- **The host's tool is unchanged:** it still paints as the pointer moves.
- **A stroke reports once it is back.** The lay sound plays, and the first refusal is said once, as at the host's release. The done message comes when the guest's own stroke laid the slide's splash.
- **At most four pool requests of a guest are open at once.** The cells waiting to go form one queue for all of a guest's strokes. When the queue is full, the guest is asked to wait.
- **Bridges, slides and Backspace.** A bridge click is a request. In the slide mode, Backspace takes back the last piece of a slide still being laid, after the pieces still on their way. Backspace with no tool on takes a bridge or a pool cell, and a refusal says why.
- **Slide towers** are now the guest's to place, sell, clear and move, and their slide goes with them, refunded.
- **Still the host's:** undoing a whole stroke (shared undo comes later), a wave machine's settings, and tracks and coasters.
- **Headless parks skip the pool water.** A park with no graphics device, such as the tests' headless parks, no longer draws pool water.
