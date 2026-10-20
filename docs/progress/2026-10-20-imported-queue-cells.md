## Imported parks keep their saved queue cells (October 20, 2026)

The Jungle Easymode import now restores the save's queue cells (map type 3) with their link byte, queue
link, placement counter, owner word and NoModify flag, as tp.exe's cell load (`0x4D0B30`, mode 0) reads every cell record
into the map. Since the typed queue front of QUEUE-003 (#540) the imported Belly Bounce's front was its
entrance-linked cell, which the import had left empty, so guests could not join and the native Jungle
smoke failed. The ride now has its saved four-cell queue (front `(52, 22)`, back `(49, 22)`, the saved
`mQueueSizeInCells` and `mBackOfQueue`), and guests join from the path attached to its back. The Jungle
smoke sends its visitors to such a ride from that join cell. New with-data tests
(`ImportedQueueCellsTests`) cover the restored cells and boarding without the native smoke.

A park saved by OpenTPW before this import gets those queue cells back at load from the level's shipped save
(`[EXT:legacy-save-queue-cells]`, user-approved 2026-10-20), only where its cells are still empty ground and the
ride's entrance links into them; newer saves are untouched. Saves older than the access-link fix of
October 17 (an entrance with no link byte) are not repaired: they cannot be told apart from a removed queue.
