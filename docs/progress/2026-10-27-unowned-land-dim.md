## The land outside the park is darker, as in the original (October 27, 2026)

tp.exe sets bit `0x400` of a terrain cell's landscape word for unowned land outside the entrance area (`0x450D10`), and the terrain draw halves the vertex colour of such a cell's corners (`0x56F670`) unless the LIGHTING option is 0 (`0x44DC28`). OpenTPW drew all land at full brightness.

- **Ported for static and procedural terrain,** the static path cells and the cells outside the procedural rectangle included. The map cell is clamped to the map as in the original, the live cell flags are read (a land purchase brightens the bought cells), and `test.shader` rounds each lighting channel to a level, clamps it to 255 and halves it like the original's `shr 1; and 0x7F7F7F` (`LandDimFlag`, web shader regenerated).
- **Presentation only.** The cell map, the economy, saves and co-op are untouched; no label.
- **LIGHTING 0.** It now turns the dimming off (`0x44DC36`); without original detail data the dimming stays on, as the original's zeroed render flags have it. Not ported: with that option tp.exe also fills the terrain colours with one white constant instead of lighting them (`0x574530`); the option stays labelled as partly applied.
