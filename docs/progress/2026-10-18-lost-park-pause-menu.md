## Lost parks use their five-entry pause menu (October 18, 2026)

The real HUD pause-menu builder now follows the branch order in `0x48C150`: read-only visits keep their three entries,
then a bankrupt writable park gets Load, Restart Park, Resume Game, Exit To Lobby and Quit Game. Save, Publish Park,
Options and Go Online are absent from that loss menu; healthy parks and levels without an economy keep their existing menu.

The five entries reuse the existing host callbacks, capability checks, restart/quit confirmations and Exit ownership guard.
Resume and Back preserve bankruptcy, balance and the speed setting. Twelve synthetic constructed-menu test methods (29 expanded cases) cover the ordering,
visit precedence, missing hooks, changed/replaced economies, repeated opens, the real load chooser, confirmations,
cancellation and pause time scale. They need no original assets or network.

ECON-030 remains open for the gate-camera, loss-root/drawing-resource and input lifecycle, plus the direct economic-guard
fidelity audit. Its stale warning-producer and tick-button claims are corrected: `AutomaticAdvisor` already produces advice
102–105 through `AdvisorScan`, and `0x48A740` has no explicit tick-button action. The loss layout is not proved empty.
See [the economy notes](../ECONOMY.md#lost-park-menu-and-remaining-loss-scene-econ-030).

No economy, simulation, save-format, determinism or camera runtime changes. Local .NET execution is unavailable because
this environment has no `dotnet`; the new C# cases require CI validation. Fidelity/progress validation and whitespace checks
pass, as do 49 Python tooling tests and the evidence runner (1,537 tests, 595 skipped). The site-doc checks pass after
generating the uncommitted fidelity-register build artifact.
