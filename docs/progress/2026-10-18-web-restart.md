## A stopped browser game gives its kept game files back (October 18, 2026)

A player whose browser game stopped on an error (the postcard crash, PR #407) found the restart very slow. The page that shows "The game stopped" stays open, and it held the Web Lock
that lets one tab at a time use the game files kept in the browser (OPFS) until it was closed. A new tab then played without kept files: no Play button, the whole folder had to be
chosen again and copied (about 600 MB for a full install), and nothing was kept, so every further restart was as slow. Measured on the interpreter build with the harness's Chrome
(`tools/web-perf` pieces, one run each, game folder served locally): a cold start reaches the lobby in 2.8 s and Jungle in 13.6 s, a warm start from the kept files shows Play after
0.3-0.4 s, reaches the lobby after 2.0-3.0 s and Jungle after 12.0 s, and a start after killing Chrome mid-session behaves like the warm start (same kept files, no scratch files left
in OPFS, manifest consistent); killing it in the first seconds of the lobby leaves the start files partly kept, so the next visit asks for the folder once. A renderer crash
(`Page.crash`) frees the lock by itself; a stopped game on a live page did not.

A page whose game has stopped now drops what is still queued to keep, writes the list of kept files, and releases the lock (`claimKeeping` in `opentpw-originals.js`), so a restart in a
new tab starts from the kept files. Checked in Chrome: with the game stopped by a thrown frame, a second tab showed "Another OpenTPW tab is using the game files" before and Play after.
Node tests cover the lock hand-over and the release when a frame throws. Not changed: a tab that is merely open next to a running game still plays without kept files.
