## Theme Park Inc readings (October 30, 2026)

- **Umbrellas, from the no-CD Theme Park Inc `Game.exe`.** UMB-002 is ported and retired:
  the scheduling and the carrying out of an umbrella change stand down for a guest on a kart
  or water track cell (`0x67F890`, `0x685680`), and a shown umbrella there is closed
  (`0x60AF32`); `UmbrellaRules.OnTrackCell` is tested over every record type. UMB-003: the
  inside words are read (`0x61A540` state `0x14` sets `+0x3C` for every attraction and `+0x38`
  for a type with a nonzero `+0x1C4`, state `0x13` clears both), so "inside" is the state
  Using, plus the pools extension's own swim states; the clock scale is read (15000 from
  `0x607EB9`); only the seconds-based age remains. UMB-004: `YAdjustCamera` is 0.1
  (`levels/Standard.sam`). UMB-001: the C runtime's `rand()` is seeded once by
  `srand(GetTickCount())` (`0x6A151B`) and shared with 96 call sites, so the original's
  delay is not reproducible even by the original; the hash stays, for the user to decide.
- **Theme Park Inc scrap value (ECON-061 retired).** The thing flag `+0x43` bit `0x10`
  comes from the type's `UsageInfo.RequiresWatering` (`0x5DB654`: the bushes, trees and
  flower beds), and the count behind the 100/67/34/0 percent is variable 0 of the thing's
  script, `VAR_CONDITION` of `vegetation.RSE` (`0x6B0740`, `0x6B0C90`).
- **Theme Park Inc money (ECON-060, ECON-062 narrowed).** A sale or a won play books its
  cost of goods to the object's month accumulator (`+0x938`, `0x5E60E0`) and debits nothing;
  the month message debits the running cost and that accumulator as one debit per object
  (`0x5DEFFF`). A shop's cost of goods is the executable's own routine (`0x5E6260`, checked
  in Unicorn on random inputs). Rides charge `InitPricePerUse` at each use and refuse a guest
  without the cash (`0x6125E7`, `0x61243A`); the willingness test of `0x6120C0` is not ported.
- **Arrival weather, footprints, fixed models (ARRIVAL-008, RIDES-031, RIDES-040
  narrowed).** The weather state is the running spell's kind (`0x642A80`) and the spells use
  the time-seeded `rand()`; the footprint commit is the twin of tp.exe's (`0x668552`); a
  thing's position bytes are decoded only by the camera-target setters.
