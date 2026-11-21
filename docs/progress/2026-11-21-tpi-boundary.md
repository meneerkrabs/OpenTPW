## Theme Park Inc: the park boundary and its growth (November 21, 2026)

A Theme Park Inc park now owns only Theme Park Inc's own land, following the static trace in docs/reverse/TPI-BOUNDARY.md.

- **Starting land.** The prebuilt Science park keeps the ownership its save stores (1259 owned squares; OpenTPW used to treat every square as owned). A new water or arabian world runs the executable's own steps: every square but the NoModify ones becomes unowned, the gate strip beside the entrance is opened, and the zone's `hoardings.map` is applied with the land loader `0x678B00`. That loader reads the file x-major, skips the map border and only ever adds land. A fresh Science world built this way matches the prebuilt park's flags on all 16,384 squares.
- **Growth.** `Level.ApplyThemeParkIncExtent` applies `extents/extent.NNN` with the same loader, ready for the mission controller. The owned counts per zone after each extent are science 1259, 3030, 4391, 6360, 7500; water 1330, 2456, 4042, 6225, 7500; arabian 2323, 3476, 4723, 6325, 7500. The campaign plan's higher counts include the files' border zeros, which the loader skips.
- **Rules.** Paths, queues and objects are refused on unowned land, as the executable's cell validator refuses them. No fence is laid, because career mode lays none, and the land is not drawn darker, because Theme Park Inc's boundary drawing is not read yet. There is still no Buy Land.
- **Catalogue.** The build list applies the catalogue's gate. The four special rides of the Science park are hidden in every mission. Thinking spaces appear from mission 1, and training rooms from mission 2 (or in mission 1 once the obstacle research is done); until the career runs, the mission number is 0.
- **Saves.** The Science continuation save and the layout checkpoint keep the ownership as one bit per cell, and a load does not re-apply any land file. A save written before this change would own every square, so it is refused with a message asking to start the park again.

Verified with these checks:

- New tests cover the loader's byte order, border and short-file rules, the new world's init and gate strip, the prebuilt equivalence, every zone's counts through its extents, the fence-post refusal, the bit-map save and the refusal of older saves, the path tool's refusal and the catalogue gate. The Theme Park Inc test filters pass with the retail data.
- The silent, offscreen Science smoke exits 0. It now arms the Bellows, because the special rides are no longer listed, and checks that a save without its owned land is refused.

The original game was not launched, and no binary or data is included.
