## Advisor help for the panels, tools and object windows (October 17, 2026)

Every help message that tp.exe posts through `0x486B00` (40 call sites found by scanning the executable for calls of the builders `0x486B00` and
`0x59B590`) is now bound where OpenTPW has the window, panel or tool:

- **Panels:** the message panel (284, withdrawn when it closes), the map window (304), the locator panels (198 staff, 287 visitors; their close
  withdraws both) and the coaster tool's panel (289, 292, 293, 294 or 295 for a flume coaster on the button press; all five withdrawn when the panel
  goes; 205 after every tool click).
- **Tools:** the path tool (184 for the first accepted click, 214 for the third click after it), the queue tool (203 for every way into it, 337 when a
  click ends it without joining a path), the buy list (201 for a ride while none is placed, 206 otherwise, 346 Buy Land, 345 Clear Land), the patrol
  button (193) and the employee window's refresh (212 for an employee with an own patrol area, withdrawn next to 192).
- **Object windows:** the window is chosen by `Info.WhichUIType` as the dispatcher `0x486920` does: a feature that provides relief posts 213 below a state
  of 25 and 299 otherwise, one that chills you out 300, any other feature 298, a ride-type add-on of `AddOn.UpgradeType` 2 posts 301, and a fixed item
  has no window and no help. Features used to post the shop's 209.
- **Mode changes:** `ParkHud` reports a change of the tool it runs, so the ten mode help messages are withdrawn when a tool starts or ends (not only
  when a screen opens). A tool's help waits for that report, as `SetMode` (`0x52F200`) withdraws before the tool posts; a help posted in the frame its
  tool ended (the third path click that ends the tool, a coaster click that closes the circuit) is dropped, as the original's `SetMode(0)` withdrew it
  after the post.
- **Not bound:** 285 (the All Staff window), 308 (the address book), 307 (the postcard window posts only outside game type 1, and OpenTPW's postcard is
  online only) and 36 help ids that no call names.
- `advisor-descriptors.toml` keeps the scan flag of the descriptors (word `+0x0` of `0x76E300`): 155 messages are scored by the background scan
  `0x59C680` (one per update), the other 195 are posted by game code. Nothing scores the scanned ones yet; the register text names their inputs (134
  callbacks over 85 park-statistics helpers) and the game-code posts that still have no producer (66, 68, 69, 215-237, 311, 315, and 167 outside Instant Action).

**Narrowed ADVISOR-009** (not retired: the scored messages remain). The pinned run hash and saves are unchanged: help is presentation only and no
world or guest RNG draw was added.

The advisor also says the staff strike messages (33-57, replacing the direct tube post when an advisor runs), research completions (93-97), new research groups (133-136) and all research done in Instant Action (167); the determinism pin is unchanged.
