## Ride window gauges, the variable box and the buy list from tp.exe (October 16, 2026)

The ride, shop and sideshow windows, read in tp.exe via REA:

- **Gauge values** (UI-028 retired): excitement (`0x4e0560`, with its sideshow, ride and coaster branches), reliability (`0x4df640`,
  `0x4df450`), local happiness (`0x4cd880`) and winners (`0x4e1f20`) are computed as tp.exe does, `(byte << 10) / 100` with x87 truncation
  and `& 0xFF` where the original masks.
- **The gauge bar** (UI-046 narrowed): values range 0..1024 (the setter's clamp `0x66d885`), the bar is clipped at u = value/1024 on
  `ridestatbar.wct` (`0x477ff0`, `0x478400`) and inset 10 units top and bottom (`0x485e30`).
- **The settings controls** (UI-024 narrowed): the buy window's capacity and wear fields; the variable box repeats while held (`0x668dd9`:
  200 ms delay, 50 ms interval); a slider's default track is its own rectangle (`0x66ac00`); loaded values always reach the window.
- **The buy list** lists only available (researched) types, as its filler `0x4aaf70` does, instead of listing locked ones and refusing
  them on click.
- **Leaving the ride view** (UI-095 narrowed): the exit test (`0x42a340`) and the yaw rule from the map cell's link byte (`0x42a1d0`).
- **The ride view camera** (UI-094 narrowed): the look rotation signs are tp.exe's (`0x46f350`, `0x46f650`), and the pointer reference is
  the full-screen park window (`0x489f50`).

The pinned run hash and saves are unchanged. Verified with 4833 tests passing without game data (569 skipped), 5292 with it (110 skipped), and all five native smokes.

New register IDs split out of the open parts: UI-104 (the Move tool, mode `0x3b`), UI-105 (the golden-ticket rows of the buy list) and UI-106
(the gauge art `ridestatbar.wct` decoding without the red end; the video-read colours stay UI-046). The customer and winner histories are covered by
the 30-day rings of ECON-057; the satisfaction history stays in UI-024 and UI-905. The ride view's field of view is UI-101's difference (UI-094
cross-references it).

Still open: the original park camera pose (`0x42b1c0`) and the projection scales (`0x5781d0`).
