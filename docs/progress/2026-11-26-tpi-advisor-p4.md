## Theme Park Inc: the advisor's first event senders (November 26, 2026)

The fourth phase of Theme Park Inc's advisor port ([TPI-ADVISOR.md](../reverse/TPI-ADVISOR.md) §9). The advisor now hears the Message Centre, its windows and the mission controller, and every one of its 606 messages has a recorded producer.

- **Producer inventory.** `content/data/tpi-advisor-producers.toml` classifies every message once: 217 scanned, 15 scanned with a zero score, 168 posted by game code, 67 window help and 139 with no poster found. Each row names the posting addresses and whether OpenTPW says it.
- **The executable's tables.** All 994 advisor responses and the scorer's whole `Advisor.sam` schema now come from the executable (a new oracle checks both), so any posted advice speaks with its own voice and sequence, and the 194 score callbacks that read one `Advisor.sam` key score as the original's.
- **Unread mail.** The third unread letter makes the advisor mention the Message Centre (`0x198`), the sixth, twelfth and so on again (`0x15D`).
- **Window help.** The Message Centre, the mission window and the two wage windows say their help after the panel delay when they open, on their help button, and cancel it when they close. F1 repeats the last tutorial advice.
- **Missions.** The researched obstacle, the removed or opened obstacle, the promotion and the mission window's own advice are posted; the end of a camera flight cancels the obstacle advice.
- **Challenges.** Accepting a challenge and its time-out cancel its sender's advice, and a challenge loss that costs the job says the firing letter twice, as the original does.
- **Still gated** (`[EXT:tpi-capability-gates]`): the go-to hint, whose score needs the first-hour clock (P7); the tool-switch cancels, as the tool mode is not modelled; the help of windows OpenTPW does not have; the challenge mails; the award advice (D-7); and the senders of P5.

No new label or approximation is added, and no CRT `rand()` is reached. The original game was not launched, and no binary or data is included.
