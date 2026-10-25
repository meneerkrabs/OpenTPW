## Parks that use OpenTPW's own extensions can be published (October 25, 2026)

A park with winter snow and the earmuff kiosk could not be published: the online menu's Publish park line was greyed out and said nothing. The cause was that publishing counted OpenTPW's own extensions (pools, umbrellas, winter, evening torches and the rule extensions) as mods, and a park package recorded no mods. Only community mods still refuse a publish now.

- **Official or community.** Built-in extensions (`OfficialExtensions.All`) are official; every other mod is a community mod. `ParkSharing.ModsOf` splits what a park would record. `ModsRefusal` names only community mods.
- **The package records the extensions.** The manifest gets an optional `extensions` member (id and version). A park without official extensions still produces a format version 2 package with no new member, so older builds and servers handle it unchanged. A package with the member is format version 3, which an older build refuses with "Park package version 3 is not supported" rather than as malformed. The server stores the package byte for byte and validates the member; the protocol version does not change.
- **Visiting asks first.** A park that uses content extensions (pools, umbrellas, winter, evening) that the visitor has off asks "Turn Winter on and visit the park?" in the confirm dialog of a park load. Confirming switches the extension on as a load does and keeps it in `mods.json`; a visit never runs without it. An extension that cannot be switched on (switched off on the command line, a Theme Park Inc run, unknown to this build) gets a message and no visit; `--visit-park` refuses with the `--enable-mod` option. The rule extensions and the cheats extension are not needed to look at a park.
- **The menus say why.** The online menu's Publish park line and the pause menu's Publish Park line show the reason on hover when greyed: no park open, a park you only visit, a shared park's guest copy, or community mods.
- **Layout and status.** The postcard reminder no longer overlaps Delete account, and shows only while logged out; logged in it says where to send the cards. A status shows once, on the top screen, and only when it came after that screen opened, so "No search results" no longer shows twice or in a menu where nothing was searched.

**Verified:**
- The online, server, co-op, mods, front-end, pools, save-extension, park and Theme Park Inc filters ran with the game, Theme Park Inc and language data: 3,474 passed, 12 skipped, none failed. The browser build with the determinism pins compiles, and `BrowserPinsExclusionTests` passes (the server tests are in their own file, `PublishOfficialExtensionsServerTests`).
- The five native smokes pass (front end, sandbox, Jungle level, park-resume capture and restore). The front-end smoke now opens the online menu with a postcard waiting, rests the pointer on the greyed Publish park line and checks that the reason is drawn and that the postcard line sits under every button; run in English and Dutch.

Still open:
- Co-op stays strict: a shared park's host and guests must run the same mod set, as before.
- A park file's `extensions` member does not list the extension versions' differences; a visit draws the park with the visitor's version.
