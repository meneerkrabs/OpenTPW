# Theme Park Inc: park awards and the shares game

This trace uses the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) and the retail data. It was
read statically (listings and Ghidra decompiles); the game was not launched. OpenTPW's port is `ThemeParkIncParkAwards`, `ThemeParkIncSharesGame`, the career's
`StoreAward` and `Shares`, and the economy's award check (`ParkEconomy.ThemeParkIncAwards.cs`). "Park" is `*0xADE6B0`, "career" `*0xBFC484`.

## 1. When the awards are checked

- [BIN:TPI-EXE:0x5D65E0 world update tail] The world update object's tail runs the staff sub-object (`+0x44C`), the hiring pool (`0x631B40`), the calendar (`+0x428`,
  `0x6083D0`) and falls through into the award gate `0x5D6600`.
- [BIN:TPI-EXE:0x5D6600 award gate] With `tick = park + 0x1EAB40`, `tick divl 100` with a remainder of 0 and the screen state `0x6A1C70()` (`[0xB8B91C]`) not `0x11`
  calls `0x606B30` with the object's `+0x448`. The screen table `0x6A0FB0` fills has no handler in slots 16 and 17; OpenTPW's park runs in the in-park screen 10.
- [BIN:TPI-EXE:0x606B30 award control] A first-call static init (`0x6A5EF0`), then the check `0x606BE0` only when the mode word `[0xC65B80]` is 0 (the career game).

## 2. The check and the storage

[BIN:TPI-EXE:0x606BE0 award check] The awards are tested in this order; every comparison is signed (`jl`), and the first award whose storage returns a nonzero result goes to
the event `0x606EC0(i, result)` and ends the scan. The thresholds are the Standard object's words `0xA028BC`..`0xA028E0` (`ParkAward.*`, `levels/Standard.sam` lines
1045 to 1054); `MinCellsOwned` and `MinCellsCovered` (`0xA028E4`, `0xA028E8`) are not read.

| i | award (UITEXT 453 + i) | test | value stored |
| ---: | --- | --- | --- |
| 0 | Highest Coaster | `0x5B7210` ≥ `CoasterHeight` (110) | the measure |
| 1 | Fastest Coaster | `0x5B7380` ≥ `CoasterSpeed` (100) | the measure |
| 2 | Cleanest Park | 100 − `0x5B80D0` ≥ `Hygiene` (90) and `0x5B8E70` ≥ `HygieneMinParkSize` (1000) | 100 − littered % |
| 3 | Most Decoration | `0x5B5680` ≥ `Decoration` (15) | the measure |
| 4 | Best Security | `0x5B8A70(7, 0)` + `0x5B8D10` ≥ `Security` (25) | the sum |
| 5 | Best Staff Training | `0x5B3460` ≥ `Training` (50) and `0x5B2F40` ≥ `TrainingMinStaff` (10) | the trained % |
| 6 | Fastest Go-kart Track | `0x5B6D20` ≥ `GokartSpeed` (5) | the measure |
| 7 | Longest Water Ride | `0x5B6FA0(.., 2)` ≥ `WaterLength` (50) | the measure |

[BIN:TPI-EXE:0x74C760 award storage] Career byte `+0x2A + i` not 1: it becomes 1, the current zone record (`career + 0x4C`) takes the flag `+0x20 + i` = 1 and the value
`+0x28 + 4i`, result 1 (won). Otherwise the current zone holding it returns 0. Otherwise the zone list (`career + 0x40`, the record at node `+0x20`) is walked: a
zone holding it with a value below the new one (signed `jg`) is cleared and the current zone takes the flag and the value, result 2 (moved); none gives 0. The career byte is
never cleared; the exit statistic 6 counts these bytes (`0x74C860`). A second entry `0x606E40(i, value)` runs the same storage and event.

## 3. The measures

All cell measures walk the 128 × 128 cell records (`park + 0x460 + 0x48 × (word − 1)`, the word `(y << 7) + x + 1`) from `0x7F` down to 0; the order does not matter.
The cell type is the record's dword `+8` (`0x67F590` 2, `0x67F5B0` 1, `0x67F5D0` 3 or 9, `0x67F600` 9, `0x67F620` 10, `0x67F660` 7 or 0x1E, `0x67F690` never,
`0x67F6B0` 4, `0x67F7B0` 0x15). Every percentage is `m × 100 / n` with `cltd; idivl`, and 0 when `n` is 0.

- **Littered share** `0x5B80D0`: `n` counts the cells of type 1, 3 or 9 or with litter (the record's `+0x30`, Theme Park World's `+0x2C`); `m` the littered ones.
- **Covered cells** `0x5B8E70`: types 9, 10, 4 and 0x15, and the cells whose second-layer record is a track kind (`0x67F890`: the record through an extension's parent
  word, then `0x6856D0`: kinds 0xB, 0xD, 0x10, 0x12, 0x19; Theme Park World's `0x5363F0`).
- **Decoration** `0x5B5680`: the sum over class-3 things whose type has `Info.WhichUIType` (`+0x600`) 3 of the `Info.Shape` record's `+0 × +4` (`0x5DF700`; the map parser
  stores the widest row's column count and the row count there, `0x41AF7B`, `0x41AF82`), × 100 / the covered cells; a sum of 0 gives 0 (a nonzero sum over no covered
  cell would fault).
- **Patrolled share** `0x5B8A70(7, 0)`: `0x5B86C0(7)` clears every cell's `+0x40` and marks the patrol rectangle of each guard (class 7) whose rectangle is not the whole map
  (`0x62C5C0`: the corner words of `+0x24C/+0x250` and `+0x254/+0x258` are not 1 and 0x4000); mode 0 (jump table `0x5B8C68`) counts the land cells (not types 2, 7, 0x1E) and
  the marked ones. OpenTPW has no patrol-area tool, so every member has the whole map and the share is 0 as in the original for such a park.
- **Secured share** `0x5B8D10`: of the same land cells, those whose region-grid record (`0x5D7420`, ten bytes a cell) has its security halfword `+6` above 0.
- **Trained share** `0x5B3460`: of the staff things (classes 4, 5, 6, 8, 7, 0x14), those whose `+0x218` (the trained ability) is nonzero. `0x5B2F40` counts them.
- **Best kart** `0x5B6D20` and **best track ride** `0x5B6FA0(.., 2)`: class-3 things that are open (`0x5E3C20`: the open word `+0x7C`), of `Info.WhichUIType` 0 and track
  type (`+0x14C`) 1 or 2: the kart track's second output of `0x694530` (the longest run of straights over the doubled section list) and the water ride's result (the
  sections). `0x694530` is Theme Park World's `0x545310` for both (`BumpController.SectionStatistics`); it counts an add-on only when its `+8` is 0, which neither reads.
- **Coasters** `0x5B7210`, `0x5B7380`: class-3 things with track type 3 (`0x5E80D0`), open, with the bytes `+0x3F` and `+0x40` set; the value is the float `+0x110`
  (height) or `+0x114` (speed) of the track record (`0x480C00` over `0x485AD0` → `0x476AE0`), truncated, the highest kept. `+0x3F` is set only by the attraction window
  (`0x52F570`, `0x52F870`); `+0x40` by every month message (`0x5DF04C`).

## 4. The event

[BIN:TPI-EXE:0x606EC0 award event] Logs "Won a Global golden Award (type - result, num - i)", then the advice through `0x71A930` and a message object posted to `0xA09728`.
Won (jump table `0x607AB0`): 0xBF, 0x13A, 0x13B, 0x13C, 0x13D, 0x13E, 0xC0, 0xC1. Moved (`0x607A90`): 0xC3, 0x142, 0x143, 0x144, 0x145, 0x146, 0xC4, 0xC5. Then the Park
Awards panel's refresh `0x561E30`. No money moves and nothing is drawn. The check then logs "You have %d total Park Awards".

## 5. The shares game

[BIN:TPI-EXE:0x620870 shares constructor] `0xAA0128` holds an owner record (`+0` `mNumberOwned`, `+4` `mPctCompanySharesOwned`), `0xAA012C` a company record (`+0`
`mCurrentValue`, `+4` `mCompanyName[31]`), and `+8`..`+0x10` three annual-report slots. There is no rival holder; `CompanySharesIssued` (`0xA02168`, 30000) is only a divisor.

- **Reset** `0x621210` (from the career reset `0x74BB40` at `0x74BE97`): no slot, holding, percentage and price −1, the name empty.
- **Start-up** `0x620930` (every world build, `0x648676`): a holding of −1 takes `InitialSharesOwned` (1000) when it is above 0; a positive holding recomputes the percentage
  when the issued count is above 0; an empty name is formatted from `TAG_SYSTEM.str` entry 0xAC; a price of −1 takes `InitialSharePrice` (100). The load `0x620E30` ends
  with the same steps (`0x622060`, `0x6223D0`).
- **Gift** `0x621CE0(n)`: for `n > 0` the holding grows (uncapped) and the percentage is recomputed. Callers: a won challenge's `SharePrize` (`0x5C4344`) and the debug menu
  (`0x538353` +10, `0x538384` +51 % of the issued shares).
- **Price effect** `0x621D30(p)`: a price or effect of 0 does nothing; else `price + price × p / 100` (32-bit, the division toward zero) is stored when above 0.
  Callers: a won challenge's `SharePriceEffectIfWon` (`0x5C4352`), a slot's first loss with the negated `SharePriceEffectIfLost` (`0x5C4867`), the debug menu's 5 % (`0x5384CF`).
  Every one of the thirty challenges has 4 and 2.
- **Annual report** `0x621300(year, zone, 12)` at the calendar's year change (`0x6086DE`): unless the mode word is 3, the zone is above 2, the year is below 2000 or the
  zone's slot already holds a report for that year or later, it builds a report from the analyser's rings (money in `+0x1FEF8`, park value `+0x1F368`, money out `+0x1F808`,
  `0x5B2580`, the ring of `0x6226D0`), and against the previous report a delta record (`+0x28`); the score `0x621F50` is the sum of the five deltas' signs clamped to ±3, and
  the price moves by it as a percentage (`0x62191F`). Then the annual text and the advisor's 0x1CA.
- **Global Master Save** `0x6209F0` (from `0x74EC8D`): the 182 bytes (per slot the present word, the year, three words and two doubles, 36 bytes; the holding and the
  percentage; the name; the price). The write formats an empty name first; the read's result is ignored.
- **Miss17**: 1000 shares from the start-up and the thirteen `SharePrize` values of `Challenges.sam` (rows 8, 14, 20 to 30: 600, 1900, 2000, 3000, 400, 400, 500, 650, 700,
  450, 2500, 600, 600, together 14300) are 15300 shares, exactly 51 % of 30000 (`0x5EFF9F`: owned × 100 / 30000). Every share-prize challenge must be won. OpenTPW offers none of them yet: their types (17, 46, 48, 50 to 53, 55 to 58) are not among the measured challenge types (2, 25 and 41, `ThemeParkIncChallenges.IsPorted`), so until those types are ported, 51 % is reachable only through the cheats extension's "Win the game...".
- **The debug menu** (procedure `0x538030`; the button is built at `0x537B7F`): "Win the game..." gives 51 % of the issued shares and posts the end-of-game object
  `0x5CD6E0(0x5D, 6, 1)`, the same object the completion tick of the last mission posts; "Award 10 Shares", "Increase share value by 5%" and "Trigger Next Annual Report"
  are its other share buttons.

## 6. What OpenTPW ports and what it gates

Ported: the gate, the check in its order, the storage, the six measures above, the event's notice, the shares game's reset, start-up, gift, price effect, annual score and
price step, its GMS section and the Science continuation save's copy, exit types 6 and 7, and "Win the game..." in the cheats extension.

Gated (`[EXT:tpi-capability-gates]`):
- the coaster awards: OpenTPW keeps neither the attraction window's byte `+0x3F` nor the month byte `+0x40`, and the coaster track record's `+0x110`/`+0x114` is not checked
  against Theme Park World's rating; a coaster award is never decided;
- the annual report at the year change: its rings are not kept as Theme Park Inc's analyser; the saved report slots are carried through;
- the award advice: the advisor's descriptor and response rows for these ids are not read (the advisor queue takes only the forecasts and the letters so far, [TPI-ADVISOR.md](TPI-ADVISOR.md));
- "Win the game..."'s end-of-game letter.

`[EXT:tpi-text-hud]`: the native share portfolio panel (`0x581A90`, refresh `0x582890`) and the Park Awards panel (`0x5625D0`..) are not ported; the challenge panel shows
their values in a line (UITEXT 24, 23, 25, 452 to 460), and an award decided becomes a note.

## 7. rand()

None on the paths ported: the check, the measures, the storage, the event, the gift, the price effect, the score, the start-up and the save have no call of the CRT
`rand()` (`0x870D38`) or the world generator. The shares panel's colour draws (`0x5813A1`, `0x5813B6`, `0x581ABE`, `0x581AD4`) belong to the native panel, and the daily
update of `0xAA0180` (`0x622F80`, `rand()` at `0x622F8E`) is not part of the shares game; neither is ported.
