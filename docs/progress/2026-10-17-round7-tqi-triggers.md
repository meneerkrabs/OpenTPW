## Movie triggers and presentation from tp.exe (October 17, 2026)

Format round 7, item 7b. The PC executable plays movies in exactly two places:

- **Triggers**: the movie start `0x51B010` has two callers, the logo (`0x54DF20`, `bf`) and the day trailer (`0x54E070`, `tm_mday & 7`
  through the jump table `0x54E288`: `bub`, `buc`, `grav`, `jug`, `mir`, `plan`, `roc`, `roll`), both from the start-up states of the
  main state machine `0x54E360`. There are no theme intro, ending or award movies. The start-up flag defaults on (`0xC0E15` at
  `0x449E1C`); only a launcher file `dialog.tcf` can replace it. `IntroPlaylist` already matched; its evidence tags now point at
  the PC addresses.
- **Movie volume**: the PC passes `percent × 1023 / 100` to the movie volume, which becomes the DirectSound buffer attenuation
  `v × 10000 / 1023 − 10000` in hundredths of a decibel (75 % is about −25 dB, 50 % about −50 dB). OpenTPW had mapped the percentage
  linearly (the Mac QuickTime scale); `MovieVolumeCurve` now applies the original curve.
- **Display packing**: the row writer packs each pixel pair to 16 bits from one table entry, so even columns round the channel value
  to the field and odd columns truncate it. The presenter now packs to 5-6-5 (assumed layout) and expands by bit replication; the PNG
  export keeps the unpacked 8-bit values. `MoviePresenter.OriginalBox` records the original's picture rectangle (screen width ×
  352/480 at y 64/480) and a test checks it against the letterboxed picture on every 4:3 mode.

Still open: whether the 12.9 s audio tail of `plan.tgq` plays out in the original, the layout of `dialog.tcf`, and the display's
channel layout (5-6-5 assumed). Docs: TGQ-MOVIES.md ("The PC build"), FORMAT-BACKLOG.md.
