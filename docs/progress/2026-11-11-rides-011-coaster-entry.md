## RIDES-011: the coaster tool's entry writes the sticky tool globals (November 11, 2026)

The entry's residual was that the coaster marker tool does not model its share of the native sticky globals `0x7639E0..0x7639EC` — the exit and entry cell pairs that the select `0x5276D0` writes.

Reading the executable shows the coaster tool has no writer of its own: its share *is* the select at entry. All three ways into it run `0x5276D0` first — the ride window's track button `0x4AF200`, the marker-cell press and panel entry `0x4988C0`, and the track tool `0x497D20` — and each stores the selected coaster's anchor mapped through `0x52A3A0` and `0x52A420`. The pylon and panel clicks themselves never touch the pair.

`BeginCoasterToolEntry` now calls `NoteToolSelection(station)` the way the kart and water tools already do, so a later bulldoze, ghost or link walk that reads the pair sees the coaster's cells instead of the previously selected ride's. That matters because the pair is not inert: the demolition clear `0x53B280`/`0x53B54A` spares the named tiles, the walk `0x52A970` and the ghost `0x532DD0` read it, and the bulldozer path deliberately selects nothing first.

What stays open is the type-3 tail of the click handler (`0x5251F5` and `0x525838` write the raw anchor when `0x52FAF0` answers 3), whose reachability from a coaster marker tool rather than a coaster-track add-on could not be shown.
