## The park gate never shows the magenta sign placeholder (October 19, 2026)

A report showed the Jungle park gate's board with the shipped placeholder text "SIGN1 SIGN2". The gate's two sign slots now follow one rule (`SignSlotPlan`): the park name when the level's gate sign exists,
otherwise the existing plain brown board, for every fixed item, never the shipped magenta art. A gate built before the level's sign exists (or after its creation failed) therefore cannot show the placeholder either.
Creating the gate sign no longer stops a park for any exception type (it is logged with its type and the gate shows the plain board), and an object type's sign that could not be drawn, or whose archive had no files yet
(the browser copies a theme's files when a park starts), is no longer remembered as missing for the whole session.

Checked in the browser build (headless, muted, no screenshots, interpreter publish, English data): a new Jungle park, a second Jungle start after Exit To Lobby, a start from kept files and the night look all
build the gate sign (10,680 text pixels, "LOST Kingdom") and bind it to the gate object, so the report's exact cause was not reproduced; the fix closes every path found where a sign slot could fall back to the placeholder.
