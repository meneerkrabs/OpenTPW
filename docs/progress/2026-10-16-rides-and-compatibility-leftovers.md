## Rides and compatibility leftovers: held clips, the waitanim clock and the TPI palette from tp.exe and Theme Park Inc (October 16, 2026)

A pass over the remaining ride and compatibility approximations, read in tp.exe and the no-CD Theme Park Inc executable via REA:

- **Held clips** (RIDES-003 retired): at a clip's end `0x4735D0` tests the instance flags (`0x47376E`): clear loops or ends as AnimID 14,
  flag 0x10 alone holds the last frame as AnimID 12 (`0x4737D7`), flag 0x8 stops. `0x473e30`, called from the creator `0x463060`, sets
  0x10 when an object has only AnimID 0 clips (88 of the 274 catalog entries), and only the advisor loader sets 0x8. Built as a hold route
  with an optional saved `Held` flag.
- **The waitanim clock** (RIDES-044 retired): WAITANIM_CH (`0x55320A`) adds the clock to max(ret − 300, 300) as an unsigned wrapping sum,
  and none of the 308 Theme Park World or 282 Theme Park Inc scripts issues it, so the clock origin is unobservable (pinned by a corpus test).
  The per-park clock origin stays labelled `[EXT:park-clock-origin]` at its code site; nothing original remains of the ID.
- **Theme Park Inc palette 0x24** (COMPAT-014 retired): the converter at TPI `0x6B7000` reads it as R, G, B with alpha 0xFF and no colour
  key (`0x6B7211`, `0x6B73F0`); only codes 0x24 and 0x2A are handled, so the one `0x2D` palette has no original reading: its A1R5G5B5 decode is OpenTPW's
  choice, `[EXT:fsh-0x2d-palette]` (COMPAT-015 closed).
- **Narrowed:** the End vehicle's GameType gate (`0x503d40`: Instant Action at once, online visits never, Full Simulation once every
  online park record is done, then advisor message 0xA7; RIDES-019, whose Theme Park Inc part, the Bus from setup, is now
  `[EXT:tpi-bus-from-setup]`), RIDES-032 (no call through `+0x140`), TOUR-008 (no reader of the
  MESHSHADOW bit), RIDES-040 (the zero-anchor scene records), COMPAT-006 (the shipped fonts carry only legacy `kern` tables), COMPAT-007
  (the gate lines and the secret name's flag, `0x4E2950`, `0x4053C0`), COMPAT-018 (the BILZ inflate step `0x6193D0`).
- **Closed as OpenTPW choices:** COMPAT-019, the Theme Park Inc viewer always draws the drained lake because it runs no missions
  (`[EXT:tpi-lake-drained]`).

The pinned run hash is unchanged; the saved animator state gains an optional `Held` flag (valid only with `Ended`). Verified with the full
suite (one generated-doc row fixed afterwards, with the affected tests rerun) and the native smokes.

Still open: the record list and text behind End's advisor message, Theme Park Inc's local-coordinate model placement,
the shadow batcher `0x581DB0`, whether classic Uniscribe applies `kern` tables, the listeners of event 0x15, the PC wavelet inverse
transform, and a native-resolution capture for COMPAT-004.
