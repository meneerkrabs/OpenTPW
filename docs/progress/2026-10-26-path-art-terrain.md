## Paths are terrain cell textures, as in the original (October 26, 2026)

OpenTPW drew a path as a separate, lifted layer over the ground. The original has no such layer: the tile refresh writes the path tile's slot, turn bits and the art flag into the terrain cell's own word (`0x5365D0` → `0x450470` → `0x46DF50`), and the terrain draw treats the cell like any other (issue 829, package F9).

- **Procedural textures on.** The path tile is composited into the cell's N × N procedural texture (`0x56F090`, `0x572440`, `0x55F780`: the base read turned by the word, the art point sampled, alpha blended), and the cell is built again when its tile changes (dirty bit `0x100`).
- **Procedural textures off, outside the rectangle, and in the camera modes that suspend them.** The path cell is a quad of its own with the tile's texture turned by the cell word; the ground leaves the cell out and nothing lifts it, and draws opaque (the terrain pass gives every record the state `0x1256`, no blend or alpha test), so the tile's transparent edge texels show their painted grass.
- **Not changed.** Queue models stay separate objects; the cell map, the simulation, saves and co-op path commands are untouched (presentation only). No new approximation: PATH-015's wording now says static batches instead of the path layer.
- **Evidence.** `PathCellWord` and `PathCellArtTests`; 24 new composite cases of the x86 oracle (`terrain_compose_golden.py`, words `0x180` to `0x38A`, 128 × 128 art with a transparent border).
