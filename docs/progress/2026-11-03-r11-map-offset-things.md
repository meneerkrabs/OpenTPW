## Objects with a map offset keep their thing and owner word (November 3, 2026)

Review finding on round 11 package I (#937), tp.exe read with REA: the placement passes the constructor `0x4DB090` the anchor plus the type's rotated `Info.MapOffsetX/Y` (`0x529F74..0x529FD5`), which the base `0x50AFE0` stores as the thing's bytes +5 and +7 (the thing cell), while `mTopLeft` (+0x34) is the anchor itself (`0x4DB2DA..0x4DB30A`) and the owner words on the cells get the thing cell (`0x528F13..0x528F26` -> `0x539210` at `0x529138`). Two shipped objects have an offset, (0, 1): `jungle/5x5rck` and `jungle/mammtunn`.

- **Import**: `OriginalParkImport.FindAttraction` (and the Theme Park Inc join) matched a saved attraction to its RideSystem record on the thing cell, so it found none for those two objects: they lost their thing id and economy state and `ObjectsInChainOrder` fell back to record order for the whole park. The match is now on `mTopLeft` against the record's anchor word `(y << 7) + x + 1` (`OriginalParkImport.AnchorWord`).
- **Owner word**: `Level.OwnerWord` returns the cell word of `OriginalObject.ThingCell` instead of the anchor, as tp.exe writes it, so the owner words OpenTPW writes and the map's owner lookup agree with an original save for those two objects.
- **Corpus**: none of the eleven original saves of `OPENTPW_TPWS_SAVES` ("All Researched Saves") holds a placed object (only fixed items), so no corpus save contains these two objects; the new tests are synthetic.
- **Guest registration**: the position cell handed to `RegisterWithGuests` (the cell the thing stands on, +5/+7 at `0x4DB33C`, which the near-the-route window of the guest decision compares) is the thing cell too.
- **Docs**: `TPWS-PAYLOAD.md` and `SaveThingRecords.cs` no longer say the thing cell equals the record cell.
- **Left**: the coaster sections are matched by cell (`OriginalParkImport` constructor); neither offset object is a coaster. 
