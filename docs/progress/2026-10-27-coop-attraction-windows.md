## Guests run the rides, shops and sideshows when the host lets them (October 27, 2026)

The host of a shared park can now give each guest the attractions right too ([COOP.md](../COOP.md), "Money and rights (C4)", "A guest's attraction windows (C4b-2)"; `[EXT:online-coop-commands]`, `[EXT:online-coop-permissions]`, `[EXT:online-coop-claims]`). This is C4b-2, the second half of C4b; staff follows in C4c.

- **The object windows.** With the attractions right, a guest's ride, shop, sideshow and toilet windows act as the host's do: the speed, capacity and duration, the quality and ingredient, the prices and prizes, the typed price, apply-all, the door, the call for a mechanic or a handyman, and the upgrade list.
- **Each change goes at once.** The original's window keeps its values until it closes. A guest's window sends each change when it is made instead, so the host can refuse it before anything changes, and nothing is ever lost when the window closes or the park is rebuilt from the host's. The controls show what was asked until the host's answer comes, and a drag or a held button sends its latest value once.
- **The host's window.** The host's window still applies its values when it closes, but in a shared park only the ones the host changed, so a guest's change made while the host's window was open is kept.
- **In the order clicked.** A guest's changes to one attraction apply in the order they were clicked, so a visitor buying between two of them pays the price that stood then.
- **Doors, calls and upgrades.** One of these waits at a time for each attraction, and the window shows the ride as that action will leave it: a ride whose upgrade was bought shows as closed for its upgrade before the host's answer comes.
- **Upgrades bought twice.** Each ride counts the upgrades bought for it, so a guest's cancel never cancels an upgrade the host bought again in the meantime. Old saves and session logs load and replay as before.
- **Claimed rides.** A guest cannot change a ride another player is building; the host and the builder still can.
- **When a session ends early.** If it ends while some of a guest's changes were still on their way, the end message says "Some of your last changes may not have been made."
- **The live acceptance** gains a management run, checked in two native games and with the guest in the browser: the guest's purchase, the host giving the right through its dialog, a typed price and a ride's door, and a park rebuilt from the host's in the middle of a guest's change, which then goes once.
