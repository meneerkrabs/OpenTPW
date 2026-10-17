## Smarter guests, phase 1: the original Top 3 Thoughts and the guest-insights refusal counters (October 17, 2026)

- **Top 3 Thoughts, traced in tp.exe.** The park information window (`0x4A52A0`, table `0x7537D0`) shows three `ti_hungry` icons and no text. The ranking `0x4C8430` counts the thought id of every guest standing on a park cell
  (`0x4FA990`) into 23 counters, discards the count of id 0 (`0x4C84D7`), keeps three unsorted slots that start as the ids 0, 1 and 2, lets each id from 3 to 22 take the slot with the lowest count when its own count is strictly higher (the lower id keeps
  a tie), and writes 0x17 (nothing) for a zero count; slot 0 is the rightmost icon, the frame is the id less 1, a one-second timer (`0x80083`) refreshes it, and an icon hidden by an empty slot stays hidden while the window is open; the icons' hover text is the THOUGHTS.str entry of the frame. `TopThoughtRanking` and `GuestSimulation.GetTopThoughts()` implement it;
  `GuestStatistics.CommonThought`, a guess nothing read, is gone. The layout table is generated (`ParkInfoWindow`, `tpw_exe_layouts.py`), and the window now draws the original frame, title and icons; the line graph, counts frame,
  happiness levels and navigation buttons are `APPROX:UI-914`.
- **`opentpw.guest-insights` has its first feature and is selectable.** `GuestRefusalCounters`, a passive observer, counts why guests said no (too expensive, queue too long, too scary, too boring, ride broken or closed) and the park
  information window shows the totals when the extension is on. Never saved, never hashed, no draw: a run with it on has the same guest hash, random state and snapshot as one with it off. `PlayerSelectable` is true for this extension
  only; names, descriptions and the new labels exist in the nine interface languages. `opentpw.smarter-guests` stays hidden.
- **Tests.** The ranking and its ties on synthetic thoughts and guests, the window's three icons and one-second refresh, the counters, off by default, identical hashes and snapshots with the counters on, the mods screen listing.
