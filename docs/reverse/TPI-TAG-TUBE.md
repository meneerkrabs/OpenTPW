# Theme Park Inc: the tag tube

The tag tube is Theme Park Inc's mail list: the tag system thing files every letter the message centre carries as a row, and the "Message Centre" window lists the rows.
Static research only, on the no-CD `Game.exe` (SHA-256 `77ea0c41…`); the original was not run. OpenTPW's port is `ThemeParkIncTagTube` (the model),
`ThemeParkIncTagLetter` (the rows the letters file), `ThemeParkIncTagTubeWindow` (the window) and `ThemeParkIncHud.TagTube` (the openers).

## 1. The model

- **The thing.** The new-world init builds the tag system among the first things (`0x6483B7`, `0x635180`, 0x830 bytes) and keeps its id at `park+0x1EAB52`. Its
  world update is `0x635AB0` (class 12 of the thing dispatch, `0x63770E`).
- **The row** is the record `0x635D80` allocates: 0xE2C bytes. `+0` tag, `+4` kind word, `+8` body (0x800 bytes), `+0x808` sender, `+0xA06` addressee, `+0xC04`
  subject, `+0xE02` picture word, `+0xE04` expiry in units of four turns, `+0xE08` expiry turn, `+0xE0C` read, `+0xE0E` unread category, `+0xE10` go-to kind,
  `+0xE14` key, `+0xE18` go-to argument, `+0xE1C` serial (`0xAA0AF0`), `+0xE20` next, `+0xE24` date, `+0xE28` flagged for deletion.
- **The row builder** (`0x635D80`, branch 0x19 for a letter) fills the record through the letter's virtual functions: slot 0 the subject, 1 the sender, 2 the addressee,
  3 the body, 5 the signature after it, 4 the row fill (tag, kind word, expiry, go-to), `0x5CD440` the picture and 7 the category (`+0xE0E` = the letter's `+0x10` word).
  It dates the row: the clock's year, 0-based month and day packed by `0x518BC0` as `year << 16 | month << 8 | day`. The letters of kinds 6 to 8 (`0x5CD060`)
  also build the letter window (`0x526CD0`) and are filed read.
- **Insert** (`0x636880`): the expiry turn and the earliest pending expiry, the next serial; a game insert goes to the head and flags an older unflagged row whose tag is
  the new row's kind word and whose `+0xE18` (and `+0xE14` when the new `+0xE10` is 1) match; a load appends. An unread row counts.
- **Delete** (`0x636A40`): the first unflagged row with the same tag and serial is flagged. The pass then unlinks it.
- **The pass** (`0x635AB0`): flagged rows leave the list, an unread one counting down first; when the earliest expiry has come, the rows whose expiry turn has come
  are flagged and the earliest pending expiry is recomputed.
- **The unread counter** (`0x54F130`): a total, a threshold of 3 and four category counts. `0x54F1A0` counts a row up; when the total reaches the threshold
  it posts advisor message 0x198 (threshold 3) or 0x15D, unless the insert is a load, and the threshold doubles. `0x54F320` counts down; the threshold stays.

## 2. The letters (play and debug senders)

Only message type 0x19 (the letter wrapper `0x435DC0`, vtable `0x93A388`) reaches the tag system (`0x637970` case 0xC). The posts in normal play are:

| Sender | Letter | Row |
| --- | --- | --- |
| Mission controller update `0x5EF8F0` (post `0x5EF991`) | "Objective Complete!", `0x5CEAE0(counter - 1)`, kind 4 | tag 0x8A, go-to by mission (`0x5CEC30`), category 0, unread |
| Arrival flyover's end `0x5F1760` (post at `0x5F18DA`) | briefing, tag letter `0x5CD6E0(0x5C, 5, 1)` | tag 0x5C, the tag record's texts, category 0, unread |
| Bank `0x5C0969` | red-month notification `0x5CEE50(8)` | tag 0x5E, category 2, read; the letter window opens |
| Challenge system `0x5C48DD` | firing notification `0x5CEE50(7)` | tag 0x5F, category 2, read; the letter window opens |
| Staff wage review | wage demand `0x5CEFB0(0xA/0xB, member, shortfall)` | not ported yet (staff package) |
| Staff training | `0x5CEFB0(0xC/0xD, member, ability)` | not ported yet (staff package) |
| Challenge won | promotion letter `0x5CF4D0` (id 0xE) | not ported (OpenTPW composes no promotion letter) |

The other posts are debug pages. `0x538030` is the window procedure of a page that `0x5379C0` builds; `0x5379C0` is reached only from the debug menu's procedure
`0x532240` (built by `0x531BC0`, table `0x9B61EC`), which opens from the native HUD's control 0x4F and the key table's F12 "debugpanel" row (`0x9A89D8`). Its first
button logs "Hire a handyman you fool!". Its letter buttons post kind 13 (`0x538135`), kind 10 with `rand()%200` (`0x538325`, a test wage demand: `0x5CEFB0(10,
member, rand()%200)`), kind 6 tag 93 (`0x53841B`), the briefing (`0x538595`) and an objective letter with its own serial `0xA5E108` (`0x538648`). `0x535380`
(page `0x5351C0`) and `0x535E56` (page `0x535BF0`) belong to the same menu. The cheats page `0x532A40` (Skip Mission, Get Promoted) is a sibling; OpenTPW's
cheats extension offers only those two buttons, so no debug letter is ported, and the `rand()` at `0x5382A2` is not on a play path.

OpenTPW files the objective letter, the briefing and the two notification letters (`ParkEconomy.FileThemeParkIncLetter`, `PostThemeParkIncMail`). A row keeps
the letter it came from (`ThemeParkIncTagTubeRow.Source` and `Argument`) and the record's numbers; its texts are composed from the same tables when the window
shows it, so they follow the selected language where the original keeps the language of the moment it filed the row.

## 3. The window

Window 3000, table `0x9BBC84` (`ThemeParkIncLayoutTables.TagTube`), builder `0x551CF0`, procedure `0x5517B0`, the window object `0xA60188` (constructor `0x551C60`).

- **Openers.** The plain M key (row `0x9A89EC`, handler `0x42EED0`, not in game mode 3) and the native HUD's mail panel 0x45 (control 0x46). The builder does nothing
  while the window exists; otherwise it closes the HUD's window (a letter, the mission window) and registers itself. OpenTPW has the key and a text HUD button
  (`[EXT:tpi-text-hud]`) that says the window's title and the panel's unread number. Nothing pauses.
- **The list** 3006 is sorted (attribute 0x211): column 0 the subject (`+0xC04`), column 1 the date (`+0xE24`, printed as UITEXT 956). The sort key `+0x68` starts
  at -2 (the date, newest first) and survives the window and every park of the game session (the window object `0xA60188` is static, built once by the startup initializer `0x551190`); a header press turns the direction of its own column or sorts another column ascending (`0x85782C`).
  Text compares as `wcscmp`; equal items keep their insert order. A read row is grey, an unread row white, and an unread row of a category above 0 flashes every
  300 ms in its category's colour (`0x5528B0`).
- **The refresh** (`0x552CF0`, on the dirty byte) rebuilds the list and scrolls the previously selected row into view without selecting it; a non-empty list enables
  Delete and hides the empty label (UITEXT 211), an empty one disables Delete, shows the label and clears the details.
- **A selection** (list message 0x401, `0x553550`) shows a different row's details (`0x553100`: From `+0x808`, To `+0xA06`, Subject `+0xC04`, the body `+0x8`,
  the picture, the go-to button when `+0xE10` is set), reads the previously shown row and starts the read deadline: 2.5 seconds (`+0x60`).
- **Reading** (`0x553480`): the tick reads the shown row at its deadline; another selection or closing reads it when it was shown longer than half a second (the
  deadline less `+0x64`, 2000). **No row click opens the letter window**: `0x526CD0` has one caller, the row builder. A double-click go-to (`0x553640`) exists but
  nothing references it.
- **Delete** (3004, or the Delete key) disables the button and deletes the selected row on the next tick. **The go-to** (3019) reads the row, closes the window and
  runs `0x5511B0`, as the letter window's go-to.

## 4. The save

The park save's thing chunk `SGAT` (save `0x64B107`, serializer `0x6355D0` at `0x64B12D`; load case 12 at `0x64CF1D`, the `SGAT` test at `0x64CF40`, the
serializer at `0x64CF7C`) holds `numtags` and one `MailMessage` per unflagged row: the 0xE28 record bytes, the flag byte and three bytes of padding. The load
resets the tube and the counter and appends every row through the insert, so the expiry restarts from the load's clock and the unread rows count silently. The
Science continuation save keeps the unflagged rows (`ParkSaveFile.Data.TpiTagTube`); a Science save from before the tube is refused before its assets are
checked, because the arrival's briefing and every objective letter would have been unread rows whose count drives the unread advice.

## 5. Not ported

- The advisor's unread messages 0x198 and 0x15D (descriptors `0x9E3F80` and `0x9E3FB8`, group 0, responses 739 and 652..653) and the go-to hint 0x211
  (`0x9E5B80`): Theme Park Inc's advisor plays only the weather forecasts, and the speech choice among two responses is not read (`[EXT:tpi-capability-gates]`).
  The tube counts and doubles its threshold exactly; the client notes the message. Porting the speech means running the advisor's playback (queued by `0x718820`), which draws
  the C runtime's `rand()` three times, all presentation: the talking-animation segments (`0x715AC4`, GenerateAnimSequence `0x715AB0`), the stop sound (`0x716A2C`, `0x716950`) and the
  mouth frame (`0x717175`). That port waits on the user's decision about those draws.
- The native mail panel's picture by category (control 0x47) and its chime (sound 0x1E of the bank `0xAE0124`), and the help button's advisor help (`0x503A40(0x1AF)`).
- The body's scroll text (message 0x800 with the slider 3017).
- The tag-definition rows (branch 0x18) and branch 0x1B of the row builder, which no ported sender posts.
- Delete by key (`0x636B10`): it flags every unflagged row whose `+0xE14` is a removed thing's id (callers `0x618790`, `0x629040` and `0x63A1E0` on message 0x21). Every ported row has key 0, so no removal reaches one.
