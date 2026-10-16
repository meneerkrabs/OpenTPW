## Backspace removes queue cells (October 16, 2026)

Backspace with no build tool on now also works over a queue cell: it plays the backtrack sound (`cat_ui` 95) and removes that
queue cell and every cell behind it, with the remove tool's refund (`Costs.QueueCell` × the ride's scrap percentage / 100).
This is an OpenTPW extension (`[EXT:queue-backtrack]`), asked for by the user: tp.exe's backtrack key only takes a type-1 (path)
cell (its key handler tests the tile through `0x536310` at `0x40BE3C`), and the original takes queue cells back with Backspace
only inside the queue tool (Back `0x52FE50`, the segment just laid), which OpenTPW already did. Over a path cell the key is
unchanged (one placement off, `ClearCell(0, 1)`). Docs: docs/PATHS.md, docs/AUDIO.md.
