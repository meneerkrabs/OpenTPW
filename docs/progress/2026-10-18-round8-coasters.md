## Coasters and track building, fidelity round 8: winch marks, self-intersection marks, build-time crossings, PC table rounding and the area cell test (October 18, 2026)

Round 8 package 3 read the open COAST and TRACK items in tp.exe via REA; COAST-029 is retired and nine more are narrowed, five of them with behaviour changes:

- **Winch marks** (COAST-029, retired): the rebuild `0x43eb70` runs the knot pass `0x430ed0`, then `0x432b50` (clears node flag `0x800` on every node without
  `0x1000`), the sampler `0x440c10` and the rating pass `0x43c450` (sets it again on a floor speed). The creation parameter `0x20` of S0 and S1 gives
  them `0x1800`, so the pair keeps its mark: `CoasterNodeFlags.WinchKept`, saved and restored (a save from before it gets the pair marked on load).
- **Self-intersection marks and build-time crossings** (COAST-004, COAST-035): the pass `0x439ab0` re-tests only nodes marked `0x200000`; a node that drops out of an
  overflowed holder's list re-marks the holder (`0x43ad50`), so the pass now repeats until no mark is left, and the cut-end skip `0x10000` is built. The rebuild
  samples and tests every track it draws, so the build cursor's link and the links of an unfinished chain, and a pylon being dragged, show the crossing marker live
  (`CoasterRide.ChainCrossings`). `0x200000` is the pass's mark and the save writer's bit (`0x439b6b`, `0x4376bb`): COAST-035's untraced reader is found.
- **PC table rounding** (COAST-014): the sampler stores the sample angles through `0x43e590` and the loop knot factor `0x432430` looks the sine table up with
  the index rounded to the nearest (`fistp`), as the knot pass already did; the Mac truncation is gone from both. COAST-014 keeps the x87 arithmetic only.
- **The placement area's cell test** (COAST-028): `0x5243a6` with the predicates `0x535db0`, `0x536310`..`0x536450` is `Level.IsCoasterAreaCell` (cell flag `0x40`, track
  tiles and fence posts, cell types 3, 4, 7, 9, 10 and `0x1e`, a path with flag `0x20`); the tilt drag turns by the park camera's original yaw (`0x443371`).
- **Read, not built**: COAST-011 (the options table accessor `0x402bf0` is a virtual method of the class with vtable `0x6fd630`, so no static reader exists), COAST-028
  (the pick `0x443ee0` maps a model handle to the node; the panel's builder `0x498790` and the face texture slot `0x763b5c` orange.tga), COAST-032 (the control word is
  still unknown), COAST-026 (the run table builder `0x4402d0`), COAST-034 (`0x43be10` starts clip 5), TRACK-004 (the clear form `0x53B280`), TRACK-010 and
  TRACK-032 (the loader `0x543560` reads the section records raw).
