# Theme Park Inc advisor: weather forecasts (superseded)

This page is superseded by [TPI-ADVISOR.md](TPI-ADVISOR.md). The forecast evidence (the trigger, the four descriptor and response rows, the captions and the class table) is in §5 there, and the queue the forecasts go through is in §3 and §4.

Several claims this page made are wrong; TPI-ADVISOR.md §7 lists them:

- The condition table is referenced: the scan `0x71C0B0` evaluates one row every world tick.
- The descriptor table has 664 rows and 606 ids, and `0x58`–`0x5C` have rows.
- The variant rule is row `+0x2C`, not `+8`.
- The Park Awards dispatcher `0x606EC0` is reached for every award (see [TPI-AWARDS-SHARES.md](TPI-AWARDS-SHARES.md)).
- The playback window is the advisor's sequence plus the closing clip plus 2000 ms, not the voice length plus 1000 ms.
