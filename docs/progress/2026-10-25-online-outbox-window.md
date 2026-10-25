## The outbox takes tp.exe's window, and the online menu gives way to its windows (October 25, 2026)

A player's screenshot of Unsent Postcards, opened in a Dutch park through Go Online, showed four problems:
- the online menu's buttons around the outbox window;
- "Geen zoekresultaten" (No search results), left over from an earlier park list;
- the weather banner "Het weer: sneeuw." over the online screens;
- an outbox built from OpenTPW's own pills, captions and Send button.

**The outbox window.** tp.exe builds Unsent Postcards from the layout table at 0x758878 (built by 0x4bc710, handled by 0x4bbc20). `tools/ppc-analysis/lanes/ui/tpw_exe_layouts.py` now decodes that table into `OriginalLayoutTables.OutboxWindow`, and the screen takes every control from it:
- the frame `window4` and the title (0xee9);
- the selected card's picture frame `!frame` (0xee4) and its addresses (0xee2, `list_emailaddr`);
- the bulk buttons: mark all for sending `b_okall` (0xee5), mark all for deletion `b_delall` (0xee6) and unmark all `b_nuall` (0xee7), with their help texts UIHELPTEXT 397-399;
- the list frame `list_outbox` (0xee8), its header buttons 16-19 with UITEXT 309-312 (Title, Date, Send, Delete), its content rectangle, and its slider with `b_up`, `b_down` and `b_scroller`, which show only while the rows do not fit (0x664495; the load list's slider, now the shared `UiListScrollBar`);
- the tick `b_okay` (help 400).

The Mac table `data:0x508bc`, which the earlier screen cited, has the same 19 controls and rectangles. The earlier screen showed the recipients in the picture frame's place and the message in the address list's place, listed each card's recipients instead of its title, drew its own header row inside the list frame and started every card unmarked.

The rows follow the row factory 0x4bcbc0 and its cell setup 0x4bcca0:
- A row is as high as the buy list's rows (font slot 7's line height × 1536 / screen height + 6, 0x4ad240), so it takes their fixed pitch (`[EXT:list-row-pitch]`): 14 rows fit the content rectangle.
- The title is the row's own text, from the row's left, fitted into its column as the buy list fits its names, so a long title does not run over the date and the marks. The date is centred in the Date column and is UITEXT 447 filled with the card's day, month and year, the save list's format (0x4bca6a). Before, it was .NET's short date.
- The Send and Delete cells hold the model `i_boxtick`. The list sets a cell's value from the row's mark (0x6634b2, 0x4bbfbc), and the value is the model's frame (0x66da4d): the empty box for an unset mark, the tick for a set one. Each box keeps the model's 51-unit size, centred in its column, as in the buy list. Before, a set mark showed as an X and an unset one as nothing.

The marks follow the handler:
- The builder adds every row with the send mark 0x1000000 (0x4bcaec), so every card starts marked for sending.
- A click on a row's Send cell (column 2) or Delete cell (column 3) toggles that mark and clears the other (cell message 0x404, 0x4bbf2c). `UiScrollList` gains `CellClicked`, `ColumnAt`, `HeaderRow` and `DrawCell` for this. A click right of the list (its slider strip) is no cell.
- The bulk buttons set or clear the marks of every row (0x4bbd2d, 0x4bbe7a, 0x4bbddf).
- The tick applies the marks first, deleting the cards marked for deletion (0x4bbcbd, 0x4bc430), then closes the window and sends the cards marked for sending (0x4bc500). Escape closes the window without either.

Where tp.exe refused the send because the account's settings forbid the service (0x5b6460, message 0x4e, ERRORMSG 78), OpenTPW opens its login dialog over the window, because its server needs a login (`[EXT:ONLINE-UI]`). A card that cannot be deleted stays in the outbox, and the menu's status line gives the reason.

**The online menu.** OpenTPW's online menu has no original counterpart. It stayed drawn under every window opened from it, so its buttons showed around the outbox, the news, Find Parks and the other windows. A new `UiScreen.HiddenWhenCovered` lets a screen give way to the one above it. The menu is not drawn while another screen lies on it, and the dimming backdrop is drawn once, for the window (`[EXT:ONLINE-UI]`).

**Status text left behind.** `OnlineSession.Run` keeps the previous status when an action returns null. The park-list fetches ended that way, or with "No search results" (UITEXT 306), so the status line kept "Getting … Park List" or the empty result while other windows were open. That was the "Geen zoekresultaten" in the screenshot. Changes:
- The park-list and news fetches now end the progress text.
- Find Parks and Other Parks still show an empty result as their own row.
- The city lobby has no list, so its status label now says "No search results" itself, while the lobby is on top. The desktop online panel lists parks without an empty row, so it says so itself too, and it shows how many cards wait in the outbox, which confirms a send.
- The outbox send sets "Sending mails from outbox, please wait ..." (UITEXT 313) while it runs, as tp.exe's 0x4bc500 did. tp.exe's progress closes with no count (0x4bc660), so the status clears once the cards are sent. Before, it ended with "Sending mails from outbox, please wait ... 2".
- OpenTPW's own windows (the menu, the login dialog, the publish form and the windows with a status line) showed "..." while a request ran, so the original's progress messages, the outbox send's among them, did not show while one of those windows was on top: the globe's status line, which shows them, is covered then (and since #837 a status line shows only on the screen on top). They now show the progress text the running request set (`OnlineSession.Progress`), and "..." only while it has set none. Another message meanwhile, such as a chat connection's error, is no progress of the request.
- The outbox's tick clears a message an earlier window left on the status lines, so that message does not hide the send's progress.

**The weather banner.** The forecast banner draws in the overlay pass above every screen. Go Online in a park replaces the park's view with the online globe, whose camera replaces the park's, and pauses the park, so the banner held its line over the online windows. It now waits while the globe camera is active (`WeatherForecastPresenter.ShowsOnlineGlobe`, `[EXT:forecast-banner]`, [AUDIO.md](../AUDIO.md)).

**Lists with column headers.** `UiScrollList` draws its column headers in the first row's place, but it hit-tested clicks as if the rows started at the top. A click on a drawn row selected the row below it, and a full list overran its frame by one row. Find Parks and Other Parks had this. Clicks now find the row drawn under the pointer, and `VisibleRows` leaves the header row out. The row helper of `ListOwnerAudioTests` had followed the old mapping and now clicks the drawn rows.

**Checks.**
- The front-end smoke:
  - It opens the outbox from the online menu with two cards and captures `online-outbox-cards.png`. The window follows the table, each row shows the card's title, date and tick box, and none of the menu's buttons are drawn (checked against the frame's text draws).
  - It posts a forecast of other weather in the park: the banner draws over the park, waits while the globe shows, and is back with the park's view.
  - The smoke passes.
- `OutboxWindowTests` (13 tests): the table's controls, the headers, columns, row pitch and date, the starting marks and cell toggles (by call and by a click on the drawn cell, but not on the slider strip), the tick boxes' frames and places, a long title inside its column, the slider's visibility as a card arrives while the window is open, the bulk buttons, the tick with and without a card to send, the menu giving way and its status line during and after a request, and Escape.
- `NativeWidgetTests`:
  - rows drawn below the headers are the rows a click finds, and a list without a header row starts its rows at the top and ends its columns at its edge;
  - a cell that acts keeps the row default out;
  - a covered menu draws neither itself nor a second backdrop.
- `WeatherForecastTests.TheBannerWaitsWhileTheOnlineGlobeTakesTheParksPlace`, `OnlineIntegrationTests.EmptyListsAndASentOutboxLeaveNoMessageOnTheStatusLine` and `OnlineIntegrationTests.ProgressIsTheTextTheRunningRequestSetAndNothingElse` and `OnlineIntegrationTests.ACityWithoutParksSaysSoInItsLobbyWhileTheLobbyIsOnTop`.
- Nineteen mutations were caught: hit-testing without the header row, `VisibleRows` counting it, the covered-screen skip removed, the menu's flag removed, the tick keeping an earlier message, the banner's globe check, the tick box's frame, its centring, the column bound at the list's edge, the slider's visibility, the `HeaderRow` flag, the date's format, a stale progress text, a progress text kept from the previous request, the title drawn unfitted, the slider updated before the re-read, the status line's dots, every status message taken as progress, and the city's empty text shown under another screen.

Still open:
- The card's picture is not drawn in its frame (`!frame`, 0xee4): the postcard photo is not ported (the postcard window leaves it out too).
- The header buttons sort the list in tp.exe (the list's attribute 0x10, 0x665a44; UIHELPTEXT 391-394 say so), but that sort (0x6628f4) is not decoded, so the headers do not sort yet, and they leave out those help texts.
- The addresses: tp.exe joins the recipients with newlines into one text in font slot 6, wrapped to the frame's inner width, with a slider (0xee3) when it overflows (0x4bc100). OpenTPW lists them a line each in the small font, without that slider.
- A title too long for its column even when fitted (one word wider than the column at the smallest size) still runs over it; how tp.exe's row text treats a title too long for the row (its text property 0x100, 0x65f5b7) is not traced.
- Find Parks and News still place OpenTPW's status line inside their original windows, where a message can overlap the search field and the news text.
