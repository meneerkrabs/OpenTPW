## Co-op: the join smoke's runner split from its test (October 22, 2026)

The native co-op join smoke ([COOP.md](../COOP.md), "The game's join"; `[EXT:online-coop-snapshot-join]`) no longer owns its end itself. This is the first step of C2c-2b-2, the browser's replay of a join recorded on the desktop.

- The smoke takes its runner's completion and failure handlers. The native runner keeps its watchdog, closes the window on completion and exits on a failure, as before.
- The join phase shows the front end only when none is shown yet (a page's start shows one), and reads its captures only by flat file names in its evidence directory.
- `OPENTPW_COOP_JOIN_PHASE=host` runs the smoke's host phase only.
