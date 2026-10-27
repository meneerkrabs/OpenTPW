## The land outside the park is darker, as in the original (October 27, 2026)

tp.exe sets bit `0x400` of a terrain cell's landscape word for unowned land outside the entrance area (`0x450D10`), and the terrain draw halves the vertex colour of such a cell's corners (`0x56F670`) unless the LIGHTING option is 0 (`0x44DC28`). OpenTPW drew all land at full brightness.

- **Ported for static and procedural terrain,** the static path cells and the cells outside the procedural rectangle included. The map cell is clamped to the map as in the original, the live cell flags are read (a land purchase brightens the bought cells), and `test.shader` halves the lighting colour of a flagged corner (`LandDimFlag`, web shader regenerated).
- **Presentation only.** The cell map, the economy, saves and co-op are untouched; no label.
