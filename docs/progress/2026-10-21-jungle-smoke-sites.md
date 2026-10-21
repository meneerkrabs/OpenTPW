## The Jungle smoke finds its ride sites again after the coaster cell markers (October 21, 2026)

The native Jungle smoke failed since #736 with "find an on-screen site for a water ride and its circuit". #736 itself is faithful: tp.exe's
station placement validates the cell beyond each track end in object mode 4 (`0x5295C4..0x5295DE` calls the cell op `0x104`, whose
preview path runs the validator `0x535670` with its sticky failure globals; an off-map beyond cell sets `0x816D50` at `0x5295E8`), and the
coaster's pylon and beyond-station cells now hold the type 21 marker. That refused some coaster sites the smoke tried before, and its site
searches depended on where earlier steps had left the camera: the coaster site test scrolled the camera cell by cell while judging a site, and
the water, kart and bumper searches only looked at the cells already on screen. The coaster therefore landed at (64, 18) instead of
(62, 19), which explains the lower excitement (6.9 instead of 8.4: at the old site #736 rates the same circuit 8.4 again), and no free 9 x 8
area was left in view for the water ride.

The coaster site test now only reads the view it judges, and the track and bumper phases take a free site the view shows or else scroll to
the free site nearest the view's centre. The water and kart phases also expect the exit and entry tiles that the station placement lays free
of charge since #739 (`0x5253E9`): before any line those are the only track tiles, and the three lines lay the other six of the ten-tile loop.
(The matching with-data `TrackEditRecoveryDataTests` expectation was fixed separately in #744.) All five native
smokes pass; no simulation code changed.
