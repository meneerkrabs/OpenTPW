## Rights per seat, and one release for a seat that leaves (October 26, 2026)

A shared park now keeps each guest's rights as five separate switches ([COOP.md](../COOP.md), "Money and rights (C4)"; `[EXT:online-coop-permissions]`, `[EXT:online-coop-speed]`). This is C4a, the first part of C4 (money and permissions). The switches are building, attractions, staff, research and finance. C4a still offers only the building switch; the windows that let a guest use the others come in C4b (attractions, research, finance) and C4c (staff).

- **Every command has one class.** The rights sort each kind of command into exactly one class: a guest's building, every player's own cleanup, one of the four management rights, the host's tools, the host's alone, or refused in a session. A test pins each class's kinds.
- **The host's management stays the host's.** The host can still open and close a ride a guest is editing, as in C3. A guest cannot manage anything yet: no management right can be given until its window waits for the host's answer.
- **One reducer, two commands.** A new command sets a seat's rights. The C3 build switch flips only the building bit through the same code, so old session logs replay unchanged. A hand-made command that gives a right this build does not offer is refused.
- **Taking building back lets go of the guest's ride claim** in the same record, whichever command took it back.
- **One release frees a seat.** When a guest leaves, one record takes its rights, its carried object and its ride claims back. The guest who next takes the seat starts with nothing.
- **The host's end releases every guest in its own log.** These records come before the log's last checkpoint, so the host's log replays to exactly the park the host keeps playing. A coaster a guest was editing leaves the tool that way too.
- **New formats.** A late joiner now receives every seat's rights and the pending hires seat by seat (join state version 5). The session's checks cover them (digest version 3). Until C4c only the host can have a pending hire.
- **Speed is the host's.** A guest's speed buttons are drawn greyed out but still react to a click, and they show the host's speed. Every speed input from a guest says that only the host can do that: the buttons, the numpad time keys and the pause key.
