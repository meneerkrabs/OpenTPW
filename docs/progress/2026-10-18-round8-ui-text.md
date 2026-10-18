## How tp.exe puts UI text on screen, read further (October 18, 2026)

Fidelity round 8 read more of the path from a text control to the screen. No behaviour changes, no pinned hash change; the open approximations UI-006, UI-007, UI-013 and UI-037 now name exactly what is still unknown.

- **The text control is a sprite object.** The sprite record at control +0x54 holds the colour at record +0x24 (the control's +0x78, the value `0x65c5d5` stores). The sprite draw `0x58a010` submits the control's quads as pre-transformed vertices with that colour as the only diffuse colour, so there is no shadow pass or offset anywhere on the way (UI-007).
- **The text bitmap is not 5-5-5 by default.** The texture format index is 4-4-4-4 or 8-8-8-8 by one option bit; the 32-bit blitter keeps the destination's alpha byte just like the 5-5-5 one. What turns grey coverage with alpha 0 into soft edges is the quads' blend and texture-stage state, which is the remaining unknown (UI-007, UI-037).
- **The park pause menu's online variant** has top 0 and gap 0 instead of 5 while a net-session field is non-zero; an offline park matches OpenTPW's top 10 and gap 5 (UI-013).

UI-094, UI-107, UI-930, UI-003, UI-004, UI-008 and UI-018 were triaged and keep their texts: their open parts need runtime state, captures or code that did not yield statically within the time spent.
