## Theme Park Inc: the guests' own state machine, phase 3a (November 24, 2026)

The guests of Theme Park Inc's own state machine (the hidden switch `--tpi-native-guests`, still off by default) now choose a ride by Theme Park Inc's score, walk to it, join its
queue, move up and are called to the stand point as Theme Park Inc's guests do. With the switch off, Theme Park Inc and Theme Park World behave exactly as before.
[TPI-GUESTS.md](../reverse/TPI-GUESTS.md) has the details.

- **The ride choice.** The attraction score `0x6101E0` is ported with its gates (special rides, a gender, the balloon and umbrella shops), Theme Park Inc's weights and new-ride words,
  the golden-ticket and upgrade scales at the game thread's 24-bit precision, the weather and generation percentages and the history divisions. The major decision `0x610080` uses it,
  so state 6's ride scan, gated in phase P2, now runs; the minor decision `0x6114B0` runs while the guest walks.
- **The walk to a ride (state 13, `0x616AC0`)**, **the queue (state 14, `0x6176C0`)** and **the queue step (state 15, `0x61A1DB`)** run, with the full branch tree of state 14: the
  call to the stand point, a worn relief object, the umbrella shop's weather reaction, the move-up and its delay, the limit, a refusing coaster or track, the happiness bands
  80/20/10, the toilet, the boredom after 100 turns and the head's turn (two world draws, one turn in ten).
- **The queue functions** (`0x5DFF50`, `0x5E0100`, `0x5E0280`, `0x5DFDD0`, `0x5DFE00`, `0x5DFF10`, `0x5E0520`, FindQueueDestination `0x619530` and the dismissal `0x619700`) are
  Theme Park World's with the field offsets moved, so the queue list is the ride bridge's; the guests' words `+0x2CC`/`+0x2CE` follow it. `+0x21B` is the guest's place in the
  queue states and the ride's roll from state 17 on.
- **The ride's side**: the call (OnAdmittance `0x619670`), the closed or broken ride's head step, the queue edit's re-evaluation `0x6197F0` and the removal message's queue branch.
- **The boarding** (states 16, 17, 19 and 20) is phase P3b's. A called guest enters state 16 as in the original and then boards, rides and leaves by Theme Park World's boarding
  (`[EXT:tpi-twp-guest-rules]`), coming back idle. Every world draw up to the boarding point is the original's; the known differences after it (state 17's missing entry roll,
  Theme Park World's balloon reseeds, sideshow tail and small-portion draws) are listed in TPI-GUESTS.md. The end of a bridged ride writes the thing id into the used history with
  Theme Park Inc's shift (`0x6118E9`), the fare's happiness term reads the happiness at the join (`+0x234`), the ride skips the pre-update's thought draw as state 20 does, and the
  bridged step first runs on the update after the one that entered state 16.

Labels:
- `[EXT:tpi-staff-crt-rand]` covers the CRT `rand()` at `0x61795E` (the delayed weather reaction in an umbrella shop's queue), as `world.Next(SlowestWeatherChangeResponse)`.
- `[EXT:tpi-capability-gates]` now covers the information desk's score (phase P5), the attraction history's ring 10 (no reader) and OnAdmittance's fee word; it no longer covers
  the major decision's ride scan.
- `[EXT:tpi-twp-guest-rules]` covers the bridged boarding from state 16 on until phase P3b.

No new approximation or label is added, and no new CRT `rand()` site was found.

Saves: no new field. A native guest in states 13 to 16 now restores, including a guest saved mid-ride in the bridged boarding; a guest in a queue state must be in the list of
the ride it names. TPI-GUESTS.md states how phase P3b will map or refuse such a mid-ride guest.

Verified with these checks:

- The native oracle `tpi_guest_states_oracle.py` found no mismatch at control word `0x7F` over 1000 scores, 1000 cases of the queue functions and the limit, 1000 cases each of
  states 13 and 14, state 15 and the earlier phases' routines; a separate run of 5000 scores agreed too.
- New tests (`ThemeParkIncNativeQueueTests`) for the score, the limit, a guest that chooses, queues, boards and comes back idle, the links and places, the refusals, the needs bands,
  the head's turn, the umbrella shop's weather draw, the removal message and saves of a queued guest and of a guest mid-ride; a Science play test where native guests queue and ride
  the prebuilt rides, and one for the level's attraction view (choosable, hosted, the special-ride table).
- The Theme Park Inc, save, resume and guest filters pass with the retail data, the Theme Park World filters pass with its data, and the determinism pins do not move.
- The silent, offscreen Science smoke passes with the switch off and with it on.

The original game was not launched, and no binary or data is included.
