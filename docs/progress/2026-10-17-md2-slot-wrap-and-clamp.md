## MD2 texture slots wrap or clamp like tp.exe (October 17, 2026)

Original MD2 textures now follow the slot's own address mode instead of wrapping every slot:

- **The rule, re-read in tp.exe via REA:** slot flag 0x10 or 0x20 becomes texture bit 0x10000/0x20000 (`0x575E9C`), either
  bit sets render-key bit 0x400000 (`0x581847`: `test 0x30000`, `or 0x400000`; `0x590C40`), and the stage decoder `0x567620`
  sets `D3DTSS_ADDRESS` to WRAP for that bit and CLAMP without it (`0x5678CF` through `0x567A70`: state 12, values 1 and 3).
- **Per corner, not per texture:** objects, tracks, tours, coaster cars and pylons, terrain scenery, lobby models and the
  advisor mark each corner of a non-wrapping slot with OpenTPW's vertex bit 0x80000000 (`TextureSlotAddressing`), so one
  image shared through the texture cache by a wrapping and a clamping slot draws correctly in both. These renderers bound
  their 16-slot arrays with one wrapping sampler; the texture's own `TextureFlags.Repeat` (mirror) never reached them.
- **The clamp is an emulation** (`[EXT:md2-clamp-in-shader]`): WebGL has 16 texture units, so test.shader and advisor.shader
  clamp the coordinates on the wrapping sampler (half a texel of the coarsest selectable mip inside the edges) and sample
  with `textureGrad` and the unclamped derivatives. A clamped slot keeps the graphics preset's filter instead of the
  original's point `MINFILTER` (COMPAT-010 narrowed).
- **Corpus:** 2,457 of the 7,282 slots wrap; 67,039 of their corners lie outside 0..1 and no corner of a clamped slot does,
  so the change only stops a clamped slot's edge texels from bleeding in the opposite edge.

Rendering only: the run hash and saves are unchanged. Verified with the full test run with the game data and the native
shader tests, and with silent native jungle captures before and after: on pixels that are stable between two runs, the
change is confined to one-pixel lines along the seams of clamped slots (the river-bank scenery), and wrapping slots tile
as before.
