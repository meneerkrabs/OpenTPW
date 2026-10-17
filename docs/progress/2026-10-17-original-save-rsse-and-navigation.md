## Original saves: the RSSE script block and the person navigation state are decoded, and two loaders' failures are enforced (October 17, 2026)

- **RSSE block** (writer `0x559350`, loader `0x5597A0`): `SaveRsseBlock` reads the 20-byte header, the five `PAD_` words, every 0xF4-byte script struct (named fields), the eight
  counted buffers (code, stack, variables, string blob, limbo, bounce, walk as an entry count, nodes), the script folder and the `'OBJ '` list of 0x1C-byte entries. The
  Jungle Easymode save is consumed to exactly 10,856 bytes with 14 records; every record's code words and string blob are byte-identical to the matching ride's `.RSE` member and its sizes
  equal that member's header; each record's thing is an attraction with the same mesh instance. The eleven corpus saves (four themes, `.TPWS` and `.INTS`) consume exactly too. The width
  walk in `SaveBlockWidths` now uses this reader.
- **Person navigation state** (reader `0x50D150`, 177 bytes): `SaveNavigation` decodes the 16.16 fixed-point fields in the reader's order; on every guest and staff member `pos` equals
  the thing position within 1/256 cell, the radius is 0.2 and the mass 1.0, with unused sub-path distances `0xCDCDCDCD`. `SavePerson` keeps the raw bytes and gains `NavigationState`.
- **Strictness**: a Scripts block without `'SCPT'` and the 0x118 record size, and a Particles block whose table sizes are not 0x9E68 / 0x8B60, now fail the split as they fail the
  original load ("Restore scripts FAILED"); before, the first was read as an 8-byte block and the second ended the block silently. The particle pool length is always 0 in saves the game
  writes (`0x41661B`).
