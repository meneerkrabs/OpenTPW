## The original object build check: overwrite priority and track cells (October 16, 2026)

The object branches of tp.exe's cell validator `0x535670` were traced via REA. The build tool's hover `0x5234D0` and click
`0x524960` run the placement `0x528A70` (the hover twice: a check pass with its sixth argument 1, then the ghost; the click
commits), which sends each footprint cell through cell op `0x204` of `0x532FC0` into the validator with type 4.

- **Overwrite priority.** The branch that compares `0x818C14` with `0x818C10` (`0x535D66`) is not a count: the hover loads the placed
  type's `Info.OverwritePriority` (type record +0x54, sam schema `0x745020`) into `0x818C10`, and the validator loads the priority of
  the object that owns the cell (`0x53570E`) into `0x818C14`. An object cell is allowed when the new priority is higher; the commit's
  cell op 0x87 (`0x5344E5`) then demolishes the owner whole (`0x527EE0`, which credits its scrap value). In the shipped catalogues
  rides, shops and sideshows have priority 5, small scenery (bushes, flowers, tiny rocks, speakers) 1 and the other features 0, so a
  ride built over a bush removes the bush. `ObjectCatalogEntry.OverwritePriority`, `ParkObjects.Check(…, out overwritten)` and
  `TryPlace` follow this.
- **Track cells.** The validator refuses a track piece on the second layer (`0x535BB3`) and any map type other than empty, path and
  the object types. OpenTPW stores track types in the cell map and did not refuse them; `Level`'s object reservation now refuses
  every cell type except 0 and the object types (4, 9, 10), which `ParkObjects` decides by priority.
- **Money.** The pending cost (`0x52FBC0`, the type's price `+0x1B8`, set by the hover) above the balance refuses the build unless
  `0x785A2C` is set; OpenTPW's economy purchase already refuses it.

The rest of the object rules (objects on plain path cells, the access cells, paths and queues over scenery) followed in the same
pull request (2026-10-16-objects-over-paths-and-access-cells.md).
