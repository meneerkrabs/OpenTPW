## Theme Park Inc: review follow-ups (November 23, 2026)

A small clean-up of the non-blocking points from the reviews of the Theme Park Inc mission window, camera and facilities work.

- **Zone global files are read once.** The career keeps each zone's `global.sam` load (and a failed load, logged once) instead of reading and parsing it on every zone-record lookup, as the original loads it once per record (`0x7509C0`). The mission window's refresh asked for the record several times per frame.
- **The mission window's refresh fails once.** The refresh throws on incomplete data (a current zone without a record, a zone past the name table, an exit statistic past the table). The open still shows the failure; the tick now logs it once and keeps the last texts instead of throwing every frame.
- **Documentation.** `0x5013C0` has a second branch (it closes the window held in `[0xA5E890]` and sends no message 5); the Objectives key refuses in game mode 3 (`0x42F04B`), which OpenTPW never runs (its Theme Park Inc slice is the single-player career, mode 0), so nothing is ported for it; the mission window opens on the update after a zone-arrival flight ends (the countdown `0xA9D258` is 3 in that update); the letter go-to comment and the `RIDES-040` row no longer say the obstacles are not world things.
- **Already fixed on main, so not touched:** the player-folder link resolution (it shares `PathLinks.ResolveLinks`), the source-contract and symbolic-link tests, the helper test for the replacing-park flag, the staff panel under the pause menu and the save-path tests, `PressThemeParkIncMissionComplete` (the mission window's Proceed calls it), and the Science build list's research note. The net-worth routine was read against `0x5AFFB0` earlier. Extension objects (pools, umbrellas, winter, torches) and mod objects are not loaded in a Theme Park Inc run, so none can vanish from its build list.

No new approximation or label is added.

Verified with these checks:

- New tests for the single `global.sam` read and for the refresh that stops after its first failure; the Theme Park Inc, save and resume filters pass with the retail data.

The original game was not launched, and no binary or data is included.
