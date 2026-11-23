## Theme Park Inc: park awards and the shares game (November 23, 2026)

Theme Park Inc's park awards and its shares game now run, so the last mission (miss17: six park awards and 51 % of the company's shares) can be completed.

- **Award check.** Every 100th world tick, after the calendar, the check `0x606BE0` measures the park against `levels/Standard.sam`'s `ParkAward` thresholds in its order and decides at most one award (`0x5D6600`). Six of the eight measures are ported: the cleanest park (`0x5B80D0`, `0x5B8E70`), the most decoration (`0x5B5680`), the best security (the guards' patrol areas `0x5B8A70` plus the secured cells `0x5B8D10`), the best staff training (`0x5B3460`, `0x5B2F40`), the go-kart track's longest straight (`0x5B6D20`) and the water ride's sections (`0x5B6FA0`). The storage `0x74C760` marks the career, makes the current zone the holder and moves an award to the zone that beats its holder's value.
- **Shares game.** The shares game `0xAA0128` is kept with the career: the reset `0x621210`, the start-up's 1000 shares at a price of 100 (`0x620930`), a won challenge's share prize (`0x621CE0`) and price effect, a slot's first loss's price fall (`0x621D30`), and the annual report's score and price step (`0x621F50`). The thirteen share prizes add up to exactly the 15300 shares miss17 needs.
- **Exit statistics.** Types 6 (the awards held, `0x74C860`) and 7 (the shares owned × 100 / 30000, `0x5EFF9F`) are measured and decided.
- **HUD.** The text HUD's challenge panel shows the shares owned, the percentage, the price and the awards held, and an award decided becomes a note, in the game's own UITEXT words.
- **Cheat.** The cheats extension adds the debug menu's "Win the game..." (`0x53835D`): 51 % of the issued shares.
- **Saves.** The shares go into the player file (as the original's 182-byte section) and the Science continuation save; a Theme Park Inc save from before the shares game is refused before its assets are checked.

Labels: `[EXT:tpi-capability-gates]` now also covers the two coaster awards (their measure needs the attraction window's byte `+0x3F`, the month byte `+0x40` and the coaster track record), the annual report at the year change (it reads analyser rings OpenTPW does not keep), the award advice (the advisor's descriptor rows for its ids are not read) and the end-of-game letter of "Win the game...". `[EXT:tpi-text-hud]` covers the panel line and the notes in place of the native share portfolio and Park Awards panels. No new approximation or label is added, and no CRT `rand()` draw is reached: the shares panel's colour draws (`0x5813A1`, `0x581ABE`) belong to the native panel, which is not ported.

Verified with these checks:

- New tests for each award measure at its threshold, one award per check, the 100-tick gate, the storage's won, held and moved rules, the share price on a win, a loss and the annual score, the share prize and the percentage, miss17's statistics 6 and 7, the cheat, the player-file and park-save round trips and the refusal of an older save; with the retail data, the bound Standard words equal the keyed thresholds and the thirteen share prizes reach exactly 51 %.

The original game was not launched, and no binary or data is included.
