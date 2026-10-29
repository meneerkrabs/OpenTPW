## Saves import: queues and leaving guests (October 29, 2026)

- **A saved queue now comes back as the original left it (SAVE-002, QUEUE-003).** tp.exe's loader
  reads an attraction's head `mFirstInQ` (`+0x3C`) and called guest `mPersonBeingLoaded` (`+0x6C`)
  and each guest's `mQNext`/`mQPrev` (`+0x228`/`+0x22A`) as saved and relinks nothing
  (`0x4DB7D0`, `0x4FB530`). The import follows: guests in states 11 to 14, and in the idle
  interlude of state 11 (state 8 with `mSavedState` 11, `0x4FC800`), keep their place in the
  chain with the saved position and move-up delay; a guest moving up or called forward routes to
  its saved accurate destination as every loaded person does (`0x4F9407`), and a head in state 14
  waits for the script (`0x4E0A70`: the script's first variable against `mFirstInQ`). A chain
  that is not a list of guests of that ride in the queue states is refused with its reason.
- **Guests leaving a ride (state 15) are imported** and walk on to their saved destination.
- Still dropped, with their reasons reported: guests using an attraction (16) and held by a guard
  (17).
- **A saved Coasters block ends where its reader stops (SAVE-011, narrowed).** tp.exe's block
  loader (`0x415270`) runs each reader and compares the next four bytes with the tag (`0x415371`);
  the coasters' reader reads the type's train slots and cars per train (`0x437B60`) and nothing
  searches. The park import now passes the park's coaster types to the payload split
  (`SavePayloadLayout.Parse(payload, coasterTrains)`, `SaveCoasterBlock.TryMeasure`), so a saved
  coaster whose riders' ids hold the delimiter's bytes no longer depends on a delimiter search.
  Callers without the types and a World the loader's readers do not measure (a synthetic payload)
  keep the delimiter search.
- **A saved kart or water track comes back (TRACK-032, narrowed).** The loader `0x543560` rebuilds a
  ride from a chunk stream while the track itself sits in the World block's structure layer
  (31-byte track cells, `0x4D0B30` and `0x50C200`). The import now writes those cells (kart tiles 11
  and 13, water 16 and 18, their type-12 extensions; type 25 is a hoarding post), owns each tile by
  reach from its ride's station, walks the circuit from them, compares it with chunk 4's section
  list and applies chunk 3's laps, performance and phase. Vehicles, riders and the pending and
  leaving lists (chunks 5, 7, 8, 9) are reported, not restored. No save available to us holds a
  ride or a track cell (every TrackRides block is the 44-byte build-stamp chunk), so this is tested
  against the saver's shapes and OpenTPW's own tracks only.
- **SAVE-921 narrowed**: a scan of the coaster module finds no user of the controller's track flags
  0x20, 0x40, 0x80, 0x100, 0x200 and 0x400 besides the saver and the loader.
