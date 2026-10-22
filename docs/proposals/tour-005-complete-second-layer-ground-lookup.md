# TOUR-005: Complete second-layer ground lookup

## Status and classification
- Classification: approximation.
- Plan-only local proposal: no runtime implementation, annotation retirement, publication or merge performed.
- Snapshot supplied as main 5ccddcab (export has no .git; commit not independently verified).
- Existing PR: none mapped for this independent residual.
- Draft eligibility: evidence/scope gates below must remain visible; this is not a completed feature.

## Current residual
TOUR-GROUND: the cars clear the height map (.hmp) of the mesh instance on a cell, its byte x 100/255 plus the instance height, as the cell lookup 0x00452AE0 reads it: the second-layer piece instance first (0x0053BF30: a kart or water track tile, its 2 x 2 map indexed from the instance corner two cells over by the engine rotation, 0x00522B90 / 0x00522D50 / 0x00466F90, on the tile height 0x00466D70; PieceGround) and else the object that owns the cell, found by the cell's footprint as tp.exe finds it by the owner word of a type-4, 9 or 10 cell and the class-3 thing there (0x527E80, 0x527D60), its map read at the object's rotation; every shipped object map has its shape's size, so the original's rebuild of a mis-sized map from the mesh (0x00451880) never runs for the catalogue; a cell with no such instance gives the terrain as the original does (every tour caller passes no owner out-pointer), but the park fence's hoarding posts (second-layer cells of type 0x19, instanced by 0x005229E0 from the table 0x00763388 with a 2 x 2 map and the cell offset once, laid by the track tool's mode 0x19, ECON-026) and the station tiles are not modelled, so those cells give the terrain or the object; the map of a track piece is never rebuilt here either, its size is not compared with the original's expected size — evidence needed: what the hoarding laying 0x00524960 / 0x00536100 writes (the piece, its rotation and the cells of a post) and whether a station tile carries a piece instance of its own

## Source inspection
- `source/OpenTPW/World/Objects/ObjectHeightMaps.cs:47`: 48: public float? Height( OriginalObject item, int cellX, int cellY ); 49: {; 50: if ( item.IsDeleted || item.Entry.IsFixedItem || MapOf( item.Entry ) is not { } map ); 51: return null;

## Bounded work
Add hoarding post/station instance height lookup only after owner coordination; track-size rebuild evidence separately.
- Preserve already-implemented behavior described above; restrict future changes to the named residual.
- For evidence-only items, establish an oracle first and file implementation only if a behavioral difference is proven.

## Missing evidence
Trace hoarding lay 0x524960/0x536100 and whether station tiles own mesh pieces.
- Source annotations record prior reverse engineering; this audit checked adjacent implementation but did not execute the original.

## Acceptance cases
1. Tour probe over rotated 2x2 hoarding map samples correct corner/rotation and base height.
2. Station, normal track, object and empty terrain probes use documented priority; malformed map fallback never silently changes valid maps.
3. Add a reproducible synthetic fixture or reference trace identifying input, expected output, and affected function; a passing build alone is insufficient.
4. Re-run focused regression tests for the listed source owner and document what remains unproven.

## Dependencies and ownership
- Claude currently owns particle/tour runtime. This document is planning only; agree file boundaries before runtime work.
- Original-executable or reference-capture evidence is unavailable in this read-only source audit; unresolved claims are hypotheses.
- Coordinate with Claude, reconcile merged #425 and #435 before any runtime work.

## Review boundary
- Stop at a reviewable design/evidence package; this draft authorizes no tag removal or parity claim.
- Do not publish original media or private game data in fixtures, screenshots, or commits.
- A future implementation needs its own reviewed evidence and acceptance results; do not treat this proposal as merge-ready.

## Current-main reconciliation (October 22, 2026)

The earlier residual above is a historical snapshot, not the current evidence contract.
Current main `ObjectHeightMaps.cs` and `docs/TOURS.md` correct the constructor family:

- Hoarding type 25 selects family 5, whose secondary instancer calls `0x00522F20`
  using table `0x00763608` (`ho_fos1`, `ho_end1`, `ho_fob1`).
  `0x005229E0` and `0x00763388` are the primary-cell family-2 queue path,
  not evidence for a hoarding instance. Do not implement from that old premise.
- The constructor computes `360 - rotation`, normalizing exactly 360 to zero,
  and applies a cardinal cell offset. Full downstream transform, map dimensions
  and ownership of laid cells are still unqualified; an offset-once rule is not proven.
- Station types 13/18 select family 1 through `0x00763138`. The traced secondary
  instancer `0x0053AFA0` constructs only families 3/4/5. This normal route provides
  no separate station mesh, but does not prove every historical handle is zero.

The existing track-first, object-second, terrain-last fallback remains unchanged.
Next implementation gate: qualify the hoarding laid-cell/head-cell relation,
instance transform and map dimensions, then add synthetic rotated probes using
those semantics. Keep malformed track-map rebuild as a separate unresolved case.
Do not add a hypothetical station map or rewrite tour heights from the old proposal.

This reconciliation inspects public source and recorded evidence only. It does
not execute the original game or claim new runtime acceptance, and TOUR-005 stays open.

