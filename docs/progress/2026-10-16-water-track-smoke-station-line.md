## The water-track smoke follows the station line (October 16, 2026)

The native smoke's track step (`SandboxSmokeTest.StartTrackSmoke`, water and kart phases) still expected the station's first tile on the
footprint anchor, OpenTPW's layout before PR #265 moved the station onto the line of tp.exe's `0x52A1A0`. Re-read via REA: at rotation 0
the line runs from (x, y+1) to (x+2, y+1) of the attraction cell, the walk starts at (x, y+1) and the exit is (x+4, y+1)
(`0x52A1E1..0x52A204`), which is what `TrackStation.FromPlacement` builds. The code was right and the smoke's expectation was stale.

- The smoke now checks the layout against `TrackStation.FromPlacement` and the footprint's rows 1 and 2, and reports the observed first tile.
- Its circuit corners are measured from the first station tile instead of the anchor (as `Level.BuildDemoTrackRide` already did), so the
  three clicks still close the same 10-tile loop around the moved station; the site search already reserved the shifted rows.

No game code changed; the pinned run hash is unchanged. Verified with the full test run and the jungle native smoke (on top of PR #270's
queue-site fix, which the smoke needs to reach the track step).
