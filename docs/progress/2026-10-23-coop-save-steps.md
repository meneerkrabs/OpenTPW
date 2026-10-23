## A joined park's save no longer stalls the page while it is read (October 23, 2026)

A guest's build of a shared park now reads the park's save over steps ([COOP.md](../COOP.md), "The game's join" and "Measurements"; `[EXT:online-coop-snapshot-join]`). This is the sixth part of C2c-2b-3.

- The save is parsed a 32 KiB slice a step, by the serializer's own asynchronous reader fed a slice a step, and its shape is then checked in steps of their own. The value and every refusal are those of one parse, message for message.
- In the page compiled ahead of time the save stage's longest step takes 15 ms instead of 91, and a resync's 3.5 ms instead of 54. On the desktop the longest step is 15 ms instead of 80.
- The measuring join smoke now counts the garbage collections of every build stage and long frame, and names the parts of the check at the cut and of the level's last step: the two remaining steps of more than 50 ms in the page, both mostly a process's first use of their code.
