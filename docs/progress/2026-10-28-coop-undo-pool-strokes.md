## Backspace takes back your own last pool stroke in a shared park (October 28, 2026)

In a shared park every player's Backspace in the pool tool's paint and erase modes now takes back that player's own last stroke, whole, under the normal refunds, and only while nobody else's later work depends on it ([COOP.md](../COOP.md), "Conflicts and undo (C5)"; `[EXT:online-coop-undo]`). This is C5-2, the second of three parts that reach the game together; C5-1 did the path, queue and slide tools, C5-3 adds the kart, water and coaster tools.

- **A whole stroke at a time.** Backspace takes back your newest stroke, however many batches a guest's drag sent it in: painted water goes and is refunded, erased water comes back and is paid for, river marks come off. Strokes go back newest first, a paint stroke and an erase stroke each their own.
- **All or nothing.** When another player has since painted or erased one of your cells, or someone stands where the water would come back, or the bank can pay only part of it, or a water attraction would lose the last water beside it, nothing changes and the stroke stays, with a note saying why.
- **Long drags in parts.** In a shared park a drag of more than 4,096 cells is laid as several strokes, so Backspace takes back its last part first; single player keeps its strokes whole.
- **Wave machines.** A guest with the attractions right can now set a wave machine's waves; the window shows the settings asked for until the host has applied them, and says so when the machine is gone meanwhile.
