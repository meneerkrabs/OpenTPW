## RideSystem records walked by their frame (October 17, 2026)

The original TPWS save's RideSystem block (scene-mesh instances, delimiter
`RSYS`, stored tag `SYSR`) is now decoded by its frame instead of a signature
scan. The layout comes from the PC loader (`0x4647a0`) and writer (`0x464140`)
in tp.exe, confirmed on real saves. Read via REA:

- **Frame** (python probes + save walk over 11 corpus saves): `u32 L` (the
  bytes after the length word), `u32 used`, `u32 free`, `u32 firstFree`;
  then one slot per `used + free` in slot order. An absent slot is a single
  `0` byte (the stored `firstFree` is the first absent ordinal, `free == 0`
  implies `firstFree == used`); a present slot is byte `1` followed by a
  0x36-byte header: InfoId `+0`, X `+4`, Y `+8`, W `+0xC`, H `+0x10`, kind
  `+0x14`, index `+0x18`, packed flags `+0x1C` (bits 0–8 map to `0x20`,
  `0x40`, `0x80`, `0x100`, `0x200`, `0x400`, `0x800`, `0x40000`,
  `0x8000000`), raw u16 `+0x20`, raw u32 `+0x22`, rotation `+0x26`, then
  `0x2E`/`0x32` two embedded words kept raw and separate from the M pairs.
- **Variable part of each record**: u16 N, u16 M `+0x2A`/`+0x2C`; then M × 8
  embedded pairs, then N × 4 packed node-state words (bits 0–3 and 5–11 map to `0x10`, `0x20`, `0x8000`, `0x10000`,
  `0x40000`, `0x80000`, `0x200000`, `0x400000`, `0x20000000`, `0x80000000`,
  `0x100000`; bit 4 and bits 12+ are refused), then T × 0x2C controls
  (11 u32 words in save order `[0] [1] [2] [4] [5] [6] [3] [9] [10] [11]
  [12]`, kept raw).
- **T is not stored, and the walk no longer guesses it.** The loader takes
  the control count from the live instance's `+0xE` (`0x464b98`), which
  `0x4557c0` copies from the kind table's type entry; that entry is built by
  `0x4629d0`, which passes its seventh argument to `0x461f10` as the count
  (entry `+0xE`) and replaces 0 with 1 (`T >= 1`). The object-archive loader
  (`0x414175`, `0x4141c7`) passes the definition's `+0x170`, the `.sam` key
  `UsageInfo.NumSimultAnims`; other registrars pass literals (`0x41437e`,
  `0x430130`: 1; `0x42fcbf`: 5). `SaveRideSystemBlock.Parse` therefore takes an
  optional `max(1, NumSimultAnims)` table by Info.Id; a record the table
  resolves has exactly that T, every other record may have any T of at least 1 that fits (no cap, so a
  cap cannot hide a second parse; the work/state budgets fail closed),
  and the iterative, memoized search (explicit state/step limits, path rebuilt
  from predecessor edges) counts **all** complete parses: exactly one is
  accepted, none or several are refused (the earlier "smallest feasible T"
  picked a wrong T/T+2 frame and called it unique, and recursed without bound).
  The search and the decoder share one record predicate, node-state bit 4 is
  refused by bit mask, counts are summed in 64 bits and bounded, and an empty
  block is exactly its 16 bytes with first free 0 (the loader itself would
  tolerate more). Scene records (815/865) are validated before routing (Info.Id
  and index 1-65,535, cell in the grid, size 1-16, rotation 0/90/180/270, on the
  raw unsigned words; checked footprint arithmetic). Easymode: L=17,522,
  used=161, free=7, firstFree=90 (absent slots 90-96), kinds 815 x 11 / 826 x
  147 / 865 x 3, T=1 everywhere except slot 114 (InfoId 1303 Jungle Spray,
  index 4, N=31, M=12, T=3 = its `NumSimultAnims`); instance flags only {0, 8}.
  All 11 corpus saves have exactly one complete parse without a table, and
  every record whose object archive is known (5 per park save, 2 per INTS
  save) has the table's T; the others (Info.Ids 17302-17326, in no object
  archive) hold T=1 in the unique parse.
- **Retired**: the `SaveObjectList` signature class ("SYSG records",
  mislabelled); TPWI's `SYSG` scan stays signature-based on its separate
  widths. The old "826 records nested inside Belly Bounce" reading is
  corrected in TPWS-PAYLOAD.md: the 826s are separate slots (N=1..4 in
  Easymode, up to 11 in the corpus, M=0). **Narrowed**: approximations RIDES
  items that rested on the record parse (placed/fixed routing now uses
  verified footprints; 815 → placed, 865 → fixed, 826 and others skipped).
- **Saves**: the park save schema keeps the block as read (raw words where
  meanings are unknown); no writer yet, the original save import stays
  read-only.

`OriginalPark.ImportOriginalSave` passes the theme catalogue's counts on every
production import (lazily, on the first record), and the corpus test runs it
over all 11 saves.

Still open: the T of the types no object archive declares (the Info.Ids
173xx) is the unique parse's value, not read from their registrar; the meaning of the raw control words (words 3–5
hold run-time pointer-like values in Easymode), the two embedded header words
`+0x2E`/`+0x32`, what node-state bit 4 would mean (never stored), and what the
M pairs mean.
