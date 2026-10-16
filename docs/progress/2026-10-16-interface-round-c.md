## Interface round C: loans, Load Park, pause lines, camera panel and postcard from tp.exe (October 16, 2026)

Six interface surfaces rebuilt from tp.exe's generated layout tables and handlers (UI-063 retired; UI-013, UI-042, UI-002 and UI-026 narrowed):

- **Loans window** from table `0x7525a0` (window4, f_loans, b_sloan/b_spayup, help 235-237). The list follows tp.exe's list class: the row factory
  `0x4a02b0` uses the buy list's slot-7 rule, so seven whole rows fit (`0x663718`); a new list sorts by lender name ascending with wcscmp and keeps
  slot order for equal keys (`0x662679`, `0x66403b`, `0x663edc`); the top row is chosen when the list fills; the scroller and arrows hide while the
  rows fit (`0x664495`). The tab last chosen stays for the session (`[0x7cb260]`, `0x4a06b0`). The summary rows were in the wrong order: Loan
  Payments (UITEXT 360) is the second line and Other Costs the third (`0x49fc90`). Together with the stored credit test (`0x4D06B0`) that runs when
  the dialog opens and on each tab select (`0x4a06b0`, `0x49fa00`).
- **Load Park** from the load dialog `0x7523f0`: window2 root, title box, f_load frame, the rows in the buy list's pitch (twelve show), slider with
  b_up/b_down/b_scroller, b_exit; no header sort (attribute 0x81). The host's Load Park text button stays (`[EXT:load-chooser]`).
- **Pause menu lines** as `0x492f60` lays them out (via `0x48c150`): each line is the measured text width + 1 by height + 5 on the 2048x1536 canvas,
  centred, first line at top 10 and 5 units between lines, in the line colour cyan (0, 175, 190) of `0x48b2a0`.
- **Camera panel** slides out of the HUD arm as the drawer `0x4a14f0` does: ten steps of a 20 ms timer, cat_ui `0x1C` when a slide starts and `0xC2`
  when it ends, panel buttons shown once their right edge passes the arm's left edge (`0x4a1440`). The camera button's advisor help `0x131` is posted
  on the press and withdrawn on the release through the new `HelpPosted` / `HelpWithdrawn` host hooks (`0x486b00`, `0x486b40`).
- **Buy window preview** turns by 0.0004 rad/ms across selections, tilts by pi/4 and is fitted as `0x4689f0` / `0x468e50` do (UI-026 narrowed).
- **Send Postcard** from table `0x7540d8` (built by `0x4a8c00`, handler `0x4a7d70`): window4, title, photo frame, message/title/recipients fields with
  prompts, preview/clear/address/tick/X buttons. Recipients stay player names (`[EXT:postcard-player-names]`). The window is reached only through the
  HUD host's `OpenPostcard`, which is null while the online extension is off, so the camera panel's button and the shortcut show "not available" then.

UI-002 font-slot table (callers of the slot getter `0x485a70`): slot 0 pause lines (`0x48c150`), 1 HUD cash (`0x4a21e4`), 2 meter labels (`0x4a1e7b`),
3 date (`0x4a2519`), 6 window labels and values, 7 buy/loans/load list rows and the help box (`0x492180`), 11 postcard fields and the locator name
(`0x4a8df3`, `0x4b3e80`).

Verification: build clean; the suite without game data passes 4420 (544 skipped), with the game files 4855 (109 skipped), the fixed-run hash pin
(`0xA2FAA54FEB56DA05`) is unchanged; the five native smokes (front end, sandbox, plain smoke, park-resume capture and restore) pass. A review pass
fixed the postcard prompts (shown while a field is empty), the camera panel's depth under the HUD's main panel and the load list's slider drag.

Still open: the roles of font slots 4, 5, 8 and 9 and OpenTPW's Label/Small roles that do not follow slot 6 (UI-002); the postcard's photo, preview and
address book (UI-042); the preview's placement terms, animation 5 and footprint grid (UI-026); the load list's date column and the pause lines' online
top and focus ramp (UI-013).
