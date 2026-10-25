## The outbox takes tp.exe's window, and the online menu gives way to its windows (October 25, 2026)

A player's screenshot of Unsent Postcards, opened in a Dutch park through Go Online, showed four problems:
- the online menu's buttons around the outbox window;
- "Geen zoekresultaten" (No search results), left over from an earlier park list;
- the weather banner "Het weer: sneeuw." over the online screens;
- an outbox built from OpenTPW's own pills, captions and Send button.

**The outbox window.** tp.exe builds Unsent Postcards from the layout table at 0x758878 (built by 0x4bc710, handled by 0x4bbc20). `tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py` now decodes that table into `OriginalLayoutTables.OutboxWindow`, and the screen takes every control from it:
- the frame `window4` and the title (0xee9);
- the selected card's picture frame `!frame` (0xee4) and its addresses list `list_emailaddr` (0xee2);
- the bulk buttons: mark all for sending `b_okall` (0xee5), mark all for deletion `b_delall` (0xee6) and unmark all `b_nuall` (0xee7), with their help texts UIHELPTEXT 397-399;
- the list `list_outbox` (0xee8) with the Title/Date/Send/Delete headers 16-19 (UITEXT 309-312);
- the tick `b_okay` (help 400).

The Mac table `data:0x508bc`, which the earlier screen cited, has the same 19 controls and rectangles. The earlier screen showed the recipients in the picture frame's place and the message in the address list's place, listed each card's recipients instead of its title, and started every card unmarked.

The marks follow the handler:
- The builder adds every row with the send mark 0x1000000 (0x4bcaec), so every card starts marked for sending.
- A click on a row's Send cell (column 2) or Delete cell (column 3) toggles that mark and clears the other (cell message 0x404, 0x4bbf2c). `UiScrollList` gains `CellClicked` and `ColumnAt` for this.
- The bulk buttons set or clear the marks of every row (0x4bbd2d, 0x4bbe7a, 0x4bbddf).
- The tick applies the marks first, deleting the cards marked for deletion (0x4bbcbd, 0x4bc430), then closes the window and sends the cards marked for sending (0x4bc500). Escape closes the window without either.

Where tp.exe refused the send because the account's settings forbid the service (0x5b6460, message 0x4e, ERRORMSG 78), OpenTPW opens its login dialog over the window, because its server needs a login (`[EXT:ONLINE-UI]`). A card that cannot be deleted stays in the outbox, and the menu's status line gives the reason.

**The online menu.** OpenTPW's online menu has no original counterpart. It stayed drawn under every window opened from it, so its buttons showed around the outbox, the news, Find Parks and the other windows. A new `UiScreen.HiddenWhenCovered` lets a screen give way to the one above it. The menu is not drawn while another screen lies on it, and the dimming backdrop is drawn once, for the window (`[EXT:ONLINE-UI]`).

**Status text left behind.** `OnlineSession.Run` keeps the previous status when an action returns null. The park-list fetches ended that way, or with "No search results" (UITEXT 306), so the status line kept "Getting … Park List" or the empty result while other windows were open. That was the "Geen zoekresultaten" in the screenshot. Changes:
- The park-list and news fetches now end the progress text.
- Find Parks and Other Parks still show an empty result as their own row.
- The city lobby has no list, so its status label now says "No search results" itself.
- The outbox send shows "Sending mails from outbox, please wait ..." (UITEXT 313) while it runs, as tp.exe's 0x4bc500 did. Its progress closes with no count (0x4bc660), so the line clears once the cards are sent. Before, it ended with "Sending mails from outbox, please wait ... 2".
- The outbox's tick clears a message an earlier window left on the menu's status line, so that message does not hide the send's progress.

**The weather banner.** The forecast banner draws in the overlay pass above every screen. Go Online in a park replaces the park's view with the online globe, whose camera replaces the park's, and pauses the park, so the banner held its line over the online windows. It now waits while the globe camera is active (`WeatherForecastPresenter.ShowsOnlineGlobe`, `[EXT:forecast-banner]`, [AUDIO.md](../AUDIO.md)).

**Lists with column headers.** `UiScrollList` draws its column headers in the first row's place, but it hit-tested clicks as if the rows started at the top. A click on a drawn row selected the row below it, and a full list overran its frame by one row. Find Parks, Other Parks and the outbox had this. Clicks now find the row drawn under the pointer, and `VisibleRows` leaves the header row out. The row helper of `ListOwnerAudioTests` had followed the old mapping and now clicks the drawn rows.

**Checks.**
- The front-end smoke opens the outbox from the online menu with two cards and captures `online-outbox-cards.png`. The window follows the table, each row shows the card's title, date and send mark, and none of the menu's buttons are drawn (checked against the frame's text draws). The smoke passes.
- `OutboxWindowTests` (10 tests): the table's controls, the columns and headers, the starting marks and cell toggles (by call and by a click on the drawn cell), the bulk buttons, the tick with and without a card to send, the menu giving way and its status line, and Escape.
- `NativeWidgetTests`: rows drawn below the headers are the rows a click finds, a cell that acts keeps the row default out, and a covered menu draws neither itself nor a second backdrop.
- `WeatherForecastTests.TheBannerWaitsWhileTheOnlineGlobeTakesTheParksPlace` and `OnlineIntegrationTests.EmptyListsAndASentOutboxLeaveNoMessageOnTheStatusLine`.
- Six mutations were caught: hit-testing without the header row, `VisibleRows` counting it, the covered-screen skip removed, the menu's flag removed, the tick keeping an earlier message, and the banner's globe check.

Still open:
- The card's picture is not drawn in its frame (`!frame`, 0xee4).
- The marks show as an X: the cells of the row factory 0x4bcbc0, which hold the original's mark art, are not decoded.
- The list's and the address list's original sliders are not drawn.
- What a click on the header buttons 16-19 does in tp.exe is not traced.
- Find Parks and News still place OpenTPW's status line inside their original windows, where a message can overlap the search field and the news text.
