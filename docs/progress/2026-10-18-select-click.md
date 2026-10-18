## Select click plays cat_ui 0xBD (October 18, 2026)

A button plays `cat_ui` 0xBD instead of 0x1F when the parent that receives message 0x100 has style bit 0x10. The hook is `0x485780` and the choice is at `0x4857cb`. The button class `0x668f9c` posts 0x100 to its parent (`[+0x60]`) at `0x6691eb` through the queue `0x658ccc`. The pump `0x658e42` calls the hook at `0x658eb2` (the dispatcher `0x658d9f` does too).

Those parents are the vertical list scroll bars (type 3, style 0x10: their up and down arrows), the sortable lists (type 7, styles 0x91, 0x291 and 0x11: their column headers) and the radio groups (type 8, style 0x11: their toggle tabs). Every other button plays 0x1F. A held arrow waits 500 ms and then steps every 125 ms (ctor `0x6688b3`, timer `0x668dd9`). The release `0x668eff` plays nothing once repeating has started. A quick click plays 0xBD and scrolls one step.

AUDIO-004 is retired. A list row click posts 0x400–0x406, not 0x100, so the hook may play nothing for it; `UiScrollList` still plays 0x1F. That question is AUDIO-041 and is left open.
