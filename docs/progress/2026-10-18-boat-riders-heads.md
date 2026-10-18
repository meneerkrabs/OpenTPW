## Riders' heads on the water rides' boats (October 18, 2026)

The water rides' boats now show their riders' heads, as the karts and bumper cars do. tp.exe creates every track vehicle in one routine (0x549DB0) and hangs rider k of its rider list on the vehicle mesh's head dummy k (0x549C60). The `wr_ring` boats of all four water rides carry five such dummies; OpenTPW read only dummies 1 and 2 and left the water family out of the head sources. `TrackRenderer.SeatHeads` now reads every seat, and `TrackAssetTests` checks the five boat seats and the karts' seats on the shipped models.

The riders' heads on coaster and tour cars were already drawn (`CoasterHeadSources`, `TourHeadSources`), so the front page no longer lists them as missing, and GUESTS.md no longer calls them "not done".
