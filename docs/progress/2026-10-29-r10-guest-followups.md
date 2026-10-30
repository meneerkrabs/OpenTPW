## Guest follow-ups: kid sets and held guests (October 29, 2026)

- **Kid sets load as tp.exe loads them.** The kid and kid-head folders stop at the
  limit of `0x41A9D0` (2, 4, 6 or 8 sets for the preset's `NUMKIDS` 0 to 3, so Low
  loads 2 and Medium and High load 6) and load `SPR_BI`, `SPR_KI`, `SPR_TA` and
  `SPR_SU` first, the others by name after them (table `0x764030`, passes in
  `0x541310`). A guest's set is its creation draw (`0x541F70`, the first draw after
  the reseed with its thing id, shifted right by 2) modulo the number loaded, so the
  set a guest wears is its place in that order, not in the alphabetical one. The
  function returns the draw modulo the count and the guest stores that remainder
  (`0x4FB1B5`), which a load reduces again (`0x4F93AA`), so the guest keeps its draw
  reduced by the sets loaded (`Guest.KidSetDraw`, saved as an optional field, not
  hashed: the simulation never reads it and the constructor already took the draw,
  so the random stream and the pins are unchanged); a guest of an original save keeps
  the bank its person record holds, and a costume return redraws it. The pools extension's kid, head and swim sets take the
  same places. GUEST-017 loses its kid-set clause.
- **Guests held by a guard are imported.** An original save's guest in state 17 is
  imported held (no walk, balloon, ride or bin, as the hold `0x4FEB10` leaves it),
  and its escorting guard (state 18, perpetrator `+0x212`) is imported escorting,
  walking on to its saved destination and setting the guest down at the booth. A
  held guest no guard holds is destroyed at its first update (`0x501C94`). No corpus
  save holds such a guest, so this is checked on synthetic saves. SAVE-002 narrows
  to the riders (state 16).
- **Review round 1.** An imported escorting guard keeps his saved walk mask (nav_mode,
  `0x4D6977`), so an escort on the off-path route to the booth still finds it; only one
  guard escorts a guest and an escort of a guest the guest import did not keep held
  goes idle. A park without costume heads (Fantasy, Hallow) picks every rider's head by
  the thing id (`0x4FCAC0`), a costume return rewrites the kid set (`0x4FE6AA`), and a
  Custom preset reads the resolved `NUMKIDS`. In such a theme every
  head hung on a ride also reseeds the world generator with the rider and draws once
  (`0x44B410` → `0x4FCAC0`), now taken at ADDHEAD, WALKON, the coaster seating, the
  vehicle creation and load and the tour boarding; the Fantasy and Hallow pins move.
  Every guard naming a restored guest escorts it (`0x4D6A50` tests no state).
