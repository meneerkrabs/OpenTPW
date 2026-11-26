# Theme Park Inc advisor

This trace uses the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) and retail data. The game was not launched. It supersedes `TPI-ADVISOR-FORECAST.md`, whose forecast evidence is kept in §5 and whose wrong claims are listed in §7.

The port is planned in phases P1 to P9. P1, the queue, P2, the playback and its presentation, and P4, the first event senders (§9), are in OpenTPW (`ThemeParkIncAdvisor`, `ThemeParkIncAdvisorPresentation`); §6 lists what is not ported yet.

## 1. The advisor thing and its update

- [BIN:TPI-EXE:0x64836F advisor thing] The new-world init builds the advisor (thing model 11) before the tag system (`0x6483B7`). The world pass updates it through the class switch (`0x6376C7` → `0x637704` → `0x718190`), skipped while the mode word `0xC65B80` is 1 (an online game). A lost park's thing pass does not run, so neither does the advisor.
- [BIN:TPI-EXE:0x718190 advisor update] Each update has three parts:
  1. **Delayed window help.** While `+0x10D` is set and less than `GeneralAdvisor.UIPanelDelay` has passed since `+0x110`, the update goes straight to the playback. This delay runs on the wall clock: `GetTickCount` (import `0xC6900C`) both when the window help sets `+0x110` (`0x503B05`) and when the update tests it (`0x7181D2`), so it keeps running while the game is paused. Once the delay has passed, `+0x10D` is cleared, the message `+0x114` is posted with its descriptor's score, and the update returns.
  2. **Intake.** The condition scan `0x71C0B0` returns one message per call (one scan row per world tick). The intake is skipped while the animation word (`0x5D0820`) or the game object's `+0x2F` is set.
  3. **Playback** (`0x71830E`). It waits until the presentation clock (the park clock's sub-clock, below) reaches `+0xE4` + `+0xE8`. A set `+0x104` is cleared and skips one playback. The selected slot's score must be strictly above `MinScoreForConsideration`.

## 2. Descriptors and settings

- [BIN:TPI-EXE:0x729530 descriptor initializer] The table at `0x9DE020` has 664 rows of 56 bytes, then a terminator (kind 2; the row count `0x9E7198` is 665). There are 606 distinct message ids (`0x0`–`0x283`). 290 rows are the condition scan's (kind 0); the rest are posted by code (kind 1). Row layout: `+0` kind, `+4` id, `+8` group (0–9), `+0xC` class, `+0x10` callback, `+0x14` callback offset (0 everywhere), `+0x20` pending-duplicate limit (byte), `+0x24` first response, `+0x28` response count, `+0x2C` variant rule (2 in every row), `+0x30` `0x285` everywhere, `+0x34` gate (set when the scorer loads, `0x73D480`).
- `content/data/tpi-advisor-descriptors.toml` holds all 664 rows. For the 165 rows whose callback is a constant (`mov eax, imm32; ret`) it also holds the score (0, 2500, 5000 or 10000); for the 194 rows whose callback returns one word of the scorer (`mov eax, [ecx + offset]; ret`, an `Advisor.sam` key such as `HelpMessage.Score` at `+0x49C`) it holds that offset (`field`). `tools/ppc-analysis/oracle_x86/tpi_advisor_queue_oracle.py` writes the file and checks it against the native initializer and the callbacks.
- [BIN:TPI-EXE:0x71B770 descriptor lookup] Every lookup takes the first row of a message in table order and refuses `0x285`. Ids `0x58`–`0x5C` do have rows (group 7).
- [BIN:TPI-EXE:0x71B6B0 scorer constructor] The scorer `0xC65220` binds `Advisor\Advisor.sam` through the schema at `0x97C180` (`GeneralAdvisor`, then `MessageGroups[10]` with six words each, then `GroupTimes` and the condition keys). The scorer's `+0x20` is `MinScoreForConsideration`, `+0x24` `UIPanelDelay` and `MessageGroups[g]` starts at `+0x2C + 24·g`. A key the file does not set keeps the reset value; the shipped file has no group 8, so the mail's group is all zeros. OpenTPW binds the whole schema (1006 records and the end record, `content/data/tpi-advisor-schema.toml`, checked by `tpi_advisor_tables_oracle.py`) through `ThemeParkIncSamObject` (`ThemeParkIncAdvisorSettings`), so a score callback that reads a scorer word reads the file's value.

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

### 4.1 The speech `0x716230`

- **Response row.** The table `0xBEF1F0` has 32-byte rows: id, voice `+4`, LIP `+8`, animation `+0xC` (-1 generated, else the canned row plus 16), `+0x10` (low word: the model that shows the clips, 0 global, 1 the level's; high word: the level's speech bank), the accessory words `+0x14`/`+0x18` and the tag `+0x1C`. The forecasts are `(25, 1, -1, 0)`; letter 748 is `(-1, 0, 0x29A, 0)`; letters 749 and 750 are `(25, 1, -1, 0)`.
- **Refusals.** It returns 0 when the response is not in the table, when a speech is active (`0xBEF074`), or with AdvisorOn (`0xA10218`) off unless the voice is in the presenter's ranges (`0x54D3E0`: `0x3E6`–`0x454`) or the response is the help button's (`0x9DDBE8`). Otherwise `0x9DDBE8` becomes `0x3F6`.
- **Base.** The base `0xBEF06C` is the presentation clock less the advisor's paused time less 200.
- **Head.** The head is hidden (`0xBF7240`) while the animation word is set (`0x5D0820`), for the accessory `0x29A`, when the presenter shows the speech, or with the "game" key group (`0xA07A28 +6`) off. Animation 0x11 shows it without its parts (`0xBF7241`). A shown head loads `Data\Global\Speech\lips\sp_%04d.lip` with the LIP number (`+8`); talking starts at once.
- **Voice and length.** The voice (`+4`, global bank `0xAE012C`) starts at once; `0x653060` gives its length in milliseconds ("Advisor sample is %d ms long"), 1 when nothing plays. The delayed voice is due at base + 1000 (`0xBEF09C`).
- **Sequence.** Length + 200 is replaced by `GenerateAnimSequence(length + 500)` unless that is 0, or by `SetCannedSequence(animation - 16, length + 500)`. A canned row whose first clip is not 14 clears the delay (`0xBEF09C` = 0): the voice keeps playing. Otherwise the voice is stopped, talking cleared and the base moved by 1000.
- **Clock words.** The next LIP mark is base + mark / 1000, the end `0xBEF070` is base + the sequence, and a canned timer (`0xBEF194`) moves by the base. It returns clip 15 + the sequence + 1000 (`0x7167CF`).
- [BIN:TPI-EXE:0x715AB0 generated sequence] **GenerateAnimSequence.** Clip 14, then while the budget is above 0 and fewer than 19 clips are queued: `rand() % 10` (one draw per choice, the first one ignored), the drawn clip when the budget is not below its length, else the first of clips 1–10 the budget does not exceed (clip 10 without one), then clip 15. The sum counts clip 15. This is Theme Park World's `0x598B20`.
- [BIN:TPI-EXE:0x715BC0 canned sequence] **SetCannedSequence.** Row `0x9DD350 + 80·n`: a word with `0x400000` is a timer (the next word plus the sum so far); any other adds clip `word & 0xFF80FFFF` with its accessory bits `0x7F0000`. A sum of 0 with a budget falls back to the generator, which keeps the timer. Ten rows are in use (16, 17, 21, 25–28, 30–32 and the timer rows 33, 34).
- [BIN:TPI-EXE:0x715966 clip table] **Clip lengths.** The advisor init `0x715850` (called by the model loader `0x4B0170`) reads 25 entries (`0xBEF0D0`–`0xBEF130`) through `0x4E0B20` (trailer word 2 × 1000 / 30, as Theme Park World's `0x474070`) from model 1, the level's advisor (`advisor.wad`, clips m1–m26), when it is loaded, else from model 0, the global advisor (m1–m15). Clip 14 is zeroed. A clip past the table reads the clip list itself: clip 26, in the forecasts' row 9, reads `0xBEF134`, the row's first word 0x19, so it counts 25 ms.

### 4.2 The per-frame advisor `0x716ED0` and the stop `0x716950`

- [BIN:TPI-EXE:0x716ED0 advisor frame] **Per-frame advisor.** The frame loop calls it once per drawn frame after the substeps (`0x6A5C49`, `0x6A524D`). While a speech is active:
  - The voice is asked whether it still plays (`0x652C40`). Once none plays and the end has passed (`end - now <= 0`), the speech ends.
  - A due delayed voice starts and sets talking.
  - A due timer plays effect 0x57 and sound 0xC6 (bank `0xAE0124`).
  - The clips step on the model.
  - With the LIP loaded and `now` past the next mark, talking flips and the next mark is read; when none is left the LIP ends with the mouth closed (node 1).
  - Talking with the LIP, once `now` passes `0xBEF1AC` the mouth is `rand() % [0xBEF18C] + 1` (**`0x717175`**) and the next change is 100 ms later; else it stays. Not talking, it is node 1. `0xBEF18C` is 5 (`0x715869`).
  - Not speaking, the LIP is freed.
- [BIN:TPI-EXE:0x716950 advisor stop] **Stop.** Only while speaking. With a voice handle, the voice is stopped and, behind a shown head, `rand() % 3` (**`0x716A2C`**) picks the stop sound `0x46E`, `0x46F` or `0x470` of the global speech bank. Without one, no sound. The speech ends and the LIP is freed; the delay and timer words stay.

### 4.3 The caption: a Message Centre row

- [BIN:TPI-EXE:0x6362CA advice row] The playback's kind-0x18 broadcast (`0x71AC35`) reaches the tag system at once (`0x436DD0` → `0x637970` → `0x635D80`). Branch 0x18 files a row from the tag's record (`0x9D3480 + 40·tag`):
  - the tag, the kind word `+4` (0x97) and the go-to kind `+0xC`;
  - the expiry `+0x14` (0: never) and the category `+0x24` (0);
  - the key and the go-to argument, the record's locate words;
  - the sender TAG_SYSTEM[`+0x1C`] = 0xC0 with its picture 8 (`0x5CD460`, sender row key 8), the player as addressee, the subject TAG_SYSTEM[`+0x20`] (0xF7 for the forecasts, 0xC1 for the letters) and the body from the formatter (`+0x18` = 0: TAG_SYSTEM[`+8`]).
- The row is unread (`+0xE0C` stays 0) and counts in the unread counter (`0x54F1A0`), which can post `0x198`/`0x15D`. A tag without a record (0x97) or an empty body files nothing.

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

- `ThemeParkIncAdvisor` is a world system. The level's thing sweep updates it at the advisor's slot (model 11); a headless economy updates it after the tag system. It holds the eight slots, the stored tutorial record, the window, `+0x104`, `+0x108`, `+0x10C`, the delayed-help fields two player inputs, `TutorialOn` and the advisor panel, and the advisor clock. The client does not feed those yet: `TutorialOn` stays on and the panel down, the options' default and the state of a park with no advisor panel (D-2 records them as simulation input with the senders). The Science save keeps all of it as `TpiAdvisor`. A save from before it (without `TpiAdvisor`, or with the old `TpiForecast`) is refused before the asset check. A Theme Park World save that carries it is refused. The help button's two words are statics and start fresh after a load (D-5).
- **Clock** (`[EXT:tpi-advisor-tick-clock]`, user decision D-1). The original runs two clocks off the same millisecond source. [BIN:TPI-EXE:0x6A5B4F world tick clock] The world ticks run on the park clock's main clock, which adds each raw delta times its rate (`0x41B760` → `0x41BDE0` → `0x41C150`, rate at `+0x20`, 0.25 to 2): one world tick every eight 31 ms substeps (`0x6A5B92`, `0x6A5C18`). [BIN:TPI-EXE:0x41B780 presentation clock] The advisor reads the sub-clock at `+0x50`, which adds the raw deltas without the rate and stops while paused (`0x41BD10` → `0x41BB50` → `0x41BA20`). So a world tick spans 248 / rate real milliseconds on the advisor's clock: at a faster speed more ticks run per second, and each spans fewer advisor milliseconds. OpenTPW's advisor clock (`ThemeParkIncAdvisor.ClockMicroseconds`, saved) advances by exactly that per world tick, at the rate in effect: a camera flight's (1.0, or 0.7 in mode 1), otherwise the player's speed (1, 2 or 4, `[EXT:speed-buttons]`), which the client sets as a simulation input. It stops while the park is paused, and a load or replay with the same speeds sees the same windows. The original's frame effects (at most three ticks a frame, the backlog cut at 2000 ms) do not enter it.
- **Window help delay:** OpenTPW measures it on the advisor clock (D-2), so unlike the original's `GetTickCount` it freezes while the park is paused.
- **Context:** the mode word is 0 (the career). The cutscene's thing and mode are the animation word and its variant. The game object's `+0x2E`/`+0x2F` are not modelled and read as clear.
- **Game speed input.** The player's speed reaches the advisor clock through `ParkEconomy.TpiSpeedRate`, which the client sets each frame from the Theme Park Inc HUD's own speed. It does not go through `SetGameSpeedCommand` (that command sets `ParkEconomy.Speed`, which the Theme Park Inc HUD does not use) and it is not saved: the advisor clock it advances is saved and hashed, but a command log does not hold the rate, so a replay or a co-op peer would not see it. Routing it through the command path means moving the Theme Park Inc HUD's speed and pause onto the economy's speed, which is not a cheap change; it is needed before the advisor runs in co-op.
- **Presentation** (`ThemeParkIncAdvisorPresentation`, §4.1–4.2). The speech, the per-frame advisor and the stop run in the simulation, on the advisor clock. They decide the window, the speaking refusal and the three draws:
  - The speech data are read from the game files, never from an audio device (`ThemeParkIncAdvisorSpeech`): the voice length decoded from its sample, the LIP from `global/Speech/lips` and the clip lengths of the level's advisor, else the global one. The forecasts' window on Science is 4 × 633 + 25 + 666 + 1000 + 1000 = 5223 ms.
  - **No voice** (D-3): a response whose sample is missing or does not decode takes the original's length for a voice that does not play, 1 ms (`0x653069`). Its sequence is built from that, and nothing plays.
  - `[EXT:tpi-advisor-tick-clock]`: the per-frame advisor runs once per 31 ms substep of the world tick (8 per tick, the eighth after the world tick), an ideal frame pacing, where the original runs it once per drawn frame. At normal speed that is 32 frames a second, so the 100 ms mouth interval is met at the fourth frame: a talking mouth changes every 124 ms, which sets how many mouth draws a speech makes and so the later sequences. A voice plays for its sample length from its start on the advisor clock, so a voice longer than the sequence keeps the speech active, and an advice played then is refused (window 1000 ms).
  - `[EXT:tpi-advisor-presentation-rand]` (user-approved 2026-11-24): the three draws (`0x715AC4`, `0x716A2C`, `0x717175`) come from one C-runtime generator of their own, never from the world generator. It is seeded from the session seed (D-4: the economy stream's two halves XORed; the default seed gives 1), saved and hashed. The original seeds the C runtime from the tick count at startup (`srand(GetTickCount())`, `0x6A1514`), which no run can repeat.
  - The client follows the presentation's cues: it plays the voice when it starts and the stop sound. The canned timer has no ported response and is only logged.
  - Read as the running park has them (D-2): the "game" key group on, no presenter window, no pause time (the advisor clock stops while paused).
- **AdvisorOn** is a player input like TutorialOn (on by default) and is saved. The client does not feed it yet.
- **Captions** are Message Centre rows (§4.3, `ThemeParkIncLetterSource.Advice`), replacing the forecast caption line. Their unread count posts `0x198`/`0x15D` as the tube's counter does (§9).
- **Saves.** [BIN:TPI-EXE:0x6A36DE load init] A load starts the speech silent, as the original's does: screen 9 (`0x6A3150`) runs the model loader `0x4B02E0`, which calls the advisor init `0x715850` (at `0x4B0484`), before it reads the park file (`0x746660` at `0x6A3BC9`), and the init zeroes `0xBEF068`–`0xBEF1E4` (the speech, the voice handle, the delayed voice, the timer, the LIP and the mouth). So the save keeps none of the speech: `TpiAdvisor` gains only `AdvisorOn` and `PresentationRandom`, the stream's state (D-4), both optional. A load resets the speech and continues the stream from its saved state; the queue's window, the advisor thing's, stays as saved. A P1 save lacks both members and loads with AdvisorOn on and the stream fresh from the session seed (P1 drew nothing and timed its windows by the voice alone). The speech fields are left out of the save rather than kept and ignored: a load could never read them.
- **Gated** (`[EXT:tpi-capability-gates]`):
  - The condition scan, its gates and its callbacks (P3, D-6). `ThemeParkIncAdvisor.ConditionScan` is off.
  - The response table is the executable's (`content/data/tpi-advisor-responses.toml`, 994 rows, §9), but only the ten tag records the posted advice's captions use are known (`ThemeParkIncAdviceTag`). The presenter's speeches and the level speech bank are refused.
  - The advisor model drawn on screen: the presentation keeps the sequence's clips and the mouth, which the client does not draw yet. That is a client change of its own, P2b.
  - Every sender §9 does not list as ported (P5 and later): the wage demands, the staff and research messages are noted, not posted; §9 lists the P4 senders that stay gated.

## 7. Corrections to `TPI-ADVISOR-FORECAST.md`

- The condition table is referenced: the scan `0x71C0B0` evaluates one row every world tick (§1).
- The table has 664 rows and 606 ids, not 518 rows and 501 ids; `0x58`–`0x5C` have rows (§2).
- The variant rule is row `+0x2C`, not `+8` (`+8` is the group) (§4).
- The Park Awards dispatcher `0x606EC0` is reached for every award index, won and moved ([TPI-AWARDS-SHARES.md](TPI-AWARDS-SHARES.md)).
- The playback window is the speech's return plus 1000, and the speech's return is the sequence plus the closing clip plus 1000 (§4), not the voice length plus 1000.
- The tag system subscribes to kind `0x18` (`0x635A63`): every captioned advice files a tag-tube row (branch `0x18`, §4.3).
- No voice is not "duration 0, window 1000 ms": a refused speech is, but a missing voice still builds its sequence from a 1 ms length (§6).

## 8. History resets

- [BIN:TPI-EXE:0x7177B0 prebuilt history reset] The world reset `0x64B940` (called at `0x64BA77`) clears every history entry except its time stamp: variant -1, and played, slaps and plays 0. It runs when the prebuilt park loads (`0x7468B0`, `PreBuilt\`, from `0x6A3CF9` unless the mode word is 3), which is the first world of a new career. OpenTPW's new career builds its history from the career constructor's values, which this reset leaves unchanged, so OpenTPW does not repeat it.
- [BIN:TPI-EXE:0x7191AB easy-mode history reset] The advisor's handler applies the same reset on `RESET_FOR_EASY_MODE`. Theme Park Inc's easy mode is not ported (an easy Theme Park Inc save is refused), so this is gated (`[EXT:tpi-capability-gates]`).

## 9. The event senders of P4

`content/data/tpi-advisor-producers.toml` names the producer of each of the 606 messages: 232 scanned ids (15 of them with a constant-zero callback), the ids game code posts (event) or a window posts as its help (help), and 139 ids no poster was found for (unused). Each row says whether OpenTPW posts it (wired), does not yet (gated, with the reason) or has nothing to post. A poster is "traced" when the id is the argument its push gives the record constructor `0x71A930`, the enqueue `0x718820` or a help wrapper (directly or through the shared call of a branch), or a table the posting code indexes; "searched" means no such site was found.

[BIN:TPI-EXE:0x708BF0 response initializer] The response table is the executable's: 994 rows (ids 0 to 1012; 833 has two and the lookup `0x716D10` takes the first), in `content/data/tpi-advisor-responses.toml`. So any posted advice speaks with its own voice, LIP, sequence and caption tag. The tag records of the researched-obstacle advice (tags `0x60`-`0x63`, `0x9D4380`) are added: their kind word is the tag itself, so a new row supersedes the older one of its tag.

**Score paths.** A record built with score -1 takes its descriptor's score (`0x71B7E0`): a constant, a scorer word (`field`, e.g. `HelpMessage.Score` 1000 for every window help), or one of the two history callbacks P4 needs, `0x727410` (`0x1FA`) and `0x728580` (`0x21D`): 10000 until the player's history has the message played, then 0. Any other callback is the scan's or a later sender's and throws.

### 9.1 Ported

- [BIN:TPI-EXE:0x54F1A0 unread advice] **Unread mail.** The tag tube's unread counter posts `0x198` when the total reaches 3 and `0x15D` at 6, 12, 24... (the threshold doubles), with the descriptor's score (10000, group 0, limit 1). The original posts inside the row insert; OpenTPW posts right after each insert, before anything else runs.
- [BIN:TPI-EXE:0x503A40 window help] **Window help.** `0x503A40` (the help button): `+0x10D` cleared, `+0x10C` set around the post, so the group, once-only and slap checks are skipped and the message becomes the help button's (`0x9DDBEC`). `0x503AD0` (the help said after `UIPanelDelay` when a window opens, §1). `0x5039F0` (the help said at once). `0x503B40` (the close): `+0x10D` cleared, then the cancel `0x718990`. They are wired for the windows OpenTPW has:
  - the tag tube window: `0x1AF` delayed at the builder's end (`0x5523EB`), on its help button (`0x551A19`), cancelled with the go-to hint `0x211` when it goes (`0x5517E0`);
  - the mission window: the mission panel's own advice `0x93E3E0[counter]` (`0x223` plus the counter; message 0 for counters 18 and 19, which no career reaches and whose level-bank speech the presentation refuses) and `0x277` delayed when it opens (`0x55A879`, `0x55A887`), `0x1B6` on its help button (`0x55BEC4`); its destroy (`0x55BB48`) cancels `0x1B6`, `0x277` and `0x93CE1C[counter]` (the same words), and every close (`0x55BB0A`, the tick's rebuild `0x55BC1D`, the Challenges tab `0x55BCC1`, Proceed, Cancel `0x55BD35`, and the HUD's message 5) then posts `0x1FA`;
  - the wage demand window (`0x1B0`: `0x59AC1B`, `0x59AAA1`, cancelled on every close) and the negotiation dialog (`0x1B1`: `0x559D1A`, `0x5599B7`, cancelled before its decision applies). The ported windows call these through `IThemeParkIncWindowAdvice` between world ticks, as the original's window procedures run between them.
- [BIN:TPI-EXE:0x718AB0 repeat tutorial] **F1.** The key table's F1 row (key `0x70`, its handler word `0x4305E0` at `0x9A866C`) and the default window procedure (`0x507FE8`) offer the stored tutorial record again with `HelpMessage.ContextHelpScore` as its given score and its bypass byte set. A lost park's key tables are off.
- **Mission controller.** The researched-obstacle step (`0x5EF6E2`) posts `0x1EB`-`0x1EE` after missions 3, 6, 9 and 12, else `0x172` once `0x19B` has been played and `0x19B` before. The removed obstacle (`0x5F0F96`) posts `0x173` when `0x19C` has been played and, at counter 1, `0x21D` after the flight starts; an obstacle opened at counter 0 (`0x5F0827`) posts `0x26C` the same way. Both are class 6, which only a flight of mode 0 admits, so the mode-2 flight scores them -1: they wait in the queue and are never said. The promotion (`0x5F10F7`) posts `0x1A4`. The end of a flight (`0x5F118D`) cancels `0x172`, `0x19B`, `0x1E3`, `0x20F` and `0x210`.
- **Challenges.** The accept (`0x5C84C0`) and the time-out (`0x5C2150`) cancel the sender's advice (word 1 of its row at `0x9CD000`: `0x239`, `0x25C`, `0x245`, `0x265`, `0x255` for senders 2 to 6). The lost branch that posts the firing letter also posts `0x1A3` itself (`0x5C4979`) after the letter's row builder has, so the advisor says it twice. Senders 0, 1, 7 and 8 name `0x285`, whose cancel outside a window would write the history past its last entry; the retail `Challenges.sam` names senders 2 to 6 only, and OpenTPW refuses the others.

### 9.2 Gated or not reached (`[EXT:tpi-capability-gates]`)

- **Go-to hint `0x211`** (the tube's details `0x5531CD`): its callback `0x727E60` scores 10000 only within the first hour (`0x650890`: the counter `0xBFCC4C` / 60 below `FirstHour.Minutes`), a clock OpenTPW does not tick yet (P7). The window's destroy still cancels it.
- **Tool switches** (`0x673230`, `0x673B20`) cancel `0x93`, `0xA4`, `0xA6`, `0xA3`, `0x9B`, `0x110`, `0x10A`, `0x1FE` and `0xA7` when the tool mode `0xB20024` changes. The tool mode is not modelled (D-2 input, P3), and nothing OpenTPW posts is among those ids, so the cancels would find nothing.
- **The other windows' help** (61 help ids in the producer inventory): their windows and panels (research, awards, rides, objects, the HUD's panels `0x52E4E0`, ...) are not ported.
- **Challenge mail and view** (`0x635E7F`, `0x52BCD0`): the challenge mails (message `0x1B`) are not filed and the challenges window is not built, so their advice (`0x239`-`0x26A`) is not posted. `0x5C2100` and `0x5C8790` (`0x5C87CE`), two more sender-advice cancels, have no caller.
- **Awards** (D-7): the advice `0xBF`, `0x13A`-`0x13E`, `0xC0`, `0xC1` (won) and `0xC3`, `0x142`-`0x146`, `0xC4`, `0xC5` (moved) from `0x606EC0` stays gated.
- **The advisor's message handler** (`0x7194A4`, `0x7190A0`) and the senders of P5 (staff, research, rides, guests, objects, the cutscene panel `0x26D`).
