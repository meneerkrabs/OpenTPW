## Original saves: staff, attraction states and guests imported from tp.exe (October 16, 2026)

What an original save restores now goes well beyond the placed objects (SAVE-003 retired, SAVE-006 and SAVE-002 narrowed):

- **Staff** (SAVE-003): the staff serializer `0x504DE0` and the person block `0x4F8B10` are decoded field by field, with the role serializers
  of the mechanic (`0x4DA110`), handyman (`0x4D6D60`), entertainer (`0x4D4460`), researcher (`0x502760`) and guard (`0x4D6000`). A member keeps
  its inline name, hire tick (the clock stamps whole turns, `0x4F8690`), idle stamp, patrol rectangle (corner cell ids y x 128 + x + 1,
  `0x506ED0`), job with its target (a mechanic's ride and repair timer, a handyman's toilet and cleaning start, a rest area while walking to or
  resting in it) and the walk on to `mAccurateDestX/Y`. A world takes back the ride or toilet claim when it places the member. OpenTPW's own
  save gains the optional staff `Name` and `ResumeDestination` fields so a saved park keeps them too.
- **Attraction states** (SAVE-006): `SetState` `0x4E0E60` is read for states 0 to 4 (1 and 4 break the object down, 2 waits for a mechanic to
  install the next level and debit its cost, 3 is a type that is not choosable), together with the total profit (takings minus costs,
  `0x4E1C60`) and the maintenance call. The serializer `0x4DB7D0` is now decoded completely, including flags, handles, claim, queue ends, the
  open word and the six daily rings. The chance of losing, quality and special ingredient amount from the ride settings work are decoded once,
  in the import loop, from the low bytes the loader reads (`0x4DCD29`..`0x4DCDC1`); a win byte above 100 imports as a chance of losing of 0.
- **Guests** (SAVE-002): the guest serializer `0x4FB530` after the person block is decoded (`SaveGuest`), and the level restores the shipped
  save's guests into the guest simulation (`GuestSimulation.ImportOriginal`) instead of starting empty: Easymode's 13 guests keep their thing
  id, type, cash, meters, exit counter, balloon, costume, walk speeds, histories and thought, and walk on to their saved destination.
  Arriving and departing guests walk on along OpenTPW's nearest arrival lane. The import draws nothing from the guest stream.

Evidence: every field cites its tp.exe address in the import code; `Evidence` lines of an import list what was restored and what was
adjusted. Verification: the full suite passes without game data (4357 passed, 547 skipped) and with it (4795 passed, 109 skipped), the five
native smokes pass, and `tools/fidelity_register.py --check` reports 241 unresolved unique APPROX IDs (the review registered ECON-056, the shop cost per sale of `0x4E1B40`, and ECON-057, the per-attraction 30-day rings). The determinism pin did not move.

Still open: SAVE-006 keeps the operating capacity, duration and speed (OpenTPW's rides keep their tier's Init values, as the shipped saves do),
a closed open word of a working attraction, the daily rings beyond this and last month, the sign lines, the third gauge `+0x4C` and a staff
claim no imported job holds. SAVE-002 keeps queued, boarding, riding and held guests, park guests off the paths, the booth's and gate's waits,
the stamps before the save's turn, and the counters, event ring and queue links; litter jobs (SAVE-010) are dropped.
