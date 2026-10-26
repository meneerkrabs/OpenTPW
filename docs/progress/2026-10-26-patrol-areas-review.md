## Patrol areas reviewed against tp.exe, with the preview colour and the locator's highlight (October 26, 2026)

Issue 829 asked for a route for the cleaners. The original has no routes: each member patrols a rectangle, set from the employee window. The feature was already in the game; this review traced the whole flow again in tp.exe, found it matching, and added the two parts that were missing.

- **How a player uses it.** Click a staff member, press the footprint button in the employee window ("Patrol area"), click two opposite corners on the map. Pressing the button again ("Whole park") clears the area. Escape (or a quick right click, with right click cancels on) leaves the tool without a change; a second click on a red rectangle ends it with the refusal sound and sets nothing.
- **Traced and found matching.** The button (`0x4B5B50`, `0x4B5AB0`), the two clicks with the corners ordered and inclusive (`0x524960`), the cell-id words and their save and import, the area test and the walk into the area (`0x506ED0`, `0x506F30`, `0x4F9490`), and the litter and toilet searches that ignore the area (`0x4D7100`). docs/STAFF-PLACEMENT.md describes the flow with its addresses.
- **Preview colour.** The marked border is red when every map cell of the rectangle is unowned land or a fence post, blue otherwise (`0x5234D0`, `0x532A50`, `0x535670`); it was always blue. The second click on a red rectangle now ends the tool, plays the refusal cue and sets nothing (`0x524A63`).
- **Highlight.** The staff locator (Ctrl+S) now draws the located member's rectangle in blue while the camera follows it (`0x4B3640`). This narrows UI-091.
- **Documentation.** docs/ECONOMY.md said patrol areas are not simulated; they are.
- **Tests.** `PatrolAreaFlowTests` drives the real path: the employee window's button, two park clicks, the stored rectangle, the clear button, the cancel, the preview cells and colour, the locator's highlight. `HandymanTests` shows a handyman sweeping litter outside his area and walking back in. `--debug-hud` gained `patrol:` and `locate:` actions for captures (Debug builds).

**Verified:**
- The staff window, handyman, linked-access wander and new patrol tests pass; native captures of the Jungle level show the preview border while the tool runs and the highlight while the locator follows a member.

Still open:
- Every park tool acts on the left press where the original acts on the release, and the pointer pick returns no cell off the grid where the original clamps; both are HUD-wide, not patrol-specific.
