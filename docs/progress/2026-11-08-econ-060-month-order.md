## Theme Park Inc's month order: the analyser closes before the bank (November 8, 2026)

Theme Park Inc delivers the month message to its listeners in **ascending thing-id order** through an id-keyed tree walked in order (`0x436DD0`, successor `0x407EA0` → `0x437EF0`), and the level setup `0x648070` gives the analyser id 5 and the bank id 9, with the model-3 objects from 14 up. The analyser's handler `0x5AF220` therefore runs first: it closes its rings, samples the bank balance through `0x5C0740` (`return [bank+0x14]`) and zeroes the accumulators, all before the bank credits the batch and takes its instalments and before the objects debit their running costs.

OpenTPW closed the ledger and sampled **last**, so a month's batch credit, instalments and running costs were counted in that same month instead of the next. `ParkEconomy.EndMonth` now routes the analyser block through `CloseAnalyserMonth`, called first under Theme Park Inc's rules and last under Theme Park World's, and samples the bank balance there instead of `NetWorth(0)` — the twin handler `0x004C7720` in `tp.exe` is the one that samples the net worth.

`ThemeParkIncClosesTheAnalyserMonthBeforeTheBankCreditsTheBatch` pins both directions: the batch appears in the month that opens under Theme Park Inc's rules, and in the closing month under Theme Park World's. The economy, ledger, bank-word and Theme Park Inc selection passes (397 passed, 0 failed, 80 skipped).

Still open in ECON-060's own text: the rating byte's monthly rings stay Theme Park World's daily 30-day rings, which is a separate item. The `+0x1818` page limit setter that UI-004 needs is unrelated to this change.
