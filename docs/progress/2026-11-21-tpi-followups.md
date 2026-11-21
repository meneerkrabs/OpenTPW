## Theme Park Inc: guest base speed and status texts after the bank-mail PR (November 21, 2026)

A Theme Park Inc guest now walks at its profile's speed. The constructor multiplies the drawn base-speed word by the profile's `SpeedMultiplier` and truncates the product to a word (`0x60C712`). The routine was already ported, but nothing applied it to a live guest. In the retail data, the grandparents' rows hold 0.33 to 0.47 and the parents' 0.81 to 0.95, so they walk at about a third and nine tenths of a child's pace. The guest creation site now cites `[EXT:tpi-twp-guest-rules]`: beyond the constructor, the guest walks, decides and animates by the shared Theme Park World rules.

ARRIVAL-008's text now says what the desktop slice already ports: rain and snow drawing, the precipitation audio, and the storm's lightning and thunder. What remains is the browser's precipitation drawing, the sky scroll and overcast values, and one guest-effects path. The website's Theme Park Inc pages no longer list the advisor as unavailable. They name what runs: the money, the weather, the advisor's weather forecasts and the bank's "Game Over" letter.

Two continuation questions from the review are answered in code comments rather than with save fields:
- A letter can never be open when the game saves, because the pause menu that saves does not open over it.
- The challenge firing line changes only when a challenge is lost, and the slice runs no Theme Park Inc challenges yet. The challenge port must save the line and the set of challenges lost once.

Verified: the Theme Park Inc, guest, arrival and determinism filters pass (1476), including a new test that a live guest's base speed is the drawn word times its profile's multiplier. The site status tests pass after regenerating the front page.
