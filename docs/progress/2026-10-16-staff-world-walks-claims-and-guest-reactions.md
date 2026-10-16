## Staff walks, claims, prank advice and price statistics from tp.exe (October 16, 2026)

Four staff and guest items were built from tp.exe, read via REA; each is narrowed, none could be fully retired:

- **Staff walk the guests' original walk** (ECON-022 narrowed, ECON-054 narrowed to the headless economy): staff use the original SetDest
  and the shared walk step (`0x4FA530`/`0x4FA5F0` → `0x510100`, then `0x4FA2A0` → `0x50F3B0`) with separation over nearby guests and
  staff; the walk mask is 1 on the strike walk (`0x506A40`, cleared at `0x5056E0`) and on the guard's second route to the booth
  (`0x4D6977`). A claim records its turn (`0x4E01F0`) and lapses only after 100 turns once its claimant no longer targets the object
  (`0x4E0220`); the mechanics' ride search uses one cursor on the mechanic HQ, walking the object list newest first (`0x4DAA90`); selling
  the rest area a member rests in sends them to the nearest other one (`0x504C70`, unlinked by `0x519DC0` first). The toilet and rest
  searches now meet objects newest first, as the original's object list holds them. Staff imported from an original save use the same
  walk: the saved destination is routed again through it (`0x4FA5F0`), the saved base, purpose and previous speeds are kept, and the claim
  of an imported job is taken again with the turn the save recorded (`+0x60`) when the member is placed, so it lapses on the original's
  100-turn rule.
- **Price test statistics and walk-aways** (GUEST-003 narrowed): the price test records the guest's view of the price in the park's
  perception statistics, paid or refused (`0x4FDE50` → `0x4C74B0`; 20 rings of 50 values from `0x4CE9E0`), as
  min(100, (W+5)·10 − price·10). A refusal raises the ride's lifetime and daily walk-away counts (`0x4E1670`), and day message `0xB`
  rolls the daily count into a 30-day ring (`0x4DD320`, sent by the calendar `0x4F8260`). `mNumWalkAways` is imported from original saves
  (the loader reads it back at `0x4DC12B`; the earlier note that it is only written means nothing else reads the field; SAVE-006 narrowed).
  The ring is the one daily ring OpenTPW keeps (ECON-057 narrowed to the five rings it does not keep, and nothing reads the ring yet).
- **Prank advice and the guard's set-down** (GUEST-007 narrowed): the prank message is delivered inside the poster's call (`0x40FB10`);
  the advisor queues advice `0x7D`–`0x7F` chosen by the prankster's thing id modulo 3 (`0x4FF2EA`), scored by `0x59F360`/70/80 from the
  optional PrankSbomb, PrankLitter and PrankBalloon keys of Advisor.sam. The guard sets a caught guest at the cell centre and uses bit 0
  of its world draw to pick the way out (`0x4FEBC1` → `0x4D86D0`).
- **The hire window's walking figure** (UI-082 narrowed): the portrait box shows the role's walk slot, facing 5, frames 0–7
  (`0x49BA00` → `0x475A10`, script `0x74F758`); register 8 set to 8.0 by `0x4755D0` is the draw scale, not an animation speed.

The determinism pin is unchanged; a set-down guest's drawn side joins the hash only while it is set. New optional save fields (walk-away
counts, claim turns, the mechanic HQ cursor, price statistics, a staff walk's original-walk state, a guest's drawn exit side) are
validated; older saves load, and their staff walks restore as route walks. Verified with 4467 tests passing without game data, 4909 with
it, and all five native smokes.

Still open: the dismissal sound for thing ids with low bits 0 (guest ids are not thing ids, WALK-010); victim order on a cell (WALK-003);
the walk to the drawn side's gate cell (GUEST-010, WALK-004); the screen-sprite pixel scale of the hire figure; entertainer animation, guard list order, access cells, striker lanes, the mechanics' advice `0x46` (`0x4DA672`), separation cell order and
the stranded stamp `+0x198` (`0x4FA770`).
