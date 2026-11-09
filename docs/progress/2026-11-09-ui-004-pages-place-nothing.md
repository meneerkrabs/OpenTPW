## UI-004: no texture is ever placed on a cache page (November 9, 2026)

The register row described the batcher's grouping by cache page as a fidelity gap. It is not one: in this build the page manager can never place a texture at all.

The class pick `0x58F1C9..0x58F1E4` takes a class only while that class's used count is below its limit at `+0x1818`, and neither that array nor the used array at `+0x1830` ever receives a non-zero value. The constructor `0x58D0F0` zeroes `+0x1800` and `+0x1830` (`0x58D1B8..0x58D1CC`) and nothing else there; the driver `0x58F940` clears `+0x1830` every frame; a full `.text` displacement scan finds eleven references to `+0x1818` and every one of them is a read (the trimmers `0x58EA20` and `0x59001A` clamp a negative limit to zero and release the pages above it). The initialisation `0x58DA40` writes the per-class budgets at `+0x1860` but never the limits.

Run in the oracle harness with the device singletons stubbed, the constructor, the initialisation and the class walk all complete and write nothing at `+0x1818`, and `0x58F190` with a one-texture pending list returns `-1` after 122 instructions. The frame driver is nevertheless entered every frame — `0x576C5A` loads the cache global `[0x8C4B44]` and calls `0x58CF40` unconditionally — so the page system runs and can never place a texture.

So the shipped PC build draws its textures directly, and the page grouping, the page identity and the between-page draw order cannot differ from the call order because no texture is ever on a page. The row now says that, and keeps the half that is still unported: the per-window show sequence, the hide-and-re-show reordering and which sprite flush the control text uses.

The virtual-page plan in `OpenTPW-notes/plans/ui-virtual-pages-plan.md` loses its premise: it would have modelled a path that does nothing in the shipped game. Its tests and probes are recorded there so the finding can be reproduced rather than re-derived.
