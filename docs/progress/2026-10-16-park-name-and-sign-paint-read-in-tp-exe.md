## Park name and sign paint read in tp.exe (October 16, 2026)

- **Rename the park** (COMPAT-007 retired): the gate's two lines (+0x3C8, +0x40A) start as the first and second OBJECT_NAMES text of the
  gate's row (`0x4DB6F9`), so an English Lost Kingdom gate reads "LOST" / "Kingdom"; the ticket price window shows the joined name
  (`0x4E2780`) and opens the rename dialog (`0x4990F0`, UITEXT 194, two fields of 14 characters), whose OK sets the lines and redraws the
  sign (`0x4993E0`, `0x4682B0`). The world save gains an optional, validated `ParkName`; old saves load with the default lines.
- **Ride signs show the info record's name** (COMPAT-016 retired): an id outside the name table gives an empty name, and the sign is
  still composed without text (`0x4146B0`, `0x467A80`, `0x5ECB40`).
- **Sign paint masks are shaped as the PC build does** (COMPAT-003 narrowed, `0x5EBF10`): offset copy, erode or dilate by the mask word,
  then a truncating Gaussian blur along rows and columns (`0x5EA8A0`); two style-2 layers combine by the maximum of their shaped masks.
  The sign baseline hashes are re-pinned.
