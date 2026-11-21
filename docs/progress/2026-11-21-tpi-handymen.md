## Theme Park Inc: handymen sweep the park's litter (November 21, 2026)

A Theme Park Inc park now has its first working staff: the player hires a Cleaner (the handyman) from the Staff panel and drops him in the park, and he walks, idles and sweeps litter by Theme Park Inc's own state machine. The guests drop litter for him by Theme Park Inc's own litter step.

- **Hire and drop.** Activating a Cleaner's row in the Staff panel puts him in the hand; a click on the park drops him on a cell the executable's drop tests accept (`0x4D4EB0`), and right-click or Esc cancels. The other roles are refused with a note (`[EXT:tpi-capability-gates]`).
- **The handyman.** A separate Theme Park Inc staff world runs his pre-update (`0x5D5000`, with the day counters of `0x628050`) and his states 0, 1, 0x0F and 0x10 (idle, walk, to litter, sweep), with his search (`0x5D52A0`), the gate before it (`0x62BBA0`, read in full for a handyman), the litter finder and claim, the work duration and the meters. Members are updated in the thing sweep, newest thing first.
- **Economy.** The day message now runs the members' daily routine (experience and wage demands, shown as notes) and the month message pays their salaries and reviews their wages.
- **Guests' litter.** The litter step follows `0x614B71`: a one-in-four world draw, the bin search, the type drawn before the cell is tested, and an occupied cell keeps the urge. The litter's sprite lies 3 to 6 tenths into its cell.
- **Saves.** The Science continuation save keeps each placed member's thing and walk, and the members' cell-chain entries.

Labels: the handymen walk with Theme Park World's walk, wander and speed step (`[EXT:tpi-twp-guest-rules]`, now approved for the staff's walk); Theme Park Inc's toilet wear and bin fill stay unported, so the handymen sweep litter only (`[EXT:tpi-no-maintenance-wear]`). No new approximation is added. Rest areas, strikes in the park, the wage negotiation, the other roles and the staff sprites come in later packages; the handymen are not drawn yet (docs/reverse/TPI-STAFF.md §11).

Verified with these checks:

- New tests for the litter step, the litter offset, the handyman's drop, search, walk to litter, sweep, the claim taken away, the tired gate, the base-speed table, the search period, the display categories, the day and month messages and a save mid-walk; the Theme Park Inc, staff, litter, save and determinism filters pass with the retail data.
- The silent, offscreen Science smoke hires a Cleaner through the staff list, drops him on a linked path, sees the park's turns update him, and continues a saved park with him exactly.

The original game was not launched, and no binary or data is included.
