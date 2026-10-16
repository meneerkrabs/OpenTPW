## Track tiles, the station line and coaster readings from tp.exe (October 16, 2026)

Read in tp.exe via REA:

- **Track tiles** (TRACK-003 retired): the instancers (`0x522B90`, `0x522D50`) and the mesh creation (`0x463060` -> `0x466F90`) turn a piece
  about its corner and offset it by two cells, which is the tile turning about its centre. The instance height is `0x466D70`'s midrange
  over the tile's 3 x 3 landscape vertices, and the terrain mode of the vehicle sampler reads the landscape triangle sampler `0x4527F0`
  at the probe instead of a cell mean (TRACK-001 narrowed).
- **The kart and water station line** (TRACK-005, TRACK-002 narrowed): the station is the line of `0x52A1A0` from the attraction cell,
  two tiles on the footprint's rows 1 and 2 across the heading; the exit and the piece walks `0x52A490` and `0x52A700` start where it says,
  and the controller reference (`0x529E10` -> `0x545890`) is the cell between the two tiles. TRACK-005 now covers only the line modes
  `0x80` and `0x83`; TRACK-002 only the other readers of the object height map.
- **Coaster readings** (text only): COAST-004 (candidate readers of the collision list), COAST-011 (the option table has no addressed
  entry), COAST-014 (the PC sampler rounds its sine indices), COAST-015 (the pose clamp write-back is what the built parameters already
  are), COAST-029 (the PC rating pass only sets the winch mark), COAST-032 (`DDSCL_FPUSETUP` without `FPUPRESERVE`) and TRACK-026
  (`0x44B410` is ADDHEAD's PC twin) are narrowed.
- **Design choice, approved by the user:** `[EXT:legacy-station-layout]`. A ride restored from a park saved before keeps its old station
  layout through the optional `StationLayout` on the controller save (same pattern as `[EXT:legacy-kart-save]`).

The pinned run hash is unchanged (`0x205523C08061A05D`, docs/DETERMINISM.md): the fixed run builds no track. Saves gain only the optional
`StationLayout` field; older saves load unchanged. Verified with the full test run with and without the game data and the native smokes.

Still open: the edit sub-modes `0xE` and `0x13` inside `0x524960` (TRACK-018), and the pylon-model posing that COAST-026, COAST-033 and
COAST-034 depend on.
