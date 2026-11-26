## Theme Park Inc: the navigator's edge rules (November 26, 2026)

The first phase of the Theme Park Inc navigator port (NV1): the walkers' edge test, behind a hidden switch.

- **The edge test.** Theme Park Inc's `0x5D7B60` is Theme Park World's with three rules that persons meet: an obstacle's walkway cell (the obstacle flag `+0x24`) is no walkway fast path, unowned land blocks after the path and walkway rules, and terrain holes (type 8) block. `WalkEdges.Blocked` reads them under `WalkEdgeRules.ThemeParkInc`, which the park's cell map carries; the step commit and the route search follow. Masks 2 and 3, and the rules only they reach, are unreachable in Theme Park Inc: every writer of the walk mask stores 0 or 1 and the camera passes 0 then 1.
- **The switch.** The hidden option `--tpi-native-walk` (off by default) selects the rules for a Theme Park Inc park. A Science save records the setting in an optional member (switch-off saves are unchanged), and a save made with the other setting is refused in both directions. Off, every walk is as before, so no determinism pin moves; the Theme Park Inc pins move once, when the port's last phase turns the switch on. Theme Park World's walk is unchanged.
- **Separation.** The gardener is already one of separation's neighbours, as in Theme Park Inc's neighbour query `0x63F1E0`; a test pins it.
- **The oracle.** Three new x86 targets run Game.exe's edge test, step commit `0x63F5D0` and route search `0x640C50` on synthetic maps with holes, obstacle walkways and unowned land: they agree on every input with the Python statement of the port (the C# meets Game.exe's results directly on one map), and Theme Park World's rules differ (docs/reverse/ORACLE.md, X25).
- **Notes.** docs/reverse/TPI-NAVIGATOR.md describes the navigator, the edge test rule by rule and the phases still to come.

No new approximation or label is added, and no C runtime `rand()` is reached.

Verified with unit tests that hold Game.exe's own results on one map (every edge for masks 0 and 1, six searches, diagonal commits and a whole walk that keeps off unowned land), the save switch's refusals in both directions, the gardener test, the new oracle targets and the existing X25 targets, the Theme Park World walk, guest and pin suites, and the silent, offscreen Science smoke with the switch off and on.

The original game was not launched, and no binary or data is included.
