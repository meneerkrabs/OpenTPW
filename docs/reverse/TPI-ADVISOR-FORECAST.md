# Theme Park Inc advisor: weather forecasts

This trace uses the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) and retail data. The game was not launched. This is evidence for the original forecast path, not an OpenTPW advisor implementation.

## Trigger and recipient

- [BIN:TPI-EXE:0x64209B forecast trigger] The weather month handler rolls the next spell, then dispatches on its kind at `0x6420AF`. Ordinary weather bypasses advice. The advice belongs to the newly rolled spell, not to the spell just started.
- [BIN:TPI-EXE:0x6420EB advice construction] The forecast uses the default-score path of `0x71A930`, then constructs a kind-5 message at `0x4355D0` and posts it to the synchronous message centre at `0x436DD0`.
- [BIN:TPI-EXE:0x7176E3 advisor subscription] The original model-11 advisor is created by the world setup at `0x64836F` and subscribes to kind 5. The recipient at `0x7194A4` checks the advice record's valid byte before submitting it to `0x718820`. Forecasts are queued, not played in the month handler.

## Descriptor and presentation data

[BIN:TPI-EXE:0x729530 descriptor initializer] The shipped descriptor region at `0x9DE020` is largely zero in the file. The initializer populates its 56-byte rows; a first word of `2` stops the scans. The four forecast rows have first word `1`, eligibility class `2`, response count `1` and pending duplicate limit byte `255`. The callbacks all return `10,000` when eligible. `0x71B7E0` returns `-1` if the eligibility check rejects the ID. In the oracle, a clear animation word with mode word zero and both game-object flags clear allows the four rows; mode word `3`, either game-object flag, or a set animation word rejects them (see the class table below). These are controlled native inputs, not documented names for all user-facing modes.

| Next spell | Message | Descriptor | Response | Response speech fields `+4` / `+8` | Tag | `TAG_SYSTEM.str` entry |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Heatwave | `0x154` | `0x9E3D88` | 642 | 763 / 763 | 62 | 127 |
| Rain | `0x157` | `0x9E3E30` | 645 | 766 / 766 | 65 | 130 |
| Snow | `0x15A` | `0x9E3ED8` | 648 | 769 / 769 | 68 | 133 |
| Storm | `0x15B` | `0x9E3F10` | 649 | 938 / 769 | 71 | 156 |

[BIN:TPI-EXE:0x70F59E forecast response rows] The contiguous response initializer fragment calls `0x715570` for each row. Response IDs are a different namespace from message IDs: rain response `0x285` is valid even though the descriptor lookup rejects `0x285` as a message ID. The response selection in `0x71A9F0` adds the supplied variant to the base when the variant is below the count, and otherwise uses the base. Each forecast's count is one. A second speech field in the storm row is not a second response; this trace does not establish when that field is used.

[BIN:TPI-EXE:0x716230 speech lookup] The native play path reads response field `+4`; `0x71661C` passes through `0x652860` to the audio system when its gates allow it. The separate lookup at `0x716D10` returns response field `+0x1C` as a tag. `0x71AC35` broadcasts it as a kind-`0x18` message unless it equals `0x97`; all four rows have a real tag. TPI uses its own 40-byte tag records at `0x9D3480` (`0x637000`), not the TPW tag table. The formatter at `0x6365A0` uses case zero and forwards tag field `+8` as the plain string entry for these tags.

The retail global speech catalogue contains events 763, 766, 769 and 938, each mapped to the same-numbered, one-based sample in bank 1. Each of those samples has a nonempty, in-bounds payload in `speechHD.SDT`. The English and Dutch `TAG_SYSTEM.str` files contain decodable nonempty entries at all four text indices. No original text, audio or executable bytes are included here.

## Eligibility classes and the current advice context

[BIN:TPI-EXE:0x71B7E0 advice score lookup] The lookup scans the descriptor table by message ID (`+0` is the row kind, `2` ends the scan; `+4` is the ID, `+0xC` the class, `+0x10` the score callback and `+0x14` the offset the callback receives on top of the advisor). It refuses the response ID `0x285` before the scan and returns `-1` when `0x71BDC0` rejects the row; otherwise the row's callback returns the score. `0x71BDC0` accepts a row only when its class is one the current advice context selects.

The oracle patches the heatwave row's class through all eight values in each context, and each context admits exactly one set:

| Context | Classes admitted |
| --- | --- |
| default, mode word 0 | 0, 2 |
| mode word 1 | 2 |
| mode word 2 | 1, 2 |
| mode word 3 | 3 |
| game-object byte `+0x2E` set | 5 |
| game-object byte `+0x2F` set | 7 |
| animation word set, variant 0 | 6 |
| animation word set, variant 1 | 5 |
| animation word set, variant 2 | 4 |

The four forecast rows are class `2`, so they are admissible while the mode word is anything but 3 and no other context is active, and are suppressed in every other context above.

[BIN:TPI-EXE:0x6A5EF0 mode word] The mode word is the four-valued static at `0xC65B80`. On first use it is derived from option bits `0x800000`, `0x1000000`, `0x2000000` and `0x20000000` of the game's packed option dword at `0xA2CA84`; the setter at `0x6A5FF0` writes the value and those bits together, and the settings loader calls it (`0x498A52` and the `0x6A49xx` ladder). [BIN:TPI-EXE:0x5BEE80 game flags] The bytes `+0x2E` and `+0x2F` are members of the global object at `0xA6E158`; the setters `0x5BEE80` and `0x5BEEC0` (thunks `0x405691` and `0x4066C7`) are called from sixteen sites, always with a literal 0 or 1, for example the window procedure at `0x4D0CB6` and the speech-finished handler at `0x5431F2`. The window handler at `0x530AC0` reads `+0x2E` (`0x530B3E`, `0x530B9A`, `0x530BE9`). [BIN:TPI-EXE:0x5D0770 animation word] The third input is the global word at `0xA9D230`, owned by the animation module (its neighbours log `playing anim %d` and `Attempt to play anim when one is already playing!`), together with the variant the resolved object carries at `+0x24`; the thing factory sets the word at `0x64D63C`, and the small predicates at `0x5D0770`–`0x5D0820` compare it, three of them also comparing `+0x24` with 0, 1 or 2.

[BIN:TPI-EXE:0x718227 advice intake gate] Before the update takes in any advice it jumps past the intake part while the animation word is set (`0x5D0820`, the same word the class check reads) or while the game object's `+0x2F` is set; the playback part at `0x71830E` still runs.

What each context means in the game is not established here. The code shows only that the class follows a persisted four-valued setting, two bytes of one global object, and the animation module's word; it does not name the modes.

## Who posts advice

[BIN:TPI-EXE:0x7176E3 advisor subscriptions] The advisor subscribes to message kinds `5`, `0x11`, `0x13`, `0x17`, `0x0E` and `0x08`. Its handler at `0x719470` dispatches on the kind minus five through the table at `0x71A40C` and appends one advice record per message through the advice constructor `0x71A930`, whose message-id field (`+0xC`, the same field the forecast fills with its message id) carries `0x58`, `0x59`, `0x5A`, `0x5B`, `0x5C`, `0xDB`, `0x11B`, `0x176`, `0x188` and `0x18C`; kind 5 takes the full descriptor-backed advice instead. Five of those ids do have descriptor rows (`0xDB` at `0x9E1CF0`, `0x11B` at `0x9E2CB0`, `0x176` at `0x9E4530`, `0x188` at `0x9E45D8`, `0x18C` at `0x9E4648`); the five small ones fall below the table's lowest id, `0x60`, so they resolve to no row at all. The table holds 501 distinct ids from `0x60` to `0x283` across 518 rows. Their message classes sit at `0x435A20` (kind `0x11`), `0x435B20` (`0x13`), `0x435CE0` (`0x17`) and `0x613B30` (`0x0E`); the senders found so far are the research module at `0x61E03E` and `0x61E120` (kind `0x11`, its records carry `mResearchGroup`), the bank at `0x5C0DB0` and the world controller at `0x64F91B` (kind `0x13`), the tag system at `0x633511` and `0x633720` (kind `0x17`) and the staff module at `0x61300C` and `0x614F3B` (kind `0x0E`, `Customer returning a costume.`). Kind `0x08`'s class returns `8` at `0x435740` (vtable `0x93A2EC`), its constructor at `0x435710` stores a word id, and its only caller is the shop module at `0x5E5261`.

[BIN:TPI-EXE:0x4355D0 advice message] The kind-5 advice message is built by one constructor (`0x4355D0`, thunk `0x4065BE`), and the executable calls it from twenty sites: the weather thing's forecast at `0x6420F5`, a window-module pair at `0x533AF3` and `0x533BA4`, one site at `0x430353`, and the sixteen template branches of the advice dispatcher between `0x606F5B` and `0x6079DC`.

[BIN:TPI-EXE:0x606EC0 advice dispatcher] That dispatcher takes an index and a value, selects a template from the jump table at `0x607A90` and posts the message through the message centre. Only indexes `6` and `7` are ever passed, both from the Park Awards routine (`0x606D54`, `0x606D82`, which logs `You have %d total Park Awards`), and no pointer to `0x606EC0` exists anywhere else in the file. So the other fourteen templates are unreachable in this executable.

Together with the unreferenced condition table above, that means this executable does not run the general condition evaluation its `Advisor.sam` names describe: the advice a Theme Park Inc park actually receives is the weather forecast, the bank's red-month mail, the challenge system's lost-challenge mail, the Park Award advice and five descriptor-backed short records from the research, bank, world, tag, staff and shop kinds; the five small ids those kinds also use fall below the table's lowest id and resolve to nothing.

## Queue boundary and verification

[BIN:TPI-EXE:0x718B40 advice acceptance] The acceptance path checks elapsed world time against a repeat-count-dependent interval when there is a previous playback timestamp. It also requires the existing exact-ID pending count to be below the descriptor limit (`0x718CBB–0x718D0F`). The queue has eight slots; on a full queue the incoming score must be strictly higher than the eligible slot it replaces (`0x718915`). The native function can report success without changing any queue bytes when the scores tie. With the shipped forecast duplicate limit of 255, a second pending rain forecast is accepted when the other checks pass; an exact-ID match alone does not suppress it.

[BIN:TPI-EXE:0x718190 advice update] The advisor waits until its presentation clock reaches the previous start plus the stored duration. It selects via `0x718F80`: among equal-scoring eligible forecast slots the earliest slot wins. The stored score must be strictly above the minimum. The default-score playback path re-evaluates the score (`0x71AA8D`), consumes the slot, and dispatches its response and caption tag. Word `+8` of every forecast row is 0, the message group; the same word also selects the variant rule, whose zero case rotates to the next variant and wraps at the response count (`0x718444`, `0x7184D9`). A one-response forecast therefore always plays variant zero and draws nothing from the world generator. On success the update stores the start clock, the returned voice duration plus 1,000 (`0x718584`), and separate per-message world-clock, variant and play-count history (`0x7185A8–0x7185DF`).

`tools/ppc-analysis/oracle_x86/tpi_advisor_forecast_oracle.py` replays the descriptor initializer, the response initializer fragment, 28 score cases, the 72 class admissions of the eligibility matrix, the four tag and caption formatter lookups, seven rain queue cases, and five rain playback ticks. The score cases cover the default context, mode word 3, the animation word and both game-object flags; the matrix patches the heatwave row's class through 0 to 7 in each of the nine contexts above. The queue cases compare the result byte and all eight slots: first admission, a pending duplicate, immediately below and exactly at a synthetic repeat interval, a full queue with equal scores, a full queue with a lower eligible score, and a valid record with an ineligible score of -1 entering an empty slot. The test interval is not a retail timing value. The playback cases cover the exact clock boundary, equality with the score floor, queue ordering on a tie, repeat playback, and the resulting history. That replay supplies a presentation clock and voice duration, no other advice candidate, and captures the speech and tag handoffs without rendering or playing them. Secondary advice-group history and the caption string fetch are outside the replay; the next boundary after this one is the original's mail presentation, which OpenTPW does not reproduce (see below).

## What OpenTPW now models

`ThemeParkIncForecastQueue` holds the eight slots, the four kinds' history and the playback window; `ThemeParkIncForecastSettings` reads `GeneralAdvisor.MinScoreForConsideration` and `MessageGroups[0].MinTimeSameMessage1/2/3/X` from the mounted `Advisor/Advisor.sam`. The economy posts the spell `ThemeParkIncWeatherSpells.OnMonth` returns when it rolls, at `Tick >> 2`, and saves the queue as an optional edition-validated `TpiForecast` payload. `ParkEconomy.TpiForecastEligible` reports the class-`2` verdict of the default context (mode word 0, no game-object flag, no animation): no other advice context is reachable on this path, so the four rows are admissible there; a context that becomes reachable has to change that verdict with it. The desktop slice ticks it on `SceneClock.Milliseconds`, plays the response's speech event from the global `cat_speech` catalogue and shows the tag's `TAG_SYSTEM.str` entry on a forecast line that holds until the next one.

Open boundaries, each needing its own evidence before the advisor is called complete: the game's four advice contexts above are pinned by the classes they admit, but the game's own names for them (the setting behind the mode word, the two game-object flags and the animation word's variants) are not established; the rounded speech duration comes from the played voice's own frames; the original's mail presentation (its panel, scroll and row actions) is represented by one caption line instead of the original's list, and the bank's red-month mail that reaches that list is traced in `docs/reverse/TPI-BANK-MAIL.md` but not modelled; and the caption, voice and window were observed together in a running retail Science park through the debug post (all four: heatwave 642/763/127, rain 645/766/130, storm 649/938/156, snow 648/769/133, each with its own voice length plus 1,000 ms), but not yet from a natural freak month.
