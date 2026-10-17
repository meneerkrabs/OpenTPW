## The advisor's park callbacks: rides, breakdowns, cleanliness and guest mood (October 17, 2026)

- **31 more scan callbacks are ported from `tp.exe`, line by line.** `AdvisorParkCallbacks` (`World/AdvisorScanPark.cs`) scores the worn, broken and too-few-mechanics lines (5-7), the
  object-selection lines (60, 62, 71, 73-75, 77-79, 81), the closed-share lines (259-262), the litter and toilet lines (8-10, 86-89), the no-toilets line (82) and the guest mood lines (3, 4,
  145, 158, 159); 85 is ported as what it is, a callback that computes a statistic and returns 0. Every callback reads the park only through `IAdvisorParkState`, whose
  `EconomyParkState` computes the helpers' statistics (`0x4C9960`, `0x4CA450`-`0x4CA9B0`, `0x4CA160`, `0x4CCDF0`, `0x4C81B0` and the guest counts `0x4C9290`-`0x4C95D0`, `0x4C7BB0`) from the
  economy, the litter map and the guest census without writing to any of them. The advisor stays outside the world hash, so no determinism pin moved.
- **Reading the bytes corrected the first mapping.** The "first object" and "first hire" stamps are rewritten by every construction and hire (they are the newest, and park init stamps them; the advisor keeps its own record, fed by the build and hire events, saved in its snapshot and outside the economy's hashes); 73, 77 and 81 ask for the
  previous kind's built types; 74 counts the shop types twice, so its ratio test passes only for a ratio above 1 (never with the shipped 0.15); 82 has no ride test; 145 lets equality pass; 259 tests the park open. The inventory and
  `docs/reverse/ADVISOR-SCAN.md` carry the corrected formulas.
- **Ride events.** The wear step raises `RideBreakdownImminent` when a ride's condition byte falls below 10 (advice 66, `0x4DF670`); as the ride enters its broken state (the new `RideEnteredBrokenState` event, not the earlier breakdown request: `0x4DADC0` runs on the type 7 message of `0x4E0E60`) it posts 68 without a mechanic, 69 when
  every mechanic is busy, resting or on strike and 258 for a condemned ride (`RideCondemned.Score`, now in `EventScoreKeys`).
- **Statistics the guest census gained.** `IParkGuestStatistics` counts the guests whose thirst, hunger or toilet byte is above a threshold, those holding a balloon or in a costume, and the
  mean happiness the advisor reads (`0x4C7BB0`).
- **Tests.** `AdvisorScanParkTests`: each callback at its thresholds over hand-built park states, a whole scan pass through the real queue (heavy litter posts 9, broken rides without a
  mechanic post 5 and 6, closed rides and thirsty guests post 259 and 3), the statistics over a real `ParkEconomy`, the guest bridge and the ride events. The producer inventory marks the 35 rows
  wired (133 in all). ADVISOR-009 narrowed: 117 scanned callbacks remain (21 constant-zero stubs, 96 computed).
