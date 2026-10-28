## Backspace takes back your own last step in a shared park (October 27, 2026)

In a shared park every player's Backspace inside the path, queue and slide tools now takes back that player's own last step, under the normal refund rules, and only while nobody else's later work depends on it ([COOP.md](../COOP.md), "Conflicts and undo (C5)"; `[EXT:online-coop-undo]`). This is C5-1, the first of three parts that reach the game together; C5-2 adds the pool strokes and the wave machine, C5-3 the kart, water and coaster tools.

- **Your own, your last.** Backspace takes back the newest path or queue segment, or slide piece, your tool laid, one at a time, the host's and a guest's alike. A queue the tool started on, or another player's work, is not yours to take back, and says so. A line that joins an existing path ends the tool, as in the original, so it is removed with Backspace on its cells, as in single player.
- **Refused while others depend on it.** When another player's later line crosses or joins your segment, or something stands on it (a pool bridge, an object), Backspace changes nothing and says why; once that player takes their own work back, yours can go too.
- **The host decides.** The host keeps a journal of every player's steps and checks each take-back against it before it runs, so every computer takes back exactly the same cells, with the same refunds.
- **No new undo elsewhere.** A placed object still leaves by its sale, as before, and a single-player game's Backspace is unchanged.
