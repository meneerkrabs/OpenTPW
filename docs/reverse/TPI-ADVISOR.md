# Theme Park Inc advisor

This trace uses the identified English no-CD TPI `Game.exe` (SHA-256 `77ea0c41410aad7c94a5eda658e31cb4ac05f7253aaf6746ad5e8a90afbd967a`) and retail data. The game was not launched. It supersedes `TPI-ADVISOR-FORECAST.md`, whose forecast evidence is kept in §5 and whose wrong claims are listed in §7.

The port is planned in phases P1 to P9. P1, the queue, P2, the playback and its presentation, and P2b, the advisor on screen, are in OpenTPW (`ThemeParkIncAdvisor`, `ThemeParkIncAdvisorPresentation`, `ThemeParkIncAdvisorView`); §6 lists what is not ported yet.

## 1. The advisor thing and its update

- [BIN:TPI-EXE:0x64836F advisor thing] The new-world init builds the advisor (thing model 11) before the tag system (`0x6483B7`). The world pass updates it through the class switch (`0x6376C7` → `0x637704` → `0x718190`), skipped while the mode word `0xC65B80` is 1 (an online game). A lost park's thing pass does not run, so neither does the advisor.
- [BIN:TPI-EXE:0x718190 advisor update] Each update has three parts:
  1. **Delayed window help.** While `+0x10D` is set and less than `GeneralAdvisor.UIPanelDelay` has passed since `+0x110`, the update goes straight to the playback. This delay runs on the wall clock: `GetTickCount` (import `0xC6900C`) both when the window help sets `+0x110` (`0x503B05`) and when the update tests it (`0x7181D2`), so it keeps running while the game is paused. Once the delay has passed, `+0x10D` is cleared, the message `+0x114` is posted with its descriptor's score, and the update returns.
  2. **Intake.** The condition scan `0x71C0B0` returns one message per call (one scan row per world tick). The intake is skipped while the animation word (`0x5D0820`) or the game object's `+0x2F` is set.
  3. **Playback** (`0x71830E`). It waits until the presentation clock (the park clock's sub-clock, below) reaches `+0xE4` + `+0xE8`. A set `+0x104` is cleared and skips one playback. The selected slot's score must be strictly above `MinScoreForConsideration`.

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

### 4.4 The advisor on screen

- [BIN:TPI-EXE:0x464E50 advisor model slots] The model loader `0x4B0170` loads two models into the slot table `0xA13AB0` (`0xB0` bytes per slot): slot 0 the global advisor (`Data\Global\Advisor`, `Advisor.md2`, clips m1–m15), slot 1 the level's (`advisor.wad`, `Advisor_SCI.MD2` on Science, clips m1–m26). The slot setup is Theme Park World's `0x429BA0` with other numbers: the model matrix is the identity with translation **(0.75, −0.6, 0.9)** (`0x464E7F`; Theme Park World has (0.6, −0.6, 0.2)), the root scale (0.01125, 0.001, 0.015), the light (26.67, −26.67, 800) and the load flags `0x50120` are the same. So the model is drawn without the park's camera over the bottom right of the screen, further right than Theme Park World's: its box (centre ± 0.4) reaches past the right edge, where the model itself ends.
- **Which model.** A speech plays its clips on the slot of its response's `+0x10` low word (`0xBEF07C`). The forecasts and letters 749 and 750 use the level's advisor; letter 748 hides the head.
- [BIN:TPI-EXE:0x7163C3 head shown or hidden] The speech moves both models out of sight (`0x464E00`: translation y −90.6) and sets `0xBF7240` when the animation word is set (a camera flight plays), the accessory is `0x29A`, the presenter shows the speech or the "game" key group is off; otherwise it puts both back (`0x464DB0`). Animation 0x11 shows the head without its parts (`0xBF7241`).
- [BIN:TPI-EXE:0x7166E9 first clip] The speech starts the sequence's first clip on the model at once (`0x465330`: AnimID 5, the clip less 1, flags 0x10), which shows the model. [BIN:TPI-EXE:0x717067 next clip] While the speech is active, each frame asks the model's channel (`0x4E09F0`) and starts the next clip only when it reads the idle set 12, the end of the clip before. The frame that ends the speech (`0x716F7B`: `0xBEF074` cleared) falls through to that step (`0x716F99`), which does not test `0xBEF074` again, so it still starts a clip whose predecessor has ended; later frames start none (`0x716F44`). The engine is Theme Park World's (`0x4DF610` is `0x4732A0`): a clip runs for its own length (trailer word 2 × 1000 / 30), and the model hides at the end of a clip with nothing after it. Clip 26 of the forecasts' row plays its own 2966 ms on Science, where the sequence table reads 25 ms for it (§4.1).
- [BIN:TPI-EXE:0x715ED0 clip-start update] For each clip the speech or a frame starts, unless the head is hidden: both models back on screen, bit 0x10 cleared on mesh record 32 of the playing model (the body of a level advisor; the global advisor has 29 meshes, where the original writes past its table) and set again for a head without its parts, the five mouths hidden (the next frame shows the mouth again), the accessory selectors `+0x14` and `+0x18` (when nonzero; skipped for a head without its parts), and with clip bit `0x10000` the bug head hidden.
- [BIN:TPI-EXE:0x715CF0 accessory selector] Theme Park Inc's table differs from Theme Park World's `0x598CA0`: −1 shows the theme costume's add-ons and hides its sub-offs (the level's `Standard.sam`, copied by `0x428900` as Theme Park World's `0x4080E0` does; Science: add-on 4, the helmet `NGon05`, and sub-offs 19 and 20, the antennae); 0 and 2 show the antennae `0x13`, `0x14`; 3–7, 9–11, 13 and 24 hide them; 14 hides the right hand `0x15`; then a nonzero selector shows its own node unless bit `0x10000` is set. `0x31`–`0x38` swap the award ribbons' textures instead (`0x4D7BB0`).
- [BIN:TPI-EXE:0x7171A7 mouth nodes] When the mouth (§4.2) changes, the frame hides the previous mouth mesh and shows the new one unless it is 0 (the init's), the clip has bit `0x10000` or the head is hidden. The meshes are found by name (`0x49A510`: "mouth - normal", "aah", "eee", "ooh", "sss", and −1 "bug head").
- [BIN:TPI-EXE:0x7169C1 advisor kill] The stop kills the model's animation (`0x4650B0`, "Kill advisor"), which hides it at once. A speech that ends by itself leaves its started clip playing to its end.

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
- **Co-op prerequisites.** The speech data decide the presentation: a peer with other or missing speech data (`cat_speech`, `lips.wad`, `advisor.wad`, the speech language) gets other voice lengths, LIPs or clip lengths, so other windows, other draws from the presentation stream and another run hash. Every peer needs the same speech data before the advisor runs in co-op. `OPENTPW_SILENT` changes none of it: the lengths come from the files, never from an audio device. The world generator is the same either way (a test plays a forecast with and without the speech data).
- **Game speed input.** The player's speed reaches the advisor clock through `ParkEconomy.TpiSpeedRate`, which the client sets each frame from the Theme Park Inc HUD's own speed. It does not go through `SetGameSpeedCommand` (that command sets `ParkEconomy.Speed`, which the Theme Park Inc HUD does not use) and it is not saved: the advisor clock it advances is saved and hashed, but a command log does not hold the rate, so a replay or a co-op peer would not see it. Routing it through the command path means moving the Theme Park Inc HUD's speed and pause onto the economy's speed, which is not a cheap change; it is needed before the advisor runs in co-op.
- **Presentation** (`ThemeParkIncAdvisorPresentation`, §4.1–4.2). The speech, the per-frame advisor and the stop run in the simulation, on the advisor clock. They decide the window, the speaking refusal and the three draws:
  - The speech data are read from the game files, never from an audio device (`ThemeParkIncAdvisorSpeech`): the voice length decoded from its sample, the LIP from `global/Speech/lips` and the clip lengths of the level's advisor, else the global one. The forecasts' window on Science is 4 × 633 + 25 + 666 + 1000 + 1000 = 5223 ms.
  - **No voice** (D-3): a response whose sample is missing or does not decode takes the original's length for a voice that does not play, 1 ms (`0x653069`). Its sequence is built from that, and nothing plays.
  - `[EXT:tpi-advisor-tick-clock]`: the per-frame advisor runs once per 31 ms substep of the world tick (8 per tick, the eighth after the world tick), an ideal frame pacing, where the original runs it once per drawn frame. At normal speed that is 32 frames a second, so the 100 ms mouth interval is met at the fourth frame: a talking mouth changes every 124 ms, which sets how many mouth draws a speech makes and so the later sequences. A voice plays for its sample length from its start on the advisor clock, so a voice longer than the sequence keeps the speech active, and an advice played then is refused (window 1000 ms).
  - `[EXT:tpi-advisor-presentation-rand]` (user-approved 2026-11-24): the three draws (`0x715AC4`, `0x716A2C`, `0x717175`) come from one C-runtime generator of their own, never from the world generator. It is seeded from the session seed (D-4: the economy stream's two halves XORed; the default seed gives 1), saved and hashed. The original seeds the C runtime from the tick count at startup (`srand(GetTickCount())`, `0x6A1514`), which no run can repeat.
  - The client follows the presentation's cues: it plays the voice when it starts and the stop sound. The canned timer has no ported response and is only logged.
  - Read as the running park has them (D-2): the "game" key group on, no presenter window, no pause time (the advisor clock stops while paused).
- **On screen** (`ThemeParkIncAdvisorView`, §4.4). The client draws the speech's model into its own image in the world pass and shows it over the bottom right after the Theme Park Inc HUD. Theme Park World's advisor shares the draw (`Advisor.DrawRig`, `AdvisorImageTarget`, `Advisor.ModelTransform`, `AdvisorRig`, the blink toggles of `AdvisorAccessories`), which is unchanged; Theme Park Inc adds its place (0.75, −0.6, depth 0.9), its clip-start update and selector (`ThemeParkIncAdvisorAccessories`) and its timeline (`ThemeParkIncAdvisorTimeline`: each clip for the playing model's own length, none started after the speech ended).
  - The view only reads the simulation. The presentation keeps for it, unsaved and unhashed, whether the model shows (`ModelShown`: set by a speech, cleared by the stop and the init), when the speech ended (`EndedAt`) and the mouth each of the last 16 frames left (`MouthAt`). Reading them changes nothing; a test runs a forecast with and without the reads and compares the saves.
  - `[EXT:tpi-advisor-tick-clock]`: a world tick runs its eight frames when it starts, so a drawn frame shows the advisor at its own point of the tick's span (`ThemeParkIncAdvisor.DisplayMilliseconds`: the clock before the tick plus the span times the fraction of the tick's fixed ticks run), with the mouth of the last frame at or before it. The next clip starts at the end of the one before, where the original starts it at its first frame after that end, and the frame of a clip start, where the original shows no mouth for one drawn frame, shows the mouth.
  - `[EXT:tpi-text-hud]`: the original draws the model in its scene's model pass, over the park and under nothing; OpenTPW's text HUD stands in for the in-game interface, so the advisor is drawn over it, as Theme Park World's advisor is over its HUD. `[EXT:advisor-aspect]`: outside 4:3 the width keeps the model's proportions.
  - The models load the first time a speech shows them (also in the browser, from the game files as other models). A model that cannot be read is logged once and never drawn; the view is released with the park.
- **AdvisorOn** is a player input like TutorialOn (on by default) and is saved. The client does not feed it yet.
- **Captions** are Message Centre rows (§4.3, `ThemeParkIncLetterSource.Advice`), replacing the forecast caption line. Their unread count posts `0x198`/`0x15D` as the tube's counter does; those advices stay noted, not posted (P4).
- **Saves.** [BIN:TPI-EXE:0x6A36DE load init] A load starts the speech silent, as the original's does: screen 9 (`0x6A3150`) runs the model loader `0x4B02E0`, which calls the advisor init `0x715850` (at `0x4B0484`), before it reads the park file (`0x746660` at `0x6A3BC9`), and the init zeroes `0xBEF068`–`0xBEF1E4` (the speech, the voice handle, the delayed voice, the timer, the LIP and the mouth). So the save keeps none of the speech: `TpiAdvisor` gains only `AdvisorOn` and `PresentationRandom`, the stream's state (D-4), both optional. A load resets the speech and continues the stream from its saved state; the queue's window, the advisor thing's, stays as saved. A P1 save lacks both members and loads with AdvisorOn on and the stream fresh from the session seed (P1 drew nothing and timed its windows by the voice alone). The speech fields are left out of the save rather than kept and ignored: a load could never read them.
- **Gated** (`[EXT:tpi-capability-gates]`):
  - The condition scan, its gates and its callbacks (P3, D-6). `ThemeParkIncAdvisor.ConditionScan` is off.
  - The response table: only the seven responses above are known (`ThemeParkIncAdviceResponse`), and with them the six tag records their captions use (`ThemeParkIncAdviceTag`). The presenter's speeches and the level speech bank are refused.
  - On screen: the award ribbons' texture swap (selectors `0x31`–`0x38`) and the screen effects of the clip bits `0x20000`–`0x200000` (`0x7BA870`, `0x7BA960`, `0x7BAA30`, Theme Park World's effect groups); no ported response has them.
  - Every other sender (P4, P5): the unread-mail advice, the wage demands, the staff and research messages are noted, not posted.

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
