## Claims keep a ride to the player editing its track (October 26, 2026)

In a shared park, a kart or water ride's track is now edited by one player at a time ([COOP.md](../COOP.md), "Claims"; `[EXT:online-coop-claims]`). This is C3c-1, the first part of C3c; a guest's own track tool comes next.

- **A track tool claims its ride.** When the host's track tool starts on a kart or water ride, it takes the ride's claim. If another player already holds that claim, no tool starts, and the host is told "Another player is editing this ride."
- **A claim keeps the ride from everyone else, the host included.** While it lasts, nobody else can lay or cut its track, sell, remove, move or demolish the ride, put an add-on on it, or edit its queue. Each such refusal gives the same reason, in every language. Opening, closing, settings and prices are not affected.
- **No tool event lets a claim go.** Each machine checks once per frame whether it holds the claim of a ride its track tool is not on, and lets it go. So every way of leaving the tool frees the ride, and turning the edit tool into the line tool on the same ride keeps it.
- **The host frees a guest's claim** when the guest leaves, after it has been away for 30 seconds, and after five minutes without a command of its own on that ride. The check also runs while paused, and in a late frame.
- **A claim moves with its player.** Claiming another ride lets go of the first, so each player holds one claim at most.
- **Claims are session state.** A late joiner receives them, the session's checks cover them, and saves never hold them.
- **Track rides are ordinary rides while nobody claims them.** A guest can sell or clear an unclaimed kart or water ride, or edit its queue. A guest can now also place bumper rides, whose queue tool follows.
- **Still the host's:** a guest's track tool (next), coasters and track add-ons.
- **Two follow-ups to the guests' building.** A guest's Backspace in the slide mode that the host's rights refused for want of a grant now says so; only an undo that met a finished slide stays quiet. What a refusal says now follows the reason the rights gave when they refused it, so a grant given or taken back since does not change it. And the exit of an object without a queue, which lays no cell and takes no queue's link off, no longer makes a placement beside a claimed queue a refusal.
