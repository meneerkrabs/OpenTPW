## Winter cold: snow costs happiness unless guests are dressed (October 18, 2026)

Phase W5 of the winter extension ([WINTER.md](../WINTER.md#cold-mood-extwinter-cold-phase-w5)): the option `opentpw.winter-cold` (off by default, not offered in a Theme Park Inc run, always recorded in a save while on). It stacks on the simulation branch of the winter work (W3).

- **The rule.** In snow, once per qualifying day per guest (the rain mood's day gate, age and once-per-day rule, with its own `ColdMoodDay` mark), happiness changes by -2, plus 1 back for a head item, 1 for mittens and 1 for a recent glühwein, capped at 0. A glühwein stall sets `WarmUntilDay` (the rest of that day and the next game day) while the option is on. With the rain mood on too, the cold rule replaces its snow happiness (+1); its thirst and hunger columns stay.
- **Measured.** A headless kid over a snowy week loses 4 happiness with no protection, 2 with a head item and 0 with a head item and mittens, where the rain mood alone cheers every kid by 2. The table is in docs/WINTER.md and pinned by a test.
- **Saves and determinism.** `ColdMoodDay` is optional, validated and hashed only once set. With the option off every pin is unchanged; two new pins cover it on, and on together with the rain mood, the brolly shop and the winter shops. A park saved mid-snow resumes with the same hash.
- **Options row.** "Cold mood" with its description in all eleven interface languages.
- **Tests.** `WinterColdTests` (20) and three determinism tests.
- **User decisions (October 19, 2026).** `[EXT:winter-cold-warm-window]`: a glühwein warms for the rest of its day plus the next game day (`WarmEffectDays` 1), answered "Rest van de dag + de volgende dag". `[EXT:winter-cold-replaces-snow-cheer]`: with cold on, only the happiness +1 of the rain mood's snow row is dropped and the thirst and hunger snow effects stay as TPI's, answered "Alleen de humeur-+1". Both are recorded as approved in docs/WINTER.md.
