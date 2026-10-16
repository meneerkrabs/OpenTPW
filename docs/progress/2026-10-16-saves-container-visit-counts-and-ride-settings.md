## Saves: the World block layout, visit counts and ride settings from tp.exe (October 16, 2026)

Four save approximations narrowed, read in tp.exe via REA:

- **SAVE-006 narrowed**: the attraction loader reads the operating capacity into the byte `+0x5D` (`0x4DCC6C`), the duration into the byte
  `+0x5C` (`0x4DCC92`) and the speed into the dword `+0x58` (`0x4DCCB8`), raw, with no clamp. `Level.ImportOriginalRideSettings` takes
  the words that differ from the tier's values into the ride runtime (a word of 0 is left alone); the shipped Jungle save changes nothing.
  Still not imported: the saved open word, the sign lines and unclaimed staff claims.
- **SAVE-005 narrowed**: the challenge loss count (`+0x24`) and the offered word (`+0x20`) are read only by the save serializer and
  loader, so there is nothing to import. Only a saved slot list that does not match the level's challenges remains.
- **SAVE-002 narrowed**: shop, sideshow and win counts (`0x4FB530`) and the arrival stamp `mArrivalDate` (`+0x1D4`, `0x4FD950`) are
  imported, which gives an imported park guest its time in park. A finished visit is sorted by the type's `WhichUIType` (use routine
  `0x4FD9B1`): rides, shops and sideshows have their own counters, where OpenTPW counted every visit as a ride. GUEST-005's wording drops
  the `+0x1D0 += 1` of a won sideshow, since that counter is now modelled. Still open: guests in states 11 to 17, the arrival index,
  admission flags, the event ring and queue links.
- **SAVE-011 narrowed**: the World loader `0x5179C0` is traced (64 variable bytes, the control manager, the macro AI, 0x4000 cells and
  the thing list; each cell is a status byte plus map, track and effects records chosen by status bits 0 to 2). The Jungle Easymode
  World measures to its exact delimiter offset without searching for `WRLD`. A Coasters block with a coaster and synthetic payloads
  still fall back to the delimiter search.

Behaviour change: shop and sideshow visits no longer raise a guest's ride count. The park resume snapshot gains the optional
`ShopsVisited`, `SideshowsVisited` and `SideshowsWon` guest fields; older saves load them as 0.

The pinned run hash moves (docs/DETERMINISM.md): `0xDB3EA9A7AF384B5B` to `0x0487112FF9FF20DB`, from the ride/shop/sideshow split and the
three hashed counters.
