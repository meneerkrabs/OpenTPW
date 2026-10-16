## The jungle smoke passes again: station line, off-grid projection, Totem cycle, rebuilt ride (October 16, 2026)

The native jungle smoke (`--load-original-level jungle --smoke-test`) stopped at its water-track step after PR #265, and three later steps
failed once that was fixed. One fix is in game code; the others bring smoke expectations up to date with recent merges.

- **Water and kart track step (stale expectation).** The smoke still expected the station's first tile on the footprint anchor, the
  layout before PR #265 moved the station onto the line of tp.exe's `0x52A1A0`. Re-read via REA: at rotation 0 the line runs from
  (x, y+1) to (x+2, y+1) of the attraction cell, the walk starts at (x, y+1) and the exit is (x+4, y+1) (`0x52A1E1..0x52A204`), which is
  what `TrackStation.FromPlacement` builds. The code was right. The smoke now checks the layout against it and lays its circuit corners
  from the first station tile, as `Level.BuildDemoTrackRide` already did.
- **`Level.TryProjectCell` outside the object grid (code fix).** The staff step scans the whole guest grid, which can be larger than the
  level's heightfield, and the off-map cell threw in `ModelHeightfield.GetCornerHeight`. The method now returns false there. The HUD's
  track-ghost cost label projects a line's snapped end through the same method.
- **Totem checks on a fresh cycle.** When the guest phase finished mid-cycle, the ride checks re-armed on the cycle still playing and found
  its main clip over ten frames later. They now wait for that cycle to end.
- **The rebuilt economy object is opened.** Since RIDES-015 was retired, a built ride with a queue starts closed (`0x4DB712`). The smoke
  opened its first economy build but not the rebuild after the scrap check, so Crazy Ape, the jungle's only built ADDHEAD ride, never took
  riders and the isolated rider-head readback found no candidate.

The pinned run hash is unchanged. Verified with the full test run (5423 passed, 113 skipped) and the jungle native smoke (5075 frames, passed).
