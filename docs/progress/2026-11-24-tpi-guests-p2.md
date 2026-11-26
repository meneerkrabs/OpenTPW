## Theme Park Inc: the guests' own state machine, phase 2 (November 24, 2026)

The guests of Theme Park Inc's own state machine (the hidden switch `--tpi-native-guests`, still off by default) now idle, wander, play their spot animations and
get rid of their litter as Theme Park Inc's guests do. With the switch off, Theme Park Inc and Theme Park World behave exactly as before.
[TPI-GUESTS.md](../reverse/TPI-GUESTS.md) has the details.

- **Idle (state 6, `0x614A40`)** runs in full. It takes one world draw first, then, in the original's order:
  - the mood animation of a happy guest (by generation and the sprite bank's gender) and the vomit of a sick one, both through the spot animation `0x60FB30`;
  - the litter urge, with Theme Park Inc's bin search `0x618D10` (the bins' script variables and their stand point) or litter on the cell;
  - the leave gate, which goes on to the later steps, as the original's does;
  - the turn towards a show and the reaction to a performing entertainer (`0x614D41`), which the entertainer's port had left gated;
  - the prank gate on Standard.sam's prank words and the prank switch: the stinkbomb, the toilet prank and the litter prank, with the prank report as an event; the bin prank's search and
    the start of rumours and whinging, which end the update as the original's do;
  - the four-way decision: the can't-decide pause, the wander, and the two `rand()` draws towards an infodesk or another guest, which are world draws.
- **Wander (state 7)** and **the walk to a bin (state 9)** run, with the wander flag, state 7's entry and the bin's `VAR_USE`, capacity and fire tests.
- **The removal message** `0x60D120` sends a guest whose target is demolished back to idle.
- **The freeze** `0x60FF40` is UI only (the park view's input handler) and is documented, not ported.

Labels:
- `[EXT:tpi-staff-crt-rand]` now also covers the two draws of `0x615318` and `0x615328`, as `world.Next(0x8000) < 0x3FFF` (the lead's decision of 2026-11-23).
- `[EXT:tpi-capability-gates]` covers:
  - the prank switch's state entries of the bin prank (phase P4) and of rumours and whinging (phase P5), the plant search (phase P4) and the balloon burst, which no native guest can
    meet before phase P3 (a restore refuses a native guest holding a balloon);
  - the major decision's ride scan (phase P3);
  - the infodesk and the walk to another guest (phase P5);
  - the prank thought 0x15;
  - the removal message's ride and queue branches.

  The entertainer reaction is no longer under it.
- `[EXT:tpi-no-maintenance-wear]` keeps the bins' fill: a dirty bin's RegionFX rows are not changed.
- `[EXT:tpi-twp-guest-rules]` keeps the wander destination, the walk and the route search.
- `[EXT:original-undefined-reads]` covers a bin walk whose target no bin holds any more.

No new approximation or label is added, and no new CRT `rand()` site was found.

Saves: the native block gains one optional member, the wander flag `+0xE8`, which phase P1 did not hold; the guest snapshot schema does not change. States 7 and 9 now restore.

Verified with these checks:

- The native oracle `tpi_guest_states_oracle.py` runs at the game thread's 24-bit precision. It found no mismatch over:
  - 1000 cases of state 6 over the prank codes 0..8, with the searches stubbed;
  - 3200 spot animations and 3000 mood cases;
  - 1000 cases of the dispatch in states 7 and 8;
  - 1000 cases of state 9;
  - the phase P1 routines.
- New tests for the idle branches, the entertainer reaction (also against a hired and performing Science entertainer), the pranks, the decision, states 7 and 9,
  the removal message and the save.
- The Theme Park Inc, save, resume and guest filters pass with the retail data, the Theme Park World guest and determinism filters pass with its data, and the
  determinism pins do not move.
- The silent, offscreen Science smoke passes with the switch off and with it on; its walk check no longer sends the idle guests home first, because idle guests now act.

The original game was not launched, and no binary or data is included.
