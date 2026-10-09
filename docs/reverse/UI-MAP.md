# Sim Theme Park (Mac) user interface map

Generated on 2026-10-09 from the Mac PowerPC executable (`SimThemePark.data`, SHA-256
`04809cd4…e295f5`, see FINDINGS.md). Sixteen Haiku subagents each read one address range
of the 242 functions that look up controls (`0x1017F770`) or UITEXT strings
(`0x10138504`) in local Ghidra output, and described screens, controls and rules in their
own words. No decompiled code is included; Ghidra local-variable names were replaced.

**Status: leads, not evidence.** Under the project's rule (COMPLETION-PLAN.md, "Herkomst
van spelregels") a rule counts as original behaviour only after it is traced and reviewed.
Every row below was checked mechanically against the decompiled functions it cites:

- `id ✓`/`id ✗`: the control id occurs in the cited functions;
- `text ✓`/`text ✗`: the cited functions fetch that UITEXT index (strings from the Mac
  `American` table; the Windows English table is one index lower);
- `game-type test ✓`/`✗`: a rule said to depend on the game type cites a function that
  actually tests the game type (`0` Full Simulation, `1` online, `2` Instant Action).
  `✗` means the agent read some other mode variable as the game type.

Totals: 97 screens; control ids 500 ✓ / 12 ✗; UITEXT
indices 191 ✓ / 34 ✗; game-type rules 23 ✓ / 26 ✗;
570 rules in all. Read by hand and confirmed by the lead analyst: the research
lab does not open in Instant Action (UITEXT 468; without researchers 467 otherwise,
`0x10161910`); the online pause menu offers only Resume Game, Exit To Lobby and Quit Game
(`0x10197D90`); the loans window and upgrade list are not built in Instant Action
(`0x10154AA0`, `0x10165A0C`); the new-player dialog (`0x1015D220`). Agent confidence
is the agent's own estimate. A ✓ checks the citation, not the interpretation.

## Screens

### Main in-game menu confirm (quit/restart variants)

Functions: `0x10000C6C`. Handler for one menu command. It shows a confirmation message box whose text depends on a state global, and cancels when the state matches neither of the two supported values. Other commands pass through to the base handler.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `(message box)` | 9: QUIT GAME  Are you sure you want to quit the game ? | Used as the header/body buffer for the confirmation (UITEXT 9). Shown with 0x10190270 as a 2-type box. The variant string is taken from one of two toc entries depending on the state value. | text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The confirm message is one of two variants chosen by a state global: value 2 uses one string and value 10 uses another. Any other state cancels the command by clearing the result flag at +4. | `0x10000C6C` | unknown | low | function ✓ |
| Not every command is handled here. Anything other than command 3 goes to the base HandleCommand. | `0x10000C6C` | all | high | function ✓ |

### Postcard / go-online notice

Functions: `0x1008CEBC`, `0x1008CFB0`. 0x1008CEBC is a message handler for a screen-level notification. On code 0x10006 it checks an online-state field and shows a notice that postcards are waiting in the outbox. Otherwise it passes the message to the base handler. 0x1008CFB0 is the screen entry and construction routine.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `(message box)` | 473: You have postcards in your outbox, waiting to be sent.   Go  | Shown as a message box through 0x10190270 when the check returns 0. The check is 0x1012D5F8, which returns the object's field at +0x60. | text ✓ |
| `0xBF432` |  | Child panel found by id in 0x1008CFB0. It is only processed when the parent pointer is non-null. The code sets a string on it and sends it message 6 if a settings field is 0. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| When the screen receives code 0x10006 and the outbox/online check returns 0, it shows the notice telling the player they have postcards waiting and must go online to send them. If the check does not return 0, it calls 0x10186A84 instead and shows no notice. | `0x1008CEBC` | not_online | medium | function ✓, game-type test ✗ |
| The notice text and the field it checks match: the field at +0x60 is most likely a connection/online state, where 0 means not connected. This is the source of the 'go online' wording. The function does not check for an actual postcard count. | `0x1008CEBC` | not_online | low | function ✓, game-type test ✗ |
| Screen entry: the routine lazily creates a large state object and several sub-objects, creates child panel 0xBF432 when the parent exists, and sets a message-6 handler on it. It returns the continue flag at +0x14. | `0x1008CFB0` | unknown | low | function ✓ |

### Visitor (guest) info panel

Functions: `0x1010AA5C`, `0x1010B0C8`, `0x1010B484`. Panel showing one selected visitor's stats, with a visitor-number slider and a 'Visitor #N' title. 0x1010AA5C builds the static labels and value widgets once. 0x1010B0C8 refreshes every value from the selected visitor record each time it runs. 0x1010B484 initialises the visitor list/scroll control (0x3734) with a base-class init and styling.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x372C` | 70: Cash remaining | Static label, set once in 0x1010AA5C. No value-update call for this id is visible in this chunk, so the value field it belongs to is unclear. | id ✓ text ✓ |
| `0x3725` | 71: Time in park | Static label. The matching value is probably the time widget 0x372D, which is fed a tick-derived time, but that link is inferred. | id ✓ text ✓ |
| `0x3726` | 72: Rides ridden | Static label, set once. Value not visible in this chunk. | id ✓ text ✓ |
| `0x3727` | 75: Purchases made | Static label, set once. Value not visible in this chunk. | id ✓ text ✓ |
| `0x3728` | 73: Sideshows played | Static label, set once. Value not visible in this chunk. | id ✓ text ✓ |
| `0x3729` | 74: Sideshows won | Static label, set once. Value not visible in this chunk. | id ✓ text ✓ |
| `0x372A` | 76: Happiness | Static label. Its bar is 0x3732, which is fed from the visitor's float at +0x19c. | id ✓ text ✓ |
| `0x372B` | 77: Current status | Static label. Its value is the formatted status text in 0x3733. | id ✓ text ✓ |
| `0x3724` |  | Value widget of a different class from the labels (created with a different constructor). Set to the constant 0x29a at build time. Refreshed each update from visitor field +0x1a0, probably the cash value (inferred). | id ✓ |
| `0x372D` |  | Time widget. Refreshed each update from a tick counter with a constant (0x61C46800) and a 0x2A0 table offset. Shows time in park (inferred). | id ✓ |
| `0x372E` |  | Numeric value widget. Refreshed from visitor field +0x1c4. Which label it belongs to is not visible here. | id ✓ |
| `0x372F` |  | Numeric value widget. Refreshed from visitor field +0x1c8. | id ✓ |
| `0x3730` |  | Numeric value widget. Refreshed from visitor field +0x1cc. | id ✓ |
| `0x3731` |  | Numeric value widget. Refreshed from visitor field +0x1d0. | id ✓ |
| `0x3732` |  | Meter/bar widget, probably the Happiness bar. Set from the visitor's float at +0x19c, scaled through the low byte times 1024 divided by 100. The range is set at build time (0..10). Medium-low confidence. | id ✓ |
| `0x3733` |  | Formatted status text for the visitor. The status code is at +0x220. Code 0x10 formats with a name taken from an indexed table entry (+0x1dc); other codes use a generic status format string. Cleared at build time. | id ✓ |
| `0x3734` |  | Visitor-number text (the value is returned by FUN_10138434). Built in 0x1010B484 with the base init and styling, and as a label in 0x1010AA5C. | id ✓ |
| `0x3735` |  | Visitor-number slider. When the visitor count (+0x30) is 0 its range value is 999. Otherwise it is count minus 1. Set via 0x1017F450. Medium confidence. | id ✓ |
| `0x3736` | 456: Visitor # | Title label. Built with a format that combines UITEXT 0x1c8 ('Visitor #') with the visitor number, and written through 0x1013876C. Medium-high confidence. | id ✓ text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The visitor panel builds its 8 stat labels once. Each label is given a fixed UITEXT string: cash remaining, time in park, rides ridden, purchases made, sideshows played, sideshows won, happiness, current status. | `0x1010AA5C` | all | high | function ✓ |
| On each refresh the visitor title shows 'Visitor #N' with the selected visitor's number. The slider range is count-1, or 999 when there are no visitors. | `0x1010B0C8` | all | medium | function ✓ |
| Happiness bar value comes from the visitor's floating-point happiness (+0x19c), clamped to a byte and scaled. | `0x1010B0C8` | all | medium | function ✓ |
| Status text: when the visitor's status code (+0x220) is 0x10, the string includes a name looked up from an indexed table entry. For any other code it uses a generic status format. | `0x1010B0C8` | all | low | function ✓ |
| The visitor-number slider is 999 when there are no visitors, otherwise count-1. | `0x1010B0C8` | all | medium | function ✓ |

### Shared helpers seen in this chunk

Functions: `0x1015bba4`, `0x1017fa64`, `0x101722a0`, `0x10172e6c`, `0x10137170`, `0x101371bc`, `0x10155280`, `0x1017b9dc`. Small helpers used by the screens above.

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| FUN_1015bba4 sends code 4 to the Park Information window handle, which closes it. | `0x1015bba4` | all | medium | function ✓ |
| FUN_1017fa64(control, flag) sets visibility or enabled state. Flag 0 clears the bit at +0x44 and flag nonzero sets it. It also walks the parent chain. | `0x1017fa64` | all | medium | function ✓ |
| FUN_101722a0(control, state, flag) sets the on/off state at +0x138 and returns 0 when unchanged. | `0x101722a0` | all | medium | function ✓ |
| FUN_10155280(2, n) sets a global screen code to n. Other first arguments are handled differently. | `0x10155280` | all | medium | function ✓ |

### Park view mouse and scroll input

Functions: `0x10137FD0`. Input handler for the main park view. It turns mouse and scroll messages into camera or push-scroll actions, and plays sound effects for clicks.

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| On a 0x100 (button) message, the handler plays one of two sound effects depending on a modifier flag from 0x1017F618 (bit 0x10 set gives sound 0xBD, otherwise 0x1F). | `0x10137FD0` | all | medium | function ✓ |
| Drag and push-scroll: when the view's option byte (at a settings struct, +0x36) is set, and the mode check passes, drag deltas are converted to camera pan or rotate through 0x10068C28 and 0x10068A74. Mode 2 is used as the gate, with a further check on the flag at +0x139 or the result of 0x1017217C. | `0x10137FD0` | unknown | low | function ✓ |
| Mode values: the function uses 0x101C7CAC(param_1) with a 1-argument call (a get, not a set) and compares the result to 2 and 5. This is a per-control state value, not the global game type, so these checks should not be read as Instant Action checks. | `0x10137FD0` | unknown | medium | function ✓ |
| Click release (0x10002) in mode 2 calls 0x10068CBC, which ends a drag or push-scroll. | `0x10137FD0` | all | low | function ✓ |

### Name (and possibly description) entry dialog

Functions: `0x101478B4`, `0x1013876C`. Modal text-entry dialog with accept and cancel. It uses two text fields from globals and two buttons, 0x3943C and 0x3943D. 0x1013876C is a general helper that writes a text label onto a named control.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x3943D` |  | Button or field toggled through 0x1017F770 and 0x101802E0 when the matching parameter is 0x943C (focus or highlight path). | id ✓ |
| `0x3943C` |  | Second button or field, set through the same path for other parameters. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Accept (message 0x100, parameter -1): the two text fields are committed to the object's record and the dialog closes with code 4. Cancel (parameter -2) closes with code 4 without committing. | `0x101478B4` | all | medium | function ✓ |
| Message 0x14 closes the dialog with cleanup calls (0x10139B1C, 0x10147D68, 0x10138E88). | `0x101478B4` | all | low | function ✓ |
| Possibly the Publish Park or rename-park dialog, since UITEXT 0xDB 'Park name' and 0xDC 'Description' exist and two text fields are used. Not confirmed. | `0x101478B4` | unknown | low | function ✓ |
| 0x1013876C writes a text label onto a named control. It creates a label object on first use, sizes it to the control's rectangle, styles it (+0x94 pointer, visible), and sets its text. | `0x1013876C` | all | medium | function ✓ |

### Shared helpers

Functions: `0x10139a7c`, `0x10139ae0`, `0x10148950`, `0x1014c0b8`, `0x1014adbc`, `0x10139578`, `0x10139b48`. Small helpers used by the screens above: show or hide a screen by id, send a close message to a window, centre the camera on a park item, and set a text-input flag.

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Show screen by id (0x10139a7c) and hide screen by id (0x10139ae0). Screen ids used in this chunk: 0x11e (visitors), 0x11d (staff), 0xbf (attractions). | `0x10139a7c` | all | high | function ✓ |
| Closing helpers send message 4 to the window: 0x10148950 closes the visitor window, 0x1014c0b8 closes the staff window, 0x1014adbc closes the attractions window. | `0x10148950` | all | high | function ✓ |
| Camera centre helper (0x10139578): given an item reference, it looks up its map position from the item's coordinates and moves the camera to that point. It also prints a debug string, which is ignored. | `0x10139578` | all | medium | function ✓ |
| Text-input flag helper (0x10139b48): calls a helper and sets a global flag to 1. | `0x10139b48` | all | low | function ✓ |

### Ride/attraction upgrades window: init, refresh and accept

Functions: `0x10144C1C`, `0x10145B44`, `0x1014552C`, `0x101460F8`. Upgrade/detail panel for a ride or item. Its name label (0x10D32) and upgrade controls (0x10D2E to 0x10D31) are refreshed from the selected item's record. Accept and close are handled through the 0x101451D0 dispatcher. The item list lives in a table (0x208-byte stride) with an in-use flag at +4.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x10D32` |  | Name label. Filled by 0x101460F8 from the item's name (up to 255 chars) when the selected item is the one being renamed, or if the item is the current selection. Created by 0x10144C1C, which also creates it hidden. | id ✓ |
| `0x10D2E` |  | Upgrade action control. Cleared and disabled when item slot 4 equals 0x1B. Otherwise its text is taken from a string table (probably the upgrade wording in UITEXT such as 'Upgrade to level', 'Cancel level', 'This ride is fully upgraded'; not confirmed) and it is enabled. Its +0xDC value is incremented on refresh. | id ✓ |
| `0x10D2F` |  | Numeric display, set from item slot 4 through 0x10144D48. Also increments +0xDC on refresh. | id ✓ |
| `0x10D30` |  | Numeric display, set from item slot 3 through 0x10144D48. Also increments +0xDC on refresh. | id ✓ |
| `0x10D31` |  | Button wired to the accept path in 0x10145B44 (see rules). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Upgrades are not available for a special category. When the item's category code (slot 4) equals 0x1B, the upgrade action is cleared and disabled. In 0x1014552C the same code is passed to 0x101451D0 together with a message from UITEXT index 27 ('Upgrades are not available in Instant Action mode'). The link to game type is inferred, not shown in this chunk. | `0x10145B44` | instant_action | medium | function ✓, game-type test ✗ |
| Refresh of the upgrade panel (message 0x1001D9): the name label is updated, the numeric displays are set, and the upgrade control text is set from a string table, or cleared when the category is 0x1B. | `0x10145B44` | all | medium | function ✓ |
| Accept/buy: the dispatcher 0x101451D0 is called with the item category (slot 3 or 4), a packed pair of slots 2 and 5, and slot 5. A return value of 2 means success: the parent is sent a 0x100002 notification with the item and the panel closes (0x101574D4). A return value of 1 or below 2 (but above 0) closes the panel without notifying. | `0x10145B44` | unknown | low | function ✓ |
| The upgrade panel's notification handling reacts to other ids. Codes in the 0x10D2D..0x10D31 range trigger cancel or deselect actions, and close the panel through 0x101574D4. | `0x10145B44` | all | low | function ✓ |
| When an item's name changes, 0x101460F8 updates the name label if the item is the current selection. If the item is a different one, it sends a 0x100002 notification to the window that owns it. Returns 0 when it found and renamed a matching item, 1 otherwise. | `0x101460F8` | all | medium | function ✓ |
| Panel construction: the upgrade panel is created hidden (0x1017FA64 with 0), a name label child 0x10D32 is added with a 0x19C object, styled, and given its default style. | `0x10144C1C` | all | medium | function ✓ |
| Message handler for the item-window list. Messages 4 to 5 (close/ remove) remove the window from the open-window table (0x208-byte entries, flag at +4), decrement a counter, send 0x14 to child windows and call 0x101574D4(0) if this was the current window. Messages 0x10006 and 0x10 handle select/deselect and notifications with 0x100002. Messages above 0xF with the flag at param3 = 0 set an item state (+0x56) to 1 through 0x1009F53C. The alternative path sets slot 10 and shows dialog 0x5A (its meaning is unknown), then resets the state to 0. | `0x1014552C` | unknown | low | function ✓ |
| Drag handling: messages 0x80080/0x80081 on a window produce an on-screen rectangle centre, scaled by 100000/75000 with a 0x5B-type placement call through 0x1009FD70, then the item position is set (0x10180490, 0x80081 event). This converts a drag into a world position for an item. | `0x1014552C` | unknown | low | function ✓ |

### Confirm-delete item / options-gated deletion

Functions: `0x10147034`. Deletes the currently selected item (ride or shop slot) after an optional confirmation. The confirmation is controlled by a game option.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `(message box)` | 396: DELETE ITEM  Are you sure you want to delete this item ? | Shown as a Yes/No-style box (type 2) through 0x10190270 when the option is on. The default button is set to 1 and the selected item index is passed as the dialog's value. | text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Delete confirmation is an option. When the option flag (settings struct at offset 0x37) is set, the 'Are you sure you want to delete this item?' box (UITEXT 396) is shown with the item index. When it is off, the item is deleted at once without the box. | `0x10147034` | all | medium | function ✓ |
| The item index is built from two bytes of the selected record: +4, plus +6 times 0x80, plus 1, with the result reduced by 1 and masked to 0x7F for the delete call. | `0x10147034` | all | low | function ✓ |
| After either path the selected-item pointer is cleared and the control is notified with 0x101C7CAC. | `0x10147034` | all | medium | function ✓ |

### Sub-window builders (generic)

Functions: `0x101471F8`, `0x10147768`. Generic helpers that build a child window from a template and wire up its controls. The builder is lazy (it does nothing if the window already exists).

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| 0x101471F8 builds a window once (when its window pointer at +0xC is 0). It creates the window from a template, names the child controls from the ids passed in, sets text from a string table entry, and installs a message-6 handler pointing back to itself. | `0x101471F8` | all | medium | function ✓ |
| 0x10147768 finds one child, stores it, sets its text from a string table entry, installs a message-0 handler and writes a label through 0x1013876C. | `0x10147768` | all | low | function ✓ |

### Rename Item dialog

Functions: `0x10147a88`. Modal dialog to rename a selected park item. Built once per calling control (cached in the caller's +8 field). Opened only for an item whose type byte is 3 and that is currently selected (global _DAT_101ed68c).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x3943e` | 194: Rename Item | Dialog title. | id ✓ text ✓ |
| `0x3943c` |  | Text field pre-filled with the item's current name. Given focus on creation. | id ✓ |
| `0x3943d` |  | Second text field, filled from a different getter (FUN_100e2cd0). Meaning unresolved. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The rename dialog appears only if an item is selected and its type byte equals 3. Otherwise nothing happens. | `0x10147a88` | all | high | function ✓ |
| The dialog is created only once per caller. Later calls do nothing if the cached dialog exists. | `0x10147a88` | all | high | function ✓ |
| The name field is pre-filled from the item's name getter, and the dialog is shown with focus on the name field. | `0x10147a88` | all | medium | function ✓ |
| No game-type branch. No name-length limit is visible in this function beyond the 0x3fff buffer size. | `0x10147a88` | all | medium | function ✓ |

### Visitor list window (All Visitors)

Functions: `0x10148654`, `0x10148078`, `0x10147f3c`, `0x101489f4`, `0x10148984`. Opens a window listing every visitor in the park as a six-column list, keeps the rows fresh on a 2-second timer, adds and removes single visitor rows, and routes its three navigation buttons to the staff window, the attractions window and a separate panel. Message handler is FUN_10148078 (the window's WM-style dispatcher). Window global is _DAT_101ed9c8, screen id 0x11e.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1e497` |  | Visitor list. Rows are visitors. Opened with an optional visitor to pre-select (found by row lookup and scrolled to). | id ✓ |
| `0x1e498` | 112: All Visitors | Title label, set on message 0x15. | id ✓ text ✓ |
| `0x10` | 113: Visitor Number | Column header 1. Header click id 0x81 (sort/command, handling not in this chunk). | id ✓ text ✓ |
| `0x11` | 114: Cash Remaining | Column header 2. Click id 0x82. | id ✓ text ✓ |
| `0x12` | 115: Time In Park | Column header 3. Click id 0x83. | id ✓ text ✓ |
| `0x13` | 116: Rides Ridden | Column header 4. Click id 0x84. | id ✓ text ✓ |
| `0x14` | 117: ? | Column header 5 is the literal placeholder '?' in the UITEXT table. Click id 0x85. | id ✓ text ✓ |
| `0x15` | 118: Happiness | Column header 6. Click id 0x86. | id ✓ text ✓ |
| `0x1e49a` |  | Navigation button. Opens the staff window (FUN_1014ba4c, last staff category). | id ✗ |
| `0x1e49b` |  | Navigation button. Opens the attractions window (FUN_1014abe4, last attraction category). | id ✓ |
| `0x1e499` |  | Navigation button. Calls FUN_1015af1c, which builds a different panel (resource sad_undec.wct, UITEXT 156 'Top 3 Thoughts'). Identity inferred, see unknowns. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Opening the visitor window shows the list with six columns and their header texts, optionally scrolled to and selecting one visitor, and makes screen 0x11e visible. Any running refresh timer 0x80083 is stopped first, so the window starts its own refresh cycle. | `0x10148654` | all | medium | function ✓ |
| While the window is open, a timer tick with id 0x80083 refreshes every visitor row. All six columns are redrawn (update mask 0x3f). Per row it reads the visitor's happiness and state fields and a color/icon table at a global. | `0x10147f3c` | all | high | function ✓ |
| A visitor who arrives in the park gets a new row in the visitor list. The row holds six values derived from visitor fields. A value of 999 is used when the visitor's field at +0x30 is zero, otherwise field minus 1 is used. The list count is then redrawn. | `0x101489f4` | all | medium | function ✓ |
| When a visitor leaves, that visitor's row is removed from the list and a global visitor counter is decremented, but only if the row was found. | `0x10148984` | all | medium | function ✓ |
| Message 0x402 (row selected in the visitor list) centres the camera on that visitor (FUN_10139578 with the visitor's position) and then closes the visitor window (message 4 to the window). | `0x10148078` | all | high | function ✓ |
| Message 0x400 (row activated) passes the visitor pointer to FUN_1010b808 with a global helper object. Its meaning (possibly follow or show info) is not resolved in this chunk. | `0x10148078` | all | low | function ✓ |
| Button 0x1e49a opens the staff window and button 0x1e49b opens the attractions window, both with 'last used category' (-1). Button 0x1e499 calls the Top 3 Thoughts panel builder. | `0x10148078` | all | high | function ✓ |
| Closing the visitor window (message 0x14 or 5, or -2) clears the window global and hides screen 0x11e. | `0x10148078` | all | high | function ✓ |
| Message 0x406 stores an int into a per-window global, presumably a remembered value such as scroll or sort state. Purpose not confirmed. | `0x10148078` | all | low | function ✓ |
| No game-type branch is in these functions. Nothing here depends on Instant Action, Full Simulation or online, and no costs or limits apply. | `0x10148654` | all | medium | function ✓ |

### Attractions window (All Rides / All Shops / All Sideshows / All Miscellaneous Items)

Functions: `0x1014abe4`, `0x1014a724`, `0x1014a14c`, `0x10149e8c`, `0x10149b84`, `0x1014a418`. One window (global _DAT_101ed9ec, screen 0xbf) with four category tabs: Rides, Shops, Sideshows, Miscellaneous. Each tab rebuilds the same window with its own column set and item-type filter. Clicking an item opens its detail (via FUN_101397b0, not in this chunk). The current category is remembered in global _DAT_101efc04.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x12c4b8` |  | Outer panel. Help text is set on it from a help-text resource. Contains the tabs, title and list. | id ✓ |
| `0x12c4b9` |  | Tab group. Its selected state shows the current category, set with FUN_10172e6c. | id ✓ |
| `0x12c4ba` |  | Tab: Rides (category 0). Notify 1 switches to the rides builder and sets title 'All Rides'. | id ✓ |
| `0x12c4bb` |  | Tab: Sideshows (category 2). Notify 1 switches to the sideshows builder and sets title 'All Sideshows'. | id ✓ |
| `0x12c4bc` |  | Tab: Shops (category 1). Notify 1 switches to the shops builder and sets title 'All Shops'. | id ✓ |
| `0x12c4bd` |  | Tab: Miscellaneous (category 3). Notify 1 switches to the misc builder and sets title 'All Miscellaneous Items'. | id ✓ |
| `0x12c4be` |  | Title label. Set to 'All Rides' (78), 'All Shops' (79), 'All Sideshows' (80) or 'All Miscellaneous Items' (81) per category. | id ✓ |
| `0x190` |  | Item detail sub-window (child id 400). Closed whenever the category changes. | id ✓ |
| `Rides columns 0x10-0x14` |  | Ride list columns. Click ids 0x69, 0x6a, 0x6b, 0x6c, 0x6d. |  |
| `Shops columns 0x10-0x14` |  | Shop list columns. Click ids 0x69, 0x248, 0x249, 0x24a, 0x24b. |  |
| `Sideshows columns 0x10-0x15` |  | Sideshow list columns. Click ids 0x69, 0x248, 0x6b, 0x249, 0x24a, 0x24b. |  |
| `Misc columns 0x10-0x11` |  | Misc list columns. Click ids 0x69, 0x24c. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The attractions window opens on screen 0xbf. With argument -1 it reuses the last category. Each category is stored in the global and drives tab selection and title text. The builder for the chosen category is then called and the help text is set. | `0x1014abe4` | all | high | function ✓ |
| Each category builder rebuilds the window with its own columns (see controls) and its own item-type filter bits. Rides use 0x80 and 0x2000, shops 0x200, sideshows 0x100, misc 0x400. | `0x1014a14c` | all | medium | function ✓ |
| Column 0 is turned off and columns 1 onward are turned on in every builder, but the meaning of the on/off flag is not resolved. | `0x1014a14c` | all | low | function ✓ |
| If the caller passes an item, the builder finds its row in the new list and selects and scrolls to it. | `0x1014a14c` | all | medium | function ✓ |
| Switching category first closes any open item detail (child 400) so the new list starts clean. | `0x10149b84` | all | medium | function ✓ |
| Notify 0x101 with value 1 on the four tabs switches category. Rides, sideshows, shops and misc are selected by the matching tab id. | `0x1014a724` | all | high | function ✓ |
| Message 0x400 (row activated) calls FUN_101397b0 with the item. That function opens the detail window for the item's kind, so this chunk only hands off. | `0x1014a724` | all | high | function ✓ |
| Message 0x402 (row selected) centres the camera on the item and closes the attractions window. | `0x1014a724` | all | high | function ✓ |
| Message 0x406 stores an int into a per-category global (category 0 to 3), so each category keeps its own remembered value. | `0x1014a724` | all | low | function ✓ |
| Detail dispatch in FUN_101397b0 (outside this chunk): for item type 3 it branches on a sub-kind at +0x7a8. Sub-kind 2 opens screen 0xd0, sub-kind 1 opens screen 0xd1, and sub-kind 0 opens screen 0x12d when +0x288 is 2, else an inline path. Sub-kind 3 calls FUN_1014fd78, which was not read. | `0x1014a724` | all | low | function ✓ |
| No game-type branch in these builders or handlers. Costs or limits are not in this chunk. | `0x1014abe4` | all | medium | function ✓ |

### Staff window (All Staff / happiness bar)

Functions: `0x1014ba4c`, `0x1014b124`, `0x1014b334`, `0x1014af00`, `0x1014aff4`. A window (global _DAT_101eda0c, screen 0x11d) listing staff and showing happiness per staff category. Five staff categories are chosen with buttons. The window has navigation buttons to the visitor and attraction windows, and a refresh timer. Uses resources ridestatbar.wct and happygrad.wct.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x321` |  | Staff list. Rows are staff members of the selected category. | id ✓ |
| `0x322` |  | Category button group. Its checked state shows the selected staff category. | id ✓ |
| `0x323` |  | Category button: Janitors (category 0). Notify 1 switches to it. | id ✓ |
| `0x326` |  | Category button: Mechanics (category 1). | id ✓ |
| `0x325` |  | Category button: Entertainers (category 2). | id ✓ |
| `0x327` |  | Category button: Guards (category 3). | id ✓ |
| `0x324` |  | Category button: Researchers (category 4). | id ✓ |
| `0x328` | 100: All Staff | Title label, set on message 0x15. | id ✓ text ✓ |
| `0x329` |  | Happiness panel container. | id ✓ |
| `0x32a` |  | Category happiness label, set per selected category. | id ✓ |
| `0x32b` | 106: Average happiness for all staff | Caption for the average gauge. | id ✓ text ✓ |
| `0x32c` |  | Average happiness gauge, value derived from a global staff-happiness figure. | id ✓ |
| `0x32d` |  | Category happiness gauge, value from the category's happiness figure. | id ✓ |
| `staff list columns 0x10-0x14` |  | Column headers. Click ids 0x77-0x7b. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Staff categories map to staff-happiness codes: Janitors 5, Mechanics 4, Entertainers 6, Guards 7, Researchers 8. The category's gauge uses the matching code. The labels come from UITEXT 107-111. | `0x1014ba4c` | all | medium | function ✓ |
| Selecting a staff category stores it in a global (the staff category global), sets the filter bit for that category (Janitors 2, Mechanics 4, Entertainers 0x10, Guards 8, Researchers 0x20), and refreshes the list. | `0x1014af00` | all | medium | function ✓ |
| The five category buttons each select a category: 0x323 Janitors, 0x324 Researchers, 0x325 Entertainers, 0x326 Mechanics, 0x327 Guards. Selecting one updates the category label and gauge and refreshes the list. | `0x1014b334` | all | medium | function ✓ |
| A timer tick 0x80083 refreshes staff rows, updating only columns 1, 3 and 4 (status, skill, happiness). Status code 1 is shown as 0 unless a secondary check passes. | `0x1014aff4` | all | medium | function ✓ |
| Message 0x32e calls FUN_1015af1c (Top 3 Thoughts panel, see unknowns). Message 0x32f opens the visitor window. Message 0x330 opens the attractions window with the last category. | `0x1014b124` | all | high | function ✓ |
| Message 0x402 centres the camera on the selected staff member and closes the window. Message 0x14 clears the window global and hides screen 0x11d. | `0x1014b124` | all | high | function ✓ |
| Message 0x400 on the staff window calls FUN_1016e1a4 with the staff item. Purpose not resolved. | `0x1014b124` | all | low | function ✓ |
| Staff window builder: the rebuilt window shows the list, the five category buttons with the current one checked, the title 'All Staff' (set in the handler), and the happiness panel. It is shown on screen 0x11d and remembers the last staff category. | `0x1014ba4c` | all | high | function ✓ |
| No game-type branch in the staff window. Nothing here depends on game mode. | `0x1014ba4c` | all | medium | function ✓ |

### Item info sub-panel (Users last month / Cleanliness / Scrap value)

Functions: `0x1014c280`, `0x1014c53c`. Builds and refreshes a small info block inside a parent window (sub-panel at +0x10, parent at +0xc) for one park item. Shows a name field, cleanliness gauge, scrap value, and two indicators.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x15bbe` | 60: Users last month | Static caption. Its value field is not set in this chunk. | id ✓ text ✓ |
| `0x15bbc` | 61: Cleanliness | Static caption for the cleanliness gauge. | id ✓ text ✓ |
| `0x15bbf` | 63: Scrap value | Static caption for the scrap value. | id ✓ text ✓ |
| `0x15bbb` |  | Cleanliness gauge, gradient style (happygrad). Set to 50 initially. Refreshed to the item's cleanliness value. | id ✓ |
| `0x15bbd` |  | Name-like field. Set from a 30-character string at the item's +0x1a4 offset. | id ✓ |
| `0x15bc0` |  | Value label for scrap value, set from a calculated value. | id ✓ |
| `0x15bc4` |  | Indicator in the parent window (+0xc). Set from a status predicate on the item. | id ✓ |
| `0x15bb4` |  | Indicator in the parent window (+0xc). Set for item states 11 to 15 and cleared otherwise. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The item-info block is built with the captions 'Users last month', 'Cleanliness' and 'Scrap value' and a gauge that starts at 50. | `0x1014c280` | all | high | function ✓ |
| The cleanliness gauge is set from the item's +0x40 value as a fixed-point percentage. | `0x1014c53c` | all | medium | function ✓ |
| The scrap value is calculated from the item's sub-value and a record field at +0x1b8, divided by 100. | `0x1014c53c` | all | low | function ✓ |
| Indicator 0x15bc4 is checked when the item passes a status predicate. The predicate is true only if the item's status is not 1, 2 or 4, the field at +0x60 is zero, a further check passes, and either a record check or a secondary helper is true. | `0x1014c53c` | all | low | function ✓ |
| Indicator 0x15bb4 is set when the item's state code from FUN_10138a1c is between 11 and 15 inclusive. Those states are shown as the active indicator, others are cleared. The whole block is refreshed only if the state changed. | `0x1014c53c` | all | low | function ✓ |
| No game-type branch, no costs, no limits. | `0x1014c53c` | all | medium | function ✓ |

### Sibling action panel (ids 0x15bb6, 0x15bb7, 0x15bc1)

Functions: `0x1014c774`. Same pattern as the item panel above, for a different panel whose controls are 0x15bb6, 0x15bb7 and 0x15bc1. It hides two controls when the permission query fails and then sets a text/resource on 0x15bc1.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x15bb6` |  | Hidden (set off) when the permission query returns less than 2. | id ✓ |
| `0x15bb7` |  | Hidden (set off) when the permission query returns less than 2. | id ✓ |
| `0x15bc1` |  | Target of the text/resource set in 0x10147768. The exact string is not resolved in this chunk. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The two buttons are hidden unless the permission query returns 2 or more. | `0x1014c774` | unknown | medium | function ✓ |

### Ride options and upgrade-state panel

Functions: `0x1014de0c`, `0x1014df5c`, `0x1014e5f0`, `0x1014cddc`. Refreshes the ride-info panel (child 0x18 of the ride-info root) for the currently selected ride. It sets a trough/loop toggle, a set of on/off indicator children, and a single-choice (radio) selection that also sets the status-bar help line. FUN_1014df5c is the entry point that switches the selected object; FUN_1014e5f0 only refreshes the indicators; FUN_1014cddc applies the radio choice.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x18` |  | Container of all the children below. Its child 0x138 holds the current radio selection (managed by FUN_10172e6c). | id ✓ |
| `0x20` |  | Toggle with a sprite-name label ('cb_loop' or 'cb_trough', not UITEXT). Icon ids 0x132 (loop) or 0x134 (trough) are chosen by mask bit 0x200. It is cleared and blanked when the selected object's flag byte at +0x4c is 0. When enabled, it is checked from mask bit 0x2 (in 1014df5c only if that object flag is non-zero). | id ✓ |
| `0x1f` | 31: Buy Upgrades | On/off indicator driven by mask bit 0x1. Label match is inferred only; the code sets state, not text. | id ✓ text ✗ |
| `0x1b` | 27: Upgrades are not available in Instant Action mode | On/off indicator driven by mask bit 0x4. Name suggests a notice; the code does not tie it to game type here. | id ✓ text ✗ |
| `0x19` | 25: Upgrades | Indicator for mask bit 0x8. Radio choice for bit 0x8 also sets status help text 14 (move the ride). | id ✓ text ✗ |
| `0x1a` | 26: No upgrades have been researched | Indicator for mask bit 0x10. Radio choice sets status help text 12 (open the ride). | id ✓ text ✗ |
| `0x1c` | 28: This ride is fully upgraded | Indicator for mask bit 0x20. Radio choice sets status help text 13 (close the ride). | id ✓ text ✗ |
| `0x21` |  | Indicator for mask bit 0x40. Radio choice sets status help text 10 (build or edit the ride's track). | id ✓ |
| `0x1e` | 30: Cancel level | Indicator for mask bit 0x80. Radio choice sets status help text 15 (delete the ride). | id ✓ text ✗ |
| `0x1d` | 29: Upgrade to level | Indicator for mask bit 0x100. Radio choice sets status help text 16 (ride the ride). | id ✓ text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Each bit of the 9-bit state mask drives one indicator child. The child is on exactly when its bit is set. | `0x1014df5c, 0x1014e5f0` | all | high | function ✓ |
| Selecting a ride re-runs the panel only when the requested object differs from the one already shown, or when no panel exists yet, and only when the global busy byte is 0. The selection is stored, the object is refreshed, and the toggle is rebuilt. | `0x1014df5c` | all | high | function ✓ |
| If a helper check on the selected object is true (bit 0 set and bit 1 clear on its flag word), the function takes a different path that opens a status help line (help 12) and skips the normal rebuild. | `0x1014df5c` | unknown | low | function ✓ |
| The trough/loop toggle is shown only when the selected object's flag byte at +0x4c is non-zero. Otherwise it is cleared, blanked and unchecked. | `0x1014de0c` | all | high | function ✓ |
| The toggle's label and icon: if mask bit 0x200 is clear it uses 'cb_loop' and icon 0x132, otherwise 'cb_trough' and icon 0x134. It is checked from bit 0x2. | `0x1014de0c` | all | high | function ✓ |
| The toggle is checked only when mask bit 0x2 is set and the object's +0x4c flag is non-zero. | `0x1014df5c` | all | high | function ✓ |
| The radio choice has a fixed priority order: bit 0x4 first, then 0x1, 0x2, 0x8, 0x10, 0x20, 0x40, 0x80, 0x100. If none is set, the radio selection is cleared. Bits 0x4, 0x1 and 0x2 set no status help text. | `0x1014cddc` | all | high | function ✓ |
| The radio setter changes the selection inside the 0x18 group: it deselects the previous child, selects the new one, and returns the previous id. Passing -1 clears the selection. | `0x1014cddc (via 0x10172e6c)` | all | high | function ✓ |
| Mask bit 0x2 (toggle) and bit 0x4 (the 0x1b indicator) appear to be the game-type dependent items. In this chunk only the object flag gates the toggle. The Instant Action wording on 0x1b is not wired to a game-type check here. | `0x1014df5c, 0x1014e5f0` | instant_action | low | function ✓, game-type test ✗ |

### Ticket Price dialog

Functions: `0x1014ec48`. A dialog for setting a price. It is built the first time it is opened. It shows the title 'Ticket Price', a current-price display, a slider, and a toggle bound to a park setting.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x4f3b0` | 160: Ticket Price | Caption, set with UITEXT 0xa0 (160). | id ✓ text ✓ |
| `0x4f3af` |  | Price text computed from a per-park table entry through a formatter (0x10138e50), set on each refresh. | id ✓ |
| `0x4f3ae` |  | Slider with range 0 to 10000 (set via 0x1017d36c). Its value comes from a field at +0x118 of the object returned by 0x10108424 (the park object). | id ✓ |
| `0x4f3ac` |  | Forced to 0 (off) when the game type is 2 (Instant Action). | id ✓ |
| `0x4f3b1` |  | Toggle set from the park field at +0x1da710 (non-zero means on). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| In Instant Action the 0x4f3ac control is forced to 0 (off) when the dialog is built. | `0x1014ec48` | instant_action | high | function ✓, game-type test ✓ |
| The dialog is built only the first time. Afterwards the build path is skipped. | `0x1014ec48` | all | high | function ✓ |
| The dialog is opened from the Financial Information dialog (message id 0x12281 in 0x10150264). | `0x10150264 -> 0x1014ec48` | unknown | medium | function ✓ |
| Building the dialog also shows a status message from message resource 0x12f ('Position' in UITEXT, but this is a separate message table, so the mapping is unconfirmed). | `0x1014ec48 -> 0x10139a7c` | all | low | function ✓ |

### Value editor with two arrow buttons ('Rename Item' title)

Functions: `0x1014f308`, `0x1014ef68`. A small editor dialog. It has two arrow buttons (0x3943c, 0x3943d) that step a value, a caption set from UITEXT 0xc2 'Rename Item' (on 0x3943e), and OK/Cancel handling. On OK it reads two text fields from the selected item, runs a check, and refreshes the Ticket Price display (0x4f3af). Built only once.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x3943c` |  | Arrow button with the 'hilight' sprite. Initially given focus. Its press changes the value. | id ✓ |
| `0x3943d` |  | Second arrow button with the 'hilight' sprite. Focus is moved here on the 0x802 message when the other one is pressed. | id ✓ |
| `0x3943e` | 194: Rename Item | Title label, set from UITEXT 0xc2 (194). | id ✓ text ✓ |
| `0x4f3af` |  | Ticket Price display (in another dialog root). Updated after OK. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| On OK (message -1) the handler loads two text fields from the selected item record, applies an index-based XOR scramble to each, and compares them against two hard-coded wide strings held in globals. Only if both match does it set a flag through 0x1010e5bc. What that flag unlocks is not established. | `0x1014ef68` | unknown | low | function ✓ |
| On OK or Cancel (-2) the dialog closes through the dismiss helper with result code 4. | `0x1014ef68` | all | medium | function ✓ |
| Pressing the arrow button 0x3943c moves focus to 0x3943d on message 0x802 when the 0x3943c id is passed. Other 0x802 ids are sent on to the generic handler. | `0x1014ef68` | all | medium | function ✓ |
| The panel is built only the first time, with the title caption set from UITEXT 0xc2. | `0x1014f308` | all | high | function ✓ |

### Item info panel (Number owned / Scrap value) with action buttons

Functions: `0x1014f6e4`, `0x1014f8d0`, `0x1014fa18`, `0x1014fb24`, `0x1014f9a4`. A detail panel for the selected item (the object in the global at _DAT_101ed68c). It shows two labelled values, a checkbox, and three action buttons (delete/sell 0x157b and two buttons 0x157c/0x157d), plus a button 0x157f that opens another panel. Its message handler also dispatches the button presses.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1587` | 53: Number owned | Caption label, created by 0x1014f6e4. | id ✓ text ✓ |
| `0x1586` |  | Shows the item count, read from the selected object's 16-bit field at +0xe and formatted through a helper (0x100c5828). Refreshed by 0x1014f8d0 and 0x1014fb24. | id ✓ |
| `0x1585` | 54: Scrap value | Caption label, created by 0x1014f6e4. | id ✓ text ✓ |
| `0x1584` |  | Shows the scrap value from a helper on the selected object (0x100e25e8). Refreshed by 0x1014f8d0 and 0x1014fb24. | id ✓ |
| `0x1581` |  | Checkbox bound to the item flag bit 0x10 at +0x2e. Checked state is set in 0x1014fa18. A click calls 0x100e1a40 on the item to toggle it. | id ✓ |
| `0x157b` |  | Delete/sell action. Depends on the global game flag byte at +0x37 (see rules). | id ✓ |
| `0x157c` |  | Action button B (queued action with a different dispatch helper). Hidden or shown by the permission query in 0x1014f9a4. | id ✓ |
| `0x157d` |  | Action button A (queued action). Hidden or shown by the permission query in 0x1014f9a4. | id ✓ |
| `0x157f` |  | Opens a sub-panel via 0x1014abe4 with mode 3 (its own help/title is in that function, not in this chunk). | id ✓ |
| `0x1582 / 0x1583` |  | Presses are forwarded to 0x101473bc (not in this chunk). |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Pressing the delete/sell button: if the game flag byte at +0x37 is 0, the item at the selected index is removed immediately. Otherwise a confirmation message box with 'DELETE ITEM ... Are you sure' is shown. | `0x1014fb24 -> 0x10147034` | unknown | medium | function ✓ |
| The selected item index is one-based: byte at +4 plus byte at +6 times 128, plus 1. | `0x10147034` | all | medium | function ✓ |
| Action buttons 0x157c and 0x157d are hidden unless the permission query returns 2 or more. The query is keyed by a message code that maps to an action flag (-2 -> 0x40, -1 -> 0x1, 0 -> 0x80, 1 -> 0x200, 2 -> 0x100, 3 -> 0x800, 0x3ea -> 0x1000, 0x3eb -> 0x8000, 0x3ec -> 0x4000). | `0x1014f9a4, 0x10146b48` | unknown | medium | function ✓ |
| Action 0x157d runs the queued action (through the idle-check helper). Action 0x157c uses the variant with the other queue helper. Both are gated by the idle check. | `0x1014fb24 -> 0x10146c80 / 0x10146de0` | unknown | medium | function ✓ |
| The checkbox 0x1581 is checked when bit 0x10 of the item flag word at +0x2e is set. | `0x1014fa18` | all | high | function ✓ |
| Refresh sets the count from +0xe and the scrap value from a helper. This runs on the refresh message (0x10 with id 0x80080), and once when the panel is built. | `0x1014f8d0, 0x1014fb24` | all | high | function ✓ |
| Creating the panel sets captions 'Number owned' (UITEXT 53) and 'Scrap value' (UITEXT 54) and blank value fields. | `0x1014f6e4` | all | high | function ✓ |
| The permission query (0x10146b48) is also the place where per-action gating for the action buttons is read. It writes the last action code to a global and returns a stored result. | `0x10146b48` | unknown | low | function ✓ |

### Financial Information dialog and its category graph

Functions: `0x10150264`, `0x101503b0`. The Financial Information dialog. It has a title, a button that opens the Ticket Price dialog, a button that opens another sub-panel (0x1227f via 0x10154aa0, not in this chunk), and a set of toggles that choose which money categories appear in a graph, plus a three-way mode selector.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x12271` | 161: Financial Information | Title, set from UITEXT 0xa1 (161) on the paint message (0x15). | id ✓ text ✓ |
| `0x1227f` |  | Opens another sub-panel via 0x10154aa0 (not in this chunk). Gated by the game type inside that function. | id ✓ |
| `0x12281` |  | Opens the Ticket Price dialog (0x1014ec48). | id ✓ |
| `0x12272` |  | Container of the toggles and mode buttons. | id ✓ |
| `0x12277 / 0x1227b` |  | Exclusive pair of toggles (bits 0x1 and 0x2). Pressing one clears the other and clears the six B toggles. |  |
| `0x12278, 0x12279, 0x1227a, 0x1227c, 0x1227d, 0x1227e` |  | Six independent toggles (bits 0x8, 0x4, 0x40, 0x10, 0x20, 0x80). Pressing one clears the A pair, then sets its own bit. |  |
| `0x12274 / 0x12275 / 0x12276` |  | Three-way mode selector. 0x12275 = mode 0, 0x12276 = mode 1, 0x12274 = mode 2. Each sets a pair of help-text ids for its tooltip. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The A pair (0x12277 and 0x1227b) and the B toggles are mutually exclusive. Turning on an A item clears all B items. Turning on a B item clears the A pair. | `0x101503b0` | all | high | function ✓ |
| Each mode sets one tooltip pair: mode 0 uses help 11/12 ('Click to buy', 'Click to open the ride'), mode 1 uses help 35/36 ('cycle through all shops' forward/back), mode 2 uses help 143/144. Only pairs of help text are set here. | `0x101503b0` | all | medium | function ✓ |
| Each toggle press recomputes the graph totals. A helper walks per-category tables in the park object (offsets such as 0x1f100, 0x1f350, 0x1fc90, 0x1fee0 for bits 0x1 to 0x8) and returns totals, which feed a graph setter and a redraw. The exact categories and graph type are not confirmed. | `0x101503b0 -> 0x1014fe1c, 0x1014ff74` | all | low | function ✓ |
| Message 0x100 with id -2 closes the dialog and releases its draw context. Id 5 and any unknown message fall through to the generic handler. | `0x10150264, 0x101503b0` | all | medium | function ✓ |
| Opening the sub-panel 0x1227f is gated by the game type inside 0x10154aa0, which is not in this chunk. Only its call site is visible here. | `0x10150264 -> 0x10154aa0` | unknown | low | function ✓ |

### Financial Information panel (graph and series toggles)

Functions: `0x10150c54`, `0x10151698`. Graph window that plots park money history for a chosen period. Eight checkboxes choose which series are plotted. Only one instance may be open at a time.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x12272` |  | Graph canvas. FUN_10151698 redraws it with the finance data when the window is open. | id ✓ |
| `0x12273` |  | Period radio group holding the three period buttons below. One is selected when the window is built. | id ✓ |
| `0x12274` | 364: 12 Years | Period button. Selected when the period global equals 2. | id ✓ text ✓ |
| `0x12275` | 362: 1 Year | Period button. Selected when the period global equals 0. | id ✓ text ✓ |
| `0x12276` | 363: 3 Years | Period button. Selected when the period global equals 1. | id ✓ text ✓ |
| `0x12277` | 162: Bank balance | Series checkbox, bit 0x01 of the series bitmask. | id ✓ text ✓ |
| `0x1227b` | 163: Park value | Series checkbox, bit 0x02 of the series bitmask. | id ✓ text ✓ |
| `0x12279` | 164: Money in | Series checkbox, bit 0x04 of the series bitmask. | id ✓ text ✓ |
| `0x12278` | 165: Gate takings | Series checkbox, bit 0x08 of the series bitmask. | id ✓ text ✓ |
| `0x1227c` | 166: Shop takings | Series checkbox, bit 0x10 of the series bitmask. | id ✓ text ✓ |
| `0x1227d` | 167: Sideshow takings | Series checkbox, bit 0x20 of the series bitmask. | id ✓ text ✓ |
| `0x1227a` | 168: Money out | Series checkbox, bit 0x40 of the series bitmask. | id ✓ text ✓ |
| `0x1227e` | 169: Staff costs | Series checkbox, bit 0x80 of the series bitmask. | id ✓ text ✓ |
| `0x1227f` |  | Text is cleared (set to empty) when the game type is Instant Action. Its normal text is not set in this function. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Opening the panel does nothing if a Financial window is already open (single instance). | `0x10150c54` | all | high | function ✓ |
| Period radio selection comes from a game global: 0 selects 1 Year, 1 selects 3 Years, 2 selects 12 Years. Any other value leaves nothing selected. | `0x10150c54` | all | high | function ✓ |
| Series checkboxes start checked from an 8-bit bitmask global, bit to series as listed in the controls. | `0x10150c54` | all | high | function ✓ |
| In Instant Action (game type 2) the text on control 0x1227f is blanked. In other modes it is left as set by the template. | `0x10150c54` | instant_action | high | function ✓, game-type test ✓ |
| The game type is read lazily the first time this window is built (the init function is called once, guarded by a flag). | `0x10150c54` | all | medium | function ✓ |
| The graph is redrawn only when the Financial window is open. The redraw passes two computed values (from helpers that read the same series mask) and 10 to the graph. | `0x10151698` | all | medium | function ✓ |
| Opening the panel posts UI notification 0xc5 (meaning not determined). | `0x10150c54` | all | low | function ✓ |

### Staff hiring dialog (Hire Staff)

Functions: `0x10152368`, `0x10152158`, `0x10151ba0`, `0x101518f4`, `0x10152e50`, `0x101519d0`, `0x10152e1c`. Lists candidate staff for the selected staff type (Janitor, Mechanic, Entertainer, Guard, Researcher), shows skill, cost preview and a 3D preview, and hires a chosen candidate. Also shows a cash readout and a monthly cash-flow summary.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x2480` | 139: Hire Janitors | Hire button. Label changes with the staff type: 0x8b Hire Janitors (type 0), 0x8c Hire Mechanics (1), 0x8d Hire Entertainers (2), 0x8e Hire Guards (3), 0x8f Hire Researchers (4). | id ✓ text ✓ |
| `0x248f` |  | Radio group of the five staff-type buttons below. Selecting a child changes the staff type. | id ✓ |
| `0x2490` |  | Staff-type button for Janitors (type 0). | id ✓ |
| `0x2493` |  | Staff-type button for Mechanics (type 1). | id ✓ |
| `0x2492` |  | Staff-type button for Entertainers (type 2). | id ✓ |
| `0x2494` |  | Staff-type button for Guards (type 3). | id ✓ |
| `0x2491` |  | Staff-type button for Researchers (type 4). | id ✓ |
| `0x248e` |  | Candidate list. One row per candidate of the selected type: name (formatted from the candidate record) and monthly wage (second value from the record). Selecting a row (0x401) updates the panels below. Activating a row (0x400) hires it. | id ✓ |
| `0x10` | 144: Candidate Name | Column header for the candidate name column. | id ✓ text ✓ |
| `0x11` | 145: Monthly Wage | Column header for the wage column. | id ✓ text ✓ |
| `0x2481` |  | Skill panel. Contains the skill label and skill bar. | id ✓ |
| `0x2483` | 146: Skill | Skill label. UITEXT 0x92 is set through the help/tooltip setter, so it is most likely a tooltip rather than visible text (medium-low confidence). | id ✓ text ✓ |
| `0x2482` |  | Skill bar (loaded from the ridestatbar widget). Set to the selected candidate's skill byte, scaled by 1024/5. | id ✓ |
| `0x2485` |  | Cost panel for the park. Value rows: 0x2489 cash in, 0x248c staff costs, 0x248b other costs, 0x2487 balance. | id ✓ |
| `0x2488` | 357: Cash in | Row label for cash in, set through the help/tooltip setter (medium-low confidence). | id ✓ text ✓ |
| `0x248d` | 358: - Staff costs | Row label for staff costs, set through the help/tooltip setter (medium-low confidence). | id ✓ text ✓ |
| `0x248a` | 359: - Other costs | Row label for other costs, set through the help/tooltip setter (medium-low confidence). | id ✓ text ✓ |
| `0x2486` | 361: Balance | Row label for balance, set through the help/tooltip setter (medium-low confidence). | id ✓ text ✓ |
| `0x2484` |  | Area whose rectangle is used to place the 3D preview of the selected candidate. | id ✓ |
| `0x2496` | 458: Cash  $ | Cash readout. Shows Cash $ N (0x1ca) when cash is zero or positive, or Cash -$ N (0x1cb) when negative. | id ✓ text ✗ |
| `0x2495` |  | Button whose handler opens another dialog through FUN_101643b4 with argument -1. The dialog and its purpose are not identified. | id ✓ |
| `0x80080` |  | Pseudo control. Its message 0x10 triggers the cash readout refresh. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Each staff type maps to one hire label and one radio button: 0 Janitor (0x8b, 0x2490), 1 Mechanic (0x8c, 0x2493), 2 Entertainer (0x8d, 0x2492), 3 Guard (0x8e, 0x2494), 4 Researcher (0x8f, 0x2491). | `0x10152158` | all | high | function ✓ |
| Opening the dialog builds it when it is closed, when the requested type is -1, or when the requested type equals the current one. If it is already open with a different type, only the radio, label and candidate list are switched. | `0x10152368` | all | medium | function ✓ |
| Candidate list shows the pool entries (up to 32) whose type equals the selected staff type. | `0x101518f4` | all | high | function ✓ |
| Switching the staff type clears the selection and refills the list for the new type. | `0x10152158` | all | high | function ✓ |
| Selecting a candidate sets the skill bar from the candidate's skill byte and recomputes the cost preview: staff costs become the current total costs plus the candidate's wage, and balance becomes a budget-like value minus (costs plus wage). | `0x10151ba0` | all | low | function ✓ |
| The 3D preview is rebuilt only when the candidate's type or the variant byte at offset 9 changes. Type maps to model index: 0 to 5, 1 to 6, 2 to 4, 3 to 7, 4 to 8. | `0x101519d0` | all | medium | function ✓ |
| Activating a candidate row hires that candidate, then closes the dialog and posts UI notification 0x135. No budget or affordability check is visible in this function. | `0x10151ba0` | all | medium | function ✓ |
| Cancel (button value -2) and the close message (5) both close the dialog. Close consumes the message. | `0x10151ba0` | all | high | function ✓ |
| The cash readout is refreshed when the dialog opens and on message 0x10 for control 0x80080. Negative cash is shown with a minus-dollar label. | `0x10151ba0` | all | high | function ✓ |
| Closing (message 0x14) clears the open-handle, resets the selection indices, posts 0xbd, and resumes the main screen. | `0x10152e1c` | all | medium | function ✓ |
| When one candidate changes, the list row is added if it matches the selected type, removed if the candidate is gone, and refreshed otherwise. | `0x10152e50` | all | medium | function ✓ |
| No Instant Action or Full Simulation check exists in the staff dialog functions in this chunk. Any restriction on staff hiring would have to be elsewhere. | `0x10151ba0` | all | high | function ✓ |

### Load Park dialog

Functions: `0x10153960`, `0x10152fb0`, `0x10153a7c`. Shows the list of saved parks and loads the one the player picks.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x23bce1` | 202: Load Park | Title label. | id ✓ text ✓ |
| `0x23bce0` |  | List of saved parks, populated by a helper not in this chunk. | id ✓ |
| `0x23bce2` |  | Name text field, hidden on open. Load has no name entry. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Opening the Load dialog has no single-instance guard. It sets the main screen busy flag and hides the name field. | `0x10153960` | all | medium | function ✓ |
| Activating a row (0x400) walks to the selected row, then calls a list action that builds a file path from the saved-park name and calls a file routine. The decompile shows a missing argument on that call, so the exact action is uncertain. | `0x10152fb0` | all | low | function ✓ |
| Cancel (value -2) closes the dialog. Close message 5 closes it and consumes the message. | `0x10152fb0` | all | high | function ✓ |
| Closing (0x14) restores the main screen. | `0x10152fb0` | all | medium | function ✓ |
| No Instant Action or Full Simulation check exists in the Load dialog functions. | `0x10152fb0` | all | high | function ✓ |

### Save Park dialog

Functions: `0x10153ab0`, `0x10153134`, `0x10153c74`. Lets the player name and save the current park. Shows the list of existing saves and a text field pre-filled with a default name.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x23bce1` | 201: Save Park | Title label. | id ✓ text ✓ |
| `0x23bce2` | 206: New Save | Park name text field. Pre-filled with New Save. Max length is set to 15 (low confidence). Selecting a list row copies that row's name into it. | id ✓ text ✓ |
| `0x23bce0` |  | List of existing saves. Populated by a helper not in this chunk. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Opening the Save dialog has no single-instance guard. It sets the main screen busy flag and the save-open flag. | `0x10153ab0` | all | medium | function ✓ |
| The name field is pre-filled with New Save. | `0x10153ab0` | all | high | function ✓ |
| Activating a save row (0x400) copies that row's name into the name field. | `0x10153134` | all | high | function ✓ |
| Confirm with a name not in the list adds the name to the list and closes the dialog. The actual save is not in this function. | `0x10153134` | all | medium | function ✓ |
| Confirm with a name already in the list shows a two-button confirmation. The message is built from UITEXT 0xcd, which is blank in UITEXT.tsv, so the exact prompt text is unknown. | `0x10153134` | all | medium | function ✓ |
| Cancel (value -2) closes the dialog and returns 0. | `0x10153134` | all | high | function ✓ |
| Closing restores the main screen and clears the save-open flag. | `0x10153134` | all | medium | function ✓ |
| Messages 0x802 and 0x804 are forwarded as confirm (-1) and cancel (-2) (likely keyboard or controller mapping). | `0x10153134` | all | low | function ✓ |
| No Instant Action or Full Simulation check exists in the Save dialog functions. | `0x10153134` | all | high | function ✓ |

### Loans dialog (available and outstanding)

Functions: `0x101547e0`, `0x10153ca8`, `0x10153ed4`. Lists the eight loan slots for the park, either as offers you can take or as loans you owe. Lets you take a loan or pay one off early.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `tabs (not in chunk)` | 170: Available Loans | Mode 0 shows loan offers (text 0xaa). The switch that sets the mode is not in this chunk. | text ✗ |
| `tabs (not in chunk)` | 171: Outstanding Loans | Mode 1 shows loans you have taken (text 0xab). | text ✗ |
| `list rows` |  | One row per loan slot. Column labels available: 172 Lender Name, 173 Loan Term (months), 174 Amount, 175 Interest Rate, 176 Monthly Repayment, 177 Total Payable. Outstanding also shows 178 Original Amount, 179 Months Remaining, 180 Remaining Balance. Column-to-field mapping is not confirmed. |  |
| `take loan handler` |  | Handler 0x10153ed4 calls the take-loan routine for the slot index, then refreshes the list. Button assignment is not in this chunk. |  |
| `repay handler` |  | Handler 0x10153ca8 calls the pay-off routine for the slot index, then refreshes the list. Button assignment is not in this chunk. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Mode 0 lists offered loans that have not been taken. Mode 1 lists loans that have been taken. | `0x101547e0` | all | medium | function ✓ |
| Taking a loan is refused if that slot is already taken. | `0x100cc904 (called from 0x10153ed4)` | all | high | function ✓ |
| Taking a loan credits the principal to cash and to a park ledger, then marks the slot taken. The credit is unconditional. | `0x100cc904 (called from 0x10153ed4)` | all | high | function ✓ |
| Paying off early requires the loan to be taken. The cost is monthly repayment times months remaining (term minus months paid). The player must have at least that much cash. | `0x100ccc78 (called from 0x10153ca8)` | all | high | function ✓ |
| When the park flag at offset 0x114 is nonzero, paying off deducts the cost from cash, adds it to the costs ledger and adjusts an accumulator. When the flag is zero, the loan is cleared with no money movement. | `0x100ccc78 (called from 0x10153ca8)` | all | medium | function ✓ |
| Paying off clears the taken flag and the months-paid field. | `0x100ccc78 (called from 0x10153ca8)` | all | high | function ✓ |
| The large-deposit and zero-month checks are debug assertions, not player messages. The same assertion helper is used for the Invalid GameType message in SetGameType. | `0x100cc904` | all | medium | function ✓ |
| No Instant Action or Full Simulation check exists in the loans functions. | `0x101547e0` | all | high | function ✓ |

### Loan take-out / repay confirmation messages

Functions: `0x10153d00`. Computes the amount owed on an outstanding loan and shows the matching message. It is called from the loan list when an outstanding row is activated.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `message box` | 181: There is $ | Shown when the amount owed is less than the current cash. Shown with message type 2, which is probably a choice dialog. | text ✗ |
| `message box` | 182: You do not have enough money to pay the outstanding balance  | Shown when the cash is not enough to pay the amount owed. Shown with message type 1, probably information only. | text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The amount owed is computed as the record's monthly repayment times the number of remaining months (a difference of two record fields). It is compared to the current cash from the bank object. If the amount owed is less than cash, the player gets the 'There is $ ...' prompt with a choice. Otherwise the 'not enough money to pay the outstanding balance' message shows. | `0x10153d00` | not_instant_action | medium | function ✓, game-type test ✗ |
| The loan-confirmation flow is not gated by game type in this function. The bank dialog itself is what is blocked in Instant Action. | `0x10153d00` | all | low | function ✓ |

### Bank / loans dialog (Available and Outstanding loans)

Functions: `0x10153f2c`, `0x10154aa0`, `0x10154184`, `0x10155234`. The bank screen where the player browses loans on offer and loans already taken out, sees a ledger summary, and takes out or repays loans. It is a modal-style dialog stored in the global handle _DAT_101edadc. Its message handler (0x10153f2c) routes list activations and buttons. FUN_10154aa0 builds it, FUN_10154184 sets the per-mode captions, and FUN_10155234 refreshes the list when a dirty flag is set.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x12c4bd` |  | Main panel of the dialog. Holds the loan list and the header row. Its list is populated by FUN_101547e0 (a callee outside this chunk). Refreshed by 0x10155234 when the dirty flag _DAT_101efd14 equals 1. | id ✓ |
| `0x12c4be` |  | Tab group that switches between the two loan views. The active page is set with FUN_10172e6c to 0x12c4c0 (Available) or 0x12c4bf (Outstanding), depending on the mode global. | id ✓ |
| `0x12c4c0` | 378: CLEANING: You don't have any janitors! | Tab page for loans on offer. Selected when the mode global is 0. | id ✓ text ✗ |
| `0x12c4bf` | 221: This will take you to the lobby and require logging in to Si | Tab page for loans already taken out. Selected when the mode global is non-zero. | id ✓ text ✗ |
| `0x12c4ca` | 170: Available Loans | Title label of the list. Set by FUN_10154aa0 and FUN_10154184. | id ✓ text ✓ |
| `0x12c4c1` |  | Ledger sub-panel on the right. Holds the four summary rows below. | id ✓ |
| `0x12c4c4 / 0x12c4c5` | 357: Cash in | Shows total income. The value comes from the record at offset 0x1fc90 of the park object. | text ✓ |
| `0x12c4c6 / 0x12c4c7` | 359: - Other costs | Other costs, computed as total costs (record at offset 0x1f5a0) minus the loan-payment sum. | text ✓ |
| `0x12c4c9 / 0x12c4c8` | 360: - Loan payments | Loan payments. The value is the sum over 8 ledger entries of a field, counted only where a flag is set. | text ✓ |
| `0x12c4c2 / 0x12c4c3` | 361: Balance | Income minus total costs (cash in value minus the 0x1f5a0 total). | text ✓ |
| `0x10` | 172: Lender Name | Header button that sorts the list by lender name. Set by FUN_10154aa0. | id ✓ text ✓ |
| `0x11` |  | Header button that sorts the list by amount. Caption and help text are chosen by mode in FUN_10154184. | id ✓ |
| `0x12` |  | Header button that sorts the list by term or by remaining term. Caption is chosen by mode in FUN_10154184. | id ✓ |
| `0x13` | 175: Interest Rate | Header sort button by interest rate. Same caption in both modes. | id ✓ text ✓ |
| `0x14` | 176: Monthly Repayment | Header sort button by monthly repayment. Same caption in both modes. | id ✓ text ✓ |
| `0x15` |  | Header sort button. Caption is chosen by mode in FUN_10154184. | id ✓ |
| `0x12c4ba (command)` |  | Button or command that leaves this dialog and opens a different screen (see FUN_1014ec48 in the 'Command routing' rule). It is passed to FUN_1014ec48. |  |
| `0x12c4bc (command)` |  | Command that opens another screen via FUN_10168d8c. |  |
| `list row (message 0x400)` |  | Activating a row of the loan list. Positive row index means an available loan (take-out path). Negative row index means an outstanding loan (repay path). See the loan-confirmation screen. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The bank/loan dialog is never built in Instant Action. If the game type is 2, the open routine does nothing (it does not create the dialog or set the handle). | `0x10154aa0` | instant_action | high | function ✓, game-type test ✓ |
| Opening the bank dialog first ensures the game type is initialised (the lazy init 0x1012bb64 runs if the init flag is not set). It then sets the screen code to 8 through 0x10155280, and it plays sound 0xc3 at the end. | `0x10154aa0` | not_instant_action | medium | function ✓, game-type test ✓ |
| The mode global (toc offset -0x1067) selects the loan view. Mode 0 shows Available Loans with the columns Lender Name, Amount, Loan Term, Interest Rate, Monthly Repayment and Total Payable. Mode 1 shows Outstanding Loans with Lender Name, Original Amount, Months Remaining, Interest Rate, Monthly Repayment and Remaining Balance. | `0x10154184` | not_instant_action | high | function ✓, game-type test ✗ |
| The header buttons are sort controls. Each has its own help text: Lender Name 'sort by lender name', Amount 'sort by amount', Loan Term 'sort by loan term', Interest Rate 'sort by interest rate', Monthly Repayment 'sort by monthly repayment', Total Payable 'sort by total amount payable'. The mode-1 variants are 'sort by original loan amount', 'sort by term remaining', and 'sort by remaining balance'. | `0x10154184` | not_instant_action | high | function ✓, game-type test ✗ |
| The Available/Outstanding tab page is chosen from the mode. Mode 0 selects the Available tab (0x12c4c0). Any other mode selects the Outstanding tab (0x12c4bf). Its help texts are 0xdc (Available) and 0xdd (Outstanding). | `0x10154aa0` | not_instant_action | high | function ✓, game-type test ✓ |
| The ledger rows are computed from the park object's records. Cash in is the value at offset 0x1fc90. Total costs are at offset 0x1f5a0. Other costs equal total costs minus the loan-payment sum. Balance equals cash in minus total costs. The loan-payment sum loops over 8 entries and adds a field only where a flag is set. | `0x10154aa0` | not_instant_action | medium | function ✓, game-type test ✓ |
| Dialog message 0x14 clears the dialog handle and plays sound 0xc3 (likely the panel close sound). | `0x10153f2c` | all | medium | function ✓ |
| Message 0x05 and command values -3 to -1 send message 4 to the bank dialog via FUN_10170f98. This is the close request for the dialog. | `0x10155200 (called from 0x10153f2c)` | all | medium | function ✓ |
| Command routing: command 0x12c4ba closes the bank dialog and opens another screen through FUN_1014ec48 (screen code 10). Command 0x12c4bc opens the ride stat bar through FUN_10168d8c (screen code 9). Any other command below 0x12c4bc goes to FUN_10150c54 (screen code 7). | `0x10153f2c` | all | medium | function ✓ |
| Selecting a row (message 0x400) gives the target index. A negative encoded index (the outstanding list) is decoded and passed to the repay-check routine 0x10153d00. A positive index (available list) takes the loan at that index and shows the take-out confirmation with UITEXT 183 'Take out a loan for $' and the record's amount. The text uses a VARM format. | `0x10153f2c` | not_instant_action | medium | function ✓, game-type test ✗ |
| A message handler for the bank dialog does not check the game type itself. The only game-type gate is at dialog open (0x10154aa0), so a dialog could exist without the Instant Action guard in other paths. The notice is for coverage. | `0x10153f2c` | all | low | function ✓ |
| Refresh: if the dialog is open and the dirty flag _DAT_101efd14 equals 1, the list is repopulated via FUN_101547e0 and the main panel is touched first. | `0x10155234` | all | medium | function ✓ |

### Cash counter / wealth meter control

Functions: `0x101555b4`. Per-tick update of a money display. It shows the absolute cash value, colours the control by a 6-step wealth level, and shows a negative marker when cash is below zero. It also reads a second global value and shows or hides a sub-control (0x30) with a time-like value when a state is in {3, 4, 5} or the extra check passes.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `param_1 (the cash control)` |  | Shows the cash as a number and sets its colour from a 6-entry palette (level = ceil(cash/1000) clamped to 0 to 5). |  |
| `0x30` |  | Sub-control with a value that is shown only when the 0x1007bccc value is valid. It is hidden when the value is -9999 or the state does not apply. | id ✓ |
| `0x31 / 0x32` |  | 0x31 is positioned by the cash digit width. 0x32 is enabled when cash is negative. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Wealth level: the cash value is divided into 1000 steps, rounded up, and clamped to 0 to 5. A colour from a table of 4-byte entries is applied when the level changes. | `0x101555b4` | all | medium | function ✓ |
| When cash is negative, the 0x32 child is enabled. When cash is zero or positive it is disabled. | `0x101555b4` | all | medium | function ✓ |
| The 0x30 sub-control is shown only when the second value (from 0x1007bccc) is not -999. It is hidden when that value is -999 or the extra state check fails. Its meaning is not confirmed. | `0x101555b4` | all | low | function ✓ |

### Shared dialog A (global _DAT_101edb30): summary line, tree build, and info-panel helpers

Functions: `0x10155a04`, `0x10156bac`, `0x10157434`, `0x101574d4`. A second dialog whose handle is kept in _DAT_101edb30. It holds an info or item panel with a hierarchy of child controls (ids 0x1d to 0x25, 0x2d to 0x37), a formatted summary line, and helper functions that open and close the sub-panel. Its identity is not established within this chunk.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x20 (child of dialog)` | 448:  | Gets a formatted text from three numbers (two counters and a third value). The text is set via FUN_10181230. | text ✗ |
| `0x2f -> 0x31` |  | Enabled/flagged when the value at offset 0x1fc90 is less than the value at 0x1f5a0 (income below costs). |  |
| `0x33 (child)` |  | Container created first when the dialog tree is built. |  |
| `0x34 / 0x36` |  | Shown only outside Instant Action (see the rule below). The 0x36 is created with a blank text and the 0x34 is set to state 3. |  |
| `0x21 / 0x22 / 0x23` |  | Nested item panel. Slot 0x23 is stored at global _DAT_101efd... as the active sub-item. Its 0x24 child gets a state 7 and a value 0. |  |
| `0x24 / 0x25 / 0x27` |  | 0x25 holds a toggle 0x27. 0x101574d4 turns 0x27 off (state 0) when the sub-panel closes. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Instant Action hides the 0x34 control of the tree (FUN_1017fa64 with 0). Outside Instant Action the 0x36 child is created and 0x34 is set to state 3. | `0x10156bac` | not_instant_action | medium | function ✓, game-type test ✓ |
| Opening the item info sub-panel (0x10157434): if the slot-2 target of control 0x21 is already set, the call returns 0 and does nothing (it blocks). Otherwise it sends message 0xb to 0x21 and message 10 with the given value (the request is accepted). | `0x10157434` | all | medium | function ✓ |
| Closing the sub-panel (0x101574d4): if the sub-panel state is 3 or 4 it clears slot 2. Otherwise it sends message 0xb to collapse it. If the flag argument is non-zero, it also turns off the toggle 0x27 under 0x25. | `0x101574d4` | all | medium | function ✓ |
| Shared dialog A is built only when its handle is empty. The 'Upgrade' or 'fully upgraded' help strings in UITEXT (28 to 30) are not used here, so the identity is still unknown. | `0x10156bac` | all | low | function ✓ |

### Count panel (two numeric labels, Instant Action gated)

Functions: `0x10155b7c`. A panel with two count controls (0x34/0x36 and 0x35/0x37). Each count is formatted with a number format and shown only when non-zero. The panel is refreshed on message 0x1e (30). The data source is a count helper (0x10128b60 and 0x10128a2c) that is not resolved here.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x34 / 0x36` |  | Container and value. Shown and updated only outside Instant Action. Hidden when the count is 0. |  |
| `0x35 / 0x37` |  | Container and value. Always updated. Hidden when the count is 0. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The 0x34/0x36 group is not touched in Instant Action. Outside Instant Action it is shown when its count is non-zero and hidden otherwise. | `0x10155b7c` | not_instant_action | high | function ✓, game-type test ✓ |
| The 0x35/0x37 group is always updated (no game-type check). It is shown when its count is non-zero and hidden otherwise. | `0x10155b7c` | all | high | function ✓ |
| Message 0x15 clears the cached counts to -1 so the next refresh recomputes them. | `0x10155b7c` | all | high | function ✓ |
| Message 0x1e (30) runs the refresh. It initialises the game type if needed, then updates the 0x34/0x36 and 0x35/0x37 counts only when they changed from the cached values. | `0x10155b7c` | all | medium | function ✓ |

### Generic show and hide animation helper

Functions: `0x10155f50`. Handles a list of child controls that should animate in or out. It is used by a parent screen that has a linked list of children (head at global _DAT_101ed980).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `child list (head _DAT_101ed980)` |  | Linked list of child controls. Each one is shown in turn, with a 100 ms stagger. |  |
| `flagged child` |  | A child with flag bit 0 set is hidden with an animation (500 ms) or immediately (if the parameter is non-zero). |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Message 0x100003 shows the child list with a stagger. The first child is shown after 100 ms, each next after another 100 ms. | `0x10155f50` | all | medium | function ✓ |
| Message 0x14 clears the child list head to 0. | `0x10155f50` | all | high | function ✓ |
| For a child with flag bit 0 set, the child is hidden and given a 500 ms animation. If the parameter is non-zero, the animation is immediate. | `0x10155f50` | all | medium | function ✓ |
| Message 0x100001 lays out a child: it saves two values from the parent, centres the child horizontally using its own size and the parent width, and starts a 500 ms animation (message 6). It then applies the two saved values back. | `0x10155f50` | all | low | function ✓ |

### Toolbar drawer with research and map buttons

Functions: `0x1015644c`. A sliding or expanding drawer of buttons. The two buttons have help text 'Click for research (R)' (UIHELPTEXT 472) and 'Click to view the map (Space)' (UIHELPTEXT 473). It uses a state byte at offset 0x134 (0 idle, 2 expanded, 3 or 4 collapsing) and a step counter at offset 0x130 (set to 10). The mapping of the drawer to a named screen is not confirmed in this chunk.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `child slot 1 (FUN_101804b8 index 1)` |  | The first sub-control. It gets the research help text (0x1001d8) when the drawer opens. |  |
| `child slot 2 (FUN_101804b8 index 2)` |  | Target control of the request. It gets the map help text (0x1001d9) when the drawer opens. |  |
| `0x24 child` |  | Set to value 0 on open and 1 when the target opens. It is cleared on close. |  |
| `0x25 toggle / 0x27 sub toggle (via FUN_101574d4)` |  | Closing the info panel from message 0x100 with param 0x24 calls FUN_101574d4(1). |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Message 0x0a (10): when the drawer is idle, it opens the sub-panel for the given target, applies the map help text, and moves to state 2 (expanded). When not idle and a target is given, it stores the target in slot 2. | `0x1015644c` | all | medium | function ✓ |
| Message 0x0b (11): if slot 1 exists and the 0x80080 hit test passes, it starts the drawer. The step is 10. The state becomes 4 when the parameter is -1, otherwise 3. The research help text 0x1d8 goes on slot 1, and a sound (0x1c) plays. | `0x1015644c` | all | low | function ✓ |
| Message 0x10 with parameter 0x80080 drives the animation. Each tick decrements the step and moves the child. When the step reaches 0, the drawer finishes: state 3 hides slot 1 and plays sound 0xc2, state 4 hides it through a different path, and state 0 resets. Any pending target is then sent message 10. | `0x1015644c` | all | low | function ✓ |
| Message 0x12 (18) repositions slot 1 so it matches the parent rectangle (its horizontal position is the parameter minus a stored offset). | `0x1015644c` | all | low | function ✓ |

### Toggle label helper (Yes/No and On/Off)

Functions: `0x10157c9c`. Builds a state word (No, Yes, Off, On) and sets it on a value control. It can also set a child toggle to match the state. Its callers are outside this chunk.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `value control (param_1 child, then 0x1d4c9)` | 332: No | The value control shows the chosen word. Words come from UITEXT 332 to 335 (leading space in the text). | text ✗ |
| `toggle child (param_6, only when param_5 is non-zero)` |  | Set to state 1 when param_3 is 0 and to state 0 otherwise. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The state word depends on two flags. With param_4 zero it is No (param_3 zero) or Yes. With param_4 non-zero it is Off (param_3 zero) or On. | `0x10157c9c` | all | high | function ✓ |
| When param_5 is set, a child toggle is updated to the opposite of the flag: state 1 when param_3 is zero, otherwise state 0. | `0x10157c9c` | all | medium | function ✓ |

### Game Options screen

Functions: `0x10158bc0`, `0x10157c9c`, `0x10157db0`, `0x10158508`. Builds and refreshes the 'Game Options' dialog (title UITEXT 315). Shows rendering mode, video card, rotation, scroll mode, resolution, graphics quality, five on/off options, four unlabeled toggles and five 0-100 sliders. Values are read from the global options block at _DAT_101ec874 (a local value in the code). Change handlers are reached through jump tables, so the per-control behaviour is only partly recovered.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1d4d4` | 315: Game Options | Dialog title, set with FUN_1013876c. | id ✓ text ✓ |
| `0x1d4d2 / 0x1d4d3` |  | Anchor labels whose text is cleared. Their right edge sets x=(width+2) for the checkbox column (FUN_10180af4). |  |
| `0x1d4c1` | 325: Advisor: | Checkbox for option byte at options+0x34. Its state is set by FUN_101722a0 (inverted relative to the option value, see rules). Row label is 0x1d4cb. | id ✓ text ✗ |
| `0x1d4c2` | 326: Tutorial: | Checkbox for option byte at options+0x35. Row 0x1d4cc. | id ✓ text ✗ |
| `0x1d4c3` | 327: Popup help: | Checkbox for option byte at options+0x36. Row 0x1d4cd. | id ✓ text ✗ |
| `0x1d4c4` | 328: Confirmations: | Checkbox for option byte at options+0x37. Row 0x1d4ce. | id ✓ text ✗ |
| `0x1d4c5` | 331: RMB cancel: | Checkbox for option byte at options+0x39. Row 0x1d4cf. | id ✓ text ✗ |
| `0x1d4c6, 0x1d4c7` |  | Also positioned in the checkbox column but never given text or state in this chunk. Purpose unknown. |  |
| `0x1d4c8` | 316: 3D card rendering | Rendering-mode row. Shows 316 when options+0 is nonzero and 317 when it is 0. Child label control 0x1d4c9 holds the text. | id ✓ text ✓ |
| `0x1d4ca` | 348: Videocard: | Video-card row. Label 348 plus 349 (Primary) or 350 (Secondary). Shows Primary when options+0x0c is 0, or when the device count at a global+0x784 is below 2. Otherwise Secondary. | id ✓ text ✓ |
| `0x1d4d0` | 351: Rotation: | Rotation row. Shows 352 (Smooth) when options byte 0x3a is 0, else 353 (90 degs). | id ✓ text ✓ |
| `0x1d4d1` | 354: Scroll: | Scroll-mode row. Shows 355 (Pushscroll) when options byte 0x38 is 0, else 356 (Ctrl-click). | id ✓ text ✓ |
| `0x1d4d5` | 319: Screen resolution: | Resolution combo row. Label 319 plus 341/342/343/344 for index 0/1/2/3 of options[1]. Any other index shows nothing. Change handler is reached through the jump table at _DAT_101efd80 (FUN_10157db0). | id ✓ text ✓ |
| `0x1d4d6` | 318: Graphics quality: | Quality combo row. Label 318 plus 336/337/338 for options[2] = 0/1/2. Other values show nothing. | id ✓ text ✓ |
| `0x1d4d7, 0x1d4d9, 0x1d4db, 0x1d4dd` |  | Four on/off toggles. Their state comes from option bytes at options+0x10, +0x18, +0x20 and +0x28. Labels are not set in this chunk. Each change calls FUN_10158508 (jump table at _DAT_101efd74). |  |
| `0x1d4d8, 0x1d4da, 0x1d4dc, 0x1d4de` |  | Sliders with range 0..100 (FUN_1017b9dc(ctrl,0,100)). Values come from options ints at index 5, 7, 9 and 11. |  |
| `0x1d4e0` |  | Slider with range 0..100 and value options[12]. Changes apply immediately through FUN_10157db0(0x1d4e0, value). | id ✓ |
| `0x1d4c9 (child of each row)` |  | Label/value text object inside each row. Gets a text built as label followed by value. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Opening the Game Options screen is idempotent. If the dialog handle at _DAT_101efd88 already exists, the function returns without rebuilding anything. | `0x10158bc0` | all | high | function ✓ |
| On build, the function sets the global render option to 0x21 on the map-who object, saves the previous value, and flushes the texture cache. The previous value is presumably restored on close, but that is not in this chunk. | `0x10158bc0` | all | medium | function ✓ |
| Each on/off option row reads its byte from the options block and shows ' Off' (0x14e) when 0 and ' On' (0x14f) when nonzero, after the label (Advisor, Tutorial, Popup help, Confirmations, RMB cancel). | `0x10157c9c` | all | high | function ✓ |
| The checkbox widget is set to the negation of the option value: option 0 sets the widget state to 1, and nonzero sets it to 0. The same inversion is used for the four unlabeled toggles. | `0x10157c9c` | all | low | function ✓ |
| Resolution index in options[1] maps to 512x384 (0), 640x480 (1), 800x600 (2), 1024x768 (3). Any other value shows an empty string. | `0x10158bc0` | all | high | function ✓ |
| Graphics quality in options[2] maps to Low (0), Medium (1), High (2). Any other value shows an empty string. | `0x10158bc0` | all | high | function ✓ |
| Rendering row shows '3D card rendering' when options[0] is nonzero and 'Software rendering' when it is zero. | `0x10158bc0` | all | high | function ✓ |
| Video card row shows Primary when options[3] is zero. If not zero, it shows Primary when a device count at global+0x784 is below 2, and Secondary otherwise. | `0x10158bc0` | all | medium | function ✓ |
| Sliders are initialised from options ints at index 5, 7, 9, 11 and 12 with range 0..100. Each slider change is applied immediately through the jump table entry for its control id. | `0x10158bc0` | all | high | function ✓ |
| No check against the global game type (_DAT_101ec8f8) appears in this function. The 'options == 2' style tests here are on resolution or quality indices, or on the device count, not on game type. | `0x10158bc0` | all | medium | function ✓ |
| Entering the options dialog sets a byte flag from options+0x3c and may call FUN_10110518 and FUN_1017d1c4. The effect is not recovered. | `0x10158bc0` | all | low | function ✓ |
| Change handlers for the option controls are not recovered. They are dispatched by control id through two jump tables, one for ids 0x1d4c1 to 0x1d4dd (FUN_10158508, _DAT_101efd74) and one for ids 0x1d4d5 to 0x1d4e0 (FUN_10157db0, _DAT_101efd80). | `0x10158508, 0x10157db0` | unknown | high | function ✓ |

### Park Information panel

Functions: `0x1015af1c`, `0x1015a368`, `0x1015a11c`, `0x1015bbd8`. Builds and updates the 'Park Information' window (title UITEXT 150). It shows park counters, happiness and rating gauges, a graph with five selectable series, a 1 Year / 3 Years / 12 Years time-range selector, and three 'Top 3 Thoughts' slots. The window refreshes once per second through timer 0x80083 and closes through command codes. Open and close go through the global window handle at _DAT_101edb68.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x4a68bb` |  | Graph control. Series data is pushed to it by FUN_10193df4 from FUN_1015bbd8. | id ✓ |
| `0x4a68bc` | 152: Arrival rate | Series toggle for bit 0 of the series-mask global at _DAT_101efd98. | id ✓ text ✓ |
| `0x4a68bd` | 153: Average happiness | Series toggle for bit 1. | id ✓ text ✓ |
| `0x4a68be` | 154: Average time in park | Series toggle for bit 2. | id ✓ text ✓ |
| `0x4a68bf` | 151: People in park | Series toggle for bit 3. | id ✓ text ✓ |
| `0x4a68c0` | 155: Park rating | Series toggle for bit 4. | id ✓ text ✓ |
| `0x4a68c1 (group)` |  | Container for the time-range selector. |  |
| `0x4a68c3` | 362: 1 Year | Time-range option for index 0. Selected through FUN_10172e6c. | id ✓ text ✓ |
| `0x4a68c4` | 363: 3 Years | Time-range option for index 1. | id ✓ text ✓ |
| `0x4a68c2` | 364: 12 Years | Time-range option for index 2. | id ✓ text ✓ |
| `0x4a68c5` | 150: Park Information | Window title. Set on message code 0x15. | id ✓ text ✓ |
| `0x4a68c9 (group)` |  | Container for the counters. |  |
| `0x4a68cb` | 151: People in park | Label for the count in 0x4a68cc. | id ✓ text ✓ |
| `0x4a68cc` |  | Value: people-in-park count from FUN_100c3684(), refreshed when it changes. | id ✓ |
| `0x4a68cd` | 158: Total park visitors | Label for the value in 0x4a68ca. | id ✓ text ✓ |
| `0x4a68ca` |  | Value: total visitor count from FUN_100c3b7c() (reads a field at +0x21c08 of the park object), refreshed when it changes. | id ✓ |
| `0x4a68ce` | 156: Top 3 Thoughts | Heading for the thoughts list. | id ✓ text ✓ |
| `0x4a68d1 (group)` |  | Container for three thought rows. |  |
| `0x4a68d2, 0x4a68d3, 0x4a68d4` |  | Thought slots 1 to 3. Each is set to a thought name by index, or hidden when the index is 0 or above 22. |  |
| `0x4a68cf` | 157: Happiness Levels | Happiness gauge, a 0x124 object with the image hap.wct. Fill width is (100 - stat) * 1024 / 100 from FUN_100c3db4(...,0x42). | id ✓ text ✓ |
| `0x4a68d0` |  | Sad/undecided gauge, image sad_undec.wct. Fill width is stat * 1024 / 100 from FUN_100c3ec8(...,0x21). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The Park Information window is built only if no instance exists yet (the global handle at _DAT_101edb68 is zero). The build creates the window, arms a one-second timer (id 0x80083), and fills all the rows described above. | `0x1015af1c` | all | medium | function ✓ |
| Once per second (timer 0x80083 while the window exists), the panel updates the people-in-park count and total visitor count only when they change. It recomputes the two gauge widths and refreshes the Top 3 Thoughts. | `0x1015a368` | all | medium | function ✓ |
| Series mask bits 0 to 4 map to the five toggles in order: arrival rate, average happiness, average time in park, people in park, park rating. Each enabled bit adds one series to the graph. | `0x1015af1c, 0x1015bbd8` | all | medium | function ✓ |
| Time-range selector index 0, 1 or 2 maps to 1 Year, 3 Years or 12 Years. The index is read from a global and passed to the sampling helper, which chooses how many samples to use. The count for index 1 is 0x24 (36). | `0x1015af1c, 0x1015bbd8` | all | medium | function ✓ |
| Top 3 Thoughts: each of three slots reads a 1-based thought index, and shows the thought name at index minus 1 when the index is between 1 and 22. Otherwise the slot is hidden. | `0x1015a11c` | all | medium | function ✓ |
| Message code 0x100 (command) with id 0x4a68c6 opens another screen through FUN_10148654(0). Id 0x4a68c8 calls FUN_1014abe4(-1,0). Id 0x4a68c7 calls FUN_1014ba4c(-1,0). Id -1 closes the window by sending code 4 to it. | `0x1015a368` | all | medium | function ✓ |
| Message code 0x14 and above clears the window handle and posts code 199 through FUN_10139ae0. Message code 5 closes the window through FUN_1015bba4. | `0x1015a368` | all | medium | function ✓ |
| Message code 0x800 is forwarded to a sub-object before the default handler runs. | `0x1015a368` | all | low | function ✓ |
| Screen-mode code 2 is set to 3 (Park Information) on open. Other screens set 4, 5 or 6 before opening, and this is only the global calling convention. | `0x1015af1c` | all | low | function ✓ |
| No game-type check appears in the Park Information code. | `0x1015af1c, 0x1015a368, 0x1015a11c, 0x1015bbd8` | all | medium | function ✓ |

### Quit Game confirmation

Functions: `0x1015c1e0`. Window handler for the quit prompt. It sets colour on some messages and shows the quit confirmation message box on message code 0x10006.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `(dialog)` | 9: QUIT GAME  Are you sure you want to quit the game ? | Shown in a message box through FUN_10190270, with white text, button style 2, and no further parameters. | text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Message code 0x10006 shows the quit confirmation (text 9) through the message-box helper, then returns 0, so the message is handled. | `0x1015c1e0` | all | medium | function ✓ |
| Message code 0x10001 sets the control colour to red. Code 0x10002 uses colour values from three globals. Any other message goes to the default handler. | `0x1015c1e0` | all | low | function ✓ |
| The button style passed to the message box is 2, which the helper maps to the 'b_exit' button. Style 1 maps to 'b_okay', and style 0 shows no button. | `0x1015c1e0 -> 0x10190270` | all | low | function ✓ |

### Player slot list

Functions: `0x1015c31c`. Refreshes four slots of a player or profile list (rows 0x7a14, 0x7a19, 0x7a1a, 0x7a1b). Each slot shows the name when filled, or 'Create New Player' when empty.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x7a14, 0x7a19, 0x7a1a, 0x7a1b` |  | Slot rows 0 to 3. Each contains a name label (0x7a15) and the controls 0x7a16 and 0x7a17. |  |
| `0x7a15` |  | Name label. Shows the stored name for a filled slot, or UITEXT 421 for an empty slot. | id ✓ |
| `0x7a16` | 421: Create New Player | Shown for filled slots and hidden for empty ones (FUN_1017fa64). | id ✓ text ✓ |
| `0x7a17` |  | Show or enable control. Hidden for empty slots. For filled slots, its visibility or state comes from the 0x14 field of the slot entry. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| An empty slot (name length zero) shows 'Create New Player' (text 421) and hides the controls 0x7a16 and 0x7a17. | `0x1015c31c` | all | high | function ✓ |
| A filled slot shows its name from the entry at offset +8, shows 0x7a16, and sets 0x7a17 from the slot entry flag at +0x14 (FUN_101371bc). | `0x1015c31c` | all | medium | function ✓ |
| The slot list is a 4-entry array of 0x18-byte records. It is created lazily on first use, and the function does nothing if the window handle at _DAT_101edba0 is zero. | `0x1015c31c` | all | medium | function ✓ |
| No game-type check appears in this function. | `0x1015c31c` | all | medium | function ✓ |

### Front-end player/park slot select screen

Functions: `0x1015c828`. Builds the front-end screen with four save slots (player/park entries) and a Quit Game button. Its refresh (0x1015c31c, in another chunk) fills the slot labels. It also posts menu commands that depend on how many slots are filled.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x7a14` |  | Slot button 0. Its slot index (stored in slot field 0) is 0. Text is cleared here and filled by 0x1015c31c: 'Create New Player' (UITEXT 421) when empty, otherwise the saved name. | id ✓ |
| `0x7a19` |  | Slot button 1, slot index 1. Same pattern as 0x7a14. | id ✓ |
| `0x7a1a` |  | Slot button 2, slot index 2. Same pattern as 0x7a14. | id ✓ |
| `0x7a1b` |  | Slot button 3, slot index 3. Same pattern as 0x7a14. | id ✓ |
| `0x7a15` |  | Child sub-panel of each slot button; holds the label that 0x1015c31c sets. | id ✓ |
| `0x7a16` |  | Child of each slot button. Enabled only when that slot has a saved entry (per 0x1015c31c). Likely delete or overwrite, meaning not confirmed. | id ✗ |
| `0x7a17` |  | Child of each slot button. Enabled state is taken from a flag in the slot record at offset 0x14 (per 0x1015c31c). Meaning of the flag not confirmed. | id ✗ |
| `0x7a1c` | 8: Quit Game | Quit button. Its slot field is also set to 3, the same value as slot button 3 (possibly a copy-paste in the decompile, unverified). Its activation command is the global value stored at +0x110. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Slot buttons hold their index 0..3 in their slot field, so each activation tells the handler which save slot was picked. | `0x1015c828` | all | high | function ✓ |
| Menu commands 0x186, 0x187 and 0x18e are posted based on how many slots are non-empty. If none are filled: enable 0x186, disable 0x187 and set a 'no saves' global to 1. If at least one is filled: enable 0x18e and set the global to 0. The count comes from FUN_10136fe0 over four 0x18-byte slot records. | `0x1015c828` | all | medium | function ✓ |
| The slot screen does not read the game type. Game type has no effect on its build or on the slot-count command logic. | `0x1015c828` | all | high | function ✓ |
| The screen is opened modally: a modal push (FUN_10115c90) is done at the start, and the layout is refreshed (FUN_1009598c) at the end. | `0x1015c828` | all | high | function ✓ |
| Slot activation on an empty slot opens the create-player dialog. The caller 0x1015bf04 (another chunk) checks the slot's name length and calls 0x1015d220 for an empty slot. For a filled slot it applies the selection and calls 0x1015cd38(0), so the slot selection path is the same in all game types. | `0x1015c828` | all | medium | function ✓ |

### Create new player: name entry plus game-mode choice dialog

Functions: `0x1015d220`, `0x1015d074`, `0x1015cf00`. Dialog shown when an empty save slot is chosen. The player types a name and picks either Instant Action or Full Simulation, then the record is created in that slot.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x70a` | 243: Type your name | Name text field, empty by default and showing this prompt. Max length about 16 characters (FUN_10173d70 third argument 0x10, medium confidence). Focus is set on it when built. | id ✓ text ✓ |
| `0x709` | 239: Please enter your name | Prompt label above the name field. | id ✓ text ✓ |
| `0x70c` |  | Container of the two mode buttons below. The choice is read from it on OK (FUN_10172e64 selection). | id ✓ |
| `0x70d` | 241: Instant Action | Mode choice: Instant Action (easy mode). Selecting it enables control 0x70b (see rules). | id ✓ text ✓ |
| `0x70e` | 242: Full Simulation | Mode choice: Full Simulation. Selecting it disables control 0x70b. | id ✓ text ✓ |
| `0x70b` |  | Not created in this function. Enabled only while 0x70d is selected, per the 0x101 notify in 0x1015d074. Likely the OK or Start button, identity not confirmed. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The dialog is only shown when a certain global flag is set. If it is clear, the dialog is dismissed instead of built. | `0x1015d220` | all | medium | function ✓ |
| The dialog stores the slot index it was opened for in its slot field 0 (FUN_10180490(dialog,0,param)). On OK this index is read back to decide which slot the record is written to. | `0x1015d220` | all | high | function ✓ |
| The game type is not checked here. The Instant Action vs Full Simulation choice is made only by the two buttons, which are not game-type dependent. | `0x1015d220` | all | high | function ✓ |
| OK with a blank or whitespace-only name is rejected. The handler's default path runs, the dialog stays open and nothing is saved. Trailing spaces are trimmed before the check. | `0x1015cf00` | all | high | function ✓ |
| Accepted name is written into the record for the chosen slot. The fourth argument of FUN_1013741c is 1 when the selected mode button is 0x70d (Instant Action), otherwise 0. The meaning of this flag in the record is not confirmed. | `0x1015cf00` | all | medium | function ✓ |
| After a name is accepted: a global flag (offset -0x18cf) is set to 1, the dialog is closed (command 4), and FUN_1015cd38(0) runs. The downstream code in FUN_1015cd38 (other chunk) then uses the game type: if it is 2 (Instant Action) it posts command 0x18a, otherwise it creates an object and posts 0x189. | `0x1015d074` | all | medium | function ✓ |
| Enabling of OK (0x70b) depends on the mode choice: a selection notify (type 0x101, param 1) on 0x70d enables 0x70b, any other selected control disables it. This is the only game-type-like gating in the dialog, and it is driven by the choice, not the global game type. | `0x1015d074` | all | medium | function ✓ |
| Command ids on the dialog: -1 (OK) runs the save path above; -2 (cancel) and the 0x804 notify close the dialog without saving. The 0x802 notify is treated as OK (posts command -1). Other ids are passed to the default handler. | `0x1015d074` | all | medium | function ✓ |
| Destroying the dialog (notify 0x14) clears the singleton pointer at the global (_DAT_101efdc0), so the dialog can be reopened. | `0x1015d074` | all | high | function ✓ |

### Email Address Book dialog

Functions: `0x1015e030`, `0x1015d620`, `0x1015d98c`, `0x1015db38`, `0x1015dd70`. Address book for choosing or typing recipient email addresses for the postcard. It shows a list of saved addresses, a typed-entry field, and a delete action. Selected entries are copied into the postcard recipient field.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x27` | 212: Email Address Book | Dialog title label. | id ✓ text ✓ |
| `0x26` |  | Address list. Rows come from the stored address list. Uses the 'hilight' style. In online mode a trailing row 'Creator's name' (UITEXT 289) is added when a creator name exists. | id ✓ |
| `0x25` |  | Typed address edit field, max about 63 characters. Set to a constant (probably empty) on open. Selecting a row copies its address here, and deselecting clears it. | id ✓ |
| `0x24` |  | Command 0x24 on the address dialog: delete action for the selected row. It opens a two-button confirmation that names the row, using format id 0xd7 (blank in UITEXT.tsv). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The address book is a singleton. It is not built if it is already open, and its status line is set to 'Search in progress' (UITEXT 308) on open, regardless of game type. | `0x1015e030` | all | medium | function ✓ |
| Double-clicking or activating a list row (notify 0x400) adds that row's address to the postcard recipients via 0x1015e574. Selecting a row (notify 0x401) copies the address into the edit field 0x25. A selection with no row (index -1) clears that field. | `0x1015d620` | all | medium | function ✓ |
| Command -1 (OK or close) closes the address dialog. Command 0x24 (delete) builds a two-button confirmation for the selected row using format 0xd7. Its result handling is not in this chunk. | `0x1015d620` | all | medium | function ✓ |
| The list is rebuilt from the stored address list (each entry formatted as a wide string and appended). With a 'clear' argument it is emptied first. In online mode, if a creator name exists, a 'Creator's name' row is appended. | `0x1015d98c` | online | medium | function ✓, game-type test ✓ |
| Typed address (in field 0x25) is validated like the recipient check: at least one character before '@' and at least one character between '@' and '.'. Valid entries are appended to the address list, the list is rebuilt, the selection is cleared and the address is also added to the postcard recipients. Invalid entries show an error message (type 1) built from format id 0xd5, which is blank in UITEXT.tsv. | `0x1015db38` | all | high | function ✓ |
| Game type matters in this dialog only for the creator-name row in the list. Nothing else in the address book reads the game type. | `0x1015d98c` | online | medium | function ✓, game-type test ✓ |
| Destroy (notify 0x14) clears the address-book singleton and posts status text 0x134 'Search in progress'. | `0x1015d620` | all | low | function ✓ |

### Send Postcard dialog (with address-book entry point)

Functions: `0x1015ef24`, `0x1015ec14`, `0x1015e574`. Dialog for composing a postcard with a photo preview, a message title, a message text and a list of recipient email addresses. The address book is opened from here. OK validates the recipients and sends (or queues) the postcard, Cancel closes it.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x5d9e` | 208: Send Postcard | Dialog title label. | id ✓ text ✓ |
| `0x5d9f` |  | Postcard preview render area. Its image is drawn by 0x1015e2a0 (other chunk) using the photo object held in the global at -0x18c1. | id ✓ |
| `0x5da4` | 209: Message title | Title text field, max about 95 characters. Editing it (notify 0x801) sets a 'title edited' flag. | id ✓ text ✓ |
| `0x5da3` | 210: Message text | Message text field, max about 255 characters. Editing it (notify 0x801) sets a 'message edited' flag. | id ✓ text ✓ |
| `0x5da5` | 211: Email address(es) | Recipient field, max about 511 characters, showing the placeholder. The first address replaces the placeholder (see 0x1015e574). | id ✓ text ✓ |
| `0x5da0` |  | Command that opens the Email Address Book dialog (0x1015e030). | id ✓ |
| `0x5da1` |  | Command that clears the title, message and recipient fields. Probably a Clear button, label not in this chunk. Edit flags are not reset here. | id ✗ |
| `0x5da2` |  | Command that redraws the preview (0x1015e2a0). Probably a change or refresh picture button, label not in this chunk. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Only one postcard dialog can be open: if it already is, the builder returns at once. | `0x1015ef24` | all | high | function ✓ |
| The dialog needs a photo to exist. If the photo-list check (FUN_1012d600) fails, the builder shows a message via FUN_101906dc with id 0x4e and returns without building. | `0x1015ef24` | all | medium | function ✓ |
| Game type decides which dialog template is used. Online (type 1) uses template A. Otherwise the builder first closes any existing dialog through the global at -0x1ed8f4 with command 5, then uses template B. Offline builds also call FUN_101385e0 (decompilation failed) and at the end post status text 0x133 'No search results'. | `0x1015ef24` | all | medium | function ✓ |
| Game-type global: 0 = normal (Full Simulation), 1 = online, 2 = Instant Action. Derived from state bits 0x2000000 -> 2, 0x1000000 -> 1, otherwise 0. SetGameType rejects values above 2 with a debug message. | `0x1012bb64` | all | high | function ✓ |
| Default message text and title are substituted on OK when the user has not edited that field. Default message text is UITEXT 217 'Greetings from SimTheme Park!'. Default title is UITEXT 216 'Postcard from SimTheme Park'. The substitution is driven by the edit flags, which are set by notify 0x801 on 0x5da3 and 0x5da4. | `0x1015ec14` | all | high | function ✓ |
| OK validates the recipients with FUN_1015e48c (other chunk), which accepts only non-empty lists where every ';', ',' or whitespace separated token has an '@' with at least one character before it and a '.' after it with at least one character between. If invalid, error UITEXT 214 'Invalid email addresses in the send to field' is shown (type 1 message) and the dialog stays open. | `0x1015ec14` | all | high | function ✓ |
| Valid OK sends the postcard via FUN_1015e644 (other chunk: copies the photo to a numbered .jpg, creates a record with the recipients split on separators, and adds it to the outbox), then closes the dialog. The game type is not checked in this path. The outbox message 'Go online to send them' (UITEXT 473) is probably shown somewhere else. | `0x1015ec14` | all | medium | function ✓ |
| Cancel (-2) closes the dialog. The 0x5da2 command redraws the preview. The 0x5da1 command clears the three text fields without resetting the edit flags. | `0x1015ec14` | all | medium | function ✓ |
| Opening the address book (command 0x5da0) is not game-type gated in this function. | `0x1015ec14` | all | high | function ✓ |
| Each recipient is added with a duplicate check: if the address is already in the list (wcsstr) it is not added again. The first address replaces the placeholder 'Email address(es)' (UITEXT 211). Later addresses are appended with a separator string taken from the format table; the exact separator is not confirmed. | `0x1015e574` | all | high | function ✓ |
| Destroy (notify 0x14) clears the postcard-dialog singleton, releases the photo object held in the global at -0x101efe54, then posts a status line: if online, FUN_10187090 runs; otherwise status 0x133 'No search results'. | `0x1015ec14` | all | medium | function ✓ |
| Modal handling: notify 0x15 sets the modal flag (FUN_10139b48). Notify 5 closes the dialog and returns 1. | `0x1015ec14` | all | medium | function ✓ |

### Five-category readout (meters or ratings)

Functions: `0x101602c4`, `0x101604a8`. Updates five numeric readouts (one bar each) on a screen from values read from a shared object. A second helper sets a state (style/colour flag and a scaled value) on one control, used by the readout's highlight. The screen itself is not identified in this chunk.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xd2d01` |  | Parent control for category 0. Its child 0xd2d05 receives the bar value. | id ✓ |
| `0xd2d08` |  | Parent control for category 1 (child 0xd2d05 receives the value). | id ✓ |
| `0xd2d09` |  | Parent control for category 2 (child 0xd2d05 receives the value). | id ✓ |
| `0xd2d0a` |  | Parent control for category 3 (child 0xd2d05 receives the value). | id ✓ |
| `0xd2d07` |  | Parent control for category 4 (child 0xd2d05 receives the value). | id ✓ |
| `0xd2d05` |  | Bar or value child under each category parent. Set to an integer 0..1024 (scaled from 100 minus the category percentage). | id ✓ |
| `0xd2d06` |  | Child of the control passed to 0x101604a8. Its style is set to 0x20 or 0x30 depending on state (colour/highlight). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Category value = 100 minus a percentage read from a shared object for that category (0..4). The result is scaled to 0..1024 (value times 1024 divided by 100) and written as the bar value of the category's control. Categories 0,1,2,3,4 map to controls 0xd2d01, 0xd2d08, 0xd2d09, 0xd2d0a, 0xd2d07. | `0x101602c4` | all | high | function ✓ |
| Game type is not read by either function. | `0x101602c4` | all | high | function ✓ |
| The state helper with argument 0 moves state 1 or 3 to state 2 and sets the highlight style to 0x20 on 0xd2d06. The state is stored in control slot 3 and a mode flag (0x200000) in slot 0. Other states are left alone. | `0x101604a8` | all | low | function ✓ |
| With a non-zero argument the helper sets slot 3 to 3 (only from states 0 or 2), sets the mode flag to 0x300000 and style to 0x30 on 0xd2d06. It also stores a scaled value (argument plus 20, times 65536 divided by 100) in slot 2. | `0x101604a8` | all | low | function ✓ |

### Research budget and category panel

Functions: `0x10160cd4`, `0x10160f18`, `0x1016065c`, `0x10160ae8`, `0x101614e0`. Keeps the five per-category research budget sliders, the effort gauge, the category caption text (current research target and cash cost) and the 'average' readout in sync with the underlying research data. Handles user slider changes, and runs the per-category progress animation.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xd2d0e` |  | Budget slider for category 0 (0..100). Value is clamped to 100 when stored. | id ✓ |
| `0xd2d0d` |  | Budget slider for category 1 (0..100). | id ✓ |
| `0xd2d0c` |  | Budget slider for category 2 (0..100). | id ✓ |
| `0xd2d0b` |  | Budget slider for category 3 (0..100). | id ✓ |
| `0xd2d0f` |  | Budget slider for category 4 (0..100). | id ✓ |
| `0xd2d10` |  | Effort gauge. A slider value v (0..max) maps to effort = 70 + v*30/max, stored clamped to 100. Updates child gauge 0xd2d11 and the two audio/handle objects (keys 12/13). Does not forward the generic notify that other sliders send. | id ✓ |
| `Category captions (child 0xd2d05 of each category panel)` | 185: All Items Researched | When the category has no remaining research target, the caption is UITEXT 185. When it has a target, the caption is the target's name in a format string (format id 0x1be, which is blank in UITEXT.tsv) plus a progress/cost value. The same child's value sets a bar of (100 - share)% of 1024. | text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Each research category has a budget share from 0 to 100. Sliders for categories 0..4 write their values through FUN_100f12c4, which clamps values above 99 to 100, then re-run the full panel refresh (FUN_1016065c(-1)) unless the init guard is set. | `0x10160f18` | all | high | function ✓ |
| Effort gauge: the slider value is scaled to a 70..100 effort value via value*30/max + 70. The stored effort is clamped to 100. The gauge then updates its child, sends values to two audio/handle objects, and refreshes the five bars. | `0x10160f18` | all | medium | function ✓ |
| Init guard: a flag is set to 1 while the lab is being built and cleared to 0 by the refresh routine. Slider change handlers only trigger a full cost refresh when the flag is 0. | `0x10160f18` | all | medium | function ✓ |
| Full refresh: updates each category caption, sets each budget slider from its stored share, refreshes the five category bars, then clears the init guard and refreshes costs. | `0x10160cd4` | all | high | function ✓ |
| Category caption: if the category has no research target (its item id is -1 or the lookup fails), it shows UITEXT 185 'All Items Researched'. Otherwise it shows the target name with a progress or cost value. | `0x10160ae8` | all | high | function ✓ |
| Cash cost per category: when a category has a research target, its cash amount is (total park value * 100 / a base) scaled by the category's share percentage. If there is no target, the amount is 0. | `0x1016065c` | all | medium | function ✓ |
| Average readout: the five category cash amounts are summed and divided by 5, and the result is sent to an external object with key 0x12. | `0x1016065c` | all | medium | function ✓ |
| Category bar: each category bar value is set to (100 - X)% of 1024, where X is a per-category percentage from FUN_100f14c0. | `0x101602c4` | all | low | function ✓ |
| Category progress animation: a per-category state machine (slots 0..3 on the category control) advances a frame counter on each tick. State 3 (active, with a target) uses frame range up to 48 and wraps; state 2 caps at 48 and goes idle. Frame values are written to child 0xd2d06 (value 0x30 active, 0x20 winding down). | `0x101614e0` | all | low | function ✓ |

### Research Lab window: root and controls

Functions: `0x10161910`, `0x101611c4`, `0x101614e0`. Opens the Research Lab window (root control 0xd2d00, title UITEXT 184 'Research Lab'), gates opening on game type and researcher staffing, builds five research category panels, and dispatches window messages (close, refresh, control value changes, per-tick animation). The gating and dispatch rules are listed on this screen.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xd2d00` | 184: Research Lab | Window root. Its title is set to 'Research Lab' when window message 0x15 is handled. | id ✓ text ✓ |
| `0xd2d01, 0xd2d07, 0xd2d08, 0xd2d09, 0xd2d0a` | 185: All Items Researched | Five research category panels, mapped to category index 0,4,1,2,3 (0xd2d01=cat0, 0xd2d07=cat4, 0xd2d08=cat1, 0xd2d09=cat2, 0xd2d0a=cat3). Each has a child bar/caption (0xd2d05) and an animated child (0xd2d06). Initialised with a bluebar.wct fill, colour (224,86,86,255) and a 0..100 range. Sub-text is set dynamically (see the budget-panel group). | text ✗ |
| `0xd2d10` |  | Research effort gauge, range 0..1023 (set in FUN_10161910). Children 0xd2d11 (fill/size) and 0xd2d12 (range 500, shows a per-tick rate value). Its value is recomputed on the 0x1e tick. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The Research Lab cannot be opened while the blocking check returns 1, or while the lab is already open (pointer non-null). | `0x10161910` | all | high | function ✓ |
| In Instant Action mode the lab does not open. A message is shown in place of it: UITEXT 468 'Research is automatic in Instant Action mode.' | `0x10161910` | instant_action | high | function ✓, game-type test ✓ |
| In Full Simulation and online modes, if the researcher count field on the park/state object equals a constant (likely zero researchers hired), the lab does not open and instead shows UITEXT 467 'You need to hire some researchers before you can carry out any research!' | `0x10161910` | not_instant_action | medium | function ✓, game-type test ✓ |
| Online mode (game type 1) has no special case in these functions. It behaves like Full Simulation for research. | `0x10161910` | online | medium | function ✓, game-type test ✓ |
| Game type is read lazily via 0x1012bb64 the first time the lab is opened, then cached. | `0x10161910` | all | high | function ✓ |
| On open, the lab creates a window (0x10181aac) and stores it as the lab pointer. The window gets the 'effortguage.wct' and 'bluebar.wct' graphics, sets the effort gauge to range 0..1023 and budget sliders to 0..100, and sets the category fills. Then it runs the initial refresh and starts a 200 cue (0x10139a7c). | `0x10161910` | not_instant_action | medium | function ✓, game-type test ✓ |
| Two sound/handle objects are created at open using ids 0x9a and 0x9b (via 0x101383f8). They are used for the effort-gauge audio. Their meaning is not confirmed (the ids match UITEXT entries 154 and 155 but are not called as text). | `0x10161910` | all | low | function ✓ |
| Window message 0x5 calls 0x10162258 (window close command 4) and consumes the message. Message 0x10 refreshes all five category panels. Message 0x14 is teardown: it clears the lab pointer, stops three sound/handle objects, and plays cue 200. Message 0x15 removes a pending timer if present and sets the title to 'Research Lab'. | `0x101611c4` | all | medium | function ✓ |
| Message 0x800 forwards (control id, value) to 0x10160f18 (slider or gauge change). Messages 0x801 and 0x802 (begin/end on a non-effort control) start and stop a sound handle (sound id 0x9c) and set its volume keys 11/10 from the value. Both are skipped for control 0xd2d10. | `0x101611c4` | all | medium | function ✓ |
| Tick (message 0x1e): when the cached park value changes, the effort gauge scale and position are recomputed from that value and the gauge's stored effort (minus 70). The scale also sets the 0xd2d12 rate value. | `0x101611c4` | all | low | function ✓ |

### Build/ride picker list and item info panel

Functions: `0x10162308`, `0x10162584`, `0x10162a60`, `0x10162e98`. The list of items (rides, shops and similar, plus Buy Land and Clear Land under category 3) with name, state and value, and the info panel under it showing a footprint preview and stat bars (number owned, excitement, safe capacity, reliability, working life) for the selected item.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1f8` |  | Item list. Rows are added by 0x10178768 (name or mystery text, value, state, id). Rebuilt when the selected category changes. Category 3 also gets two extra rows. | id ✓ |
| `0x1ea` |  | Info panel container. Queried by 0x10162584 and 0x10162a60 under a common parent. | id ✓ |
| `0x1eb` |  | Render area for the footprint preview of the selected item (drawn by 0x10162584). | id ✓ |
| `0x1ec` |  | Preview caption. Shows UITEXT 134 'Buy Land', 135 'Clear Land', 137 '??? Mystery Ride! ???', or the text from 0x1013849c for a known item. | id ✓ |
| `0x1ed` |  | Stat bar panel (ridestatbar.wct). Its children are set up in 0x10162a60 and filled in 0x10162e98. | id ✓ |
| `0x1f4 (500)` | 125: Number owned | Stat label. | text ✓ |
| `0x1f3 (499)` | 126: Excitement | Stat label. | text ✓ |
| `0x1f1` | 127: Safe capacity | Stat label. | id ✓ text ✓ |
| `0x1f6` | 128: Reliability | Stat label. Its value is shown on gauge 0x1ee (no value text). | id ✓ text ✓ |
| `0x1f7` | 129: Working life | Stat label. Its value is shown on gauge 0x1ef (no value text). | id ✓ text ✓ |
| `0x1f5` | 136: None | Number-owned value text. Shows the count of the selected item in the park, or 'None' when the count is zero. | id ✓ text ✓ |
| `0x1f2` | 136: None | Excitement value text, shown only for some categories (see rules). | id ✓ text ✓ |
| `0x1ee, 0x1ef, 0x1f0` |  | Gauges with ranges 500, 200 and 100 (set in 0x10162a60). 0x1ee and 0x1ef are filled from the item's stat fields. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Selecting a category stores it as the selected category and rebuilds the item list. If a rebuild is requested, the list is cleared first. | `0x10162308` | all | high | function ✓ |
| An item appears in the list only if its flags are clear, its category matches the selected category, its secondary flag is clear, and its availability byte in the item table is set. | `0x10162308` | all | medium | function ✓ |
| Unresearched items are shown as the mystery entry, UITEXT 137 '??? Mystery Ride! ???'. Their row value is the negative of an item field. Researched items show their name. | `0x10162308` | all | medium | function ✓ |
| Category 3 appends two rows: 'Buy Land' (id -1, UITEXT 134) with a value from a land-cost field, and 'Clear Land' (id -2, UITEXT 135). | `0x10162308` | all | medium | function ✓ |
| Row state value: 1 if the item count in the park is at least 1; otherwise 2 if the item is one of the category's top three entries; otherwise 0. | `0x10162308` | all | low | function ✓ |
| Preview: a negative id shows a placeholder with 'Buy Land' (-1) or 'Clear Land' (-2). An unresearched positive id shows the mystery caption with the value 100. Only a known item shows its footprint grid. | `0x10162584` | all | medium | function ✓ |
| Footprint grid colours: cell codes 1 and 4 are drawn blue, 2 green, 3 orange. Other codes are not drawn. | `0x10162584` | all | low | function ✓ |
| Stat bar gauge ranges: 0x1ee=500, 0x1ef=200, 0x1f0=100. The stat text on 0x1f2 and 0x1f5 defaults to 'None'. | `0x10162a60` | all | medium | function ✓ |
| Stat values: number-owned text shows the park count (or 'None' when zero). The mode global decides which other stats are filled: mode 0 fills the reliability gauge, the excitement text and the working-life gauge; mode 2 fills only the reliability gauge; modes 1 and 3+ fill none of them. | `0x10162e98` | all | medium | function ✓ |
| The gauge values are computed as field*1024/100, but the gauges have ranges of 500, 200 and 100. Either the value scale or the range is different from what the code suggests. | `0x10162e98` | all | low | function ✓ |

### Buy window (Buy Ride / Buy Shop / Buy Sideshow / Buy Miscellaneous)

Functions: `0x101643b4`, `0x10163748`, `0x10163e7c`, `0x10163414`, `0x10162308`, `0x101630c0`. Modal purchase window for park items. It has a category dropdown, a three-column item list, a status line for the selected item, and a cash readout. Activating a row buys or queues the item and closes the window. 0x101643b4 builds or updates it, 0x10163748 is its message handler, 0x10163e7c handles category changes, 0x10163414 enables or disables controls per category, 0x10162308 fills the list, and 0x101630c0 updates the status for the selected row.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1f8` |  | Item list. Columns are Name (UITEXT 0x7b, help 0x90), Price (UITEXT 0x7c, help 0x91) and a third '.' column (UITEXT 0x8a, help 0x92, sort by owned or recently researched). Selection changes call 0x101630c0 after a short debounce. Activation starts the purchase path. | id ✓ |
| `0x1f9` |  | Category dropdown. Its options are 0x1fb (Buy Ride), 0x1fd (Buy Shop), 0x1fa (Buy Sideshow) and 0x1fc (Buy Miscellaneous). Choosing one calls 0x10163e7c, which maps it to category 0 (Ride), 1 (Shop), 2 (Sideshow) or 3 (Misc). | id ✓ |
| `0x1fe` |  | Title label. It shows the selected category name: 0x1fb -> UITEXT 119 'Buy Ride', 0x1fd -> 120 'Buy Shop', 0x1fa -> 121 'Buy Sideshow', 0x1fc -> 122 'Buy Miscellaneous Items'. | id ✓ |
| `0x200` | 458: Cash  $ | Cash readout. It is refreshed by a 1-second timer (control id 0x80080) with the park's current money, formatted positive or negative. | id ✓ text ✗ |
| `0x1f3 (decimal 499)` |  | Enabled for category 0 (Ride) and category 2 (Sideshow). Disabled for Shop and Misc. Purpose not identified. |  |
| `0x1ee` |  | Enabled for Ride and Sideshow, disabled for Shop and Misc. Purpose not identified. | id ✓ |
| `0x1f0, 0x1f1, 0x1f2, 0x1f6, 0x1f7, 0x1ef` |  | Enabled only for category 0 (Ride). Disabled for Sideshow, Shop and Misc. Purpose not identified. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Category switch rebuilds the list for the chosen category, sets the title to that category's name, and applies the per-category enable matrix. | `0x10163e7c` | all | high | function ✓ |
| Enable matrix: Ride enables all eight controls (0x1f3, 0x1f1, 0x1f6, 0x1f7, 0x1ee, 0x1f2, 0x1ef, 0x1f0). Sideshow enables only 0x1f3 and 0x1ee. Shop and Misc disable all eight. | `0x10163414` | all | high | function ✓ |
| List filter: an item is listed only if its category field matches the chosen category, it is not in the hidden-flag state, its park-side entry has its availability byte set, and the record's secondary type field is 0. | `0x10162308` | all | medium | function ✓ |
| Locked item display: if the item has a requirement value greater than 0 and is not already researched, the row shows '??? Mystery Ride! ???' (UITEXT 0x89) and its price column shows the negative of the requirement value. Otherwise it shows the real name and cost. | `0x10162308` | all | medium | function ✓ |
| Selecting a row computes a status state (0-3) and shows a status message. State 0 (prerequisites met, not owned): message 0x97, no price. State 1 (prerequisites not met): message 0x98, no price. State 2 (no prerequisite or already researched, and cash below price): message 0x96 with the price. State 3 (affordable): message 0x94 with the price. The status is only rewritten when the state or item changes. | `0x101630c0` | all | medium | function ✓ |
| Purchase path A (item has no prerequisite, or is already researched): buying requires cash greater than or equal to the entry's price. If affordable, the window enqueues a type-4 request for the item in the park message queue, closes the window, and plays a success feedback. If not affordable, error feedback plays and the window stays open. | `0x10163748` | all | medium | function ✓ |
| Purchase path B (item has a prerequisite that is not yet researched): if the prerequisite count is met, the item proceeds through the same enqueue-and-close path with NO cash check. Otherwise error feedback. This suggests a research step that does not charge money at this point. | `0x10163748` | all | low | function ✓ |
| Special negative row ids (-1, -2/-3) on activation show an error message and close the window. Message ids are unresolved. | `0x10163748` | all | low | function ✓ |
| Selection changes are debounced: the status update runs only after about 500 ms have passed since the pending selection changed. | `0x10163748` | all | low | function ✓ |
| Window open applies the current category's enable matrix and starts or re-arms the 1-second cash refresh timer (id 0x80080). Closing (0x14 event) clears the current-window global and posts a follow-up message. | `0x10163748` | all | medium | function ✓ |
| Command 0x1ff opens a different screen (not identified). Command -2 closes the buy window. | `0x10163748` | all | low | function ✓ |
| Opening the window with no existing instance builds the dialog: a three-column list with headers, the category dropdown with four options, the title, and the list's help ids. With an existing instance it just updates category and preselects an item by id. | `0x101643b4` | all | medium | function ✓ |
| No game-type check exists in any function of this screen. Instant Action gets the same buy flow. | `0x10163748` | all | high | function ✓ |

### Ride stat bar (selected ride info strip with buttons and upgrades list)

Functions: `0x10164f00`, `0x101654bc`, `0x10164a5c`, `0x10165a0c`. Info strip for the currently selected ride (pointer in global _DAT_101ed68c). It shows labelled statistics, gauges, a name value, and a group of action buttons whose visibility depends on the ride's state. It also contains an upgrade list, built by 0x10165a0c. 0x10164f00 builds the controls, 0x101654bc refreshes them every update, 0x10164a5c sets button visibility and enablement, and 0x10165a0c fills the upgrade list.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x3e20` | 17: Users last month | Static label. | id ✓ text ✓ |
| `0x3e1a` | 18: Age | Static label. | id ✓ text ✓ |
| `0x3e1c` | 19: Excitement | Static label. | id ✓ text ✓ |
| `0x3e1e` | 20: Reliability | Static label. | id ✓ text ✓ |
| `0x3e1f` | 21: State of repair | Static label. | id ✓ text ✓ |
| `0x3e1d` | 22: Remaining life | Static label. | id ✓ text ✓ |
| `0x3e22` | 23: Scrap value | Static label. | id ✓ text ✓ |
| `0x3e21` |  | Name value field, set from a 30-character string on the ride record (offset +0x1a4). Built as a text-field control. | id ✓ |
| `0x3e1b` |  | Signed number readout, formatted with the blank template 0x1b1 plus a sign. Label unclear. | id ✓ |
| `0x3e16, 0x3e18, 0x3e19, 0x3e17` |  | Gauges. Values are percentages scaled by 1024/100. 0x3e16 comes from one ride stat function, 0x3e18 from a three-value combination of ride-record fields, 0x3e19 from a float on the record (+0x40), and 0x3e17 from a byte scaled the same way. Setup gives each a 0-10 range. |  |
| `0x3e23` |  | Numeric readout from a ride-record accessor. Label unknown. | id ✓ |
| `0x3e2a` |  | Button. Shown when the ride state is not 3. Enabled only if the ride record's +0x9c field is nonzero. | id ✓ |
| `0x3e2c` |  | Toggle-like control. Shown when the ride state is not 3. Its state reflects a boolean from a ride accessor. | id ✓ |
| `0x3e34, 0x3e35, 0x3e36, 0x3e37` |  | Shown when the ride state is not 3. 0x3e36 is additionally turned on for ride states 0x11 to 0x15. Purpose not identified. |  |
| `0x3e38` |  | Shown when the ride state is not 3. Its text is cleared for states 0x19 and 0x1a, otherwise set by flag logic. Purpose not identified. | id ✓ |
| `0x1f4d` |  | Upgrade list inside the stat bar. Filled by 0x10165a0c when the bar's list child exists. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The stat bar refreshes only when a ride is selected (the global ride pointer is non-null). Refresh values come from ride accessor functions. | `0x101654bc` | all | high | function ✓ |
| When the ride's state (queried with mode 2) changes, the bar's header is updated, the button visibility is recomputed, and a further update routine runs. | `0x101654bc` | all | medium | function ✓ |
| Button group visibility: when the state is 3, seven buttons (0x3e38, 0x3e34, 0x3e2a, 0x3e36, 0x3e2c, 0x3e37, 0x3e35) are hidden. Otherwise they are shown, with individual enables as described. | `0x10164a5c` | all | high | function ✓ |
| Button 0x3e2a is enabled only when the ride's secondary record field (+0x9c) is nonzero. | `0x10164a5c` | all | medium | function ✓ |
| Button 0x3e38 text: cleared for states 0x19 and 0x1a. Otherwise the label is chosen from two ride flag accessors (the flags and their meaning are unidentified). | `0x10164a5c` | all | low | function ✓ |
| Upgrade list is refreshed only when the bar's upgrade-list child exists, using the ride type id, the upgrade list control, and the current ride object. | `0x101654bc` | all | high | function ✓ |
| Labels and gauges are created once: static labels from UITEXT 0x11-0x17, gauges with a 0-10 range, and the bar resource 'ridestatbar.wct' for the gauge graphics. | `0x10164f00` | all | medium | function ✓ |
| No game-type check exists in the stat bar functions. | `0x10164a5c` | all | high | function ✓ |

### Ride upgrade list (in the ride stat bar)

Functions: `0x10165a0c`. Fills the upgrade list for the selected ride. Rows are: available researched upgrades for that ride type, an 'Upgrade to level N' row, a 'Cancel level N' row when an upgrade is in progress, and header or info text lines when nothing else applies. This is where Instant Action restrictions apply.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1f4d` |  | Target list. Cleared when the refresh flag is set. | id ✗ |
| `header text` | 27: Upgrades are not available in Instant Action mode | Shown in Instant Action (game type 2) as the only info line. Split into lines and added as non-selectable rows. | text ✓ |
| `header text` | 28: This ride is fully upgraded | Shown when the ride's upgrade level is 2, and no list rows were added. | text ✓ |
| `header text` | 26: No upgrades have been researched | Shown when the list is empty for other game types and the ride is not fully upgraded. | text ✓ |
| `upgrade row` | 29: Upgrade to level | Row for the next upgrade level. Shown when the ride's level is below 2 and not in progress, and the ride type's researched level count is at least the next level. Row id is the negative of the next level index. | text ✗ |
| `cancel row` | 30: Cancel level | Shown when an upgrade is in progress. Row id is -12999 (0xffffcd39). | text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Game-type gate: when the game type is Instant Action (2), the whole row-building block is skipped. Only the header 'Upgrades are not available in Instant Action mode' is shown. No upgrade rows, no level-up row, no cancel row. | `0x10165a0c` | instant_action | high | function ✓, game-type test ✓ |
| Outside Instant Action: the list shows researched upgrade items for the ride type (park-side availability set, matching category, not hidden), each with a name and cost. If none exist and no level or cancel row is added, a header is shown: 'fully upgraded' (level 2) or 'No upgrades have been researched' (other levels). | `0x10165a0c` | not_instant_action | medium | function ✓, game-type test ✓ |
| Outside Instant Action: an upgrade in progress shows 'Cancel level N' with row id -12999. Otherwise, if the ride's level is below 2, an 'Upgrade to level N' row is offered, but only when the ride type's researched level count (park entry +0x14) is at least the next level. The displayed number is the level index plus 2. | `0x10165a0c` | not_instant_action | medium | function ✓, game-type test ✓ |
| Info lines are produced by splitting the header text on a delimiter. Each piece becomes a non-selectable row (id -7890). | `0x10165a0c` | all | low | function ✓ |
| Game type is lazily initialised on first use, from the same global read used for the Instant Action check. | `0x10165a0c` | all | high | function ✓ |

### Buy Upgrades dialog (ride/shop upgrade list)

Functions: `0x10165ed0`, `0x10165d34`, `0x10166084`, `0x10166f1c (callee, read in full)`. A modal child dialog opened over the current object. It lists purchasable upgrades for the selected ride/shop type and lets the player buy one. Opening is gated so only one instance exists at a time.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1f4e` | 31: Buy Upgrades | Title/label of the dialog, set from UITEXT 0x1f. | id ✓ text ✓ |
| `0x1f4d` |  | List of upgrades. Filled by FUN_10165a0c using the selected record's type id. Selecting an entry sends a 0x400 list-selection message whose item index is resolved by FUN_1017991c to an upgrade id. | id ✓ |
| `0x1f4e (child 5/6)` |  | Helper widgets created with the dialog. Their exact roles are not verified. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The dialog is only built when no upgrade dialog is already open (the stored dialog handle is 0 and the owner's secondary slot is 0). | `0x10165ed0` | all | high | function ✓ |
| Opening the dialog sets its state caption to 1 and stores the new dialog handle; the reset helper clears the handle and sets the caption back to 0 (closed/idle). | `0x10166084` | all | medium | function ✓ |
| Escape-like message (key code -2 in a 0x100 command) closes the dialog with close code 4. | `0x10165d34` | all | high | function ✓ |
| Message 0x14 (and only that code within the 0x14..0x15 window) resets the upgrade dialog state. Other messages are passed to the generic handler. | `0x10165d34` | all | medium | function ✓ |
| Selecting an entry in the upgrade list (0x400) reads the item index, resolves it to an upgrade id, then attempts purchase. Selecting id -12999 is a special 'close' choice. Sentinel -7890 is ignored. | `0x10165d34` | all | medium | function ✓ |
| After a purchase attempt the dialog always closes. A failed purchase (or a successful one with a non-positive id) also runs a refresh helper. A successful purchase with a positive id returns after closing only. | `0x10165d34` | all | medium | function ✓ |
| Purchase logic for a positive upgrade id: looks up the item cost in a table. If cost <= current cash (a value at +0xc of a global object, probably park money), the item is created and charged, the selected-object global is set, a sound/refresh is triggered, and success is returned. Otherwise an insufficient-funds helper runs and failure is returned. | `0x10166f1c` | all | medium | function ✓ |
| For a negative item id (a slot/category index, -id) the code compares a per-slot field against cash, with the opposite inequality from the positive case. It shows a failure message when the field is less than the cash value. This direction looks inverted and is unverified. | `0x10166f1c` | all | low | function ✓ |
| A value flag of 1 in the purchase helper refunds or cancels with a special message and returns success, without charging. | `0x10166f1c` | all | low | function ✓ |
| No game-type check (Instant Action vs Full Simulation vs online) appears in the dialog builder or handler. Purchase costs and gating depend only on cash and the record data, not on the game-type global. | `0x10165ed0` | unknown | medium | function ✓ |

### Ride status bar (selected ride HUD strip)

Functions: `0x101660b0`, `0x101661bc`, `0x101664a4`, `0x10166994`. A strip of gauges and labels that shows the selected ride's speed, capacity, and the duration/laps/cycles/repetitions value, with per-tick refresh.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x3e26` |  | Extra control hidden unless the state gate (see rule) returns at least 2. | id ✓ |
| `0x3e27` |  | Extra control hidden unless the state gate returns at least 2. | id ✓ |
| `0x3e28` |  | Title or help string set via a separate string table id (0x3e28, not a UITEXT index). Text not known. | id ✓ |
| `0x3e2d` |  | Gauge (range 0..120, smallguage.wct child 0x3e2e) for capacity fill. Hidden when the ride's capacity min equals max. | id ✓ |
| `0x3e2f` |  | Slider (range 0..10). Enabled only when the ride type is nonzero. Its value comes from a byte in the ride record. | id ✓ |
| `0x3e30` |  | Gauge (range from ride record) with smallguage.wct child, showing a fraction of a per-ride value. | id ✓ |
| `0x3e31` | 1: Go Online | Capacity readout. Format depends on the ride type field: type 3 uses UITEXT 0x1cf, type 2 uses 0x1cd, other types use 0x1ab. Value = record +0x30. | id ✓ text ✗ |
| `0x3e32` |  | Time/count readout. Type 1 shows Duration (0x1ac when value is 1, otherwise 0x1ad). Type 2 shows Laps (0x1ae). Type 3 shows Cycles (0x1af). Type 4 shows Repetitions (0x1b0). Type 0 hides the control. Value = record +0x34. | id ✓ |
| `0x3e33` | 426: Speed : | Speed readout, signed. Value = handle +0x2c. The sign is shown by a separate flag, as with the other readouts. | id ✓ text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The ride status bar is only built when the owner window exists. It sets up gauges and labels, then calls the per-tick refresh. | `0x101661bc` | all | high | function ✓ |
| Gauges 0x3e2d and 0x3e30 use range 0..120 with the smallguage.wct image. Slider 0x3e2f uses range 0..10. | `0x101661bc` | all | high | function ✓ |
| Show/hide and enable behaviour: ride type 0 hides the duration control and disables the 0x3e2f slider. Any nonzero type enables it. | `0x10166994` | all | medium | function ✓ |
| Capacity gauge (0x3e2d) and capacity label (0x3e31) are hidden when the ride's capacity min equals max. Otherwise they are shown with a fill fraction. | `0x10166994` | all | medium | function ✓ |
| The 0x3e30 gauge range comes from the ride record's min and max fields. Its value is the ride-object field at +0x54. The 0x3e2e child shows a fraction computed as a per-ride value scaled by 1024 and divided by the range size. | `0x10166994` | all | medium | function ✓ |
| Possible divide-by-zero: the fraction for 0x3e2e in the first block divides by (max - min) without an equality guard. The later block does guard this. | `0x10166994` | all | low | function ✓ |
| Format rule for the speed readout: shows Speed : with the absolute value and a sign flag. Shown regardless of type. | `0x101664a4` | all | high | function ✓ |
| Capacity text variant by ride type: type 3 uses 0x1cf, type 2 uses 0x1cd, others use 0x1ab. Value from +0x30. | `0x101664a4` | all | high | function ✓ |
| Time/count readout: type 2 shows Laps, type 3 shows Cycles, type 4 shows Repetitions, type 1 shows Duration, type 0 hides the control, and negative types leave it unchanged. Value from +0x34. | `0x101664a4` | all | medium | function ✓ |
| Duration for a value of 1 uses a separate string (0x1ac, which takes an extra argument), otherwise 0x1ad. This probably handles singular vs plural wording. | `0x101664a4` | all | low | function ✓ |
| The state gate for the extra controls 0x3e26 and 0x3e27: the status bar shows them only when the state result from the mode helper is at least 2; otherwise they are hidden. | `0x101660b0` | unknown | low | function ✓ |
| Selecting or showing a ride-type-dependent panel: the status bar is shown, and its title is set from string id 0x3e28 (not from UITEXT). | `0x101660b0` | all | medium | function ✓ |
| The status bar refreshes all three readouts with a bit mask (7 = speed, capacity, time/count). The per-tick refresh uses the same mask. | `0x10166994` | all | high | function ✓ |
| The ride-type field (+0x70 of the ride record) appears to encode the time mode: 0 none, 1 duration, 2 laps, 3 cycles, 4 repetitions. This is inferred from the label choices. | `0x101664a4` | all | low | function ✓ |

### Ride/shop statistics panel (sales and rating readouts)

Functions: `0x10167714`, `0x10167d18`. A read-only panel with labelled stat rows (customers, costs, takings, profit, satisfaction, scrap value, happiness) plus gauges. The refresh updates values from the shop/park records.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xc075` | 32: Customers last month | Row label. The value is set by the refresh. | id ✓ text ✓ |
| `0xc071` | 33: Cost of goods | Row label. | id ✓ text ✓ |
| `0xc072` | 34: Takings last month | Row label. | id ✓ text ✓ |
| `0xc073` | 35: Profit last month | Row label. | id ✓ text ✓ |
| `0xc074` | 36: Customer satisfaction | Row label. | id ✓ text ✓ |
| `0xc07a` | 37: Scrap value | Row label. | id ✓ text ✓ |
| `0xc07b` | 38: Local happiness | Row label. | id ✓ text ✓ |
| `0xc070` |  | Header/summary line. Cleared at build time, then set in refresh from a format string id 0x1cc with two computed numbers. | id ✓ |
| `0xc076` |  | Numeric readout whose value is derived from the record and a value times 50. Converted through a float helper. | id ✓ |
| `0xc077` |  | Numeric readout from a string-to-number conversion of a shop record field. | id ✓ |
| `0xc078` |  | Numeric readout set from a shop-record helper. | id ✓ |
| `0xc079` |  | Gauge (range 0..10, ridestatbar.wct image). Value is a percentage. | id ✓ |
| `0xc07c` |  | Gauge (range 0..10, happygrad.wct image, max value 100). Value is a percentage. | id ✓ |
| `0xc07d` |  | Numeric readout set from a shop-record helper. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The statistics panel is built only if its window exists. It places the seven labels and the gauges, then clears the summary line. | `0x10167714` | all | high | function ✓ |
| The labels map to the UITEXT block 0x20..0x26 in order: Customers last month, Cost of goods, Takings last month, Profit last month, Customer satisfaction, Scrap value, Local happiness. | `0x10167714` | all | high | function ✓ |
| Refresh updates the summary line and each readout from the current shop/park record. The summary is a formatted string from a numeric value in the record plus a second sum. | `0x10167d18` | all | medium | function ✓ |
| Happiness and satisfaction gauges show percentages. One is computed from a shop-record helper scaled by 1024 and divided by 100, the other from a clamp of a happiness helper. | `0x10167d18` | all | low | function ✓ |
| A state value from the refresh helper (0x10138a1c) is compared with the stored value. If it changed, it is stored and the panel is redrawn. | `0x10167d18` | all | medium | function ✓ |
| No game-type check is visible in these two functions. | `0x10167d18` | unknown | medium | function ✓ |

### Stall/ride pricing and quality panel (shop controls)

Functions: `0x1016803c`, `0x101683a8`, `0x101686b4 (message dispatcher for this panel)`. A control panel with three sliders (0..2, 0..100, 0..500), toggles, and labelled rows for the selected shop. Build and refresh set the values from the shop record, and message handlers write the changes back.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xc069` |  | Control hidden unless the state gate returns at least 2. | id ✓ |
| `0xc06a` |  | Control hidden unless the state gate returns at least 2. Command 0xc06a calls a helper in the dispatcher. | id ✓ |
| `0xc06b` |  | Slider (range 0..2, step 50 in the record scale). Changes write the value to the panel at +0x24 and update 0xc07f. | id ✓ |
| `0xc07f` |  | Shows 1 shifted left by the 0xc06b value (a power-of-two readout). Set on value change. | id ✓ |
| `0xc086` |  | Slider (range 0..100). Value written to panel at +0x28. Shown only when a shop-record gate returns a nonzero value. | id ✓ |
| `0xc085` |  | Slider (range 0..500). Value written to the panel at +0x2c. | id ✓ |
| `0xc07e` | 39: Quality of goods | Row label. | id ✓ text ✓ |
| `0xc083` | 40: Sale price | Row label. | id ✓ text ✓ |
| `0xc084` |  | Label shown only when the shop record's category field is in the mapped range (1..4). It is hidden otherwise, together with 0xc086. | id ✓ |
| `0xc082` |  | Toggle. Refresh sets its state to on or off from a helper (0 or 1). | id ✓ |
| `0xc06e` |  | Title set from string table id 0xc06e (not a UITEXT index). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Build sets the three sliders and labels, then calls the refresh helper with the owner window. Gauges and labels are created and attached in the same pass. | `0x1016803c` | all | high | function ✓ |
| The controls 0xc069 and 0xc06a are hidden unless the state gate (mode helper) returns at least 2. | `0x1016803c` | unknown | low | function ✓ |
| Quality of goods (0xc07e) and Sale price (0xc083) are labelled from UITEXT 0x27 and 0x28. | `0x1016803c` | all | high | function ✓ |
| The category label 0xc084 and the 0xc086 slider are shown only when the shop record's category field maps to a nonempty string. Otherwise both are hidden. | `0x101683a8` | all | medium | function ✓ |
| Refresh sets the 0xc06b slider from a record byte divided by 50 and the 0xc086 slider from a record byte. | `0x101683a8` | all | medium | function ✓ |
| Refresh sets toggle 0xc082 to on when a helper returns nonzero, otherwise off. | `0x101683a8` | all | medium | function ✓ |
| Refresh sets slider 0xc085 from a helper that reads a record field. | `0x101683a8` | all | medium | function ✓ |
| Dispatcher: a 0x800 value change on 0xc085 stores the value into the panel at +0x2c and redraws. A change on 0xc06b stores it at +0x24 and updates 0xc07f. A change on the 0xc086 slider stores it at +0x28 and redraws. | `0x101686b4` | all | high | function ✓ |
| Dispatcher: a command on 0xc06a runs a helper. Command 0xc067 closes and returns 0. Commands 0xc067..0xc069 use the close or action helpers. Commands in the 0xc07f..0xc080 range run a helper. Commands 0xc081 post a message and redraw. Commands 0xc082..0xc083 toggle a value via a helper with on or off and redraw the stats panel. | `0x101686b4` | all | medium | function ✓ |
| Dispatcher: message 0x10 redraws the statistics panel (0x10167d18) when a specific sub-code is received. | `0x101686b4` | all | medium | function ✓ |
| No game-type check is visible in this panel, apart from the state gate on the 0xc069 and 0xc06a controls. | `0x101686b4` | unknown | medium | function ✓ |

### Sales summary dialog (finance totals)

Functions: `0x10168bc0`, `0x10168a38 (callee, read in part)`. A modal summary of the sales and cost totals for a shop/ride, shown as a list-like dialog. Totals are computed from the record and written back by setters.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x2de` |  | Dialog root/list control. Its value fields are updated on each refresh. | id ✓ |
| `0x2df` |  | Total value: the sum of five record fields (likely costs). Label not verified. | id ✓ |
| `0x2e4` |  | Derived value: one record field minus the sum of another field and the 0x2df total (likely profit). Label not verified. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The summary totals are recomputed on each 0x800 value notification. The total is the sum of five fields. The derived value is a second field minus (another field + the total). | `0x10168bc0` | all | medium | function ✓ |
| The setter 0x10168a38 clamps negative input to 0 and sets a flag when the value is positive. It handles ids 0x2e7 and 0x2eb. | `0x10168a38` | all | medium | function ✓ |
| Command 0x2db runs a helper. Command 0x2dc runs a helper, and 0x2dd runs another. Command -1 closes the dialog with code 4 (via the close helper). | `0x10168bc0` | all | medium | function ✓ |
| Message 5 closes the dialog and returns 1. Message 0x14 clears the global dialog handle. | `0x10168bc0` | all | medium | function ✓ |
| No game-type check is visible in this dialog. | `0x10168bc0` | unknown | medium | function ✓ |

### Finance / last-month statistics panel

Functions: `0x10168d8c`. Builds once (guarded by the panel pointer at _DAT_101edc6c being null) a park statistics window with five stat bars (icon ridestatbar.wct) plus a small cash-flow block (Cash in, Staff costs, Other costs, Balance). Values come from the park object (FUN_101082b4 on the global at _DAT_101ec930).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x2e7 / 0x2ec / 0x2ea / 0x2eb / 0x2ed` |  | Five progress-style stat bars, each with a 0..10000 range, a ridestatbar.wct image and a value child (0x2e8). Stat indices fed to the bars are 5, 4, 6, 7 and 8 in that order. Each value is converted from the stored 64-bit amount with the (v<<10)/100 idiom (see rules). |  |
| `0x2e8` |  | Numeric value child inside each stat bar, filled with the converted stat value. | id ✓ |
| `0x2de` |  | Cash-flow sub-section. Only populated if the child exists. Contains the four rows below. | id ✓ |
| `0x2e3` | 357: Cash in | Label for the cash-in row. Its value control 0x2e2 gets the park finance amount at offset +0x1fc90. | id ✓ text ✓ |
| `0x2e6` | 358: - Staff costs | Label for the staff-cost row. Value control 0x2df gets the amount at +0x1f7f0. | id ✓ text ✓ |
| `0x2e1` | 359: - Other costs | Label for other costs. Value control 0x2e0 gets total costs minus staff costs. | id ✓ text ✓ |
| `0x2e5` | 361: Balance | Label for the balance row. Value control 0x2e4 gets cash in minus total costs (total costs come from +0x1f5a0). | id ✓ text ✓ |
| `0x2da` | 147: Staff Training Budgets | Text set on the panel root. The string does not obviously match the displayed figures, so its role is uncertain. | id ✓ text ✓ |
| `0x2dd` |  | Text is cleared (set to 0) only when the game type is 2 (Instant Action). Other modes keep whatever text it has. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The statistics panel is only built on the first call. A later call with the panel pointer already set does nothing. | `0x10168d8c` | all | high | function ✓ |
| The game type is read lazily. If the cached init flag is zero, FUN_1012bb64 is called on the type global and the flag is set. | `0x10168d8c` | all | high | function ✓ |
| In Instant Action only (type == 2), the label at control 0x2dd has its text cleared. In Full Simulation and online modes the text is left as it is, so the panel differs by game type. | `0x10168d8c` | instant_action | high | function ✓, game-type test ✓ |
| Cash-flow figures are derived from park finance accessors. Cash in is the amount at +0x1fc90, staff costs the amount at +0x1f7f0, and total costs the amount at +0x1f5a0. | `0x10168d8c` | all | medium | function ✓ |
| Other costs are shown as total costs minus staff costs. Balance is shown as cash in minus total costs. | `0x10168d8c` | all | medium | function ✓ |
| Stat bar values use the idiom (v*1024)/100 with signed-division rounding. This maps a value to a 0..1024-scale bar unit. Reading the exact scale is inference. | `0x10168d8c` | all | low | function ✓ |
| Each stat bar is a 0..10000 range progress control with a default value of 25 and a black-type colour setting. | `0x10168d8c` | all | low | function ✓ |

### Selected-object info strip (name, value, mode switch)

Functions: `0x10169bb0`, `0x10169d00`, `0x1016a370`, `0x1016a444`, `0x1016a5c0`, `0x1016a880`, `0x1016ab88`. Small strip for the currently selected object (a ride/shop/sideshow or a visitor). It shows the name and a per-type value, with two sub-panels (0x95c default, 0x963 for type 1) and a mode value that swaps between a text control and a numeric control. Built by FUN_1016a5c0, updated by FUN_10169d00 and FUN_10169bb0, and selected by FUN_1016a880 and FUN_1016ab88.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x147` |  | Name label. For object types 4..8 it shows the object's name (via FUN_100f46b4). For other types it shows 'Visitor #n' (UITEXT 0x1c8, n taken from the object's +0x1d8 field), or 'New Visitor' (UITEXT 0x1c9) when that field is zero. | id ✓ |
| `0x148` |  | Shown in normal mode and hidden in mode 6. For types 4..8 it is a value bar updated from FUN_1013892c whenever that value changes. Otherwise it is set to empty text. | id ✓ |
| `0x14a` |  | Shown in mode 6 and hidden otherwise. Numeric label: for types 4..8 it shows the object's value minus 1, or 0x29a (666) when that value is zero. Behaviour for other types is not clear. | id ✓ |
| `0x149` |  | Hidden on build. Shown when message 0x10001 is received and hidden on message 0x10002. Its role is unknown. | id ✓ |
| `0x95c / 0x963` |  | Two sub-panels, each holding a child 0x95d. The 0x963 panel is used when the selected object is type 1, and 0x95c for all other types. Both are hidden on build. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Selecting a null object clears the selection and deselects the current panel item. | `0x1016a880` | all | medium | function ✓ |
| Selecting an object of type 1 uses sub-panel 0x963 and sets the mode global to 2. All other types use 0x95c and set the mode global to 1. | `0x1016a880` | all | medium | function ✓ |
| For object types below 9 the selection code dispatches through a jump table to a per-type handler. Those handlers are outside this chunk. Types 9 and above take a generic path that shows the name label and refreshes it. | `0x1016a880` | all | medium | function ✓ |
| Mode 6 (the mode the selection uses for types with a dedicated value) hides 0x148, shows 0x14a and plays/triggers the event for ID 0x11f. Any other mode does the reverse and triggers the event for ID 0xc6. | `0x1016a880` | all | medium | function ✓ |
| Mode 6 is also set by the alternate entry point (0x1016ab88), which hides 0x148, shows 0x14a and fires ID 0x11f. | `0x1016ab88` | all | medium | function ✓ |
| The name label is refreshed for types 4..8 with the object's name, and for other types with 'Visitor #n' or 'New Visitor'. | `0x10169d00` | all | medium | function ✓ |
| The value on 0x148 for types 4..8 updates only when FUN_1013892c changes. Otherwise the 0x14a value is computed as the stored value minus 1, or 666 when it is zero. | `0x10169bb0` | all | low | function ✓ |
| Message 0x10001 shows control 0x149 and message 0x10002 hides it. Message 0x14 clears the panel pointer (_DAT_101edc90). Message 0x10006 with a zero parameter calls one of two handlers. The handler is FUN_1016e1a4 for types 4..8 and FUN_1010b808 otherwise. | `0x1016a444` | all | low | function ✓ |
| Message 0x100 (in FUN_1016a370) selects the item when parameter 4 is 1 and otherwise sets a state with two flags. It then sends 0x101 to the child. Otherwise the message goes to the default handler. | `0x1016a370` | all | low | function ✓ |
| Build sets the sub-panels hidden and gives the name label a default text. The 0x149 item is hidden and an item at 0x1e84-style offset is set up. The current-selection global is cleared at the end. | `0x1016a5c0` | all | low | function ✓ |

### Attraction statistics panel (customers, takings, happiness, scrap)

Functions: `0x1016af08`, `0x1016b5d0`. Builds (0x1016af08) and refreshes (0x1016b5d0) a stat panel for the object at panel+0x10. It has eight labels with UITEXT 41..48 and several value bars. Values come from the attraction's data via accessors on the object.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xa09c / 0xa095 / 0xa09a / 0xa098 / 0xa09b / 0xa097 / 0xa0a1 / 0xa09f` |  | Eight labels. The UITEXT mapping is 0xa09c=41 'Customers last month', 0xa095=42 'Winners last month', 0xa09a=43 'Takings last month', 0xa098=44 'Profit last month', 0xa09b=45 'Excitement', 0xa097=46 'Customer satisfaction', 0xa0a1=47 'Scrap value', 0xa09f=48 'Local happiness'. The label-to-value pairing is only partly known. |  |
| `0xa0a0` |  | Scrap value number, from FUN_100e25e8 on the object. This pairing is supported by the matching accessor used in 0x1016c738. | id ✓ |
| `0xa09e` |  | Local happiness bar, drawn with the happygrad.wct image. Range 0..10 with the value set to 100, then refreshed from the object's happiness field. | id ✓ |
| `0xa096 / 0xa099 / 0xa094` |  | Numeric bars with ridestatbar.wct image, each filled from one of the object's accessors (see rules). |  |
| `0xa093 / 0xa092` |  | 0xa093 is a text control filled from a 30-character string at object+0x6c. 0xa092 is a numeric value from FUN_100e21f4. Both are hidden or empty on build. |  |
| `0xa09d` | 460:  | Text control whose content is a format string with two runtime arguments built from the object's name strings. The format is UITEXT 0x1cc, which is an empty string. Display is unclear. | id ✓ text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The statistics panel is built once with eight labels and their value controls. Build sets default text, a 0..10 range on the happiness bar and an image of ridestatbar.wct on the stat bars. | `0x1016af08` | all | high | function ✓ |
| Refresh pulls values from the object through accessors. Some values are scaled with the (v<<10)/100 idiom, and the happiness value combines two fields and a park-level call. | `0x1016b5d0` | all | medium | function ✓ |
| The panel's own value is set to a new value only when the accessor FUN_10138a1c(obj,2) changes, so the panel is not redrawn unnecessarily. | `0x1016b5d0` | all | low | function ✓ |
| Scrap value is shown from FUN_100e25e8 on the object. The same accessor is used by the small scrap panel below. | `0x1016b5d0` | all | medium | function ✓ |

### Game price, prize and entry panel (sideshows and games)

Functions: `0x1016b954`, `0x1016bd3c`, `0x1016c454`. Builds (0x1016b954) and refreshes (0x1016bd3c) the pricing block for a game or sideshow: game price, prize cost and chance of winning. Or, when the item uses entry-fee mode, a price of entry with the prize rows hidden. 0x1016c454 keeps a 3D preview of the selected item sized to control 0x1e84.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xa0a5` | 51: Price of game | Price label. Its text is 51 ('Price of game') in game mode and 52 ('Price of entry') in entry mode. | id ✓ text ✓ |
| `0xa0a7` | 49: Chance of winning | Label for the prize-chance bar. Shown only in game mode. | id ✓ text ✓ |
| `0xa0a6` | 50: Cost of prize | Label for the prize-cost bar. Shown only in game mode. | id ✓ text ✓ |
| `0xa090` |  | Chance-of-winning bar, range 0..10000, value 1 on build. Enabled in game mode, disabled in entry mode. | id ✓ |
| `0xa08f` |  | Bar with range 0..500 and value 1 on build. Probably the prize cost. Value is set from FUN_100e1f70. | id ✓ |
| `0xa08e` |  | Slider with range 0..100. Value is set from FUN_100e2364. Enabled in game mode, disabled in entry mode. | id ✓ |
| `0xa08c / 0xa08d` |  | Two buttons (plus and minus style). Hidden on build when FUN_10146b48 reports fewer than two items. |  |
| `0xa08a` |  | Toggle state set from FUN_100dff18 (on or off). | id ✓ |
| `0xa0a4` |  | Passed to a helper on build. Role unknown. | id ✓ |
| `0x1e84` |  | Preview viewport. Its rectangle drives a render object at panel+0x24 that is rebuilt when the size or item changes. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Game-mode versus entry-mode switch. When the flag at +0x10c of the item data is 0, the item is a game with price and prizes. The price and prize controls are enabled and the label reads 'Price of game'. When the flag is non-zero, the item is charged as entry: the price and prize controls are disabled and the label reads 'Price of entry'. | `0x1016bd3c` | all | medium | function ✓ |
| The prize label rows (0xa0a6 and 0xa0a7) and the prize bar are visible only in game mode. They are hidden in entry mode. | `0x1016bd3c` | all | medium | function ✓ |
| Build sets the chance-of-winning bar range to 0..10000 and the prize-cost bar range to 0..500, and the price slider range to 0..100. | `0x1016b954` | all | medium | function ✓ |
| The two cycle buttons (0xa08c and 0xa08d) are hidden when FUN_10146b48 on the current item list returns fewer than two entries. Probably they cycle through multiple items. | `0x1016b954` | all | medium | function ✓ |
| The preview viewport is rebuilt only when the item's stored size (fields at +0x24 and +0x20) changes. The old preview object is freed first. | `0x1016c454` | all | low | function ✓ |

### Small scrap-value panel

Functions: `0x1016c62c`, `0x1016c738`. A compact panel with a 'Scrap value' label and a numeric value. 0x1016c62c builds it and 0x1016c738 refreshes it.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1e8b` | 56: Scrap value | Label for the scrap value. | id ✓ text ✓ |
| `0x1e8a` |  | Scrap value number, cleared to 0 on build and set to FUN_100e25e8 of the current object on refresh. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Scrap value on this small panel comes from the same accessor as the scrap value in the stat panel. | `0x1016c738` | all | medium | function ✓ |

### Staff list browser (category tabs + list)

Functions: `0x1016c830`, `0x1016cc14`, `0x1016ce88`, `0x1016cf54`, `0x1016cfac`, `0x10172e6c`. Dialog that lists staff members (grouped by five staff-type categories, modes 0-4) in list control 0x1e7b. It has category radio tabs, prev/next arrows, a delete button, a map view that scrolls to the selected member, and an info button. Opening a row or activating it leads to the staff member dialog. The list rows are kept in sync with the park's staff as they change.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1e7b` |  | Main list. Rows are staff members in the current category. Selecting a row scrolls the map view 0x1e84 to that member; a deselect clears the selection. Activating a row (double-click, msg 0x400) opens the rename sub-window via 0x1016e1a4. | id ✓ |
| `0x10` | 57: Name | Column header on the list (child of 0x1e7b). Sort-related command id 0x43 is passed when it is built; the sort effect is not visible in this chunk. | id ✓ text ✓ |
| `0x11` | 58: Energy | Column header on the list (child of 0x1e7b). Sort-related command id 0x44 is passed when it is built. | id ✓ text ✓ |
| `0x1e7c` |  | Container of the five category radio buttons, a child of the list. | id ✓ |
| `0x1e7d` |  | Category radio for mode 0. Selected by the mode setter (0x1016c830) and by the list init (0x1016cc14). | id ✓ |
| `0x1e80` |  | Category radio for mode 1. | id ✓ |
| `0x1e7f` |  | Category radio for mode 2. | id ✓ |
| `0x1e81` |  | Category radio for mode 3. | id ✓ |
| `0x1e7e` |  | Category radio for mode 4. | id ✓ |
| `0x1e82` |  | Title/header control of the window; set from the window template string by the init function. | id ✓ |
| `0x1e84` |  | Map or view area. Selecting a list row centres it on that member's position (0x1016c454). | id ✓ |
| `0x1e85` |  | Arrow that steps the filter mode one way (msg 0x100 with id 0x1e85 calls 0x10146de0). Hidden when fewer than 2 members match the current filter. | id ✓ |
| `0x1e86` |  | Arrow that steps the filter mode the other way (calls 0x10146c80). Hidden under the same condition as 0x1e85. | id ✓ |
| `0x1e7a` |  | Delete/remove button for the selected entry. It calls the shared delete-item handler 0x10147034. With the Confirmations option on it opens the 'DELETE ITEM' box; otherwise it acts at once. | id ✓ |
| `0x1e87` |  | Sell/demolish-style button. It calls the shared handler 0x101473bc, which works on the selected park object. | id ✓ |
| `0x1e88` |  | Opens an info sub-panel for the selected entry (0x1014abe4 with mode 3). | id ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Selecting a staff category (mode 0-4) stores the mode, selects the matching radio, and rebuilds the list. Each mode also feeds a bitmask into the filter counter: mode 0 uses mask 0x2, mode 1 uses 0x4, mode 2 uses 0x10, mode 3 uses 0x8, mode 4 uses 0x20. | `0x1016c830` | all | high | function ✓ |
| On init, the prev/next arrows 0x1e85 and 0x1e86 are hidden unless the current filter matches at least 2 members. The match count comes from the filter helper 0x10146b48, which sets the mode and returns the count of matching entities. | `0x1016cc14` | all | medium | function ✓ |
| The window title is set from control id 0x1e82 via the template string. The list column headers Name and Energy are labelled from UITEXT 0x39 and 0x3a. | `0x1016cc14` | all | medium | function ✓ |
| The list row for a staff member is added or updated only if the member matches the current category (category from the staff-type mapping equals the mode global) and passes a state/key check. The row shows the member's name (from the record at offset 0x1a0) and an energy-like value taken from the float at offset 0x1f8, scaled to a 0-1023 range. | `0x1016ce88` | all | medium | function ✓ |
| When a staff member leaves the current filter or is removed, the matching list row is removed. | `0x1016cf54` | all | medium | function ✓ |
| Delete (0x1e7a): when the Confirmations option is off, the handler centres the view on the selected object's tile and clears the selection. When the option is on, it opens the 'DELETE ITEM' yes/no message (UITEXT 396). The removal happens on confirmation, in code outside this chunk. | `0x1016cfac` | all | medium | function ✓ |
| Command 0x1e87 runs the shared handler 0x101473bc. Command 0x1e88 opens the info sub-panel for mode 3. Close (command -1 or key 5) closes the window through 0x10147358, which sends close code 4. | `0x1016cfac` | all | medium | function ✓ |
| Arrows 0x1e85 and 0x1e86 step the filter mode forward or backward using the mode-stepping helpers, and the list refreshes. Which arrow goes which way is not confirmed. | `0x1016cfac` | all | medium | function ✓ |
| Selecting a list row (msg 0x401) scrolls the map view 0x1e84 to the selected member. Deselect (-1) clears the selection state. | `0x1016cfac` | all | medium | function ✓ |
| Activating a row (msg 0x400) opens the staff rename sub-window for that member (0x1016e1a4). That helper stores the filter mode in a global, opens a small dialog with controls 0x511 and 0x512, and plays UI sound id 0xc0. | `0x1016cfac` | all | low | function ✓ |
| Hover help appears after 2000 ms on the 0x80080 event. Message 0x10 with 0x80080 rebuilds the list rows, walking from the last row back to the first. | `0x1016cfac` | all | low | function ✓ |
| Radio-group setter: selecting child id X deselects the previously selected child and selects X, and updates the stored selection. Selecting -1 clears the selection. It returns the previous index, or 0xffff if nothing changed. | `0x10172e6c` | all | high | function ✓ |

### Staff member detail dialog (info panel, dismiss, pick-up, active toggle)

Functions: `0x1016d430`, `0x1016d91c`, `0x1016db50`, `0x1016dbc4`, `0x1016dd78`, `0x1016e0f0`. Per-staff-member dialog with a stats panel built from the template 'happygrad.wct' (child 0x51x controls, created in 0x1016d430). It shows employment time, wage, status, skill, energy and happiness. It also offers dismiss, pick-up and an active/on-duty checkbox, plus prev/next stepping through the category list.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x520` |  | Title control, set via the title helper 0x10147768 with the window's string. | id ✓ |
| `0x50b` |  | Opens a sub-screen for the member's staff category (0x1014ba4c, category from 0x100f40e8). Its contents are outside this chunk. | id ✓ |
| `0x50c` |  | Dismiss button. Calls 0x1016e0f0, which runs the dismiss flow described in the rules. | id ✗ |
| `0x50d` |  | Prev arrow for staff stepping (mode step 0x10146de0). Hidden in 0x1016db50 when the match count is below 2. | id ✓ |
| `0x50f` |  | Next arrow for staff stepping (mode step 0x10146c80). Hidden under the same condition as 0x50d. | id ✓ |
| `0x50e` |  | Pick-up button. Sets the held-staff global to the selected member and calls the pick-up routine 0x100f380c. It is enabled only when the member can be picked up. | id ✓ |
| `0x510` |  | Active/on-duty checkbox. Checked means the member is not flagged. Unchecking sets the member's flag word to 0x4000 (the off-duty or stopped marker, via 0x100f4e70). Checking runs the hold/select branch and plays sound 0xc1. | id ✓ |
| `0x521` |  | Calls 0x1016a880 with the selected member. Behaviour not resolved in this chunk. | id ✓ |
| `0x51a` | 64: Employed for | Label. Its value is control 0x51b, a formatted time string built from the member's hire timestamp (0x100f3708). | id ✓ text ✓ |
| `0x51b` |  | Value for 'Employed for', formatted as a duration. | id ✓ |
| `0x51d` | 65: Monthly wage | Label. Its value is control 0x51e, the wage amount for the member's staff type (0x100f46bc). | id ✓ text ✓ |
| `0x51e` |  | Value for 'Monthly wage'. | id ✓ |
| `0x516` | 66: Current status | Label. Its value is control 0x517, which shows the member's state category (0x100f4170); state 1 shows as 0 when the flag is set. | id ✓ text ✓ |
| `0x517` |  | Value for 'Current status'. | id ✓ |
| `0x519` | 67: Skill | Label. No value control is clearly linked to it in this chunk. | id ✓ text ✓ |
| `0x51c` | 68: Energy | Label. Its bar is in the 0x513-0x515 set; the exact link is unresolved. | id ✓ text ✓ |
| `0x518` | 69: Happiness | Label. Its bar is in the 0x513-0x515 set; the exact link is unresolved. | id ✓ text ✓ |
| `0x513` |  | Progress bar. Fill comes from the float at member offset 0x1f4, scaled from its low byte to 0-1023. | id ✓ |
| `0x514` |  | Progress bar. Fill comes from helper 0x100f41dc (float at +0x1e4 with byte +0x1e8). | id ✓ |
| `0x515` |  | Progress bar. Fill comes from the float at member offset 0x1f8, scaled the same way. | id ✓ |
| `0x51f` |  | Shown or enabled depending on the member's state category (0x1013892c), via 0x1017f450. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The dismiss button (0x50c) opens the dismiss flow. If the Confirmations option is off, the member is dismissed at once by the dismissal routine 0x100f33ec. If on, the yes/no box 'DISMISS EMPLOYEE' (UITEXT 397) is shown. | `0x1016e0f0` | all | medium | function ✓ |
| The pick-up button (0x50e) is enabled only if the member can be picked up. The check excludes staff categories 4 and 5 (from 0x100f4170), state 0xd or 0x12, and members where 0x100e6c6c returns 0. Picking up sets the held-staff global and calls 0x100f380c. | `0x1016dbc4` | all | medium | function ✓ |
| The active checkbox (0x510) is checked when the member is not flagged. When it is checked, the refresh plays sound id 0xd4. | `0x1016dbc4` | all | medium | function ✓ |
| Unchecking the active checkbox (0x510) sets the flag word to 1 and 0x4000, marking the member as off-duty or stopped, via 0x100f4e70. Checking it runs the hold branch: a new object is created if needed and sound 0xc1 plays. | `0x1016dd78` | all | low | function ✓ |
| Arrows 0x50d and 0x50f step through staff (mode step helpers 0x10146de0 and 0x10146c80) and are hidden when the match count is below 2. | `0x1016db50` | all | medium | function ✓ |
| The panel is built once: label controls are given their UITEXT strings (Employed for, Monthly wage, Current status, Skill, Energy, Happiness), and the value and bar controls are created. Monthly wage comes from the per-type wage helper, and Employed for is a formatted duration. | `0x1016d430` | all | medium | function ✓ |
| The refresh copies the staff member's data into the panel: duration, wage, the three bars, a status value, and control 0x51f's visibility. The status value is 0 when state 1 is combined with the flag word set. | `0x1016d91c` | all | medium | function ✓ |
| The window's command handler routes staff controls: 0x50c dismiss, 0x50d and 0x50f step modes, 0x50e pick-up, 0x510 active toggle, 0x50b open sub-screen, 0x521 unresolved. Close and the 4000 ms hover help are handled too. | `0x1016dd78` | all | medium | function ✓ |

### Rename staff member sub-window

Functions: `0x1016e3e4`. Small dialog to rename a staff member. It opens only when no such sub-window exists yet and the member has a valid staff type.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x5daa` | 195: Rename Staff Member | Label or title for the rename window. | id ✓ text ✓ |
| `0x5dab` |  | Name edit field, initialised with the member's current name (record offset 0x1a0). Its maximum length comes from a global (0x1e plus a value) and is not resolved. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The rename window is created only for staff types 4 to 8, and only when no rename window is already open. The window is shown after it is built. | `0x1016e3e4` | all | medium | function ✓ |

### Scrap or item-stock panel (number owned, scrap value)

Functions: `0x1016e774`, `0x1016e960`, `0x1016ea34`, `0x1016eb20`. Panel for the selected park item type, showing how many are owned and their scrap value. It has delete, sell-type and prev/next controls, plus an info button.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x5211` | 55: Number owned | Label for the count value 0x5210. | id ✓ text ✓ |
| `0x5210` |  | Value for 'Number owned'. Filled by a formatter from the selected object's item type id (offset 0xe). The exact content, count or name, is not clear. | id ✓ |
| `0x520f` | 56: Scrap value | Label for the scrap value 0x520e. | id ✓ text ✓ |
| `0x520e` |  | Value for 'Scrap value'. Computed as the item's base value (table offset 0x1b8) times the owned count, divided by 100. | id ✓ |
| `0x5209` |  | Delete button, shared handler 0x10147034 (same confirm/tile behaviour as the staff list). | id ✓ |
| `0x520a` |  | Prev arrow (0x10146de0). Hidden when fewer than 2 matches. | id ✓ |
| `0x520b` |  | Next arrow (0x10146c80). Hidden under the same condition. | id ✓ |
| `0x5212` |  | Title control, set via the title helper. | id ✓ |
| `0x5213` |  | Opens an info sub-panel (0x1014abe4 with mode 0). | id ✗ |
| `0x5214` |  | Sell/demolish-style button, shared handler 0x101473bc. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Scrap value equals the item's base value multiplied by the number owned, divided by 100. The number owned comes from the park's per-type count. | `0x1016e960` | all | medium | function ✓ |
| Init hides the prev and next arrows unless the filter match count is at least 2. | `0x1016ea34` | all | medium | function ✓ |
| Delete (0x5209) and sell (0x5214) share the handlers 0x10147034 and 0x101473bc, so the Confirmations option and tile removal apply as in the staff list. The info button 0x5213 opens a sub-panel in mode 0. | `0x1016eb20` | all | medium | function ✓ |

### End of Year Summary dialog (singleton)

Functions: `0x1016eeb0`. Builds the 'End Of Year Summary' dialog once. It has a This Year and Last Year column for park value, park rating, money in, money out and bank balance. Values are read from the per-year statistics arrays.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xadb3` | 186: End Of Year Summary | Title of the summary window. | id ✓ text ✓ |
| `0xada1` |  | Container of the summary grid. | id ✓ |
| `0xadae` | 188: This Year | Column header for the first value column. | id ✓ text ✓ |
| `0xadaf` | 187: Last Year | Column header for the second value column. | id ✓ text ✓ |
| `0xada9` | 189: Park value | Row label. Values are 0xada8 (first column) and 0xadac (second column). | id ✓ text ✓ |
| `0xada3` | 190: Park rating | Row label. Values are 0xada2 and 0xadad. | id ✓ text ✓ |
| `0xada4` | 191: Money in | Row label. Values are 0xada7 and 0xadaa. | id ✓ text ✓ |
| `0xada5` | 192: Money out | Row label. Values are 0xada6 and 0xadab. | id ✓ text ✓ |
| `0xadb0` | 193: Bank balance | Row label. Values are 0xadb1 and 0xadb2. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The summary dialog is a singleton: it is created only if the stored handle is zero. Its values are read from the park's per-year statistics arrays, index 0 for one column and index 12 for the other. | `0x1016eeb0` | all | medium | function ✓ |

### Exclusive selection group (radio/tab-style buttons)

Functions: `0x10172f4c`. Generic handler for a parent panel whose child buttons act as a group. It tracks the currently selected child index in the panel's +0x138 field (-1 = none), and sends a notification 0x101 to its parent with the child id and state whenever the selection changes.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `child ids (arbitrary, passed as param_3)` |  | Each child button is pressed/released through message 0x100 (param_3 = child id, param_4 = 1 for press, 0 for release). Pressing a child deselects the previous selection (state 0) and selects the new one (state 1), so only one child is active. |  |
| `0x15 message (key/navigation)` |  | Only handled when the group has flag bit 0x10 set and nothing is selected yet. It looks at the first child and, if that child's value is 2, selects it. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Selection index is stored in the group at +0x138; -1 means nothing is selected. | `0x10172f4c` | all | high | function ✓ |
| The group flag bit 0x10 chooses behaviour. Without it, a release (param_4 = 0) clears the selection and notifies the parent with state 0. With it, a release acts like a press of that child, and it is kept as the selection. | `0x10172f4c` | all | medium | function ✓ |
| Pressing the already-selected child in a press (param_4 = 1) event does nothing extra beyond re-notifying the parent. | `0x10172f4c` | all | medium | function ✓ |
| The handler only acts when the child exists (lookup by id returns non-null) and the panel itself is non-null. Other messages fall through to the base handler FUN_101813d0. | `0x10172f4c` | all | high | function ✓ |

### Bank of up to 31 toggle buttons with a bitmask state

Functions: `0x101768d0`. Sets the on/off state of a bank of 31 buttons from a bitmask. Children have ids 0x11100000 + n for n = 0..30.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x11100000 + n (n = 0..30)` |  | Each button is enabled or updated depending on whether its bit changed. Bit 0->1 sends a state update with value 1 and bit 1->0 sends value 0 (FUN_1017f450). |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The new mask is stored at +0x138 and forwarded to a sub-object via FUN_10182d48 (message 0x80). Return value is always 1. | `0x101768d0` | all | high | function ✓ |
| Only buttons whose bit actually changes are touched. Unchanged bits are skipped. | `0x101768d0` | all | high | function ✓ |
| FUN_1017f450(child, flag) does not forward its flag: the decompile stores the flag in a local and passes an uninitialised stack value. So whether this means show/hide, enable/disable or highlight is unknown. | `0x1017f450` | all | low | function ✓ |

### Scrollable item list box - data model (insert, remove, clear, reset)

Functions: `0x10176d64`, `0x10178768`, `0x10179048`, `0x101792c4`. Storage and maintenance of list items: adding, removing, clearing and reinitialising. The list keeps a linked node pool at +0x144, item count at +0x150, selected index at +0x14c, head at +0x160, scroll top at +0x154, visible rows at +0x172 and fields per row at +0x164 (+4 overhead words).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1 (child id 1)` |  | Scrollbar child of the list. Its range, value and enabled state are updated after every insert, remove, clear and reset (range = count minus visible rows; enabled only when count > visible). |  |
| `0x174 sub-object` |  | Optional attached sub-object that gets value 0 on reset/clear. Its use is not confirmed in this chunk. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Reset (FUN_10176d64) clears the list: selected index = -1, count = 0, scroll top = 0, and sets the scrollbar value to 0 and its range to 0. It sends a redraw (message 3) only when a global 'notify/redraw enabled' flag is set. | `0x10176d64` | all | high | function ✓ |
| Insert (FUN_10178768) grows storage when full: the first allocation reserves 20 items, later growth adds 10 items at a time. | `0x10178768` | all | high | function ✓ |
| Insert places items by the item fields. Field descriptors with flag 0 store an owned copy of the string (allocation and copy), others store the value directly. Index -1 or index >= count appends at the end; a valid index inserts before that item. | `0x10178768` | all | medium | function ✓ |
| When flag bit 0x10 is set, insert uses an ordered search: a comparator (FUN_10178654) chooses the position among existing items, so the list is kept in sorted order. | `0x10178768` | all | medium | function ✓ |
| Inserting into an empty list selects the first item (selection set to 0). | `0x10178768` | all | high | function ✓ |
| After insert, the scrollbar range becomes count minus visible rows, and the parent gets a 0x405 notification carrying the first and last visible item indices. | `0x10178768` | all | medium | function ✓ |
| Remove (FUN_10179048) fails if the index is negative, >= count, or the list has no storage. On success it unlinks the item, and adjusts the selection: if count drops below 1 the selection becomes -1, and if the removed item was at or before the selection it moves down by one. | `0x10179048` | all | medium | function ✓ |
| Remove redraws only if the removed row is in the visible window and the redraw flag is set, then refreshes the scrollbar and relayouts. Returns 1 on success. | `0x10179048` | all | medium | function ✓ |
| Clear all (FUN_101792c4) frees every owned string (FUN_1010e1ac) and rebuilds the node pool as a free chain. It resets count to 0, selection to -1, head to -1, scroll to 0, clears the attached sub-object, and relayouts. It does nothing when the list is already empty. | `0x101792c4` | all | high | function ✓ |

### Scrollable item list box - mouse and hover input

Functions: `0x10176e2c`. Pointer handling for the list body. It maps a pointer's vertical position to a row index (row offset plus scroll top), tracks a hover row, and sends notifications to the parent.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x28 sub-object (hit rect)` |  | Hit-test rectangle for the list body. Pointer events must fall inside it. |  |
| `0x138 sub-object` |  | Rectangle-like sub-object that receives set-size/bounds updates via message 0x13. Its offset is shared with other classes that use it for a different purpose, so the meaning here is low confidence. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Hover row = (pointer y - origin) divided by row height, plus scroll top, valid only if the row index is below the visible-row count and below the item count. Otherwise the hover is -1. | `0x10176e2c` | all | high | function ✓ |
| When the hover row changes: if it is now valid, the parent receives notify 0x201 with the list's id; then a redraw is requested if the redraw flag is set. | `0x10176e2c` | all | medium | function ✓ |
| Mouse-drag tracking (flag 0x80 set) only updates hover while the pointer is inside the body rectangle. Without the flag, the same hover logic runs on message 0x11005..0x11007. | `0x10176e2c` | all | medium | function ✓ |
| Scroll-top value received from scrollbar child 1 (message 0x800) updates the list's top index, and a redraw is requested if the flag is set. | `0x10176e2c` | all | medium | function ✓ |
| Message 0x11007 (release) notifies the parent with 0x200 when a hover row exists. Message 0x13 sets bounds on the +0x138 sub-object. | `0x10176e2c` | all | medium | function ✓ |

### Scrollable item list box - row layout and scroll-to-item

Functions: `0x101781c0`, `0x1017a194`. Recalculate visible rows and refill row controls, and set the selection/scroll position so a chosen item is visible.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1 (scrollbar)` |  | Its range is set to the item count, and its value follows the scroll top. |  |
| `0x174 sub-object` |  | Optional attached sub-object that receives the visible-data buffer, or a value of 1 or 0 for enabled state. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Visible row count = (list bottom - list top) divided by the row height (both as shorts), and the per-row control array is allocated the first time it is needed. | `0x101781c0` | all | medium | function ✓ |
| Rows are refilled from the last visible row back to the first, and the layout is recalculated afterwards. | `0x101781c0` | all | medium | function ✓ |
| Scroll-to-item (FUN_1017a194) with a non-negative index: clamps the index to count minus 1; if it is outside the visible window, moves the scroll top so the item is visible (top clamped to count minus visible). Then sets the selection relative to the top, relayouts, and sets the scrollbar value. A 0x405 notify goes to the parent if the top changed. | `0x1017a194` | all | high | function ✓ |
| Negative index (FUN_1017a194) deselects the list. | `0x1017a194` | all | high | function ✓ |
| FUN_1017a194 does nothing when the list has no storage (+0x144 is 0). | `0x1017a194` | all | high | function ✓ |

### Scrollable item list box - message dispatcher

Functions: `0x1017a5fc`, `0x1017a444`, `0x10179fb8`. Main message handler for the list: keyboard navigation, row presses, scrollbar changes, enable-state propagation and the mouse wheel/drag paths. 0x1017a444 handles row presses and 0x10179fb8 is the click handler called from 0x11006.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x10 + row index` |  | Row child ids. A press message 0x100 carries 0x10 + row and is converted to a row index (row = id - 0x10). |  |
| `0x1 (scrollbar)` |  | Scroll changes (0x800) are received from it, and it gets delta changes via 0x1000d. |  |
| `0x174 sub-object` |  | Gets an enabled or disabled-style state from the selection: on when a row is selected and enabled. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Message 0x800 from the scrollbar sets the top index to its value; a 0x405 notify and a redraw follow if the flag is set. No change means no action. | `0x1017a5fc` | all | high | function ✓ |
| Message 0x11 (enable state push) enables each visible row control if its data index is below the item count and the flag parameter is non-zero. The +0x174 sub-object is enabled when a row is selected and the flag is non-zero, otherwise disabled. | `0x1017a5fc` | all | medium | function ✓ |
| Keyboard navigation (message 0x1000a, only when flag 0x200 is set): codes 0x2600 = previous row, 0x2800 = next row, 0x2400 = first row, 0x2300 = last row, implemented via FUN_1017a194. The exact code-to-key mapping is inferred. | `0x1017a5fc` | all | low | function ✓ |
| Message 0x100 from a row (row = id - 0x10) selects that row only when flag 0x10 is set and the row index is valid. | `0x1017a444` | all | medium | function ✓ |
| Message 0x13 forwards bounds to the list's +0x138 sub-object and to every row control in the row array. | `0x1017a5fc` | all | medium | function ✓ |
| Pointer click (0x11006 with param 0) calls the click handler 0x10179fb8, which selects the row under the pointer. With press and flag 0x80, the row under the pointer is selected and a 0x402 notify goes to the parent when a hover row exists. Message 0x11007 (release) sends notify 0x400 when a row is selected. | `0x1017a5fc` | all | medium | function ✓ |
| Mouse wheel or drag delta (0x1000d) subtracts the delta from the scrollbar value. | `0x1017a5fc` | all | medium | function ✓ |
| The list has no game-type check (no reference to the global game type). Any Instant Action vs Full Simulation differences would come from the caller. | `0x1017a5fc` | all | high | function ✓ |

### Slider with track, thumb and arrow buttons (value bar with step/page)

Functions: `0x1017b634`, `0x1017b724`, `0x1017bc9c`, `0x1017bd80`. A horizontal or vertical value control with a draggable thumb, page-step track clicks, and arrow buttons. The value is stored in an integer range with a step and a page size. It pushes its position to two sub-objects as a fraction.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x3 (child id 3)` |  | Thumb/track sub-object. Its rectangle is used to map the pointer to a value (drag) and to detect whether a track click is before or after the thumb. |  |
| `0x100 arrow ids 1 and 2` |  | Arrow buttons send message 0x100 with id 1 (step up) or 2 (step down). Inferred from the handler. |  |
| `0x11006 (track click)` |  | Clicking the track moves by one page toward the click position. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Value fields: min at +0x13c, max at +0x138, current value at +0x140, step at +0x144 (signed, sign gives direction), page at +0x148. Flag bit 0x2 at +0x14c selects the vertical axis. | `0x1017b724` | all | medium | function ✓ |
| Step modes for the set-value function: 1 = +step, 0 = -step, 2 = +page, 3 = -page, -1 = set to the absolute argument, -2 = keep current value and force update. | `0x1017b724` | all | high | function ✓ |
| Value is clamped between min and max. When the step is positive the clamp is min..max, and when the step is negative (inverted) the clamp is reversed. | `0x1017b724` | all | high | function ✓ |
| When the value changes, the thumb is repositioned and the value is sent to two sub-objects (+0x94 and +0x98) as a 0..1024 fraction of the range, plus a 0x80 update to a further sub-object. A value-changed notify (0x800) goes to the parent. | `0x1017b724` | all | high | function ✓ |
| Dragging the thumb (0x1017b634) maps the pointer position to a value between min and max along the active axis, and a 0x800 notify goes to the parent when the value changes. | `0x1017b634` | all | high | function ✓ |
| Track click (0x1017bc9c) takes a page step: if the click is on the far side of the thumb it steps toward it (2 or 3 depending on direction). For inverted orientation (step > 0) the direction is swapped. | `0x1017bc9c` | all | medium | function ✓ |
| Message 0x100 on the slider routes arrow ids: id 2 gives a step down (mode 0), id 1 gives a step up (mode 1). | `0x1017bd80` | all | medium | function ✓ |
| Message 0x1000d subtracts the delta from the slider value directly (no clamping in this function). | `0x1017bd80` | all | medium | function ✓ |
| Message 3 (refresh) on the slider forwards to the sub-tree refresh (FUN_1017e5c0) on child 3, which redraws enabled descendants. | `0x1017bd80` | all | medium | function ✓ |
| The slider has no game-type check. | `0x1017bd80` | all | high | function ✓ |

### Shared spin/scroll message handling

Functions: `0x1017d9d8`, `0x1017f770`, `0x1017d530`. Generic control message translator used by numeric step/scroll style controls, plus the child-lookup helper used everywhere in the UI.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `child lookup` |  | FUN_1017f770(parent,id) walks the parent's child list and returns the child whose id field matches; returns 0 when absent. |  |
| `ids 1 and 2 under the handled parent` |  | Message 0x15 writes a fixed target value (200) and position (50) into both of these child controls. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Message 0x100 with parameter 2 sends action 0, with parameter 1 sends action 1 to the step helper. Message 0x10008 maps parameter 2 to action 4 and parameter 1 to action 5. Message 0x10009 maps parameter 2 to action 2 and parameter 1 to action 3. Other message ids in that range are ignored. | `0x1017d9d8` | all | high | function ✓ |
| Message 0x1000d adds the event's delta argument to the control's current value (read and write through the generic value accessor). | `0x1017d9d8` | all | high | function ✓ |
| Message 0x15 sets the target value of children 1 and 2 to 200 and their current position to 50, presumably a reset/initialise of two scroll or slider parts. | `0x1017d9d8` | all | low | function ✓ |
| Step helper: action 1 moves the position forward by the step size (+0x148) clamped to the min (+0x138) and the target (+0x13c); if the position changes it sends a 0x800 notification to the parent with the new value. Action 3 also sets an acceleration of 5, or 20 when the step size is 1 (sticky repeat). | `0x1017d530` | all | medium | function ✓ |
| Message handling ends by passing the event to the default handler, so the rules above only add behaviour on top of the default path. | `0x1017d9d8` | all | medium | function ✓ |

### Online connection status box

Functions: `0x10182e00`, `0x10182fa0`. A small modal status popup that shows a progress message (UITEXT id) while an online request runs, for example connecting to the news server.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x2241` |  | The message label in the status popup. Its text is the UITEXT entry given to FUN_10182fa0 (or a text passed directly to FUN_10182e00). Font index 6. | id ✓ |
| `0x19c-sized sub-object` |  | Helper sub-object attached to the label (created with a 0x19c-byte allocation), not interpreted further. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The popup is created once and rebuilt only when the requested message id changes; otherwise only the label text is replaced. | `0x10182fa0` | online | high | function ✓, game-type test ✗ |
| The text shown is the UITEXT entry for the id: 0xf4 Connecting to News Server, 0xf5 Getting Server Details, 0xf6 Getting Game News, 0xf7 Getting System News, 0xf9 Getting City List, 0xfa Getting Top 10 Park List. Other callers pass other ids (0xfb to 0x102 and more, not in this chunk). | `0x10182fa0` | online | high | function ✓, game-type test ✗ |
| The popup is parented to the main window (global _DAT_101ed1bc). Its label is configured with font 6 and colour/layout set from the window's style table. | `0x10182e00` | online | medium | function ✓, game-type test ✗ |
| When the label text is set without an id, it writes the given string directly to the label without rebuilding. | `0x10182e00` | online | high | function ✓, game-type test ✗ |

### Item detail overlay

Functions: `0x10183274`, `0x101834a0`. An overlay that shows the name and number of the currently selected item (for example a ride or a park) and an icon, created once and filled when the selection changes.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x49d9` |  | Name label. Font 5. Its text is the selected item's name (wide string stored in the item's sub-object at +0x14). | id ✓ |
| `0x49da` |  | Index label. Its text is a formatted number: the item's index (+0x24) plus one, through a format string. | id ✓ |
| `0x4c-sized render helper` |  | Picture helper drawn when the item's flag at +0x25 is set (not interpreted further). |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The overlay is created only when the screen's parent (global at +4 of the current screen object) exists; both labels are created hidden-by-default and later shown or hidden by the update function. | `0x10183274` | all | medium | function ✓ |
| With no selected item (null or with no sub-object) both labels are hidden. With a selection, both are shown, the index label is formatted with index+1, and the name is copied from the sub-object's wide string. The picture is drawn if the item's flag is set. | `0x101834a0` | all | high | function ✓ |

### Park/browse panel with count label and scroll slider (lobby list)

Functions: `0x1018395c`, `0x10183cc0`, `0x10183dd0`, `0x10184028`, `0x10184410`. A lobby panel that shows a list-browse control (slider 0x1e0eb inside container 0x1e0ea), a count label (0x1e0ee) and a caption (0x1e0ef). It appears in Full Simulation and online modes; the group is hidden in Instant Action. The exact screen identity is not certain (see unknowns).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x1e0ec` |  | Root panel of the count group; shown or hidden by 0x10184028, and enabled together with 0x1e0ea by 0x10184410. | id ✓ |
| `0x1e0ea` |  | Container of the slider (0x1e0eb). Hidden in Instant Action. Its initial value is 1. | id ✓ |
| `0x1e0eb` |  | Slider or scroll control inside the container. Its value is set to a limit-based number by 0x10183dd0 and 0x10184028. Initial value 5. | id ✓ |
| `0x1e0ed` |  | Sub-control with initial value 3. | id ✓ |
| `0x1e0ee` |  | Count text, formatted from the current total with a format string from the string table (not a UITEXT id). | id ✓ |
| `0x1e0ef` |  | Caption whose text is copied from a global string pointer set at creation (not resolved). | id ✓ |
| `0x1e0f0 and 0x1e0f1` |  | Two controls whose value is set to 0 or 1 together with the visibility of 0x1e0ea and 0x1e0ec (0x10184410). Probably the on/off or arrow parts of the count group. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| In Instant Action the whole count group is hidden: container 0x1e0ea and root 0x1e0ec are hidden and the two helper controls are turned off. | `0x10184028` | instant_action | high | function ✓, game-type test ✓ |
| In Full Simulation and online, the group is shown when the computed total is at least 1; the count label then shows the total formatted with the string-table format. When the total is zero, the root panel is hidden. | `0x10184028` | not_instant_action | medium | function ✓, game-type test ✓ |
| In the item lookup branch, Instant Action hides the container 0x1e0ea and does no slider update. Other modes show it and compare the total with the per-item limit: if total is below the limit the slider value is limit+4 (and 0x1e0ea is fixed to 1), else the slider value is limit-1 and a looping sound is started (sound id 0x61, panned to the screen position of the panel). When the total is at or above the limit, the looping sound is stopped. | `0x10183dd0` | all | medium | function ✓ |
| Click sound: when not Instant Action, a one-shot click (sound id 0x62, fixed volume 50000) is played panned to the horizontal centre of the 0x1e0ea rectangle. Instant Action plays nothing. | `0x10183cc0` | all | medium | function ✓ |
| The helper sets 0x1e0f0, 0x1e0f1 and enables or disables 0x1e0ea and 0x1e0ec with the same boolean argument. | `0x10184410` | all | high | function ✓ |
| Creation builds the panel once (guarded by the global panel pointer), then calls the update routine, sets the initial values (1, 3, 5), and calls the button-state helper of the lobby. | `0x1018395c` | all | high | function ✓ |

### Selected-entry detail labels

Functions: `0x101846a8`, `0x10184570`. Two labels showing the currently selected entry's name and a formatted count, updated when the selection changes.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x16c11` |  | Name label for the selected entry, text from the entry's name at +0x14. Cleared when nothing is selected. | id ✓ |
| `0x16c12` |  | Detail label. Its text is a formatted string built from the entry's +0x40 value with string id 0x1ce (UITEXT 0x1ce reads 'Parks: ', so probably 'Parks: N', but this is not confirmed). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The update does nothing when the selection is unchanged (compared with the last stored selection). | `0x10184570` | all | high | function ✓ |
| Selecting nothing clears both labels; selecting an entry sets the name and the formatted detail. | `0x10184570` | all | high | function ✓ |
| Creation builds both labels with a shared style and no game-type checks. | `0x101846a8` | all | medium | function ✓ |

### Online login dialog

Functions: `0x10184b48`, `0x10184924`, `0x10184e78`. Modal dialog for logging in to the online service with a login name, password and a Login button, with keyboard handling between the two fields.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x49` | 237: Login name | Text entry field for the login name, limited to 16 characters, default focus. The field's text is initialised from a global string. | id ✓ text ✓ |
| `0x4a` | 237: Login name | Label for the name field. | id ✓ text ✓ |
| `0x4b` | 238: Password | Password text entry field, limited to 16 characters. | id ✓ text ✓ |
| `0x4c` | 238: Password | Label for the password field. | id ✓ text ✓ |
| `0x4d` | 236: Login | Login button, the default button of the dialog. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Login name and password are text entry fields with a 16-character limit, default focus on the name field. | `0x10184b48` | online | high | function ✓, game-type test ✗ |
| Message 0x803 (focus switch): from the name field focus moves to the password field, otherwise focus moves to the name field. | `0x10184924` | online | medium | function ✓, game-type test ✗ |
| Message 0x802 (Enter): on the name field it moves focus to the password field; from the password field it sends command -1 to the parent (presumably a login attempt, not confirmed). | `0x10184924` | online | medium | function ✓, game-type test ✗ |
| Command 0x100 with -1 runs a helper with the dialog's two text values (presumably submitting the login); with -2 or -3 it calls 0x10184e78 (close the dialog) and reports handled. | `0x10184924` | online | low | function ✓, game-type test ✗ |
| Message 0x804 closes the dialog through 0x10184e78 and reports handled. | `0x10184924` | online | medium | function ✓, game-type test ✗ |
| Message 0x15 marks the dialog open and sets the input-lock flag; message 0x14 closes it, clears the stored dialog pointer and clears the input-lock flag. | `0x10184924` | online | low | function ✓, game-type test ✗ |
| The dialog is built only when the online session object at +0x60 is zero, which is the first-open case of the calling screen; the login dialog is not used when the online session already exists. | `0x10184b48` | online | low | function ✓, game-type test ✗ |
| Login dialog labels are built with the UITEXT strings for Login name, Password and Login. The 'Login name' text is also written to the 0x4a label. | `0x10184b48` | online | high | function ✓, game-type test ✗ |

### Online news panel

Functions: `0x1018539c`, `0x10184f18`. News panel showing Game News and System News from the online server, with a header 'News from SimThemePark.com'. Text is filled from the news store, with a scroll bar shown when the text overflows.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x5b7f` |  | News body text box. Filled with the chosen news text, wrapped to the box width. | id ✓ |
| `0x5b80` |  | Scroll bar for the news body, shown and ranged only when the text is taller than the box. | id ✓ |
| `0x5b81` |  | Sub-panel for the first news column (child 0x5b82 labelled Game News). | id ✓ |
| `0x5b82` | 268: Game News | Header of the Game News column. | id ✓ text ✓ |
| `0x5b83` | 269: System News | Header of the System News column. | id ✓ text ✓ |
| `0x5b84` | 267: News from SimThemePark.com | Header label for the whole news panel. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The panel is built once, only when the screen object exists and the news store is ready; it then fills the news text. | `0x1018539c` | online | high | function ✓, game-type test ✗ |
| The panel is built only from the online news loader, after the news connection is released and when the news panel has not yet been built (flag at +0x70). | `0x1018539c` | online | high | function ✓, game-type test ✗ |
| The news body text is taken from one of two news store fields (+0xbc or +0xa0), chosen by a global flag; the text is measured and wrapped to the box width. The scroll bar shows when the text height exceeds the box. | `0x10184f18` | online | medium | function ✓, game-type test ✗ |
| Scroll position is reset to the target from the measured height when the scroll bar is shown and the text is not yet scrolled. | `0x10184f18` | online | low | function ✓, game-type test ✗ |
| No game-type check in this panel; it is only built online. | `0x1018539c` | online | medium | function ✓, game-type test ✗ |

### Park details and list screen (stats list, preview, description)

Functions: `0x10185768`, `0x10185d5c`, `0x10185a18`, `0x10185ba0`, `0x10186018`, `0x101856b4`. Screen with a sortable list (0xee8) of entries showing three numeric stats each, a description box (0xee2) with overflow scroll bar (0xee3), a preview picture (0xee4), and a lobby button (0xbf432) whose state depends on the object at +0x8c.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xee8` |  | List of entries. Each row shows the entry name and three numbers from the entry's +0x80, +0x84 and +0x88 fields, formatted with the string table. | id ✓ |
| `0xee2` |  | Description text box. Its text is set from the selected entry. | id ✓ |
| `0xee3` |  | Scroll bar beside the description; shown and sized when the text is taller than the box. | id ✓ |
| `0xee4` |  | Preview picture area. Loaded from the entry's image data. | id ✓ |
| `0xbf432` |  | Button on the root window; enabled when the object at +0x8c of the state object is non-zero, disabled otherwise. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The list is rebuilt from the entries list: for each entry, the name and the three numbers are formatted into a row and added, until the iterator reports no more entries. | `0x10185768` | all | high | function ✓ |
| Clicking a sort column (called with parameter 2 or 3) toggles that column's sort flag (0x1000000 or 0x2000000) and clears the other one; parameters 0 and 1 do nothing. The header indicators are set from the new flags and the list is re-sorted. | `0x10185d5c` | all | medium | function ✓ |
| The description box text comes from the selected entry. If the wrapped text is taller than the box, the scroll bar is shown and its range set; otherwise it is hidden. | `0x10185a18` | all | high | function ✓ |
| The preview picture is decoded from the entry's image data and drawn into the 0xee4 area. The drawing mode depends on a global display-setting value: 2 or 4 uses one mode, 3 or 5 another, and other values draw nothing. | `0x10185ba0` | all | medium | function ✓ |
| Button 0xbf432 is enabled when the state object's field at +0x8c is non-zero and disabled otherwise; the dispatcher turns it off on message 0x14 when that field is zero. | `0x101856b4` | all | high | function ✓ |
| Message 0x1e forwards to a helper that posts a notification only when a stored value changed; message 0x14 clears a pointer and disables button 0xbf432 when the state field is zero; all other messages go to the default handler. | `0x10186018` | all | medium | function ✓ |

### Unsent Postcards screen - command and list event handler

Functions: `0x101862ac`, `0x10185d5c`, `0x10185e94`. Window message handler for the postcard screen. Handles bulk mark commands (mark all send, mark all delete, clear all), the Send action, list row selection, and per-row checkbox toggles.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xee5` |  | Command: sets the send flag on every row in list 0xee8 and clears its delete flag, then refreshes the list. Label not set in this chunk. | id ✓ |
| `0xee6` |  | Command: sets the delete flag on every row and clears its send flag, then refreshes the list. | id ✗ |
| `0xee7` |  | Command: clears both send and delete flags on every row, then refreshes the list. | id ✓ |
| `-1 (0xffffffff)` |  | Likely the main Send action. Counts rows marked for sending. Blocks with a message if the count is above zero and the gate FUN_1012d600 on object 0x404 fails. Otherwise it closes the window (FUN_10170f98 type 4), calls FUN_101860f4 to start the send overlay (only when the count is above zero), and then runs FUN_1012d9f0 on object 0x404. |  |
| `0x401 list select` |  | A list item event on list 'param_3' with a valid row index. Opens that postcard through FUN_10185908, then FUN_10185a18 and FUN_10185ba0 (not read in this chunk). |  |
| `0x404 cell click` |  | Calls FUN_10185d5c with the clicked column index (param_4). Only columns 2 and 3 do anything. |  |
| `message 0x14` |  | Not handled in this function; handled in FUN_10187a98 (close path). |  |
| `message 0x15` |  | Calls FUN_10170f98 on the child window 0x15 with mode 6, value 0. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Row mark bits are mutually exclusive: a row is marked for sending or for deletion, never both. Marking one clears the other. | `0x10185d5c` | all | high | function ✓ |
| Clicking the send checkbox column (2) toggles the send mark on that row; clicking the delete column (3) toggles the delete mark. Columns 0 and 1 are ignored. | `0x10185d5c` | all | high | function ✓ |
| The Send action does not block when nothing is marked. It still closes and runs the outbox process; only the progress overlay is skipped. | `0x101862ac` | all | medium | function ✓ |
| If there is at least one marked postcard and the outbox gate FUN_1012d600 returns 0, the Send action is blocked and message 0x4e is shown. | `0x101862ac` | unknown | low | function ✓ |
| The postcard count for the send gate sums, per row, the result of FUN_10185e94, which returns 1 when the send bit is set. | `0x101862ac, 0x10185e94` | all | medium | function ✓ |
| Bulk mark commands apply to every row and refresh the list. They do not check game type. | `0x101862ac` | all | high | function ✓ |

### Outbox sending overlay

Functions: `0x101860f4`. Starts or restarts the 'sending mails from outbox' overlay. When there is something to send it shows a progress message. Otherwise it only initialises state.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xbf432 (under a parent window)` |  | Hidden via FUN_1017fa64 with 0 when the send start fails and the outbox object's +0x8c flag is 0. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| When the send is started with a non-zero count, the overlay text is UITEXT 0x13a 'Sending mails from outbox, please wait ...' and the message is shown with FUN_10190270. | `0x101860f4` | all | high | function ✓ |
| The recipient or park name used in the send start is the global name string at a local value+0xc, with a fallback default string and a fixed max length of 0x19 when empty. | `0x101860f4` | unknown | low | function ✓ |
| If the start fails and the flag at +0x8c is non-zero, the code shows message 0x39. If the flag is zero, it hides control 0xbf432 instead. | `0x101860f4` | unknown | low | function ✓ |

### Unsent Postcards (outbox) screen - builder

Functions: `0x10186a84`. Builds the 'Unsent Postcards' screen the first time it is opened. Only builds if the object at global state +0x8c is non-zero (gate). Creates the window from the main window handle, a 0xee2 panel, a 0xee4 container, the 0xee8 postcard list with four columns, the 0xee9 title, and the list column headers. Then populates the list and shows the window.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xee8` |  | Main postcard list. Four columns: 0 = title, 1 = date, 2 = 'send' checkbox (narrow), 3 = 'delete' checkbox (narrow). Rows carry bit flags: 0x1000000 = marked for sending, 0x2000000 = marked for deletion. | id ✓ |
| `0xee9` | 309: Unsent Postcards | Screen title text (set via FUN_1013876c). | id ✓ text ✓ |
| `0x10` | 310: Title | Column header button on list 0xee8; tooltip help 391 'Click to sort the list by message title'. Sort action is not in this chunk. | id ✓ text ✓ |
| `0x11` | 311: Date | Column header button on list 0xee8; tooltip help 392 'Click to sort the list by date'. | id ✓ text ✓ |
| `0x12` | 312: Send | Column header button on list 0xee8; tooltip help 393 'Click to sort the list on which postcards are marked for sending'. | id ✓ text ✓ |
| `0x13` | 313: Delete | Column header button on list 0xee8; tooltip help 394 'Click to sort the list on which postcards are marked for deletion'. | id ✓ text ✓ |
| `0xee2` |  | Panel created by the builder and given a 0x19c-byte sub-object. Exact role not clear from this chunk. | id ✓ |
| `0xee4` |  | Container given a 0xdc-byte sub-object. Exact role not clear from this chunk. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The screen is only built when the outbox object's flag at +0x8c is non-zero; otherwise the builder returns 0 and nothing is shown. | `0x10186a84` | unknown | medium | function ✓ |
| Column 2 (send) and column 3 (delete) are created as narrow flagged columns; columns 0 and 1 are normal width. | `0x10186a84` | all | medium | function ✓ |
| The header tooltips and labels are the UITEXT strings Title, Date, Send, Delete and the UIHELPTEXT sort tooltips for ids 0x187 to 0x18a. | `0x10186a84` | all | high | function ✓ |

### Postcard list selection and button-state helpers

Functions: `0x10187110`, `0x101879a0`, `0x10187a20`. Helper routines that update the enabled and value state of the postcard action buttons based on whether a postcard is selected, and set a numeric value on a control in the detail panel.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xc3123 > 0xc3124 / 0xc3128` |  | Paths used by these helpers to reach the detail panel controls. 0xc3128 is enabled or disabled by 0x101879a0 via FUN_101722a0. |  |
| `0xc3123 > 0xc3124 > 0xc3129` |  | Set to a numeric value by 0x10187a20 (value 1 when the low byte of the arg is 0, otherwise 0). |  |
| `0xc3123 > 0xc312b / 0xc312c` |  | Enabled or focused by 0x10187110 based on the selection flag. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| A selection flag (global byte _DAT_101f00ec, called 'selection flag' here) gates the action controls. When set, the controls at 0xc312b and 0xc312c are updated and the window gets the inverse value. | `0x10187110` | all | low | function ✓ |
| When the selection flag is set and no other flag (_DAT_101f00f0) is set, and the sub-state value is 9, the cursor is set to state 0x13. The sub-state value comes from FUN_101c7cac. | `0x10187110` | unknown | low | function ✓ |
| 0x101879a0 enables or disables the 0xc3128 toggle according to its argument (flag == 1) and sets the second parameter to 1. | `0x101879a0` | all | medium | function ✓ |

### Postcard screen main input and event handler

Functions: `0x10187a98`. Large per-window handler for the postcard screen. Handles window close, focus, mouse press/release, click-to-select of a postcard item, key presses (Tab, Enter, '/', 's'), wheel scrolling, and the send and cancel paths.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xee8 (via window handle global)` |  | Main list. Click-select logic uses its item index. |  |
| `0xc312c` |  | Receives focus when '/' is pressed and receives command 0x1000c with key 0x2f. | id ✓ |
| `0xc3124 / 0xc3123 subtree` |  | Hidden or focus-cleared on some key and click paths. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Click-release (0x1e) on a list item only arms a pick state when a pending press flag is set and the item is of type 3 with a matching owner id. For the arm check, a non-zero sub-struct at +0x7a8 must also be type 3 with flag 0x10. | `0x10187a98` | all | low | function ✓ |
| The pick state (0..4) selects the tip text: state 2 or 4 uses formatted string id 0x244 with the item name, state 3 uses 0x243, state 1 uses 0x191, state 0 clears the tip. Cursor 0x13 is set when armed or 0 when cleared. | `0x10187a98` | all | low | function ✓ |
| Key '/' (0x2f) with no modifier while the mode flag is set focuses the input control 0xc312c and posts command 0x1000c with key 0x2f. Otherwise it forwards the key to the text object. | `0x10187a98` | all | medium | function ✓ |
| Key 's' (0x73) with no modifier and a clear global flag (_toc[-0xf70]) toggles the panel state: the selection flag and the panel visibility are both refreshed, using control ids 0xc3123 and the list on 0xc3124. | `0x10187a98` | all | low | function ✓ |
| Tab (9) and Enter (13) are forwarded to the same path as '/', so they route the key to the 0xc3123 panel when the mode flag is set. | `0x10187a98` | all | low | function ✓ |
| Mouse wheel (0x10007) scrolls the postcard view by the scaled delta (two global scale shorts), via FUN_1006ba7c. | `0x10187a98` | all | low | function ✓ |
| Window close (0x14) clears the window global, calls FUN_10141e10 to release it, and hides three windows (type 4). | `0x10187a98` | all | medium | function ✓ |
| Message 0x11 (sub-code 1) sets the selection flag when the event is on the postcard window; sub-code 0 clears it. | `0x10187a98` | all | low | function ✓ |
| Messages 0x10005 (sub-code 1) and 0x10004 claim and release a global focus owner slot via FUN_10180278 and FUN_1018029c. | `0x10187a98` | all | low | function ✓ |

### Postcard composer / secondary panel input handler

Functions: `0x101887b0`. Second window handler, used with the postcard panel. Handles focus gain and loss, Enter to submit a composed message, Tab, the 's' toggle, and generic key-to-list actions.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xee8 window (via global)` |  | Focus events are handled for this window. |  |
| `default text (ppuVar12[-0xf7f])` |  | Used as the default text on the control after Enter submit and as the reset text. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Message 0x1f (gained focus) sets a global flag to 1 and message 0x20 (lost focus) sets it to 0. | `0x101887b0` | all | medium | function ✓ |
| Enter with no modifier builds a message from a template object, submits it via FUN_101acee8 (on a lazily created object of size 0xe8c), destroys the temporary record and resets the control text to the default. It returns the consumed state. | `0x101887b0` | all | medium | function ✓ |
| Tab (9) removes focus from the control (FUN_10180368) and clears the focus owner on the 0xc3123 subtree (FUN_101802e0). | `0x101887b0` | all | medium | function ✓ |
| Key 's' with no modifier toggles the panel when the global flag _DAT_101f00f0 is clear, updating the 0xc3123 list and the cursor. | `0x101887b0` | all | low | function ✓ |
| Other keys go through a key table (FUN_10114a60). If the lookup returns 0, the code adds an item to the state list (FUN_1006cbdc, FUN_1006bab4), refreshes focus, and consumes the key. Otherwise it calls FUN_10198514(0) (likely a beep or no-op). | `0x101887b0` | all | low | function ✓ |

### Panel 0xc3123 command and key handler

Functions: `0x10188d14`. Handles command and event messages for the postcard detail panel (0xc3123 subtree): a toggle, a clear control, an unknown control 0xc312a, and a suspend flag set by a special 0x101 event.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xc3129` |  | Command: calls FUN_101ad860 on the 0xe8c object and sets the control value to 0 via FUN_101c7cac. | id ✓ |
| `0xc3128` |  | Command: calls FUN_101ade2c when param_4 == 1, otherwise FUN_101adee4 (two modes of the same object). | id ✗ |
| `0xc312a` |  | Command: calls FUN_1015f67c (error or unknown path). | id ✗ |
| `0xc3126` |  | Event 0x101 with this id (0xc0000 bias) clears the suspend flag _DAT_101f00ac and calls FUN_10189518. Other ids set the flag and call FUN_10189758. | id ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The suspend flag _DAT_101f00ac is set to 1 for any 0x101 event except when the control id is 0xc3126 (which clears it). While set, the list helpers in the 0xc3123 subtree do nothing. | `0x10188d14` | all | medium | function ✓ |

### Detail panel text wrapping and list helpers (0xc3123 subtree)

Functions: `0x1018911c`, `0x1018935c`, `0x101893e8`, `0x10189474`. Helpers for the message body and item lists in the 0xc3123 panel: wrapping a wide string into lines, adding a row, removing a row by id, and updating a row. All are no-ops when the window is missing or the suspend flag is set.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xc3123 > 0xc312b` |  | Multi-line text box. Wrapping inserts one row per line and trims extra rows from the top so the count stays within the global line limit. |  |
| `0xc3123 > 0xc3124` |  | List of entries. The add, remove and update helpers operate on this list. |  |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Text is wrapped to the pixel width of the 0xc312b box, measured per glyph with FUN_101c7cac, into lines that are inserted as rows. The row count is capped by the global value _DAT_101ede88; excess rows are removed from the top, then the list is scrolled to the bottom. | `0x1018911c` | all | medium | function ✓ |
| Add row, remove row by id and update row by id on list 0xc3124 each return early when the window is missing or the suspend flag _DAT_101f00ac is set. | `0x1018935c, 0x101893e8, 0x10189474` | all | medium | function ✓ |

### World overlay list and hover bubble

Functions: `0x10189E64`, `0x10189BAC`, `0x10189518`, `0x10189758`, `0x1018959C`, `0x10189628`, `0x101896B4`. Initialises and manages an in-game overlay list (ids 0xc3123 and 0xc3124) and a hover info bubble with two variants. The list is the main overlay list, and the bubble is anchored to a list row.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xc3123` |  | Outer overlay list. | id ✓ |
| `0xc3124` |  | Inner list. Rows are added, removed and updated by the functions below. | id ✓ |
| `0xc3125` |  | Tab or selection control inside the overlay, set by id 0xc3126. | id ✓ |
| `0x1789` |  | Bubble variant B title. Sub-part 0x178c. | id ✓ |
| `0x3003` |  | Bubble variant A title. Sub-part 0x3006. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Row add, remove and update on the overlay list only run while the global flag _DAT_101F00AC equals 1. Init sets the flag to 0. | `0x1018959C` | all | medium | function ✓ |
| The hover bubble is anchored to the list row under the cursor. Its variant is chosen by bit 0x1 of the row flags: variant A (0x3003) when clear, variant B (0x1789) when set. Its sub-part shows when bit 0x8 is set. It is shown only when the predicate FUN_101AD14C returns 0. | `0x10189BAC` | all | low | function ✓ |
| FUN_10189518 and FUN_10189758 lazily create a 0xE8C-byte object and then open it (FUN_101AFEF8) or the alternate path (FUN_101AFF98). Both clear the list first with FUN_101792C4. | `0x10189518` | all | low | function ✓ |
| FUN_10189E64 is the initialiser. It builds the overlay with style 'hilight' and the bubble templates, and creates a large root container of roughly 2047 by 1535 units. | `0x10189E64` | all | low | function ✓ |

### Find Parks (online park search) dialog

Functions: `0x1018AB84`, `0x1018A1BC`, `0x1018A84C`, `0x1018AAF0`, `0x1018AF54`. Modal-style search window titled 'Find Parks'. It has a Search button, a name-like text field, a six-column results list with sortable headers, and a status line. Selecting a result opens the park details dialog.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xe059` |  | Search button. Shown pressed while a search is running. Press handler: when FUN_1017217C returns 0, it starts the search. After results are filled it is reset to off. | id ✓ |
| `0xe05a` |  | Text field, initialised to the current player's name (FUN_1012D630 of the session object). Its exact purpose is not confirmed. | id ✓ |
| `0xe05b` |  | Results list. Holds the placeholder rows 'Search in progress' (134) and 'No search results' (133) plus one row per result. Columns are Name, Theme, City, Position, Visits and Votes. | id ✓ |
| `0xe05c` | 299: Find Parks | Title or status line, set with FormatString using index 0x12B. | id ✓ text ✗ |
| `0x10` | 300: Name | Column header. Sort button with id 0x17D. | id ✓ text ✓ |
| `0x11` | 301: Theme | Column header. Sort button with id 0x17E. | id ✓ text ✓ |
| `0x12` | 302: City | Column header. Sort button with id 0x17F. | id ✓ text ✓ |
| `0x13` | 303: Position | Column header. Sort button with id 0x180. | id ✓ text ✓ |
| `0x14` | 304: Visits | Column header. Sort button with id 0x181. | id ✓ text ✓ |
| `0x15` | 305: Votes | Column header. Sort button with id 0x182. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The dialog is a singleton. It is built once and reused. | `0x1018AB84` | all | high | function ✓ |
| Pressing Search when the check FUN_1017217C returns 0 marks the button pressed, posts a start command and, if no search is already running, starts the network request (FUN_101334D4) and shows the 'Search in progress' row (134). | `0x1018A1BC` | all | medium | function ✓ |
| Pressing Search again while a search is running (argument != 1) shows a developer status line, not a UITEXT string: 'WL / MW : Stop the search and display whatever has been got in the sea'. It does not itself stop the search. | `0x1018A1BC` | all | low | function ✓ |
| When results arrive, an empty set shows 'No search results' (133). Otherwise each result becomes a row: name (+0x14), theme (name looked up from +0x4C), city (name looked up from the +0x48 id), position (+0xB8), visits (+0x50), votes (+0x54). The last row is flagged. The Search button is then reset to off. | `0x1018A84C` | all | medium | function ✓ |
| Selecting a row (notify 0x400) reads the record id from the list and opens the park details dialog (FUN_1018B474). | `0x1018A1BC` | all | high | function ✓ |
| Cancel (command -2) or the Escape key (0x1B) closes the dialog and runs a virtual cleanup call. | `0x1018A1BC` | all | high | function ✓ |
| On creation the dialog plays sound 0x254. Position and Visits and Votes columns get a flag of 1, probably marking them numeric. | `0x1018AB84` | all | low | function ✓ |
| Whether the search is reachable offline, or only online, is not decided in this chunk. No game-type check appears in these functions. | `0x1018A1BC` | unknown | medium | function ✓ |

### Park details dialog

Functions: `0x1018B474`. Modal details window for one published park. It shows the owner, email, date, visits, votes, theme, chart position and the Instant Action flag, plus a description block and an owner-only action button. Opened from the search results, the chart list, and two external callers.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0xe5932` |  | Container for the detail labels. | id ✓ |
| `0xe5933` | 279: Number of visits | Shows visits (+0x50). Owner-branch only. | id ✓ text ✓ |
| `0xe5934` | 21: State of repair | Shows day, month and year from the record's +0xA8, +0xAA and +0xAC shorts. Owner-branch only. Format index 0x1C0 has no text in UITEXT.tsv. | id ✓ text ✗ |
| `0xe5935` |  | Yes (0x14D) or No (0x14C) from the record's +0xBC. Owner-branch only. | id ✓ |
| `0xe5936` |  | Shows the record's email field (+0x9C). Falls back to 'None supplied' (0x11C) when empty. Owner-branch only. | id ✓ |
| `0xe5937` |  | Owner-branch: 'Created by' (0x113) with the author name (+0x3C). Else-branch: the theme text (0x110) is shown here instead. | id ✓ |
| `0xe5938` |  | Shows votes (+0x54). Owner-branch only. | id ✓ |
| `0xe5939` |  | Theme name from the theme-list lookup (+0x4C). Owner-branch only. | id ✓ |
| `0xe593a` |  | Sentinel 0x7FFFFFFF (+0xB8) shows the unranked text variant (0x111 with an offset). Otherwise the number. | id ✓ |
| `0xe592f` |  | Owner action button. Enabled only when the author name equals the current player name (case-insensitive). Its label is not set here. | id ✓ |
| `0xe5930` |  | Header text, set from the record's +0x28 string. | id ✓ |
| `0xe5931` |  | Text block made of the +0x28 string followed by the park name (+0x14). | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The owner action button 0xE592F is enabled only if the park's author equals the current player name, compared case-insensitively. Otherwise it is disabled. | `0x1018B474` | all | high | function ✓ |
| The full detail set is shown only when the city or region record's +0x60 flag is 0. Otherwise the reduced set is shown: theme only, the owner button disabled, and the other fields left empty. | `0x1018B474` | all | low | function ✓ |
| The Instant action mode field reflects the park record's flag (+0xBC). It is a property of the saved park, not the current game type. | `0x1018B474` | all | medium | function ✓ |
| Chart position shows the unranked variant when +0xB8 equals 0x7FFFFFFF, and the number otherwise. | `0x1018B474` | all | medium | function ✓ |
| The dialog is a singleton. A nonzero second argument places it 200 pixels to the side of the given parent. The sound effect 0x251 plays on open. | `0x1018B474` | all | low | function ✓ |

### Other Parks chart list

Functions: `0x1018C880`, `0x1018C654`, `0x1018C180`. Modal list titled 'Other Parks In <location>', with a sortable five-column chart. Selecting a row opens the park details dialog.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x14beb` |  | Chart list. Columns Name, Theme, Position, Visits, Votes. Position, Visits and Votes columns get flag 1. | id ✓ |
| `0x126` | 294: Name | Column header. Sort button with id 0x177. | id ✓ text ✓ |
| `0x127` | 295: Theme | Column header. Sort button with id 0x178. | id ✓ text ✓ |
| `0x128` | 296: Position | Column header. Sort button with id 0x179. | id ✓ text ✓ |
| `0x129` | 297: Visits | Column header. Sort button with id 0x17A. | id ✓ text ✓ |
| `0x12a` | 298: Votes | Column header. Sort button with id 0x17B. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The title is 'Other Parks In' (UITEXT 0x1BF) followed by a location name taken from a global record's +0x14 string. | `0x1018C880` | all | medium | function ✓ |
| Each row shows name (+0x14), theme (name lookup from +0x4C), position (+0xB8), visits (+0x50) and votes (+0x54). The last row is flagged. | `0x1018C654` | all | medium | function ✓ |
| Selecting a row (notify 0x400) opens the park details dialog (FUN_1018B474). Cancel (-2) and Escape (0x1B) close it. | `0x1018C180` | all | high | function ✓ |
| The dialog plays sound 0x253 on open. | `0x1018C880` | all | high | function ✓ |

### Publish Park dialog

Functions: `0x1018CE80`, `0x1018CC6C`. Form for publishing the current park to the online park list. It has a title, a name field and a description field. OK submits the park, and the submission records whether the game is Instant Action.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x11e5` | 218: Publish Park | Title label. | id ✓ text ✓ |
| `0x11e4` |  | Name-like text field, pre-filled from a game-level string (probably the park name, low confidence). | id ✓ |
| `0x11e3` | 220: Description | Description field, pre-filled with the text 'Description'. Hidden or shown in a mutually exclusive way with 0x11e4. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| OK (command -1) first reads the game type (lazily initialised with FUN_1012BB64), then calls the submit routine FUN_10130498 with a flag equal to (game type == 2). The flag is 1 only for Instant Action. | `0x1018CC6C` | instant_action | high | function ✓, game-type test ✗ |
| The submit routine is identified as the publish request only at medium-low confidence. The flag is what the park details dialog later shows as 'Instant action mode'. | `0x1018CC6C` | all | low | function ✓ |
| Cancel (commands -3 to -2) and Escape (0x1B) close the dialog. OK also closes it after submitting. | `0x1018CC6C` | all | high | function ✓ |
| Notify 0x803 with argument 0x11E4 hides 0x11E3. Any other argument hides 0x11E4. | `0x1018CC6C` | all | low | function ✓ |
| The dialog plays sound 0x250 on open. | `0x1018CE80` | all | high | function ✓ |

### Park map screen

Functions: `0x101B9C54`, `0x101BAA60`, `0x1018F404`. Full-screen map of the park with zoom and pan, layer toggles, two radio groups, four sliders, and a hover label showing what is under the cursor.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x991` |  | Hover label. Shows a ride or shop name, or a guest label. | id ✓ |
| `0x982` |  | Layer toggle. Checked from state bit 0x8. | id ✓ |
| `0x987` |  | Layer toggle. Checked from state bit 0x1. | id ✓ |
| `0x989` |  | Layer toggle. Checked from state bit 0x10. | id ✓ |
| `0x993` |  | Layer toggle. Checked from state bit 0x2. | id ✓ |
| `0x994` |  | Layer toggle. Checked from state bit 0x4. | id ✓ |
| `0x983` |  | Layer toggle. Checked from state bit 0x20. | id ✓ |
| `0x98b` |  | Radio group with four options 0x98C to 0x98F, indexed by state +0x1C. | id ✓ |
| `0x996` |  | Radio group with three options 0x997 to 0x999, indexed by state +0x20. | id ✓ |
| `0x98a` |  | Slider, range 0 to 10. | id ✓ |
| `0x984` |  | Slider, range 0 to 10. | id ✓ |
| `0x981` |  | Slider, range 0 to 10. | id ✓ |
| `0x988` |  | Slider, range 0 to 10. | id ✓ |
| `0x992` |  | Setting control, set from a global. Purpose not resolved. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The map is opened only if it is not already open (FUN_10198638 returns 0 and the state's dialog handle is 0). It hides the 3D view, turns fog off, sets render options 0x2821, flushes the texture cache and sets the redraw flag. | `0x101B9C54` | all | medium | function ✓ |
| Layer toggles and radio indices are initialised from the state's bitmask (+0x18) and indices (+0x1C, +0x20). | `0x101B9C54` | all | medium | function ✓ |
| Pan is clamped to the map bounds, and zoom is clamped to the map size minus one. | `0x101BAA60` | all | medium | function ✓ |
| Hovering finds the nearest marker (only entries with flag +0x2C set) within a radius. Rides and shops (type codes 4 to 7) show their name. Guests show 'New Visitor' (0x1C9) when their id is 0, otherwise 'Visitor #N' (0x1C8, with the id at +0x1D8). | `0x101BAA60` | all | medium | function ✓ |
| Key 0x1E zooms in or out, but only when the global flag +0x38 is set and the drag-active flag is set. | `0x101BAA60` | all | low | function ✓ |
| The map can be opened from the menu code (FUN_1018F404) and from FUN_10155414, which lie outside this chunk. | `0x1018F404` | all | high | function ✓ |

### Generic message or confirm dialog

Functions: `0x10190270`. Shared builder for modal message boxes with a text block and up to three buttons. It is called from many places outside this chunk, and the button labels are style names rather than UITEXT.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x9873c8` | 8: Quit Game | Button 1. Type code 1 uses style b_okay, 2 uses b_exit, and 0 hides it. Callback value is param_3. | id ✓ text ✗ |
| `0x9873c9` | 8: Quit Game | Button 2. Same type codes as button 1. Callback value is param_5. | id ✓ text ✗ |
| `0x9873ca` | 8: Quit Game | Button 3. Same type codes as button 1. Callback value is param_7. | id ✓ text ✗ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| Each of the three button slots takes a type code: 0 hides the button, 1 gives the OK style ('b_okay'), and 2 gives the Exit style ('b_exit'). The callback value for each slot is stored with FUN_10180490. | `0x10190270` | all | high | function ✓ |
| The dialog is built from a modal template (FUN_1018FE7C with flag 1). Its text block comes from the first argument, and the font size is taken from the argument at offset 2. | `0x10190270` | all | medium | function ✓ |
| When param_8 is 0, the function records the current state of the global byte and then sets it to 1. Otherwise it records -1. This suggests the dialog pauses or blocks input while open. | `0x10190270` | all | low | function ✓ |
| If the game-level object's +0x3C is 1 and a global is 0, the function clears that state, sets +0x3C to 0 and requests a redraw. | `0x10190270` | all | low | function ✓ |

### In-game pause menu

Functions: `0x10197D90`. Builds the main in-game menu (Resume, Load, Save, Restart Park, Publish Park, Options, Exit To Lobby, Quit). Dispatched by FUN_10198514 when its argument is 0 (FUN_10198514 is outside this chunk).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x0` | 7: Resume Game | Menu command 0 in the normal menu; command 0x10 in the online menu; command 0x15 in the state-4 variant. | id ✓ text ✓ |
| `0x1` | 3: Load | Command 1 in the normal menu; command 0x16 in the state-4 variant. Not shown in the online menu. | id ✓ text ✓ |
| `0x2` | 4: Save | Command 2. Normal menu only. | id ✓ text ✓ |
| `0x3` | 10: Restart Park | Command 3 in the normal menu; command 0x17 in the state-4 variant. Not shown in the online menu. | id ✓ text ✓ |
| `0x4` | 5: Publish Park | Command 4. Normal menu only, always listed there. | id ✓ text ✓ |
| `0x5` | 6: Options | Command 5. Shown only when FUN_1012d3a0 returns 0. | id ✓ text ✓ |
| `0x6` | 12: Exit To Lobby | Command 6 in the normal menu; command 0x13 online; command 0x18 in the state-4 variant. | id ✓ text ✓ |
| `0x7` | 2: Go Offline | Command 7. Shown only when FUN_1012d5f8 returns nonzero. Normal menu only. | id ✓ text ✓ |
| `0x8` | 8: Quit Game | Command 8 in the normal menu; command 0x14 online; command 0x19 in the state-4 variant. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| If the game type is 1 (online), the menu contains only Resume, Exit To Lobby and Quit Game, with no Load, Save, Restart, Publish or Options. | `0x10197D90` | online | high | function ✓, game-type test ✓ |
| Else, if the state field at +0x1da738 of the game object equals 4, the menu is a variant with Load, Restart Park, Resume, Exit To Lobby and Quit Game (command ids 0x15 to 0x19), without Save, Publish, Options or Go Offline. | `0x10197D90` | unknown | low | function ✓ |
| Otherwise (offline or normal), the menu lists Load, Save, Restart Park and Publish Park unconditionally. Go Offline is added only if FUN_1012d5f8 is nonzero. Options is added only if FUN_1012d3a0 returns 0. Resume, Exit To Lobby and Quit Game are always present. | `0x10197D90` | not_online | medium | function ✓, game-type test ✓ |
| Inside this function there is no test for game type 2 (Instant Action). In the offline case Instant Action gets the same full menu as Normal, including Save, Publish and Load, unless the state-4 branch applies to it. | `0x10197D90` | instant_action | medium | function ✓, game-type test ✓ |
| Menu items use the same layout code in every branch (FUN_10140b38 with a 5px vertical step), and the menu is created by FUN_101409BC with its position set from globals. | `0x10197D90` | all | medium | function ✓ |

### Secondary menu (Go Online / Options / Select New Player / Return To Park)

Functions: `0x1019821C`. Builds the second menu. FUN_10198514 calls it when its argument is nonzero. Its contents depend on whether the object at +0x108 is set.

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x9` | 1: Go Online | Shown when FUN_1012d5f8 returns 0. Command 9, branch +0x108 == 0. | id ✓ text ✓ |
| `0xa` | 2: Go Offline | Shown when FUN_1012d5f8 returns nonzero. Command 0xA, branch +0x108 == 0. | id ✓ text ✓ |
| `0xb` | 6: Options | Command 0xB. Shown when FUN_1012d3a0 returns 0. Branch +0x108 == 0 only. | id ✓ text ✓ |
| `0xc` | 13: Select New Player | Command 0xC. Shown when the zero-argument call FUN_101C7CAC returns nonzero (predicate unknown). Branch +0x108 == 0 only. | id ✓ text ✓ |
| `0xd` | 224: Return To Park | Command 0xD. Replaces the Go/Options/Select block when +0x108 is nonzero. | id ✓ text ✓ |
| `0xe` | 7: Resume Game | Command 0xE in both branches. | id ✓ text ✓ |
| `0xf` | 8: Quit Game | Command 0xF in both branches. | id ✓ text ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The secondary menu is used only when FUN_10198514 is called with a nonzero argument. Argument 0 opens the pause menu (FUN_10197D90) instead. | `0x1019821C` | all | high | function ✓ |
| If the object at +0x108 is 0, the menu shows Go Online (or Go Offline if FUN_1012d5f8 is nonzero), then Options if FUN_1012d3a0 is 0, then Select New Player if the predicate is nonzero, then Resume and Quit. | `0x1019821C` | all | medium | function ✓ |
| If +0x108 is nonzero, the menu is Return To Park, Resume Game and Quit Game only. | `0x1019821C` | all | medium | function ✓ |
| No game-type check in this function. | `0x1019821C` | all | high | function ✓ |

### Settings-style panel with sliders, checkbox and 5-option group

Functions: `0x101B9068`, `0x101B9658`, `0x101B9698`, `0x101B96D8`, `0x101B97F8`. Options-style page with four sliders (0 to 10), a checkbox, and a five-option button group whose labels are supplied by callers. The 'Options' menu item probably opens it (low confidence).

| Control | Text | Behaviour | Check |
| --- | --- | --- | --- |
| `0x16000` |  | Text cleared when the hide branch runs. | id ✓ |
| `0x16001` |  | Slider, range 0 to 10. | id ✓ |
| `0x16002` |  | Slider, range 0 to 10. | id ✓ |
| `0x16003` |  | Text cleared when the hide branch runs. | id ✓ |
| `0x16004` |  | Slider, range 0 to 10. | id ✓ |
| `0x16005` |  | Slider, range 0 to 10. | id ✓ |
| `0x16006` |  | Checkbox. Set on when the flag passed to FUN_101B97F8 is 1. | id ✓ |
| `0x16007` |  | Container for the 5-option group. Shown or hidden through message 6, and hidden by the hide branch. | id ✓ |
| `0x16008` |  | Option 1 (index 0) of the 5-option group, label set by FUN_101B96D8 index 0. Command value 1. | id ✓ |
| `0x16009` |  | Option 2 (index 1). Command value 2. | id ✓ |
| `0x1600a` |  | Option 3 (index 2). Command value 3. | id ✓ |
| `0x1600b` |  | Option 4 (index 3). Command value 4. | id ✓ |
| `0x1600c` |  | Option 5 (index 4). Command value 5. | id ✓ |

| Rule | Function | Modes | Agent confidence | Check |
| --- | --- | --- | --- | --- |
| The panel is built only once, and only when a global pointer is 0 and a parent object exists. With argument 0 the 5-option group is built and shown. With a nonzero argument the group is hidden and the two texts cleared. | `0x101B9068` | all | medium | function ✓ |
| Sliders 0x16001, 0x16002, 0x16004 and 0x16005 are set to range 0 to 10 in both branches. | `0x101B9068` | all | medium | function ✓ |
| Labels for the 5-option group are set by index through FUN_101B96D8, with index 0 to 4 mapping to 0x16008 to 0x1600C. An index out of range is ignored. | `0x101B96D8` | all | high | function ✓ |
| FUN_101B9658 and FUN_101B9698 show and hide the 5-option container 0x16007 through message 6 with argument 1 or 0. | `0x101B9658` | all | high | function ✓ |
| FUN_101B97F8 sets the checkbox 0x16006 to on when its argument is 1. | `0x101B97F8` | all | high | function ✓ |

## Open questions from the agents

- FUN_10000C6C: the state global read through toc slot -0x1D9E is not identified, and the two strings chosen by its values 2 and 10 are not resolved. The value 2 matching Instant Action is only the lead's assumption. This chunk never reads _DAT_101EC8F8, so no direct game-type check exists in chunk00.
- Chunk00 contains no direct game-type (_DAT_101EC8F8) check. The only Instant Action link is indirect: category code 0x1B (slots 4 or 6) disables upgrades, and UITEXT 27 says upgrades are not available in Instant Action mode. Confirm the meaning of 0x1B and whether it depends on game type.
- FUN_101C7CAC is a vtable dispatch. The one-argument form is a get, and its result is compared with 2 and 5 in FUN_10137FD0 and elsewhere. Those compares are per-control state values, not the game type. Other chunks that compare FUN_101C7CAC(...) == 2 should be treated the same way.
- Visitor panel: the value widgets 0x372E to 0x3731 and 0x3724 are not mapped to their labels (cash, rides, purchases and so on). Visitor fields +0x1A0, +0x1C4, +0x1C8, +0x1CC, +0x1D0 and +0x1D8 need names from the visitor struct definition.
- Meaning of the visitor status codes at +0x220 (0x10 is special) and the table at +0x570 used to format status text is not resolved.
- FUN_1012D5F8 returns object field +0x60. The field's meaning (online state, outbox count or tri-state with -1) is not resolved. The postcard notice logic depends on it.
- FUN_101451D0: the category dispatch table (codes 3 to 0x1B) and return values (2 = accepted, 1 = partial) are not resolved. The item purchase/upgrade flow depends on them.
- Dialog id 0x5A passed to FUN_10139B80 in the item window is not identified.
- The item-window table at _DAT_101ED98C (0x208-byte entries, in-use flag at +4) and its counter are assumed to be the open-window list. Confirm this from the allocation code.
- Message code 0x10006 and the codes 0x100002 and 0x100 used throughout are inferred as activate/press and notification codes. Their exact definitions are not confirmed.
- FUN_1008CFB0 is the entry routine for an unidentified screen. Its owner (screen name) and the meaning of child panel 0xBF432 are not resolved.
- FUN_101478B4: whether it is Publish Park, rename park or another name/description dialog is inferred only. The accept path calls FUN_1018E9B0, whose behaviour is not confirmed.
- The option flags used for confirmations (settings+0x37) and push scroll (settings+0x36) are inferred from the UITEXT labels 'Confirmations:' and 'Push scroll:'. The byte offsets are not confirmed against the options struct.
- Several helpers were only read at their first lines (for example 0x1017FA64, 0x10181230, 0x1017F450, 0x10180AF4). Their behaviour is partly inferred from usage.
- Game type (Instant Action vs Full Simulation vs online): none of the 19 functions in this chunk reads the game-type global, and the predicates they call (FUN_100dfe0c, FUN_100dfe20, FUN_100dfe88, FUN_100dfe90, FUN_100dd744, FUN_100e2fe4, FUN_10045eac, FUN_100f4e94, FUN_100f4170) do not either. The downstream detail builders FUN_1016c3ac, FUN_1016ed40 and FUN_10168990 also do not. Any game-type gating must be in another chunk or in FUN_1014fd78, which was not read.
- FUN_1015af1c (called by buttons 0x1e499 and 0x32e) builds a separate panel using resource sad_undec.wct and UITEXT 156 'Top 3 Thoughts' and shows screen 3. Whether that is the intended destination of those buttons is inferred, not confirmed.
- Meaning of the visitor list header 5 text '?' (UITEXT 117). It may be an unfinished label in the original game.
- Timer 0x80083 is stopped on window open but started elsewhere. The 2000 ms interval argument passed when stopping the timer is not confirmed. FUN_1015af1c uses 1000 ms.
- Meaning of the column on/off flag (FUN_10178070) with column 0 set off and the others on in every builder. Possibly sort-enable or visibility.
- Meaning of the item-type filter bits passed to FUN_10194f58: attractions use 0x80, 0x2000, 0x100, 0x200, 0x400; visitors 0x40; staff 0x2, 0x4, 0x8, 0x10, 0x20. Mapping of each bit to a list kind is inferred from list contents.
- Staff category to happiness-code mapping (5 Janitors, 4 Mechanics, 6 Entertainers, 7 Guards, 8 Researchers) is inferred from UITEXT labels and how the codes are passed to FUN_100c4638.
- The second argument of FUN_10155280(2, N) (N = 4 staff, 5 attractions, 6 visitors, 3 for the Top 3 Thoughts panel) and of FUN_10138584() are screen-mode and cleanup calls whose exact meaning is not resolved.
- FUN_100dc320 is used as a record lookup for side effects in FUN_1014c53c, but in FUN_101397b0 its return value is used as a record pointer. The decompiled body shows no return, so the value used by FUN_101397b0 (sub-kind at +0x7a8) is uncertain.
- Identity of the second text field 0x3943d in the rename dialog and of what 0x15bbd shows (name vs another value). The getters used (FUN_100e2cd0, FUN_100ca3e4) were not read.
- Which function sets up the item list inside the attractions window: child id 0x12c4b9 is the tab group, and the list child was not decoded in this chunk.
- Button text for the staff category buttons (0x323-0x327) and the attraction tabs' button captions are not set in this chunk, so their labels are unknown from this code.
- The rule that 'type byte == 3' marks renamable items (rename dialog and detail dispatch) is inferred. It probably means attraction items (rides, shops, sideshows, misc) but this is not confirmed.
- Callees 0x1014fd78 (detail for sub-kind 3), FUN_1010b808 (visitor row activation) and FUN_1016e1a4 (staff row activation) were not read.
- Helper corrections for the lead: FUN_1017fa64 sets a checked state (bit 0 of control+0x44 with parent propagation), not hide/show or enable. FUN_101c7cac is a vtable dispatch whose meaning depends on the argument type (text or value), not only text. FUN_10157b3c writes the status-bar help-text index (via FUN_10180a98 on the help control). FUN_10172e6c is a radio select inside a group that returns the previous selection. FUN_1017f18c and FUN_101722a0 are value/checked setters with redraw.
- The ride panel labels (0x19 to 0x21 and 0x1f to 0x1b) are inferred from the UITEXT names. The help-text indices set by the radio chooser (move, open, close, track, delete, ride) suggest those child ids may be ride command buttons rather than upgrade labels. Confirm the control ids in the window resource.
- The meaning of the per-object flag byte at +0x4c (gates the trough/loop toggle) and of the global flag byte at +0x37 (chooses direct delete vs confirmation) is not established.
- The mask producer FUN_10049d74 (not in this chunk) sets the 9-bit ride-state mask. Check whether bit 0x4 (the Instant Action wording) is set from game type there.
- FUN_10146b48 / 10146c80 / 10146de0 form a permission query and queued-action dispatcher keyed by action code. The exact meaning of values below 2 is not confirmed.
- FUN_10154aa0 (opened by button 0x1227f) contains the game-type check (type not equal to 2). Its behaviour needs its own chunk.
- The value editor (0x3943c to 0x3943e) on OK runs a scramble-and-compare check against two hard-coded strings and sets a flag through 0x1010e5bc. What it unlocks is not established. This is a sensitive area: the reference strings are intentionally not reproduced here, and the lead should decide how to document it.
- Several message ids passed to the status/message helpers (0x12f, 0x12a, 0xc5) are not UITEXT indices. They come from a separate message table, so their text is unconfirmed.
- Some decompiler string comments in this chunk are misattributed. For example, the 'Thing has been allocated in ThingArray index 0' string is used as a placeholder format argument in 0x1014ef68 and 0x1014f308, not as a real message.
- Not verified: the exact text of the 0x1014c774 sibling panel (0x15bc1) and the function behind 0x1582 / 0x1583 (0x101473bc) and 0x157f (0x1014abe4). Both are outside this chunk.
- Instant Action (game type 2) only changes one thing in this chunk: the text of control 0x1227f in the Financial window is blanked. What that label normally says is not identified, and no UITEXT entry matches it clearly.
- The period-radio default global (the one read at offset -0x4208 from the TOC) is not identified. It is probably a saved setting, but this is not confirmed.
- Helpers 0x1014fe1c and 0x1014ff74 (used for the graph range) are not analysed. They read the same series mask.
- The button that opens the Financial window, and the handlers for the period and series toggles, are not in this chunk.
- The 0x2495 button in the staff dialog opens another dialog through 0x101643b4 with argument -1. That dialog is not identified.
- The save-overwrite prompt is built from UITEXT 0xcd, which is blank in UITEXT.tsv. Its real text is unknown (maybe another string table).
- The actual park save write and the actual park load are not in this chunk. The load path builds a file name and calls a file helper (0x1019878c and 0x1019882c with 0x1011acfc and 0x1011a5f4). Exact semantics are uncertain because the decompiled call is missing an argument.
- Whether the hire action (0x100f61d0 when a candidate is activated) checks budget or cash is not visible. The routine builds a world object from the candidate record, and its full logic was not read.
- The park-object field at offset 0x114 gates money movement only on loan repayment. What it represents (maybe a financial-simulation or game-mode flag) is unknown. Setting it is outside this chunk.
- Meaning of the 0x80080 control and of message 0x15 handling (helpers 0x10180940 and 0x101809d8) is unknown. It may be a flash or animation on the cash readout.
- Meaning of UI notification codes 0xbd (staff dialog open and close), 0xc5 (Financial open) and 0x135 (hire confirm) is not determined.
- FUN_10155280(kind, value) sets screen-state globals. Observed uses: kind 1 with 2 (staff dialog), kind 1 with 1 (0x101643b4 dialog), kind 3 with 7 (Financial). The meaning of each is not determined.
- FUN_10153ca8 and FUN_10153ed4 look up control 0x12c4bd and discard it. This may be a leftover refresh with no effect.
- Which button on the loans dialog triggers the take handler (0x10153ed4) and which triggers the repay handler (0x10153ca8) is not in this chunk. The mapping here is by callee only.
- The meaning of each loan-row column (which field goes in which column) is inferred from the UITEXT order and is not confirmed.
- Meaning of the second value on candidate rows (0x100f5df0) is inferred as the monthly wage, not confirmed.
- The staff-budget strings 0x93 to 0x95 (Staff Training Budgets, Average Skill, Monthly Budget) are not used in this chunk. They are probably in another screen.
- Whether the Staff, Loans, Save and Load screens are restricted in Instant Action is not determined from this chunk. Other chunks (menu and button builders) need to be checked.
- The 3D preview model indices (4 to 8) are assumed to be staff models. This is inferred from the staff types, not confirmed.
- Chunk is 14 functions, 1142 lines. All of them are covered, but several rules are inferred and carry medium or low confidence.
- Game type gating: the bank dialog (0x10154aa0) is skipped entirely in Instant Action (game type 2). Verified by the single guard around the build. Whether the bank can be reached from another route in Instant Action is not checked here.
- Out-of-chunk observation: FUN_1014ec48 (the screen that command 0x12c4ba opens) hides control 0x4f3ac when the game type is 2. That suggests the screen behaves differently in Instant Action. Not verified in detail.
- The identity of the dialog behind _DAT_101edb30 (Shared dialog A) is not established. Its text ids (0x1c0 and the 0x1d-0x25 tree strings) are not resolved. Text 0x1c0 is blank in UITEXT.tsv.
- FUN_1015644c: the drawer's owning screen is not confirmed. Its state values (2, 3, 4) and the sounds 0x1c, 0xc2 and 0x12f family are inferred from the code.
- FUN_10157c9c calls FUN_10138504(param_2) and discards the result. It is not clear if param_2 is meant as the label prefix. Its callers are outside this chunk.
- FUN_10153d00: the field meanings are inferred. The amount owed is taken to be monthly repayment times remaining months, and the comparison is against the bank cash at offset 0xc. The 'There is $' prompt (UITEXT 181) is shown with the amount but the full sentence is not resolved, so the exact wording and the choice's outcome are unknown. The take-out and repay confirm handlers are not in this chunk.
- Ledger fields: the offsets 0x1fc90 (income) and 0x1f5a0 (costs) are inferred from how they are used. The loop's flag and value fields in the 8-entry loop are not identified.
- FUN_10172e6c, FUN_1017f450 (sets a 16-bit state, probably warning or highlight), FUN_1017fa64 (enable/show helper), FUN_10155280 (screen-code setter) and FUN_101722a0 (toggle set) are used with inferred meanings. Their precise semantics should be checked against their own functions.
- FUN_101555b4 uses 0x1007bccc (returns a global at _DAT_101ed134) to drive the 0x30 sub-control. The meaning of that global is not established.
- The command ids 0x12c4ba, 0x12c4bc and the range below 0x12c4bc are mapped to screens by the code. The screens they open (codes 10, 9 and 7) are only partly described and should be covered by their own chunks.
- The 'Take out a loan' confirmation (0x10190270 with message type 2) returns to a handler that is not in this chunk. The result path is unknown.
- No file was created or modified. All findings were produced from reads of chunk04.c, UITEXT.tsv, UIHELPTEXT.tsv and stp_all_s.c.
- The jump-table targets for FUN_10158508 (ids 0x1d4c1 to 0x1d4dd) and FUN_10157db0 (ids 0x1d4d5 to 0x1d4e0) are not recovered, so the exact effect of each option change (live apply, save, or restart needed) is unknown.
- Labels for the four unlabeled toggles (0x1d4d7, 0x1d4d9, 0x1d4db, 0x1d4dd) and the five sliders (0x1d4d8, 0x1d4da, 0x1d4dc, 0x1d4de, 0x1d4e0) are not set in this chunk. Their text probably comes from the dialog resource, and the options fields they map to are not identified.
- Controls 0x1d4c6 and 0x1d4c7 are positioned in the checkbox column but get no text or state here.
- Checkbox polarity: the code sets the widget state to the negation of the option, so it is unclear whether the widget means checked or pressed. This needs the widget implementation to settle.
- Effects of FUN_10110518, FUN_1017d1c4, FUN_10007160, FUN_101260a8 and the options+0x3c flag are not recovered.
- Targets of the Park Information command codes: FUN_10148654 (screen 6), FUN_1014abe4 (screen 5) and FUN_1014ba4c (screen 4, 'ridestatbar.wct') were not read beyond their openers.
- Meaning of the 199 code posted by FUN_10139ae0 and FUN_10139a7c is unknown.
- The time-range index global read through a local value-0x411c, and the series history offsets (0x20cc0 and the others), are inferred from the code, not confirmed against the park object definition.
- The thought-name list of 22 entries and the source of FUN_100c26dc (the three thought indices) need checking in another chunk.
- Meaning of controls 0x7a16 and 0x7a17 (for example, delete button or play button) and the 0x14 flag of the slot record are not confirmed.
- UITEXT entries 0x153 (Laptop), 0x154 (Custom), 0x159, 0x15a (1600 x 1200) and 0x15b (400 x 300) exist but are not referenced in this chunk. They may be used by other chunks or may be unused.
- Game-type (_DAT_101ec8f8) checks: none appear in this chunk. Whether the Game Options or Park Information screens differ by game type must be checked in the other chunks or in the callers.
- The 'Top 3 Thoughts' rows show thought names, but the lookup table for those names is in another chunk.
- FUN_10198638 (blocking check before the Research Lab opens): what it tests. It returns 1 when a global is set and FUN_10140e70 returns non-zero. Could be a modal dialog, an online session or a loading state.
- Identity of the short at park offset 0x1da742 and the global short it is compared with (researcher count, and whether the constant is zero).
- The format string with id 0x1be is blank in UITEXT.tsv. The caption text for a current research target (name plus value) is unknown.
- FUN_101383f8(id) returns a sound/handle object, not UITEXT text. The ids 0x9a, 0x9b and 0x9c coincide with UITEXT entries 154 'Average time in park', 155 'Park rating' and 156 'Top 3 Thoughts', but are used as resource/sound ids here. Verify before naming.
- Meaning of the audio keys 10/11 and 12/13 sent via FUN_100baf70 (volume, pitch or pan?) and what the 0x12 notify on the 'average' object does.
- Category names: the five category panels (0,1,2,3,4) are not named in the UITEXT strings used in this chunk. Need the category order from other chunks to map them to Rides, Shops, Land, etc.
- Whether category 3 (Buy Land / Clear Land) is the land-purchase part of the build menu. This is inferred from the presence of the land rows and captions, not confirmed.
- Gauge scaling mismatch on the stat bar: values are field*1024/100 while the ranges are 500/200/100. Need FUN_101c7cac's gauge model to confirm.
- 0xd2d11 and 0xd2d12 children of the effort gauge: their exact roles (fill width, rate display) are inferred from the code.
- FUN_10138584 closes another window (close code 5) before opening the lab. Which window is closed is unknown.
- Callers of FUN_10161910 (the button or menu that opens the Research Lab) are not in this chunk.
- FUN_100f1e50 normalisation (what happens when the five category shares do not sum to 100) and FUN_100f1f40 (per-category field at +0x1410) are only partly traced.
- The 'known' check FUN_100d30d0 and the item availability byte at +0x10 of FUN_100d1be8 are not fully traced. The mystery/unlock rules depend on them.
- Meaning of 'mode' (the global read in 0x10162e98 and set in 0x10162308) is inferred from the branches. Confirm which category index maps to which stat set.
- The window message 0x100 with a negative id (close, return 0) is inferred as a close-button path. Not confirmed.
- Command ids 0x186, 0x187, 0x18a, 0x189, 0x18e, 0x23a and the 0x100 command ids in this chunk are not UITEXT ids. Their mapping to front-end menu buttons is not resolved, and their identity must be checked against the menu code.
- Text id 0x4e passed to FUN_101906dc (in the postcard builder) matches neither UITEXT (78 = 'All Rides') nor UIHELPTEXT (78 = 'Click to open the bathroom'). It is probably a message from another table.
- Format strings 0xd5 (invalid typed address), 0xd7 (delete confirmation in the address book) and 0x18f (slot delete confirmation, seen in the slot handler in another chunk) are blank in UITEXT.tsv. They may live in the FormatString table at offset +0x564 and need separate lookup.
- FUN_101385e0 failed to decompile, so the offline branch of the postcard builder is only partly understood.
- The control that triggers 'add typed address' is not in this chunk (caller FUN_1015d90c is in another chunk).
- Meaning of buttons 0x5da1 (apparently Clear) and 0x5da2 (apparently refresh preview) is inferred from handler code only; no labels in this chunk. The clear path does not reset the edit flags, so defaults may not come back after a clear. Possible bug, unverified.
- Meaning of the mode flag passed as the fourth argument of FUN_1013741c (1 when Instant Action was picked) is not confirmed. It may be an Instant Action flag stored in the new record.
- Identity of control 0x70b (likely OK or Start, enabled only when Instant Action is selected) is not confirmed. Its label is not in this chunk.
- Meaning of the flags at slot record offset 0x14 and of controls 0x7a16 and 0x7a17 is not confirmed (other chunk).
- Quit button 0x7a1c also gets slot field 3 (same as slot 3). Possibly copy-paste in the decompile, unverified.
- Address-book destroy path posts status 0x134 ('Search in progress'), while the postcard destroy path posts 0x133 ('No search results') offline. Status line semantics are unclear.
- The 'Creator's name' row (UITEXT 289) is added only in online mode. Its exact role (own creator name vs a list heading) is unclear.
- The five-category readout screen (controls 0xd2d01-0xd2d0a, 0xd2d0e, 0xd2d10 slider) is not identified in this chunk. Callers are in other chunks (FUN_10160cd4, FUN_10160eac, FUN_1016065c). The screen name, and what the categories represent, need follow-up.
- FUN_1015e644 (send) and FUN_1015e48c (validation) are in other chunks. Their game-type dependence, if any, is not checked here beyond what the postcard handler shows.
- No Haiku subagents were spawned: this chunk was analysed by one agent reading it and its callers directly. No files were created or modified.
- None of the functions in chunk09 read the game-type global (the one accessed via _DAT_101ec8f8 and compared to 0/1/2). The Instant Action vs Full Simulation differences are therefore not visible in this chunk. The state gate used in FUN_101660b0, FUN_1016803c and related functions calls FUN_10146b48, which is a state/mode dispatcher. Its exact meaning (and whether it relates to game type) is not verified.
- FUN_10166f1c (purchase helper, read as a callee): the direction of the negative-id slot check looks inverted (it fails when the field is less than cash). Needs verification against the original data.
- The ride type field (+0x70 of the ride record) mapping to time modes (0 none, 1 duration, 2 laps, 3 cycles, 4 repetitions) is inferred from the label choices only.
- UITEXT 0x1cc is blank in the table, but FUN_10167d18 uses it as a summary format string. Its real text is unknown (maybe a multi-part string or a different table).
- Sentinels -12999 and -7890 in FUN_10165d34 are not named. Their meaning (close vs. none) is inferred.
- Panel title/help ids 0x3e28 and 0xc06e are not UITEXT indices. Their text table is unknown.
- The labels on the 0x2df and 0x2e4 totals are inferred from arithmetic only. Confirm against the sales summary dialog's UI strings.
- FUN_10166994 has an unguarded division by (max - min) in the first gauge block. It may divide by zero if a ride has equal range values.
- The meaning of FUN_10146c80, FUN_10147034, FUN_10146de0, FUN_101473bc, FUN_1014ec48, FUN_10150c54, FUN_10154aa0 (command handlers in the dispatchers) is not verified. They are in other chunks.
- The sign-flag handling in the readouts (the extra argument) was not confirmed against FormatString's signature.
- Ride/shop record fields used (+0x54, +0x58, +0x59, +0x70, +0x124/+0x128, +0x134/+0x138, +0x19c/+0x1ac, +0x1b8, +0x1a4/+0x22c) are assumed from usage. Their names are not confirmed.
- FUN_1017fa64 was read only partly; show/hide vs enable behaviour is inferred from usage, not confirmed.
- Message text ids: many FormatString and on-screen message ids in this chunk (0x94, 0x96, 0x97, 0x98 used in the status code; 0x21a, 0x21b; 0x39, 0x3a; 0x159, 0x15a; 0xc9, 0xce; 0x1b1) do not match UITEXT.tsv in a usable way. For example UITEXT 0x39 is 'Name' and 0x159/0x15a are resolution labels. UIHELPTEXT 0x97/0x98 are 'Click to buy this item' and a golden-tickets warning. The text tables used by the formatter (offsets +0x568 and +0x5a0) and by the on-screen message routine are probably different from UITEXT. The lead should resolve which table is used before trusting the 0x94-0x98 status texts. Only 0x1ca/0x1cb (cash) and 0x1d/0x1e (upgrade level) line up with UITEXT.
- Identity of the buy-window button ids 0x1ee, 0x1ef, 0x1f0, 0x1f1, 0x1f2, 0x1f3 (499), 0x1f6, 0x1f7 and the stat-bar buttons 0x3e2a, 0x3e2c, 0x3e34-0x3e38 is not established. Their UI labels and actions need checking in the layout resources.
- Helper semantics that are inferred, not confirmed: the researched-set membership helper (0x100d30d0), the prerequisite comparison helper (0x100d313c, which compares the record's +0xc4 field against a count from the park-side set), the item-availability lookup (0x100d1be8, where +0x10 is availability, +4 is price, +0x14 is a researched level count), the shop/ride record lookup (0x10118018), and the enable/disable helper (0x1017fa64, which sets bit 0 of control+0x44 and walks the parent chain). Confidence on all of these is medium or low.
- Whether the prerequisite-gated 'research' path (0x10163748 path B, and status state 0 in 0x101630c0) charges money later, and whether Instant Action makes this research automatic (UITEXT 0x1d4 says 'Research is automatic in Instant Action mode'). Not visible in this chunk.
- The post-purchase check 0x101960c8(0x80, 0) that chooses between messages 0xc9 and 0xce, and what the type-4 request enqueued by the buy path does downstream (build placement, or something else). Not read.
- 0x10162308 was only partly read. The sorting logic (the three recent-slot lookups via 0x100c5a68, the owned-count lookup via 0x100c5828, and the 'sort by owned or recent' column 0x8a) is not fully understood.
- Meaning of the stat-bar readouts 0x3e1b (signed number), 0x3e23, the 0x3e18 gauge formula, and the labels for them. Also whether 0x3e21 is the ride name. The gauge range setter (0x101388a4) was not verified.
- Unresolved callees in this chunk: 0x10162a60, 0x10166994, 0x101391ac, 0x10162584, 0x10162e98, 0x101391dc, 0x10152368 (its id -1 path opens an unidentified screen), 0x10141d38 (status display sink?), 0x10164a18 (assumed close via 0x10170f98 with code 4), 0x10139a40 (assumed error feedback sound, event 0x1d), 0x10139a7c (posts a message by id, probably not UITEXT).
- Game-type gating outside this chunk: none found in the 9 functions read beyond the upgrade list (0x10165a0c). Whether other screens gate the buy list, research or the stat bar buttons by game type is for the other chunks to confirm.
- Game-type dependence in this chunk is limited to FUN_10168d8c (the 0x2dd clearing for Instant Action). None of the other 14 functions tests the game type, so the Instant Action vs Full Simulation vs online differences in this chunk are narrow. Other game-type differences may sit in callers outside this chunk.
- FUN_1017fa64 is treated as show/hide (1 = show/enable, 0 = hide/disable). The evidence is that it is used in panel show/hide code and on the same controls as FUN_1016a880's show-on-select logic. This is medium confidence, not verified.
- FUN_1017f450 is treated as a value setter (used on the 0x148 bar with an int). Not verified.
- Per-type handlers for object types 0..8 (the jump table in FUN_1016a880) and their callers are outside this chunk.
- Names of the object types 4..8, and of the stat indices 4..8 used by the finance bars, are not determined.
- UITEXT entries for ids 0x11f and 0xc6 ('Theme', 'Sign text line 2') look like sound or animation IDs, not text. Confirm what FUN_10139a7c takes.
- Panel 0x1e84 / 0x1e8a / 0x1e8b (compact scrap value) is not linked to any screen name.
- Title text 0x93 'Staff Training Budgets' on the finance panel conflicts with the cash-flow content, so the screen name is uncertain.
- Labels for the attraction stat panel (0xa09c..0xa09f) to value controls are only partly known. Only the scrap value and happiness pairings are supported.
- Chunk 12 contains only generic widget-class code: an exclusive group, a 31-button toggle bank, a scrollable list box and a slider. It has no UITEXT calls (FUN_10138504) and no game-type checks (_DAT_101ec8f8). It therefore says nothing on its own about which screen or which Instant Action / Full Simulation / online logic applies. The caller sites are needed: find who calls FUN_1017f770 with 0x11100000+n and who calls FUN_10178768 (list inserts) to map these classes to real screens.
- The message codes used here (0x100, 0x101, 0x200, 0x201, 0x400, 0x402, 0x405, 0x800, 0x1000a-0x1000d, 0x10001-0x10004, 0x11005-0x11008, 0x2300-0x2800, 0x13, 0x11, 0x15, 0x1a, 0x1e) are only partly identified. They need the definition of the base dispatcher FUN_101813d0 and the notify/post helpers FUN_10170ec8 and FUN_10170f98.
- FUN_1017f450(control, flag) does not forward its flag in the decompile (the value is stored in a local and an uninitialised stack buffer is passed). Whether it means show/hide, enable/disable or highlight is unresolved. The same pattern appears in FUN_101722a0.
- Field offset +0x138 is used for different things by different widget classes: the selected index in the group, the bitmask in the toggle bank, a bounds rectangle in the list and the slider maximum. The class-to-offset mapping needs confirming per class, probably from the constructor.
- Flag bit 0x10 at +0x44 seems to mean 'sorted insert' in FUN_10178768 but 'row press enabled' in FUN_1017a444. Its meaning for the list class is unresolved.
- Not read: FUN_10178654 (comparator used for sorted insert), FUN_10178eb4 (unlink helper), FUN_10179e0c (set selection), FUN_10177d44 (layout), FUN_10177a60 (grow storage), FUN_1017835c (next node), FUN_10182d48 (sub-object notify), FUN_101722a0's helper FUN_10172188, FUN_10179c8c (message 0x1e), FUN_101803c8 (apparently stores the active control in a global, low confidence), and the 0x1a/0x15 path that calls it.
- The global byte at _DAT_101edd48 gates redraw/notify messages throughout. Its exact meaning (visible, active, or notifications enabled) is unconfirmed.
- Keyboard code mapping (0x2300 = last, 0x2400 = first, 0x2600 = previous, 0x2800 = next) and the arrow-id mapping for the slider (1 = up, 2 = down) are inferred from branch structure, not from a named constant.
- Remaining question for the lead: does FUN_1017fa64 (enable/disable) refuse to enable a control whose ancestors are disabled? The branch that does so (LAB_1017fb20) was not read.
- No function in chunk11 reads the game-type global (_DAT_101ec8f8), so none of these panels change with Instant Action, Full Simulation or Online. The only gate found is the 'Confirmations' option, flag byte at the settings object (_DAT_101ec874) offset 0x37. Its label is UITEXT 328 ('Confirmations:'), set in the options dialog outside this chunk. It controls the delete and dismiss confirms.
- Sound and action ids are passed to 0x10139a7c and 0x10139ae0. Those include 0xc0, 0xc1, 0xd4, 0x129, 0x12c, 0x12d, 0xcf, 0x33 and 0x3b. They are not UITEXT indices. The lead's note that 0xd4 is 'Email Address Book' does not apply here; in this chunk 0xd4 is a sound. This needs checking against the sound helper.
- Shared handlers 0x10147034 and 0x101473bc (delete and sell/demolish) are used from several windows. Their confirm-off branch centres the view on a tile and removes the object there. The actual removal path and the confirm-result handler are outside this chunk, so the exact effect is inferred.
- Whether list 0x1e7b shows staff members or park items is ambiguous. The category mapping (staff types 4-8) and the staff detail panel point to staff. The shared delete handler (0x1e7a) and the 'DELETE ITEM' confirm point to park items. Needs checking.
- The filter helpers 0x10194f58 (count of matching entities per mask) and 0x10146b48 (sets the mode and returns the count) are only partly understood. Mask meanings were inferred, and the mode-to-mask table has nine values.
- The arrow direction of 0x10146de0 and 0x10146c80 (prev or next) is not confirmed. The mode-step helpers 0x10197274 and 0x10197234 are not fully decoded.
- Helpers 0x1016e1a4 (rename-sub-window opener), 0x1014ba4c (0x50b sub-screen), 0x1016a880 (0x521 handler) and the dialog-result handlers are outside this chunk. Their behaviour is not verified.
- The progress bars 0x513-0x515 are not clearly tied to Energy, Happiness and Skill labels. Helper 0x100f41dc is declared void but its return value is used, so the decompilation may be unreliable there.
- The 0x5210 value in the scrap panel is produced by a formatter from the item type id. It is not clear whether it shows the count or a name.
- The rename sub-window's maximum name length comes from a global (base 0x1e) whose value is not resolved.
- End-of-year columns: 'This Year' is read from index 0 and 'Last Year' from index 12 of each statistics array. The index-to-column assignment is inferred from the 0xc offset and the header ids, not confirmed.
- Options dialog (0x10158bc0 area) checkbox labels: the Confirmations checkbox (flag +0x37) is inferred from the UITEXT lookup 0x148. The other flags, +0x34 and +0x35, were seen in other functions but not examined.
- Hover help: 2000 ms in the staff list and 4000 ms in the staff detail and scrap panels. Only the timer values are visible. Help strings in UIHELPTEXT.tsv are not referenced by any function in this chunk.
- Game-type gating in this chunk only distinguishes Instant Action (type 2) from everything else: Normal (0) and Online (1) take the same branches. Only the browse panel (0x1e0ea group) and the click sound depend on Instant Action.
- The chunk's game-type mapping was verified in FUN_1012bb64 and FUN_1012bbf4: launch flag 0x2000000 gives Instant Action (2), 0x1000000 gives Online (1), 0x800000 or none gives Normal (0).
- Full-screen identities are partly unknown: the browse panel (0x1e0ea group, FUN_1018395c) and the item overlay (FUN_10183274) are called from lobby code outside this chunk (around lines 90578 and 91079 of the full decompile).
- The park details list (FUN_101862ac, outside this chunk) drives the list, preview and description functions in this chunk.
- FUN_1012d200 (the login submit path) and FUN_10170f98 / FUN_10170ec8 (message posting helpers) were not fully traced.
- Meaning of the format ids 0x1c0 and 0x1ce in the string-table format calls (TbITable, not UITEXT), so the exact texts are unknown.
- Meaning of control ids 0xbf432, 0x1e0ea to 0x1e0f1, 0x2241, 0x49d9 and 0x49da, 0x16c11, 0x16c12, 0x5b7f to 0x5b84, and 0xee2 to 0xee8 in the wider UI is inferred from the code in this chunk only; UIHELPTEXT tooltips are not linked to these ids here.
- Requested multi-subagent review was not performed: this agent works alone on chunk13 and has no subagent-spawning tool in this session.
- No game-type checks (the global read through _DAT_101ec8f8, or '== 2' tests) appear in this chunk. Instant Action vs Full Simulation vs online differences are therefore not visible here. The only possible online-related gates are FUN_1012d600 (on the send path) and the +0x8c flag (on the builder and the failure path), and neither is confirmed as online-only.
- The FormatString and message ids 0x39, 0x4e, 0x191, 0x243 and 0x244 are not UITEXT entries. UITEXT index 0x191 reads 'CHANGE SCREEN RESOLUTION', which does not fit this context, so the ids probably belong to a different string table. Their text is unknown.
- UITEXT 0xf0 'Game Mode' and 0xf1 'Instant Action' are passed to FUN_1006b890 as the first argument, with a sub-code 0 or other. They are probably effect or sound ids and not screen text. Confidence low.
- The button labels for commands 0xee5, 0xee6, 0xee7 and -1 (the Send action) are not set in this chunk. Their texts are likely set elsewhere, and the -1 mapping to a Send button is inferred.
- The purposes of controls 0xee2, 0xee4, 0xc3125 to 0xc3127, 0xc312a and 0xc312c are unclear. 0xc312c seems to be an input or edit control, based on the '/' key handling.
- Callees not read here: FUN_10185908, FUN_10185a18, FUN_10185ba0 (postcard open and preview), FUN_1012d600 (outbox gate, only partly read), FUN_1012d9f0 (outbox process), FUN_10114a60 (key table), FUN_1006b890, FUN_1008dda0 (call with 0x255, meaning unknown), FUN_10170f98 and FUN_101722a0 (state and enable helpers, types 4 and 6 read only from usage), FUN_10115c40 and FUN_10115c90 (focus or cursor effects).
- The message ids 0x14, 0x15, 0x1e, 0x101, 0x1f, 0x20, 0x10001 to 0x1000c are read from the code and assumed to be WM-style events (close, show or hide, click, focus, key, scroll). The exact meanings are inferred from branch structure and are not confirmed.
- The meaning of the global selection and mode flags (_DAT_101f00ec, _DAT_101f00f0, _toc[-0xf70], _toc[-0x10ea], _DAT_101f00ac) is inferred from usage only.
- The meaning of the postcard 'marked for sending' flow (whether it goes online, and what FUN_1012d9f0 does) is not confirmed.
- Chunk 15 has 25 functions. Most are dialog or menu builders and handlers, and they share one virtual call FUN_101C7CAC with no arguments, which is used as a predicate in some places and as a cleanup in others.
- Game-type handling in this chunk is limited. The only explicit Instant Action (type 2) test is in FUN_1018CC6C, which passes the flag to the publish submit. The pause-menu function FUN_10197D90 tests only for online (type 1), and the other type-dependent behaviour comes from state field +0x1da738. Whether Instant Action is restricted anywhere else is not visible in this chunk.
- UITEXT.tsv does not contain several string indices used here: 0x1C0 (date format, empty), 0x98A to 0x999 (map labels), and sound ids 0x250 to 0x254 (not text). Some of these are probably in a different string table reached via the FormatString table pointer at offset 0x564.
- Several predicates are unresolved: FUN_1017217C (search allowed), FUN_10198638 (map open), FUN_1012D3A0 (gates Options), FUN_1012D5F8 (read as online flag), FUN_101AD14C (bubble), and FUN_101C7CAC() with no arguments.
- The Publish limit and login messages (UITEXT 0xDD, 0xDE, 0x101) are not handled in this chunk. Where they gate publishing is unknown.
- Caller context is only partly known. FUN_10198514 (outside the chunk) selects the pause menu or the secondary menu. FUN_10190270 has many callers outside the chunk. The entry points for the Find Parks, park detail and chart screens are outside this chunk as well.
