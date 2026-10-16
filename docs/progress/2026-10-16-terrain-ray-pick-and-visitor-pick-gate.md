## The terrain ray pick and the visitor pick gate from tp.exe (October 16, 2026)

Cursor and visitor picking, read in tp.exe via REA:

- **The terrain ray pick** (RIDES-021 narrowed): `TerrainRayPick` follows `0x45bf90`, testing each cell's two triangle planes (split by
  cell-word bit 0x2) and taking the nearest hit that lies inside the cell's square; the hovered cell is the hit divided by the cell size
  (`0x45d560`), handed by the click command `0x46c5d0` to `0x524790`. On original levels the build cursor, `Level.TryGetGridCell`,
  `TryGetPlacementPosition` and `TryProjectCell` use it; the generic sandbox keeps the ground plane (`[EXT:build-cursor-ground-plane]`).
- **The visitor pick gate** (UI-930 narrowed): a visitor is picked only with Shift alone and no locator panel out, or with the visitor
  locator panel out and no modifier (`0x487013`), and only when standing in the hovered cell or one of its eight neighbours (`0x532bd0`,
  offset table `0x81ED40`). A plain left click on a visitor no longer selects them, as in the original; object selection is suppressed in
  those states, because the hover handler `0x486d90` never combines visitor and object picking.
- **Object selection** (UI-027 narrowed, read): the ray's object hit `[0x7a1cd4]` selects nothing (only the camera block reads it);
  objects are selected through the pick sprite pass `0x5890e0` (sprites flagged 0x200) and the 3 × 3 cell owner lookup `0x532bd0`, then the
  click dispatcher `0x4879d0` opens the object window `0x4b6080`.
- **The per-visitor window** (UI-910) is described: handler `0x4b7720`, refresh `0x4b72a0`, controls `0x3720`–`0x3738`, layout `0x7575e0`.

Presentation and input only: the pinned run hash and saves are unchanged.

Still open: the object half of the nearest hit (`0x45cdc0` → `0x45d090`), keeping the previous hit when the ray finds nothing, the pick
sprites' positions and sizes (word `+0x114`, `0x475ef0`), the visitor pick circle (radius² 0.0002 × (w² + h²) in clip units), and building
the per-visitor window.
