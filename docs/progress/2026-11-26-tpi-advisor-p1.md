## Theme Park Inc: the advisor's queue (November 26, 2026)

Theme Park Inc's advisor now has the original's queue, and every advice goes through it: the weather forecasts and the notification letters so far. This is the first phase of the advisor port ([TPI-ADVISOR.md](../reverse/TPI-ADVISOR.md)).

- **The advisor thing** (`0x718190`) runs in the world tick, at the advisor's place in the thing pass. It holds eight advice records and plays the best one once the last one's window has passed.
- **Acceptance** (`0x718B40`) follows the executable for all ten message groups: the repeat intervals by play count, say-only-once, the slaps that discard an advice, the TutorialOn option, the help-button bypass, the pending-duplicate limit and the variant rule. A full queue replaces the first lowest score but never a letter (`0x718E90`). Letters are chosen before any other advice (`0x718F80`). The cancel (`0x718990`) clears an advice and undoes it while it plays.
- **The history** of every advice is the player's, in the career file (`mHistory`), so it carries over from park to park as the original's does.
- **The descriptor table** ships as `content/data/tpi-advisor-descriptors.toml`: all 664 rows and 606 messages. A new oracle checks it against the executable's initializer, and checks 82 acceptance cases and the replacement, selection and cancel scenarios that the tests repeat.
- **Timing.** The window runs on the world ticks, 248 ms each, so a pause freezes it and a replay or a load sees the same windows (`[EXT:tpi-advisor-tick-clock]`, user decision). Until the advisor's presentation comes (P2), the window holds the response's voice length, now read from the speech data in the simulation, plus 1000 ms. The original holds the clip sequence plus the closing clip plus 2000 ms; P2 replaces the voice length with that.
- **Letters.** A bank or challenge letter's advice now waits in the queue and is spoken by the advisor, as the original does, instead of with the letter.
- **Saves.** The Science continuation save keeps the advisor (`TpiAdvisor`); its history is in the career. A Science save from before, holding the old forecast queue or no advisor, is refused before its assets are checked. A Theme Park World save that holds it is refused.
- **Corrections.** `TPI-ADVISOR-FORECAST.md` is superseded. The condition table is used (one row every world tick), the table has 664 rows and 606 messages, the variant rule is at row `+0x2C`, and the window is not the voice length plus 1000 ms.

Labels: `[EXT:tpi-advisor-tick-clock]` is new (approved by the user on 2026-11-25). `[EXT:tpi-capability-gates]` now covers the condition scan (off until P3), the speech duration and the senders not ported yet. No approximation is added, and no CRT `rand()` draw is reached: the three presentation draws come with P2.

The original game was not launched, and no binary or data is included.
