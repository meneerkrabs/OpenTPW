## The advisor's background scan and the money warnings (October 17, 2026)

- **The scan driver is `0x59C680`, as the original runs it.** `AdvisorScan` keeps the cursor over the 351 descriptors, skips the rows game code posts without counting them
  (the loop's counter is bumped only at `0x59C822`, so one call walks to the next scanned row; a pass is 155 scored updates, not 351 as the mapping first said), wraps at
  351, applies the game-mode selector, dispatches to the callback by its address and keeps the strictly highest eligible score. `AdvisorController.ScanOnce` queues a score
  above `MinScoreForConsideration` through the real queue. A callback that is not ported scores 0, like the original's stub `0x5D60C0`.
- **State.** The cursor is controller state: it is in `AdvisorControllerSnapshot` (a snapshot without it restores 0, and a cursor outside 0-351 is rejected) and an original
  save import starts it at 0. The advisor is outside the simulation hash, so no determinism pin moved; the headless runs that pin hashes attach no advisor. The advisor runs once per
  frame here, so `AutomaticAdvisor.ScanTurns` scans once per park turn passed (at most 32 per update, none while paused), noted under ADVISOR-017.
- **Money batch (wired).** 102 (take a loan), 103/104/105 (no loan available, months in the red below 3, 3 or 4, exactly 5), 100 (rich and no loan taken), 101 (the cash left
  after all debts covers the cheapest loan times `Margin`, with the original's single-precision compare) and 107 (staff wages above the income times a factor over the newest months
  of the monthly rings) are ported from `0x59E8D0`-`0x59EB80` over `EconomyMoneyState`, which reads the bank and the ledger without writing to them (the net worth is read fresh
  and the offers' stored credit tests are left alone, where `0x4D0810` refreshes them). 101 and 102 are for the full simulation only (mode 0).
- **Golden-ticket advice 234-237 now speaks.** `AutomaticAdvisor.GlobalTicketMovedMessage` posted them but `AdvisorTables` had no definition; the four `AwardMoved.Score` rows
  are added (group 2, read from the settings file when it has the group).
- **Tests.** `AdvisorScanTests`: cursor step and wrap, rows skipped for free, unported callbacks at 0, the mode selector, strict best and minimum, the tutorial option, the snapshot,
  each money callback at its thresholds (single-rounding edge of 101 included), the economy adapter against a real `ParkEconomy`, a park in the red going through the real queue and
  selection to 103, 104 and 105, and 234-237 now speak. The producer inventory marks 100-105, 107 and 234-237 wired. ADVISOR-009 narrowed: 127 computed callbacks remain.
