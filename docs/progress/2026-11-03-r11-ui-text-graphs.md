## UI text, buttons, graphs and locator from tp.exe (November 3, 2026)

The finance graph now places its series as the shared graph drawer does (0x480460, 0x47FE90): n = min(period samples, months held)
+ 1 points from the left over steps of width / samples summed as floats, a 0 before the first month while the ring holds no
older one, whole-pixel columns and rows cut to the plot, a box round each segment end in the 1 and 3 year views and the last
value written at the line's end; the scale scans the samples 0 to the period inclusive (0x49A7B0, 0x4801D0, 0x480380). The park
information graph uses the same series renderer, so the two graphs no longer differ in column rounding. The pixel size stays
the host adapter `[EXT:graph-pixel-adapter]`. UI-060 is narrowed to the 32-bit wrap of a very large financial span (OpenTPW draws
the true value) and the raster composition.

List column headers wear the widget model `!cbut` (the b_header plate) with the colour (255, 239, 0) that the header builder 0x486660
gives them from its 59 call sites; they no longer draw as bare text. No PC screen draws a model-less text button, so UI-008 narrows
to OpenTPW's own screens (a user decision on their art). All 134 call sites of the text colour setter 0x65C5D5 are assigned to their
window builders in docs/UI.md; the exact red of cost rows, the HUD cost text's yellow and the black window texts replace video
estimates (UI-006 narrowed).

A locator key pressed while the arm carries a locator panel now retracts that panel first, and its close message zeroes the mode word
the key had just set, so the panel that comes out ignores its radios while still showing the pressed radio; Ctrl+V without a visitor
sets the word after the arm call and stays live (0x4B3F60, 0x4B4280, 0x4A2590, 0x1001D8). A deleted located visitor makes the locator
ask the arm to put the panel away (0x4B3620 from the thing event listener 0x50CA50). The roster's posts are read: the guest
constructor's message 0x1C and the deletion's 0x1B reach the listener that adds and removes the visitors list rows (GUEST-920 narrowed
to the seconds-based fallbacks). UI-091 is narrowed to the visitor position-loss fallback.
