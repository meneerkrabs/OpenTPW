## Co-op: what a join costs, measured (October 22, 2026)

A co-op join now times itself ([COOP.md](../COOP.md), "Measurements"; `[EXT:online-coop-snapshot-join]`). This is C2c-2b-1: the numbers decide which parts of the join are split over frames next, once the browser's are in.

- The host keeps, for each capture: the cut in one frame (the digest, the session state and the save's snapshot), the save's serialization and the compression over frames, and the upload.
- The guest keeps, for each build of a capture, whatever became of it (joined, resynced, failed its check, timed out, abandoned, refused, failed, or ended with the session):
  - the download and the decode;
  - every build stage with its steps, frames and longest step, and every step that alone took the frame's budget;
  - the build's work by section;
  - the check at the cut, the swap and the catch-up.
- The native join smoke writes them into its evidence with every update frame of the join, and requires them whole. A measuring mode joins the untouched captures once each, and `scripts/measure-coop-join.sh` runs it five times in a Release build, with `tools/coop_join_timings.py` printing the medians and ranges.
- First results on the desktop: the guest's worst build step is the restore of the park's objects. It took 0.6 s in a fresh process and 0.2 s warm, in a single frame. The host's cut takes one frame of 5 to 10 ms.
- `scripts/run.sh` builds and runs the Release configuration with `OPENTPW_CONFIGURATION=Release`.
