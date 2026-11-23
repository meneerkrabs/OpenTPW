## Theme Park Inc: the guests' own state machine, phase 1 (November 23, 2026)

Theme Park Inc guests can now run Theme Park Inc's own state machine instead of Theme Park World's rules. A hidden switch, `--tpi-native-guests`, turns
it on. It is off by default, so the default Theme Park Inc park and every Theme Park World park behave exactly as before. Tests turn it on.
[TPI-GUESTS.md](../reverse/TPI-GUESTS.md) has the details.

- **The machine.** A native guest carries its own record of the constructor's fields. It has:
  - the state setter `0x61A540` with its entry table and the dispatch `0x619FF0`;
  - the name table, the guard's catch test `0x614720` and the queue predicate `0x61AC40`;
  - a projection of its state onto the shared guest state for the HUD and the statistics.
- **The pre-update** `0x619B50` is ported, with:
  - the phase gate, the exit counter, the region words and the needs;
  - the thought draw and pick `0x60FC60`, and the balloon countdown;
  - the history shift, including its overlapping copy;
  - Theme Park Inc's own weather rule `0x611050`: the generation's row, the heat wave included, on every phase update of a qualifying day.
- **Arrival and leaving.**
  - The guest is built at the vehicle's stop. Off the park it makes two draws that the original discards, then walks to a ticket booth.
  - At the booth: Theme Park Inc's verdict, the wait for the gate (state 3), the walk to the entrance with the visitor number, idle.
  - Leaving: the walk to the bus stop (22), the wait for the pick-up (30), and the bus takes the guest.
  - The vehicle dispatch builds a new bus, heli or mono each time.
  - In this phase, idle (state 6) runs only its leave gate.
- **Settings.** The guest states read Standard.sam's words by their addresses in the Standard object, and a test checks each address against its key.
- **Saves.** The guest snapshot is now schema 10. A native guest saves an optional block with all of its fields. A save made with the switch off holds
  no such block.

Labels:
- `[EXT:tpi-staff-crt-rand]` now also covers the guests' `rand()` sites. Its text was widened on 2026-11-22 (user decision). The weather delay
  `0x613F18` is the one this phase reaches.
- `[EXT:tpi-capability-gates]` covers:
  - the state 6 stub and the entries of the later phases' states;
  - state 5's tutorial tips;
  - state 30's tag mark;
  - the balloon's litter.
- `[EXT:tpi-twp-guest-rules]` keeps the walk, the route search, the speed, the thought bubble's timing and the analyser's litter blocks.
- `[EXT:tpi-bus-from-setup]` now holds only on the default path.

No new approximation or label is added, and no new CRT `rand()` site was found.

Verified with these checks:

- The native oracle `tpi_guest_states_oracle.py` runs at the game thread's 24-bit precision. It found no mismatch over:
  - the catch test for every state word from -1 to 40;
  - the setter's phase 1 entries;
  - 1000 pre-update cases;
  - 1000 cases of the booth's verdict.
- New tests for:
  - the tables and the switch;
  - the constructor's draws and the walk from the stop into the park;
  - the setter's entries, the pre-update and the weather rule;
  - the booth's verdicts, the leaving states and the vehicle dispatch;
  - the save block and the settings' addresses.
- The Theme Park Inc, save, resume and Theme Park World guest and determinism filters pass with the retail data.

The original game was not launched, and no binary or data is included.
