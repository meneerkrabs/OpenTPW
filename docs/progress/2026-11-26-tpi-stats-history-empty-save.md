## Saves keep an imported park's untouched statistics history (November 26, 2026)

A fix for the Theme Park Inc Science smoke, which failed about two runs in three on main with "Science economy continues as in the uninterrupted park".

- **The cause.** The Science park imports its prebuilt analyser's monthly rings. No month has run, so every ring is empty, but their unread slots keep the prebuilt's `0xCDCDCDCD` fill as raw words (the ring loader reads every slot raw). A save left the history out whenever no ring had an entry, so the continued park restored zeros in those slots. Once a guest arrived within the smoke's continuation window, the uninterrupted park's history was no longer empty and its save carried the raw words, while the continued park's carried zeros. Whether an arrival landed in that window decided the outcome.
- **The fix.** A save now leaves the statistics history out only when it equals a fresh park's: no entry, every slot word zero and no arrivals this month (`ParkStatisticsHistory.IsFresh`). A Theme Park World park that never had history still writes nothing, so its save bytes and run hash are unchanged. An imported park whose rings are untouched but hold saved words now keeps them.
- **Saves.** [PARK-SAVES.md](../PARK-SAVES.md) notes the rule. An older Science save without the block still loads, with a fresh history as before.

No new approximation or extension label is added.

Verified with new tests that save and restore an entry-less imported history with filled slot words (a synthetic Theme Park World analyser, and the retail Science park with OPENTPW_TPI_PATH), both failing before the fix, and that a fresh park still saves no history; the full test project with Theme Park Inc and with Theme Park World data; and the silent, offscreen Science smoke five times in a row.

The original game was not launched, and no binary or data is included.
