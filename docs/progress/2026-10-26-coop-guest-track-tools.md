## Guests build kart and water ride tracks in a shared park (October 26, 2026)

A guest the host lets build can now lay and edit the track of a kart or water ride in a shared park, one player per ride at a time ([COOP.md](../COOP.md), "A guest's track tools"; `[EXT:online-coop-claims]`). This is C3c-1b, after the rides' claims; a guest's coaster tool comes next.

- **The track button and a placed ride claim through the host.** A guest's track tool asks the host for the ride's claim first and starts once the answer is back: a closed circuit opens the edit tool, an open one the line tool from the end of its track. Escape, a right click or another tool cancels the wait.
- **Guests place kart and water rides.** Their track tool follows the placement through the claim, and a ride with a queue hands over to the queue tool once its circuit is closed, as the host's does.
- **Lines, the cut and Backspace go to the host one at a time.** The clicked line or cut stays as the ghost until its outcome, and the tool goes on from there: a closed circuit ends it, follows the queue, or keeps the line tool when the click held exactly Ctrl. Under its claim a guest's Backspace takes its last line back.
- **A claim the host lets go ends the tool.** When the host frees a guest's claim (the guest was away for 30 seconds, or idle on the ride for five minutes), the guest's track tool ends with "You are no longer editing this ride.", in every language. A line refused for that reason says the same.
- **Taking a guest's building back frees its ride.** When the host takes a guest's grant back, the guest's claim goes with it at once, so the ride is open to the others again without waiting for the five-minute lease.
- **Fewer needless refusals around a claimed ride.** Taking back a path line that only crossed the path cell a claimed queue joins is no longer refused, and the host's track tool says "Another player is editing this ride" only when that is why its claim was refused.
- **Still the host's:** coasters and track add-ons.
