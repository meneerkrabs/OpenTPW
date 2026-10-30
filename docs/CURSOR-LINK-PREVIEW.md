# Completed-preview link cursor

The path and queue tools now supply the link pointer from their existing successful preview validators. A path preview records a valid queue-end conversion; a queue preview records its existing final-cell join to a path. These are the code-8 branches already documented in ParkPathBuilder and QueueLineWriter.

The HUD uses this indication only for a completed current path/queue ghost. It does not infer a link from the broader `EndedOnExisting` field. Preview replaces the indication; commit, cancel and tool changes clear the ghost. Preview does not write cells or spend money. Existing UI, ride/camcorder, placement and cash pointer precedence is unchanged.

The end pointer follows the validator's code 0xB (flag 0x816D4C, `0x535AE5` and `0x535B3D`): a completed path preview whose last cell is an existing path cell (a one-cell line included, so the hover over a path shows it), and a completed queue preview whose last cell is the ride's own queue cell. A refused line (the ghost's latch 0x816D48, `0x527950`) takes the no-place pointer, as the pointer switch `0x52F950` does after the tool's own, link and end pointers; the cash pointer still wins last. A queue that stopped only at OpenTPW's own length bound is no refusal.

UI-100 remains open for the end pointer of the kart and water track lines (`0x53A1C6` in the track chooser `0x539760`), the cash flag `0x539FB6` sets for a chooser call that does not refuse, and the crosshair art, which is not in the game data.

Asset-free tests cover both actual preview producers, ordinary cells, queue-end validation, object-overwrite normalization, refusal before/after a link, real tool reset paths and pointer priority. C# tests require exact-head CI; source review alone is not execution or rendered visual proof.
