## Theme Park Inc: the guests' own state machine, phase 3b (November 26, 2026)

The guests of Theme Park Inc's own state machine (the hidden switch `--tpi-native-guests`, still off by default) now visit an attraction as Theme Park Inc's guests do: they are loaded, wait
for the ride's script, ride under its control, pay, take the use's effects and are let off. Phase 3a's bridged boarding through Theme Park World's rules is gone. With the switch off, Theme Park
Inc and Theme Park World behave exactly as before. [TPI-GUESTS.md](../reverse/TPI-GUESTS.md) has the details.

- **Being loaded (state 16, `0x617F80`)**: the walk to the stand point, the price test, the umbrella shop's weather reaction armed in the queue, AdmitPerson, and the rejoin of a refused guest.
- **Waiting for the script (state 17, `0x61A21D`)**: the entry's chance roll `0x5E7A00` (one world draw against the thing's win byte, into `+0x21B` and the script's VAR_PARAM); the guest
  keeps its list place until the script has taken it, and the ride's head step forces such a guest onto the ride (`0x6182A0`).
- **Under the object's control (state 20)**: the entry `0x61A77E` puts a balloon and an umbrella away, plays a teleporting ride's beam and posts the admission's challenge events.
- **The exit (`0x619980`) and the use routine (`0x6118D0`)**: the used history, the visit counts, the fee recorded at the call (`+0x1B4`) and paid at the exit (`0x6126D0`), the
  customer, the fatigue, a scripted result, and the outcome `0x612740`: the excitement match, the type's effects, a shop's own terms, the small portions, the special ingredient, a balloon,
  a costume or an umbrella from a shop, a relief object's visit, a won play's gain, the mood with its thought and animation, and the perception records.
- **Being unloaded (state 19, `0x618380`)**: the walk off, the old major destination (and the photo booth's, unreachable without add-ons), and the mood or shrug animation. With Theme Park Inc's navigator (`--tpi-native-walk`) the sprite
  placement that the entry calls for a teleporting ride (`0x61A70B`) raises the stamp counter and clears a stranded mark below it, before the beam, as the guest constructor's does (NV3).
- **The removal message** now gives a rider its model back (`0x60D120`, state 20), and **the balloon prank** (state 6's case 5) bursts the balloon of a guest on the prankster's cell.
- **A lost balloon's litter**: the umbrella's opening in the rain (`0x61413A`), state 20's entry at an umbrella shop and the balloon prank (`0x613D00(1)`) drop Theme Park Inc's burst
  balloon (the litter types 9 to 12, by the balloon's colour through `0x9D0614`) on the guest's cell through the common drop `0x5D8C80`, which reseeds the world generator with the cell and draws
  twice; Theme Park Inc's litter sprites draw them from its own `SPR_LI` slots 8 to 11.
- **A balloon's colour** is drawn modulo the loaded balloon bank's slot count (`0x690394`, the ESP's slots with frames), and a won play's gain takes its logarithms at full precision, as the
  x87's `fyl2x` does, rounding only the quotient, the ratio and the product.

Labels:
- `[EXT:tpi-capability-gates]` now covers the toilet prank's priming and its victim (phase P4), the attraction history's rings the visit raises besides the declined ring, the analyser's
  balloon, costume and umbrella counters; it no longer covers state 6's balloon prank, OnAdmittance's fee word or the removal message's rider.
- `[EXT:tpi-no-maintenance-wear]` covers a relief visit's toilet wear (`0x5E78E0`).
- `[EXT:tpi-twp-guest-rules]` no longer covers the boarding.

Open: a guest let off while still in state 17 (a script that takes it and lets it off before its own update) is not yet handled. The original keeps a ghost of it in the ride's queue list;
OpenTPW ignores the let-off for now, so the guest stays stuck and a later save of the park is refused. It is under research before a decision.

No new approximation or label is added, and no new CRT `rand()` site was found.

Saves: the native block gains five optional members (the fee, the use flag, the balloon colour and a costume's sprite words), without a new schema. A native guest may now hold a balloon. A
guest saved in states 16, 17, 19 or 20 restores. A phase 3a save's bridged guest restores while it was still walking to the stand point; one waiting, riding or leaving is refused, because the
bridged boarding never saved the chance roll, fee and mode its native state needs. A Theme Park Inc park's litter may hold the burst balloons 9 to 12.

Review follow-ups: `ThemeParkIncX87Single` is folded into `ThemeParkIncX87`; the deferred items (a native oracle round for case 3, the guest's sex from its own sprite words, the cell's
thing chain order of the minor decision, `WorldRng.Next(0)`) are recorded in TPI-GUESTS.md.

Verified with these checks:

- The new native oracle `tpi_guest_visit_oracle.py` found no mismatch at control word `0x7F` over 1000 cases each of states 16, 17, 19, state 20's entry and the use routine with its
  outcome (with sideshow prizes and prices up to 100,000); the phase 1 to 3a oracle still agrees.
- New tests (`ThemeParkIncNativeVisitTests`) for the fee and its payment, the chance roll, the use routine of a balloon shop against the oracle's reading, a lost use, a won play's gain, the
  head step's force, the removal of a ridden ride and the balloon prank; the oracle's reading of the use routine for a sideshow won and lost, a costume put on and taken off, an umbrella shop
  and a relief visit; the balloon litter's reseed after the prank and in the rain; the stamp counter at a teleporting ride's unloading with the navigator; an exhaustive check that the won
  play's ratio rounds as the x87's does for every quotient; saves of a guest mid-ride and the refusal of a phase 3a bridged rider; a Science play test where native guests board a ride, ride it
  and are let off. The native rigs also pass with Theme Park Inc's navigator (`OPENTPW_TEST_TPI_NATIVE_WALK=1`).
- The Theme Park Inc and Theme Park World suites pass with their data, and the determinism pins do not move.
- The silent, offscreen Science smoke passes with the switch off and with it on.

The original game was not launched, and no binary or data is included.
