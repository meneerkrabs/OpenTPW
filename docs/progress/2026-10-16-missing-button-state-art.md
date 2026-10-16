## No white square for a disabled call button (October 16, 2026)

A ride window with no mechanic hired showed a white square where the call-mechanic button sits. The button is disabled then
(UIHELPTEXT 574), and the disable frame of `b_callmech` names the texture `b_callmechg`, which `ui.wad` does not ship; an
unresolved texture draws as untextured white. The same holds for `b_callhandy` (a toilet's call button) and `b_upgrade`, and
for the pressed frames of the unused `b_loantog`; no other state frame of a UI model whose normal frame draws from `/ui/textures` lacks its art (pinned by a corpus
test). `UiButton` now draws the normal frame for a state frame whose art is missing, greyed when disabled
(`[EXT:missing-state-texture]`). That is not the original's look: tp.exe's texture binder `0x46EAD0` logs "Could not load
texture" and binds entry 0 of its texture table instead, whose contents at run time are not known. The old note that `b_callmechg` was the highlight frame was wrong: it is the disable frame.
