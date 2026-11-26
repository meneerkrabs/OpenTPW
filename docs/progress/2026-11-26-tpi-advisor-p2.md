## Theme Park Inc: the advisor's playback and presentation (November 26, 2026)

The second phase of Theme Park Inc's advisor port ([TPI-ADVISOR.md](../reverse/TPI-ADVISOR.md) §4). The advisor now speaks with the original's timing, and its captions arrive as Message Centre letters.

- **The pause after an advice** is the original's: the advisor's animation sequence, its closing clip and 2000 ms. Before, it was the voice length and 1000 ms. The sequence is generated (GenerateAnimSequence `0x715AB0`) or canned (`0x715BC0`), from the clip lengths of the level's advisor model. On Science a forecast holds the advisor for 5.2 seconds.
- **The presentation runs in the simulation:** the speech (`0x716230`), the per-frame advisor (`0x716ED0`: the delayed voice, the LIP walk and the talking mouth) and the stop (`0x716950`). They run once per 31 ms substep, on the advisor clock. A voice longer than its sequence keeps the advisor talking, and an advice due then is not said, as in the original.
- **Its three random draws** (the animation clips, the mouth shape and the stop sound) come from a random stream of their own, seeded from the session seed, saved and hashed. They never touch the world's generator (`[EXT:tpi-advisor-presentation-rand]`, user-approved 2026-11-24).
- **No voice.** A missing voice takes the length the original reads for a voice that does not play (1 ms); nothing is invented. With AdvisorOn off, only the help button's advice is said.
- **Captions are Message Centre letters.** Each forecast and letter advice files an unread row from its tag record (`0x635D80`, branch 0x18), with the advisor as sender. The forecast caption line is gone.
- **Sound.** The voice plays when the presentation starts it. A cancelled advice ends with one of the three stop sounds.
- **Saves.** The advisor's save gains the presentation and the AdvisorOn option. Saves from the first phase still load: they start the presentation silent, with a fresh stream.
- **Oracle.** A new oracle runs the sequence builders, the speech, the stop and the per-frame advisor on the executable, with `rand()` replaced by a seeded generator. Its 54 cases are the tests' cases.

Labels: `[EXT:tpi-advisor-presentation-rand]` is new (approved by the user on 2026-11-24). `[EXT:tpi-advisor-tick-clock]` now also covers the per-frame advisor's substep pacing and the voice's length on the advisor clock. No approximation is added, and no other CRT `rand()` is reached. The advisor model is not drawn on screen yet.

The original game was not launched, and no binary or data is included.
