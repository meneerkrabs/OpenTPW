## Content kit: Blender conventions, exporter and glTF reader (October 17, 2026)

The first part of the content kit for new, original objects ([CONTENT-KIT.md](../CONTENT-KIT.md)), phase 0 of the swimming-pool mod plan:

- **Blender scripts** (`tools/blender/`):
  - authoring conventions: one Blender unit is one MD2 unit and a cell is 10 units; the glTF export is Y-up like MD2; custom properties mark rider head points (`opentpw_head`), texture wrap and self-lit materials;
  - an exporter that validates a scene before it writes a `.glb` (head ids from 1 without gaps, texture-name length);
  - a sample generator that builds a 3 × 3 cell pool with six swimmer head points from code, so the sample has no original art.
- **`GltfScene`** reads what the exporter writes:
  - nodes with their parent-relative transforms;
  - triangle meshes;
  - materials with embedded images;
  - the `extras` of nodes and materials.
- **Refusals:** it refuses skins, animations, morph targets, sparse accessors, external files, required extensions and broken hierarchies or indices instead of ignoring them.

Tests: `GltfSceneTests` uses hand-built files, plus the real Blender export of the sample when Blender is installed (inconclusive otherwise). Nothing touches the game's simulation or data.
