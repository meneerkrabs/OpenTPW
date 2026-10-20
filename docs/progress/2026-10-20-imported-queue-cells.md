## Imported parks keep their saved queue cells (October 20, 2026)

The Jungle Easymode import now restores the save's queue cells (map type 3) with their link byte, queue
link, placement counter, owner word and NoModify flag, as tp.exe's cell load (`0x4D0B30`, mode 0) reads every cell record
into the map. Since the typed queue front of QUEUE-003 (#540) the imported Belly Bounce's front was its
entrance-linked cell, which the import had left empty, so guests could not join and the native Jungle
smoke failed. The ride now has its saved four-cell queue (front `(52, 22)`, back `(49, 22)`, the saved
`mQueueSizeInCells` and `mBackOfQueue`), and guests join from the path attached to its back. The Jungle
smoke sends its visitors to such a ride from that join cell. New with-data tests
(`ImportedQueueCellsTests`) cover the restored cells and boarding without the native smoke.
