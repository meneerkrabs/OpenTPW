## Economy rules: bankruptcy, challenges, cell types, net worth and breakdowns (October 15, 2026)

Economy rules read again in tp.exe and built:

- **Bankruptcy** (ECON-030 narrowed): the bank acts once on message 2 (`0x4D02D0`), skipped while a winning sequence shows; `0x5168F0`
  sets world state 4, breaks every attraction (VAR_BREAKSTAT = 1) and sets the gate's variable 0 to 2. State 4 skips the thing loop
  (`0x516380`) while the calendar, bank, staff pool and golden tickets run on; the park stays open, and the warnings at 3 and 5 months
  are gone (the bank only compares with 5).
- **Challenges** go to the message tube with Accept and Decline, which call AcceptChallenge and DeclineChallenge (`0x48E0F0` actions
  `0x1A`/`0x1B` → `0x4D2DF0`/`0x4D2EC0`) for whichever offer is waiting.
- **Cell types** (ECON-050 narrowed): world init types each cell from the MAP and the terrain (`0x536490`): cells without terrain become
  7, 2 or 30, every cell without the fixed flag `0x20` gets the unowned flag `0x40`, which the level's land (Buy Land's `ParkLand`) clears
  inside the fence, and placement writes footprint types (`0x528A70` → `0x5346D0`).
- **Park rating and net worth**: the visitor term counts guests on cell types 0/1/3/9/10, capped at 1000 (`0x4CCEB0`, ECON-027 retired);
  owned cells add 2/3/5/6/1/50 points (`0x4C7DB0`) over the land Buy Land maintains, so the net worth and the loans' credit test count
  only owned land, and the net worth is cached as the original does (ECON-026 narrowed: the fence's hoardings are not counted).
- **Loans** (ECON-007 retired): the Available tab lists an offer that is not bought and passed the stored credit test, amount ≤ net worth
  − bought loans' remaining instalments (`0x4D06B0`), run when the dialog opens or a tab is selected; the stored tests are imported from
  original saves (SAVE-008 narrowed). The park save gains the optional `AvailableLoanOffers`; older saves get the bank's default.
- **Litter and path block caches** (`0x4C8BA0`, `0x4C8AE0`) are read with the strike's and the thought's ages (ECON-020 narrowed).
- **Breakdowns go through the ride scripts** (ECON-023 narrowed): `0x4E0B90` writes VAR_BREAKSTAT = 1, the ride's state 1 breaks it down and
  calls a mechanic, and the repair waits until the script clears VAR_BROKEN (`0x4DA830`).
- **The bank's message 2 every month** (ADVISOR-020 retired): `0x4D0370` posts it at each month end more than five months in the red,
  and the advisor answers each with advice 106; the bank's handler still loses the park once.
- **Saves**: the analyser caches (net worth, litter and path blocks) and the last observed script state are saved as optional fields
  outside the run hash, and an unanswered challenge offer whose message is gone is posted again (UI-025).

The pinned run hash moves to `0xA2FAA54FEB56DA05` on top of the ticket booth, the guest walk and the staff candidate draws (docs/DETERMINISM.md).
