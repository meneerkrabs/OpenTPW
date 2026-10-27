## The land outside the park is darker, as in the original (October 27, 2026)

tp.exe sets bit `0x400` of a terrain cell's landscape word for unowned land outside the entrance area (`0x450D10`), and the terrain draw halves the vertex colour of such a cell's corners (`0x56F670`) unless the LIGHTING option is 0 (`0x44DC28`). OpenTPW drew all land at full brightness.

- **Ported for static and procedural terrain,** the static path cells and the cells outside the procedural rectangle included. The map cell is clamped to the map as in the original, the live cell flags are read (a land purchase brightens the bought cells), and `test.shader` rounds each lighting channel to a level, clamps it to 255 and halves it like the original's `shr 1; and 0x7F7F7F` (`LandDimFlag`, web shader regenerated).
- **Presentation only.** The cell map, the economy, saves and co-op are untouched; no label.
- **LIGHTING 0.** It turns the dimming off (`0x44DC36`) and draws the terrain unlit: `0x574530` (test of bit `0x800` at `0x57453A`) copies one white constant, `0xFFFFFFFF`, into every terrain colour and skips the light sum, so `test.shader` shades such a corner with 1 (`TerrainUnlitFlag`). The evening extension still darkens that white on top. Without original detail data the dimming and lighting stay on, as the original's zeroed render flags have it.
