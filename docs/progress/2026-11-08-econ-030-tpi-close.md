## A Theme Park Inc park closes instead of being lost (November 8, 2026)

Theme Park Inc has no loss scene. Its bank's month tick `0x5C0B10` stamps the turn the balance first goes negative (`+0x128`), converts that stamp to months, and at **`0x5C0C99: cmp eax,6; jl`** — six or more months in the red — builds `CMsgEvent(2)` and broadcasts it on the global bus `0xA09728`. That is the ordinary **park-closed** event, the same one `0x64F640` sends when the player closes the park; it writes no state, opens no window and loads no asset, and the bank module holds no file name at all.

The lost state the register described is Theme Park World's. In Theme Park Inc, state 4 has a single writer, `0x649E92` inside the level-teardown routine `0x649E60` (its callers are the win/lose/quit branches), and no economy or bank code writes it.

`EndMonth` now routes a Theme Park Inc park through `ClosePark()` on the six-month rule — raising the ordinary close and leaving `IsBankrupt` alone — so the refusals that hang off the lost state no longer apply to it, which matches an original that blocks no purchase, hire or admission on bankruptcy. Theme Park World is unchanged.

`SixMonthsInTheRedCloseTheParkAndItKeepsTrading` pins it (the park closes, no `Bankrupt` event, a build is not refused), and `ThemeParkIncClosesTheParkAfterSixMonthsInTheRedInsteadOfLosingIt` pins the same through an imported negative balance. `TheBanksMonthComesBeforeTheObjectsDebits` keeps its ordering claim with the closed park asserted instead of the lost one. The economy, ledger, bank-word, advisor and Theme Park Inc selection passes (921 passed, 0 failed, 109 skipped).

The `[APPROX:ECON-030]` text now records the read behaviour and narrows the residual to what is still unknown: who consumes the 36-byte notification the bank broadcasts as message type 25 (`0x5C09D9`), whether a level script writes `mWorldState` itself, and the advisor's own `Bankrupted` and `InTheRed*` lines.
