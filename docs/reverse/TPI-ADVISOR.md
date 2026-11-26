# Theme Park Inc advisor

This trace uses the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) and retail data. The game was not launched. It supersedes `TPI-ADVISOR-FORECAST.md`, whose forecast evidence is kept in §5 and whose wrong claims are listed in §7.

The port is planned in phases P1 to P9. P1, the queue, is in OpenTPW (`ThemeParkIncAdvisor`); §6 lists what is not ported yet.

## 1. The advisor thing and its update

- [BIN:TPI-EXE:0x64836F advisor thing] The new-world init builds the advisor (thing model 11) before the tag system (`0x6483B7`). The world pass updates it through the class switch (`0x6376C7` → `0x637704` → `0x718190`), skipped while the mode word `0xC65B80` is 1 (an online game). A lost park's thing pass does not run, so neither does the advisor.
- [BIN:TPI-EXE:0x718190 advisor update] Each update has three parts:
  1. **Delayed window help.** While `+0x10D` is set and less than `GeneralAdvisor.UIPanelDelay` has passed since `+0x110`, the update goes straight to the playback. Once the delay has passed, `+0x10D` is cleared, the message `+0x114` is posted with its descriptor's score, and the update returns.
  2. **Intake.** The condition scan `0x71C0B0` returns one message per call (one scan row per world tick). The intake is skipped while the animation word (`0x5D0820`) or the game object's `+0x2F` is set.
  3. **Playback** (`0x71830E`). It waits until the presentation clock reaches `+0xE4` + `+0xE8`. A set `+0x104` is cleared and skips one playback. The selected slot's score must be strictly above `MinScoreForConsideration`.

## 2. Descriptors and settings

- [BIN:TPI-EXE:0x729530 descriptor initializer] The table at `0x9DE020` has 664 rows of 56 bytes, then a terminator (kind 2; the row count `0x9E7198` is 665). There are 606 distinct message ids (`0x0`–`0x283`). 290 rows are the condition scan's (kind 0); the rest are posted by code (kind 1). Row layout: `+0` kind, `+4` id, `+8` group (0–9), `+0xC` class, `+0x10` callback, `+0x14` callback offset (0 everywhere), `+0x20` pending-duplicate limit (byte), `+0x24` first response, `+0x28` response count, `+0x2C` variant rule (2 in every row), `+0x30` `0x285` everywhere, `+0x34` gate (set when the scorer loads, `0x73D480`).
- `content/data/tpi-advisor-descriptors.toml` holds all 664 rows. For the 165 rows whose callback is a constant (`mov eax, imm32; ret`) it also holds the score (0, 2500, 5000 or 10000). `tools/ppc-analysis/oracle_x86/tpi_advisor_queue_oracle.py` writes the file and checks it against the native initializer.
- [BIN:TPI-EXE:0x71B770 descriptor lookup] Every lookup takes the first row of a message in table order and refuses `0x285`. Ids `0x58`–`0x5C` do have rows (group 7).
- [BIN:TPI-EXE:0x71B6B0 scorer constructor] The scorer `0xC65220` binds `Advisor\Advisor.sam` through the schema at `0x97C180` (`GeneralAdvisor`, then `MessageGroups[10]` with six words each, then `GroupTimes` and the condition keys). The scorer's `+0x20` is `MinScoreForConsideration`, `+0x24` `UIPanelDelay` and `MessageGroups[g]` starts at `+0x2C + 24·g`. A key the file does not set keeps the reset value; the shipped file has no group 8, so the mail's group is all zeros. OpenTPW binds the schema up to `MessageGroups` through `ThemeParkIncSamObject` (`ThemeParkIncAdvisorSettings`).

## 3. Records, queue and history

- [BIN:TPI-EXE:0x71A930 advice record] A record is 24 bytes: `+0` and `+4` the locate word and dword, `+8` the variant (-1: the rule chooses), `+0xC` the message, `+0x10` the score, `+0x14` valid, `+0x15` the bypass byte, `+0x16` explicit score. A score below 0 asks the descriptor (`0x71B7E0`) and leaves `+0x16` clear.
- [BIN:TPI-EXE:0x71B7E0 advice score] -1 for no row or a class the context refuses; otherwise the row's callback.
- [BIN:TPI-EXE:0x718820 advice enqueue] A group-1 record is first copied to `+0xEC` (`mRequestedLastTutorial`). Then the acceptance runs. A free slot takes the record. On a full queue, `0x718E90` names the first lowest-scoring valid slot that is not group 8, and the record replaces it only on a strictly higher score; an equal score still reports success. A queue full of group-8 records refuses. With `+0x10C` set, the message becomes the help button's (`0x9DDBEC`).
- [BIN:TPI-EXE:0x718B40 advice acceptance] The checks run in this order:
  1. With `TutorialOn` (`0xA10219`) off, a group-1 message is refused (`0x71C030`).
  2. When the history's time `>> 2` is not 0, the message is refused while `(world word >> 2) − (time >> 2)` (unsigned) is below the group's interval for the play count: `MinTimeSameMessage1`, `2`, `3` for 0, 1, 2 plays and `X` for any other count (`0x71BAC0`).
  3. Unless the record's bypass byte or `+0x10C` is set: `SayOnlyOnce` with the played byte refuses (`0x71BB90`); a nonzero `DiscardAfterSlaps` with slaps ≥ that limit refuses (`0x71BD20`, never for group 10).
  4. The pending records of the message must be below the row's limit byte (`0x71BA40`).
  5. Unless `+0x10C` is set: variant rule 0 whose last variant is the last response refuses.
- [BIN:TPI-EXE:0x718F80 advice selection] Over the valid slots with a row (only groups 1 and 8 while the advisor panel `0xA568BC` is up): a group-8 slot with a positive score is taken, the last such slot winning. Otherwise the first strictly higher score than the best so far wins, starting from 0.
- [BIN:TPI-EXE:0x718990 advice cancel] A stored tutorial record of the message is dropped and every pending slot of it cleared. When the presentation clock is inside the window and `+0x108` is the message, the speech stops (`0x716950`), the history's played byte is cleared, its slaps raised and the window set to 0.
- [BIN:TPI-EXE:0x7185A8 advice history] The history is per player: the career (`0xBFC484`, `mHistory` at `+0x14F0`, 20 bytes per id): `+0` the world word at the last play, `+4` its variant, `+8` the played byte, `+0xC` slaps, `+0x10` plays. It survives across parks in the career file. OpenTPW reads and writes it in `ThemeParkIncCareer.History`, which the career file (GMS) and the park save carry.

## 4. Playback

- [BIN:TPI-EXE:0x7183B1 variant rule] A slot without a variant asks the rule with the history's last variant: rule 0 takes the next until the last, then the message id; rule 1 the next, staying at the last; rule 2 the next; rule 3 a world-generator draw modulo the count (`0x71847D`, unreachable, since every row has rule 2); any other rule the message id. A result below 0 or at or above the count becomes 0.
- [BIN:TPI-EXE:0x71A9F0 default-score playback] The slot is consumed, the descriptor scores again (`0x71B870`, which also writes the locate words) and a score below the minimum fails. The response is the base plus a variant below the count, otherwise the base. Then the speech (`0x716230`), the group's gate timer reset (`0x73D3F0`) and the caption broadcast (kind `0x18`, unless the tag is `0x97`).
- [BIN:TPI-EXE:0x71AD10 explicit-score playback] No second score and no gate timer; the tag is broadcast without the `0x97` test.
- [BIN:TPI-EXE:0x718584 playback window] On success the start is the presentation clock and the window the speech's return plus 1000. The history gets the world word, the variant, played 1 and one more play; `+0x108` is the message.
- [BIN:TPI-EXE:0x716230 speech] The speech returns the advisor's sequence length plus the closing clip plus 1000, or 0 when it does not speak. The sequence is generated (`0x715AB0`, CRT `rand()` at `0x715AC4`) or canned (`0x715BC0`); the forecasts use canned sequence 9. So the pause before the next advice is the sequence plus the closing clip plus 2000 ms, not the voice plus 1000 ms the old port held.

## 5. The weather forecasts and the letters

- [BIN:TPI-EXE:0x6420EB forecast advice] The weather's month handler rolls the next spell and posts its forecast with the descriptor's score (`0x71A930`, score -1) as a kind-5 message, which the advisor's handler (`0x7194A4`) passes to `0x718820`. Ordinary weather posts nothing.
- [BIN:TPI-EXE:0x6362AA letter advice] The tag system's row builder posts a notification letter's advice (`0x1A1`, `0x1A2` or `0x1A3`) to the queue the same way before it inserts the row; it is spoken by the playback, not by the letter.

| Next spell | Message | Row | Response | Speech `+4` / `+8` | Tag | `TAG_SYSTEM.str` |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Heatwave | `0x154` | 427 | 642 | 763 / 763 | 62 | 127 |
| Rain | `0x157` | 430 | 645 | 766 / 766 | 65 | 130 |
| Snow | `0x15A` | 433 | 648 | 769 / 769 | 68 | 133 |
| Storm | `0x15B` | 434 | 649 | 938 / 769 | 71 | 156 |

The four forecast rows are class 2, group 0, limit 255, one response, and their callbacks return 10,000. The letters `0x1A1`–`0x1A3` are group 8 (responses 748–750, speech 868–870). `tpi_advisor_forecast_oracle.py` checks these rows, the responses and the captions.

[BIN:TPI-EXE:0x71BDC0 class check] The context admits a class as follows:

| Context | Classes admitted |
| --- | --- |
| mode word 3 | 3 |
| game object `+0x2E` set | 5 |
| game object `+0x2F` set | 7 |
| cutscene playing (`0xA9D230`), mode 0 / 1 / 2 | 6 / 5 / 4 |
| otherwise | 2; 0 in mode 0; 1 in mode 2 |

## 6. What OpenTPW models

- `ThemeParkIncAdvisor` is a world system. The level's thing sweep updates it at the advisor's slot (model 11); a headless economy updates it after the tag system. It holds the eight slots, the stored tutorial record, the window, `+0x104`, `+0x108`, `+0x10C`, the delayed-help fields and two player inputs, `TutorialOn` and the advisor panel. The Science save keeps all of it as `TpiAdvisor`. A save from before it (without `TpiAdvisor`, or with the old `TpiForecast`) is refused before the asset check. A Theme Park World save that carries it is refused. The help button's two words are statics and start fresh after a load (D-5).
- **Clock** (`[EXT:tpi-advisor-tick-clock]`, user decision D-1): the presentation clock is the world tick times 248 ms. It stops while the park is paused and is the same on replay, load and co-op. At a speed other than normal, the window spans the ticks of normal speed.
- **Context:** the mode word is 0 (the career). The cutscene's thing and mode are the animation word and its variant. The game object's `+0x2E`/`+0x2F` are not modelled and read as clear.
- **Gated** (`[EXT:tpi-capability-gates]`):
  - The condition scan, its gates and its callbacks (P3, D-6). `ThemeParkIncAdvisor.ConditionScan` is off.
  - The speech duration. Until the sequences and the advisor model come with the presentation (P2), the duration is the response's voice length, decoded from its sample in the speech data in the simulation (`ThemeParkIncAdvisorSpeech`). That is the window the old port held, now deterministic. A response without a voice has duration 0, so its window is 1000 ms.
  - The response table: only the seven responses above are known (`ThemeParkIncAdviceResponse`). The caption is the forecast line, OpenTPW's stand-in for the kind-0x18 tag-tube row (P2).
  - Every other sender (P4, P5): the unread-mail advice, the wage demands, the staff and research messages are noted, not posted.

## 7. Corrections to `TPI-ADVISOR-FORECAST.md`

- The condition table is referenced: the scan `0x71C0B0` evaluates one row every world tick (§1).
- The table has 664 rows and 606 ids, not 518 rows and 501 ids; `0x58`–`0x5C` have rows (§2).
- The variant rule is row `+0x2C`, not `+8` (`+8` is the group) (§4).
- `0x606EC0(award, won|moved)` is called from `0x606D99` and `0x606E69`; the pushes of 6 and 7 belong to `0x74C760`.
- The playback window is the speech's return plus 1000, and the speech's return is the sequence plus the closing clip plus 1000 (§4), not the voice length plus 1000.
- The tag system subscribes to kind `0x18` (`0x635A63`): every captioned advice files a tag-tube row (branch `0x18`, see `TPI-TAG-TUBE.md`).
