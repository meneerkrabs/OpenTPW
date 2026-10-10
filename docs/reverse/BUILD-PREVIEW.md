# Build-menu preview model

2026-10-10. Static analysis of the Mac PowerPC `SimThemePark.data` (SHA-256 `04809cd4…e295f5`,
see [FINDINGS.md](FINDINGS.md)) with Ghidra 12.1.4. No original program was run, and no decompiled code
is kept in this repository. Addresses are Ghidra addresses: code starts at `0x10000000`.

## Question

Seven jungle ride archives have no `P<name>.MD2` preview model: `bouncy`, `bumper`, `incagod`, `monkey`,
`mystery`, `tourride` and `volcano`. The `porkpie` archive has none either. OpenTPW drew only `P*.MD2`
files as build-menu icons, so these slots stayed empty. What does the original show for them?

## Answer

The original shows the object's main model. The preview model is optional: the original instances it
when it loaded, and the main model otherwise.

1. **Level load (`0x10119328`).** Each buildable object type's mesh is loaded with flags `0xb24a9`, which
   include `0x20000` ("also load the preview"). The object types that take the other branch use `0x50c00`,
   without that bit. The mesh id is stored in the object type at `+0x78c`.
2. **Mesh load (`0x100594c8`).** After the main mesh loads, bit `0x20000` makes the loader format
   `p%s` from the model name and load it into the mesh record's preview slot (`+0xd0`) through
   `0x10058a3c`. When that file does not exist, `0x10058a3c` returns 0 without writing the slot, so the
   slot stays empty.
3. **Buy window (`0x10162584`).** The buy window sets the text of control `0x1ec`, a child of preview frame
   490 (`0x1ea`). It passes the frame rectangle and the object type's mesh id (`+0x78c`) to `0x10139084`.
   That function maps the rectangle from the 2048 × 1536 interface space to the screen, removes the
   previous preview (`0x1005cb9c`) and keeps one preview at a time.
4. **Instance (`0x1005c35c` → `0x10059f00`).** The preview instance is created with flags `0xc01`. In
   `0x10059f00`, bit `0x400` selects the mesh. If the bit is set and the preview slot is filled, the
   preview mesh is instanced; otherwise the main mesh is. Objects placed in the park never set `0x400`:
   the callers that do pass kinds `0x32f`, `0x361` or `0x33a`, so they always show the main mesh.

`*i.MD2` models (for example `bouncyi.MD2`) are not icons: they exist next to `P*.MD2` models as well
(`spideri` beside `Pspider`).

## In OpenTPW

`OriginalBuildCatalog` uses `PreviewModelPath ?? ModelPath` for every non-bonus object, labelled
`[BIN:STP-PPC:0x10059F00 …]`. Bonus objects still have no icon, because their models live in a separate
file system that the icon loader does not read.

The original layout is not changed by this. It has one preview frame next to a three-column text catalogue,
not a grid of turning icons (UI-024). The icon projection, tilt and turn rate remain the UI-026
approximation.
