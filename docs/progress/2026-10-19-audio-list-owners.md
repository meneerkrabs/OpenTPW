## Every list owner traced: list rows never click (October 19, 2026)

`APPROX:AUDIO-041` is retired. The hook `0x485780` clicks (0x1F, or 0xBD under a style-0x10 parent) only for message 0x100,
and the only 0x100 senders in tp.exe are the button release `0x6691eb` and eight window keyboard paths. The type-7 list
constructor `0x662562` is reached only from layout records: 19 lists in 18 tables, whose owners (Load, Save, Find Parks, Other
Parks, visitors, overview, staff, hire, loans, buy, upgrades, the staff dialog, postcards, chat, the dial-up chooser and the IME
candidate list) send no 0x100 on any row path. A still click on the scroll thumb `b_scroller` sends 0x802 to the slider through
its own procedure `0x66bb9b`, never 0x100. The keyboard clicks only where a dialog turns its text field's Enter (0x802; the save
screen also Escape) into its tick, always 0x1F by the window's style 1.

Changed: `UiScrollList` is silent by default; the Theme Park Inc text HUD and the Extensions and mods screen (both OpenTPW's own)
opt in. Enter in the park rename's second line, in the employee rename and in Find Parks' search field now plays the tick's
0x1F through the tick or search button. A refused hire keeps its message but no longer plays the error 0x1D, which the original's
hire row (`0x49b988`) never plays; the buy and add-on refusals keep their 0x1D (`0x4ac7f6`, `0x4af5d6`). The unused `UiListItem`
is removed. [AUDIO.md](../AUDIO.md#list-owners-october-18-and-19-2026) has the owner and keyboard tables. No determinism pin moved.
